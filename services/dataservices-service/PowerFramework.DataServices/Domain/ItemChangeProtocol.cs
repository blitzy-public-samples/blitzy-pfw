// ==================================================================================================
//  ItemChangeProtocol - THE {0,1,2,3} ITEM-CHANGE ALPHABET AND ITS MICRO-PROTOCOL
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                   :L182-L253  the `ondwnitemchange` event body, `end event` at :L254
//                   :L211-L251  the outer dispatch, FOUR arms
//                   :L231-L244  the inner coercion dispatch, SIX arms and NO default
//                   :L256-L293  `ondoitemchange`, the semantic handler this protocol invokes,
//                               including the dormant commented byte-length check at :L280-L290
//
//  The refactor plan (AAP 0.6.1.5) calls this routine "the single most intricate behaviour in the
//  in-scope set", and the failure mode is what makes it worth the length of this comment: a mistake
//  here shows up as a corrupted cell value or a validation error routed to the wrong place, neither
//  of which a row-count assertion or a "did it return a number" assertion can see. Every behavioural
//  claim below therefore carries the ws_objects/** locator it came from, and the parity matrix in the
//  sibling test project drives one case per arm.
//
//  ORACLE STATUS  se_cst_dw.sru is READ ONLY and is the behavioural oracle for parity testing, never
//                 an edit target (constraint C-C). It is also the ONLY specification: logfile.md
//                 stops at framework 3.0.7.2062 (2022-04-14) while the commit history runs years
//                 later, and the two PowerBuilder build definitions ws_objects/pfw.pbl.src/
//                 project.srj and p_pfw.srj contradict each other and both name
//                 pfw.utility.imgcodec.pbl, which exists nowhere in the repository, so neither would
//                 build as written. Nothing else in the tree can adjudicate a behavioural question.
//
//                 The source object declares ZERO PBNI native bindings, so this is a pure logic port
//                 with no native substitution problem anywhere in it.
//
//  ================================================================================================
//  FOUR ALPHABETS NOW SHARE THE NUMERALS 1 AND 2. THIS FILE OWNS THE FOURTH.
//  ================================================================================================
//  This is the single most dangerous confusion available in this service, because every one of the
//  four is a small integer, three of them use 1 to mean something, and conflating any two produces a
//  plausible-looking wrong answer rather than an error. The set, in full:
//
//    (a) VetoResult                     - shared/PowerFramework.Shared.Eventful/VetoResult.cs
//                                         Continue = 0, PreventOnce = 1, PreventDeep = 2.
//                                         The broker's TRI-VALUED veto. Flattening it to a boolean
//                                         silently converts a deep prevention into a shallow one.
//    (b) EventBroker.OnException        - shared/PowerFramework.Shared.Eventful/EventBroker.cs
//                                         1 = prevent, 2 = continue, anything else = rethrow.
//                                         Note that 1 and 2 mean the OPPOSITE things here that they
//                                         mean in (a): 2 continues, where in (a) 2 is the strongest
//                                         possible prevention.
//    (c) The RetCode PREVENT convention - shared/PowerFramework.Shared.Kernel/RetCode.cs
//                                         OK/SUCCESS/ALLOW = 0, PREVENT = 1, FAILED = -1,
//                                         CANCELED/CANCELLED = -2. Tested through
//                                         Predicates.IsPrevented. Note that IsSucceeded is `>= 0`,
//                                         so a PREVENT reads as a SUCCESS in that algebra - which is
//                                         a fourth reason not to route item-change values through it.
//    (d) ItemChangeResult - THIS FILE   - Default = 0, TriggerValidationError = 1,
//                                         RestoreAndRejectText = 2, KeepValueNoFocusMove = 3.
//
//  AAP 0.6.1.5 states the requirement in terms: the alphabet "must be modelled as its own
//  enumeration on the wire, never mapped onto the return-code algebra". The author of
//  shared/PowerFramework.Contracts/Proto/dataservices.v1.proto has already done that on the wire
//  side - `enum ItemChangeResult` there carries these same four names and these same four values -
//  so the enum below is the IN-PROCESS HALF OF A MATCHED PAIR. Phase 6 of this file's brief requires
//  an explicit test asserting the two agree, because PowerFramework.Contracts.csproj declares zero
//  ProjectReference by design and therefore nothing enforces the agreement at compile time.
//
//  ================================================================================================
//  THE CASE-1 QUESTION, AND WHY THIS FILE DOES NOT FALL THROUGH
//  ================================================================================================
//  The refactor plan (AAP 0.6.1.5 and 0.4.2.5) describes `case 1` as "falling through" to `case 2`.
//  TAKEN LITERALLY THAT IS WRONG, and implementing it literally would be a behavioural regression -
//  it would restore the original value and item status before the validation-error event ran, and
//  that handler performs the restore itself [:L369-L379], so the restore would happen twice. The
//  evidence, all of it from the oracle:
//
//    1. :L212 is `case 1` with an EMPTY BODY; :L213 is `case 2` and carries the restore statements.
//    2. PowerScript `CHOOSE CASE` has Select-Case semantics. Arms are EXCLUSIVE and do not fall
//       through the way a C `switch` does; an arm with no statements executes nothing and control
//       leaves the construct.
//    3. DECISIVE PROOF OF AUTHORIAL INTENT, FROM THIS SAME FILE: the author uses the multi-value
//       form `case 1,3` at :L367, inside `ondwnitemvalidationerror`. Had 1 and 2 been meant to share
//       a body it would have been written `case 1,2`, exactly as :L367 was written.
//
//  So the purpose of the empty `case 1` is to EXCLUDE the value 1 from `case else`, which would
//  otherwise apply the type coercion and forcibly rewrite the result to 2 [:L250]. Without that
//  guard a 1 would be swallowed and `ondwnitemvalidationerror` would never be triggered, which is
//  precisely what the oracle's own comment at :L210 says returning 1 is FOR. The AAP's "falls
//  through" wording describes the source's visual shape - two adjacent arm labels with nothing
//  between them - rather than its executable semantics.
//
//  VERIFY-AGAINST-ORACLE ITEM, NOT A CLOSED QUESTION. The distinction is observable (whether value
//  and status are restored before the validation-error event fires), so a paired legacy/target
//  characterization recording is the arbiter (AAP 0.6.7). It is recorded as such rather than
//  presented as settled by reasoning alone, and the decision is documented here rather than merely
//  implemented, because constraint C-K requires that and AAP 0.1.4 makes the legacy source the only
//  specification. The repository already agrees in two other places: the wire enum's own comment in
//  dataservices.v1.proto states it, and the repository-root .editorconfig section keyed to this
//  exact file path states it again.
//
//  ================================================================================================
//  TWO DIFFERENT `Left(colType,5)` SWITCHES EXIST IN THIS LIBRARY AND THEY DISAGREE
//  ================================================================================================
//  Recorded here because harmonising them is an easy, silent, and wrong "cleanup" (C-B, C-K).
//
//      se_cst_dw.sru:L231-L244        THIS FILE'S SWITCH - a COERCION switch
//          "char","char("            -> SetItem(..., data)
//          "decim","real","numbe"    -> SetItem(..., Dec(data))          <-- "numbe" IS DECIMAL HERE
//          "long","ulong"            -> SetItem(..., Long(data))
//          "datet"                   -> SetItem(..., DateTime(data))
//          "date"                    -> SetItem(..., Date(data))
//          "time"                    -> SetItem(..., Time(data))
//          (no `case else`)                                              <-- NO DEFAULT ARM
//
//      n_cst_dwsvc.sru:L503-L518      A DIFFERENT SWITCH - `_of_convertcoltype`, a CLASSIFICATION
//          "char","char("            -> COL_TYPE_STRING
//          "numbe","long","ulong"    -> COL_TYPE_INTEGER                 <-- "numbe" IS INTEGER HERE
//          "decim","real"            -> COL_TYPE_DECIMAL
//          "datet"                   -> COL_TYPE_DATETIME
//          "date"                    -> COL_TYPE_DATE
//          "time"                    -> COL_TYPE_TIME
//          case else                 -> COL_TYPE_UNKNOWN                 <-- HAS A DEFAULT ARM
//
//  The two disagree about `numbe` and disagree about whether an unrecognised type has an outcome.
//  This file reproduces the FORMER, verbatim. A reader who "fixes" the inconsistency by routing
//  `numbe` through the integer conversion would silently truncate the fractional part of every
//  `number` column, and one who adds a default arm would start writing cells the oracle leaves
//  untouched. Both are behaviour changes dressed as consistency.
//
//  ================================================================================================
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ================================================================================================
//  It owns the alphabet, the ordered prologue, the outer four-arm dispatch and the inner six-arm
//  coercion. It owns NO state: the three legacy instance fields this routine reads and writes,
//
//      long    _nDisabledEvent                                          [se_cst_dw.sru:L88-L89]
//      boolean _bDoItemChange                                           [se_cst_dw.sru:L91-L92]
//      long    _nItemChangeRetCode                                      [se_cst_dw.sru:L95-L96]
//
//  belong to Domain/ValidationSession.cs, and are reached through IItemChangeSessionState below.
//  The three events it raises are `se_cst_dw`'s OWN declared events rather than ancestry events, so
//  they belong to Domain/DataWindowEventChain.cs and are reached through IItemChangeEventSink below.
//  Both seams are declared HERE rather than imported, because the consumer of an abstraction is the
//  right owner of it: this file states exactly what it needs, the two implementers satisfy it, and a
//  test double satisfies it without a live DataWindow (constraint C-H).
//
//  It reaches the DataWindow only through the Domain/DataWindowServiceHost.cs abstraction and the
//  five conversions published by Validators/. It contains NO Win32 call, NO DPI conversion, NO font
//  measurement, NO geometry and NO theming, so constraint C-D is satisfied structurally rather than
//  by review - and independently: a grep of se_cst_dw.sru for every presentational primitive returns
//  zero matches, so there was nothing presentational in this routine to leave out.
//
//  It produces NO dialog. se_cst_dw.sru carries exactly one live MessageBox/MessageBoxEx call, at
//  :L357 inside `ondwnitemvalidationerror`, which belongs to Domain/ValidationSession.cs. The one in
//  this file's territory, at :L286, is COMMENTED OUT. So no structured-error type is needed here.
//
//  ACCESSIBILITY: internal, BY DESIGN. The service's published surface is contract C-03, and
//  versioned contracts are the only permitted cross-service coupling (constraint C-A), so every type
//  here is an implementation detail behind that contract. PowerFramework.DataServices.csproj says so
//  directly: it names "the item change alphabet of Domain/ItemChangeProtocol.cs" among the internal
//  seams its tests must drive and records that "None is public API, and none should be made public
//  merely to be testable." The sibling test project reaches these members through the
//  InternalsVisibleTo grant, so `internal` costs the coverage gate nothing.
//
//  CONSTANT IDENTIFIERS ARE PRESERVED VERBATIM (AAP 0.4.5.3), AND THIS FILE HAS NONE TO PRESERVE.
//  The repository-root .editorconfig carries a section keyed to this exact file path that sets CA1707
//  and IDE1006 to none, so any preserved SCREAMING_SNAKE identifier could be declared plainly here.
//  The suppression scope is deliberately UNUSED, and that is a finding rather than an omission: the
//  oracle's four arms are LITERAL DIGITS in a `choose case` [:L212, :L213, :L223, :L226], not named
//  constants, so there is no legacy spelling to carry across. The enum member names below are
//  therefore idiomatic C#, chosen to match the wire enum in dataservices.v1.proto name for name -
//  which is the same conclusion that file's author reached and recorded. The one SCREAMING_SNAKE
//  identifier this file mentions, EID_ITEMCHANGE, is a USAGE and is declared by Domain/EventGate.cs,
//  whose own .editorconfig section covers it; CA1707 reports declarations only.
//
//  This file carries NO in-source analyzer suppression directive of any kind. If a build ever reports
//  CA1707 or IDE1006 here, the .editorconfig glob has stopped matching this path - that is a defect to
//  report and fix in .editorconfig, never to paper over by renaming an identifier.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE (AAP 0.8.5). The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment,
//  so none may be asserted. No member here is described as fast, optimised, or a hot path, and no
//  shape below is justified on performance grounds - each is justified by a locator instead. The
//  only quantitative requirement in scope is the coverage gate.
//
//  ================================================================================================
//  THE PRESERVED ODDITIES, LISTED ONCE SO THE SET IS VISIBLE (constraint C-B)
//  ================================================================================================
//  Each is annotated again at its point of reproduction, so a maintainer reading only that member
//  cannot mistake it for a defect in this port. NONE of them is repaired.
//
//    O-1  `case 1` is EMPTY - an exclusion guard, not a fall-through.                       [:L212]
//    O-2  `case 3` REWRITES the result to 1, so 3 is accepted but never returned.           [:L225]
//    O-3  The stash keeps the PRE-REWRITE value, so the validation-error event still sees 3, while
//         the caller of this routine sees 1. Stash and return DIVERGE.            [:L195 vs :L253]
//    O-4  `case else` FORCIBLY returns 2 regardless of what the coercion did.                [:L250]
//    O-5  The inner coercion switch has NO default arm: an unmatched column type writes nothing and
//         reports nothing.                                                                  [:L244]
//    O-6  The restore and the coercion are both gated on the earlier equality test, so an unequal
//         buffer is left alone by BOTH.                                            [:L216, :L229]
//    O-7  ONE-SIDED NULL FALLS TO "NOT EQUAL", because a PowerScript comparison against a null
//         operand yields null, which `if` treats as false.                             [:L198-L202]
//    O-8  The re-entrancy flag is RESTORED from a local, not set to false, because the call may have
//         been nested.                                                                      [:L196]
//    O-9  A dormant commented byte-length validation, carried across inert.            [:L280-L290]
//    O-10 A dead local assignment in the semantic handler.                                  [:L278]
// ==================================================================================================

