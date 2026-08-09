// ==================================================================================================
//  EventGate - THE COMPOSABLE EVENT-DISPATCH BITMASK
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                   :L40-L43    the three EID_* constants and the "combination supported" note
//                   :L109-L111  the three public prototypes
//                   :L469-L487  of_iseventdisabled, body at :L486
//                   :L489-L511  of_disableevent,    guard :L506, mutate :L508, return :L510
//                   :L513-L535  of_enableevent,     guard :L530, mutate :L532, return :L534
//
//  ORACLE STATUS  se_cst_dw.sru is READ ONLY and is the behavioural oracle for parity testing, never
//                 an edit target (constraint C-C). It is also the ONLY specification: logfile.md
//                 stops at framework 3.0.7.2062 (2022-04-14) while the commit history runs years
//                 later, and the two PowerBuilder build definitions ws_objects/pfw.pbl.src/
//                 project.srj and p_pfw.srj contradict each other and both name
//                 pfw.utility.imgcodec.pbl, which exists nowhere in the repository, so neither would
//                 build as written. Nothing else in the tree can adjudicate a behavioural question,
//                 which is why every claim below carries the ws_objects/** locator it came from.
//
//                 The source object declares ZERO PBNI native bindings - `grep -cE 'native |external
//                 function'` over it returns 0 - so this is a pure logic port with no native
//                 substitution problem anywhere in it.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  This file owns the mask VOCABULARY (the three EID_* bits) and the three OPERATIONS over a mask.
//  It does NOT own the mask VALUE. In the legacy the value is a private instance field,
//
//      //禁用的事件
//      long _nDisabledEvent                                              [se_cst_dw.sru:L88-L89]
//
//  and in this port that field belongs to Domain/ValidationSession.cs, which is the type that holds
//  the per-session state the event chain reads and writes between events. The split is deliberate
//  and has two consequences worth stating, because both are easy to undo by accident:
//
//    1. Every operation here takes the mask as an argument rather than reading a field, so this type
//       carries NO state of its own. There is no static mutable state anywhere in it, which is what
//       makes its tests order independent (constraint C-H).
//    2. The two mutators take the mask by reference, because the legacy assigns the field IN PLACE
//       at :L508 and :L532. Passing it by reference reproduces that exactly and - the part that
//       matters for parity - lets the rejection path leave the caller's mask provably UNTOUCHED,
//       because on that path the reference is simply never assigned.
//
//  Do not add a field here to "simplify" the call sites. A mask stored on a static type would be
//  shared by every session in the process, which is a behavioural change of the worst kind: two
//  unrelated DataWindows would silently gate each other's events.
//
//  THE FOUR GUARD SITES THIS FILE EXISTS TO SERVE
//  ------------------------------------------------------------------------------------------------
//  The gate is read at exactly four places in the legacy event chain. The list is exhaustive: a grep
//  for EID_ over the source returns the three declarations and these four reads and nothing else.
//
//      :L124  ondwnrowchange        if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE)  then return 0
//      :L130  ondwnrowchanging      if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE)  then return 0
//      :L176  ondwnitemchangefocus  if BitTest(_nDisabledEvent,EID_ITEMFOCUSCHANGE) then return 0
//      :L187  ondwnitemchange       if BitTest(_nDisabledEvent,EID_ITEMCHANGE)      then return 0
//
//  ALL FOUR SHORT-CIRCUIT WITH `return 0`, AND THAT ZERO IS NOT AN ERROR CODE. Zero is the
//  continue value of the prevent convention the chain uses throughout, so a gated event reports
//  "continue, nothing prevented" - never a failure. Domain/DataWindowEventChain.cs consumes this
//  file at those four points, which is why IsEventDisabled is shaped as a cheap predicate over a
//  mask and a bit rather than as something that returns a result code.
//
//  THE EID_ITEMCHANGE COUPLING IS A FIRST-CLASS BEHAVIOUR, NOT A FOOTNOTE
//  ------------------------------------------------------------------------------------------------
//  The oracle's own trailing comment on the third constant says so [se_cst_dw.sru:L43]:
//
//      constant long EID_ITEMCHANGE = 4   //ItemChanged，禁用后将不会触发列表达式计算！
//                                         "ItemChanged; once disabled, column-expression
//                                          calculation will NOT be triggered!"
//
//  The mechanism is visible in the source and is worth tracing once, because the effect is silent.
//  The guard at :L187 sits at the TOP of `ondwnitemchange` [:L182-L253] and returns before that
//  handler can reach `ondoitemchanged` [:L295-L320], whose first act is
//
//      if ColumnExp.#Enabled then
//          ColumnExp.Event OnItemChanged(row,dwo)                          [se_cst_dw.sru:L313-L315]
//      end if
//
//  So a consumer that disables item change merely to quieten a notification ALSO stops every bound
//  column expression from recalculating, gets stale computed values, and receives no error anywhere
//  to tell it that happened. That is legacy behaviour and it is PRESERVED here (constraint C-B).
//  What is added is only that it is written down, at the constant, where it cannot be missed - which
//  is the same position contract C-03 takes in shared/PowerFramework.Contracts/Proto/
//  dataservices.v1.proto, where the coupling is documented on the wire message rather than left to
//  an implementation note. This is also the reason Expressions/ depends on the gate and not the
//  other way round.
//
//  THE INTEGER WIDTH IS A DECISION, AND IT IS uint (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  The mask and the event argument are both `uint`, and that is not the obvious choice, so here is
//  the reasoning in full.
//
//  PowerBuilder and C# spell different widths with the same names. PowerBuilder `ulong` /
//  `unsignedlong` is 32-bit, so it maps to C# `uint` and NOT to C# `ulong`; PowerBuilder `uint` /
//  `unsignedinteger` is 16-bit, so it maps to C# `ushort`. The three bit primitives this file calls
//  are declared over the 32-bit unsigned type in every position:
//
//      global function boolean bittest (readonly ulong num, readonly ulong bits)
//                                                        [ws_objects/pfw.common.pbl.src/bittest.srf]
//      global function ulong   bitor   (readonly ulong a,   readonly ulong b)
//                                                        [ws_objects/pfw.common.pbl.src/bitor.srf]
//      global function ulong   bitclear(readonly unsignedlong num, readonly unsignedlong bits)
//                                                      [ws_objects/pfw.common.pbl.src/bitclear.srf]
//
//  Shared.Kernel's Bits therefore exposes them as BitTest(uint, uint), BitOr(uint, uint) and
//  BitClear(uint, uint). Matching that width exactly is what lets every call below compile with NO
//  cast and no implicit conversion in either direction, which is the requirement: warnings are
//  promoted to errors repository wide by Directory.Build.props, so a sign-conversion or
//  loss-of-precision diagnostic here would FAIL the build rather than merely warn.
//
//  Widening the mask to `long` to match the legacy's own `long _nDisabledEvent` [:L89] is the trap,
//  and it does not merely cost a cast - it does not compile. Bits documents that a `long` operand
//  raises CS1503 by design and that neither widening its parameters nor adding a `long` overload is
//  permitted, because such an overload is exactly the silent-truncation door that file exists to
//  keep shut. Nothing is lost by using `uint`: the three bits are 1, 2 and 4, every legal mask is a
//  sum of them, and PowerBuilder itself converts its `long` argument to the primitives' `ulong`
//  parameter at each of the four guard sites above.
//
//  ONE PLACE THIS FILE DOES NOT FOLLOW ITS OWN WIDTH: the wire. Contract C-03 carries the mask as
//  `int64 mask = 1` on message EventGate, and RetCode's members are `const long`. That is correct on
//  both sides and is not an inconsistency to resolve here - the int64-to-uint conversion belongs at
//  the gRPC boundary in Grpc/DataWindowService.cs, which is the only layer that sees the wire type.
//  Do not widen anything in this file to make the two look alike.
//
//  ACCESSIBILITY: internal, BY DESIGN AND NOT BY OVERSIGHT
//  ------------------------------------------------------------------------------------------------
//  The legacy members are `public` [:L39 for the constants, :L109-L111 for the functions], but
//  PowerBuilder `public` means "visible to other objects in the one flat global namespace" and has
//  no assembly analogue. In this architecture the service's published surface is contract C-03, and
//  versioned contracts are the ONLY permitted cross-service coupling, so this type is an
//  implementation detail behind that contract. PowerFramework.DataServices.csproj says so directly:
//  it justifies its InternalsVisibleTo grant by naming "the EID_* gate of Domain/EventGate.cs"
//  among the internal seams the tests must drive, and states that none of them is public API and
//  none should be made public merely to be testable. The sibling test project reaches these members
//  through that grant, so `internal` costs the coverage gate nothing.
//
//  THREE LEGACY ODDITIES ARE REPRODUCED HERE, NOT REPAIRED (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//  Each is annotated again at its point of reproduction, so that a maintainer reading only the
//  member cannot mistake it for a defect in this port. Summarised once here so the set is visible:
//
//    O-1  IsEventDisabled has NO zero-argument guard, while both mutators do. The legacy predicate's
//         entire body is a bare bit test [:L486]. Adding a guard "for consistency" would be a
//         behavioural change.
//    O-2  The two mutators return DIFFERENT types - `long` for disable [:L110] against `integer`
//         for enable [:L111] - though their bodies are mirror images.
//    O-3  The header comment above of_enableevent claims "Returns: long" [:L521] while its own
//         signature says `integer` [:L111]. The comment is wrong and is preserved as written.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE (AAP 0.8.5). The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment,
//  so none may be asserted. Bit masking is used here because it is what the oracle does and because
//  the bits must compose - not because it is fast. The only quantitative requirement in scope is the
//  coverage gate.
//
//  CONSTANT IDENTIFIERS ARE PRESERVED VERBATIM (AAP 0.4.5.3). The SCREAMING_SNAKE spellings travel
//  in serialized payloads, in log records and in characterization recordings, where a rename would
//  silently invalidate every stored comparison. The repository root .editorconfig carries a section
//  keyed to this exact file path that sets CA1707 and IDE1006 to none, so the names are declared
//  plainly below and this file carries NO in-source analyzer suppression directive of any kind. If a
//  build ever reports CA1707 or IDE1006 here, the .editorconfig glob has stopped matching this path -
//  that is a defect to report and fix in .editorconfig, never to paper over by renaming a constant.
// ==================================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The composable event-dispatch bitmask: the three <c>EID_*</c> bits that name a suppressible
/// DataWindow event, and the three operations that test, set and clear them on a mask.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>: the constants at
/// <c>:L41-L43</c> and the three public functions at <c>:L109-L111</c>.
/// </para>
/// <para>
/// SET MEANS SUPPRESSED. The mask records the events that are <b>disabled</b>, so a set bit means
/// the corresponding event does not fire. A zero mask therefore disables nothing and is the state of
/// a freshly constructed control. <see cref="DisableEvent"/> adds bits, <see cref="EnableEvent"/>
/// removes them, and both are idempotent.
/// </para>
/// <para>
/// THIS TYPE HOLDS NO MASK. The mask value lives on the session that owns it
/// (<c>_nDisabledEvent</c> at <c>se_cst_dw.sru:L89</c>), and every member here receives it as an
/// argument. The type has no fields and no static mutable state of any kind.
/// </para>
/// <para>
/// The bits are powers of two so that they compose, which the oracle states in its own comment at
/// <c>:L40</c> (&#x652F;&#x6301;&#x7EC4;&#x5408; - "combination supported"). A caller may pass a sum
/// such as <c>EID_ROWFOCUSCHANGE + EID_ITEMCHANGE</c>; see <see cref="IsEventDisabled"/> for the one
/// place where a composite argument does not mean what it appears to mean.
/// </para>
/// </remarks>
internal static class EventGate
{
    // ----------------------------------------------------------------------------------------------
    //  THE THREE BITS                                            se_cst_dw.sru:L40-L43, verbatim
    // ----------------------------------------------------------------------------------------------
    //  The oracle text, reproduced exactly as it appears so the port can be diffed against it:
    //
    //      //事件ID（支持组合）
    //      constant long EID_ROWFOCUSCHANGE      = 1   //RowFocusChanging/RowFocusChanged
    //      constant long EID_ITEMFOCUSCHANGE     = 2   //ItemFocusChanged
    //      constant long EID_ITEMCHANGE          = 4   //ItemChanged，禁用后将不会触发列表达式计算！
    //
    //  There are exactly THREE. The wire enum in dataservices.v1.proto additionally declares
    //  EID_UNSPECIFIED = 0, but that is proto3's mandatory zero member and the proto itself records
    //  it as "the protocol zero and never a legacy event". No zero-valued constant is declared here,
    //  because inventing vocabulary the oracle does not have is the kind of improvement C-B forbids.
    //  Zero is not a nameless gap either - it is the value both mutators reject outright.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Suppresses the row-focus events. Covers <b>both</b> <c>RowFocusChanging</c> and
    /// <c>RowFocusChanged</c> - one bit gates the pair.
    /// </summary>
    /// <remarks>
    /// <c>se_cst_dw.sru:L41</c>. Tested at <c>:L124</c> (<c>ondwnrowchange</c>) and <c>:L130</c>
    /// (<c>ondwnrowchanging</c>), both of which return <c>0</c> - continue - when the bit is set.
    /// </remarks>
    internal const uint EID_ROWFOCUSCHANGE = 1;

