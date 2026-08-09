// ==============================================================================================
//  Bits - the nine bit operations and the six word and byte composition primitives
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.common.pbl.src/bitand.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitor.srf     (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitxor.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitnot.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitlsh.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitrsh.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bittest.srf   (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/getbit.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/bitclear.srf  (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/hibyte.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/lobyte.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/hiword.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/loword.srf    (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/makeword.srf  (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/makelong.srf  (9 lines, DECLARATION ONLY)
//
//  ORACLE STATUS  All fifteen .srf files are READ ONLY and are the behavioural oracle for parity
//                 testing, never an edit target. Every one of them declares
//                 `global type <name> from function_object native "pfw.dll"` and then a single
//                 forward prototype, so the file IS the declaration and nothing more. The bodies
//                 live inside the CLOSED pfw.dll: a full depth search of this repository for
//                 *.cpp, *.cxx, *.cc, *.h, *.hpp and *.vcxproj returns ZERO results, so no
//                 implementation of any of these fifteen functions can be read anywhere. The
//                 prototypes and the in-repo call sites are therefore the entire evidence, and
//                 every behaviour below carries the locator it was taken from.
//
//                 The plan's native-binding matrix classifies this library's primitives as
//                 SUBSTITUTE using the C# operators, so writing managed equivalents is the
//                 sanctioned outcome and reverse engineering the binary is explicitly not
//                 expected of this file.
//
//  THE WIDTH HAZARD - POWERBUILDER AND C# SPELL DIFFERENT WIDTHS WITH THE SAME NAMES
//  --------------------------------------------------------------------------------------------
//  This is the whole difficulty of this file, and it is the sibling of the one based to zero
//  based array hazard the plan calls the single most dangerous mechanical hazard in the refactor.
//  It is more insidious than that one, because a wrong width COMPILES CLEANLY, raises no warning,
//  and returns a plausible looking number.
//
//      PowerBuilder ulong / unsignedlong   32-bit unsigned   ->   C# uint      NOT C# ulong
//      PowerBuilder uint  / unsignedinteger 16-bit unsigned  ->   C# ushort    NOT C# uint
//
//  A name for name port is therefore WRONG in every signature in this file. The four concrete
//  divergences it produces, each of which would pass a careless test:
//
//      BitNot(1) must be 0xFFFFFFFE. Ported with a C# ulong parameter the same expression yields
//      0xFFFFFFFFFFFFFFFE, a completely different number, with no diagnostic of any kind.
//
//      HiWord must take the upper 16 bits of a 32-bit value into a 16-bit result, so the shift
//      distance is 16. Over a 64-bit operand a 16-bit shift lands in the wrong place entirely and
//      returns part of the low half.
//
//      MakeLong must pack two 16-bit words into one 32-bit value, so the high word is shifted by
//      16. Over 64-bit operands the same code silently builds a value in the wrong half.
//
//      A shift count wraps modulo the operand width: C# masks it to the low 5 bits for a 32-bit
//      operand and to the low 6 bits for a 64-bit one. Getting the operand width right therefore
//      gets the wrap behaviour right for free, and getting it wrong changes the result of every
//      over-wide shift rather than only the extreme ones.
//
//  BitRsh must additionally be a LOGICAL, zero filling shift rather than an arithmetic one. That
//  is guaranteed here by the operand being unsigned: C# selects the arithmetic, sign propagating
//  shift only for signed operands, so a signed port would sign extend on every high bit value.
//
//  THIS FILE AGREES WITH Enums.cs BY DESIGN, NOT BY COINCIDENCE
//  --------------------------------------------------------------------------------------------
//  The masks these helpers actually receive are the STATE_* and REGEXP_* families, declared
//  `Constant Ulong` in the legacy catalogue [enums.sru:L96-L111, L976-L997] and surfaced by the
//  sibling Enums.cs as C# `uint`. That file's own header records the same mapping and names these
//  helpers as its reason for choosing an exact width over a uniform widening to long. The two
//  files were reconciled rather than left to be patched at the call sites, so
//
//      Bits.BitTest(state, Enums.STATE_HOVER + Enums.STATE_FOCUS)
//
//  compiles with NO cast: uint + uint yields uint, which is exactly the parameter type. Had the
//  catalogue widened those constants to long, every mask test in the tree would need a cast, and
//  a cast at every call site is precisely the paper-over this reconciliation avoids. The mapping
//  is also lossless in both directions, which two range checks confirm: REGEXP_MATCH_GLOBAL is
//  65536 [enums.sru:L997], so it does NOT fit the 16-bit ushort and settles the 32-bit question
//  on its own, while LOG_LEVEL_ALL is 0xFFFFFFFF [enums.sru:L1033], which fits uint exactly and
//  does not fit any signed 32-bit type at all.
//
//  NOT EVERY Enums BITMASK IS AN OPERAND HERE, AND THE MISMATCH IS CORRECT
//  --------------------------------------------------------------------------------------------
//  Read this before "fixing" a compiler error at a call site. The catalogue's agreement with this
//  file covers the families the legacy declares `Constant Ulong`. It does NOT cover the ones it
//  declares `Constant Long`, which is PowerBuilder's SIGNED 32-bit type and which the catalogue
//  correctly surfaces as C# `long`. The INIT_FLAG_ENABLE_* capability gate is the notable example
//  [enums.sru:L41-L49], so passing one of those constants to a member here raises CS1503.
//
//  That error is the width discipline working, not a defect to route around, and the resolution is
//  never to widen a parameter here or to add a `long` overload. Both are forbidden by C-B, and an
//  overload taking `long` is precisely the silent truncation door this file exists to keep shut.
//  There is also nothing owed: the legacy never mask tests that family at all, since no bittest or
//  bitand call anywhere under ws_objects takes an INIT_FLAG_* argument. Its one use passes the
//  value whole, as `pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`
//  [ws_objects/pfw.pbl.src/pfw.sra:L91]. A caller that genuinely needs to test such a flag is
//  working with a signed 32-bit quantity and should say so explicitly at its own call site.
//
//  TWO FAMILIES LIVE HERE, AND THEIR NAMES DO NOT SAY WHICH IS WHICH
//  --------------------------------------------------------------------------------------------
//  The six composition primitives split into two families that operate at different widths, and
//  the split does not follow the parameter spellings. Reading the names alone gets it wrong.
//
//      BYTE LEVEL, over a 16-bit word    HiByte, LoByte, MakeWord
//      WORD LEVEL, over a 32-bit value   HiWord, LoWord, MakeLong
//
//  MakeWord is the trap: its parameters are declared PowerBuilder uint, that is 16 bits, yet it
//  combines two BYTES into a 16-bit word, mirroring the Win32 macro of the same name. Its
//  operands are therefore byte valued arguments carried in 16-bit slots, not two words.
//
//  DERIVED FROM THE CORPUS - measured facts, not choices
//  --------------------------------------------------------------------------------------------
//      D-1  BitTest is a MASK test with ANY-bit semantics, `(num & bits) != 0`, and not a single
//           bit test. It is the highest traffic member in this file by a wide margin, with 326
//           call sites across ws_objects. The semantics are settled by the shape of the calls
//           rather than assumed: sites such as
//               BitTest(state, Enums.STATE_HOVER + Enums.STATE_FOCUS)
//               BitTest(state, Enums.STATE_HOVER + Enums.STATE_PRESSED)
//               BitTest(state, Enums.STATE_HOVER + Enums.STATE_SELECTED)
//           pass a TWO bit mask and expect true when EITHER bit is present, which a single bit
//           reading cannot express at all. An all-bits reading is excluded by the same evidence,
//           since the two states in each pair are mutually exclusive in practice and an all-bits
//           test would then be permanently false.
//
//      D-2  The legacy composes masks with `+` rather than `|`, as every site quoted above shows.
//           The two are equivalent ONLY while the combined bits are disjoint. That condition
//           holds for these callers, because STATE_* is a contiguous single bit family running
//           1, 2, 4 ... 16384 over bits 0 to 14 with no gap and no duplicate [enums.sru:L96-L111],
//           so no two members share a bit and no sum can carry. The idiom is recorded here and no
//           caller is "fixed": rewriting a caller would be a behavioural change, and this file
//           cannot see its callers anyway.
//
//      D-3  BitClear is `num & ~bits`. Its single call site is
//           [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L532]
//               _nDisabledEvent = BitClear(_nDisabledEvent, evt)
//           which removes an event's bit from a disabled event mask. The guard against a zero
//           argument sits in the CALLER, one line earlier at L530, as
//               if evt = 0 then return RetCode.E_INVALID_ARGUMENT
//           so validation is demonstrably the caller's job and not this primitive's. That is
//           direct evidence for the no-throw posture described below, not an assumption about it.
//
//      D-4  BitLsh's shift argument is 16 bits wide at the only place it is used. The site is
//           [ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L489]
//               if SetThreadAffinityMask(_handle, BitLSH(1, CPUIndex - 1)) = 0 then ...
//           inside `of_setaffinity (readonly unsignedinteger cpuindex)` declared at L488, so the
//           value flows from a PowerBuilder unsignedinteger, that is 16 bits, which independently
//           corroborates the ushort shift parameter from the prototype. The affinity primitive
//           itself is a deliberate non-port elsewhere in the plan; BitLsh belongs here regardless.
//
//      D-5  bitclear.srf is spelled inconsistently WITHIN ITS OWN SINGLE PROTOTYPE, at L7:
//               global function ulong bitclear (readonly unsignedlong num, ...)
//           The return type uses the short alias `ulong` while both parameters use the long form
//           `unsignedlong`. It is the only one of the fifteen to use the long form at all. The two
//           spellings name the SAME 32-bit unsigned type, so this is a cosmetic inconsistency in
//           the legacy source with no behavioural consequence, recorded only so that a reader
//           comparing the fifteen prototypes does not go looking for a distinction that is not
//           there. Two of the fifteen files also carry a different authorship comment, bittest.srf
//           and bitclear.srf being framework additions while the other thirteen come from the
//           PowerBuilder Extension credited in their headers; that too is cosmetic.
//
//      D-6  FIVE of the fifteen members have NO call site anywhere in the repository: BitXor,
//           BitNot, BitRsh, MakeWord and GetBit. For the first four the name and the prototype
//           together fix the meaning with no room to differ, because exclusive or, complement,
//           right shift and the Win32 word composition macro each denote exactly one operation.
//           GetBit is the exception, and it is the only genuine unknown in this file; see I-1.
//
//  INFERRED - choices made where the corpus is silent
//  --------------------------------------------------------------------------------------------
//  Each is flagged again at its point of reproduction and is named here with the test that pins
//  it, so that a later characterization run against the behavioural oracle can find exactly the
//  choices, revise any the oracle contradicts, and leave the derived facts above untouched. None
//  of the three below is presented as verified.
//
//  DP-7: "NOT PRESENTED AS VERIFIED" MEANS NOT GOLDEN-MASTER PARITY. Naming the standard, because
//  the disclaimer above states the limitation without saying which claim it withholds. The tests
//  pinning I-1, I-2 and I-3 are TARGET REGRESSION GUARDS: each stops a later edit to this file
//  moving the choice silently, and none can detect a disagreement with pfw.dll, because no
//  recording of pfw.dll's output exists in this repository to compare against. Promoting any of
//  the three to parity evidence requires a paired recording under
//  characterization/recordings/{legacy,dotnet}/<workflowId>/, captured against one unrecreated
//  persistence-db volume state; until one exists none may be reported as verified legacy behaviour
//  in docs/PARITY.md or any published summary, and IF A RECORDING CONTRADICTS ONE THE RECORDING
//  WINS. The DERIVED facts above are unaffected: each is settled by a cited locator or by the
//  operator the plan sanctions, and downgrading them here would understate real evidence.
//
//      I-1  GetBit numbers bits from ONE, so bit 1 is the least significant bit. The repository
//           does NOT adjudicate this: getbit.srf is declaration only, no call site exercises it,
//           and no comment anywhere documents the base. PowerBuilder is pervasively one based and
//           the plan singles out one based to zero based translation as the most dangerous
//           mechanical hazard in the refactor, so a one based reading is the likelier intent, but
//           it remains an inference.
//           Test: GetBitInferredIndexBaseIsOneBased
//
//      I-2  GetBit returns false for a bit index outside 1 to 32 rather than throwing. See
//           DECISION 2 for why the boundary needs deciding at all rather than falling out of the
//           arithmetic.
//           Test: GetBitInferredIndexBaseOutOfRangeReturnsFalse
//
//      I-3  A shift count at or beyond the 32-bit operand width wraps modulo 32 rather than
//           saturating to zero. This is C#'s own defined behaviour for a 32-bit operand and is
//           adopted rather than overridden. It is listed as inferred because the native body
//           cannot be read, though the inference is strong: the x86 shift instructions mask the
//           count to five bits for a 32-bit operand in exactly the same way, so a thin native
//           shim over them would agree.
//           Test: BitLshShiftCountWrapsModuloThirtyTwoInferred
//
//  NO ARGUMENT VALIDATION THROWS, ANYWHERE IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  Every member returns a value for every input. Nothing throws, nothing clamps and nothing
//  reports an error, because the legacy is a thin shim over a native call and masking and
//  wrapping ARE its behaviour rather than defects in it. D-3 shows the pattern directly: the one
//  caller that needed a zero check performed that check itself before calling. Adding a throwing
//  guard here would be a behaviour change that the plan forbids, and it would break callers that
//  today rely on a total function. Where a mask appears in an expression below, for example the
//  0xFF in MakeWord, it reproduces the legacy truncation and is not new validation.
//
//  NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The repository publishes no service level agreement, no latency budget and no throughput
//  target, so none may be claimed here or used to justify a design choice. In particular the
//  parameters are passed BY VALUE rather than by `in` reference. The plan's type table maps the
//  legacy `readonly` modifier to `in`, but `readonly` on a 16 or 32-bit scalar carries no
//  observable contract whatsoever: the callee cannot mutate the caller's copy either way, so the
//  modifier is unobservable and dropping it changes nothing a test could detect. By value is
//  chosen because these operands are smaller than the reference that would point at them, and
//  that is stated as the reason for the choice, not as a performance claim.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project: the rules document was retrieved and it
//  contains exactly one statement, that no user rules were provided. No rule is therefore
//  invented or inferred here, and the absence is not read as licence to lower the bar. The
//  binding constraints in their place are the plan's own constraint inventory and its enterprise
//  standard baseline:
//
//      C-A      Shared IMPLEMENTATION consumed in process. All fifteen members are pure functions
//               of their arguments: no I/O, no logging, no clock, no culture, no environment
//               access, no static mutable state, and no reference to the Contracts project, which
//               is the only permitted cross-service coupling in this refactor. Nothing here can
//               become a cross-service channel.
//      C-B      No behaviour improvements. The operand types are exact rather than widened for
//               convenience; there is no long or int convenience overload, because one would let
//               a caller pass a 64-bit value that silently truncates, which is the precise failure
//               mode the width discipline exists to prevent; no argument check throws where the
//               legacy wraps or truncates; and every legacy name is preserved, so BitLsh stays
//               BitLsh rather than becoming ShiftLeft.
//      C-C      The legacy tree is read only and is the oracle. All fifteen .srf files were read
//               and none was modified; each member below cites the exact file it came from.
//      C-K      Every technology-specific decision is documented at its point of reproduction, as
//               the numbered DECISION blocks below, and the width mapping that drives all fifteen
//               signatures is recorded above with its concrete divergences.
//      0.4.5.2  A *.srf global function becomes a static method on a role-named static class. The
//               plan names Bits.BitAnd as its own illustration of that rule, so the class name and
//               the member spellings are fixed by the plan rather than chosen here.
//      0.6.5    The native-binding matrix classifies these primitives as SUBSTITUTE using the C#
//               operators, so the managed bodies below are the sanctioned outcome.
//      0.6.7    Determinism. Every member is a total, pure function of its arguments and reads no
//               ambient state, so paired characterization recordings are reproducible and no
//               member needs a determinism seam.
//
//  This file declares NO SCREAMING_SNAKE and no underscore bearing identifier. That is a build
//  requirement rather than a style preference: the repository sets TreatWarningsAsErrors, and the
//  root .editorconfig scopes its naming analyzer suppressions to the individual files that
//  genuinely carry preserved legacy constant identifiers. This file is deliberately not one of
//  them, because it declares no constant at all. Where a named mask is wanted, callers reference
//  the Enums catalogue, which is one of the scoped files.
// ==============================================================================================

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The bit, word and byte primitives ported from the fifteen PBNI declarations in the legacy
/// <c>pfw.common</c> library.
/// </summary>
/// <remarks>
/// <para>
/// Nine members operate on bits of a 32-bit unsigned value and six compose or decompose 16-bit
/// and 32-bit values. Every one is a substitute for a function whose body lives in the closed
/// <c>pfw.dll</c> and cannot be read, so the semantics come from the prototypes and from the call
/// sites measured across the legacy tree. See the file header for the evidence behind each.
/// </para>
/// <para>
/// The operand widths are exact rather than widened, because PowerBuilder and C# use the same
/// type names for different widths: PowerBuilder <c>ulong</c> is 32-bit and maps to C#
/// <see cref="uint"/>, while PowerBuilder <c>uint</c> is 16-bit and maps to
/// <see cref="ushort"/>. The file header sets out the concrete wrong answers a name for name port
/// would return instead. The sibling <see cref="Enums"/> catalogue surfaces the
/// <c>STATE_*</c> and <c>REGEXP_*</c> bitmask families as <see cref="uint"/> for exactly this
/// reason, so a mask test needs no cast at the call site.
/// </para>
/// <para>
/// Every member is a total, pure function: nothing throws, nothing clamps, nothing reads ambient
/// state, and every input has a defined result. That mirrors the legacy, which is a thin shim
/// over a native call in which masking and wrapping are the behaviour rather than defects.
/// </para>
/// </remarks>
public static class Bits
{
    // ------------------------------------------------------------------------------------------
    // DECISION 1 - THE OPERAND WIDTHS ARE EXACT, WHICH IS WHY NO SIGNATURE MENTIONS ulong
    // ------------------------------------------------------------------------------------------
    // Restated at the top of the class body because it governs every member below and because a
    // maintainer reading only this far would otherwise see C# type names that look one size too
    // small for their legacy spellings.
    //
    //     legacy ulong / unsignedlong   32-bit   ->   uint
    //     legacy uint  / unsignedinteger 16-bit  ->   ushort
    //
    // A C# `ulong` anywhere in this file would be a 64-bit operand and therefore a defect, even
    // though the legacy prototypes say `ulong` throughout. See the WIDTH HAZARD block in the file
    // header for the four concrete divergences that follow from getting this wrong.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the bitwise AND of two 32-bit masks.
    /// </summary>
    /// <param name="a">The first 32-bit operand.</param>
    /// <param name="b">The second 32-bit operand.</param>
    /// <returns>The bits set in both <paramref name="a"/> and <paramref name="b"/>.</returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitand.srf</c>, prototype
    /// <c>ulong bitand(readonly ulong a, readonly ulong b)</c>. Both operands are 32-bit
    /// unsigned. With 258 call sites this is the second highest traffic member here, after
    /// <see cref="BitTest(uint, uint)"/>, and it is used throughout the legacy to extract a
    /// masked subset rather than to test for one.
    /// </remarks>
    public static uint BitAnd(uint a, uint b)
    {
        return a & b;
    }

    /// <summary>
    /// Returns the bitwise OR of two 32-bit masks.
    /// </summary>
    /// <param name="a">The first 32-bit operand.</param>
    /// <param name="b">The second 32-bit operand.</param>
    /// <returns>The bits set in either <paramref name="a"/> or <paramref name="b"/>.</returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitor.srf</c>, prototype
    /// <c>ulong bitor(readonly ulong a, readonly ulong b)</c>. Both operands are 32-bit unsigned.
    /// Ten call sites. Note that the legacy more often combines mask constants with <c>+</c> than
    /// by calling this member; the two agree while the combined bits are disjoint, which D-2 in
    /// the file header establishes for the families involved.
    /// </remarks>
    public static uint BitOr(uint a, uint b)
    {
        return a | b;
    }

    /// <summary>
    /// Returns the bitwise exclusive OR of two 32-bit masks.
    /// </summary>
    /// <param name="a">The first 32-bit operand.</param>
    /// <param name="b">The second 32-bit operand.</param>
    /// <returns>
    /// The bits set in exactly one of <paramref name="a"/> and <paramref name="b"/>.
    /// </returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitxor.srf</c>, prototype
    /// <c>ulong bitxor(readonly ulong a, readonly ulong b)</c>. Both operands are 32-bit unsigned.
    /// This member has no call site anywhere in the legacy tree, so its behaviour is taken from
    /// the prototype and from the single meaning exclusive or can have, not from usage. See D-6 in
    /// the file header.
    /// </remarks>
    public static uint BitXor(uint a, uint b)
    {
        return a ^ b;
    }

    /// <summary>
    /// Returns the one's complement of a 32-bit value.
    /// </summary>
    /// <param name="num">The 32-bit operand to complement.</param>
    /// <returns>The operand with every one of its 32 bits inverted.</returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitnot.srf</c>, prototype
    /// <c>ulong bitnot(readonly ulong num)</c>. The operand is 32-bit unsigned. No call site
    /// exercises it; see D-6 in the file header.
    /// </para>
    /// <para>
    /// This member is the clearest demonstration of why the parameter is <see cref="uint"/> and
    /// not <see cref="ulong"/>, and it is the reason the width mapping is stated as prominently
    /// as it is. The complement is taken across the operand's width, so the width is not an
    /// implementation detail but part of the result: <c>BitNot(1)</c> is <c>0xFFFFFFFE</c> over 32
    /// bits and <c>0xFFFFFFFFFFFFFFFE</c> over 64. A port that read the legacy <c>ulong</c> as a
    /// C# <see cref="ulong"/> would compile without a single diagnostic and return the second.
    /// </para>
    /// </remarks>
    public static uint BitNot(uint num)
    {
        return ~num;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 2 - THE SHIFT COUNT IS NOT RANGE CHECKED, AND ITS WRAP IS A CONSEQUENCE OF WIDTH
    // ------------------------------------------------------------------------------------------
    // C# defines the shift count for a 32-bit operand as the low FIVE bits of the right operand,
    // so a count of 32 shifts by 0, a count of 33 shifts by 1, and so on. That is the behaviour
    // reproduced here, unmodified, for the two shift members below.
    //
    // The important point is that this falls out of choosing the correct OPERAND width rather than
    // being coded for. Over a 32-bit operand the count masks to five bits and matches the legacy;
    // over a 64-bit operand C# would mask to SIX bits instead, so a count of 32 would shift by 32
    // rather than by 0 and every over-wide shift would return a different answer. Getting the
    // operand width right therefore gets the wrap right for free, which is why no explicit modulo
    // appears below.
    //
    // Neither member clamps or rejects an over-wide count, in keeping with the no-throw posture
    // described in the file header. The inference involved is recorded as I-3: the native body
    // cannot be read, but the x86 shift instructions mask the count to five bits for a 32-bit
    // operand in the same way, so a thin shim over them agrees with C#.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Shifts a 32-bit value left by the given number of bit positions.
    /// </summary>
    /// <param name="num">The 32-bit value to shift.</param>
    /// <param name="shift">
    /// The number of positions to shift by, as a 16-bit count. Only its low five bits are
    /// significant, so a count of 32 or more wraps; see the remarks.
    /// </param>
    /// <returns>
    /// The value shifted left, with vacated low bits set to zero and bits shifted past bit 31
    /// discarded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitlsh.srf</c>, prototype
    /// <c>ulong bitlsh(readonly ulong num, readonly uint shift)</c>. The value is 32-bit unsigned
    /// and the count is 16-bit unsigned, which is why <paramref name="shift"/> is a
    /// <see cref="ushort"/> rather than a wider integer.
    /// </para>
    /// <para>
    /// The single call site builds a processor affinity mask,
    /// <c>SetThreadAffinityMask(_handle, BitLSH(1, CPUIndex - 1))</c> at
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L489</c>, inside a function whose own
    /// parameter is declared <c>readonly unsignedinteger cpuindex</c> at L488. The count therefore
    /// demonstrably originates as a 16-bit value in the legacy, which corroborates the prototype
    /// independently. The affinity primitive it feeds is a deliberate non-port elsewhere in the
    /// plan, but this shift is a general purpose operation and is reproduced here regardless.
    /// </para>
    /// <para>
    /// A count of 32 or more wraps modulo 32 rather than yielding zero, and nothing is validated
    /// or clamped. See DECISION 2 above, and I-3 in the file header for the inference that
    /// underwrites it.
    /// </para>
    /// </remarks>
    public static uint BitLsh(uint num, ushort shift)
    {
        return num << shift;
    }

    /// <summary>
    /// Shifts a 32-bit value right by the given number of bit positions, filling with zeros.
    /// </summary>
    /// <param name="num">The 32-bit value to shift.</param>
    /// <param name="shift">
    /// The number of positions to shift by, as a 16-bit count. Only its low five bits are
    /// significant, so a count of 32 or more wraps; see the remarks.
    /// </param>
    /// <returns>
    /// The value shifted right, with vacated high bits set to zero and bits shifted past bit 0
    /// discarded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitrsh.srf</c>, prototype
    /// <c>ulong bitrsh(readonly ulong num, readonly uint shift)</c>. The value is 32-bit unsigned
    /// and the count is 16-bit unsigned. No call site exercises it; see D-6 in the file header.
    /// </para>
    /// <para>
    /// The shift is LOGICAL, meaning it fills the vacated high bits with zeros rather than
    /// propagating the top bit. That is guaranteed by the operand being unsigned rather than by
    /// anything written here: C# selects the arithmetic, sign propagating right shift only for a
    /// signed operand, so a port that used a signed type would sign extend and, for example, turn
    /// <c>0x80000000</c> shifted right by 31 into <c>0xFFFFFFFF</c> instead of <c>1</c>. The
    /// unsigned operand is therefore load bearing here, not incidental.
    /// </para>
    /// <para>
    /// A count of 32 or more wraps modulo 32, exactly as for
    /// <see cref="BitLsh(uint, ushort)"/>. See DECISION 2 above.
    /// </para>
    /// </remarks>
    public static uint BitRsh(uint num, ushort shift)
    {
        return num >> shift;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 3 - BitTest IS A MASK TEST WITH ANY-BIT SEMANTICS, NOT A SINGLE BIT TEST
    // ------------------------------------------------------------------------------------------
    // With 326 call sites this is the highest traffic member in the file, so getting it wrong
    // would be the most widely felt defect the port could contain. The body is unreadable, so the
    // semantics were recovered from the shape of the calls, and the evidence is conclusive in both
    // directions.
    //
    // It is not a SINGLE BIT test, because callers pass multi-bit masks that a single bit reading
    // could not accept at all:
    //     BitTest(state, Enums.STATE_HOVER + Enums.STATE_FOCUS)
    // It is not an ALL BITS test either, because the two states combined in that idiom are
    // mutually exclusive in practice, so an all-bits reading would make those tests permanently
    // false and the surrounding branches dead code.
    //
    // What remains is ANY-bit: true when the operand shares at least one bit with the mask. The
    // adjacent finding is recorded as D-2 in the file header: the legacy builds these masks with
    // `+` rather than `|`, which agrees only while the combined bits are disjoint. That condition
    // holds for the STATE_* family because it is a contiguous single bit family with no duplicate
    // [enums.sru:L96-L111], so no sum can carry. No caller is rewritten to use `|`; doing so would
    // be a behavioural change, and the idiom is recorded rather than corrected.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether a 32-bit value shares at least one bit with the given mask.
    /// </summary>
    /// <param name="num">The 32-bit value to test.</param>
    /// <param name="bits">
    /// The 32-bit mask to test against, which may name more than one bit.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when at least one bit of <paramref name="bits"/> is also set in
    /// <paramref name="num"/>; otherwise <see langword="false"/>. An empty mask yields
    /// <see langword="false"/>, because no bit can be shared with it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bittest.srf</c>, prototype
    /// <c>boolean bittest(readonly ulong num, readonly ulong bits)</c>. Both operands are 32-bit
    /// unsigned.
    /// </para>
    /// <para>
    /// The test is ANY-bit rather than all-bits or single-bit, which DECISION 3 above establishes
    /// from the call sites. Because <paramref name="bits"/> is a mask rather than a position, the
    /// caller passes the bit's VALUE and not its index: testing the least significant bit uses a
    /// mask of 1, not 0 or 1 as a position. <see cref="GetBit(uint, ushort)"/> is the member that
    /// takes a position, and the two must not be confused.
    /// </para>
    /// <para>
    /// The mask argument is typically an <see cref="Enums"/> constant or a sum of them, as in
    /// <c>BitTest(state, Enums.STATE_HOVER + Enums.STATE_FOCUS)</c>. That expression needs no cast
    /// because the catalogue surfaces those families as <see cref="uint"/>; see the reconciliation
    /// note in the file header.
    /// </para>
    /// </remarks>
    public static bool BitTest(uint num, uint bits)
    {
        return (num & bits) != 0u;
    }

    /// <summary>
    /// Returns a 32-bit value with the bits named by the given mask cleared.
    /// </summary>
    /// <param name="num">The 32-bit value to clear bits in.</param>
    /// <param name="bits">The 32-bit mask naming the bits to clear.</param>
    /// <returns>
    /// <paramref name="num"/> with every bit of <paramref name="bits"/> removed. An empty mask
    /// returns the value unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/bitclear.srf</c>, prototype
    /// <c>ulong bitclear (readonly unsignedlong num, readonly unsignedlong bits)</c>. Both
    /// operands are 32-bit unsigned. This is the one prototype of the fifteen that uses the long
    /// spelling <c>unsignedlong</c>, and it uses BOTH spellings in that single line, since its
    /// return type is written <c>ulong</c>. The two name the same type, so the inconsistency is
    /// cosmetic; it is recorded as D-5 in the file header only so a reader comparing prototypes
    /// does not look for a distinction that is not there.
    /// </para>
    /// <para>
    /// The single call site is
    /// <c>_nDisabledEvent = BitClear(_nDisabledEvent, evt)</c> at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L532</c>, which re-enables an
    /// event by removing its bit from a disabled event mask. That site also settles the validation
    /// question: its guard against a zero argument sits in the CALLER at L530, one line earlier,
    /// so rejecting a zero mask is demonstrably not this member's responsibility. An empty mask is
    /// consequently a well defined no-op here rather than an error, which is what D-3 in the file
    /// header records.
    /// </para>
    /// </remarks>
    public static uint BitClear(uint num, uint bits)
    {
        return num & ~bits;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 4 - GetBit's INDEX BASE IS AN INFERENCE, AND ITS OUT OF RANGE ANSWER IS A CHOICE
    // ------------------------------------------------------------------------------------------
    // This is the ONLY member in the file whose contract the repository does not settle, and it is
    // documented as unresolved rather than presented as ported. Two separate questions arise, and
    // both are recorded as inferences: I-1 and I-2 in the file header.
    //
    // WHAT THE EVIDENCE ACTUALLY IS: nothing. getbit.srf is declaration only, its body is inside
    // the closed pfw.dll, and a search of the entire repository finds no call site at all - the
    // only hit for the name is its own declaration. No comment anywhere documents the base. Unlike
    // BitXor, BitNot, BitRsh and MakeWord, which are equally uncalled but whose names admit exactly
    // one meaning, "the bit at position n" is ambiguous precisely because n needs an origin.
    //
    // I-1, THE BASE. One based is chosen, so bit 1 is the least significant bit. PowerBuilder is
    // pervasively one based - arrays start at 1 and its upper bound function returns the last valid
    // index - and the plan singles out one based to zero based translation as the single most
    // dangerous mechanical hazard in this refactor. A one based reading is therefore the likelier
    // intent for a PowerBuilder facing helper. It is still an inference, and if the behavioural
    // oracle later contradicts it the test named in the header is the single place to revise.
    //
    // I-2, THE OUT OF RANGE ANSWER. A position outside 1 to 32 returns false. This has to be
    // decided explicitly rather than left to the arithmetic, because the arithmetic's answer is
    // actively misleading. C# masks a shift count for a 32-bit operand to its low five bits, so
    // without the guard:
    //     bit = 0   ->  1u << -1  ->  count -1 & 31 = 31  ->  silently tests bit 31
    //     bit = 33  ->  1u << 32  ->  count 32 & 31 = 0   ->  silently tests bit 1
    // Both would return a confident answer about a DIFFERENT bit than the caller named. That is
    // not the same thing as the shift wrap accepted in DECISION 2: there the wrap IS the
    // operation's defined behaviour and has a native analogue, whereas here it would silently
    // answer a different question.
    //
    // The guard returns false rather than throwing, so the file's no-throw posture is intact and
    // this member remains a total function. False is the correct shape for the answer as well as
    // the safe one: a position that does not exist cannot hold a set bit.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reports whether the bit at the given ONE BASED position of a 32-bit value is set.
    /// </summary>
    /// <param name="num">The 32-bit value to test.</param>
    /// <param name="bit">
    /// The one based bit position, where 1 is the least significant bit and 32 the most
    /// significant. A position outside that range returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the named bit is set; <see langword="false"/> when it is clear
    /// and also when <paramref name="bit"/> is outside the range 1 to 32.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/getbit.srf</c>, prototype
    /// <c>boolean getbit(readonly ulong num, readonly uint bit)</c>. The value is 32-bit unsigned
    /// and the position is 16-bit unsigned.
    /// </para>
    /// <para>
    /// THE INDEX BASE IS INFERRED AND IS NOT VERIFIED BEHAVIOUR. The repository does not adjudicate
    /// whether <paramref name="bit"/> counts from zero or from one: the body is inside the closed
    /// <c>pfw.dll</c>, no call site anywhere exercises this member, and no comment records the
    /// base. One based was chosen because PowerBuilder is pervasively one based, which makes it the
    /// likelier intent, but the choice is pending characterization against the behavioural oracle
    /// and must be revised if the oracle contradicts it. It is tracked as I-1 in the file header
    /// together with the name of the test that pins it, so the choice can be found deliberately
    /// rather than discovered by accident. Callers who want certainty today should prefer
    /// <see cref="BitTest(uint, uint)"/>, whose mask semantics are established from 326 call sites.
    /// </para>
    /// <para>
    /// The behaviour for a position outside 1 to 32 is likewise a documented choice, tracked as
    /// I-2: it returns <see langword="false"/> and does not throw. DECISION 4 above explains why
    /// leaving that case to the shift arithmetic would silently report on a different bit than the
    /// one named.
    /// </para>
    /// </remarks>
    public static bool GetBit(uint num, ushort bit)
    {
        // Reject positions that name no bit of a 32-bit operand. Returning false keeps the member
        // total, and the guard is what stops the shift count below from wrapping to an unrelated
        // position; see DECISION 4 and I-2.
        if (bit < 1 || bit > 32)
        {
            return false;
        }

        // One based, so position 1 selects bit 0 of the machine word. The mask is built at 32-bit
        // width for the same width reason that governs the whole file: 1u rather than 1UL.
        return (num & (1u << (bit - 1))) != 0u;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 5 - THE SIX COMPOSITION MEMBERS FORM TWO FAMILIES AT DIFFERENT WIDTHS
    // ------------------------------------------------------------------------------------------
    // The remaining six split into a byte level family over a 16-bit word and a word level family
    // over a 32-bit value. The split is stated here because the member names do not reveal it and
    // because mixing the two is the easiest way to produce a plausible wrong number:
    //
    //     BYTE LEVEL   HiByte, LoByte, MakeWord     operate on a 16-bit word, halves are 8 bits
    //     WORD LEVEL   HiWord, LoWord, MakeLong     operate on a 32-bit value, halves are 16 bits
    //
    // MakeWord is the member that misleads. Its prototype declares both parameters PowerBuilder
    // uint, which is 16 bits, yet it combines two BYTES into a 16-bit word, exactly as the Win32
    // macro of the same name does. Its arguments are byte valued operands carried in 16-bit slots,
    // so the shift distance is 8 and not 16.
    //
    // PARAMETER ORDER IS LOW THEN HIGH in both composers, per the prototypes
    //     uint  makeword(readonly uint low, readonly uint high)
    //     ulong makelong(readonly uint low, readonly uint high)
    // which again matches the Win32 macros. The order is preserved verbatim: silently swapping it
    // to read high first would be a behavioural change that no diagnostic could catch, since both
    // parameters have the same type.
    //
    // MASKS IN THESE BODIES REPRODUCE LEGACY TRUNCATION AND ARE NOT NEW VALIDATION. Where a mask
    // cannot change the value because the parameter type already bounds it, it is retained to
    // document which bits the legacy contract says participate, and that is noted per member.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the high order byte of a 16-bit word.
    /// </summary>
    /// <param name="num">The 16-bit word to take the high byte of.</param>
    /// <returns>Bits 8 to 15 of <paramref name="num"/>, in the range 0 to 255.</returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/hibyte.srf</c>, prototype
    /// <c>uint hibyte(readonly uint num)</c>. The operand is a 16-bit unsigned WORD, so its two
    /// halves are bytes and the shift distance is 8. Two call sites.
    /// </para>
    /// <para>
    /// The result is returned as a <see cref="ushort"/> rather than a <see cref="byte"/> because
    /// the legacy return type is a 16-bit unsigned integer, and narrowing it would change the
    /// declared contract for no behavioural gain. The mask after the shift cannot change the value,
    /// since a 16-bit operand shifted right by 8 already fits in a byte; it is kept to state which
    /// bits participate. See DECISION 5.
    /// </para>
    /// </remarks>
    public static ushort HiByte(ushort num)
    {
        return (ushort)((num >> 8) & 0xFF);
    }

    /// <summary>
    /// Returns the low order byte of a 16-bit word.
    /// </summary>
    /// <param name="num">The 16-bit word to take the low byte of.</param>
    /// <returns>Bits 0 to 7 of <paramref name="num"/>, in the range 0 to 255.</returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/lobyte.srf</c>, prototype
    /// <c>uint lobyte(readonly uint num)</c>. The operand is a 16-bit unsigned WORD, so this is
    /// the lower of its two bytes and the mask is 8 bits wide. One call site. The result type
    /// follows the legacy 16-bit return type for the same reason given on
    /// <see cref="HiByte(ushort)"/>. Together the two members partition the operand exactly:
    /// <c>MakeWord(LoByte(w), HiByte(w))</c> reconstructs any <paramref name="num"/>.
    /// </remarks>
    public static ushort LoByte(ushort num)
    {
        return (ushort)(num & 0xFF);
    }

    /// <summary>
    /// Returns the high order 16-bit word of a 32-bit value.
    /// </summary>
    /// <param name="num">The 32-bit value to take the high word of.</param>
    /// <returns>Bits 16 to 31 of <paramref name="num"/>, as a 16-bit word.</returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/hiword.srf</c>, prototype
    /// <c>uint hiword(readonly ulong num)</c>. Note that the prototype's two type names differ in
    /// width and both differ from their C# namesakes: the operand is a 32-bit unsigned value and
    /// the result is a 16-bit unsigned word, so this signature takes a <see cref="uint"/> and
    /// returns a <see cref="ushort"/>. Fifteen call sites.
    /// </para>
    /// <para>
    /// This member is the second demonstration of the width hazard. The shift distance is 16
    /// because the halves of a 32-bit value are 16 bits wide; over a 64-bit operand the identical
    /// expression would return part of the low half instead, which is a wrong answer that compiles
    /// silently. See the WIDTH HAZARD block in the file header.
    /// </para>
    /// </remarks>
    public static ushort HiWord(uint num)
    {
        return (ushort)(num >> 16);
    }

    /// <summary>
    /// Returns the low order 16-bit word of a 32-bit value.
    /// </summary>
    /// <param name="num">The 32-bit value to take the low word of.</param>
    /// <returns>Bits 0 to 15 of <paramref name="num"/>, as a 16-bit word.</returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/loword.srf</c>, prototype
    /// <c>uint loword(readonly ulong num)</c>, so a 32-bit operand and a 16-bit result. Six call
    /// sites. The mask is 16 bits wide because that is the width of a word within a 32-bit value,
    /// and it reproduces the legacy truncation rather than validating anything. Together with
    /// <see cref="HiWord(uint)"/> this partitions the operand exactly, so
    /// <c>MakeLong(LoWord(x), HiWord(x))</c> reconstructs any <paramref name="num"/>.
    /// </remarks>
    public static ushort LoWord(uint num)
    {
        return (ushort)(num & 0xFFFFu);
    }

    /// <summary>
    /// Composes a 16-bit word from a low order and a high order BYTE.
    /// </summary>
    /// <param name="low">The byte to place in bits 0 to 7. Only its low 8 bits participate.</param>
    /// <param name="high">
    /// The byte to place in bits 8 to 15. Only its low 8 bits participate.
    /// </param>
    /// <returns>
    /// The 16-bit word whose low byte is <paramref name="low"/> and whose high byte is
    /// <paramref name="high"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/makeword.srf</c>, prototype
    /// <c>uint makeword(readonly uint low, readonly uint high)</c>. No call site exercises it; see
    /// D-6 in the file header.
    /// </para>
    /// <para>
    /// DESPITE ITS 16-BIT PARAMETER TYPES THIS IS A BYTE COMBINING OPERATION, mirroring the Win32
    /// macro of the same name. The parameters are declared 16-bit because that is the narrowest
    /// unsigned type PowerBuilder offers, but only the low 8 bits of each participate, so the shift
    /// distance is 8 rather than 16. Confusing it with <see cref="MakeLong(ushort, ushort)"/>, the
    /// word level composer, is the specific mistake DECISION 5 exists to prevent.
    /// </para>
    /// <para>
    /// The two masks here are the one place in this family where masking is not redundant: a
    /// caller may legitimately pass a value above 255 in a 16-bit slot, and the legacy discards its
    /// upper bits. That truncation is reproduced rather than rejected, in keeping with the file's
    /// no-throw posture. The inverse is <see cref="LoByte(ushort)"/> with
    /// <see cref="HiByte(ushort)"/>.
    /// </para>
    /// </remarks>
    public static ushort MakeWord(ushort low, ushort high)
    {
        return (ushort)(((high & 0xFF) << 8) | (low & 0xFF));
    }

    /// <summary>
    /// Composes a 32-bit value from a low order and a high order 16-bit WORD.
    /// </summary>
    /// <param name="low">The word to place in bits 0 to 15.</param>
    /// <param name="high">The word to place in bits 16 to 31.</param>
    /// <returns>
    /// The 32-bit value whose low word is <paramref name="low"/> and whose high word is
    /// <paramref name="high"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/makelong.srf</c>, prototype
    /// <c>ulong makelong(readonly uint low, readonly uint high)</c>. Both operands are 16-bit
    /// unsigned words and the result is a 32-bit unsigned value, which is why this returns
    /// <see cref="uint"/> and not <see cref="ulong"/>. Seven call sites.
    /// </para>
    /// <para>
    /// The name is retained verbatim even though "long" reads as a 64-bit type in C#. It refers to
    /// the PowerBuilder <c>ulong</c>, which is 32 bits, and renaming it would break the legacy
    /// spelling the plan requires be preserved. The parameter order is low then high, per the
    /// prototype; see DECISION 5 on why that order is load bearing.
    /// </para>
    /// <para>
    /// The high word is shifted by 16 because the halves of a 32-bit value are 16 bits wide. The
    /// mask on the low word cannot change it, since a <see cref="ushort"/> already fits in 16 bits;
    /// it is retained to state that only those bits participate, matching the legacy contract. The
    /// inverse is <see cref="LoWord(uint)"/> with <see cref="HiWord(uint)"/>.
    /// </para>
    /// </remarks>
    public static uint MakeLong(ushort low, ushort high)
    {
        return ((uint)high << 16) | ((uint)low & 0xFFFFu);
    }
}