using System.Globalization;

using PowerFramework.DataServices.Validators;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The four-value alphabet the DataWindow item-change chain speaks -
/// <c>{0, 1, 2, 3}</c> from <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L211-L251</c>.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS ITS OWN DOMAIN AND IS NEVER THE RETURN-CODE ALGEBRA. It shares nothing with
/// <c>PowerFramework.Shared.Kernel.RetCode</c> but the digits: there <c>1</c> is <c>PREVENT</c> and
/// <c>-1</c> is <c>FAILED</c>; here <c>1</c> triggers the validation-error event and <c>2</c> means
/// "the buffer has already been written, do not re-apply the edit text". It is equally not
/// <c>VetoResult</c> and not the exception-handler alphabet of <c>EventBroker.OnException</c>. See the
/// four-alphabets block in this file's header for why conflating any two of them is silent rather
/// than loud.
/// </para>
/// <para>
/// THE IN-PROCESS HALF OF A MATCHED PAIR. <c>enum ItemChangeResult</c> in
/// <c>shared/PowerFramework.Contracts/Proto/dataservices.v1.proto</c> carries these same four names
/// and these same four values. The agreement is asserted by test rather than by the compiler, because
/// <c>PowerFramework.Contracts.csproj</c> declares no <c>ProjectReference</c> by design.
/// </para>
/// <para>
/// ALL FOUR VALUES ARE DISTINCT AND NONE IS MERGED, ALIASED OR COLLAPSED. The underlying values are
/// exactly <c>0</c>, <c>1</c>, <c>2</c> and <c>3</c>, because they travel in serialized payloads, in
/// log records and in characterization recordings.
/// </para>
/// <para>
/// WHAT THE ROUTINE ACTUALLY RETURNS IS A SUBSET. <see cref="ItemChangeProtocol.OnDwnItemChange"/>
/// yields <see cref="Default"/> only from the gate short-circuit at <c>:L187</c>, and otherwise
/// <see cref="TriggerValidationError"/> or <see cref="RestoreAndRejectText"/>.
/// <see cref="KeepValueNoFocusMove"/> is a value a HANDLER RETURNS; the routine rewrites it to
/// <see cref="TriggerValidationError"/> at <c>:L225</c> and so never yields it. See oddity O-2 and
/// O-3 in the header.
/// </para>
/// </remarks>
internal enum ItemChangeResult
{
    /// <summary>
    /// <c>0</c> - and every value outside <c>{1, 2, 3}</c> - reaches <c>case else</c>
    /// [<c>se_cst_dw.sru:L226-L250</c>].
    /// </summary>
    /// <remarks>
    /// The default arm coerces the edit text by the first five characters of the column type
    /// [<c>:L231-L244</c>] but only when the earlier equality test held [<c>:L229</c>], fires the
    /// changed event [<c>:L247</c>], and then FORCIBLY REWRITES THE RESULT TO
    /// <see cref="RestoreAndRejectText"/> [<c>:L250</c>] so the DataWindow will not re-apply the
    /// current edit text over the buffer. <c>case else</c> catches any value outside
    /// <c>{1, 2, 3}</c>, not only zero - a handler returning <c>-1</c> or <c>42</c> lands here too.
    /// This member is also the value the gate short-circuit at <c>:L187</c> returns, where it means
    /// "continue, nothing prevented" rather than "coerce".
    /// </remarks>
    Default = 0,

    /// <summary>
    /// <c>1</c> - the EMPTY arm at <c>se_cst_dw.sru:L212</c>. Nothing runs, and <c>1</c> is returned
    /// exactly as the semantic handler produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RETURNING THIS TRIGGERS <c>OnDwnItemValidationError</c>, which is what the oracle's own
    /// comment at <c>:L210</c> says it is for. That handler then pre-sets its own result to <c>1</c>
    /// from the stash [<c>:L338-L340</c>] and performs the value-and-status restore itself
    /// [<c>:L369-L379</c>].
    /// </para>
    /// <para>
    /// NO RESTORE HAPPENS ON THIS ARM. A port that restored here - which is what reading the arm as
    /// falling through into <see cref="RestoreAndRejectText"/> would produce - would restore twice.
    /// See the case-1 block in this file's header for the full evidence, including the decisive
    /// <c>case 1,3</c> at <c>:L367</c>.
    /// </para>
    /// </remarks>
    TriggerValidationError = 1,