    /// <summary>
    /// Suppresses <c>ItemFocusChanged</c>.
    /// </summary>
    /// <remarks>
    /// <c>se_cst_dw.sru:L42</c>. Tested at <c>:L176</c> (<c>ondwnitemchangefocus</c>), which returns
    /// <c>0</c> - continue - when the bit is set.
    /// </remarks>
    internal const uint EID_ITEMFOCUSCHANGE = 2;

    /// <summary>
    /// Suppresses <c>ItemChanged</c> - <b>and, as a documented side effect, all column-expression
    /// calculation with it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>se_cst_dw.sru:L43</c>. Tested at <c>:L187</c> (<c>ondwnitemchange</c>), which returns
    /// <c>0</c> - continue - when the bit is set.
    /// </para>
    /// <para>
    /// THE COUPLING IS THE POINT OF THIS DOC COMMENT. The oracle's own trailing comment at
    /// <c>:L43</c> warns that once this event is disabled, column-expression calculation will no
    /// longer be triggered. The mechanism: the guard at <c>:L187</c> sits at the top of
    /// <c>ondwnitemchange</c> [<c>:L182-L253</c>] and returns before that handler can reach
    /// <c>ondoitemchanged</c> [<c>:L295-L320</c>], whose first act is to invoke the column-expression
    /// service's changed handler [<c>:L313-L315</c>].
    /// </para>
    /// <para>
    /// So setting this bit to quieten a notification also silently stops every bound column
    /// expression from recalculating, yielding stale computed values with no error raised anywhere.
    /// That is preserved legacy behaviour (constraint C-B), and it is the reason contract C-03's gate
    /// methods have an observable effect on contract C-04.
    /// </para>
    /// </remarks>
    internal const uint EID_ITEMCHANGE = 4;

