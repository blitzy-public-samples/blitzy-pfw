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
//  A THIRD SOURCE CONTRIBUTES TO THIS CONTRACT, AND ITS USES ARE MEASURED THE SAME WAY.
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru is ported by
//  Services/DropDownSearchModel.cs, which is an ATTACHED SERVICE and therefore reaches its host and
//  its child only through this file. Its additions, each with the call sites that justify it:
//
//      MEMBER                      USES  WHERE (all n_cst_dwsvc_dropdownsearch.sru)
//      GetColumnName                  1  :L138 - "" is a live answer, tested at :L139
//      OnDDSGetFilter                 1  :L342 - `#DataWindow.Event`, ref string out-parameter
//      OnDDSFiltered                  1  :L408 - `#DataWindow.Event`, pre-move counts
//
//      datawindowchild additions, receiver `_editCtx.dddw.object` in every case:
//      SetRedraw                      2  :L387 :L410      SetFilter                      1  :L389
//      Filter                         1  :L390            Sort                           1  :L391
//      SetSort                        1  :L220            FilteredCount                  2  :L394 :L402
//      RowsMoveFilterToPrimary        1  :L402 - narrowed to the one form used; see its remarks
//
//  MEASURED AND DELIBERATELY NOT ADDED, so the omissions are auditable rather than accidental:
//  `Handle(...)` [:L171, :L214, :L469], `IsWindowVisible` [:L255, :L385], `SetDetailHeight`
//  [:L399, :L404], `SetText` and `SelectText` [:L128-L129], `UnitsToPixels` [:L480] and the four
//  `Win32.*` calls [:L256, :L474, :L475, :L489]. All are the presentational half of the drop-down
//  search split (AAP 0.2.1.3 Correction 4) and belong to the reserved `/v1/design/**` extension
//  point (AAP 0.4.4); putting any of them on this contract would breach constraint C-D by giving a
//  deferred capability a place to live.
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