    /// <summary>
    /// <c>2</c> - restore the original value and the original item status
    /// [<c>se_cst_dw.sru:L219-L220</c>], BUT ONLY WHEN THE EARLIER EQUALITY TEST HELD
    /// [<c>:L216</c>].
    /// </summary>
    /// <remarks>
    /// The guard exists because, per the oracle's comment at <c>:L215</c>, the buffer value may
    /// already have been changed by the handler and must not be overwritten. This is also the value
    /// <see cref="Default"/> rewrites to at <c>:L250</c>, where it means "do not re-apply the edit
    /// text" rather than "restore" - one value, two jobs, and both are the oracle's.
    /// </remarks>
    RestoreAndRejectText = 2,

    /// <summary>
    /// <c>3</c> - keep the value, do not move focus, and REWRITE THE RESULT TO
    /// <see cref="TriggerValidationError"/> [<c>se_cst_dw.sru:L223-L225</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's comment at <c>:L224</c> reads <c>保留值，不切换焦点</c> - "keep the value, do not
    /// switch focus". The whole arm body is the single rewrite at <c>:L225</c>, so this is a value a
    /// handler RETURNS and never a value the event YIELDS.
    /// </para>
    /// <para>
    /// THE REWRITE DOES NOT ERASE IT. The stash was written at <c>:L195</c> with the PRE-REWRITE
    /// value, so <c>ondwnitemvalidationerror</c> can still observe <c>3</c> - and it does, at
    /// <c>:L338</c> and again at <c>:L369</c>, where the restore is suppressed precisely when the
    /// stashed code was <c>3</c>. A <c>3</c> that became a <c>1</c> therefore remains
    /// distinguishable downstream. See oddity O-3 in the header.
    /// </para>
    /// </remarks>
    KeepValueNoFocusMove = 3,
}

/// <summary>
/// The three pieces of per-session mutable state the item-change protocol reads and writes -
/// the seam onto <c>Domain/ValidationSession.cs</c>, which owns them.
/// </summary>
/// <remarks>
/// <para>
/// THE STATE IS NOT THIS FILE'S, AND THE SEAM IS DECLARED HERE ANYWAY. In the oracle all three are
/// private instance fields of one control
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L88-L96</c>), which is the natural
/// shape when the protocol and the state live in the same object. They are split apart in this port
/// because a stateless request boundary has nowhere to put them, so they become fields of a
/// server-held session correlated by id and opened and closed by dedicated calls (AAP 0.3.4).
/// </para>
/// <para>
/// The interface is declared by the CONSUMER rather than by the owner, which is what lets this file
/// state exactly the three members it needs out of the session's four
/// (<c>_bDwnItemValidationError</c> at <c>:L93-L94</c> is the fourth and belongs entirely to the
/// validation-error handler, so it is deliberately absent here), and what lets a test double drive
/// every arm of the protocol with no live DataWindow and no live session (constraint C-H).
/// </para>
/// <para>
/// AN IMPLEMENTATION MUST BE PLAIN FIELD STORAGE. Two of the three members are written and then read
/// back within a single invocation, and one of them is written for a LATER event to consume, so an
/// implementation that recomputed, cached, clamped or validated on the way through would break the
/// protocol rather than harden it.
/// </para>
/// </remarks>
internal interface IItemChangeSessionState
{
    /// <summary>
    /// The composable mask of suppressed events - the port of <c>long _nDisabledEvent</c>
    /// (<c>se_cst_dw.sru:L88-L89</c>).
    /// </summary>
    /// <value>
    /// A bitwise combination of <see cref="EventGate.EID_ROWFOCUSCHANGE"/>,
    /// <see cref="EventGate.EID_ITEMFOCUSCHANGE"/> and <see cref="EventGate.EID_ITEMCHANGE"/>. A set
    /// bit means the corresponding event is DISABLED, so a zero mask suppresses nothing.
    /// </value>
    /// <remarks>
    /// <para>
    /// READ-ONLY FROM HERE, DELIBERATELY. The item-change protocol only ever tests this mask
    /// [<c>:L187</c>]; the two mutators live on <see cref="EventGate"/> and are driven by
    /// <c>of_disableevent</c> [<c>:L110</c>] and <c>of_enableevent</c> [<c>:L111</c>]. Exposing a
    /// setter here would invite this routine to gate its own events, which the oracle never does.
    /// </para>
    /// <para>
    /// <see langword="uint"/> AND NOT <see langword="long"/>, matching <see cref="EventGate"/>
    /// exactly. PowerBuilder <c>ulong</c> is 32-bit, so the bit primitives that back the gate are
    /// declared over C# <see cref="uint"/>; widening here would not merely cost a cast, it would fail
    /// to compile against <see cref="EventGate.IsEventDisabled"/>. The wire carries the mask as
    /// <c>int64</c> on contract C-03, and that conversion belongs at the gRPC boundary rather than
    /// here - the same division <see cref="EventGate"/> records.
    /// </para>
    /// </remarks>
    uint DisabledEvent { get; }

    /// <summary>
    /// Whether execution is currently inside the semantic item-change handler - the port of
    /// <c>boolean _bDoItemChange</c> (<c>se_cst_dw.sru:L91-L92</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// SET AND THEN RESTORED, NEVER SET AND CLEARED. <c>:L192</c> saves the current value into a
    /// local, <c>:L193</c> sets it, and <c>:L196</c> assigns the SAVED value back. The distinction is
    /// load-bearing: the semantic handler may itself cause a nested item change, and a port that
    /// wrote <see langword="false"/> at <c>:L196</c> would clear an outer invocation's flag while
    /// that outer invocation was still running. See oddity O-8 in the file header.
    /// </para>
    /// <para>
    /// The flag ESCAPES this routine. <c>ondwnkillfocus</c> queues its deferred accept-text
    /// continuation only when the flag is clear [<c>:L387-L390</c>], so leaving it set would suppress
    /// that continuation for the remainder of the session.
    /// </para>
    /// </remarks>
    bool DoItemChange { get; set; }

    /// <summary>
    /// The item-change result stashed for the validation-error event to consume - the port of
    /// <c>long _nItemChangeRetCode</c> (<c>se_cst_dw.sru:L95-L96</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS VALUE ESCAPES THE ROUTINE THAT WRITES IT, AND THAT IS THE POINT.
    /// <see cref="ItemChangeProtocol.OnDwnItemChange"/> writes it at <c>:L195</c> and never reads it
    /// back. <c>ondwnitemvalidationerror</c> READS AND CLEARS it [<c>:L331-L332</c>], pre-sets its own
    /// result to <c>1</c> when the stashed code was <c>1</c> or <c>3</c> [<c>:L338-L340</c>], and
    /// suppresses its own restore when the stashed code was <c>3</c> [<c>:L369</c>]. The clearing
    /// belongs to that handler, not to this one.
    /// </para>
    /// <para>
    /// WHY THIS IS <see langword="long"/> AND NOT <see cref="ItemChangeResult"/> (constraint C-K).
    /// The oracle's field is <c>long</c> and it is assigned the semantic handler's return value
    /// UNCLASSIFIED, before the <c>choose case</c> has looked at it - so a handler that returns
    /// <c>-1</c> or <c>42</c> stashes <c>-1</c> or <c>42</c>. Those values reach <c>case else</c>
    /// [<c>:L226</c>] and are indistinguishable from <c>0</c> as far as the DISPATCH is concerned, but
    /// they are NOT indistinguishable in the stash, which the next event compares against <c>1</c>
    /// and <c>3</c> numerically. Narrowing this to the enum would therefore discard information the
    /// oracle keeps. Contract C-03 does model the field as the wire enum, and that projection belongs
    /// at the gRPC boundary - exactly where <see cref="EventGate"/> puts the equivalent width
    /// conversion - rather than being pushed down into the domain type.
    /// </para>
    /// <para>
    /// IT HOLDS THE PRE-REWRITE VALUE. When the handler returns <c>3</c> this is <c>3</c>, even though
    /// the routine goes on to return <c>1</c> [<c>:L225</c>, <c>:L253</c>]. Stash and return diverge,
    /// and the divergence is observable downstream. See oddity O-3 in the file header.
    /// </para>
    /// </remarks>
    long ItemChangeRetCode { get; set; }
}