    // ----------------------------------------------------------------------------------------------
    //  OPERATION 1 OF 3 - THE PREDICATE                          se_cst_dw.sru:L469-L487, body :L486
    // ----------------------------------------------------------------------------------------------
    //  ODDITY O-1, PRESERVED DELIBERATELY: THERE IS NO ZERO-ARGUMENT GUARD HERE.
    //
    //  The legacy prototype is
    //      public function boolean of_iseventdisabled (readonly long evt)              [:L109]
    //  and its entire body, after the header comment block, is one line:
    //      return BitTest(_nDisabledEvent,evt)                                         [:L486]
    //
    //  Both mutators below DO guard against a zero argument, at :L506 and :L530 respectively. This
    //  predicate does not, and the asymmetry is reproduced exactly. Adding `if (evt == 0u) return
    //  false;` or making it reject zero would be a behavioural change, which constraint C-B forbids -
    //  and it would be observable, because a zero argument is a legal call here with a defined
    //  answer. Bits.BitTest(mask, 0) evaluates (mask & 0) != 0, which is false for every mask, so a
    //  degenerate zero query answers "not disabled". The legacy permits that call and so does this
    //  port. Note also what the legacy CANNOT do: it has no way to report an error at all, because
    //  the declared return type is `boolean`, so a guard here would have had to invent a meaning for
    //  false rather than merely add validation.
    //
    //  Bits.BitTest is used rather than a hand-rolled `(disabledEvent & evt) != 0u`. It is the
    //  sanctioned managed substitute for the closed pfw.dll primitive, and Bits names this file's
    //  :L532 call site as the single call site of its BitClear member, so the two files were
    //  reconciled with each other rather than left to agree by accident.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether any of the events named by <paramref name="evt"/> is currently suppressed by
    /// <paramref name="disabledEvent"/>.
    /// </summary>
    /// <param name="disabledEvent">
    /// The mask of currently disabled events, owned by the caller's session
    /// (<c>_nDisabledEvent</c>, <c>se_cst_dw.sru:L89</c>).
    /// </param>
    /// <param name="evt">
    /// The event bit to test, normally one of <see cref="EID_ROWFOCUSCHANGE"/>,
    /// <see cref="EID_ITEMFOCUSCHANGE"/> or <see cref="EID_ITEMCHANGE"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one bit of <paramref name="evt"/> is set in
    /// <paramref name="disabledEvent"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>se_cst_dw.sru:L469-L487</c>; the whole body is the bit test at <c>:L486</c>. This is the
    /// member the four guard sites use - <c>:L124</c>, <c>:L130</c>, <c>:L176</c> and <c>:L187</c> -
    /// each of which returns <c>0</c> (continue) rather than an error when it reports
    /// <see langword="true"/>.
    /// </para>
    /// <para>
    /// A COMPOSITE ARGUMENT IS AN <b>ANY</b> QUESTION, NOT AN <b>ALL</b> QUESTION. This is the one
    /// genuine trap in this member. <c>BitTest</c> is <c>(num &amp; bits) != 0</c>, so
    /// <c>IsEventDisabled(mask, EID_ROWFOCUSCHANGE + EID_ITEMCHANGE)</c> is <see langword="true"/>
    /// when <b>either</b> bit is set, not only when both are. Contract C-03 sidesteps the ambiguity by
    /// carrying per-bit results as an explicit list on <c>GetEventGateResponse</c> instead of letting
    /// a consumer test a multi-bit value itself. Callers that need an "all of these" answer must test
    /// each bit separately; this member cannot be changed to answer that question, because the legacy
    /// answers the "any" one.
    /// </para>
    /// <para>
    /// NO ZERO-ARGUMENT GUARD, DELIBERATELY - see oddity O-1 in the file header and the block
    /// immediately above this member. A zero <paramref name="evt"/> is a legal degenerate call that
    /// returns <see langword="false"/> for every mask, exactly as the legacy does.
    /// </para>
    /// </remarks>
    internal static bool IsEventDisabled(uint disabledEvent, uint evt)
    {
        // se_cst_dw.sru:L486 - the entire legacy body, and no guard precedes it.
        return Bits.BitTest(disabledEvent, evt);
    }