using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Containers;
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
    /// The object's KIND, as the legacy raw token: <c>"column"</c>, <c>"compute"</c>, <c>"text"</c>,
    /// <c>"datawindow"</c>, and so on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADDED ON MEASURED EVIDENCE, AND DISTINCT FROM <see cref="ColType"/> - the two are easy to
    /// confuse and mean different things. <see cref="ColType"/> is the DATABASE type of a column
    /// (<c>"char(50)"</c>, <c>"decimal(2)"</c>); this is the DataWindow OBJECT type. Both exist on the
    /// legacy <c>dwobject</c> and both are read in the in-scope set.
    /// </para>
    /// <para>
    /// TWO CALL SITES, BOTH IN THE CONTEXT-MENU SERVICE'S DEFAULT ITEM SET:
    /// <c>n_cst_dwsvc_contextmenu.sru:L242</c> reads
    /// <c>row &gt; 0 and (dwo.Type = "column" or dwo.Type = "compute")</c> and <c>:L245</c> reads
    /// <c>elseif dwo.Type = "compute"</c>. Those two tests select between the two item-copy early
    /// returns, so the token decides whether the menu has one item or many.
    /// </para>
    /// <para>
    /// NOT SUBSTITUTED BY <c>Describe(dwo.Name + ".Type")</c>, EVEN THOUGH THE SAME FILE USES THAT FORM
    /// ELSEWHERE [<c>:L258</c>]. The two mechanisms agree for a NAMED object and disagree for the
    /// DataWindow itself, whose property expression is not describable, so collapsing them would change
    /// behaviour in exactly the case a right-click on the DataWindow background produces. The oracle
    /// uses both forms and both are carried.
    /// </para>
    /// <para>
    /// Non-nullable, because <c>:L242</c> compares it with no null guard. An implementation with no
    /// object kind to report returns the empty string, which matches neither arm and falls through -
    /// the same outcome the legacy reaches for any unrecognised kind.
    /// </para>
    /// </remarks>
    string Type { get; }

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

    // ------------------------------------------------------------------------------------------
    //  THE FILTER, SORT AND BUFFER MEMBERS THE DROP-DOWN SEARCH SERVICE DRIVES ON ITS CHILD
    //  ----------------------------------------------------------------------------------------
    //  ADDED FOR Services/DropDownSearchModel.cs, AND EVERY ONE IS MEASURED ON THE CHILD RATHER
    //  THAN ON THE HOST, exactly as the typed getters above are. Measured on
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru, where the
    //  receiver is `_editCtx.dddw.object` - the datawindowchild handle - in all nine cases:
    //      SetRedraw       :L387 (false) and :L410 (true), the pair around a filter application
    //      SetFilter       :L389
    //      Filter          :L390
    //      Sort            :L391 and, indirectly, :L220's SetSort
    //      SetSort         :L220, seeding the display-column sort when the child has none
    //      FilteredCount   :L394 (captured for the event) and :L402 (re-read for the move)
    //      RowsMove        :L402
    //  There is not one `#DataWindow.SetFilter`, `#DataWindow.FilteredCount` or
    //  `#DataWindow.RowsMove` in that source, so declaring them on the host as well would invent a
    //  surface the oracle does not have (constraint C-B).
    //
    //  WHY NOT REUSE THE HOST'S Filter()/Sort()/SetSort()/SetRedraw(). Those exist for the CHAIN -
    //  the host's Filter() is overridden by se_cst_dw and notifies the row-select service
    //  [se_cst_dw.sru:L406-L410]. The child is a different DataWindow entirely, with its own buffers
    //  and its own filter, and routing its calls through the host's members would apply the search
    //  filter to the parent DataWindow. The duplication of NAME here is the oracle's own and is not
    //  a duplication of BEHAVIOUR.
    //
    //  THE RETURN TYPES ARE THE POWERSCRIPT ONES, NOT NORMALISED. PowerBuilder's SetFilter, Filter,
    //  Sort, SetSort and SetRedraw all answer `integer` with 1 for success, and RowsMove answers
    //  `integer` as well; the one call site that tests a result is :L402, which tests `= 1`
    //  explicitly. Mapping any of them onto bool would erase the distinction between the -1 failure
    //  and the null-argument outcome the runtime also has.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies a filter to the child, BINDING its literals rather than executing interpolated text - the
    /// port of <c>dwc.SetFilter(sFilter)</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L389</c>).
    /// </summary>
    /// <param name="filter">
    /// The filter in both forms. <see cref="Services.BoundFilterExpression.ParameterizedText"/> plus
    /// <see cref="Services.BoundFilterExpression.Parameters"/> is what an implementation must ALWAYS
    /// execute; <see cref="Services.BoundFilterExpression.ObservableText"/> is for reporting and recording
    /// and is never executed. <see cref="Services.BoundFilterExpression.Bindable"/> reports whether the
    /// expression is CERTIFIED free of unbound caller values - it is NOT a switch selecting which form to
    /// run, because an uncertified expression's parameterized text simply is its text.
    /// </param>
    /// <returns><c>1</c> on success, <c>-1</c> on failure.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS CONTRACT CARRIES AN EXPRESSION AND NOT A STRING, AND WHY THERE IS NO STRING OVERLOAD
    /// BESIDE IT.</b> The legacy composes its search filter by concatenating the user's typed text into
    /// expression syntax with no escaping at all [<c>:L319</c>, <c>:L323</c>, <c>:L331</c>, <c>:L334</c>],
    /// so a term carrying a quote, a parenthesis or the word <c>OR</c> changes the STRUCTURE of what runs -
    /// CWE-94. A string-taking member cannot distinguish a value from syntax, so an implementer offered one
    /// has no way to be safe; taking the expression makes the safe path the only path. A convenience
    /// overload would silently become the one everybody called.
    /// </para>
    /// <para>
    /// <b>AND AN IMPLEMENTATION THAT CANNOT BIND MUST SAY SO RATHER THAN SILENTLY INTERPOLATE.</b> There is
    /// deliberately NO default interface implementation delegating to the rendered text: that default would
    /// reinstate exactly the hole this member closes, in every implementer that had not thought about it,
    /// invisibly. An implementer with no binding facility should execute the rendered text and DOCUMENT
    /// that it does - a decision taken in the open.
    /// </para>
    /// <para>
    /// <b>THE SAME TRAP IN MINIATURE: DO NOT BRANCH ON <c>Bindable</c> TO FALL BACK TO THE RENDERED TEXT.</b>
    /// A conjunction of an application-supplied original filter and a user's bound search reports
    /// uncertified - the original half's provenance is unknown - while still carrying the user's value as a
    /// parameter. Falling back there would re-interpolate the one value that had been bound, on the one path
    /// (the re-apply after an original-filter change) that a test of the first application never reaches.
    /// </para>
    /// <para>
    /// <c>integer</c> RETURN, NOT <c>bool</c>: PowerBuilder's <c>SetFilter</c> answers <c>1</c> for success
    /// and <c>-1</c> for failure, and mapping it onto a boolean would erase the null-argument outcome the
    /// runtime also has.
    /// </para>
    /// <para>
    /// SETTING AND APPLYING ARE TWO CALLS IN POWERBUILDER AND STAY TWO CALLS HERE. <c>:L389</c> sets and
    /// <c>:L390</c> applies; merging them would change the observable sequence a characterization recording
    /// captures, and would also make the no-op case - a filter set to the value it already had -
    /// indistinguishable from a re-filter.
    /// </para>
    /// </remarks>
    int SetFilter(Services.BoundFilterExpression filter);

    /// <summary>
    /// Suspends or resumes the child's repainting - the port of <c>dwc.SetRedraw(enable)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L387</c>, <c>:L410</c>).
    /// </summary>
    /// <param name="enable"><see langword="false"/> to suspend, <see langword="true"/> to resume.</param>
    /// <returns><c>1</c> on success, <c>-1</c> on failure.</returns>
    /// <remarks>
    /// A HEADLESS SERVICE STILL CALLS THIS, AND THAT IS DELIBERATE. Repaint suppression is a
    /// data-model operation in the sense that matters here - it is a property of the DataWindow
    /// object, not a window handle operation - so it stays on this contract. What does NOT cross the
    /// boundary is the legacy's PRECONDITION for calling it: <c>if IsWindowVisible(hWnd)</c>
    /// [<c>:L385</c>] is a <c>user32.dll</c> prototype [<c>:L41-L44</c>] that AAP 0.6.5 places OUT OF
    /// SCOPE as presentational. Services/DropDownSearchModel.cs records that narrowing at the call
    /// site.
    /// </remarks>
    int SetRedraw(bool enable);

    /// <summary>
    /// Applies the filter expression, moving non-matching rows to the filter buffer - the port of
    /// <c>dwc.Filter()</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L390</c>).
    /// </summary>
    /// <returns><c>1</c> on success, <c>-1</c> on failure.</returns>
    int Filter();

    /// <summary>
    /// Applies the current sort - the port of <c>dwc.Sort()</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L391</c>).
    /// </summary>
    /// <returns><c>1</c> on success, <c>-1</c> on failure.</returns>
    /// <remarks>
    /// Called UNCONDITIONALLY after every filter application at <c>:L391</c>, with no guard and no
    /// test of the result. Filtering re-orders the primary buffer, so the oracle re-sorts to restore
    /// the order the child was given at <c>:L220</c>.
    /// </remarks>
    int Sort();

    /// <summary>
    /// Sets the child's sort expression WITHOUT applying it - the port of
    /// <c>dwc.SetSort(sort)</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L220</c>).
    /// </summary>
    /// <param name="sort">
    /// The sort expression, for example the display column name followed by <c>" ASC"</c>, which is
    /// the one form the oracle composes.
    /// </param>
    /// <returns><c>1</c> on success, <c>-1</c> on failure.</returns>
    /// <remarks>
    /// Reached only when the child reports no sort of its own -
    /// <c>if dwc.Describe("DataWindow.Table.Sort") = "?"</c> [<c>:L219</c>] - so a child that already
    /// carries a sort keeps it. The oracle discards the result.
    /// </remarks>
    int SetSort(string sort);

    /// <summary>
    /// The number of rows the filter moved out of the primary buffer - the port of
    /// <c>dwc.FilteredCount()</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L394</c>, <c>:L402</c>).
    /// </summary>
    /// <returns>The filtered row count.</returns>
    /// <remarks>
    /// READ TWICE BY THE ORACLE FOR TWO DIFFERENT PURPOSES, AND THE TWO READS MUST STAY SEPARATE.
    /// <c>:L394</c> captures it BEFORE any buffer move, because that captured value is what the
    /// <c>onddsfiltered</c> event carries [<c>:L408</c>] - the oracle stresses the point in comments
    /// at <c>:L392</c> and <c>:L407</c>. <c>:L402</c> re-reads it live as the row count to move.
    /// </remarks>
    long FilteredCount();

    /// <summary>
    /// Moves rows out of the child's FILTER buffer and into its PRIMARY buffer - the port of the one
    /// <c>RowsMove</c> form the oracle uses,
    /// <c>dwc.RowsMove(startRow, endRow, Filter!, dwc, beforeRow, Primary!)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L402</c>).
    /// </summary>
    /// <param name="startRow">
    /// The first row to move, in the FILTER buffer's own one-based numbering. The oracle always
    /// passes <c>1</c>.
    /// </param>
    /// <param name="endRow">
    /// The last row to move. The oracle passes a live <see cref="FilteredCount"/>, so the whole
    /// filter buffer moves.
    /// </param>
    /// <param name="beforeRow">
    /// The one-based primary-buffer row to insert before. The oracle passes the pre-move row count
    /// plus one, which appends.
    /// </param>
    /// <returns>
    /// <c>1</c> on success, <c>-1</c> on failure. THE ORACLE TESTS <c>= 1</c> [<c>:L402</c>] and
    /// performs its follow-up only then, so the value must not be normalised to a bool.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NARROWED TO THE ONE FORM THE ORACLE USES, DELIBERATELY. The general PowerScript member takes
    /// a source buffer, a target DataWindow and a target buffer; at the single call site the source
    /// is always <c>Filter!</c>, the target is always THIS SAME CHILD and the target buffer is always
    /// <c>Primary!</c>. Carrying three parameters no caller can vary would widen the contract past
    /// what AAP 0.4.2.5's consumption criterion admits - the same judgement this file already applies
    /// to <see cref="DataWindowServiceHost.GetObjectAttribute"/>'s dropped second argument - and it
    /// would additionally drag the generated <c>common.v1.DwBuffer</c> enum into
    /// Services/DropDownSearchModel.cs, whose contract audit under constraint C-A requires ZERO
    /// references to the published contracts assembly. The buffers are therefore named in the member
    /// name and documented here rather than passed.
    /// </para>
    /// <para>
    /// WHY A HEADLESS SERVICE PERFORMS THIS MOVE AT ALL. Relocating rows between buffers is a
    /// data-model operation and its result - which rows now sit where - is data. What is deferred is
    /// the CONCEALMENT that surrounds it: the two <c>SetDetailHeight</c> calls at <c>:L399</c> and
    /// <c>:L404</c> are row geometry, which AAP 0.4.4 assigns to the reserved
    /// <c>/v1/design/**</c> extension point. Services/DropDownSearchModel.cs performs the move and
    /// publishes the row ranges those two calls would have consumed.
    /// </para>
    /// </remarks>
    int RowsMoveFilterToPrimary(long startRow, long endRow, long beforeRow);
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
    /// <c>DataWindowServiceBase.GetDataWindowObject(string)</c> can reproduce
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
    /// The name of the column that currently has the edit cursor - the port of
    /// <c>GetColumnName()</c>
    /// (<c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L138</c>).
    /// </summary>
    /// <returns>
    /// The current column's name, or THE EMPTY STRING when no column is current. Both outcomes are
    /// live: the one call site tests <c>if sColName = "" then return</c> [<c>:L139</c>] and abandons
    /// the refresh, so the empty string is an ordinary answer rather than an error and must NOT be
    /// mapped onto null or onto an exception.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ADDED FOR Services/DropDownSearchModel.cs. It is a DATA-MODEL read - which column the cursor
    /// sits in - and not a window or input-method operation, which is why it belongs on this contract
    /// rather than on the deferred <c>/v1/design/**</c> side of the drop-down search split
    /// (AAP 0.2.1.3 Correction 4, AAP 0.4.4).
    /// </para>
    /// <para>
    /// PAIRED WITH <see cref="GetRow"/> AND IN THAT ORDER. The oracle reads the row FIRST and
    /// abandons on zero [<c>:L136-L137</c>], then reads the column and abandons on empty
    /// [<c>:L138-L139</c>], and only then resolves the name to an object through
    /// <see cref="DataWindowServiceBase.GetDataWindowObject(in string)"/>. Reversing the two reads
    /// would resolve a column object for a DataWindow with no current row.
    /// </para>
    /// <para>
    /// NAME-VALUED RATHER THAN OBJECT-VALUED, DELIBERATELY. PowerScript's own member answers a
    /// string, and the ported call site passes that string through the name-taking accessor. Having
    /// this member answer an <see cref="IDataWindowObject"/> instead would fuse two legacy calls into
    /// one and lose the empty-string branch, which is the only branch that distinguishes "no current
    /// column" from "a column that does not resolve".
    /// </para>
    /// <para>
    /// TWO SERVICES CONSUME IT, AND ONE DECLARATION SERVES BOTH. The context-menu service reads it
    /// immediately after <see cref="GetRow"/> to establish that a click landed on the cell being
    /// edited - <c>if #DataWindow.GetRow() &lt;&gt; row then return true</c>
    /// [<c>n_cst_dwsvc_contextmenu.sru:L320</c>] followed by
    /// <c>if #DataWindow.GetColumnName() &lt;&gt; sName then return true</c> [<c>:L321</c>] - and the
    /// drop-down search service reads it for the focus refresh at [<c>:L138-L139</c>]. Both consume
    /// the SAME legacy member, so declaring it twice on this contract would be two names for one
    /// oracle call rather than two capabilities; the empty-string answer is live for both, allowing
    /// the popup in the first case and abandoning the refresh in the second.
    /// </para>
    /// </remarks>
    public abstract string GetColumnName();

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

    // ==========================================================================================
    //  SORT AND ROW IDENTITY - THE DATA MODEL'S ORDERING, NOT AN INDICATOR
    //  ----------------------------------------------------------------------------------------
    //  ADDED AS A MEASURED EXTENSION OF THE CONSUMED SURFACE, ON THE SAME CRITERION AS THE ROW
    //  SELECTION REGION BELOW. The census in the file header was taken over se_cst_dw.sru and
    //  n_cst_dwsvc.sru only, and neither sorts. A fourth in-scope source does:
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru's apply path calls
    //  `#DataWindow.GetRowIDFromRow` at :L408, `#DataWindow.SetSort` at :L414,
    //  `#DataWindow.Sort` at :L415, `#DataWindow.GroupCalc` at :L418 and
    //  `#DataWindow.GetRowFromRowID` at :L422. AAP 0.4.2.5's criterion is consumption, and these
    //  five are consumed - each exactly once, on one code path, in that order.
    //
    //  WHY THEY BELONG HERE AND NOT BEHIND THE DEFERRED /v1/design/** ROUTE. AAP 0.2.1.3
    //  Correction 4 splits n_cst_dwsvc_columnsort.sru into a headless half that ships and a
    //  rendering half that is deferred, and the split line is drawn at the DPI conversions in
    //  `_of_setarrow` [:L355-L364]. These five fall on the SHIPPING side without ambiguity:
    //  ordering is a property of the row set, and the row identifier round trip exists precisely so
    //  that the CURRENT ROW survives a reorder [:L408 captured, :L421-L423 restored]. Not one
    //  carries geometry, colour, font, DPI or a window handle, so constraint C-D is not engaged.
    //
    //  `Modify` IS DELIBERATELY ABSENT, AND ITS ABSENCE IS THE SPLIT WORKING AS INTENDED.
    //  n_cst_dwsvc_columnsort.sru calls it three times and ALL THREE are on the deferred side: the
    //  two `Modify("Destroy " + ...)` calls at :L351-L352 and the `Modify(sSyntax)` at :L393 that
    //  emits a `create text(...)` band object. The headless half emits no DataWindow syntax at all,
    //  so the consumption criterion is not met and adding the member would create an unused
    //  extension point - the same reasoning that keeps the column-NUMBER helper overloads out of
    //  DataWindowServiceBase below.
    // ==========================================================================================

    /// <summary>
    /// Captures the STABLE identifier of a row - the port of <c>GetRowIDFromRow(row)</c> as used at
    /// <c>n_cst_dwsvc_columnsort.sru:L408</c>.
    /// </summary>
    /// <param name="row">The one-based row number to identify.</param>
    /// <returns>
    /// The row identifier, or a value that is not positive when <paramref name="row"/> does not
    /// identify a row.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE FIRST HALF OF A ROUND TRIP, AND THE ROUND TRIP IS THE WHOLE POINT. A row NUMBER
    /// is positional and a re-sort invalidates it; a row IDENTIFIER survives reordering. The sort
    /// path captures the identifier BEFORE reordering [<c>:L408</c>] and converts it back to a
    /// number afterwards [<c>:L422</c>], which is what makes the caret stay on the same DATA row
    /// rather than the same POSITION.
    /// </para>
    /// <para>
    /// THE GUARD AT THE CALL SITE IS <c>&gt; 0</c> AND NOT <c>&lt;&gt; 0</c> [<c>:L421</c>], so a
    /// negative answer must be reported rather than normalised to zero. PowerBuilder answers a
    /// non-positive value for an out-of-range row, and the call site relies on exactly that.
    /// </para>
    /// </remarks>
    public abstract long GetRowIDFromRow(long row);

    /// <summary>
    /// Resolves a stable row identifier back to a row NUMBER - the port of
    /// <c>GetRowFromRowID(rowId)</c> as used at <c>n_cst_dwsvc_columnsort.sru:L422</c>.
    /// </summary>
    /// <param name="rowId">The identifier previously obtained from <see cref="GetRowIDFromRow"/>.</param>
    /// <returns>
    /// The one-based row number the identifier now occupies, or a value that is not positive when
    /// the identifier no longer corresponds to a row.
    /// </returns>
    /// <remarks>
    /// The second half of the round trip described on <see cref="GetRowIDFromRow"/>. The call site
    /// passes the result STRAIGHT into <see cref="SetRow"/> without checking it [<c>:L422</c>], so
    /// this must not raise for an identifier that has gone away - it answers a non-positive number
    /// and <see cref="SetRow"/> deals with it, exactly as in the oracle.
    /// </remarks>
    public abstract long GetRowFromRowID(long rowId);

    /// <summary>
    /// Sets the sort expression WITHOUT applying it - the port of <c>SetSort(sort)</c> as used at
    /// <c>n_cst_dwsvc_columnsort.sru:L414</c>.
    /// </summary>
    /// <param name="sort">
    /// The sort expression, in the DataWindow's own comma-separated
    /// <c>clause&#160;A</c>/<c>clause&#160;D</c> grammar. THE EMPTY STRING IS A LEGAL AND MEANINGFUL
    /// VALUE: it clears the sort, and the sort service passes it whenever the user has cleared every
    /// column AND there was no original sort to fall back to [<c>:L201-L207</c>].
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// SETTING AND APPLYING ARE TWO CALLS, NOT ONE, AND THEY MUST STAY TWO. <c>:L414</c> is
    /// <c>SetSort(sort)</c> and <c>:L415</c> is <c>Sort()</c>. Collapsing them into a single
    /// "sort by this expression" member would remove the state in between, which is observable: a
    /// caller can set the expression and let a later <see cref="Sort"/> - or a retrieve - apply it.
    /// The oracle discards both return codes; they are surfaced here so a test can assert the calls
    /// happened rather than inferring them from row order.
    /// </remarks>
    public abstract int SetSort(string sort);

    /// <summary>
    /// Applies the current sort expression, reordering the rows - the port of <c>Sort()</c> as used
    /// at <c>n_cst_dwsvc_columnsort.sru:L415</c>.
    /// </summary>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Takes no argument, exactly as the oracle's call does: the expression comes from the preceding
    /// <see cref="SetSort"/>. See that member for why the pair is not collapsed.
    /// </remarks>
    public abstract int Sort();

    /// <summary>
    /// Recomputes group breaks and group aggregates - the port of <c>GroupCalc()</c> as used at
    /// <c>n_cst_dwsvc_columnsort.sru:L418</c>.
    /// </summary>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// <para>
    /// CALLED ONLY WHEN THE DATAWINDOW HAS GROUPS [<c>:L417</c>], and reaching it at all is an
    /// interesting corner: the button-up entry point refuses to sort a grouped DataWindow outright
    /// [<c>:L63</c>], so this is reached only when the sort was applied through
    /// <c>of_reset</c>/<c>of_update</c> rather than through a header click. Both routes are real, so
    /// the guard and this call are both preserved.
    /// </para>
    /// <para>
    /// GROUP AGGREGATES ARE DATA, NOT PRESENTATION. A group break decides which rows a group
    /// aggregate covers, and reordering the rows moves the breaks - so omitting this would leave
    /// every group total stale after a sort, which is a data defect rather than a rendering one.
    /// </para>
    /// </remarks>
    public abstract int GroupCalc();

    // ==========================================================================================
    //  THE EVENT GATE - THE HOST'S SUPPRESSED-EVENT MASK       se_cst_dw.sru:L109-L111, L469-L525
    //  ----------------------------------------------------------------------------------------
    //  THE MASK BELONGS TO THE HOST, NOT TO A SERVICE, WHICH IS WHY THESE THREE ARE HERE. The
    //  legacy declares `long _nDisabledEvent` as a PRIVATE INSTANCE FIELD OF se_cst_dw [:L88-L89]
    //  and publishes exactly three members over it [:L109-L111]. Every attached service reaches it
    //  through the host - n_cst_dwsvc_columnsort.sru:L409-L411 and :L424-L426 do precisely that -
    //  so a service that kept its own copy would gate only its own events and would silently stop
    //  suppressing the host's. Reproducing the mask inside a service is therefore not a style
    //  choice, it is a behaviour change.
    //
    //  THE OPERATIONS ARE NOT REDEFINED HERE. Domain/EventGate.cs already ports the three bodies
    //  verbatim - BitTest, BitOR with a zero-argument rejection, and BitClear with the same
    //  rejection - and an implementation of this contract is expected to delegate to it so the bit
    //  arithmetic exists in exactly one place. These three members are the ACCESS PATH the oracle
    //  publishes; EventGate is the ALGEBRA. Domain/ValidationSession.cs holds the mask VALUE for a
    //  server-held session and exposes the identical trio over it, which is the shape a real host
    //  composes from.
    //
    //  THE RETURN-TYPE ASYMMETRY IS A SIGNATURE CONTRACT, NOT AN OVERSIGHT.
    //  `of_disableevent` returns `long` [:L110] and `of_enableevent` returns `integer` [:L111], for
    //  the same argument list and the same two possible values. It is reproduced exactly - here, in
    //  EventGate and in ValidationSession - so a caller that stores either result keeps the width
    //  the oracle gave it. Harmonising the pair would be a silent widening or narrowing at every
    //  call site.
    // ==========================================================================================

    /// <summary>
    /// Whether a raw DataWindow event is currently suppressed - the port of
    /// <c>of_iseventdisabled(evt)</c> (<c>se_cst_dw.sru:L109</c>, body <c>:L471</c>).
    /// </summary>
    /// <param name="evt">
    /// One of <c>EventGate.EID_ROWFOCUSCHANGE</c>, <c>EventGate.EID_ITEMFOCUSCHANGE</c> or
    /// <c>EventGate.EID_ITEMCHANGE</c>, or a bitwise combination of them.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the event is suppressed.
    /// </returns>
    /// <remarks>
    /// A COMBINATION ANSWERS TRUE WHEN ANY BIT MATCHES, because the body is a bit TEST rather than
    /// an equality. That is the oracle's semantics and the ported <c>EventGate.IsEventDisabled</c>
    /// reproduces it; the distinction matters for a caller passing two bits at once, which reads as
    /// "is either suppressed" and never as "are both suppressed".
    /// </remarks>
    public abstract bool IsEventDisabled(uint evt);

    /// <summary>
    /// Suppresses a raw DataWindow event - the port of <c>of_disableevent(evt)</c>
    /// (<c>se_cst_dw.sru:L110</c>, body <c>:L491-L493</c>).
    /// </summary>
    /// <param name="evt">The event bit, or a bitwise combination of bits, to suppress.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="evt"/> is
    /// <c>0</c> - a zero mask names no event and is rejected rather than treated as a no-op.
    /// </returns>
    /// <remarks>
    /// RETURNS <see langword="long"/> WHILE <see cref="EnableEvent"/> RETURNS <see langword="int"/>.
    /// See the region banner above: the asymmetry is the oracle's and is deliberately preserved.
    /// </remarks>
    public abstract long DisableEvent(uint evt);

    /// <summary>
    /// Stops suppressing a raw DataWindow event - the port of <c>of_enableevent(evt)</c>
    /// (<c>se_cst_dw.sru:L111</c>, body <c>:L515-L517</c>).
    /// </summary>
    /// <param name="evt">The event bit, or a bitwise combination of bits, to stop suppressing.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="evt"/> is
    /// <c>0</c>.
    /// </returns>
    /// <remarks>
    /// RETURNS <see langword="int"/> WHILE <see cref="DisableEvent"/> RETURNS <see langword="long"/>,
    /// which means the two success values are <c>RetCode.OK</c> widened differently. Both are the
    /// same numeric <c>0</c>; only the declared width differs, and it differs because the oracle
    /// declares it so.
    /// </remarks>
    public abstract int EnableEvent(uint evt);

    // ==========================================================================================
    //  ROW SELECTION - ROW STATE, NOT RENDERING
    //  ----------------------------------------------------------------------------------------
    //  ADDED AS A MEASURED EXTENSION OF THE CONSUMED SURFACE, NOT AS A GUESS. The census in the
    //  file header was taken over se_cst_dw.sru and n_cst_dwsvc.sru only, and neither touches row
    //  selection at all. The third in-scope source does:
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru calls
    //  `#DataWindow.SelectRow` at :L51 :L53 :L63 :L66 :L71 :L78 :L80 :L90 :L93 :L103 :L107 :L174
    //  :L176 :L278 and :L282, `#DataWindow.IsSelected` at :L80 and :L193, and
    //  `#DataWindow.GetSelectedRow` at :L224. AAP 0.4.2.5's criterion is consumption, and these
    //  three are consumed.
    //
    //  WHY THEY BELONG HERE AND NOT BEHIND THE DEFERRED /v1/design/** ROUTE. Selection is a
    //  ROW-STATE FLAG that the data model carries, exactly like an item status: it decides which
    //  rows a subsequent operation applies to, and n_cst_dwsvc_rowselect.sru:L224 iterates it to
    //  PROPAGATE A DATA CHANGE across the selection. Highlighting a selected row is a rendering
    //  consequence of the flag, not the flag itself. No member below carries geometry, colour,
    //  font, DPI or a window handle, so constraint C-D is not engaged - which is also why AAP
    //  0.2.1.3 Correction 4 measured RowSelect as having exactly ONE presentational reference, and
    //  that one is a dialog rather than anything here.
    // ==========================================================================================

    /// <summary>
    /// Selects or deselects a row - the port of <c>SelectRow(row, select)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L51</c> and fourteen further sites).
    /// </summary>
    /// <param name="row">
    /// The one-based row number, or <c>0</c> TO MEAN EVERY ROW. The zero form is load-bearing rather
    /// than incidental: <c>SelectRow(0,false)</c> is the legacy's clear-the-whole-selection idiom and
    /// appears at <c>:L51</c>, <c>:L63</c>, <c>:L90</c>, <c>:L103</c>, <c>:L174</c> and <c>:L282</c>.
    /// An implementation that treated <c>0</c> as out of range would leave every one of those six
    /// sites doing nothing, and nothing would report it.
    /// </param>
    /// <param name="select"><see langword="true"/> to select, <see langword="false"/> to deselect.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Returns the raw legacy integer and is NOT mapped onto <c>RetCode</c>, for the same reason
    /// <see cref="SetRow(long)"/> is not: success here is <c>1</c> while <c>RetCode.OK</c> is
    /// <c>0</c>. Every ported call site DISCARDS this value, exactly as the oracle does, so the code
    /// exists on the contract for fidelity rather than for branching.
    /// </remarks>
    public abstract int SelectRow(long row, bool select);

    /// <summary>
    /// Whether a row is currently selected - the port of <c>IsSelected(row)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L80</c>, <c>:L193</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <returns><see langword="true"/> when the row is selected.</returns>
    /// <remarks>
    /// Both uses are decisions rather than reports. <c>:L80</c> TOGGLES with
    /// <c>SelectRow(row, Not IsSelected(row))</c>, so the answer must reflect writes made through
    /// <see cref="SelectRow(long, bool)"/> in the same instant; and <c>:L193</c> is one of the eight
    /// guards that decide whether a range check-box propagation runs at all.
    /// </remarks>
    public abstract bool IsSelected(long row);

    /// <summary>
    /// Finds the next selected row at or after a starting point - the port of
    /// <c>GetSelectedRow(startRow)</c> (<c>n_cst_dwsvc_rowselect.sru:L224</c>).
    /// </summary>
    /// <param name="startRow">
    /// The row to search AFTER, one-based, or <c>0</c> to start from the beginning. The legacy
    /// iterates by feeding its own previous answer back in, starting from an uninitialised
    /// <c>long</c> - which in PowerScript is <c>0</c>.
    /// </param>
    /// <returns>
    /// The one-based number of the next selected row, or <c>0</c> when there is none. THE ZERO
    /// TERMINATES THE LOOP: <c>:L225</c> is <c>if nRow &lt;= 0 then exit</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// SEARCHES STRICTLY AFTER <paramref name="startRow"/>, NOT AT IT. This is the whole reason the
    /// legacy loop terminates: it assigns the answer back into the same variable it passes in
    /// [<c>:L224</c>], so an implementation that could return <paramref name="startRow"/> itself
    /// would spin on the first selected row for ever. The behaviour is asserted rather than assumed
    /// because it is invisible at the call site.
    /// </para>
    /// <para>
    /// Row numbers cross this member in the legacy's one-based numbering and are never rebased. AAP
    /// 0.4.5.4 names one-based to zero-based translation the single most dangerous mechanical hazard
    /// in this refactor, and this member - a loop cursor that is both an input and an output - is the
    /// one most likely to be rebased by accident.
    /// </para>
    /// </remarks>
    public abstract long GetSelectedRow(long startRow);

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

    // ==========================================================================================
    //  NAME-KEYED ITEM ACCESS - THE THIRD SOURCE'S OWN CALLING CONVENTION
    //  ----------------------------------------------------------------------------------------
    //  A SECOND MEASURED EXTENSION, AND IT CORRECTS A STATEMENT IN THE HEADER. DECISION 2 records
    //  that "there is not one `#DataWindow.GetItem*` call anywhere in either source", and that is
    //  still true OF THE TWO SOURCES IT WAS MEASURED OVER. The third in-scope source contradicts
    //  it: ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru reads
    //  `#DataWindow.GetItemDecimal` at :L205 and :L232, `#DataWindow.GetItemNumber` at :L211 and
    //  :L234, and `#DataWindow.GetItemString` at :L217 and :L236, and writes
    //  `#DataWindow.SetItem` at :L248, :L250 and :L252. All nine are ON THE HOST, not on a
    //  datawindowchild, and all nine address the column BY NAME rather than by id.
    //
    //  WHY BY NAME AND NOT BY ID. The name is what the oracle has in hand: :L188 takes
    //  `sColName = dwo.Name` once and then threads that string through every read and write. Making
    //  these members id-keyed would force a name-to-id resolution the legacy never performs, at nine
    //  sites, each of which could fail differently - so the contract takes the string the oracle
    //  takes. The id-keyed SetItem overloads above are NOT duplicates of these: they serve
    //  se_cst_dw's coercion table, which genuinely holds `Long(dwo.ID)` [se_cst_dw.sru:L233-L243].
    //  Both calling conventions exist in the legacy and both are therefore carried.
    //
    //  ALL SIX TYPES ARE CONSUMED, AND THE COUNT RESTS ON MEASURED EVIDENCE FROM MORE THAN ONE SOURCE.
    //  Measuring only n_cst_dwsvc_rowselect.sru supports the narrower reading "only the three types
    //  actually consumed", because its type switch [:L203-L222, :L230-L237, :L246-L253] has
    //  exactly three arms - COL_TYPE_DECIMAL, COL_TYPE_INTEGER and a default that reads text. A
    //  FOURTH in-scope source contradicts that narrower reading: n_cst_dwsvc_contextmenu.sru's
    //  paste-into-column protocol switches on ALL SEVEN COL_TYPE_* values and reads
    //  `#DataWindow.GetItemDateTime` [:L1036], `GetItemDate` [:L1042] and `GetItemTime` [:L1048]
    //  BY NAME, then writes `SetItem(nRow,colName,DateTime(sVal))` [:L1067],
    //  `SetItem(nRow,colName,Date(sVal))` [:L1069] and `SetItem(nRow,colName,Time(sVal))` [:L1071]
    //  BY NAME. So the three temporal members below are consumed surface, not fabricated surface,
    //  and the by-name family spans the same six types IDataWindowChild declares - which is the only
    //  coherent shape, since a column read is a column read whether the buffer is the DataWindow's
    //  or its child's.
    // ==========================================================================================

    /// <summary>
    /// Reads a <c>string</c>-typed item by column NAME - the port of
    /// <c>#DataWindow.GetItemString(row, colName)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L217</c>, <c>:L236</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name, as taken from <see cref="IDataWindowObject.Name"/>.</param>
    /// <returns>The item text, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// NULLABLE BECAUSE THE COMPARISONS THAT CONSUME IT MUST BE ABLE TO SEE A NULL. Both uses are
    /// equality tests against a check-box on or off string, and a null item is not equal to either -
    /// so at <c>:L217</c> a null selects the on value and at <c>:L236</c> it does not skip the row.
    /// Substituting the empty string for a null would make a null item indistinguishable from an
    /// empty one, and AAP 0.4.5.4 forbids collapsing null in either direction.
    /// </remarks>
    public abstract string? GetItemString(long row, string column);

    /// <summary>
    /// Reads a <c>decimal</c>-typed item by column NAME - the port of
    /// <c>#DataWindow.GetItemDecimal(row, colName)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L205</c>, <c>:L232</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// Reached only on the <c>COL_TYPE_DECIMAL</c> arm. Both call sites immediately wrap the result
    /// in <c>String(...)</c> and compare the TEXT, never the number, so the ported call sites format
    /// it explicitly rather than relying on any implicit conversion - the formatting is part of the
    /// observable behaviour and belongs at the call site where the oracle puts it.
    /// </remarks>
    public abstract decimal? GetItemDecimal(long row, string column);

    /// <summary>
    /// Reads a numeric item by column NAME - the port of
    /// <c>#DataWindow.GetItemNumber(row, colName)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L211</c>, <c>:L234</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// <para>
    /// RETURNS <see cref="double"/> AND NOT AN INTEGER, EVEN THOUGH THE ARM THAT REACHES IT IS
    /// NAMED <c>COL_TYPE_INTEGER</c>. PowerBuilder's <c>GetItemNumber</c> yields a <c>double</c>
    /// whatever the column's declared width, and the mismatch between the arm's name and the
    /// function's type is the legacy's own. It matters observably: the value is stringified and
    /// compared as text at <c>:L211</c> and <c>:L234</c>, so a widening or narrowing here changes
    /// which comparisons match. This mirrors
    /// <see cref="IDataWindowChild.GetItemNumber(long, string)"/>, which carries the same note.
    /// </para>
    /// <para>
    /// The corresponding WRITE is <see cref="SetItem(long, string, long?)"/>, because <c>:L250</c>
    /// writes <c>Long(sVal)</c> - the legacy reads wide and writes narrow on the same arm, and both
    /// halves are reproduced as they are.
    /// </para>
    /// </remarks>
    public abstract double? GetItemNumber(long row, string column);

    /// <summary>
    /// Writes a <c>string</c> value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, sColName, sVal)</c> (<c>n_cst_dwsvc_rowselect.sru:L252</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to write a null item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// The DEFAULT arm of the three-arm write switch, so it serves every column type that is neither
    /// decimal nor integer - which is why it is reached for a check box over a character column, the
    /// ordinary case in the primary fixture.
    /// </remarks>
    public abstract int SetItem(long row, string column, string? value);

    /// <summary>
    /// Writes a <c>decimal</c> value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, sColName, Dec(sVal))</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L248</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">
    /// The value to write, or <see langword="null"/> when the source text did not parse as a number -
    /// which is what PowerScript's <c>Dec</c> yields for unparseable text, and is therefore a real
    /// outcome rather than a defensive case.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, string column, decimal? value);

    /// <summary>
    /// Writes an integer value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, sColName, Long(sVal))</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L250</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">
    /// The value to write, or <see langword="null"/> when the source text did not parse - the
    /// behaviour of PowerScript's <c>Long</c> on unparseable text.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, string column, long? value);

    /// <summary>
    /// Reads a <c>datetime</c>-typed item by column NAME - the port of
    /// <c>#DataWindow.GetItemDateTime(nRow, colName)</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1036</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// Reached only on the <c>COL_TYPE_DATETIME</c> arm of the paste protocol, where the comparison is
    /// <c>GetItemDateTime(nRow,colName) = DateTime(sVal)</c> - a comparison of MOMENTS and not of text,
    /// unlike the decimal and number arms which stringify first. Nullable because a null item must not
    /// compare equal to a parsed moment, and because AAP 0.4.5.4 forbids collapsing null onto a
    /// default - substituting <c>DateTime.MinValue</c> here would make a null item indistinguishable
    /// from the empty <c>datetime</c> the oracle keeps in its own <c>dttEmpty</c> local
    /// [<c>:L945</c>], which is a DIFFERENT value with a different meaning.
    /// </remarks>
    public abstract DateTime? GetItemDateTime(long row, string column);

    /// <summary>
    /// Reads a <c>date</c>-typed item by column NAME - the port of
    /// <c>#DataWindow.GetItemDate(nRow, colName)</c> (<c>n_cst_dwsvc_contextmenu.sru:L1042</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// <see cref="DateOnly"/> rather than <see cref="DateTime"/>, per AAP 0.4.5.2's mapping of the
    /// legacy <c>date</c>. Keeping the two distinct is what preserves the oracle's own distinction
    /// between the <c>COL_TYPE_DATE</c> arm [<c>:L1037-L1042</c>], which validates with
    /// <c>IsDate</c>, and the <c>COL_TYPE_DATETIME</c> arm [<c>:L1031-L1036</c>], which compares
    /// against an empty <c>datetime</c> instead.
    /// </remarks>
    public abstract DateOnly? GetItemDate(long row, string column);

    /// <summary>
    /// Reads a <c>time</c>-typed item by column NAME - the port of
    /// <c>#DataWindow.GetItemTime(nRow, colName)</c> (<c>n_cst_dwsvc_contextmenu.sru:L1048</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// <see cref="TimeOnly"/> per AAP 0.4.5.2's mapping of the legacy <c>time</c>.
    /// </remarks>
    public abstract TimeOnly? GetItemTime(long row, string column);

    /// <summary>
    /// Writes a <c>datetime</c> value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, colName, DateTime(sVal))</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1067</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">
    /// The value to write, or <see langword="null"/> when the source text did not parse - which for
    /// PowerScript's <c>DateTime</c> is the EMPTY datetime rather than an error, and is passed through
    /// as a null here rather than coerced.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, string column, DateTime? value);

    /// <summary>
    /// Writes a <c>date</c> value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, colName, Date(sVal))</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1069</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">The value to write, or <see langword="null"/> when the text did not parse.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, string column, DateOnly? value);

    /// <summary>
    /// Writes a <c>time</c> value into one item by column NAME - the port of
    /// <c>#DataWindow.SetItem(nRow, colName, Time(sVal))</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1071</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">The value to write, or <see langword="null"/> when the text did not parse.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, string column, TimeOnly? value);

    // ==========================================================================================
    //  THREE FURTHER DATA-MODEL OPERATIONS, EACH ADDED ON A MEASURED CALL SITE
    //  ----------------------------------------------------------------------------------------
    //  Find and InsertRow are all consumed by
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru and by nothing else in
    //  the in-scope set. Each is a DATA-MODEL operation - it reads or writes the buffer, or reports
    //  which column the edit control is on - so each belongs here rather than on any service, and
    //  none of them is presentational: no window, no pointer, no device coordinate is involved.
    //  Constraint C-D is therefore not engaged by any of the three.
    // ==========================================================================================

    /// <summary>
    /// Searches the primary buffer for the first row in a range that satisfies a DataWindow boolean
    /// expression - the port of <c>#DataWindow.Find(sFind, start, end)</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1191</c>, <c>:L1222</c>, <c>:L1357</c>, <c>:L1388</c>).
    /// </summary>
    /// <param name="expression">
    /// The boolean DataWindow expression to match. Built by
    /// <c>Formatting.Sprintf</c> from one of three format strings at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1183</c>, <c>:L1186</c> and <c>:L1188</c>, and passed through
    /// VERBATIM: this member neither parses nor rewrites it.
    /// </param>
    /// <param name="start">The one-based row to start from, inclusive.</param>
    /// <param name="end">The one-based row to stop at, inclusive.</param>
    /// <returns>
    /// The one-based row number of the first match, <c>0</c> when there is no match in the range, or a
    /// NEGATIVE value on error. All three outcomes are load-bearing: the ported scan loops
    /// <c>do while(nRow &gt; 0)</c> [<c>:L1192</c>, <c>:L1358</c>], so <c>0</c> terminates it and a
    /// negative value terminates it identically - which is exactly what the oracle does with an
    /// unusable expression, silently measuring nothing rather than faulting.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS A DISTINCT-VALUE SCAN, NOT A SEARCH FOR A KNOWN ROW. Its callers use it to skip rows
    /// whose display value equals row 1's, so that only rows that could widen a column are examined.
    /// Losing it would not merely change complexity - it would change WHICH rows are measured, and
    /// therefore the computed width.
    /// </para>
    /// <para>
    /// THE RANGE IS INCLUSIVE AT BOTH ENDS AND ONE-BASED AT BOTH ENDS. The oracle passes
    /// <c>Find(sFind, 1, nRowCnt)</c> on the first pass and <c>Find(sFind, nRow + 1, nRowCnt)</c> on
    /// each subsequent pass, then guards <c>if nRow = nRowCnt then exit</c> BEFORE searching again
    /// [<c>:L1221</c>, <c>:L1387</c>] - so a match on the final row never leads to a call with
    /// <c>start &gt; end</c>. An implementation must still answer <c>0</c> for an empty range rather
    /// than throwing, because that guard is the oracle's and a future edit could remove it.
    /// </para>
    /// </remarks>
    public abstract long Find(string expression, long start, long end);

    /// <summary>
    /// Inserts a row into the primary buffer - the port of <c>#DataWindow.InsertRow(0)</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L999</c>).
    /// </summary>
    /// <param name="row">
    /// The one-based row to insert BEFORE, or <c>0</c> to append after the last row. The only ported
    /// call site passes <c>0</c>; the parameter is carried because PowerBuilder's own signature
    /// carries it and narrowing it to a no-argument append would silently forbid an insert the legacy
    /// permits.
    /// </param>
    /// <returns>
    /// The one-based number of the inserted row, or a value <c>&lt;= 0</c> on failure. The
    /// distinction is observable: <c>if #DataWindow.InsertRow(0) &lt;= 0 then return RetCode.FAILED</c>
    /// [<c>:L999</c>] abandons the whole paste, mid-way, leaving the rows already written in place.
    /// </returns>
    /// <remarks>
    /// REACHED ONLY WHEN THE PASTED TEXT HAS MORE LINES THAN THE BUFFER HAS ROWS [<c>:L994</c>], so
    /// pasting is allowed to GROW the buffer. That is a data-model mutation and stays on this
    /// contract; nothing about it is presentational.
    /// </remarks>
    public abstract long InsertRow(long row);

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
    /// <param name="data">
    /// The entered text, not yet written to the buffer. NULLABLE for the same reason
    /// <see cref="OnDoItemChange(long, IDataWindowObject, string?)"/> is - that event's one-line body
    /// forwards straight to this one [<c>se_cst_dw.sru:L292</c>], so a null arriving there arrives
    /// here unchanged.
    /// </param>
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
    public virtual long ItemChanged(long row, IDataWindowObject dwo, string? data)
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

    // ==========================================================================================
    //  THE SIX se_cst_dw SEMANTIC EVENTS THAT AN ATTACHED SERVICE RAISES THROUGH #DataWindow
    //  ----------------------------------------------------------------------------------------
    //  These are declared BY se_cst_dw itself [se_cst_dw.sru:L24 `event type long ondoitemchange
    //  (long row, dwobject dwo, string data)` and :L26 `event ondoitemchanged (long row, dwobject
    //  dwo)`], so the file header's ownership statement correctly assigns the 22-event chain to
    //  Domain/DataWindowEventChain.cs. EXACTLY SIX OF THE NINE ESCAPE THAT BOUNDARY, and they do so
    //  because an ATTACHED SERVICE raises them on its host rather than the chain raising them on
    //  itself:
    //      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L238
    //          `#DataWindow.Event OnDoItemChange(nRow,dwo,sVal)`
    //      n_cst_dwsvc_rowselect.sru:L254
    //          `#DataWindow.Event OnDoItemChanged(nRow,dwo)`
    //      n_cst_dwsvc_contextmenu.sru:L147
    //          `if #DataWindow.Event OnInitContextMenu(row,dwo) = 1 then return`
    //      n_cst_dwsvc_contextmenu.sru:L194
    //          `if #DataWindow.Event OnContextMenu(row,dwo,rtCode) = 1 then return`
    //      n_cst_dwsvc_dropdownsearch.sru:L342
    //          `#DataWindow.Event OnDDSGetFilter(_editCtx.row,_editCtx.dwo,data,ref sFilter)`
    //      n_cst_dwsvc_dropdownsearch.sru:L408
    //          `#DataWindow.Event OnDDSFiltered(row,dwo,nRowCnt,nFilteredCnt)`
    //  A service holds its host through the base's DataWindow property, which is typed as THIS
    //  contract, so all six must be declared here or the ported call sites cannot exist at all.
    //
    //  CORRECTION OF RECORD, KEPT VISIBLE RATHER THAN QUIETLY EDITED AWAY (constraint C-K). This
    //  block has twice understated the count, and both understatements were MEASUREMENT ERRORS
    //  rather than judgement calls. It first read "EXACTLY TWO OF THE NINE", naming only the two
    //  row-select raises; the two context-menu events were then found to be raised by the
    //  context-menu service at :L147 and :L194 - the chain's own doc-comment already said as much
    //  while declaring them, a contradiction resolved in favour of the oracle. They remain visible
    //  on the chain, now as `override`, so no consumer of the chain loses anything and there is
    //  still exactly ONE definition of each legacy event. The same measurement then found
    //  onddsgetfilter and onddsfiltered among the events said to be "raised by the CHAIN on
    //  itself": they are not - both raises above are `#DataWindow.Event ...` inside the drop-down
    //  search service, so the chain only DECLARES them. The count is SIX, and the two DDS members
    //  were added when Services/DropDownSearchModel.cs was ported and that error surfaced.
    //
    //  THE OTHER THREE ARE DELIBERATELY ABSENT. oncolumnexpinvokemethod, onitemchanged and
    //  oncolumnexptrace [:L14, :L25, :L32] are raised by the CHAIN on itself, never by a service on
    //  its host, so none of them is consumed through this contract and adding them would fabricate
    //  surface (C-B). `ondwnchanging` is likewise absent: it is a raw pbm_dwn* handler on the
    //  chain [:L21].
    //
    //  SIGNATURE-COMPATIBLE WITH Domain/ItemChangeProtocol.cs's IItemChangeEventSink ON PURPOSE.
    //  That internal interface declares OnDoItemChange and OnDoItemChanged with exactly these
    //  parameter lists and return types, so Domain/DataWindowEventChain.cs can satisfy it with
    //  these INHERITED members and there is no second definition to keep in step. A derived chain
    //  that supplies its own body must use `override`; declaring a new member of the same name
    //  would report CS0108 and, with warnings promoted to errors repository wide, fail the build -
    //  which is the desired outcome, because two definitions of one legacy event is the defect
    //  worth failing over.
    //
    //  WHY VIRTUAL WITH A NO-OP DEFAULT RATHER THAN ABSTRACT. A PowerBuilder event with no script
    //  attached yields its type's initial value and performs nothing, which is exactly this. Making
    //  them abstract would force every host - including every test double - to supply a body for an
    //  event the legacy allows to be unhandled.
    // ==========================================================================================

    /// <summary>
    /// Raised to ask whether one item's value may change - the port of
    /// <c>Event OnDoItemChange(row, dwo, data)</c> (declared <c>se_cst_dw.sru:L24</c>; raised by an
    /// attached service at <c>n_cst_dwsvc_rowselect.sru:L238</c>).
    /// </summary>
    /// <param name="row">The one-based row whose item is changing.</param>
    /// <param name="dwo">The column the change applies to.</param>
    /// <param name="data">
    /// The proposed new value, as text. NULLABLE, AND THE NULL IS LOAD-BEARING: the paste path at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1050</c> executes <c>SetNull(sVal)</c> when the pasted cell is
    /// empty and the column declares <c>NilIsNull = "yes"</c>, and then raises this very event with
    /// that null at <c>:L1051</c>. Collapsing it to an empty string would tell a handler that a value
    /// is being written where the oracle says a null is - a difference a veto can observe.
    /// </param>
    /// <returns>
    /// <c>0</c> TO ACCEPT. ANY NON-ZERO VALUE IS A REJECTION - the test at
    /// <c>n_cst_dwsvc_rowselect.sru:L238</c> is <c>&lt;&gt; 0</c>, so this is NOT the four-value
    /// item-change alphabet and NOT the return-code algebra: it is a two-state accept-or-reject read
    /// of whatever code the handler produced.
    /// </returns>
    /// <remarks>
    /// The rejection is observable and must not be swallowed. Its consumer stops the propagation it
    /// was performing and surfaces a message naming the offending row
    /// [<c>:L238-L241</c>]; Services/RowSelectService.cs reproduces that as a structured error result
    /// because a headless service has no dialog. The four-value alphabet that the CHAIN layers on top
    /// of this event lives in Domain/ItemChangeProtocol.cs and is a separate mechanism.
    /// </remarks>
    public virtual long OnDoItemChange(long row, IDataWindowObject dwo, string? data)
    {
        return 0L;
    }

    /// <summary>
    /// Raised after one item's value has changed - the port of
    /// <c>Event OnDoItemChanged(row, dwo)</c> (declared <c>se_cst_dw.sru:L26</c>; raised by an
    /// attached service at <c>n_cst_dwsvc_rowselect.sru:L254</c>).
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    /// <remarks>
    /// RETURNS NOTHING, AND THAT IS THE LEGACY DECLARATION RATHER THAN A SIMPLIFICATION.
    /// <c>se_cst_dw.sru:L26</c> declares it without a <c>type</c> clause, unlike <c>:L24</c> which
    /// declares <c>type long</c> - so there is no code to test and its raiser at <c>:L254</c>
    /// discards nothing. Giving it a return value here would invent a veto the notification does not
    /// have.
    /// </remarks>
    public virtual void OnDoItemChanged(long row, IDataWindowObject dwo)
    {
    }

    /// <summary>
    /// Raised so the application can populate the context menu before it is shown - the port of
    /// <c>Event OnInitContextMenu(row, dwo)</c> (declared <c>se_cst_dw.sru:L11</c>; raised by an
    /// attached service at <c>n_cst_dwsvc_contextmenu.sru:L147</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row the menu was requested over, or <c>0</c> when none.</param>
    /// <param name="dwo">The object the menu was requested over.</param>
    /// <returns>
    /// <c>1</c> TO PREVENT THE MENU; any other value continues. The test at <c>:L147</c> is
    /// <c>= 1</c> - an EQUALITY, not a truthiness test - so <c>2</c> does not prevent and neither does
    /// <c>-1</c>. This is the tri-valued prevent convention read at its shallow value, and it must not
    /// be flattened to a boolean.
    /// </returns>
    /// <remarks>
    /// ORDERING: SYNCHRONOUS, AND STRICTLY SO (AAP 0.6.1.4 pattern (b) for the context-menu area).
    /// Population must COMPLETE before the identifier passed to
    /// <see cref="OnContextMenu(long, IDataWindowObject, long)"/> can mean anything, because that
    /// identifier is chosen from the very items this event added. A prevention here suppresses the whole
    /// menu, and the service still performs its per-invocation cleanup on that path
    /// [<c>:L197-L201</c>].
    /// </remarks>
    public virtual long OnInitContextMenu(long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised when a context-menu item has been chosen, before the service's own default handling -
    /// the port of <c>Event OnContextMenu(row, dwo, mid)</c> (declared <c>se_cst_dw.sru:L12</c>;
    /// raised by an attached service at <c>n_cst_dwsvc_contextmenu.sru:L194</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row the menu was shown over.</param>
    /// <param name="dwo">The object the menu was shown over.</param>
    /// <param name="mid">
    /// The chosen item's identifier, as returned by the menu. Meaningful only because
    /// <see cref="OnInitContextMenu(long, IDataWindowObject)"/> has already completed.
    /// </param>
    /// <returns>
    /// <c>1</c> to prevent the service's own default handling; any other value lets it proceed. Again an
    /// EQUALITY test at <c>:L194</c>.
    /// </returns>
    /// <remarks>
    /// THE SECOND HALF OF THE CONTEXT-MENU PAIR, AND THE APPLICATION'S CHANCE TO CLAIM AN IDENTIFIER
    /// IT ADDED ITSELF. The service's own dispatch [<c>:L196</c>, <c>:L204-L222</c>] recognises only the
    /// nine reserved <c>MID_*</c> values and has NO default arm, so an application identifier reaches
    /// the dispatch harmlessly and does nothing - preventing here is how an application acts on its own
    /// item rather than how it avoids a fault.
    /// </remarks>
    public virtual long OnContextMenu(long row, IDataWindowObject dwo, long mid)
    {
        return 0L;
    }

    /// <summary>
    /// Raised to let the application supply, replace or extend the drop-down search filter the
    /// service has just composed - the port of
    /// <c>Event OnDDSGetFilter(row, dwo, data, ref filter)</c> (declared
    /// <c>se_cst_dw.sru:L13</c>; raised by an attached service at
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L342</c>).
    /// </summary>
    /// <param name="row">
    /// The one-based row being edited, taken from the service's edit context. IT MAY BE <c>0</c>:
    /// the context is reset to row zero on lose-focus [<c>n_cst_dwsvc_dropdownsearch.sru:L148</c>],
    /// and the composition path is also reached during context initialisation.
    /// </param>
    /// <param name="dwo">
    /// The column being edited, or <see langword="null"/> when the edit context holds no valid
    /// object. NULLABLE ON PURPOSE - the legacy passes <c>_editCtx.dwo</c> straight through, and that
    /// field is deliberately assigned an UNINITIALISED <c>dwobject</c> by the lose-focus reset
    /// [<c>:L145-L148</c>]. Collapsing that to a sentinel would hide the reset from a handler that
    /// legitimately branches on it.
    /// </param>
    /// <param name="data">
    /// The search text, verbatim and UNESCAPED. THE EMPTY STRING IS A LIVE VALUE AND ITS CALL IS
    /// LOAD BEARING: <c>:L247</c> composes with <c>_of_GetFilter("")</c>, which skips the whole clause
    /// builder [<c>:L315</c>] and reaches this event with empty data - that is precisely how an
    /// application supplies the INITIAL filter for a freshly focused cell.
    /// </param>
    /// <param name="filter">
    /// The filter expression, passed BY REFERENCE. On entry it carries whatever the service composed,
    /// which is the empty string when no clause was produced; on return it is whatever the handler
    /// left there, and the service uses that value verbatim [<c>:L344</c>]. A handler may replace it,
    /// wrap it, or leave it untouched.
    /// </param>
    /// <remarks>
    /// <para>
    /// RETURNS NOTHING, AND THAT IS THE LEGACY DECLARATION. <c>se_cst_dw.sru:L13</c> carries no
    /// <c>type</c> clause, so the whole result of the event travels through the <c>ref</c> parameter.
    /// </para>
    /// <para>
    /// STRICTLY SYNCHRONOUS, AND THE <c>ref</c> IS THE REASON. AAP 0.6.1.4 assigns the drop-down
    /// search capability area pattern (b) - synchronous request and response with NO reordering
    /// permitted - specifically because the caller blocks on the produced filter and cannot proceed
    /// without it, so the answer has no fire-and-forget form however it is projected onto the wire. This
    /// member must therefore never be made fire-and-forget and must never carry a sequencing token as
    /// though its delivery were reorderable.
    /// </para>
    /// <para>
    /// WHY VIRTUAL WITH AN EMPTY DEFAULT. A PowerBuilder event with no script attached performs
    /// nothing and leaves its arguments alone, which is exactly this: the composed filter passes
    /// through unchanged. Making it abstract would force every host - including every test double -
    /// to supply a body for an event the legacy allows to be unhandled.
    /// </para>
    /// </remarks>
    public virtual void OnDDSGetFilter(long row, IDataWindowObject? dwo, string data, ref string filter)
    {
    }

    /// <summary>
    /// Raised after the drop-down search filter has been applied - the port of
    /// <c>Event OnDDSFiltered(row, dwo, rowcount, filteredcount)</c> (declared
    /// <c>se_cst_dw.sru:L28</c>; raised by an attached service at
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L408</c>).
    /// </summary>
    /// <param name="row">The one-based row being edited. May be <c>0</c>; see
    /// <see cref="OnDDSGetFilter"/>.</param>
    /// <param name="dwo">The column being edited, or <see langword="null"/>; see
    /// <see cref="OnDDSGetFilter"/>.</param>
    /// <param name="rowCount">
    /// The child's row count AS IT WAS BEFORE ANY BUFFER MOVE. The oracle captures it at
    /// <c>:L393</c>, under a comment at <c>:L392</c> that exists solely to say so, and repeats the
    /// same comment at <c>:L407</c> immediately above this raise.
    /// </param>
    /// <param name="filteredCount">
    /// The child's filtered-row count, likewise captured BEFORE the move, at <c>:L394</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// RETURNS NOTHING - <c>se_cst_dw.sru:L28</c> declares no <c>type</c> clause, so no veto is
    /// possible from here and the raiser discards nothing.
    /// </para>
    /// <para>
    /// THE ORDERING IS CONTRACT, NOT INCIDENTAL: the two counts are captured BEFORE the show-filtered
    /// rows relocation and this event is raised AFTER it, carrying the pre-move values. A handler
    /// therefore sees the partition as the user's query produced it rather than as the concealment
    /// left it. An implementation that re-read either count at raise time would report the post-move
    /// totals, which for the relocating branch are a different pair of numbers entirely.
    /// </para>
    /// </remarks>
    public virtual void OnDDSFiltered(long row, IDataWindowObject? dwo, long rowCount, long filteredCount)
    {
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
/// [<c>se_cst_dw.sru:L80-L84</c>]. Only the HOST-FACING half belongs here, PLUS the thin
/// property-reading helpers the legacy declares <c>protected</c> on this very base and shares
/// between services - see THE SHARED PROPERTY-READING HELPER LAYER below for the eight that are
/// carried and the exact criterion that admits them. What stays out is the half that needs machinery
/// this type does not have: <c>_of_lookupdisplay</c> [<c>:L271</c>] and <c>_of_evaluate</c>
/// [<c>:L199</c>] need the DataWindow expression evaluator, and <c>_of_getcolumnvaluemap</c>
/// [<c>:L560</c>] needs an <see cref="IDataWindowChild"/> plus an ordered map. Those three belong to
/// Expressions/, which consumes this type rather than being consumed by it, along with the
/// object-enumeration helpers at <c>:L633-L805</c>.
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

    // ==========================================================================================
    //  THE SHARED PROPERTY-READING HELPER LAYER
    //  ----------------------------------------------------------------------------------------
    //  WHY THESE EIGHT ARE HERE. The legacy declares them `protected` on n_cst_dwsvc itself
    //  [n_cst_dwsvc.sru:L47-L73], which is the type this class ports, and it declares them there
    //  because MORE THAN ONE attached service consumes them. Reproducing them inside a single
    //  service would leave the other four to duplicate the same Describe-and-Evaluate composition,
    //  and the composition is not trivial: the tab-delimited property-versus-expression split at
    //  :L189-L196 has to come out byte-identical everywhere or two services will disagree about
    //  whether the same cell is protected.
    //
    //  THE ADMISSION CRITERION IS DEPENDENCY, NOT SIZE. Every member in the first eight composes a
    //  string and hands it to DataWindowServiceHost.Describe. Not one of them needs the DataWindow
    //  expression evaluator or any presentational primitive - so each is fully portable here, and each
    //  is fully testable against a Describe test double with no DataWindow, no database and no UI
    //  (constraint C-H). The helpers that DO need the expression evaluator are named in this class's
    //  own remarks and stay in Expressions/.
    //
    //  SEVEN MORE FOLLOW THE EIGHT, ADDED FOR Services/ContextMenuModel.cs, and two of them widen the
    //  criterion in a way worth stating: GetColumnValueMap reaches an IDataWindowChild and builds an
    //  OrderedMap. Neither is presentational and neither is an expression evaluator - a child
    //  DataWindow is a DATA source and the ordered map is the ported n_map container - so the criterion
    //  still holds as written: what may not appear here is UI, and what must not appear here is
    //  behaviour a single service could own alone. Both new dependencies arrive through project
    //  references PowerFramework.DataServices.csproj already declares (constraint C-I).
    //
    //  NAMES ARE PascalCase, FOLLOWING GetDataWindowObject ABOVE. The `_of_` prefix is PowerBuilder's
    //  protected-member convention, not part of any wire payload, log record or characterization
    //  recording - so AAP 0.4.5.3, which preserves CONSTANT identifier spellings because they travel
    //  in serialized output, does not reach these. Keeping the prefix would instead have raised CA1707
    //  on eight externally-visible members and forced a naming-analyzer suppression onto this file for
    //  no parity benefit.
    //
    //  THE FIRST-CLASS OVERLOADS ONLY. The legacy declares a column-NUMBER twin of almost every one
    //  of these, each a one-line `_of_X("#" + String(colNum))` delegation [:L244, :L268, :L351,
    //  :L394, :L397, :L452, :L541]. None is carried, because no in-scope ported call site uses one:
    //  n_cst_dwsvc_rowselect.sru threads `sColName = dwo.Name` [:L188] through every call. The
    //  delegation is trivially recoverable from GetDataWindowObject's own "#" composition if a later
    //  service needs it, and AAP 0.4.2.5's consumption criterion forbids adding it before then.
    // ==========================================================================================

    /// <summary>
    /// Whether the DataWindow as a whole accepts edits - the port of <c>_of_iseditable()</c>
    /// (<c>n_cst_dwsvc.sru:L295</c>, body <c>:L312</c>).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the DataWindow is editable.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// THE TEST IS AGAINST THE STRING <c>"no"</c> AND NOT AGAINST NOT-<c>"yes"</c>. <c>:L312</c> is
    /// <c>return (#DataWindow.Describe("DataWindow.ReadOnly") = "no")</c>, so ANY answer other than
    /// that exact text - including the <c>"!"</c> invalid-expression sentinel and the <c>"?"</c>
    /// undetermined sentinel a DataWindow returns for an unanswerable Describe - reads as NOT
    /// editable. Inverting the test to <c>!= "yes"</c> would turn both sentinels into "editable" and
    /// silently open every guard that depends on this one.
    /// </remarks>
    protected bool IsEditable()
    {
        // n_cst_dwsvc.sru:L312 - ordinal comparison, because the oracle compares a fixed
        // machine-generated token rather than user text; a culture-sensitive compare could match
        // differently under a Turkish-style casing rule.
        return string.Equals(
            RequireHost().Describe("DataWindow.ReadOnly"),
            "no",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The DataWindow's presentation style - the port of <c>_of_getstyle()</c>
    /// (<c>n_cst_dwsvc.sru:L46</c>, body <c>:L143-L158</c>).
    /// </summary>
    /// <returns>
    /// One of the <c>STYLE_*</c> constants declared on this class.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE PROPERTY READ IS <c>"DataWindow.Processing"</c> AND NOT <c>"DataWindow.Style"</c>. That is
    /// worth stating because the method NAME says style while the property says processing, and a
    /// reader "correcting" the property name would get the invalid-expression sentinel back and every
    /// style would collapse to <c>STYLE_DEFAULT</c>. The mapping is the oracle's, digit for digit:
    /// <c>"1"</c> grid, <c>"2"</c> label, <c>"3"</c> graph, <c>"4"</c> crosstab, <c>"5"</c> composite,
    /// <c>"7"</c> rich text.
    /// </para>
    /// <para>
    /// *** THERE IS NO ARM FOR <c>"6"</c>, AND THERE MUST NOT BE ONE. *** The gap documented on the
    /// <c>STYLE_*</c> constant block reappears here as a missing <c>case</c>, so a processing value of
    /// <c>6</c> falls to the default arm and answers <c>STYLE_DEFAULT</c> - the SAME answer an
    /// unreadable property gives. Adding a sixth arm, or reaching for an enum parse, would invent a
    /// distinction the oracle does not draw (constraint C-B).
    /// </para>
    /// <para>
    /// THE DEFAULT ARM IS FOUR STYLES PLUS EVERY UNRECOGNISED ANSWER AT ONE VALUE, which is why it is
    /// reached by the <c>"?"</c> and <c>"!"</c> Describe sentinels too. Callers therefore test for the
    /// style they want rather than for "not default": <c>n_cst_dwsvc_columnsort.sru:L61</c> is
    /// <c>if _of_GetStyle() &lt;&gt; STYLE_GRID then return 0</c>, so an unreadable property correctly
    /// disables column sorting rather than enabling it.
    /// </para>
    /// </remarks>
    protected long GetPresentationStyle()
    {
        // n_cst_dwsvc.sru:L143 - ordinal comparison throughout, because these are machine-generated
        // single-digit tokens rather than user text.
        string processing = RequireHost().Describe("DataWindow.Processing");

        // :L144-L157 - the choose case, arm for arm and in the oracle's own order. Written as a
        // switch on the raw string rather than on a parsed number so that a non-numeric answer
        // reaches the default arm by the same route the oracle takes.
        return processing switch
        {
            "1" => STYLE_GRID,       // :L145-L146
            "2" => STYLE_LABEL,      // :L147-L148
            "3" => STYLE_GRAPH,      // :L149-L150
            "4" => STYLE_CROSSTAB,   // :L151-L152
            "5" => STYLE_COMPOSITE,  // :L153-L154
                                     // NO ARM FOR "6" - see the remarks. It falls through below.
            "7" => STYLE_RICHTEXT,   // :L155-L156
            _ => STYLE_DEFAULT,      // :L157-L158 - and every unrecognised answer, including "?"/"!".
        };
    }

    /// <summary>
    /// Whether the DataWindow defines any grouping - the port of <c>_of_hasgroup()</c>
    /// (<c>n_cst_dwsvc.sru:L82</c>, body <c>:L850-L852</c>).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a group header OR a group trailer band exists.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EXISTENCE IS PROVED BY THE ABSENCE OF THE INVALID-EXPRESSION SENTINEL, NOT BY A HEIGHT. Both
    /// tests are <c>Describe(...) &lt;&gt; "!"</c> against the height of GROUP 1's header and trailer
    /// bands - a DataWindow with no groups has no such bands, so the property is unrecognised and
    /// answers <c>"!"</c>. A zero height is therefore a REAL group with a collapsed band and reads as
    /// true, which is correct and is what a numeric test would get wrong.
    /// </para>
    /// <para>
    /// ONLY GROUP 1 IS PROBED, AND ONLY TWO BANDS. Group 2 and beyond cannot exist without group 1,
    /// so one probe of each kind settles it; adding further probes would cost Describe calls and
    /// change nothing. The two tests are ORDERED AND SHORT-CIRCUITING [<c>:L850</c> then
    /// <c>:L851</c>], so a DataWindow with a group header never has its trailer probed - reproduced
    /// with two sequential returns rather than an <c>||</c> so the call order is visible at the point
    /// it happens.
    /// </para>
    /// <para>
    /// CONSUMED BY THE SORT SERVICE ON TWO OPPOSITE POLARITIES: <c>:L63</c> REFUSES to sort a grouped
    /// DataWindow from a header click, while <c>:L417</c> recomputes the group aggregates AFTER a sort
    /// applied by another route. Both are live, so neither test may be simplified away.
    /// </para>
    /// </remarks>
    protected bool HasGroup()
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc.sru:L850 - the group HEADER band. "!" means the property is unrecognised,
        // which means there is no such band, which means there is no group.
        if (!string.Equals(
                host.Describe("DataWindow.Header.1.Height"),
                InvalidExpressionSentinel,
                StringComparison.Ordinal))
        {
            return true;
        }

        // :L851 - the group TRAILER band. Reached only when the header probe said no.
        if (!string.Equals(
                host.Describe("DataWindow.Trailer.1.Height"),
                InvalidExpressionSentinel,
                StringComparison.Ordinal))
        {
            return true;
        }

        // :L852
        return false;
    }

    /// <summary>
    /// The answer a DataWindow gives when a Describe property is not recognised at all.
    /// </summary>
    /// <remarks>
    /// Named because <see cref="HasGroup"/> uses its ABSENCE as proof that a band exists, which is
    /// the opposite of the usual "treat the sentinel as a failure" reading and is easy to misread as
    /// a bug. It is deliberately not shared with the <c>"?"</c> undetermined sentinel: the two mean
    /// different things and <c>n_cst_dwsvc_columnsort.sru:L403</c> is one of the few places that
    /// normalises both together.
    /// </remarks>
    private const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The answer a DataWindow gives when a Describe property is recognised but cannot be determined
    /// in the current context - <c>"?"</c>.
    /// </summary>
    /// <remarks>
    /// DISTINCT FROM <see cref="InvalidExpressionSentinel"/> AND NOT INTERCHANGEABLE WITH IT, for the
    /// reason that constant's own remarks give. It is named here because the seven helpers added for
    /// <c>Services/ContextMenuModel.cs</c> test BOTH sentinels together at five sites -
    /// <c>n_cst_dwsvc.sru</c>'s <c>_of_getdwoprop</c> body, <c>:L587</c>, <c>:L598</c>, <c>:L599</c>
    /// and <c>:L701</c> - and spelling the pair out at each would invite one of them to drift.
    /// </remarks>
    private const string UndeterminedValueSentinel = "?";

    /// <summary>
    /// The TAB that separates a DataWindow property's unconditional value from its conditional
    /// expression, and that separates the entries of both <c>DataWindow.Objects</c> and a code-table
    /// entry's display and data halves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE CHARACTER, THREE UNRELATED GRAMMARS, AND THE ORACLE SPELLS IT <c>"~t"</c> IN ALL THREE. It is
    /// named once here rather than at each site so that a reader can see the three uses are the same
    /// byte and not a coincidence: the property split
    /// (<c>_of_getdwoprop</c>, <c>_of_getpropexp</c>), the code-table split
    /// [<c>n_cst_dwsvc.sru:L653</c>] and the object-name list [<c>:L697</c>].
    /// </para>
    /// <para>
    /// PROTECTED RATHER THAN PRIVATE because a derived service has to make the SAME tab test for
    /// itself: the auto-width pass detects an expression-form format with
    /// <c>Pos(sFormat,"~t") &gt; 0</c> [<c>n_cst_dwsvc_contextmenu.sru:L1169</c>, <c>:L1335</c>] before
    /// handing the stripped expression to <see cref="GetPropertyExpression"/>. Re-declaring the literal
    /// there would be exactly the duplication this constant exists to prevent.
    /// </para>
    /// </remarks>
    protected const char PropertyExpressionSeparator = '\t';

    /// <summary>
    /// The separator between names in the <c>DataWindow.Objects</c> answer - the same TAB as
    /// <see cref="PropertyExpressionSeparator"/>, named separately because the grammar is different.
    /// </summary>
    /// <remarks>
    /// The list has NO TRAILING SEPARATOR, which is the whole reason
    /// <see cref="GetObjectNames(ref string[], in string, in string)"/> needs a tail block after its
    /// loop [<c>n_cst_dwsvc.sru:L722-L742</c>].
    /// </remarks>
    private const char ObjectNameSeparator = '\t';

    /// <summary>
    /// Reads one of a column's properties, resolving a property EXPRESSION when the DataWindow
    /// reports one - the port of <c>_of_getcolumnprop(colName, prop)</c>
    /// (<c>n_cst_dwsvc.sru:L314</c>, body <c>:L332-L346</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <param name="prop">The property name, for example <c>"protect"</c> or <c>"visible"</c>.</param>
    /// <returns>
    /// The property's value as text, or the empty string when either argument is empty.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE TAB IS THE WHOLE MECHANISM. A DataWindow answers a property Describe either with a plain
    /// value or with <c>value</c> TAB <c>expression"</c> when the property is conditionally computed.
    /// <c>:L339</c> looks for the tab; with no tab the plain value is returned as-is [<c>:L341</c>],
    /// and with a tab the text AFTER the tab is taken, an OPENING DOUBLE QUOTE IS PREPENDED, and the
    /// result is evaluated [<c>:L343-L344</c>].
    /// </para>
    /// <para>
    /// ONLY AN OPENING QUOTE IS ADDED, AND THAT IS DELIBERATE RATHER THAN A BUG. The DataWindow's own
    /// answer already ends with the closing quote - <c>_of_getpropexp</c> [<c>:L806</c>, body
    /// <c>:L825-L831</c>] confirms it by stripping exactly one trailing character with
    /// <c>Len(prop) - nPos - 1</c>. Adding a second closing quote here would produce
    /// <c>Evaluate("expr"",0)</c> and every conditional property in the DataWindow would start
    /// answering with the invalid-expression sentinel.
    /// </para>
    /// <para>
    /// EVALUATED AT ROW <c>0</c>, unlike <see cref="GetItemProperty(in long, in string, in string)"/>
    /// which evaluates at a specific row [<c>:L195</c>]. That is the only difference between the two
    /// bodies, and it is the difference between a COLUMN-level and an ITEM-level answer. The two are
    /// not interchangeable and neither is expressed in terms of the other, exactly as in the oracle.
    /// </para>
    /// </remarks>
    protected string GetColumnProperty(in string colName, in string prop)
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc.sru:L336 - both-arguments guard, returning the empty string rather than
        // raising. IsNullOrEmpty rather than Length, so a null reads as empty exactly as
        // PowerScript's always-present possibly-empty string does.
        if (string.IsNullOrEmpty(colName) || string.IsNullOrEmpty(prop))
        {
            return string.Empty;
        }

        // :L338
        string exp = host.Describe(colName + "." + prop);

        // :L339 - Pos returns a ONE-BASED index and 0 when absent; IndexOf returns ZERO-BASED and
        // -1. The translation is the comparison below, and it is the kind of off-by-one AAP 0.4.5.4
        // names as the refactor's most dangerous mechanical hazard, so it is written out rather than
        // folded into an expression.
        int tabIndex = exp.IndexOf('\t', StringComparison.Ordinal);

        // :L341 - no tab, so the plain value IS the answer.
        if (tabIndex < 0)
        {
            return exp;
        }

        // :L343 - the text after the tab, with one OPENING quote prepended and no closing quote
        // added. PowerScript's Mid(exp, nPos + 1) with a one-based nPos starts at the character
        // after the tab, which is the zero-based index tabIndex + 1.
        string quoted = "\"" + exp[(tabIndex + 1)..];

        // :L344 - evaluated at row 0. Composed as a string because Describe's Evaluate form is the
        // DataWindow's own expression entry point, exactly as the oracle composes it.
        return host.Describe("Evaluate(" + quoted + ",0)");
    }

    /// <summary>
    /// Reads one of a single ITEM's properties, resolving a property EXPRESSION at that row when the
    /// DataWindow reports one - the port of <c>_of_getitemprop(row, colName, prop)</c>
    /// (<c>n_cst_dwsvc.sru:L164</c>, body <c>:L183-L197</c>).
    /// </summary>
    /// <param name="row">The one-based row number the expression is evaluated at.</param>
    /// <param name="colName">The column name.</param>
    /// <param name="prop">The property name, for example <c>"protect"</c> or <c>"visible"</c>.</param>
    /// <returns>
    /// The property's value as text, or the empty string when either name is empty.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// Identical to <see cref="GetColumnProperty(in string, in string)"/> except that the expression
    /// is evaluated at <paramref name="row"/> rather than at row <c>0</c> [<c>:L195</c>] - which is
    /// what makes a per-row protect or visible expression answer differently for different rows.
    /// Note that <paramref name="row"/> is NOT validated: the oracle guards the two NAMES and not the
    /// row [<c>:L186</c>], so a row outside the buffer reaches Describe and comes back as the
    /// DataWindow's own sentinel rather than as an exception. Adding a range check here would convert
    /// a sentinel into a throw, which constraint C-B forbids.
    /// </remarks>
    protected string GetItemProperty(in long row, in string colName, in string prop)
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc.sru:L186
        if (string.IsNullOrEmpty(colName) || string.IsNullOrEmpty(prop))
        {
            return string.Empty;
        }

        // :L188
        string exp = host.Describe(colName + "." + prop);

        // :L189
        int tabIndex = exp.IndexOf('\t', StringComparison.Ordinal);

        // :L192
        if (tabIndex < 0)
        {
            return exp;
        }

        // :L194 - see GetColumnProperty for why only an OPENING quote is prepended.
        string quoted = "\"" + exp[(tabIndex + 1)..];

        // :L195 - evaluated AT THIS ROW. Invariant-culture formatting, which PowerScript's
        // String(long) has no parameter for and does implicitly: a culture with digit-group
        // separators would compose Evaluate("expr",1,234) and resolve nothing.
        return host.Describe(
            "Evaluate(" + quoted + "," + row.ToString(CultureInfo.InvariantCulture) + ")");
    }

    /// <summary>
    /// Whether a COLUMN is protected - the port of <c>_of_iscolumnprotected(colName)</c>
    /// (<c>n_cst_dwsvc.sru:L374</c>, body <c>:L391</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns><see langword="true"/> when the column's protect property reads <c>"1"</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// THE TEST IS EQUALITY WITH THE STRING <c>"1"</c>, not truthiness and not a numeric parse. Every
    /// other answer - <c>"0"</c>, the empty string from the guard, and both DataWindow sentinels -
    /// reads as NOT protected, which is the permissive direction. That asymmetry is the legacy's and
    /// is reproduced: an unanswerable Describe leaves the column editable rather than locking it.
    /// </remarks>
    protected bool IsColumnProtected(in string colName)
    {
        // n_cst_dwsvc.sru:L391
        return string.Equals(GetColumnProperty(colName, "protect"), "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a COLUMN accepts edits - the port of <c>_of_iscolumneditable(colName)</c>
    /// (<c>n_cst_dwsvc.sru:L400</c>, body <c>:L417-L426</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns><see langword="true"/> when all four conditions below hold.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FOUR GUARDS, IN THE ORACLE'S ORDER, EACH SHORT-CIRCUITING TO <see langword="false"/>:
    /// </para>
    /// <para>
    /// 1. <c>:L419</c> the DataWindow itself must be editable - see <see cref="IsEditable"/>.
    /// </para>
    /// <para>
    /// 2. <c>:L421</c> the tab sequence must be neither <c>"0"</c> nor <c>"32766"</c>. Both are
    /// magic values with distinct meanings in PowerBuilder - zero removes the column from the tab
    /// order entirely, and 32766 is the conventional read-only-but-tabbable value - and BOTH are
    /// compared AS TEXT, so <c>"00"</c> and <c>"32766 "</c> would not match. Reproduced as written.
    /// </para>
    /// <para>
    /// 3. <c>:L423</c> the edit must not be display-only, tested as the string <c>"yes"</c>.
    /// </para>
    /// <para>
    /// 4. <c>:L425</c> the column must not be protected.
    /// </para>
    /// <para>
    /// NOTE THAT GUARDS 2 AND 3 GO STRAIGHT TO <c>Describe</c> AND DO NOT USE
    /// <see cref="GetColumnProperty(in string, in string)"/>, so a tab sequence or display-only
    /// setting expressed as a conditional EXPRESSION is NOT evaluated here - the raw
    /// <c>value</c>-TAB-<c>expression</c> text is compared and therefore matches neither magic value.
    /// That inconsistency with guard 4, which does resolve expressions, is the legacy's own and is
    /// preserved rather than harmonised (constraint C-B).
    /// </para>
    /// </remarks>
    protected bool IsColumnEditable(in string colName)
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc.sru:L419
        if (!IsEditable())
        {
            return false;
        }

        // :L420-L421 - the raw Describe, deliberately NOT routed through GetColumnProperty.
        string prop = host.Describe(colName + ".TabSequence");
        if (string.Equals(prop, "0", StringComparison.Ordinal)
            || string.Equals(prop, "32766", StringComparison.Ordinal))
        {
            return false;
        }

        // :L422-L423
        prop = host.Describe(colName + ".Edit.DisplayOnly");
        if (string.Equals(prop, "yes", StringComparison.Ordinal))
        {
            return false;
        }

        // :L425
        if (IsColumnProtected(colName))
        {
            return false;
        }

        // :L427
        return true;
    }

    /// <summary>
    /// Whether one ITEM is protected - the port of <c>_of_isitemprotected(row, colName)</c>
    /// (<c>n_cst_dwsvc.sru:L223</c>, body <c>:L241</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="colName">The column name.</param>
    /// <returns><see langword="true"/> when the item's protect property reads <c>"1"</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// Distinct from <see cref="IsColumnProtected(in string)"/> because a protect expression can
    /// answer differently per row - which is exactly why the range check-box propagation at
    /// <c>n_cst_dwsvc_rowselect.sru:L201</c> and <c>:L229</c> tests the ITEM, once for the clicked
    /// row and again for every selected row it visits.
    /// </remarks>
    protected bool IsItemProtected(in long row, in string colName)
    {
        // n_cst_dwsvc.sru:L241
        return string.Equals(GetItemProperty(row, colName, "protect"), "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether one ITEM is visible - the port of <c>_of_isitemvisible(row, colName)</c>
    /// (<c>n_cst_dwsvc.sru:L247</c>, body <c>:L265</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="colName">The column name.</param>
    /// <returns><see langword="true"/> when the item's visible property reads <c>"1"</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// THE FAIL-CLOSED DIRECTION IS THE OPPOSITE OF <see cref="IsItemProtected(in long, in string)"/>,
    /// AND THAT MATTERS AT THE CALL SITE. Both test for the string <c>"1"</c>, so an unanswerable
    /// Describe reads as NOT protected and NOT visible; at
    /// <c>n_cst_dwsvc_rowselect.sru:L227</c> the invisible answer SKIPS the row, whereas the
    /// not-protected answer at <c>:L229</c> lets it through. One sentinel therefore has opposite
    /// effects two lines apart. Both are reproduced exactly as the oracle has them.
    /// </remarks>
    protected bool IsItemVisible(in long row, in string colName)
    {
        // n_cst_dwsvc.sru:L265
        return string.Equals(GetItemProperty(row, colName, "visible"), "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps a DataWindow column type string onto one of the <c>COL_TYPE_*</c> constants - the port of
    /// <c>_of_convertcoltype(colType)</c> (<c>n_cst_dwsvc.sru:L486</c>, body <c>:L503-L518</c>).
    /// </summary>
    /// <param name="colType">
    /// The raw type text, for example <c>"char(50)"</c>, <c>"decimal(2)"</c> or <c>"long"</c>.
    /// </param>
    /// <returns>
    /// One of <see cref="COL_TYPE_STRING"/>, <see cref="COL_TYPE_INTEGER"/>,
    /// <see cref="COL_TYPE_DECIMAL"/>, <see cref="COL_TYPE_DATETIME"/>, <see cref="COL_TYPE_DATE"/>,
    /// <see cref="COL_TYPE_TIME"/> or <see cref="COL_TYPE_UNKNOWN"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE MATCH IS ON THE FIRST FIVE CHARACTERS, AND THE ORDER OF THE ARMS IS LOAD-BEARING.
    /// <c>:L503</c> switches on <c>Left(colType,5)</c> over seven arms: <c>"char"</c>/<c>"char("</c>
    /// to string, <c>"numbe"</c>/<c>"long"</c>/<c>"ulong"</c> to integer,
    /// <c>"decim"</c>/<c>"real"</c> to decimal, <c>"datet"</c> to datetime, <c>"date"</c> to date,
    /// <c>"time"</c> to time, and anything else to unknown.
    /// </para>
    /// <para>
    /// <c>"datet"</c> MUST BE TESTED BEFORE <c>"date"</c>, and the five-character truncation is why.
    /// <c>"datetime"</c> truncates to exactly <c>"datet"</c> while <c>"date"</c> is shorter than the
    /// truncation length and stays <c>"date"</c>, so the two are distinguishable - but only if the
    /// longer token is not shadowed. The C# switch below preserves the oracle's arm order verbatim
    /// for that reason.
    /// </para>
    /// <para>
    /// PowerScript's <c>Left(s, 5)</c> returns the WHOLE string when it is shorter than five
    /// characters rather than raising, which is why <c>"long"</c>, <c>"real"</c>, <c>"date"</c> and
    /// <c>"time"</c> are spelled at their natural length in the arms above. The C# equivalent is a
    /// length-clamped slice, not <c>Substring(0, 5)</c>, which would throw.
    /// </para>
    /// </remarks>
    protected static long ConvertColumnType(in string colType)
    {
        // n_cst_dwsvc.sru:L503 - Left(colType,5), clamped so a shorter string is returned whole
        // exactly as PowerScript's Left does. Ordinal comparison throughout: these are
        // machine-generated tokens, never user text.
        string prefix = colType.Length <= 5 ? colType : colType[..5];

        return prefix switch
        {
            // :L504-L505
            "char" or "char(" => COL_TYPE_STRING,

            // :L506-L507
            "numbe" or "long" or "ulong" => COL_TYPE_INTEGER,

            // :L508-L509
            "decim" or "real" => COL_TYPE_DECIMAL,

            // :L510-L511 - BEFORE "date", see the remarks.
            "datet" => COL_TYPE_DATETIME,

            // :L512-L513
            "date" => COL_TYPE_DATE,

            // :L514-L515
            "time" => COL_TYPE_TIME,

            // :L516-L517
            _ => COL_TYPE_UNKNOWN,
        };
    }

    /// <summary>
    /// The <c>COL_TYPE_*</c> classification of a named column - the port of
    /// <c>_of_getcolumntype(name)</c> (<c>n_cst_dwsvc.sru:L521</c>, body <c>:L538</c>).
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>
    /// The classification, or <see cref="COL_TYPE_UNKNOWN"/> when the DataWindow cannot answer.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// A one-line composition in the oracle:
    /// <c>_of_ConvertColType(#DataWindow.Describe(name+".ColType"))</c>. It reads the type from the
    /// DataWindow rather than from <see cref="IDataWindowObject.ColType"/> even where a handle is in
    /// hand, and that is preserved: the two are separate paths in the legacy and a Describe answers
    /// with a sentinel where a handle would have raised. Both DataWindow sentinels truncate to
    /// themselves and fall through to <see cref="COL_TYPE_UNKNOWN"/>, which is the same arm an
    /// unrecognised type reaches - so an unanswerable column and an exotic one are deliberately
    /// indistinguishable here, exactly as in the oracle.
    /// </remarks>
    protected long GetColumnType(in string name)
    {
        // n_cst_dwsvc.sru:L538
        return ConvertColumnType(RequireHost().Describe(name + ".ColType"));
    }

    // ==========================================================================================
    //  SEVEN FURTHER PROTECTED HELPERS FROM n_cst_dwsvc, ALL CONCRETE IN THE ORACLE
    //  ----------------------------------------------------------------------------------------
    //  Every member below ports a body that ALREADY EXISTS in
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru as a `protected function`, so none
    //  of them invents behaviour and none of them is abstract: adding them breaks no implementer.
    //  They live here for the reason the whole helper layer lives here - the oracle declares them
    //  `protected` on the SHARED BASE so that all five attached services reach one copy, and
    //  reproducing them on a service instead would fork that copy per service.
    //
    //  THEY ARE ADDED BECAUSE THEY ARE CONSUMED. Services/ContextMenuModel.cs reaches every one:
    //  _of_LookupDisplay at n_cst_dwsvc_contextmenu.sru:L1205 and :L1371; _of_GetDWOProp at :L1142
    //  and :L1308; _of_GetDWOText at :L1137 and :L1303; _of_GetPropExp at :L1171 and :L1337;
    //  _of_SplitString at :L952; _of_GetObjectNames at :L1100 and :L1284; _of_GetColumnValueMap at
    //  :L987. GetValue and GetChild were already added to the host contract on the strength of
    //  _of_getcolumnvaluemap's two branches, which is the other half of this same finding.
    //
    //  NOTHING HERE IS PRESENTATIONAL. Each one reads the DATA MODEL through Describe, GetValue,
    //  GetChild or a typed item read. No window, font, device coordinate or DPI conversion appears,
    //  so constraint C-D is not engaged by any of them - which is exactly why the headless half of
    //  the three presentational DataWindow services can be ported at all.
    // ==========================================================================================

    /// <summary>
    /// Reads one item's DISPLAY value - the value the user sees rather than the value stored - as the
    /// port of <c>_of_LookupDisplay(row, colName)</c> (<c>n_cst_dwsvc.sru:L289</c>).
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="colName">The column name.</param>
    /// <returns>
    /// The display text, or one of the DataWindow sentinels - <c>"!"</c> for an invalid expression and
    /// <c>"?"</c> for an undetermined one. Callers must test the sentinels rather than assume text;
    /// <c>n_cst_dwsvc_contextmenu.sru:L1207</c> tests only for the empty string, which is the oracle's
    /// own narrower test and is preserved as such at that call site.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WHOLE BODY IS ONE <c>Describe</c> OF AN <c>Evaluate</c> PROPERTY - <c>:L289</c> is
    /// <c>Describe("Evaluate('LookUpDisplay("+colName+")',"+string(row)+")")</c> - so the DISPLAY value
    /// is obtained by asking the DataWindow's own expression engine, not by reading the buffer. That
    /// distinction is the point of the member: for a code-table, DDDW or check-box column the stored
    /// value and the displayed value differ.
    /// </para>
    /// <para>
    /// AAP 0.4.2.5 NAMES THIS EXACT LINE as one of the sites behind the largest net-new obligation in
    /// the refactor, because <c>Describe("Evaluate(...)")</c> has no .NET equivalent. It is resolved by
    /// composition rather than here: this member composes the property text the oracle composes and
    /// hands it to the host, and a host whose <see cref="DataWindowServiceHost.Describe(string)"/>
    /// routes an <c>Evaluate</c> property through <c>Expressions/DataWindowExpressionEvaluator.cs</c> is
    /// what answers it. Keeping the composition here and the evaluation there is what lets a test
    /// double answer the same property with no evaluator at all.
    /// </para>
    /// <para>
    /// THE ROW IS FORMATTED INVARIANTLY. <c>String(row)</c> on an integral value cannot vary by culture
    /// in PowerScript, and pinning the culture here keeps the composed property text - which appears in
    /// characterization recordings - identical on every machine.
    /// </para>
    /// </remarks>
    protected string LookupDisplay(in long row, in string colName)
    {
        // n_cst_dwsvc.sru:L289
        return RequireHost().Describe(
            "Evaluate('LookUpDisplay("
            + colName
            + ")',"
            + row.ToString(CultureInfo.InvariantCulture)
            + ")");
    }

    /// <summary>
    /// Reads one DataWindow object's property, resolving it when the property holds an EXPRESSION
    /// rather than a literal - the port of <c>_of_GetDWOProp(dwoName, prop)</c>
    /// (<c>n_cst_dwsvc.sru</c>, the <c>_of_getdwoprop</c> body).
    /// </summary>
    /// <param name="dwoName">The object name.</param>
    /// <param name="prop">The property name, for example <c>"text"</c> or <c>"Format"</c>.</param>
    /// <returns>
    /// The literal property value, the EVALUATED value when the property carries an expression, or the
    /// EMPTY STRING when the property is unreadable. Note that the two sentinels are normalised to the
    /// empty string HERE, unlike <see cref="GetColumnProperty"/> which passes them through - the
    /// difference is the oracle's and both are preserved.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE TAB IS THE DISCRIMINATOR, AND IT IS A THREE-STEP DECISION. A DataWindow property that has
    /// been made conditional reads back as <c>literal~texpression"</c> - the unconditional value, a
    /// TAB, then the expression with a TRAILING DOUBLE QUOTE. So: read the property; normalise both
    /// sentinels to empty; if there is no tab the value IS the answer; otherwise take everything after
    /// the tab, PREFIX A DOUBLE QUOTE, and evaluate the result at row <c>0</c>.
    /// </para>
    /// <para>
    /// THE PREFIXED QUOTE IS NOT A TYPO AND MUST NOT BE "CLEANED UP". The stored form ends with a
    /// closing quote and no opening one, so the oracle supplies the opening quote to make
    /// <c>Evaluate("...",0)</c> parse - which is why this member, unlike
    /// <see cref="GetPropertyExpression"/>, does NOT strip the trailing character. The two helpers
    /// consume the same encoding for different purposes and their string arithmetic differs
    /// accordingly.
    /// </para>
    /// <para>
    /// ROW <c>0</c> IS DELIBERATE: a property expression is evaluated OUTSIDE any row context, so a
    /// per-row property answers its unconditional form here.
    /// </para>
    /// </remarks>
    protected string GetDataWindowObjectProperty(in string dwoName, in string prop)
    {
        DataWindowServiceHost host = RequireHost();

        // _of_getdwoprop: sExp = #DataWindow.Describe(dwoName + "." + prop)
        string exp = host.Describe(dwoName + "." + prop);

        // _of_getdwoprop: if sExp = "!" or sExp = "?" then return ""
        if (string.Equals(exp, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(exp, UndeterminedValueSentinel, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // _of_getdwoprop: nPos = Pos(sExp,"~t") - PowerScript's Pos is one-based and answers 0 for no
        // match, which is IndexOf's -1. The branch below is on PRESENCE, so the two agree.
        int tab = exp.IndexOf(PropertyExpressionSeparator, StringComparison.Ordinal);
        if (tab < 0)
        {
            // _of_getdwoprop: if nPos = 0 then return sExp
            return exp;
        }

        // _of_getdwoprop: sExp = "~"" + Mid(sExp,nPos + 1)
        //
        // Mid's one-based start of (tabPosition + 1) is the zero-based index (tab + 1), so the
        // conversion is the whole of the one-based arithmetic here and it is done exactly once.
        string quoted = "\"" + exp[(tab + 1)..];

        // _of_getdwoprop: return #DataWindow.Describe("Evaluate("+sExp+",0)")
        return host.Describe("Evaluate(" + quoted + ",0)");
    }

    /// <summary>
    /// Reads one DataWindow object's <c>text</c> property - the port of <c>_of_GetDWOText(dwoName)</c>
    /// (<c>n_cst_dwsvc.sru</c>, the <c>_of_getdwotext</c> body).
    /// </summary>
    /// <param name="dwoName">The object name.</param>
    /// <returns>The object's text, or the empty string when it is unreadable.</returns>
    /// <remarks>
    /// A ONE-LINE DELEGATION IN THE ORACLE TOO - <c>return _of_GetDWOProp(dwoName,"text")</c> - so it
    /// inherits the expression resolution and the sentinel normalisation rather than repeating them. It
    /// exists as its own member because the text property is the one a caller wants by far most often;
    /// <c>n_cst_dwsvc_contextmenu.sru:L1137</c> and <c>:L1303</c> use it to obtain the label of a
    /// heading object whose width is being measured.
    /// </remarks>
    protected string GetDataWindowObjectText(in string dwoName)
    {
        // _of_getdwotext: return _of_GetDWOProp(dwoName,"text")
        return GetDataWindowObjectProperty(dwoName, "text");
    }

    /// <summary>
    /// Extracts the EXPRESSION half of a conditional property value - the port of
    /// <c>_of_GetPropExp(prop)</c> (<c>n_cst_dwsvc.sru</c>, the <c>_of_getpropexp</c> body).
    /// </summary>
    /// <param name="prop">
    /// A raw property value, possibly in the <c>literal~texpression"</c> conditional form.
    /// </param>
    /// <returns>
    /// The expression with its TRAILING DOUBLE QUOTE REMOVED, or <paramref name="prop"/> unchanged when
    /// it carries no tab and is therefore not conditional.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE TRAILING CHARACTER IS DROPPED ON PURPOSE, AND THE ORACLE SAYS SO. Its own comment reads
    /// 取表达式（排除尾部'"'） - "take the expression, excluding the trailing quote" - and the length
    /// arithmetic <c>Len(prop) - nPos - 1</c> is one shorter than the remainder for exactly that
    /// reason. This is the opposite operation to
    /// <see cref="GetDataWindowObjectProperty"/>'s, which ADDS a leading quote: that one is building an
    /// <c>Evaluate</c> argument, this one is extracting an expression to evaluate directly.
    /// </para>
    /// <para>
    /// A ZERO-LENGTH RESULT ANSWERS THE EMPTY STRING RATHER THAN FAULTING, which is what PowerScript's
    /// <c>Mid</c> does for a non-positive length. It is reachable: a value that ends with a tab has
    /// nothing after it to take.
    /// </para>
    /// </remarks>
    protected static string GetPropertyExpression(in string prop)
    {
        // _of_getpropexp: nPos = Pos(prop,"~t")
        int tab = prop.IndexOf(PropertyExpressionSeparator, StringComparison.Ordinal);
        if (tab < 0)
        {
            // _of_getpropexp: else return prop
            return prop;
        }

        // _of_getpropexp: return Mid(prop,nPos + 1,Len(prop) - nPos - 1)
        int start = tab + 1;
        int length = prop.Length - start - 1;

        return length <= 0 ? string.Empty : prop.Substring(start, length);
    }

    /// <summary>
    /// Splits text on a multi-character delimiter into a one-based array - the port of
    /// <c>_of_SplitString(src, delimiter, ref dstArray, ignoreEmpty)</c>
    /// (<c>n_cst_dwsvc.sru:L455-L478</c>).
    /// </summary>
    /// <param name="src">
    /// The text to split. Taken BY VALUE and consumed as the walk proceeds, exactly as the oracle
    /// declares it - <c>string src</c> with no <c>readonly</c>, unlike every other parameter on the
    /// signature.
    /// </param>
    /// <param name="delimiter">The delimiter. An EMPTY delimiter answers <c>0</c> and writes nothing.</param>
    /// <param name="dstArray">
    /// Receives the pieces. REPLACED WHOLESALE rather than appended to [<c>:L462</c>], so a caller
    /// cannot accumulate across two calls.
    /// </param>
    /// <param name="ignoreEmpty">
    /// When <see langword="true"/>, empty pieces are dropped; when <see langword="false"/>, they are
    /// kept. <c>n_cst_dwsvc_contextmenu.sru:L952</c> passes <see langword="false"/>, so a blank
    /// clipboard line is a real value that will be pasted.
    /// </param>
    /// <returns>
    /// The number of pieces, which for a one-based array IS the last valid index - <c>UpperBound</c> at
    /// <c>:L477</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A TRAILING DELIMITER PRODUCES NO TRAILING EMPTY PIECE, and that asymmetry is the oracle's. The
    /// loop appends the text BEFORE each delimiter and the tail is appended only
    /// <c>if src &lt;&gt; ""</c> [<c>:L473</c>] - which is not governed by
    /// <paramref name="ignoreEmpty"/> at all. So <c>"a\r\nb"</c> and <c>"a\r\nb\r\n"</c> both answer two
    /// pieces, and the empty string answers ZERO pieces even with
    /// <paramref name="ignoreEmpty"/> <see langword="false"/>. That zero is what
    /// <c>n_cst_dwsvc_contextmenu.sru:L953</c> converts into <c>E_INVALID_ARGUMENT</c>.
    /// </para>
    /// <para>
    /// STATIC BECAUSE IT TOUCHES NO HOST. It is the only member in this helper block that reads nothing
    /// from the DataWindow, and saying so in the signature keeps it usable - and testable - with no
    /// attachment.
    /// </para>
    /// </remarks>
    protected static int SplitString(
        string src,
        in string delimiter,
        ref string[] dstArray,
        in bool ignoreEmpty)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(delimiter);

        // :L459-L460  nLenDelimiter = Len(delimiter) / if nLenDelimiter = 0 then return 0 - and note
        // this happens BEFORE dstArray is replaced, so an empty delimiter leaves the caller's array
        // untouched rather than emptying it.
        int delimiterLength = delimiter.Length;
        if (delimiterLength == 0)
        {
            return 0;
        }

        // :L462  dstArray = emptyArray
        List<string> pieces = [];

        // :L464-L472  the walk. `remaining` is the oracle's `src`, reassigned as it consumes.
        string remaining = src;
        int pos = remaining.IndexOf(delimiter, StringComparison.Ordinal);
        while (pos >= 0)
        {
            // :L466  str = Left(src,nPos - 1) - the text before the delimiter, empty when the
            // delimiter is at the start.
            string piece = remaining[..pos];

            // :L467-L469  if str <> "" or Not ignoreEmpty then append
            if (piece.Length > 0 || !ignoreEmpty)
            {
                pieces.Add(piece);
            }

            // :L470  src = Mid(src,nPos + nLenDelimiter) - one-based Mid start (pos + 1) plus the
            // delimiter length, which is the zero-based index (pos + delimiterLength).
            remaining = remaining[(pos + delimiterLength)..];
            pos = remaining.IndexOf(delimiter, StringComparison.Ordinal);
        }

        // :L473-L475  the tail, appended only when non-empty REGARDLESS of ignoreEmpty.
        if (remaining.Length > 0)
        {
            pieces.Add(remaining);
        }

        dstArray = [.. pieces];

        // :L477  return UpperBound(dstArray)
        return dstArray.Length;
    }

    /// <summary>
    /// Enumerates the DataWindow's object names, optionally filtered by object type and by band - the
    /// port of <c>_of_GetObjectNames(ref names, sType, band)</c> (<c>n_cst_dwsvc.sru:L669-L745</c>).
    /// </summary>
    /// <param name="names">Receives the names, REPLACED WHOLESALE [<c>:L695</c>].</param>
    /// <param name="sType">
    /// The object type to keep, or the EMPTY STRING for no type filter - the empty string is the
    /// "unfiltered" marker, not a type that matches nothing [<c>:L706</c>].
    /// </param>
    /// <param name="band">The band to keep, or the empty string for no band filter [<c>:L711</c>].</param>
    /// <returns>The number of names, which for a one-based array is the last valid index.</returns>
    /// <remarks>
    /// <para>
    /// THE FIRST FILTER IS NOT A FILTER BUT AN EXISTENCE TEST. Before either optional filter, every
    /// candidate must answer a readable <c>.Band</c> [<c>:L700-L705</c>]: an object whose band reads
    /// back as <c>"!"</c> or <c>"?"</c> is dropped. That is how the DataWindow-level pseudo-objects in
    /// the <c>DataWindow.Objects</c> list are excluded without naming any of them.
    /// </para>
    /// <para>
    /// THE BAND IS DESCRIBED TWICE WHEN A BAND FILTER IS SUPPLIED - once for the existence test and
    /// again for the comparison [<c>:L712</c>] - and the redundancy is preserved rather than hoisted,
    /// because <c>Describe</c> is observable: a recording of the ported call sequence has to match the
    /// oracle's, and a host may legitimately answer differently on a second read.
    /// </para>
    /// <para>
    /// THE TAIL BLOCK IS DUPLICATED IN THE ORACLE AND THE DUPLICATION IS FAITHFUL, NOT COPIED BY
    /// ACCIDENT. <c>DataWindow.Objects</c> is TAB-SEPARATED WITHOUT A TRAILING TAB, so the loop
    /// [<c>:L698-L721</c>] consumes every name that IS followed by a tab and the final name falls out
    /// of the loop as the remainder, which <c>:L722-L742</c> then re-tests with the same three
    /// conditions. Rewriting the two blocks as one would be a behaviour-preserving tidy-up in this
    /// case, but it would also erase the shape a reader must recognise to see that the LAST object is
    /// handled at all - and a list of exactly one object exercises only the tail block.
    /// </para>
    /// </remarks>
    protected int GetObjectNames(ref string[] names, in string sType, in string band)
    {
        DataWindowServiceHost host = RequireHost();

        // :L693  sObjString = #DataWindow.Describe("DataWindow.Objects")
        string objString = host.Describe("DataWindow.Objects");

        // :L695  names = emptyArray
        List<string> collected = [];

        // :L697-L721  the loop over every tab-terminated name.
        int pos = objString.IndexOf(ObjectNameSeparator, StringComparison.Ordinal);
        while (pos >= 0)
        {
            // :L699  sName = Left(sObjString,nPos - 1)
            string name = objString[..pos];

            if (MatchesObjectFilters(host, name, sType, band))
            {
                // :L717  names[UpperBound(names) + 1] = sName - the one-based append idiom.
                collected.Add(name);
            }

            // :L719-L720  sObjString = Mid(sObjString,nPos + 1) then re-scan.
            objString = objString[(pos + 1)..];
            pos = objString.IndexOf(ObjectNameSeparator, StringComparison.Ordinal);
        }

        // :L722-L742  the tail: the final name carries no trailing tab.
        if (objString.Length > 0 && MatchesObjectFilters(host, objString, sType, band))
        {
            // :L740
            collected.Add(objString);
        }

        names = [.. collected];

        // :L744  return UpperBound(names)
        return names.Length;
    }

    /// <summary>
    /// Enumerates the DataWindow's object names filtered by object type - the port of the two-argument
    /// <c>_of_GetObjectNames(ref names, sType)</c> (<c>n_cst_dwsvc.sru:L747</c>).
    /// </summary>
    /// <param name="names">Receives the names.</param>
    /// <param name="sType">The object type to keep.</param>
    /// <returns>The number of names.</returns>
    /// <remarks>A one-line delegation in the oracle: <c>_of_GetObjectNames(ref names,sType,"")</c>.</remarks>
    protected int GetObjectNames(ref string[] names, in string sType)
    {
        // :L747
        return GetObjectNames(ref names, sType, string.Empty);
    }

    /// <summary>
    /// Enumerates ALL of the DataWindow's object names - the port of the one-argument
    /// <c>_of_GetObjectNames(ref names)</c> (<c>n_cst_dwsvc.sru:L750</c>).
    /// </summary>
    /// <param name="names">Receives the names.</param>
    /// <returns>The number of names.</returns>
    /// <remarks>
    /// A one-line delegation in the oracle: <c>_of_GetObjectNames(ref names,"","")</c>. This is the
    /// arity both column-auto-width passes use [<c>n_cst_dwsvc_contextmenu.sru:L1100</c>,
    /// <c>:L1284</c>], which is why they filter by band and type THEMSELVES afterwards rather than
    /// asking for a filtered list - and the order of their own guards is observable.
    /// </remarks>
    protected int GetObjectNames(ref string[] names)
    {
        // :L750
        return GetObjectNames(ref names, string.Empty, string.Empty);
    }

    /// <summary>
    /// Builds a column's DISPLAY-to-DATA value map - the port of
    /// <c>_of_GetColumnValueMap(colName)</c> (<c>n_cst_dwsvc.sru:L561-L664</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns>
    /// A NEWLY CREATED map, keyed by display value. NEVER <see langword="null"/> and never shared: the
    /// oracle creates it at <c>:L585</c> before any guard and returns it from every exit, including the
    /// three that return it EMPTY [<c>:L587</c>, <c>:L598</c>, <c>:L599</c>]. An empty map therefore
    /// means "no translation is available", not "failure".
    /// </returns>
    /// <remarks>
    /// <para>
    /// AN <see cref="OrderedMap"/> AND NOT A DICTIONARY, because insertion order is observable (AAP
    /// 0.2.1.3 Correction 2) - the check-box branch below seeds five keys in a fixed order and a
    /// positional read of the result is a legitimate use of the ported container.
    /// </para>
    /// <para>
    /// THREE BRANCHES, SELECTED BY PROBING PROPERTIES IN A FIXED ORDER:
    /// </para>
    /// <para>
    /// 1. DDDW [<c>:L590-L636</c>]. The child DataWindow's display and data columns are read by name,
    /// their column types converted, and each row contributes one entry. Both the display and the data
    /// value are read through a SIX-ARM type switch and both are stringified - only the string arm
    /// escapes a <c>String(...)</c> wrapper, and only because it is already text. A NULL display value
    /// or a NULL data value SKIPS the row [<c>:L617</c>, <c>:L633</c>] rather than storing a null.
    /// </para>
    /// <para>
    /// 2. CHECK BOX [<c>:L639-L648</c>]. FIVE literal keys are seeded, and they are asymmetric: THREE
    /// spellings map to the on value (是, Y and √) but only TWO map to the off value (否 and N).
    /// There is no √-equivalent for off. That asymmetry is the oracle's and is reproduced - a paste of
    /// a tick character sets the box, while there is no character that clears it by the same route.
    /// The raw <c>Describe</c> answers are stored WITHOUT sentinel normalisation, so an unreadable
    /// check-box property stores the sentinel text as the mapped value.
    /// </para>
    /// <para>
    /// 3. CODE TABLE [<c>:L650-L659</c>]. <c>GetValue</c> is walked from index <c>1</c> until it answers
    /// the empty string, and each entry is split on its tab. AN ENTRY WITH NO TAB IS SKIPPED ENTIRELY
    /// [<c>:L654</c>] - it is not stored with an empty value - and the walk still advances, so one
    /// malformed entry does not truncate the table.
    /// </para>
    /// <para>
    /// <c>Set</c> AND NOT <c>Add</c>, at every one of the four store sites. The oracle writes
    /// <c>map.Set</c> throughout, which OVERWRITES a duplicate key, whereas <c>Add</c> FAILS on one -
    /// so a code table with two entries sharing a display value keeps the LAST, and swapping in
    /// <c>Add</c> would keep the first and silently change which value a paste resolves to.
    /// </para>
    /// <para>
    /// THE TEMPORAL RENDERINGS ARE SINGLE-SOURCED THROUGH THE THREE VALIDATORS rather than restated
    /// here, for the reason <c>Expressions/MacroInvoker.cs</c> gives for the same choice: a value that
    /// arrives back through a paste is re-parsed by those same validators, so the format used to write
    /// a map value and the format used to read it must be one decision, not two.
    /// </para>
    /// </remarks>
    protected OrderedMap GetColumnValueMap(in string colName)
    {
        DataWindowServiceHost host = RequireHost();

        // :L585  map = Create n_map - BEFORE the guards, so every exit has a map to return.
        OrderedMap map = new();

        // :L587  if colName = "" or colName = "!" or colName = "?" then return map
        if (colName.Length == 0
            || string.Equals(colName, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(colName, UndeterminedValueSentinel, StringComparison.Ordinal))
        {
            return map;
        }

        // :L589-L590  sProp = Describe(colName+".dddw.name") / if sProp <> "!" and sProp <> "?"
        string prop = host.Describe(colName + ".dddw.name");
        if (!IsUnreadable(prop))
        {
            PopulateFromDropDownDataWindow(host, colName, map);
            return map;
        }

        // :L638-L639  sProp = Describe(colName+".edit.style") / if sProp = "checkbox"
        prop = host.Describe(colName + ".edit.style");
        if (string.Equals(prop, "checkbox", StringComparison.Ordinal))
        {
            // :L641-L642  the on value under three spellings.
            string on = host.Describe(colName + ".checkbox.on");
            _ = map.Set("是", on);
            _ = map.Set("Y", on);
            _ = map.Set("√", on);

            // :L646-L648  the off value under two. THERE IS NO THIRD SPELLING - see the remarks.
            string off = host.Describe(colName + ".checkbox.off");
            _ = map.Set("否", off);
            _ = map.Set("N", off);

            return map;
        }

        // :L650-L659  the code-table walk.
        long index = 1L;
        string value = host.GetValue(colName, index);
        while (value.Length > 0)
        {
            // :L653-L656  split on the tab; an entry with no tab is skipped rather than stored.
            int tab = value.IndexOf(PropertyExpressionSeparator, StringComparison.Ordinal);
            if (tab > 0)
            {
                _ = map.Set(value[..tab], value[(tab + 1)..]);
            }

            // :L657-L658
            index++;
            value = host.GetValue(colName, index);
        }

        // :L663
        return map;
    }

    /// <summary>
    /// The DDDW branch of <see cref="GetColumnValueMap"/> (<c>n_cst_dwsvc.sru:L592-L636</c>), factored
    /// out so the three-branch selection above stays legible.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="colName">The column name.</param>
    /// <param name="map">The map to populate.</param>
    private void PopulateFromDropDownDataWindow(
        DataWindowServiceHost host,
        string colName,
        OrderedMap map)
    {
        // :L591 carries a COMMENTED-OUT auto-retrieve guard,
        //       `//if IsFailed(of_AutoRetrieveDDDW(colName)) then return map`, which is carried across
        //       as dormant and NOT revived (constraint C-B): reviving it would make this member
        //       retrieve data as a side effect of being asked for a translation table.

        // :L592  #DataWindow.GetChild(colName,ref dwc) - the return code is DISCARDED.
        IDataWindowChild? child = null;
        _ = host.GetChild(colName, ref child);

        // :L593  if IsValidObject(dwc) - validity is established on the OBJECT, not on the code.
        if (!Predicates.IsValidObject(child))
        {
            return;
        }

        // :L594-L597
        string displayColumn = host.Describe(colName + ".dddw.displaycolumn");
        string dataColumn = host.Describe(colName + ".dddw.datacolumn");
        string displayColType = child!.Describe(displayColumn + ".coltype");
        string dataColType = child.Describe(dataColumn + ".coltype");

        // :L598-L599  both guards test THREE values - the two sentinels AND the empty string, which is
        // one more than the oracle's other sentinel tests. Preserved as written.
        if (displayColType.Length == 0 || IsUnreadable(displayColType))
        {
            return;
        }

        if (dataColType.Length == 0 || IsUnreadable(dataColType))
        {
            return;
        }

        long displayType = ConvertColumnType(displayColType);
        long dataType = ConvertColumnType(dataColType);

        // :L600-L635
        long rowCount = child.RowCount();
        for (long row = 1L; row <= rowCount; row++)
        {
            // :L602-L616  the display value, read through the six-arm switch.
            string? display = ReadChildItemAsText(child, row, displayColumn, displayType);

            // :L617  if IsNull(sDispVal) then continue
            if (display is null)
            {
                continue;
            }

            // :L618-L632  the data value, read through the same six-arm switch.
            string? data = ReadChildItemAsText(child, row, dataColumn, dataType);

            // :L633  if IsNull(aVal) then continue
            if (data is null)
            {
                continue;
            }

            // :L634  map.Set(sDispVal,aVal) - Set, not Add. See the remarks on GetColumnValueMap.
            _ = map.Set(display, data);
        }
    }

    /// <summary>
    /// Reads one child-DataWindow item as text through the six-arm column-type switch the oracle uses
    /// twice (<c>n_cst_dwsvc.sru:L603-L616</c> for the display value, <c>:L619-L632</c> for the data
    /// value).
    /// </summary>
    /// <param name="child">The child DataWindow.</param>
    /// <param name="row">The one-based row.</param>
    /// <param name="column">The column name.</param>
    /// <param name="colType">The converted <c>COL_TYPE_*</c> value.</param>
    /// <returns>
    /// The rendered text, or <see langword="null"/> when the item is null - which the callers turn into
    /// a skipped row.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ONE HELPER FOR BOTH SWITCHES, WHICH ARE TEXTUALLY IDENTICAL IN THE ORACLE apart from the local
    /// they assign. Their only real difference is the DECLARED TYPE of that local - <c>string</c> for
    /// the display value and <c>any</c> for the data value - and since every arm of the data switch
    /// assigns text anyway, collapsing them loses nothing observable.
    /// </para>
    /// <para>
    /// THE DEFAULT ARM IS NULL, NOT THE EMPTY STRING. The oracle's <c>choose case</c> has SIX arms and
    /// no <c>case else</c>, so an unrecognised column type leaves the local at its initial value - and
    /// for both a <c>string</c> and an <c>any</c> that is null, which the very next line treats as a
    /// row to skip. Answering the empty string here would store an entry the oracle does not store.
    /// </para>
    /// </remarks>
    private static string? ReadChildItemAsText(
        IDataWindowChild child,
        long row,
        string column,
        long colType)
    {
        return colType switch
        {
            // :L604-L605 / :L620-L621  already text, so no String() wrapper.
            COL_TYPE_STRING => child.GetItemString(row, column),

            // :L606-L607 / :L622-L623  String(decimal)
            COL_TYPE_DECIMAL => child.GetItemDecimal(row, column)?.ToString(CultureInfo.InvariantCulture),

            // :L608-L609 / :L624-L625  String(number) - a double, despite the arm's name.
            COL_TYPE_INTEGER => child.GetItemNumber(row, column)?.ToString(CultureInfo.InvariantCulture),

            // :L610-L611 / :L626-L627  String(datetime), single-sourced through the validator.
            COL_TYPE_DATETIME => child.GetItemDateTime(row, column) is DateTime moment
                ? DateTimeValidator.FormatExpressionValue(moment)
                : null,

            // :L612-L613 / :L628-L629  String(date)
            COL_TYPE_DATE => child.GetItemDate(row, column) is DateOnly day
                ? DateValidator.FormatValue(day)
                : null,

            // :L614-L615 / :L630-L631  String(time)
            COL_TYPE_TIME => child.GetItemTime(row, column) is TimeOnly moment
                ? TimeValidator.Format(moment)
                : null,

            // No `case else` in the oracle: the local keeps its initial null.
            _ => null,
        };
    }

    /// <summary>
    /// The three-part candidate test <see cref="GetObjectNames"/> applies, in the oracle's order
    /// (<c>n_cst_dwsvc.sru:L700-L715</c> in the loop, repeated verbatim at <c>:L723-L738</c> for the
    /// tail).
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="name">The candidate object name.</param>
    /// <param name="sType">The type filter, or the empty string for none.</param>
    /// <param name="band">The band filter, or the empty string for none.</param>
    /// <returns><see langword="true"/> when the name survives all three tests.</returns>
    /// <remarks>
    /// FACTORED FROM THE ORACLE'S DUPLICATED BLOCK, WHICH IS THE ONE PLACE THIS PORT DOES NOT MIRROR
    /// THE ORACLE'S SHAPE. The two blocks are textually identical apart from the variable they test, so
    /// a single helper cannot diverge from itself the way two copies can - and the call ORDER, which is
    /// what is observable through <c>Describe</c>, is unchanged.
    /// </remarks>
    private static bool MatchesObjectFilters(
        DataWindowServiceHost host,
        string name,
        string sType,
        string band)
    {
        // :L700-L705  the existence test: an unreadable band drops the candidate.
        string bandOfName = host.Describe(name + ".Band");
        if (IsUnreadable(bandOfName))
        {
            return false;
        }

        // :L706-L710  the optional type filter.
        if (sType.Length > 0
            && !string.Equals(host.Describe(name + ".Type"), sType, StringComparison.Ordinal))
        {
            return false;
        }

        // :L711-L715  the optional band filter - which describes .Band a SECOND time rather than
        // reusing the value read above. Preserved; see the remarks on GetObjectNames.
        return band.Length == 0
            || string.Equals(host.Describe(name + ".Band"), band, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a <c>Describe</c> answer is one of the two DataWindow sentinels - the port of the
    /// <c>= "!" or = "?"</c> pair the oracle writes at a dozen sites.
    /// </summary>
    /// <param name="value">The answer to test.</param>
    /// <returns><see langword="true"/> when the property was not readable.</returns>
    private static bool IsUnreadable(string value)
    {
        return string.Equals(value, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(value, UndeterminedValueSentinel, StringComparison.Ordinal);
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