/// <summary>
/// The three events <see cref="ItemChangeProtocol.OnDwnItemChange"/> raises - the seam onto
/// <c>Domain/DataWindowEventChain.cs</c>, which owns them.
/// </summary>
/// <remarks>
/// <para>
/// THESE THREE ARE NOT ON <see cref="DataWindowServiceHost"/>, AND THE REASON IS STRUCTURAL. The
/// eleven semantic events that host publishes come from further up the PowerBuilder ancestry chain.
/// These three are declared by <c>se_cst_dw</c> ITSELF - <c>ondwnchanging</c> at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L21</c>, <c>ondoitemchange</c> at
/// <c>:L24</c> and <c>ondoitemchanged</c> at <c>:L26</c> - so they belong to the ported event chain
/// rather than to the host contract, and putting them here would have given the host three members no
/// ancestor supplies.
/// </para>
/// <para>
/// EXACTLY THREE, AND NO FOURTH. Measured on the routine being ported: <c>:L194</c>, <c>:L207</c> and
/// <c>:L247</c> are the only <c>Event</c> raises inside <c>:L182-L253</c>. Adding a member no call
/// site raises would fabricate a hook the oracle does not have (constraint C-B) and would be one more
/// thing every test double has to fabricate for no behavioural gain (constraint C-H).
/// </para>
/// <para>
/// These are methods rather than C# <see langword="event"/> members for the same reason
/// <see cref="DataWindowServiceHost"/>'s are: a PowerBuilder event raised with the <c>Event</c>
/// keyword RETURNS A VALUE to its single raiser, which a multicast void-returning
/// <see langword="event"/> cannot express - and the value returned by <c>:L194</c> is the single most
/// load-bearing number in this file.
/// </para>
/// </remarks>
internal interface IItemChangeEventSink
{
    /// <summary>
    /// Raised before the edit text is written to the buffer, to validate it - the port of
    /// <c>Event OnDoItemChange(row,dwo,data)</c> (<c>se_cst_dw.sru:L194</c>, declared at <c>:L24</c>).
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column the change is addressed to.</param>
    /// <param name="data">
    /// The edit text the DataWindow is about to apply. The oracle's own description at <c>:L259</c>
    /// records that the value has NOT yet been written to the buffer at this point.
    /// </param>
    /// <returns>
    /// The raw, unclassified item-change code. Interpreted by
    /// <see cref="ItemChangeProtocol.Classify"/>; anything outside <c>{1, 2, 3}</c> reaches
    /// <c>case else</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see langword="long"/> AND NOT <see cref="ItemChangeResult"/>, because the oracle's event is
    /// declared <c>event type long ondoitemchange</c> [<c>:L24</c>] and <c>case else</c> [<c>:L226</c>]
    /// exists precisely to absorb values the alphabet does not name. Typing the return as the enum
    /// would imply a closed set that the oracle leaves open, and the open-ness is observable through
    /// the stash - see <see cref="IItemChangeSessionState.ItemChangeRetCode"/>.
    /// </para>
    /// <para>
    /// The legacy handler body is one line, <c>return Event ItemChanged(row,dwo,data)</c>
    /// [<c>:L292</c>], preceded by a dormant commented byte-length check
    /// [<c>:L280-L290</c>] - reproduced inert on <see cref="ItemChangeProtocol"/>, never revived.
    /// </para>
    /// </remarks>
    long OnDoItemChange(long row, IDataWindowObject dwo, string data);

    /// <summary>
    /// Raised, NESTED, when the buffer value turns out to have changed - the port of
    /// <c>Event OnDwnChanging(row,dwo,data)</c> (<c>se_cst_dw.sru:L207</c>, declared at <c>:L21</c>
    /// over <c>pbm_dwnchanging</c>).
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column the change is addressed to.</param>
    /// <param name="data">
    /// The RE-READ buffer value from <c>:L206</c>, not the incoming edit text. May be
    /// <see langword="null"/>: the oracle produces it with <c>String(dwo.Primary[row])</c>, and
    /// PowerScript's <c>String</c> of a null yields null, so a null cell arrives here as null. AAP
    /// 0.4.5.4 forbids collapsing that to an empty string.
    /// </param>
    /// <returns>
    /// The raw handler code, which <see cref="ItemChangeProtocol.OnDwnItemChange"/> DISCARDS - see
    /// the remarks.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE RETURN VALUE IS DISCARDED AT THE CALL SITE, AND THAT IS THE ORACLE'S CHOICE, NOT AN
    /// OVERSIGHT. <c>:L207</c> is a bare statement, unlike every other raise in this object, which
    /// are all consumed by the <c>= 1 then return 1</c> prevent convention. The value is nonetheless
    /// declared and returned here rather than typed <see langword="void"/>, because the handler
    /// genuinely produces one - its own body at <c>:L164-L174</c> returns <c>1</c> to prevent - and
    /// because a <see langword="void"/> signature would make the discard invisible instead of
    /// explicit. Acting on the value would ADD a prevention path the oracle does not have.
    /// </para>
    /// <para>
    /// This is an event raised from inside another event handler. The nesting is preserved as a
    /// nested synchronous call.
    /// </para>
    /// </remarks>
    long OnDwnChanging(long row, IDataWindowObject dwo, string? data);

    /// <summary>
    /// Raised after the data has genuinely changed and been written - the port of
    /// <c>Event OnDoItemChanged(row,dwo)</c> (<c>se_cst_dw.sru:L247</c>, declared at <c>:L26</c>).
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column the change was addressed to.</param>
    /// <remarks>
    /// <para>
    /// <see langword="void"/>, because the oracle declares it with no return type
    /// (<c>event ondoitemchanged ( long row, dwobject dwo )</c> at <c>:L26</c>, contrasted with the
    /// <c>event type long</c> form used at <c>:L24</c>) and the call site at <c>:L247</c> is a bare
    /// statement. The oracle's description at <c>:L298</c> records that the input HAS been written to
    /// the buffer by this point.
    /// </para>
    /// <para>
    /// ITS BODY CARRIES THE COLUMN-EXPRESSION COUPLING. The handler's first act is to invoke the
    /// column-expression service's own changed handler when that service is enabled
    /// [<c>:L313-L315</c>], then the broker trigger when the topic has a subscriber
    /// [<c>:L316-L318</c>], then the semantic changed event [<c>:L319</c>] - in that order. This is
    /// why disabling <see cref="EventGate.EID_ITEMCHANGE"/> also stops every bound column expression
    /// from recalculating, as the oracle warns at <c>:L43</c>: the gate at <c>:L187</c> returns before
    /// this raise can be reached.
    /// </para>
    /// </remarks>
    void OnDoItemChanged(long row, IDataWindowObject dwo);
}


/// <summary>
/// The item-change micro-protocol - the port of <c>ondwnitemchange</c>
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L182-L253</c>).
/// </summary>
/// <remarks>
/// <para>
/// Stateless by construction. Every collaborator arrives as an argument, so the type has no fields
/// and no static mutable state of any kind, which is what makes its tests order-independent
/// (constraint C-H). The per-session state lives on <see cref="IItemChangeSessionState"/>.
/// </para>
/// <para>
/// See this file's header for the four-alphabets warning, the case-1 evidence, the two disagreeing
/// column-type switches, and the ten preserved oddities O-1 to O-10.
/// </para>
/// </remarks>
internal static class ItemChangeProtocol
{
    // ==============================================================================================
    //  ONE-BASED INDEXING AUDIT                                                       (AAP 0.4.5.4)
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.4.5.4 names one-based to zero-based translation "the single most dangerous mechanical
    //  hazard in this refactor" and requires that every ported loop either go through a centralized
    //  one-based helper or be individually audited. THIS FILE IS AUDITED, AND THE RESULT IS THAT
    //  THERE IS NOTHING TO CONVERT:
    //
    //    * `row` is never the subject of arithmetic anywhere below. It is read from the event, passed
    //      unchanged to the buffer indexer, to GetItemStatus/SetItem/SetItemStatus, and to the three
    //      raises - and that is the whole of its use. It stays in the one-based numbering the oracle
    //      uses, which is also the numbering IDataWindowValueBuffer's indexer documents.
    //    * The column identifier is likewise passed through, never offset.
    //    * There is NO LOOP in this routine at all. The oracle's :L182-L253 contains none, and neither
    //      does this port, so the append idiom and the reverse-iteration hazard that AAP 0.4.5.4
    //      warns about cannot arise here.
    //
    //  Do not "normalise" `row` to zero-based on the way in. Contract C-03 carries it one-based, the
    //  host contract takes it one-based, and the oracle produces it one-based, so a rebase anywhere
    //  in this chain would be an off-by-one indistinguishable from a behavioural regression.
    // ==============================================================================================