    // ----------------------------------------------------------------------------------------------
    //  OPERATION 2 OF 3 - DISABLE                       se_cst_dw.sru:L489-L511, prototype :L110
    // ----------------------------------------------------------------------------------------------
    //  ODDITY O-2, HALF ONE OF TWO: THIS MEMBER RETURNS THE WIDER TYPE.
    //
    //      public function long    of_disableevent (readonly long evt)                 [:L110]
    //      public function integer of_enableevent  (readonly long evt)                 [:L111]
    //
    //  Their bodies are mirror images - both reject a zero argument with E_INVALID_ARGUMENT, both
    //  mutate the mask with one bit operation, both return OK - yet the declared return types differ.
    //  PowerBuilder `integer` is 16-bit and `long` is 32-bit, so the enable path is declared NARROWER
    //  than the disable path for no reason the source gives. It is almost certainly an oversight in
    //  the original, and it is NOT repaired and NOT silently equalized here (constraint C-B): the
    //  relative asymmetry is carried into C# as `long` here against `int` on EnableEvent, following
    //  the plan's PowerBuilder-to-C# type mapping, which sends PowerBuilder `long` to C# `long`.
    //
    //  It is observationally benign, and saying so is part of preserving it honestly: the only two
    //  values either member can return are OK (0) and E_INVALID_ARGUMENT (-3), and both fit either
    //  width without truncation. So this is preserved for CONTRACT fidelity, not because a value
    //  would otherwise be lost. Contract C-03 takes the same position, carrying
    //  common.v1.RetCode.Value on both responses while annotating the difference at each message.
    //  The unit tests assert both return types by reflection, so a future "tidy-up" that harmonises
    //  them fails the build rather than passing unnoticed.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Suppresses the events named by <paramref name="evt"/> by adding their bits to
    /// <paramref name="disabledEvent"/>.
    /// </summary>
    /// <param name="disabledEvent">
    /// The mask of currently disabled events, updated in place. Left <b>untouched</b> when
    /// <paramref name="evt"/> is rejected.
    /// </param>
    /// <param name="evt">
    /// The event bit or bit-or of bits to suppress. A value of <c>0</c> is rejected.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// <paramref name="evt"/> is <c>0</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>se_cst_dw.sru:L489-L511</c>: the guard at <c>:L506</c>, the bit-or at <c>:L508</c> and the
    /// success return at <c>:L510</c>.
    /// </para>
    /// <para>
    /// IDEMPOTENT. A bit already set is set again to the same value, so repeating a call changes
    /// nothing and still returns <see cref="RetCode.OK"/>.
    /// </para>
    /// <para>
    /// COMPOSABLE. Because the three bits are disjoint powers of two, the legacy idiom of combining
    /// them with <c>+</c> is exactly a bit-or and cannot carry, so
    /// <c>DisableEvent(ref mask, EID_ROWFOCUSCHANGE + EID_ITEMCHANGE)</c> suppresses both and leaves
    /// <see cref="EID_ITEMFOCUSCHANGE"/> enabled.
    /// </para>
    /// <para>
    /// RETURNS <see langword="long"/>, WHILE <see cref="EnableEvent"/> RETURNS <see langword="int"/>.
    /// That asymmetry is the legacy's, not this port's - see oddity O-2 in the file header and the
    /// block above this member. Do not harmonise it.
    /// </para>
    /// </remarks>
    internal static long DisableEvent(ref uint disabledEvent, uint evt)
    {
        // se_cst_dw.sru:L506 - `if evt = 0 then return RetCode.E_INVALID_ARGUMENT`. The mask is not
        // assigned on this path, so the caller's value is left untouched, matching the legacy, which
        // returns before reaching its own assignment.
        if (evt == 0u)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // se_cst_dw.sru:L508 - `_nDisabledEvent = BitOR(_nDisabledEvent,evt)`. Assigning through the
        // reference reproduces the legacy's in-place mutation of its own instance field.
        disabledEvent = Bits.BitOr(disabledEvent, evt);

        // se_cst_dw.sru:L510. RetCode.OK is `const long`, and this member returns `long`, so the
        // value is returned directly with no conversion of any kind.
        return RetCode.OK;
    }

    // ----------------------------------------------------------------------------------------------
    //  OPERATION 3 OF 3 - ENABLE                        se_cst_dw.sru:L513-L535, prototype :L111
    // ----------------------------------------------------------------------------------------------
    //  ODDITY O-2, HALF TWO OF TWO: THIS MEMBER RETURNS THE NARROWER TYPE. See the block above
    //  DisableEvent for the full account; it is not restated here.
    //
    //  ODDITY O-3, PRESERVED: THE LEGACY HEADER COMMENT CONTRADICTS THE LEGACY SIGNATURE.
    //
    //  The comment block above of_enableevent declares its own return type, and gets it wrong:
    //      // Returns:  long                                                            [:L521]
    //  while the signature one line into the object's prototype list says otherwise:
    //      public function integer of_enableevent (readonly long evt)                   [:L111]
    //
    //  This was checked against all three members rather than assumed, and exactly ONE of them is
    //  inconsistent: :L477 says "boolean" and of_iseventdisabled is boolean; :L497 says "long" and
    //  of_disableevent is long; :L521 says "long" and of_enableevent is integer. So the wrong claim
    //  at :L521 is a genuine documentation defect in the oracle, not a pattern of loose commenting -
    //  which is precisely why it is recorded rather than quietly corrected. It is reproduced as a
    //  note ABOUT the oracle. It is emphatically not reproduced as a wrong C# return type: this
    //  member returns `int`, matching the SIGNATURE, because the signature is what the compiler and
    //  every caller actually saw.
    //
    //  THE CAST ON EACH RETURN IS REQUIRED BY THE LANGUAGE AND HIDES NOTHING.
    //
    //  RetCode's members are `const long` (RetCode.OK = 0, RetCode.E_INVALID_ARGUMENT = -3) while
    //  this member returns `int`, and C# has no implicit long-to-int conversion even for constants -
    //  the implicit constant expression conversion is defined only FROM int - so a bare
    //  `return RetCode.OK;` is CS0266 here. The explicit cast is therefore not a widening or
    //  narrowing choice made by this file, it is the only legal spelling.
    //
    //  Critically, it cannot mask a real mismatch, which is the property that makes it acceptable
    //  under "resolve widths explicitly, never with an unchecked cast that hides a real mismatch":
    //  the operand is a compile-time CONSTANT, and C# evaluates a constant conversion in a checked
    //  context at compile time. Were RetCode.OK or RetCode.E_INVALID_ARGUMENT ever changed to a value
    //  outside int's range, these casts would fail the build with CS0221 rather than truncate
    //  silently. The two current values, 0 and -3, are trivially in range.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Re-enables the events named by <paramref name="evt"/> by clearing their bits from
    /// <paramref name="disabledEvent"/>.
    /// </summary>
    /// <param name="disabledEvent">
    /// The mask of currently disabled events, updated in place. Left <b>untouched</b> when
    /// <paramref name="evt"/> is rejected.
    /// </param>
    /// <param name="evt">
    /// The event bit or bit-or of bits to re-enable. A value of <c>0</c> is rejected.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// <paramref name="evt"/> is <c>0</c>, each narrowed to <see langword="int"/> by a
    /// compile-time-checked constant conversion.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>se_cst_dw.sru:L513-L535</c>: the guard at <c>:L530</c>, the bit-clear at <c>:L532</c> and
    /// the success return at <c>:L534</c>. This is the call site that
    /// <c>PowerFramework.Shared.Kernel.Bits.BitClear</c> documents as its only one.
    /// </para>
    /// <para>
    /// IDEMPOTENT, AND A ROUND TRIP WITH <see cref="DisableEvent"/>. Clearing a bit that is already
    /// clear changes nothing and still returns <see cref="RetCode.OK"/>; disabling and then enabling
    /// the same bits restores the original mask exactly.
    /// </para>
    /// <para>
    /// RETURNS <see langword="int"/>, WHILE <see cref="DisableEvent"/> RETURNS
    /// <see langword="long"/> - the legacy's own asymmetry (oddity O-2), preserved rather than
    /// harmonised. The oracle's header comment at <c>:L521</c> additionally claims this function
    /// returns <c>long</c>, contradicting its own signature at <c>:L111</c>; that is oddity O-3, a
    /// documentation defect in the oracle recorded here as a note and not reproduced as a wrong
    /// return type.
    /// </para>
    /// </remarks>
    internal static int EnableEvent(ref uint disabledEvent, uint evt)
    {
        // se_cst_dw.sru:L530 - `if evt = 0 then return RetCode.E_INVALID_ARGUMENT`. As with
        // DisableEvent, the mask is not assigned on this path, so it is left untouched.
        if (evt == 0u)
        {
            return (int)RetCode.E_INVALID_ARGUMENT;
        }

        // se_cst_dw.sru:L532 - `_nDisabledEvent = BitClear(_nDisabledEvent,evt)`. BitClear is
        // `num & ~bits`, and it deliberately performs no validation of its own: Bits records that the
        // zero-argument guard belongs to this caller, one line earlier at :L530.
        disabledEvent = Bits.BitClear(disabledEvent, evt);

        // se_cst_dw.sru:L534. Cast required because RetCode.OK is `const long` and this member
        // returns `int`; see the block above for why this cannot hide a truncation.
        return (int)RetCode.OK;
    }
}