    // ==============================================================================================
    //  THE DORMANT BYTE-LENGTH VALIDATION FROM `ondoitemchange`             se_cst_dw.sru:L280-L290
    //  --------------------------------------------------------------------------------------------
    //  ODDITY O-9. `ondoitemchange` [:L256-L293] is the semantic handler this protocol raises at
    //  :L194 through IItemChangeEventSink.OnDoItemChange. Its executable body is ONE LINE, :L292
    //  `return Event ItemChanged(row,dwo,data)`. Everything between :L280 and :L290 is COMMENTED OUT
    //  in the oracle, and AAP 0.6.1.5 requires it be "carried across as commented and inert, not
    //  revived." It is reproduced verbatim here, at the point this protocol raises the handler that
    //  contains it, so that a reader of this routine can see the validation that ALMOST happens:
    //
    //      /*if data <> "" then
    //          //检查字符串输入长度是否合法                    "check whether the input string length is legal"
    //          sProp = dwo.ColType
    //          if Left(sProp,5) = "char(" then
    //              nDBLimit = Long(Mid(sProp,6,Len(sProp) - 6))
    //              if LenA(data) > nDBLimit then
    //                  MessageBoxEx("超出最大允许的长度，" + String(nDBLimit) + "个字节(每汉字占2个字节)!",StopSign!)
    //                  return 2
    //              end if
    //          end if
    //      end if*/
    //
    //      The message reads "exceeds the maximum allowed length, <n> bytes (each Chinese character
    //      occupies 2 bytes)!" - which is what `LenA` measures: bytes in the ANSI encoding, not
    //      characters. Note that the declared width is parsed back out of the SAME ColType string the
    //      coercion switch reads only the first five characters of, which is one reason
    //      IDataWindowObject.ColType carries the raw text rather than a pre-parsed enum.
    //
    //  MUST NOT BE ACTIVATED (constraint C-B). Reviving it would reject edit text the oracle accepts,
    //  which is a behaviour change, and it would introduce the only dialog in this file's territory:
    //  se_cst_dw.sru carries exactly ONE live MessageBox/MessageBoxEx call, at :L357 in the
    //  validation-error handler, and the one above at :L286 is commented. Reviving it would also
    //  return 2 from the semantic handler, steering the outer dispatch into
    //  ItemChangeResult.RestoreAndRejectText for over-long input where the oracle steers it into
    //  `case else`.
    //
    //  ODDITY O-10, THE DEAD ASSIGNMENT. :L276 declares `string sColName,sProp` and :L275 declares
    //  `long nDBLimit`; :L278 then executes `sColName = dwo.Name`. That assignment is DEAD: `sColName`
    //  is never read anywhere in the handler, and `sProp` and `nDBLimit` are read only inside the
    //  commented block above. RECORDED DECISION: none of the three locals is reproduced, because a
    //  write-only local would raise an unused-value diagnostic and warnings are promoted to errors
    //  repository wide, and because reproducing a dead store has no observable effect to preserve -
    //  the only thing worth preserving is the KNOWLEDGE that :L278 is dead, which is why the locator
    //  is recorded here rather than the statement.
    // ==============================================================================================

    /// <summary>
    /// Runs the item-change micro-protocol for one changed cell - the port of the whole of
    /// <c>ondwnitemchange</c> (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L182-L253</c>).
    /// </summary>
    /// <param name="host">
    /// The DataWindow the change happened on. Reached only through the abstraction, never as a
    /// concrete control, so every arm below is drivable by a test double (constraint C-H).
    /// </param>
    /// <param name="session">
    /// The per-session state carrying the disabled-event mask, the re-entrancy flag and the stash.
    /// </param>
    /// <param name="events">The sink for the three events this routine raises.</param>
    /// <param name="row">
    /// The ONE-BASED row whose item changed. Passed through unchanged; see the indexing audit above.
    /// </param>
    /// <param name="dwo">The column the change is addressed to.</param>
    /// <param name="data">
    /// The edit text the DataWindow is about to apply, before it reaches the buffer.
    /// </param>
    /// <returns>
    /// <see cref="ItemChangeResult.Default"/> when the event is gated at <c>:L187</c>; otherwise
    /// <see cref="ItemChangeResult.TriggerValidationError"/> or
    /// <see cref="ItemChangeResult.RestoreAndRejectText"/>.
    /// <see cref="ItemChangeResult.KeepValueNoFocusMove"/> is never returned - it is rewritten at
    /// <c>:L225</c> - although it IS what the stash retains (oddity O-3).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="host"/>, <paramref name="session"/>, <paramref name="events"/>,
    /// <paramref name="dwo"/> or <paramref name="data"/> is <see langword="null"/>. These are
    /// structural guards on parameters the signature already declares non-nullable: none can fire for
    /// any input the oracle can produce, and failing fast on a contract violation rather than
    /// continuing is the posture this refactor preserves throughout (AAP 0.1.4). They add no error
    /// path to any reachable legacy behaviour.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE PROLOGUE IS CONTRACT. Gate, then snapshot value, then snapshot status, then
    /// the four-step re-entrancy dance, then the equality test, then the conditional re-read and
    /// nested raise. Nothing below may be reordered or hoisted: the status snapshot must be taken
    /// before the handler runs, the equality test compares a reading taken AFTER it, and the stash
    /// must be written before the dispatch can rewrite the code.
    /// </para>
    /// <para>
    /// The gated path returns <see cref="ItemChangeResult.Default"/>, and THAT ZERO IS NOT AN ERROR
    /// CODE - it is the continue value of the prevent convention the chain uses throughout, exactly as
    /// at the three sibling guard sites <c>:L124</c>, <c>:L130</c> and <c>:L176</c>.
    /// </para>
    /// </remarks>
    internal static ItemChangeResult OnDwnItemChange(
        DataWindowServiceHost host,
        IItemChangeSessionState session,
        IItemChangeEventSink events,
        long row,
        IDataWindowObject dwo,
        string data)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(data);

        // ------------------------------------------------------------------------------------------
        // :L187  if BitTest(_nDisabledEvent,EID_ITEMCHANGE) then return 0
        //
        // FIRST, AND NOTHING ELSE RUNS. No snapshot is taken, no handler is raised, the stash is left
        // exactly as the previous event left it, and the flag is not touched. The gate is also what
        // silently suppresses column-expression recalculation: the oracle warns of that coupling at
        // :L43, and the mechanism is this early return, which never reaches the :L247 raise whose
        // handler drives the expression service [:L313-L315].
        // ------------------------------------------------------------------------------------------
        if (EventGate.IsEventDisabled(session.DisabledEvent, EventGate.EID_ITEMCHANGE))
        {
            return ItemChangeResult.Default;
        }

        // ------------------------------------------------------------------------------------------
        // `Long(dwo.ID)` - RESOLVED ONCE. The oracle re-evaluates it at every addressing site
        // (:L190, :L219, :L220, :L233, :L235, :L237, :L239, :L241, :L243), and resolving it once here
        // is unobservable: `dwo` is a handle to one DataWindow object for the duration of one event,
        // so its identifier cannot change between those sites. Resolving once additionally removes
        // the possibility of two different identifiers being used inside a single invocation, which
        // the oracle's repetition permits but never exercises.
        //
        // NULL IS A REAL STATE AND IT IS NOT COLLAPSED TO ZERO. DataWindowObjectExtensions.ToColumnId
        // returns null when the identifier itself is null, and AAP 0.4.5.4 forbids defaulting it,
        // because column zero is a real column position - so a default would write to the wrong cell
        // silently. PowerScript reaches the same outcome by a different route: `Long(null)` is null,
        // and every addressing call below would receive that null and write nothing. The
        // `HasValue` guards on each addressing site reproduce that no-write outcome exactly, rather
        // than substituting an identifier or raising an error the oracle does not raise.
        // ------------------------------------------------------------------------------------------
        long? columnId = dwo.ColumnId();

        // :L189  aOrgValue = dwo.Primary[row]
        // The `any` snapshot, taken BEFORE the handler runs. AAP 0.4.5.2 maps `any` to `object?`, and
        // a null cell is preserved as null - the equality test below has an arm that depends on it.
        object? aOrgValue = dwo.Primary[row];

        // :L190  orgStatus = GetItemStatus(row,Long(dwo.ID),Primary!)
        // Taken at the same instant as the value, because ItemChangeResult.RestoreAndRejectText
        // restores BOTH together [:L219-L220] and restoring a value without its status would leave
        // the row claiming a modification state it no longer has. With no resolvable column the read
        // cannot be addressed, and NotModified is the value PowerBuilder yields for an item it cannot
        // resolve - so the degenerate path degrades exactly as the oracle does rather than throwing.
        ItemStatus orgStatus = columnId.HasValue
            ? host.GetItemStatus(row, columnId.Value, DwBuffer.Primary)
            : ItemStatus.NotModified;

        // ------------------------------------------------------------------------------------------
        // :L192-L196  THE FOUR-STEP RE-ENTRANCY DANCE. All four steps, in this order.
        //
        //     :L192  bDoItemChange = _bDoItemChange      save
        //     :L193  _bDoItemChange = true               set
        //     :L194  rtCode = Event OnDoItemChange(...)  raise
        //     :L195  _nItemChangeRetCode = rtCode        STASH
        //     :L196  _bDoItemChange = bDoItemChange      RESTORE, not clear
        //
        // ODDITY O-8: :L196 assigns the SAVED value back rather than false, because the raise at
        // :L194 may itself have caused a nested item change - writing false would clear an outer
        // invocation's flag while that outer invocation is still running. The flag also escapes this
        // routine: `ondwnkillfocus` queues its deferred accept-text continuation only when the flag
        // is clear [:L387-L390].
        //
        // THE STASH AT :L195 IS LOAD-BEARING ACROSS EVENTS AND IS NEVER READ BACK HERE. It is written
        // with the RAW, UNCLASSIFIED handler result - before the dispatch below has looked at it, and
        // therefore before the :L225 rewrite. `ondwnitemvalidationerror` reads and CLEARS it
        // [:L331-L332], pre-sets its own result to 1 when it was 1 or 3 [:L338-L340], and suppresses
        // its own restore when it was 3 [:L369]. Clearing belongs to that handler, not to this one:
        // a port that cleared the stash here would make the pre-set unreachable and would silently
        // convert every handler-driven validation error into a plain one.
        // ------------------------------------------------------------------------------------------
        bool bDoItemChange = session.DoItemChange;
        session.DoItemChange = true;
        long rtCode = events.OnDoItemChange(row, dwo, data);
        session.ItemChangeRetCode = rtCode;
        session.DoItemChange = bDoItemChange;

        // :L198-L202  the three-arm equality test. See IsBufferValueEqual for the null asymmetry.
        bool bEqual = IsBufferValueEqual(aOrgValue, dwo.Primary[row]);

        // ------------------------------------------------------------------------------------------
        // :L204-L208  THE CONDITIONAL RE-READ AND THE NESTED RAISE.
        //
        //     :L204  if Not bEqual then
        //     :L205    //取修改后的值                                  "take the modified value"
        //     :L206    data = String(dwo.Primary[row])
        //     :L207    Event OnDwnChanging(row,dwo,data)
        //     :L208  end if
        //
        // THE ORACLE REASSIGNS ITS OWN `data` PARAMETER AT :L206, so the nested handler at :L207 sees
        // the RE-READ buffer value and not the incoming edit text. That is modelled here as a local
        // seeded from the parameter, which keeps the reassignment visible while leaving the parameter
        // itself non-nullable at the boundary. The local is nullable because PowerScript's `String` of
        // a null yields null, so a null cell genuinely produces a null here (AAP 0.4.5.4 forbids
        // collapsing that to an empty string).
        //
        // WHERE THE REASSIGNMENT IS AND IS NOT OBSERVABLE: the only later reader of the value is the
        // coercion at :L233 and its five siblings, and that whole block is guarded by `if bEqual`
        // [:L229] - so when the re-read HAS happened the coercion never runs, and when the coercion
        // runs the re-read never happened. The coercion therefore always sees the original incoming
        // edit text. The reassignment is observable only through the :L207 handler, which is exactly
        // why it must not be optimised away into a separate throwaway variable that the switch cannot
        // see.
        //
        // THE :L207 RETURN VALUE IS DISCARDED, deliberately - see IItemChangeEventSink.OnDwnChanging.
        // Every other raise in this object is consumed by the `= 1 then return 1` prevent convention;
        // this one is a bare statement in the oracle, and acting on it would add a prevention path the
        // oracle does not have.
        // ------------------------------------------------------------------------------------------
        string? currentData = data;
        if (!bEqual)
        {
            currentData = ToLegacyString(dwo.Primary[row]);
            events.OnDwnChanging(row, dwo, currentData);
        }

        // ------------------------------------------------------------------------------------------
        // :L210  //*return 1触发OnDwnItemValidationError    "returning 1 triggers OnDwnItemValidationError"
        // :L211  choose case rtCode
        //
        // SWITCHED ON THE RAW CODE, exactly as the oracle switches on its `long rtCode`, so that this
        // block can be read line for line against :L211-L251. The case labels are written as the
        // named alphabet members cast to their underlying values, which are compile-time constants:
        // the numbers are therefore literally the oracle's 1, 2 and 3, and the names are visible at
        // the point of dispatch. FOUR ARMS, counted from the source.
        // ------------------------------------------------------------------------------------------
        switch (rtCode)
        {
            // --------------------------------------------------------------------------------------
            // :L212  case 1        <-- AN ARM WITH NO STATEMENTS
            //
            // ODDITY O-1, AND THE HEADLINE DECISION OF THIS FILE (constraint C-K). This arm is EMPTY
            // in the oracle and it DOES NOT FALL THROUGH into :L213. PowerScript `CHOOSE CASE` has
            // Select-Case semantics - arms are exclusive - so an arm with no statements executes
            // nothing and control leaves the construct. The arm exists to EXCLUDE the value 1 from
            // `case else` [:L226], which would otherwise coerce the edit text and forcibly rewrite
            // the result to 2, swallowing the 1 that :L210 says is what triggers the validation-error
            // event.
            //
            // DECISIVE PROOF OF AUTHORIAL INTENT, FROM THIS SAME FILE: the author writes the
            // multi-value form `case 1,3` at :L367. Had 1 and 2 been meant to share a body it would
            // have been written `case 1,2`. The refactor plan's "case 1 falls through to case 2"
            // wording (AAP 0.6.1.5, 0.4.2.5) describes the source's VISUAL SHAPE - two adjacent arm
            // labels with nothing between them - rather than its executable semantics, and
            // implementing it literally would perform the restore twice, because
            // `ondwnitemvalidationerror` performs it itself at :L369-L379.
            //
            // NOT A CLOSED QUESTION: the difference is observable, so a paired legacy/target
            // characterization recording is the arbiter (AAP 0.6.7). This is recorded as a
            // VERIFY-AGAINST-ORACLE item, not as settled by reasoning alone.
            //
            // NO RESTORE, NO COERCION, NO REWRITE, NO RAISE. `rtCode` leaves this arm as the 1 the
            // handler produced, and :L253 returns it unchanged.
            // --------------------------------------------------------------------------------------
            case (long)ItemChangeResult.TriggerValidationError:
                break;

            // --------------------------------------------------------------------------------------
            // :L213  case 2
            // :L214    //还原当前显示的不可编辑下拉值
            //          "restore the currently displayed non-editable dropdown value"
            // :L215    //*缓冲区的值可能已经被改变,防止覆盖
            //          "the buffer value may already have been changed; prevent overwriting it"
            // --------------------------------------------------------------------------------------
            case (long)ItemChangeResult.RestoreAndRejectText:
                // :L216  if bEqual then
                // ODDITY O-6. The guard is the whole reason the restore is safe: if the handler
                // already wrote the buffer, restoring the snapshot would DISCARD the handler's own
                // write. Removing the guard would look like a simplification and would silently
                // undo legitimate edits.
                if (bEqual && columnId.HasValue)
                {
                    // :L217-L218 and :L221 - THE DORMANT DDDW/DDLB GUARD, carried across inert
                    // (constraint C-B). Commented out in the oracle exactly as reproduced here, and
                    // reproduced because it records what :L214 means: the restore was originally
                    // scoped to DropDownDataWindow and DropDownListBox columns whose AllowEdit is
                    // "no", where the visible text is a display value the user cannot type into, so
                    // the buffer must be put back to the value that display was derived from. With
                    // the guard commented the restore applies to EVERY column type, which is the
                    // live behaviour and is what is implemented. Do not activate it.
                    //
                    //     //if Describe(dwo.Name + ".DDDW.AllowEdit") = "no" or &
                    //     //   Describe(dwo.Name + ".DDLB.AllowEdit") = "no" then //DDDW/DDLB
                    //             SetItem(row,Long(dwo.ID),aOrgValue)
                    //             SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)
                    //     //end if
                    //
                    // :L219  SetItem(row,Long(dwo.ID),aOrgValue)
                    // Binds the `object?` overload, matching the oracle's `any`. The return value is
                    // discarded because the oracle discards it.
                    host.SetItem(row, columnId.Value, aOrgValue);

                    // :L220  SetItemStatus(row,Long(dwo.ID),Primary!,orgStatus)
                    // Restored TOGETHER with the value and never separately: a value put back with a
                    // modified status still reaches the update contract as a pending change.
                    host.SetItemStatus(row, columnId.Value, DwBuffer.Primary, orgStatus);
                }

                break;

            // --------------------------------------------------------------------------------------
            // :L223  case 3
            // :L224    //保留值，不切换焦点          "keep the value, do not switch focus"
            // :L225    rtCode = 1
            //
            // ODDITY O-2: the arm body is that single rewrite, so 3 is ACCEPTED from the handler and
            // NEVER RETURNED - it collapses into 1 and the caller of this routine cannot observe it.
            // No restore and no coercion happen, which is what "keep the value" means.
            //
            // ODDITY O-3, THE SUBTLEST THING IN THIS FILE: the rewrite does NOT erase the 3. The
            // stash was written at :L195 with the PRE-REWRITE value, so `ondwnitemvalidationerror`
            // still sees 3 - and it uses it twice, at :L338 where 1 and 3 both pre-set its result,
            // and at :L369 where a stashed 3 SUPPRESSES its restore. So the value the stash holds and
            // the value this routine returns genuinely diverge for this one input, and both halves of
            // the divergence are observable. A port that rewrote the stash here, or that returned 3
            // here, would break a different downstream behaviour in each case.
            // --------------------------------------------------------------------------------------
            case (long)ItemChangeResult.KeepValueNoFocusMove:
                rtCode = (long)ItemChangeResult.TriggerValidationError;
                break;

            // --------------------------------------------------------------------------------------
            // :L226  case else       <-- reached by 0 AND by every value outside {1,2,3}
            // :L227    //检查值是否被修改              "check whether the value was modified"
            // :L228    //*缓冲区的值可能已经被改变,防止覆盖
            //          "the buffer value may already have been changed; prevent overwriting it"
            //
            // Three steps, in this order: coerce (guarded), raise, then force the result.
            // --------------------------------------------------------------------------------------
            default:
                // :L229  if bEqual then
                // ODDITY O-6 again, and for the same reason as :L216: if the handler already wrote
                // the buffer, coercing the edit text over it would discard that write.
                if (bEqual && columnId.HasValue)
                {
                    // :L230  //*此处不能调用AcceptText来应用数据，因为像CheckBox这种控件输入是没有[Text]需要Accept的
                    //        "AcceptText must NOT be called here to apply the data, because a control
                    //         such as a CheckBox has no [Text] to accept"
                    //
                    // Carried because it explains the shape of everything below: the value is written
                    // by an explicit typed SetItem per column type rather than by asking the control
                    // to accept its own edit text. DataWindowServiceHost publishes AcceptText, and
                    // calling it here would be the obvious simplification and is exactly what the
                    // oracle forbids.
                    ApplyColumnTypeCoercion(host, row, columnId.Value, dwo.ColType, currentData);
                }

                // :L246  //触发数据真实更改后的事件   "raise the event for data having really changed"
                // :L247  Event OnDoItemChanged(row,dwo)
                //
                // RAISED UNCONDITIONALLY - outside the `if bEqual` guard, so it fires even when no
                // coercion was applied. This is the raise whose handler drives the column-expression
                // service [:L313-L315], the broker topic [:L316-L318] and the semantic changed event
                // [:L319], in that order.
                events.OnDoItemChanged(row, dwo);

                // :L248  //拒绝DW将当前文本再次应用到缓冲区
                //        "refuse to let the DataWindow apply the current text to the buffer again"
                // :L249  //*防止ItemChanged改变了缓冲区的值后被覆盖!
                //        "prevent the value ItemChanged wrote to the buffer from being overwritten!"
                // :L250  rtCode = 2
                //
                // ODDITY O-4. THE FORCED 2 IS A DELIBERATE SUPPRESSION OF THE RUNTIME'S WRITE-BACK,
                // NOT A STATUS CODE. It is assigned regardless of what the coercion did, regardless
                // of whether the coercion ran at all, and regardless of the value the handler
                // returned - so a handler returning 0, -1 or 42 all yield 2 from this arm. Returning
                // the handler's own value instead would let the DataWindow re-apply its edit text
                // over a buffer the changed-event chain may already have written, which is precisely
                // what :L249 says must not happen.
                rtCode = (long)ItemChangeResult.RestoreAndRejectText;
                break;
        }

        // :L253  return rtCode
        // The possibly-rewritten code, classified. The reachable set here is exactly
        // {TriggerValidationError, RestoreAndRejectText}: the gate returned Default earlier, and
        // KeepValueNoFocusMove was rewritten at :L225. Classification is total, so a handler value
        // outside {1,2,3} that reached `case else` has already been forced to 2 above.
        return Classify(rtCode);
    }

    /// <summary>
    /// Classifies a raw item-change code into the alphabet - the port of which arm of
    /// <c>choose case rtCode</c> a value selects
    /// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L211-L251</c>).
    /// </summary>
    /// <param name="rtCode">
    /// The raw code, as a handler returned it. Any <see cref="long"/> is legal input.
    /// </param>
    /// <returns>
    /// The named arm the value selects. Every value outside <c>{1, 2, 3}</c> maps to
    /// <see cref="ItemChangeResult.Default"/>, which is <c>case else</c> [<c>:L226</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// TOTAL BY CONSTRUCTION, AND THAT IS THE ORACLE'S SHAPE, NOT A CONVENIENCE. The outer dispatch
    /// has a <c>case else</c>, so there is no such thing as an unrecognised item-change code: <c>0</c>
    /// selects the default arm, and so do <c>-1</c> and <c>42</c>. This method therefore never
    /// reports a failure and never throws.
    /// </para>
    /// <para>
    /// Do not use it to decide whether a value is "valid". It answers only which arm runs. The
    /// distinction matters at the stash, where the RAW value is retained precisely because the next
    /// event compares it numerically against <c>1</c> and <c>3</c> [<c>:L338</c>, <c>:L369</c>] - see
    /// <see cref="IItemChangeSessionState.ItemChangeRetCode"/>.
    /// </para>
    /// <para>
    /// It is exposed rather than private so the classification can be asserted directly by the parity
    /// matrix, including for the out-of-alphabet values that are otherwise only reachable through a
    /// handler double (constraint C-H).
    /// </para>
    /// </remarks>
    internal static ItemChangeResult Classify(long rtCode)
    {
        return rtCode switch
        {
            // :L212, :L213, :L223 - the three named arms, in the oracle's own order.
            (long)ItemChangeResult.TriggerValidationError => ItemChangeResult.TriggerValidationError,
            (long)ItemChangeResult.RestoreAndRejectText => ItemChangeResult.RestoreAndRejectText,
            (long)ItemChangeResult.KeepValueNoFocusMove => ItemChangeResult.KeepValueNoFocusMove,

            // :L226 `case else`. Reached by 0 and by every other value, which is why this is a
            // discard pattern and not an enumeration of the remaining possibilities.
            _ => ItemChangeResult.Default,
        };
    }

    /// <summary>
    /// The three-arm buffer equality test - the port of <c>se_cst_dw.sru:L198-L202</c>.
    /// </summary>
    /// <param name="originalValue">
    /// The snapshot taken at <c>:L189</c>, before the semantic handler ran.
    /// </param>
    /// <param name="currentValue">
    /// The reading taken after it, at <c>:L198</c> / <c>:L200</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the oracle would leave its <c>bEqual</c> flag set; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ORACLE, VERBATIM:
    /// </para>
    /// <code>
    /// :L198  if aOrgValue = dwo.Primary[row] then
    /// :L199      bEqual = true
    /// :L200  elseif IsNull(aOrgValue) and IsNull(dwo.Primary[row]) then
    /// :L201      bEqual = true
    /// :L202  end if
    /// </code>
    /// <para>
    /// ODDITY O-7 - THE NULL ASYMMETRY IS DELIBERATE AND IS THE REASON THE SECOND ARM EXISTS. A
    /// PowerScript comparison with a null operand yields NULL rather than false, and <c>if</c> treats
    /// null as false, so <c>:L198</c> cannot succeed when either side is null - which is exactly why
    /// the author added <c>:L200</c> to classify null-versus-null as EQUAL. ONE-SIDED NULL falls
    /// through both arms to "not equal", and that is correct: a cell that went from a value to null,
    /// or from null to a value, genuinely changed.
    /// </para>
    /// <para>
    /// NULL IS NEVER COLLAPSED TO A DEFAULT (AAP 0.4.5.4). Substituting zero or the empty string for a
    /// null would make the second arm unreachable and would report "changed" for every null-to-null
    /// comparison, which would then drive the restore at <c>:L216</c> and the coercion at <c>:L229</c>
    /// to be skipped for every null column.
    /// </para>
    /// <para>
    /// EQUALITY IS <see cref="object.Equals(object?)"/> ON THE RUNTIME VALUES, WITH NO NUMERIC
    /// WIDENING. Both operands are two reads of the SAME cell of the same buffer, so their runtime
    /// type is invariant and a type-crossing comparison cannot arise from the DataWindow. PowerScript
    /// would coerce across numeric types, but adding that here would WIDEN the contract on a case the
    /// oracle cannot reach, where the governing rule is to narrow with a defined outcome instead.
    /// </para>
    /// <para>
    /// Exposed rather than private so the full null matrix - both null, original null, current null,
    /// neither null - is assertable directly (constraint C-H).
    /// </para>
    /// </remarks>
    internal static bool IsBufferValueEqual(object? originalValue, object? currentValue)
    {
        // :L198 - the value comparison. Guarded on both operands being non-null, because that is what
        // a PowerScript `=` against a null operand amounts to: a null result, which `if` rejects.
        if (originalValue is not null && currentValue is not null && originalValue.Equals(currentValue))
        {
            return true;
        }

        // :L200 - `elseif IsNull(aOrgValue) and IsNull(dwo.Primary[row]) then`. BOTH null is EQUAL.
        if (originalValue is null && currentValue is null)
        {
            return true;
        }

        // :L202 - the oracle's `end if` with no else, leaving `bEqual` at the false it was declared
        // with at :L184. One-sided null lands here.
        return false;
    }

    /// <summary>
    /// The inner coercion dispatch - the port of <c>choose case Left(dwo.ColType,5)</c>
    /// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L231-L244</c>).
    /// </summary>
    /// <param name="host">The DataWindow to write to.</param>
    /// <param name="row">The one-based row.</param>
    /// <param name="columnId">The resolved column identifier.</param>
    /// <param name="colType">
    /// The column's declared type, RAW. Each arm's predicate applies the <c>Left(...,5)</c>
    /// truncation itself, so this must not be pre-truncated or pre-parsed.
    /// </param>
    /// <param name="data">The edit text to coerce. May be <see langword="null"/>.</param>
    /// <remarks>
    /// <para>
    /// EXACTLY SIX ARMS, IN THE ORACLE'S OWN ORDER, AND NO DEFAULT (oddity O-5). The arms are written
    /// as an <c>if</c> / <c>else if</c> chain rather than a <c>switch</c> on a truncated string so that
    /// each arm delegates truncation and comparison to the validator that owns it - keeping one
    /// implementation of the <c>Left(...,5)</c>-then-ordinal-equality rule rather than a second copy
    /// here that could drift from the five conversions it dispatches to.
    /// </para>
    /// <para>
    /// THE `datet` / `date` ORDERING IS A TRAP AND IT IS PRESERVED. <c>:L238</c> tests <c>"datet"</c>
    /// BEFORE <c>:L240</c> tests <c>"date"</c>. Both arms are genuinely distinct because
    /// <c>choose case</c> compares for EQUALITY: <c>Left("datetime",5)</c> is <c>"datet"</c>, which
    /// does not equal <c>"date"</c>. A port that used a prefix test would route every
    /// <c>datetime</c> column into the date arm and silently drop the time of day, with no error
    /// anywhere. Each validator's predicate is equality-based for exactly this reason, so the arms are
    /// mutually exclusive and the order below is faithful rather than load-bearing - and it is kept
    /// literal anyway, so the block reads against the source line for line.
    /// </para>
    /// <para>
    /// AN UNMATCHED COLUMN TYPE WRITES NOTHING AND REPORTS NOTHING. There is deliberately no trailing
    /// <c>else</c>, because <c>:L244</c> closes the oracle's <c>choose case</c> with no <c>case else</c>.
    /// Adding one - even one that only logged - would be behaviour the oracle does not have, and adding
    /// a throw would turn a silent no-op into a failure. Note that the sibling classification switch at
    /// <c>n_cst_dwsvc.sru:L516-L517</c> DOES have a default arm; the two switches are different and
    /// must not be harmonised. See this file's header.
    /// </para>
    /// <para>
    /// EVERY <c>SetItem</c> RESULT IS DISCARDED, because the oracle discards all six. The write is
    /// attempted and its success is not consulted, so a failed write is silent - which is legacy
    /// behaviour and is preserved.
    /// </para>
    /// <para>
    /// The five conversions come from <c>Validators/</c> rather than being reimplemented here.
    /// <c>NumberValidator.CoerceToDecimal</c> names this exact call site, <c>:L226-L251</c>, as its
    /// caller and records that the item-change path must use the silent form rather than the reporting
    /// form. Two implementations of one legacy conversion could diverge without either being obviously
    /// wrong, which is the parity hazard this reuse removes.
    /// </para>
    /// </remarks>
    private static void ApplyColumnTypeCoercion(
        DataWindowServiceHost host,
        long row,
        long columnId,
        string colType,
        string? data)
    {
        // :L231  choose case Left(dwo.ColType,5)

        // :L232  case "char","char("
        if (StringValidator.OwnsColType(colType))
        {
            // :L233  SetItem(row,Long(dwo.ID),data)  - the raw edit text, with NO conversion function
            // around it. Coerce is the identity and preserves null as null.
            host.SetItem(row, columnId, StringValidator.Coerce(data));
        }

        // :L234  case "decim","real","numbe"
        // NOTE THE MEMBERSHIP OF "numbe". It belongs to the DECIMAL arm here, while the sibling
        // classification switch at n_cst_dwsvc.sru:L506 groups it with the INTEGER arm. This file
        // reproduces THIS switch. Routing it through the long conversion instead would truncate the
        // fractional part of every `number` column.
        else if (NumberValidator.IsDecimalCoercionColumnType(colType))
        {
            // :L235  SetItem(row,Long(dwo.ID),Dec(data))
            host.SetItem(row, columnId, NumberValidator.CoerceToDecimal(data));
        }

        // :L236  case "long","ulong"
        else if (NumberValidator.IsLongCoercionColumnType(colType))
        {
            // :L237  SetItem(row,Long(dwo.ID),Long(data))
            // One arm serves two legacy ranges: PowerScript `long` is 32-bit signed and `ulong` is
            // 32-bit unsigned, and the single 64-bit signed carrier spans both without loss.
            host.SetItem(row, columnId, NumberValidator.CoerceToLong(data));
        }

        // :L238  case "datet"        <-- TESTED BEFORE "date". See the remarks.
        else if (DateTimeValidator.MatchesColumnType(colType))
        {
            // :L239  SetItem(row,Long(dwo.ID),DateTime(data))
            host.SetItem(row, columnId, DateTimeValidator.Coerce(data));
        }

        // :L240  case "date"
        else if (DateValidator.MatchesColumnType(colType))
        {
            // :L241  SetItem(row,Long(dwo.ID),Date(data))
            host.SetItem(row, columnId, DateValidator.Coerce(data));
        }

        // :L242  case "time"
        else if (TimeValidator.MatchesColumnType(colType))
        {
            // :L243  SetItem(row,Long(dwo.ID),Time(data))
            host.SetItem(row, columnId, TimeValidator.Coerce(data));
        }

        // :L244  end choose
        //
        // NO `else` HERE, AND THAT IS THE BEHAVIOUR (oddity O-5). The oracle's inner switch has no
        // `case else`, so a column type matching none of the six arms is simply not written: the cell
        // keeps whatever the runtime left in it, nothing is reported, and nothing is thrown. Do not add
        // a final else.
    }

    /// <summary>
    /// The port of <c>String(dwo.Primary[row])</c> at <c>se_cst_dw.sru:L206</c> - PowerScript's
    /// <c>String</c> applied to an <c>any</c> read out of the primary buffer.
    /// </summary>
    /// <param name="value">The buffer value. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>; otherwise its
    /// text form.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NULL PROPAGATES, AND IS NOT COLLAPSED TO AN EMPTY STRING. PowerScript's <c>String</c> of a null
    /// yields null, and the value produced here is handed straight to the nested
    /// <c>OnDwnChanging</c> raise at <c>:L207</c>, so a handler that distinguishes null from empty
    /// would observe the difference (AAP 0.4.5.4).
    /// </para>
    /// <para>
    /// THE DATE, TIME AND DATETIME FORMS ARE THE VALIDATORS' OWN CANONICAL FORMS, DELIBERATELY.
    /// PowerScript's <c>String(datetime)</c> renders using the machine's regional settings, so the
    /// oracle's exact text is not determined by the repository and any fixed choice is a substitution.
    /// Routing these three through <c>DateTimeValidator</c>, <c>DateValidator</c> and
    /// <c>TimeValidator</c> makes the substitution CLOSED UNDER ROUND TRIP: the text this produces is
    /// exactly the text the corresponding coercion arm at <c>:L239</c>, <c>:L241</c> or <c>:L243</c>
    /// parses back, so no new format is introduced anywhere in the service and no value can be
    /// stringified into a form its own arm cannot read. It is also invariant-culture, which the
    /// oracle cannot express and which matters because a container's locale is not the developer
    /// workstation's.
    /// </para>
    /// <para>
    /// The boolean arm is DEFENSIVE and is documented as such: the DataWindow column-type system has
    /// no boolean, so a boolean cannot arrive from a real primary buffer. It is handled rather than
    /// left to the general arm because <see cref="bool.ToString()"/> capitalises where PowerScript's
    /// <c>String(boolean)</c> does not, and a defensive arm that quietly diverged from the oracle
    /// would be worse than no arm at all.
    /// </para>
    /// <para>
    /// Private because it exists to serve one call site. It is reachable from the parity matrix
    /// through that call site - the not-equal path drives it - so it needs no wider accessibility.
    /// </para>
    /// </remarks>
    private static string? ToLegacyString(object? value)
    {
        return value switch
        {
            // PowerScript propagates null through its conversion functions.
            null => null,

            // Already text: returned unchanged, with no trim and no normalisation.
            string text => text,

            // The three temporal forms, in the canonical shapes their own coercion arms parse.
            // DateTime is matched BEFORE the IFormattable arm below, which it also satisfies.
            DateTime dateTime => DateTimeValidator.FormatExpressionValue(dateTime),
            DateOnly date => DateValidator.FormatValue(date),
            TimeOnly time => TimeValidator.Format(time),

            // Defensive; see the remarks. Lowercase, matching PowerScript rather than the BCL.
            bool flag => flag ? "true" : "false",

            // Every numeric buffer type lands here: invariant culture, so a locale with a
            // digit-group separator or a comma decimal mark cannot change the text.
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),

            // Anything else a host implementation might carry in an `any`. ToString may itself return
            // null, which is propagated rather than replaced.
            _ => value.ToString(),
        };
    }
}
