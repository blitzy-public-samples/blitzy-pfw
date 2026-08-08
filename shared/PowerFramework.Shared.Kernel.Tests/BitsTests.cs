// ==============================================================================================
//  BitsTests - the width fidelity suite for PowerFramework.Shared.Kernel.Bits
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  shared/PowerFramework.Shared.Kernel/Bits.cs, which ports the fifteen global
//                   functions declared under ws_objects/pfw.common.pbl.src/.
//
//  WHY THIS SUITE IS SCOPED TO WIDTH, AND NOT TO ARITHMETIC
//  --------------------------------------------------------------------------------------------
//  This is the first thing to understand about this file, because it explains what is asserted
//  below and, just as importantly, what is deliberately not.
//
//  All fifteen legacy functions are DECLARATION ONLY. Each .srf is nine or ten lines and reads
//
//      L3   global type <name> from function_object native "pfw.dll"
//      L7   global function  <return> <name>(readonly <type> <arg> ...)
//
//  and nothing else. There is no PowerScript body to port and none to read: the bodies live
//  inside the closed pfw.dll, and the repository contains no C++ source of any kind, so no
//  implementation of any of the fifteen can be inspected anywhere. What the repository DOES
//  establish is exactly two things:
//
//      (a) the DECLARED PARAMETER AND RETURN WIDTHS, on L7 of each file, and
//      (b) the NAMES, which are the canonical Win32 bit, word and byte macro names.
//
//  Width is therefore precisely the property that is provable from the repository, which is why
//  the requirements scope this suite to it. There is also no behavioural oracle to characterize
//  anything finer against: among the 47 w_test_*.srw windows in ws_objects/pfw.tests.pbl.src
//  there is NO w_test_bits, so these primitives have no legacy test window either. The plan's
//  native-binding decision matrix classifies them as a straight SUBSTITUTION to the C# operators
//  rather than as a characterization target, and this suite pins that substitution's width
//  contract plus the canonical macro semantics the names denote. It does not invent behaviour the
//  prototypes do not establish, and it asserts no arithmetic that the operator semantics and the
//  as-built port do not jointly fix.
//
//  C-K - THE TECHNOLOGY DECISION THIS SUITE EXISTS TO PIN
//  --------------------------------------------------------------------------------------------
//  PowerBuilder and C# spell DIFFERENT WIDTHS WITH THE SAME TYPE NAMES. That single fact is the
//  whole reason this file exists.
//
//      PowerBuilder ulong / unsignedlong      32-bit unsigned   ->   C# uint     NOT C# ulong
//      PowerBuilder uint  / unsignedinteger   16-bit unsigned   ->   C# ushort   NOT C# uint
//
//  A name for name port is therefore WRONG in every one of the fifteen signatures, and wrong in
//  the most dangerous possible way: it COMPILES CLEANLY, raises no warning of any kind, and
//  returns a plausible looking number. No type error, no overflow diagnostic, no test failure
//  unless a test is written specifically to look for it. These tests are that test.
//
//  Two classic errors are targeted explicitly, and each has a named guard below.
//
//      64-BIT WIDENING. Reading the legacy `ulong` as a C# `ulong` doubles every operand width.
//      BitNot is the sharpest detector, because the complement is taken ACROSS the operand's
//      width, so the width is part of the answer rather than an implementation detail:
//      BitNot(0) is 0xFFFFFFFF over 32 bits and 0xFFFFFFFFFFFFFFFF over 64, and BitNot(0xFFFFFFFF)
//      is 0 over 32 bits but a large non-zero value over 64. Region 1 pins both.
//
//      TRANSPOSED COMPOSITION ARGUMENTS. Both composers take LOW FIRST:
//          makeword.srf:L7   uint  makeword(readonly uint low, readonly uint high)
//          makelong.srf:L7   ulong makelong(readonly uint low, readonly uint high)
//      which is the inverse of how the composed result reads left to right, and is the order the
//      Win32 macros of the same names use. Transposing it cannot be caught by any diagnostic,
//      because both parameters have the SAME TYPE, and it yields a plausible wrong answer. Region
//      4 therefore carries asymmetric rows whose two arguments differ, which fail immediately if
//      the order is flipped, alongside symmetric rows that would pass either way and are kept
//      only as boundary coverage. Region 5 re-proves the ordering independently.
//
//  A third asymmetry is worth stating because the member names actively mislead about it. The six
//  composition primitives split into two families that operate at DIFFERENT widths, and the split
//  does not follow the parameter spellings:
//
//      BYTE LEVEL, over a 16-bit word     HiByte, LoByte, MakeWord      halves are 8 bits
//      WORD LEVEL, over a 32-bit value    HiWord, LoWord, MakeLong      halves are 16 bits
//
//  So HiWord and LoWord NARROW, taking a 32-bit operand to a 16-bit result, while HiByte and
//  LoByte do NOT change width, taking a 16-bit operand to a 16-bit result in which the extracted
//  byte is returned widened. And MakeWord, despite 16-bit parameter spellings, combines two BYTES
//  with a shift distance of 8 rather than 16. Region 3's 0x1234 row is the guard against a HiByte
//  implemented as a shift by 16.
//
//  WHY THE WIDTH DISCIPLINE MATTERS BEYOND THIS ONE FILE
//  --------------------------------------------------------------------------------------------
//  Bits is among the highest traffic members of the whole shared layer. BitTest is the single
//  most called member in it and BitAnd the second, and their operands are the STATE_* and
//  REGEXP_* bitmask families from the legacy constant catalogue. A width error here would corrupt
//  mask arithmetic across all four services rather than failing locally, which is why this suite
//  is deliberately heavier than the arithmetic it covers would otherwise justify.
//
//  HOW THE WIDTH CONTRACT IS ASSERTED - TWO INDEPENDENT MECHANISMS
//  --------------------------------------------------------------------------------------------
//  A value assertion alone cannot pin a width, and this is the trap the suite is built to avoid:
//  a row asserting that the high word of 0xFFFFFFFF is 0xFFFF passes whether the declared return
//  type is 16-bit or 32-bit, because 0xFFFF is representable in both. Both mechanisms below are
//  therefore present, and neither is redundant.
//
//      REFLECTION, in Region 0. Reads the DECLARED return and parameter types of all fifteen
//      members and compares their CLR names against the prototype table. UInt32 is 32-bit and
//      UInt64 is 64-bit, so a name comparison here IS a width comparison, and it fails on a
//      widened signature even when every value in the suite would still have passed.
//
//      COMPILE TIME TYPED LOCALS, in Regions 2 and 3. Each extraction result is assigned to an
//      explicitly declared `ushort` local. Only `byte`, `sbyte` and `char` convert implicitly to
//      `ushort`, so a member that returned `uint` would make these assignments CS0266 and break
//      the build rather than fail a test. That is a stronger guarantee than an assertion, and it
//      is why the locals are declared with their type spelled out instead of `var`.
//
//  C-B - NO BEHAVIOUR IMPROVEMENT IS ASSERTED ANYWHERE IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  No test here asserts range validation, argument checking, clamping or saturation, because the
//  legacy prototypes promise none of it. Nothing asserts that any member throws; every member is
//  treated as the total function the legacy shim is. Where truncation or wrapping is asserted, it
//  is asserted as the operation's own behaviour and is labelled as such, never as a guard.
//
//  ONE FINDING IS REPORTED RATHER THAN ENSHRINED. The as-built GetBit carries a range guard that
//  the prototype does not promise, returning false for a position outside 1 to 32. It is reported
//  here as a finding, and the reason it is nonetheless covered by a test is set out in full at
//  Region 9: the guard keeps GetBit a TOTAL function, it introduces no throw and no clamp, and
//  the covering test is labelled as pinning a PORT-SIDE INFERENCE rather than legacy behaviour,
//  so nothing about it is presented as verified.
//
//  THREE PORT-SIDE INFERENCES ARE PINNED, AND ALL THREE ARE LABELLED AS INFERENCES
//  --------------------------------------------------------------------------------------------
//  Three of the port's contracts are not settled by the repository, and Bits.cs says so, tracking
//  them as I-1, I-2 and I-3 and naming the test that pins each. Those three tests are named
//  below EXACTLY as that file names them, so the cross-references resolve and a later
//  characterization run against the behavioural oracle can find the choices deliberately instead
//  of discovering them by accident:
//
//      I-1   GetBit numbers bits from ONE          GetBitInferredIndexBaseIsOneBased
//      I-2   an out of range position yields false GetBitInferredIndexBaseOutOfRangeReturnsFalse
//      I-3   a shift count wraps modulo 32         BitLshShiftCountWrapsModuloThirtyTwoInferred
//
//  Each of the three carries a comment saying it is CHARACTERIZED FROM THE PORT and is not
//  claimed as legacy behaviour. If the oracle later contradicts one, that test is the single
//  place to revise. Everything outside those three is either a declared width, a canonical macro
//  identity, or an algebraic property of the operator the plan sanctions as the substitute.
//
//  ONE CONTRACT IS SETTLED FROM THE CORPUS RATHER THAN INFERRED
//  --------------------------------------------------------------------------------------------
//  BitTest is a MASK test with ANY-BIT semantics, and that is derived rather than assumed.
//  Bits.cs recovers it from the shape of its call sites, which is conclusive in both directions:
//  callers pass two-bit masks, which a single-bit reading could not accept at all, and the two
//  states so combined are mutually exclusive in practice, which an all-bits reading would render
//  permanently false. Region 8 pins the surviving reading and includes a row that each of the two
//  rejected readings would fail, so the suite states the discrimination rather than merely
//  benefiting from it.
//
//  SHAPE, AND WHAT IS DELIBERATELY NOT REFERENCED
//  --------------------------------------------------------------------------------------------
//  The prescribed shape for a parity suite in this refactor is a table-driven matrix expressed as
//  theories with member data, and that is the shape used throughout, with strongly typed
//  TheoryData providers so a row that does not match its consumer is a compile error rather than
//  a run-time surprise. Every region cites the .srf locator its width contract comes from.
//
//  The constant catalogue is NOT referenced from any test below, even though the masks these
//  members really receive are its STATE_* and REGEXP_* families. Coupling a primitive's own suite
//  to a second ported artifact would mean a change in the catalogue could redden this file, so
//  every mask here is written as a literal and the catalogue appears in commentary only.
//
//  NO LEGACY PATH IS OPENED AT RUNTIME. Every ws_objects reference in this file is a comment. The
//  legacy tree is read only and is the behavioural oracle, never a test input, and nothing here
//  reads, writes or probes for any file at all. The suite is fully in process, synchronous and
//  deterministic: it touches no clock, no culture, no socket and no file system, so it has no
//  await to thread the ambient test cancellation token through and needs no determinism seam.
//
//  NAMING. The repository root .editorconfig scopes its naming-analyzer suppressions by file glob
//  to the ten implementation files that genuinely carry preserved legacy SCREAMING_SNAKE constant
//  identifiers. NO test file is covered by any of them, and warnings are errors. Every identifier
//  declared here is therefore conventionally named, and this file declares no SCREAMING_SNAKE and
//  no underscore-bearing identifier of any kind.
// ==============================================================================================

using System.Reflection;

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Width fidelity tests for <see cref="Bits"/>, the fifteen bit, word and byte primitives ported
/// from the PBNI declarations in the legacy <c>pfw.common</c> library.
/// </summary>
/// <remarks>
/// <para>
/// The type under test resolves by simple name with no using directive, because this file's
/// namespace <c>PowerFramework.Shared.Kernel.Tests</c> is nested inside
/// <c>PowerFramework.Shared.Kernel</c>, so simple-name lookup walks up into the enclosing
/// namespace and finds <c>Bits</c> there. The redundant directive is omitted rather than added.
/// </para>
/// <para>
/// The suite's named obligation is width, because width is the only property the repository
/// establishes: all fifteen legacy functions are declaration-only over the closed <c>pfw.dll</c>.
/// See the file header for the full statement of what that permits and forbids, for the two
/// independent mechanisms used to pin a declared width, and for the three port-side inferences
/// that are labelled as inferences rather than presented as verified behaviour.
/// </para>
/// </remarks>
public sealed class BitsTests
{
    // ==========================================================================================
    // REGION 0 - THE DECLARED SIGNATURE CONTRACT
    //
    // Oracle: L7 of each of the fifteen files under ws_objects/pfw.common.pbl.src/, which is the
    //         entire specification available for them. Each row below transcribes one prototype's
    //         declared widths through the PowerBuilder to C# mapping stated in the file header.
    //
    // This region is the load-bearing one, and it is first because everything after it asserts
    // values that a widened signature could still produce. Reflection reads what the port
    // DECLARES rather than what it returns for a particular input, so this is the only part of
    // the suite that fails on a widening even when no value changes.
    //
    // The expected types are given as CLR simple names. That is a width comparison and not a
    // spelling convention: UInt32 is the 32-bit unsigned type and UInt64 the 64-bit one, so a
    // port that widened an operand would report UInt64 here and the row would fail. Likewise
    // UInt16 against UInt32 for the six composition members, three of which narrow.
    //
    //      UInt32   C# uint     32 bits   <-  legacy ulong / unsignedlong
    //      UInt16   C# ushort   16 bits   <-  legacy uint  / unsignedinteger
    //      Boolean  C# bool               <-  legacy boolean
    // ==========================================================================================

    /// <summary>
    /// The fifteen declared signatures, one row per legacy prototype, as
    /// (member name, expected return type, expected comma-separated parameter types).
    /// </summary>
    /// <returns>A row per member of <see cref="Bits"/>.</returns>
    public static TheoryData<string, string, string> DeclaredSignatureRows()
    {
        TheoryData<string, string, string> rows = [];

        // ---- The nine bit operations ----
        // bitand.srf:L7    ulong bitand(readonly ulong a, readonly ulong b)
        rows.Add("BitAnd", "UInt32", "UInt32, UInt32");

        // bitor.srf:L7     ulong bitor(readonly ulong a, readonly ulong b)
        rows.Add("BitOr", "UInt32", "UInt32, UInt32");

        // bitxor.srf:L7    ulong bitxor(readonly ulong a, readonly ulong b)
        rows.Add("BitXor", "UInt32", "UInt32, UInt32");

        // bitnot.srf:L7    ulong bitnot(readonly ulong num)
        rows.Add("BitNot", "UInt32", "UInt32");

        // bitlsh.srf:L7    ulong bitlsh(readonly ulong num, readonly uint shift)
        // MIXED WIDTHS IN ONE PROTOTYPE: a 32-bit value and a 16-bit shift count.
        rows.Add("BitLsh", "UInt32", "UInt32, UInt16");

        // bitrsh.srf:L7    ulong bitrsh(readonly ulong num, readonly uint shift)
        rows.Add("BitRsh", "UInt32", "UInt32, UInt16");

        // bittest.srf:L7   boolean bittest(readonly ulong num, readonly ulong bits)
        // Both operands are 32-bit: `bits` is a MASK, not a position, which is what separates
        // this member from GetBit below.
        rows.Add("BitTest", "Boolean", "UInt32, UInt32");

        // getbit.srf:L7    boolean getbit(readonly ulong num, readonly uint bit)
        // A 32-bit value and a 16-bit POSITION. The contrast with BitTest's 32-bit mask is
        // visible in the widths themselves.
        rows.Add("GetBit", "Boolean", "UInt32, UInt16");

        // bitclear.srf:L7  ulong bitclear (readonly unsignedlong num, readonly unsignedlong bits)
        // The one prototype of the fifteen written with the long-form spelling, and it uses BOTH
        // spellings in that single line, since its return type is written `ulong`. The two name
        // the same 32-bit unsigned type, so the inconsistency is cosmetic and this row is
        // identical in width to BitAnd's. It is transcribed here only so a reader comparing the
        // prototypes does not go looking for a distinction that is not there.
        rows.Add("BitClear", "UInt32", "UInt32, UInt32");

        // ---- The byte-level composition family, over a 16-bit word ----
        // hibyte.srf:L7    uint hibyte(readonly uint num)
        // NO WIDTH CHANGE: a 16-bit operand and a 16-bit result. The extracted byte is returned
        // widened into a 16-bit value rather than as a byte, because that is the declared return
        // type; narrowing it to `byte` would change the contract for no behavioural gain.
        rows.Add("HiByte", "UInt16", "UInt16");

        // lobyte.srf:L7    uint lobyte(readonly uint num)
        rows.Add("LoByte", "UInt16", "UInt16");

        // makeword.srf:L7  uint makeword(readonly uint low, readonly uint high)
        // Two 16-bit slots to a 16-bit result, and LOW FIRST. Despite the 16-bit parameter
        // spellings this combines two BYTES, so only the low 8 bits of each participate; see
        // Region 4.
        rows.Add("MakeWord", "UInt16", "UInt16, UInt16");

        // ---- The word-level composition family, over a 32-bit value ----
        // hiword.srf:L7    uint hiword(readonly ulong num)
        // NARROWING, and the clearest single illustration of the hazard: the prototype's two type
        // names differ in width from each other AND both differ from their C# namesakes.
        rows.Add("HiWord", "UInt16", "UInt32");

        // loword.srf:L7    uint loword(readonly ulong num)
        rows.Add("LoWord", "UInt16", "UInt32");

        // makelong.srf:L7  ulong makelong(readonly uint low, readonly uint high)
        // Two 16-bit words to a 32-bit value, LOW FIRST. The name is retained verbatim even
        // though "long" reads as 64-bit in C#: it refers to the PowerBuilder ulong, which is 32
        // bits, which is why the expected return type here is UInt32 and not UInt64.
        rows.Add("MakeLong", "UInt32", "UInt16, UInt16");

        return rows;
    }

    /// <summary>
    /// Proves that every member's DECLARED return and parameter widths match its legacy
    /// prototype, and that each prototype is represented by exactly one member.
    /// </summary>
    /// <param name="memberName">The member of <see cref="Bits"/> to inspect.</param>
    /// <param name="expectedReturnTypeName">The CLR simple name the return type must have.</param>
    /// <param name="expectedParameterTypeNames">
    /// The CLR simple names the parameter types must have, in declaration order, comma separated.
    /// </param>
    [Theory]
    [MemberData(nameof(DeclaredSignatureRows))]
    public void DeclaredSignatureMatchesTheLegacyPrototypeWidths(
        string memberName,
        string expectedReturnTypeName,
        string expectedParameterTypeNames)
    {
        MethodInfo[] candidates = typeof(Bits)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == memberName)
            .ToArray();

        // C-B - EXACTLY ONE MEMBER PER PROTOTYPE, SO NO WIDENING OVERLOAD HAS BEEN ADDED.
        // A second candidate would mean a convenience overload taking a wider type had appeared
        // alongside the exact one. That is the specific door the width discipline exists to keep
        // shut, because such an overload lets a caller pass a 64-bit value that silently
        // truncates, and overload resolution would prefer it for an untyped literal.
        MethodInfo member = Assert.Single(candidates);

        // THE WIDTH ASSERTIONS. UInt32 against UInt64, and UInt16 against UInt32, are the two
        // comparisons that catch a name for name port of the legacy spellings.
        Assert.Equal(expectedReturnTypeName, member.ReturnType.Name);

        string actualParameterTypeNames = string.Join(
            ", ",
            member.GetParameters().Select(parameter => parameter.ParameterType.Name));

        Assert.Equal(expectedParameterTypeNames, actualParameterTypeNames);
    }

    /// <summary>
    /// Proves that the public surface of <see cref="Bits"/> is exactly the fifteen ported
    /// prototypes, with nothing added and nothing missing.
    /// </summary>
    [Fact]
    public void PublicSurfaceIsExactlyTheFifteenPortedPrototypes()
    {
        // A static class compiles to abstract plus sealed, so this pins the shape the plan's
        // object-kind mapping calls for: a *.srf global function becomes a static method on a
        // role-named static class, never an instance method on something constructible.
        Assert.True(typeof(Bits).IsAbstract, "Bits must be a static class.");
        Assert.True(typeof(Bits).IsSealed, "Bits must be a static class.");

        string[] expectedMembers =
        [
            "BitAnd",
            "BitClear",
            "BitLsh",
            "BitNot",
            "BitOr",
            "BitRsh",
            "BitTest",
            "BitXor",
            "GetBit",
            "HiByte",
            "HiWord",
            "LoByte",
            "LoWord",
            "MakeLong",
            "MakeWord",
        ];

        string[] actualMembers = typeof(Bits)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Asserting the SET rather than only the count catches a rename as well as an addition,
        // and a rename matters here because the legacy spellings are preserved deliberately: the
        // plan requires BitLsh to stay BitLsh rather than becoming ShiftLeft.
        Assert.Equal(expectedMembers, actualMembers);

        // No public instance surface, because a static class can have none and this states that
        // the port did not grow one.
        Assert.Empty(typeof(Bits).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        // Bits declares no constant and no property. This is worth pinning rather than assuming,
        // because it is the reason Bits.cs is NOT one of the ten files the root .editorconfig
        // scopes its naming suppressions to: a file that carries no preserved SCREAMING_SNAKE
        // identifier needs no suppression, and adding one here later would be a signal that a
        // constant had migrated into the wrong file.
        Assert.Empty(typeof(Bits).GetFields(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(Bits).GetProperties(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    // ==========================================================================================
    // REGION 1 - BitNot, THE 64-BIT WIDENING DETECTOR
    //
    // Oracle: ws_objects/pfw.common.pbl.src/bitnot.srf:L7
    //             global function  ulong bitnot(readonly ulong num)
    //         so a 32-bit unsigned operand and a 32-bit unsigned result.
    //
    // C-K - WHY THIS IS THE SINGLE BEST WIDTH DETECTOR IN THE SUITE.
    // The one's complement is taken ACROSS THE OPERAND'S WIDTH, so the width is not an
    // implementation detail here but part of the result itself. Every other member in this file
    // can be made to look correct at a wrong width by choosing a small enough operand; BitNot
    // cannot, because it always sets the bits the width provides:
    //
    //     BitNot(0)           over 32 bits  0xFFFFFFFF          over 64 bits  0xFFFFFFFFFFFFFFFF
    //     BitNot(0xFFFFFFFF)  over 32 bits  0                   over 64 bits  0xFFFFFFFF00000000
    //     BitNot(1)           over 32 bits  0xFFFFFFFE          over 64 bits  0xFFFFFFFFFFFFFFFE
    //
    // The second row is the sharpest of the three and is the reason it is here. A widened port
    // returns a large NON-ZERO value where the contract requires exactly zero, so the failure is
    // unmistakable rather than a near miss. Note that a port whose operand was widened would also
    // fail Region 0 on its declared type; these rows are the value-level confirmation that the
    // arithmetic follows the declaration rather than merely being annotated with it.
    //
    // This member has no call site anywhere in the legacy tree, so its meaning comes from the
    // prototype and from the single meaning a one's complement can have. That is not a gap: unlike
    // "the bit at position n", "the complement of n" needs no further origin to be unambiguous.
    // ==========================================================================================

    /// <summary>
    /// One's-complement rows for <see cref="Bits.BitNot(uint)"/>, as (operand, expected result).
    /// </summary>
    /// <returns>Rows covering zero, the 32-bit maximum, and both extreme single-bit operands.</returns>
    public static TheoryData<uint, uint> ComplementRows()
    {
        TheoryData<uint, uint> rows = [];

        // Zero complements to every bit the 32-bit width provides, and to no more than those.
        rows.Add(0u, 0xFFFFFFFFu);

        // The 32-bit maximum complements to EXACTLY zero. A 64-bit port returns a large non-zero
        // value here, which makes this the decisive row of the suite.
        rows.Add(0xFFFFFFFFu, 0u);

        // The least significant bit only, so all thirty-one other bits must come back set.
        rows.Add(0x00000001u, 0xFFFFFFFEu);

        // The most significant bit of a 32-bit value. Its complement is the largest value with
        // that bit clear, which pins where the top of the operand actually is.
        rows.Add(0x80000000u, 0x7FFFFFFFu);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitNot(uint)"/> complements across exactly 32 bits.
    /// </summary>
    /// <param name="operand">The value to complement.</param>
    /// <param name="expected">The complement the 32-bit contract requires.</param>
    [Theory]
    [MemberData(nameof(ComplementRows))]
    public void ComplementInvertsExactlyThirtyTwoBits(uint operand, uint expected)
    {
        Assert.Equal(expected, Bits.BitNot(operand));
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitNot(uint)"/> is its own inverse, so complementing twice
    /// returns the original value across the whole 32-bit range.
    /// </summary>
    /// <param name="operand">The value to complement twice.</param>
    /// <param name="expected">
    /// The same value, restated as the expected result so the row reads as an identity rather than
    /// as a tautology over a single variable.
    /// </param>
    [Theory]
    [MemberData(nameof(ComplementRows))]
    public void ComplementIsItsOwnInverse(uint operand, uint expected)
    {
        // Involution is a genuinely independent check on the width rather than a restatement of
        // the row above. A port that complemented at 32 bits but STORED the result more widely
        // would satisfy the single-application rows only by truncation on the way out, and would
        // then reveal itself on the second application. Both arguments are used: the round trip is
        // asserted on the operand, and the expected column is exercised in the same direction so
        // the identity is pinned from both ends of every row.
        Assert.Equal(operand, Bits.BitNot(Bits.BitNot(operand)));
        Assert.Equal(expected, Bits.BitNot(Bits.BitNot(expected)));
    }

    // ==========================================================================================
    // REGION 2 - WORD EXTRACTION. HiWord AND LoWord NARROW 32 BITS TO 16.
    //
    // Oracle: ws_objects/pfw.common.pbl.src/hiword.srf:L7
    //             global function  uint hiword(readonly ulong num)
    //         ws_objects/pfw.common.pbl.src/loword.srf:L7
    //             global function  uint loword(readonly ulong num)
    //
    // C-K - THESE TWO PROTOTYPES ARE WHERE THE WIDTH HAZARD IS MOST VISIBLE, because a single
    // line of legacy declaration contains BOTH mappings at once and the two spellings differ in
    // width from each other as well as from their C# namesakes:
    //
    //     legacy `uint`  return  ->  16 bits  ->  C# ushort
    //     legacy `ulong` operand ->  32 bits  ->  C# uint
    //
    // So the shift distance is 16, because the halves of a 32-bit value are 16 bits wide. Over a
    // 64-bit operand the identical expression returns part of the LOW half instead, which is a
    // wrong answer that compiles in silence.
    //
    // WHY A VALUE ASSERTION ALONE IS NOT ENOUGH HERE, AND WHAT IS DONE ABOUT IT.
    // The 0xFFFFFFFF row expects 0xFFFF from both members, and 0xFFFF is representable in a
    // 32-bit type just as well as in a 16-bit one, so that row proves nothing at all about the
    // return width on its own. Region 0 pins the declared type by reflection, and each test below
    // additionally assigns the result to an explicitly declared `ushort` local. Only byte, sbyte
    // and char convert implicitly to ushort, so a member returning uint would make those
    // assignments CS0266 and BREAK THE BUILD rather than pass quietly. The locals are therefore
    // spelled with their type rather than declared `var`, and doing so is load bearing.
    //
    // Together the two members partition the operand exactly, which Region 5 exploits.
    // ==========================================================================================

    /// <summary>
    /// Word-extraction rows, as (32-bit value, expected high word, expected low word).
    /// </summary>
    /// <returns>Rows covering zero, a fully asymmetric value, and the three width boundaries.</returns>
    public static TheoryData<uint, ushort, ushort> WordExtractionRows()
    {
        TheoryData<uint, ushort, ushort> rows = [];

        // Zero in both halves.
        rows.Add(0u, (ushort)0x0000, (ushort)0x0000);

        // THE PRINCIPAL ROW. Every one of the eight nibbles differs, so it pins WHICH half each
        // member returns and cannot be satisfied by returning the wrong one. A member that
        // confused the halves would report 0x5678 as the high word, and a member that used the
        // byte-level shift distance of 8 instead of 16 would report 0x3456.
        rows.Add(0x12345678u, (ushort)0x1234, (ushort)0x5678);

        // Both halves saturated. This is the row that pins the MASK WIDTH: a member that masked
        // with more than 16 bits would return more than 0xFFFF and fail. It says nothing about the
        // return type's width on its own, which is exactly why the typed local below exists.
        rows.Add(0xFFFFFFFFu, (ushort)0xFFFF, (ushort)0xFFFF);

        // Low half only, so the high word must be zero. A port that failed to shift at all would
        // return 0xFFFF here for the high word.
        rows.Add(0x0000FFFFu, (ushort)0x0000, (ushort)0xFFFF);

        // High half only, so the low word must be zero. A port that failed to mask would return
        // 0xFFFF here for the low word.
        rows.Add(0xFFFF0000u, (ushort)0xFFFF, (ushort)0x0000);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.HiWord(uint)"/> and <see cref="Bits.LoWord(uint)"/> split a
    /// 32-bit value into its two 16-bit halves, and that both return a 16-bit type.
    /// </summary>
    /// <param name="value">The 32-bit value to split.</param>
    /// <param name="expectedHighWord">Bits 16 to 31 of <paramref name="value"/>.</param>
    /// <param name="expectedLowWord">Bits 0 to 15 of <paramref name="value"/>.</param>
    [Theory]
    [MemberData(nameof(WordExtractionRows))]
    public void WordExtractionSplitsThirtyTwoBitsIntoTwoSixteenBitHalves(
        uint value,
        ushort expectedHighWord,
        ushort expectedLowWord)
    {
        // THE COMPILE-TIME WIDTH PIN. These two locals are declared `ushort` deliberately and must
        // not be relaxed to `var`. If either member's return type were widened to uint, these
        // assignments would fail to compile with CS0266 rather than fail as a test, which is a
        // stronger guarantee than any assertion can give. See the note in the region banner.
        ushort actualHighWord = Bits.HiWord(value);
        ushort actualLowWord = Bits.LoWord(value);

        Assert.Equal(expectedHighWord, actualHighWord);
        Assert.Equal(expectedLowWord, actualLowWord);
    }

    // ==========================================================================================
    // REGION 3 - BYTE EXTRACTION. HiByte AND LoByte DO NOT CHANGE WIDTH.
    //
    // Oracle: ws_objects/pfw.common.pbl.src/hibyte.srf:L7
    //             global function  uint hibyte(readonly uint num)
    //         ws_objects/pfw.common.pbl.src/lobyte.srf:L7
    //             global function  uint lobyte(readonly uint num)
    //
    // C-K - THE ASYMMETRY AGAINST REGION 2, WHICH THE NAMES DO NOT REVEAL. Both spellings on
    // these two prototypes are the legacy 16-bit `uint`, so unlike HiWord and LoWord these
    // members do NOT narrow: a 16-bit operand yields a 16-bit result in which the extracted byte
    // is returned WIDENED. The result is deliberately not a `byte`, because the declared return
    // type is a 16-bit unsigned integer and narrowing it would change the contract for no
    // behavioural gain.
    //
    // The operand being a 16-bit WORD is what fixes the shift distance at 8 rather than 16, since
    // the halves of a word are bytes. The 0x1234 row below is the specific guard against a HiByte
    // implemented as a shift by 16: such a port would return 0 there, and 0 again for the 0xFF00
    // row, so the mistake is caught twice.
    // ==========================================================================================

    /// <summary>
    /// Byte-extraction rows, as (16-bit word, expected high byte, expected low byte).
    /// </summary>
    /// <returns>Rows covering zero, an asymmetric word, and the three width boundaries.</returns>
    public static TheoryData<ushort, ushort, ushort> ByteExtractionRows()
    {
        TheoryData<ushort, ushort, ushort> rows = [];

        // Zero in both bytes.
        rows.Add((ushort)0x0000, (ushort)0x00, (ushort)0x00);

        // THE SHIFT-DISTANCE GUARD. Both bytes differ, so this row pins the distance at 8: a
        // HiByte written as a shift by 16 returns 0 instead of 0x12, and one written as a shift by
        // 4 returns 0x23. It is also the row that catches a member returning the wrong half.
        rows.Add((ushort)0x1234, (ushort)0x12, (ushort)0x34);

        // Both bytes saturated, which pins the mask at 8 bits: a member masking more widely would
        // return more than 0xFF.
        rows.Add((ushort)0xFFFF, (ushort)0xFF, (ushort)0xFF);

        // Low byte only, so the high byte must be zero.
        rows.Add((ushort)0x00FF, (ushort)0x00, (ushort)0xFF);

        // High byte only, so the low byte must be zero. This is the second row that a shift by 16
        // would fail.
        rows.Add((ushort)0xFF00, (ushort)0xFF, (ushort)0x00);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.HiByte(ushort)"/> and <see cref="Bits.LoByte(ushort)"/> split a
    /// 16-bit word into its two bytes, each returned widened into a 16-bit result.
    /// </summary>
    /// <param name="word">The 16-bit word to split.</param>
    /// <param name="expectedHighByte">Bits 8 to 15 of <paramref name="word"/>.</param>
    /// <param name="expectedLowByte">Bits 0 to 7 of <paramref name="word"/>.</param>
    [Theory]
    [MemberData(nameof(ByteExtractionRows))]
    public void ByteExtractionSplitsSixteenBitsIntoTwoBytes(
        ushort word,
        ushort expectedHighByte,
        ushort expectedLowByte)
    {
        // The same compile-time width pin as Region 2, and it carries a second meaning here: it
        // states that the declared result is 16-bit and not `byte`. A port that narrowed the
        // return type to `byte` would still compile against these locals, because byte converts
        // implicitly to ushort, so Region 0 is what catches that particular narrowing. The two
        // mechanisms cover different mistakes, which is why both are present.
        ushort actualHighByte = Bits.HiByte(word);
        ushort actualLowByte = Bits.LoByte(word);

        Assert.Equal(expectedHighByte, actualHighByte);
        Assert.Equal(expectedLowByte, actualLowByte);

        // Neither byte may exceed 255. This is an explicit statement that the extracted byte is
        // widened rather than the operand being passed through, and it is the property that makes
        // the composition round trip in Region 5 well defined.
        Assert.InRange(actualHighByte, (ushort)0, (ushort)0xFF);
        Assert.InRange(actualLowByte, (ushort)0, (ushort)0xFF);
    }

    // ==========================================================================================
    // REGION 4 - COMPOSITION. BOTH COMPOSERS TAKE LOW FIRST.
    //
    // Oracle: ws_objects/pfw.common.pbl.src/makeword.srf:L7
    //             global function  uint makeword(readonly uint low, readonly uint high)
    //         ws_objects/pfw.common.pbl.src/makelong.srf:L7
    //             global function  ulong makelong(readonly uint low, readonly uint high)
    //
    // C-K - THE ARGUMENT ORDER IS A DOCUMENTED DECISION, AND IT IS THE OTHER CLASSIC ERROR.
    // Both prototypes name `low` before `high`, matching the Win32 macros they are named after.
    // That order is the INVERSE of how the composed result reads left to right, which is exactly
    // what makes transposing it such an easy mistake: MakeLong(0x5678, 0x1234) produces
    // 0x12345678, so the argument that appears first in the call is the one that appears LAST in
    // the answer.
    //
    // No diagnostic can catch a transposition, because both parameters have the SAME TYPE, and the
    // wrong answer is entirely plausible. The guard has to be a row whose two arguments DIFFER.
    // That distinction governs the tables below and is called out per row:
    //
    //     ASYMMETRIC rows are the actual transposition guards. MakeWord(0x34, 0x12) == 0x1234
    //     fails immediately if the order is flipped, because a transposed port returns 0x3412.
    //
    //     SYMMETRIC rows such as MakeWord(0xFF, 0xFF) == 0xFFFF would pass EITHER WAY and prove
    //     nothing about ordering. They are retained deliberately, but only as width and boundary
    //     coverage, and they are labelled so nobody mistakes them for ordering guards.
    //
    // THE ORDERING CONVENTION IS INDEPENDENTLY CORROBORATED BY THE LEGACY CORPUS, which matters
    // because neither composer has a call site of its own to learn it from - MakeWord has none at
    // all. The corroboration comes from the INVERSE pair instead, where a packed value is unpacked
    // low word first and the two halves are then consumed in that order:
    //
    //     ws_objects/pfw.tests.pbl.src/w_test_camera_capture.srw:252
    //         uo_capture.Open(nDevIndex, LoWord(nDims[nResIndex]), HiWord(nDims[nResIndex]))
    //     ws_objects/pfw.tests.pbl.src/w_test_camera_capture.srw:341
    //         ddlb_resolutions.AddItem(Sprintf("{1} x {2}", LoWord(nDims[...]), HiWord(nDims[...])))
    //
    // A packed screen resolution, whose LOW word is the width and whose HIGH word is the height,
    // taken apart low-then-high and passed on in that same order. That is the ordering the
    // extractors and the composers must agree on, and Region 5 asserts precisely that agreement by
    // feeding the extractors' output straight into the composer's declared argument order.
    //
    // THE FAMILY SPLIT IS ALSO PINNED HERE. MakeWord is byte level despite its 16-bit parameter
    // spellings: it mirrors the Win32 macro and combines two BYTES, so the shift distance is 8 and
    // only the low 8 bits of each argument participate. MakeLong is word level and shifts by 16.
    // The final MakeWord row passes values above 255 to pin the resulting truncation, which is
    // legacy behaviour being reproduced and NOT validation being added - nothing rejects an
    // oversized argument, it is simply truncated, per C-B.
    // ==========================================================================================

    /// <summary>
    /// Byte-composition rows for <see cref="Bits.MakeWord(ushort, ushort)"/>, as
    /// (low byte, high byte, expected word), in the prototype's own argument order.
    /// </summary>
    /// <returns>Rows covering the ordering guards, the boundaries, and the truncation contract.</returns>
    public static TheoryData<ushort, ushort, ushort> WordCompositionRows()
    {
        TheoryData<ushort, ushort, ushort> rows = [];

        // ASYMMETRIC - THE PRIMARY TRANSPOSITION GUARD. A transposed port returns 0x3412, and a
        // port using the word-level shift distance of 16 returns 0x0034 once truncated to 16 bits.
        rows.Add((ushort)0x34, (ushort)0x12, (ushort)0x1234);

        // SYMMETRIC - boundary coverage only. Passes under a transposition and is not an ordering
        // guard. It pins that both bytes reach the result and that nothing overflows past 16 bits.
        rows.Add((ushort)0xFF, (ushort)0xFF, (ushort)0xFFFF);

        // SYMMETRIC - the zero identity, again not an ordering guard.
        rows.Add((ushort)0x00, (ushort)0x00, (ushort)0x0000);

        // ASYMMETRIC - low byte only, so the result must occupy the low half. A transposed port
        // returns 0xFF00.
        rows.Add((ushort)0xFF, (ushort)0x00, (ushort)0x00FF);

        // ASYMMETRIC - high byte only, so the result must occupy the high half. A transposed port
        // returns 0x00FF.
        rows.Add((ushort)0x00, (ushort)0xFF, (ushort)0xFF00);

        // ASYMMETRIC, AND THE TRUNCATION ROW. Both arguments exceed a byte, which a 16-bit slot
        // permits, so only their low bytes participate: 0x1234 contributes 0x34 and 0x5678
        // contributes 0x78, giving 0x7834. This is the row that proves MakeWord is byte level
        // rather than word level, and it reproduces the legacy discard rather than rejecting the
        // oversized input.
        rows.Add((ushort)0x1234, (ushort)0x5678, (ushort)0x7834);

        return rows;
    }

    /// <summary>
    /// Long-composition rows for <see cref="Bits.MakeLong(ushort, ushort)"/>, as
    /// (low word, high word, expected 32-bit value), in the prototype's own argument order.
    /// </summary>
    /// <returns>Rows covering the ordering guards and the width boundaries.</returns>
    public static TheoryData<ushort, ushort, uint> LongCompositionRows()
    {
        TheoryData<ushort, ushort, uint> rows = [];

        // ASYMMETRIC - THE PRIMARY TRANSPOSITION GUARD, and the clearest illustration that the
        // first argument lands LAST in the answer. A transposed port returns 0x56781234.
        rows.Add((ushort)0x5678, (ushort)0x1234, 0x12345678u);

        // SYMMETRIC - boundary coverage only. It pins that the result spans the full 32 bits and
        // is not itself truncated to 16, which a port that forgot the widening cast would do.
        rows.Add((ushort)0xFFFF, (ushort)0xFFFF, 0xFFFFFFFFu);

        // SYMMETRIC - the zero identity.
        rows.Add((ushort)0x0000, (ushort)0x0000, 0x00000000u);

        // ASYMMETRIC - low word only. A transposed port returns 0xFFFF0000.
        rows.Add((ushort)0xFFFF, (ushort)0x0000, 0x0000FFFFu);

        // ASYMMETRIC - high word only, which pins the shift distance at 16. A transposed port
        // returns 0x0000FFFF, and a port shifting by 8 returns 0x00FFFF00.
        rows.Add((ushort)0x0000, (ushort)0xFFFF, 0xFFFF0000u);

        // ASYMMETRIC - a single bit in each half, so the two halves cannot be confused and the
        // shift distance is pinned exactly rather than merely bounded.
        rows.Add((ushort)0x0001, (ushort)0x8000, 0x80000001u);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.MakeWord(ushort, ushort)"/> composes a 16-bit word from a low
    /// and a high BYTE, in that argument order, truncating each argument to eight bits.
    /// </summary>
    /// <param name="low">The byte destined for bits 0 to 7.</param>
    /// <param name="high">The byte destined for bits 8 to 15.</param>
    /// <param name="expected">The composed 16-bit word.</param>
    [Theory]
    [MemberData(nameof(WordCompositionRows))]
    public void WordCompositionTakesTheLowByteFirst(ushort low, ushort high, ushort expected)
    {
        // The arguments are passed in the prototype's order and nothing is reordered at the call
        // site. Writing them here in the same order the legacy declares them is the point: a
        // helper that "corrected" the order would hide the very thing being tested.
        ushort actual = Bits.MakeWord(low, high);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Proves that <see cref="Bits.MakeLong(ushort, ushort)"/> composes a 32-bit value from a low
    /// and a high 16-bit WORD, in that argument order.
    /// </summary>
    /// <param name="low">The word destined for bits 0 to 15.</param>
    /// <param name="high">The word destined for bits 16 to 31.</param>
    /// <param name="expected">The composed 32-bit value.</param>
    [Theory]
    [MemberData(nameof(LongCompositionRows))]
    public void LongCompositionTakesTheLowWordFirst(ushort low, ushort high, uint expected)
    {
        // Declared `uint` rather than `var` for the same compile-time reason as Regions 2 and 3,
        // in the opposite direction: this states that the result is WIDER than its arguments. A
        // port that returned ushort here would fail Region 0 on its declared type, and a port that
        // returned ulong would still assign, so Region 0 remains the authority on this one.
        uint actual = Bits.MakeLong(low, high);

        Assert.Equal(expected, actual);
    }

    // ==========================================================================================
    // REGION 5 - THE ROUND TRIP. EXTRACTION AND COMPOSITION MUST AGREE.
    //
    // Oracle: the four extraction and two composition prototypes cited in Regions 2, 3 and 4. The
    //         identities themselves follow from the Win32 macro family the names denote, in which
    //         the extractors partition a value exactly and the composers reassemble it.
    //
    // WHY THIS REGION EARNS ITS PLACE RATHER THAN RESTATING THE ONES ABOVE. It is the cleanest
    // single statement that the two halves of the family agree with each other, and it does two
    // things no individual row does.
    //
    //     It re-proves the (low, high) ordering INDEPENDENTLY. The extractors supply the arguments
    //     in the composer's declared order, so if the composer were transposed the value would
    //     come back with its halves swapped. That makes the ordering guard a consequence of the
    //     family's own definition rather than of a hand-written expected value, so it would still
    //     hold if someone edited an expected column in Region 4.
    //
    //     It re-proves the WIDTH PAIRING. MakeLong consumes exactly what HiWord and LoWord
    //     produce, and MakeWord exactly what HiByte and LoByte produce, so a member whose width
    //     drifted would break the composition at the call site rather than merely return a
    //     different number.
    //
    // NOTE ON ROW SELECTION, because it is the whole reason this region can catch a transposition:
    // a value whose two halves are EQUAL round trips correctly even under a transposed composer,
    // so 0 and the all-ones maximum prove nothing about ordering here. Both tables below are
    // therefore weighted towards values whose halves DIFFER, and the symmetric values are kept
    // only as boundary coverage. Each table says which of its rows is which.
    // ==========================================================================================

    /// <summary>
    /// Values used to round trip a 32-bit quantity through <see cref="Bits.LoWord(uint)"/>,
    /// <see cref="Bits.HiWord(uint)"/> and <see cref="Bits.MakeLong(ushort, ushort)"/>.
    /// </summary>
    /// <returns>Rows weighted towards values whose two halves differ.</returns>
    public static TheoryData<uint> LongRoundTripValues()
    {
        // ASYMMETRIC halves - these are the rows that would catch a transposed MakeLong.
        TheoryData<uint> rows =
        [
            0x12345678u,
            0x0000FFFFu,
            0xFFFF0000u,
            0x80000001u,
            0x00000001u,
            0x80000000u,
            0xDEADBEEFu,
            0xCAFE0000u,
        ];

        // SYMMETRIC halves - boundary coverage only, since each round trips under a transposition
        // as well. They are retained because the two width extremes still need exercising.
        rows.Add(0x00000000u);
        rows.Add(0xFFFFFFFFu);

        return rows;
    }

    /// <summary>
    /// Values used to round trip a 16-bit quantity through <see cref="Bits.LoByte(ushort)"/>,
    /// <see cref="Bits.HiByte(ushort)"/> and <see cref="Bits.MakeWord(ushort, ushort)"/>.
    /// </summary>
    /// <returns>Rows weighted towards words whose two bytes differ.</returns>
    public static TheoryData<ushort> WordRoundTripValues()
    {
        // ASYMMETRIC bytes - the rows that would catch a transposed MakeWord.
        TheoryData<ushort> rows =
        [
            (ushort)0x1234,
            (ushort)0x00FF,
            (ushort)0xFF00,
            (ushort)0x8001,
            (ushort)0x0001,
            (ushort)0x8000,
            (ushort)0xABCD,
            (ushort)0xBE00,
        ];

        // SYMMETRIC bytes - boundary coverage only.
        rows.Add((ushort)0x0000);
        rows.Add((ushort)0xFFFF);

        return rows;
    }

    /// <summary>
    /// Proves that splitting a 32-bit value into words and recomposing it in the prototype's
    /// (low, high) order returns the original value exactly.
    /// </summary>
    /// <param name="value">The 32-bit value to round trip.</param>
    [Theory]
    [MemberData(nameof(LongRoundTripValues))]
    public void LongRoundTripsThroughItsTwoWords(uint value)
    {
        // The extractors feed the composer in its DECLARED argument order, low first. This call is
        // the ordering guard: swapping the two arguments here would break every asymmetric row,
        // and so would transposing the composer's own parameters.
        Assert.Equal(value, Bits.MakeLong(Bits.LoWord(value), Bits.HiWord(value)));
    }

    /// <summary>
    /// Proves that splitting a 16-bit word into bytes and recomposing it in the prototype's
    /// (low, high) order returns the original word exactly.
    /// </summary>
    /// <param name="word">The 16-bit word to round trip.</param>
    [Theory]
    [MemberData(nameof(WordRoundTripValues))]
    public void WordRoundTripsThroughItsTwoBytes(ushort word)
    {
        Assert.Equal(word, Bits.MakeWord(Bits.LoByte(word), Bits.HiByte(word)));
    }

    /// <summary>
    /// Proves that the two composition families are NOT interchangeable, by showing that the
    /// byte-level composer and the word-level composer disagree on the same arguments.
    /// </summary>
    [Fact]
    public void TheTwoCompositionFamiliesOperateAtDifferentWidths()
    {
        // C-K - THE FAMILY SPLIT, STATED AS AN EXECUTABLE ASSERTION RATHER THAN ONLY A COMMENT.
        // Given the same two arguments, the byte-level composer shifts by 8 and truncates each
        // argument to a byte, while the word-level composer shifts by 16 and keeps both in full.
        // Spelling that out here is what stops a future edit from "unifying" the two, which is the
        // specific mistake the naming invites, since MakeWord's 16-bit parameter spellings suggest
        // it composes words when it composes bytes.
        const ushort Low = 0x5678;
        const ushort High = 0x1234;

        // Byte level: only 0x78 and 0x34 participate, so the answer is 0x3478.
        Assert.Equal((ushort)0x3478, Bits.MakeWord(Low, High));

        // Word level: both arguments participate in full, so the answer is 0x12345678.
        Assert.Equal(0x12345678u, Bits.MakeLong(Low, High));
    }

    // ==========================================================================================
    // REGION 6 - THE DYADIC MASK OPERATIONS AND BitClear
    //
    // Oracle: ws_objects/pfw.common.pbl.src/bitand.srf:L7
    //             global function  ulong bitand(readonly ulong a, readonly ulong b)
    //         ws_objects/pfw.common.pbl.src/bitor.srf:L7
    //             global function  ulong bitor(readonly ulong a, readonly ulong b)
    //         ws_objects/pfw.common.pbl.src/bitxor.srf:L7
    //             global function  ulong bitxor(readonly ulong a, readonly ulong b)
    //         ws_objects/pfw.common.pbl.src/bitclear.srf:L7
    //             global function ulong bitclear (readonly unsignedlong num,
    //                                             readonly unsignedlong bits)
    //
    // All four take two 32-bit unsigned operands and return one. The plan's native-binding matrix
    // classifies them as a straight substitution to the corresponding C# operators, so what is
    // asserted here is that substitution at the correct width, plus BitClear's composite meaning.
    //
    // WHY THE THREE DYADIC OPERATIONS SHARE ONE TABLE. Each row gives one operand pair and all
    // three results, which makes a swapped implementation impossible to miss: a port that wired
    // BitOr to the AND operator would fail on the same row that proves BitAnd correct, rather than
    // on a separate row somebody could disable in isolation. The rows are chosen so that no two of
    // the three answers coincide wherever that is achievable.
    //
    // BitClear IS A COMPOSITE, NOT A FOURTH PRIMITIVE. It removes the masked bits, which is the
    // AND of the operand with the complement of the mask, so it is pinned both by its own table
    // and by an asserted relation to BitAnd and BitNot. Its single legacy call site removes one
    // event's bit from a disabled-event mask, and the guard against a zero argument sits in the
    // CALLER one line earlier - so rejecting an empty mask is demonstrably not this member's job,
    // and an empty mask is a well-defined no-op here rather than an error. That is C-B in action:
    // the table asserts the no-op and asserts no validation.
    // ==========================================================================================

    /// <summary>
    /// Operand pairs with all three dyadic results, as
    /// (a, b, expected AND, expected OR, expected XOR).
    /// </summary>
    /// <returns>Rows covering the identities, the width extremes and fully interleaved masks.</returns>
    public static TheoryData<uint, uint, uint, uint, uint> DyadicMaskRows()
    {
        TheoryData<uint, uint, uint, uint, uint> rows = [];

        // Both operands empty.
        rows.Add(0u, 0u, 0u, 0u, 0u);

        // Both operands saturated across the full 32 bits. XOR is the discriminator here: it must
        // be zero while the other two are saturated.
        rows.Add(0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0u);

        // Disjoint extremes: AND empties, while OR and XOR both saturate.
        rows.Add(0xFFFFFFFFu, 0u, 0u, 0xFFFFFFFFu, 0xFFFFFFFFu);

        // Fully interleaved nibbles with no overlap. Exercises every bit of the width in one row.
        rows.Add(0xF0F0F0F0u, 0x0F0F0F0Fu, 0u, 0xFFFFFFFFu, 0xFFFFFFFFu);

        // PARTIAL OVERLAP, so all three answers differ from one another and from both operands.
        // This is the row that catches a port wiring one operator to another.
        rows.Add(0xFF00FF00u, 0xFFFF0000u, 0xFF000000u, 0xFFFFFF00u, 0x00FFFF00u);

        // The most significant bit only, which pins that bit 31 is reachable at this width.
        rows.Add(0x80000000u, 0x80000000u, 0x80000000u, 0x80000000u, 0u);

        // The two extreme single bits, disjoint.
        rows.Add(0x00000001u, 0x80000000u, 0u, 0x80000001u, 0x80000001u);

        // A masking case of the shape the legacy actually uses: extracting the low half.
        rows.Add(0x12345678u, 0x0000FFFFu, 0x00005678u, 0x1234FFFFu, 0x1234A987u);

        return rows;
    }

    /// <summary>
    /// Bit-clearing rows for <see cref="Bits.BitClear(uint, uint)"/>, as
    /// (value, mask to clear, expected result).
    /// </summary>
    /// <returns>Rows covering the no-op cases, the extremes and the legacy call-site shape.</returns>
    public static TheoryData<uint, uint, uint> BitClearRows()
    {
        TheoryData<uint, uint, uint> rows = [];

        // Clearing the low half of a saturated value leaves the high half.
        rows.Add(0xFFFFFFFFu, 0x0000FFFFu, 0xFFFF0000u);

        // Clearing everything empties the value.
        rows.Add(0xFFFFFFFFu, 0xFFFFFFFFu, 0u);

        // AN EMPTY MASK IS A DEFINED NO-OP, NOT AN ERROR. The legacy call site performs its own
        // zero check one line before calling, so validation is the caller's responsibility and
        // this member is total. Asserting the no-op is how C-B is honoured: nothing here expects a
        // throw, a clamp or an error code.
        rows.Add(0xFFFFFFFFu, 0u, 0xFFFFFFFFu);

        // Clearing an already-empty value is also a no-op.
        rows.Add(0u, 0xFFFFFFFFu, 0u);

        // Clearing a masked span out of the middle of a value.
        rows.Add(0x12345678u, 0x00005678u, 0x12340000u);

        // THE LEGACY CALL-SITE SHAPE, at the same scale it occurs at: a three-bit event mask with
        // one event's bit removed. The legacy composes such masks from a contiguous single-bit
        // family, so 1, 2 and 4 are the real magnitudes involved.
        rows.Add(0x00000007u, 0x00000002u, 0x00000005u);

        // Clearing a bit that is NOT set changes nothing, which is the property that makes the
        // operation idempotent and safe to apply twice.
        rows.Add(0x00000005u, 0x00000008u, 0x00000005u);

        // The most significant bit, cleared, to pin the operation at the top of the width.
        rows.Add(0x80000000u, 0x80000000u, 0u);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitAnd(uint, uint)"/>, <see cref="Bits.BitOr(uint, uint)"/> and
    /// <see cref="Bits.BitXor(uint, uint)"/> are the three bitwise operators at 32-bit width.
    /// </summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <param name="expectedAnd">The bits set in both operands.</param>
    /// <param name="expectedOr">The bits set in either operand.</param>
    /// <param name="expectedXor">The bits set in exactly one operand.</param>
    [Theory]
    [MemberData(nameof(DyadicMaskRows))]
    public void DyadicMaskOperationsMatchTheirOperatorsAtThirtyTwoBits(
        uint a,
        uint b,
        uint expectedAnd,
        uint expectedOr,
        uint expectedXor)
    {
        Assert.Equal(expectedAnd, Bits.BitAnd(a, b));
        Assert.Equal(expectedOr, Bits.BitOr(a, b));
        Assert.Equal(expectedXor, Bits.BitXor(a, b));

        // All three are commutative, which is asserted rather than assumed because a port that
        // consumed its operands in the wrong order would otherwise pass every row above: the three
        // operators are symmetric, so the ordering error is invisible in the forward direction.
        // Stating it here means the composition members remain the only order-sensitive ones, and
        // Region 4 is where order is genuinely at stake.
        Assert.Equal(expectedAnd, Bits.BitAnd(b, a));
        Assert.Equal(expectedOr, Bits.BitOr(b, a));
        Assert.Equal(expectedXor, Bits.BitXor(b, a));
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitClear(uint, uint)"/> removes exactly the masked bits, and
    /// that an empty mask is a no-op rather than an error.
    /// </summary>
    /// <param name="value">The value to clear bits in.</param>
    /// <param name="mask">The bits to remove.</param>
    /// <param name="expected">The value with those bits removed.</param>
    [Theory]
    [MemberData(nameof(BitClearRows))]
    public void BitClearRemovesExactlyTheMaskedBits(uint value, uint mask, uint expected)
    {
        Assert.Equal(expected, Bits.BitClear(value, mask));

        // BitClear IS the AND of the value with the complement of the mask, expressed through the
        // two members that are pinned independently in Regions 1 and 6. Asserting the relation
        // rather than only the table means a drift in any one of the three members breaks this,
        // and it re-uses BitNot's already-proven 32-bit complement to state that BitClear clears
        // across the same width rather than a wider one.
        Assert.Equal(expected, Bits.BitAnd(value, Bits.BitNot(mask)));

        // Idempotence. Removing the same bits twice cannot remove more of them.
        Assert.Equal(expected, Bits.BitClear(Bits.BitClear(value, mask), mask));
    }

    /// <summary>
    /// Proves the algebraic identities of the four mask operations against the width extremes,
    /// which pins their behaviour at the boundaries the tables above sample rather than span.
    /// </summary>
    /// <param name="value">The operand to apply each identity to.</param>
    [Theory]
    [MemberData(nameof(LongRoundTripValues))]
    public void MaskOperationIdentitiesHoldAcrossTheWidth(uint value)
    {
        const uint Empty = 0x00000000u;
        const uint Saturated = 0xFFFFFFFFu;

        // AND: idempotent, annihilated by an empty mask, and neutral under a saturated one. The
        // third of these is a width statement in disguise - it only holds if the saturated mask
        // really does cover every bit of the operand, which it cannot if the operand is wider.
        Assert.Equal(value, Bits.BitAnd(value, value));
        Assert.Equal(Empty, Bits.BitAnd(value, Empty));
        Assert.Equal(value, Bits.BitAnd(value, Saturated));

        // OR: idempotent, neutral under an empty mask, saturating under a saturated one.
        Assert.Equal(value, Bits.BitOr(value, value));
        Assert.Equal(value, Bits.BitOr(value, Empty));
        Assert.Equal(Saturated, Bits.BitOr(value, Saturated));

        // XOR: self-annihilating, neutral under an empty mask, and equal to the complement under a
        // saturated one. That last identity ties XOR and BitNot together at the same width.
        Assert.Equal(Empty, Bits.BitXor(value, value));
        Assert.Equal(value, Bits.BitXor(value, Empty));
        Assert.Equal(Bits.BitNot(value), Bits.BitXor(value, Saturated));

        // CLEAR: neutral under an empty mask, and emptying under a saturated one or under itself.
        Assert.Equal(value, Bits.BitClear(value, Empty));
        Assert.Equal(Empty, Bits.BitClear(value, Saturated));
        Assert.Equal(Empty, Bits.BitClear(value, value));
    }

    // ==========================================================================================
    // REGION 7 - THE SHIFTS, AND THE TWO PROPERTIES THAT DEPEND ON THE OPERAND BEING UNSIGNED
    //
    // Oracle: ws_objects/pfw.common.pbl.src/bitlsh.srf:L7
    //             global function  ulong bitlsh(readonly ulong num, readonly uint shift)
    //         ws_objects/pfw.common.pbl.src/bitrsh.srf:L7
    //             global function  ulong bitrsh(readonly ulong num, readonly uint shift)
    //
    // MIXED WIDTHS IN ONE PROTOTYPE: a 32-bit value shifted by a 16-bit count. Region 0 pins that
    // pairing; the rows here pin what the shift does.
    //
    // C-K - TWO PROPERTIES HERE FOLLOW FROM THE OPERAND'S TYPE RATHER THAN FROM ANY CODE, WHICH IS
    // EXACTLY WHY THEY NEED TESTS.
    //
    //     THE RIGHT SHIFT IS LOGICAL, NOT ARITHMETIC. C# selects the sign-propagating shift only
    //     for a SIGNED operand, so the zero fill is guaranteed by the operand being unsigned and
    //     by nothing else. A port that used a signed 32-bit type would sign-extend on every value
    //     with the top bit set, turning 0x80000000 shifted right by 31 into 0xFFFFFFFF instead of
    //     1, and 0xFFFFFFFF shifted right by 1 into 0xFFFFFFFF instead of 0x7FFFFFFF. Rows for
    //     both are present, and a dedicated test states the property.
    //
    //     THE COUNT WRAPS MODULO THE OPERAND WIDTH. C# masks the count to the low FIVE bits for a
    //     32-bit operand, so 32 shifts by 0 and 33 shifts by 1. For a 64-bit operand it would mask
    //     to SIX bits instead, so 32 would shift by 32 - which means getting the operand width
    //     right gets the wrap right for free, and getting it wrong changes the answer for every
    //     over-wide count rather than only the extreme one.
    //
    // I-3 - THE WRAP IS A PORT-SIDE INFERENCE AND IS LABELLED AS ONE. Nothing in the repository
    // states what the native body does with an over-wide count; the body is unreadable and no call
    // site passes one. The port adopts C#'s own defined behaviour and records the choice, and the
    // test that pins it is named exactly as Bits.cs names it, immediately below the tables. It is
    // NOT presented as verified legacy behaviour. Note also what is deliberately NOT asserted:
    // nothing here expects an over-wide count to be rejected, clamped or saturated to zero, per
    // C-B, because the legacy prototype promises no validation whatsoever.
    // ==========================================================================================

    /// <summary>
    /// Left-shift rows, as (value, shift count, expected result).
    /// </summary>
    /// <returns>Rows covering counts 0, 1, 31, 32 and 33 plus discard at the top of the width.</returns>
    public static TheoryData<uint, ushort, uint> LeftShiftRows()
    {
        TheoryData<uint, ushort, uint> rows = [];

        // A count of zero is the identity.
        rows.Add(1u, (ushort)0, 1u);
        rows.Add(0xFFFFFFFFu, (ushort)0, 0xFFFFFFFFu);
        rows.Add(0x12345678u, (ushort)0, 0x12345678u);

        // A count of one doubles.
        rows.Add(1u, (ushort)1, 2u);

        // A count of 31 moves the least significant bit to the most significant position, which
        // pins where the top of the 32-bit operand is.
        rows.Add(1u, (ushort)31, 0x80000000u);
        rows.Add(0xFFFFFFFFu, (ushort)31, 0x80000000u);

        // BITS SHIFTED PAST BIT 31 ARE DISCARDED rather than retained in a wider result. These two
        // rows are a width assertion: a 64-bit port would keep the bits that must vanish here.
        rows.Add(0xFFFFFFFFu, (ushort)1, 0xFFFFFFFEu);
        rows.Add(0x80000000u, (ushort)1, 0u);

        // A mid-range count on an asymmetric operand, so the distance is pinned exactly.
        rows.Add(0x12345678u, (ushort)4, 0x23456780u);

        // I-3, THE WRAP. A count of 32 masks to 0 and is therefore the identity, and a count of 33
        // masks to 1. The pair proves the behaviour is MODULO and not saturation: saturation would
        // give 0 for both.
        rows.Add(1u, (ushort)32, 1u);
        rows.Add(1u, (ushort)33, 2u);

        return rows;
    }

    /// <summary>
    /// Right-shift rows, as (value, shift count, expected result).
    /// </summary>
    /// <returns>
    /// Rows covering counts 0, 1, 31, 32 and 33, the zero-fill proof, and discard at bit 0.
    /// </returns>
    public static TheoryData<uint, ushort, uint> RightShiftRows()
    {
        TheoryData<uint, ushort, uint> rows = [];

        // A count of zero is the identity.
        rows.Add(1u, (ushort)0, 1u);
        rows.Add(0xFFFFFFFFu, (ushort)0, 0xFFFFFFFFu);
        rows.Add(0x12345678u, (ushort)0, 0x12345678u);

        // A count of one halves.
        rows.Add(2u, (ushort)1, 1u);

        // THE ZERO-FILL PROOF. A signed, arithmetic shift would return 0xFFFFFFFF for the first of
        // these and 0xFFFFFFFF for the second; the logical shift returns 1 and 0x7FFFFFFF. These
        // are the two rows that fail if the operand type is ever made signed.
        rows.Add(0x80000000u, (ushort)31, 1u);
        rows.Add(0xFFFFFFFFu, (ushort)1, 0x7FFFFFFFu);
        rows.Add(0xFFFFFFFFu, (ushort)31, 1u);

        // Bits shifted past bit 0 are discarded.
        rows.Add(1u, (ushort)1, 0u);

        // A mid-range count on an asymmetric operand.
        rows.Add(0x12345678u, (ushort)4, 0x01234567u);

        // I-3, THE WRAP, in the other direction. 32 masks to 0 and 33 masks to 1.
        rows.Add(0x80000000u, (ushort)32, 0x80000000u);
        rows.Add(0x80000000u, (ushort)33, 0x40000000u);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitLsh(uint, ushort)"/> shifts left at 32-bit width, discarding
    /// bits shifted past bit 31.
    /// </summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="shift">The 16-bit shift count.</param>
    /// <param name="expected">The shifted result.</param>
    [Theory]
    [MemberData(nameof(LeftShiftRows))]
    public void LeftShiftMovesBitsUpAndDiscardsAtTheTopOfTheWidth(
        uint value,
        ushort shift,
        uint expected)
    {
        Assert.Equal(expected, Bits.BitLsh(value, shift));
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitRsh(uint, ushort)"/> shifts right at 32-bit width, filling
    /// the vacated high bits with zeros.
    /// </summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="shift">The 16-bit shift count.</param>
    /// <param name="expected">The shifted result.</param>
    [Theory]
    [MemberData(nameof(RightShiftRows))]
    public void RightShiftMovesBitsDownAndFillsWithZeros(uint value, ushort shift, uint expected)
    {
        Assert.Equal(expected, Bits.BitRsh(value, shift));
    }

    /// <summary>
    /// Proves that the right shift is LOGICAL rather than arithmetic, so it never propagates the
    /// most significant bit.
    /// </summary>
    [Fact]
    public void RightShiftIsLogicalAndNeverPropagatesTheSignBit()
    {
        // C-K - THIS PROPERTY IS GUARANTEED BY THE OPERAND'S TYPE, NOT BY THE MEMBER'S BODY, which
        // is precisely why it is asserted separately. Every value below has its most significant
        // bit set, which is the only case in which a signed operand would behave differently, so
        // this test is the one that fails if the port is ever re-typed to a signed 32-bit integer.
        Assert.Equal(0x40000000u, Bits.BitRsh(0x80000000u, (ushort)1));
        Assert.Equal(0x00000001u, Bits.BitRsh(0x80000000u, (ushort)31));
        Assert.Equal(0x7FFFFFFFu, Bits.BitRsh(0xFFFFFFFFu, (ushort)1));
        Assert.Equal(0x00000001u, Bits.BitRsh(0xFFFFFFFFu, (ushort)31));
        Assert.Equal(0x0000FFFFu, Bits.BitRsh(0xFFFF0000u, (ushort)16));

        // Shifting any value all the way down by 31 can leave at most the single bit that was at
        // the top, so the result is 0 or 1 and never a sign-extended run of ones.
        Assert.InRange(Bits.BitRsh(0x80000000u, (ushort)31), 0u, 1u);
        Assert.InRange(Bits.BitRsh(0x7FFFFFFFu, (ushort)31), 0u, 1u);
    }

    /// <summary>
    /// Pins the INFERRED behaviour of a shift count at or beyond the 32-bit operand width: the
    /// count wraps modulo 32 rather than saturating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZED FROM THE PORT, NOT FROM THE LEGACY. This test is named exactly as
    /// <c>Bits.cs</c> names it against its inference I-3, so that cross-reference resolves. The
    /// repository does not settle the question: <c>bitlsh.srf</c> and <c>bitrsh.srf</c> are
    /// declaration-only over the closed <c>pfw.dll</c>, and the one legacy call site passes a count
    /// derived from a processor index, which never reaches 32. The port adopts C#'s own defined
    /// behaviour for a 32-bit operand, and the inference is a strong one because the x86 shift
    /// instructions mask the count to five bits in exactly the same way, so a thin native shim over
    /// them would agree. It is nonetheless an inference, and this test is the single place to
    /// revise if the behavioural oracle ever contradicts it.
    /// </para>
    /// <para>
    /// This is also the reason the wrap is worth a test of its own rather than a table row: it is
    /// the one behaviour in the file that a WIDER operand would change silently, because a 64-bit
    /// operand masks the count to six bits instead of five.
    /// </para>
    /// </remarks>
    [Fact]
    public void BitLshShiftCountWrapsModuloThirtyTwoInferred()
    {
        // A count of exactly the operand width masks to zero, so both shifts are the identity.
        Assert.Equal(1u, Bits.BitLsh(1u, (ushort)32));
        Assert.Equal(0x80000000u, Bits.BitRsh(0x80000000u, (ushort)32));
        Assert.Equal(0x12345678u, Bits.BitLsh(0x12345678u, (ushort)32));

        // MODULO, NOT SATURATION. Saturation would give zero for every count at or above 32; these
        // assertions require the count to continue wrapping instead.
        Assert.Equal(2u, Bits.BitLsh(1u, (ushort)33));
        Assert.Equal(0x40000000u, Bits.BitRsh(0x80000000u, (ushort)33));
        Assert.Equal(0x80000000u, Bits.BitLsh(1u, (ushort)63));

        // A count two widths out wraps twice and is the identity again, which pins the period at
        // exactly 32 rather than merely showing that some wrap occurs. A 64-bit operand would give
        // zero here, so this assertion is simultaneously a width assertion.
        Assert.Equal(1u, Bits.BitLsh(1u, (ushort)64));
        Assert.Equal(0x80000000u, Bits.BitRsh(0x80000000u, (ushort)64));

        // The 16-bit count parameter permits values far beyond the width, and they wrap too. This
        // asserts no rejection and no clamping, per C-B, because the prototype promises neither.
        Assert.Equal(1u, Bits.BitLsh(1u, ushort.MaxValue - (ushort)31));
        Assert.Equal(0x80000000u, Bits.BitLsh(1u, ushort.MaxValue));
    }

    // ==========================================================================================
    // REGION 8 - BitTest. A MASK TEST WITH ANY-BIT SEMANTICS.
    //
    // Oracle: ws_objects/pfw.common.pbl.src/bittest.srf:L7
    //             global function  boolean bittest(readonly ulong num, readonly ulong bits)
    //         plus the shape of its call sites across ws_objects, which is what settles the
    //         convention. This is the highest-traffic member in the file by a wide margin.
    //
    // THIS CONVENTION IS DERIVED FROM THE CORPUS, NOT INFERRED, WHICH IS WHY IT IS NOT LABELLED AS
    // AN INFERENCE the way Region 9 is. Bits.cs recovers it from the calls, and the evidence
    // excludes both alternative readings:
    //
    //     NOT A SINGLE-BIT POSITION TEST. Callers pass multi-bit masks - the recurring idiom is a
    //     state word tested against the sum of two state constants - which a position-based reading
    //     could not accept at all. The second operand's declared width says the same thing
    //     independently: it is 32-bit, the full width of a mask, whereas GetBit's position operand
    //     is 16-bit. Region 0 pins that contrast.
    //
    //     NOT AN ALL-BITS TEST. The two states combined in that idiom are mutually exclusive in
    //     practice, so an all-bits reading would make every such test permanently false and the
    //     surrounding branches dead code.
    //
    // What survives is ANY-BIT: true when the operand shares at least one bit with the mask. The
    // rows below do not merely benefit from that discrimination, they STATE it - specific rows are
    // marked as the ones each rejected reading would fail, so the reasoning is executable rather
    // than only recorded in a comment.
    //
    // ADJACENT LEGACY IDIOM, RECORDED AND NOT CORRECTED: the legacy composes these masks with `+`
    // rather than `|`. The two agree only while the combined bits are disjoint, which holds for the
    // families involved because they are contiguous single-bit families with no duplicate, so no
    // sum can carry. The masks below are written as literals for the reason given in the file
    // header, and the magnitudes chosen mirror the real ones.
    // ==========================================================================================

    /// <summary>
    /// Mask-test rows, as (value, mask, expected result).
    /// </summary>
    /// <returns>
    /// Rows covering the any-bit discrimination, the empty mask, and both ends of the width.
    /// </returns>
    public static TheoryData<uint, uint, bool> MaskTestRows()
    {
        TheoryData<uint, uint, bool> rows = [];

        // ---- Single-bit masks: the baseline ----
        rows.Add(0x00000001u, 0x00000001u, true);
        rows.Add(0x00000001u, 0x00000002u, false);
        rows.Add(0x00000002u, 0x00000002u, true);

        // ---- The empty cases. An empty mask shares no bit with anything, so it is false even
        // against a saturated operand. Nothing here expects an empty mask to be rejected: the one
        // legacy caller that needed a zero check performs it itself, so this member is total.
        rows.Add(0xFFFFFFFFu, 0u, false);
        rows.Add(0u, 0xFFFFFFFFu, false);
        rows.Add(0u, 0u, false);

        // ---- THE ANY-BIT DISCRIMINATORS. The mask names two bits and the operand holds only one
        // of them. An ALL-BITS reading returns false for both of these rows, so they are what
        // excludes it. They are also the exact shape of the legacy idiom, at the real magnitudes.
        rows.Add(0x00000001u, 0x00000003u, true);
        rows.Add(0x00000002u, 0x00000003u, true);

        // Both mask bits present, which every candidate reading would accept. Kept as coverage.
        rows.Add(0x00000003u, 0x00000003u, true);

        // ---- THE POSITION-READING DISCRIMINATOR. Under a one-based POSITION reading, a second
        // operand of 3 would name bit 3, whose value is 4, and this operand holds exactly that bit
        // - so a position-based port returns TRUE here. The mask reading shares no bit with 3 and
        // must return FALSE. This single row excludes the position interpretation outright.
        rows.Add(0x00000004u, 0x00000003u, false);

        // ---- The top of the width, which pins that bit 31 participates in the test.
        rows.Add(0x80000000u, 0x80000000u, true);
        rows.Add(0x80000000u, 0x7FFFFFFFu, false);
        rows.Add(0xFFFFFFFFu, 0x80000000u, true);

        // ---- Half-word masks, the shape used when testing a masked span rather than a flag.
        rows.Add(0x00005678u, 0x0000FFFFu, true);
        rows.Add(0x12340000u, 0x0000FFFFu, false);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitTest(uint, uint)"/> reports whether the value shares at least
    /// one bit with the mask, across the full 32-bit width.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="mask">The mask to test against, which may name more than one bit.</param>
    /// <param name="expected">Whether at least one bit is shared.</param>
    [Theory]
    [MemberData(nameof(MaskTestRows))]
    public void MaskTestReportsWhetherAnyMaskedBitIsSet(uint value, uint mask, bool expected)
    {
        Assert.Equal(expected, Bits.BitTest(value, mask));

        // BitTest IS BitAnd COMPARED AGAINST ZERO, expressed through a member pinned independently
        // in Region 6. Asserting the relation ties the highest-traffic member in the file to the
        // second highest, so a drift in either breaks this rather than only one of them.
        Assert.Equal(expected, Bits.BitAnd(value, mask) != 0u);

        // The test is symmetric in its operands, because sharing a bit is a symmetric relation.
        // Worth stating because the two parameters are named asymmetrically - a value and a mask -
        // which could suggest an asymmetry in behaviour that does not exist.
        Assert.Equal(expected, Bits.BitTest(mask, value));
    }

    /// <summary>
    /// Proves that <see cref="Bits.BitTest(uint, uint)"/> uses ANY-bit semantics and is therefore
    /// neither an all-bits test nor a single-bit position test.
    /// </summary>
    /// <remarks>
    /// The two rejected readings are excluded here in one place, at the magnitudes the legacy
    /// actually uses, so the discrimination recorded in the region banner is executable. This is
    /// derived from the call-site corpus rather than inferred, so unlike the two tests in Region 9
    /// it is not labelled as a pending characterization.
    /// </remarks>
    [Fact]
    public void MaskTestUsesAnyBitSemanticsRatherThanAllBitsOrASinglePosition()
    {
        // The legacy idiom, reproduced at its real scale: a state word tested against a mask
        // naming TWO mutually exclusive states. Both single-state cases must be true.
        const uint FirstState = 0x00000001u;
        const uint SecondState = 0x00000002u;
        const uint EitherState = FirstState + SecondState;

        Assert.True(Bits.BitTest(FirstState, EitherState));
        Assert.True(Bits.BitTest(SecondState, EitherState));

        // AN ALL-BITS PORT WOULD RETURN FALSE FOR BOTH OF THE ABOVE, which is what makes the two
        // assertions the exclusion rather than mere coverage. The two states are mutually exclusive
        // in practice, so an all-bits reading would leave both branches unreachable in the legacy.
        // The following assertion is what an all-bits port would additionally get right, and it is
        // included so the difference between the two readings is visible side by side.
        Assert.True(Bits.BitTest(EitherState, EitherState));

        // A POSITION-READING PORT WOULD RETURN TRUE HERE, because a second operand of 3 would name
        // bit 3, whose value is 4, and the operand holds exactly that bit. The mask reading shares
        // no bit with 3, so the answer must be false.
        Assert.False(Bits.BitTest(0x00000004u, EitherState));

        // The mask carries a bit VALUE and not a position, which is the practical consequence of
        // the above and the single most common way the two members are confused: testing the least
        // significant bit uses a mask of 1, and a mask of 0 tests nothing at all.
        Assert.True(Bits.BitTest(0xFFFFFFFFu, 0x00000001u));
        Assert.False(Bits.BitTest(0xFFFFFFFFu, 0x00000000u));
    }

    // ==========================================================================================
    // REGION 9 - GetBit. THE ONE MEMBER WHOSE CONTRACT THE REPOSITORY DOES NOT SETTLE.
    //
    // Oracle: ws_objects/pfw.common.pbl.src/getbit.srf:L7
    //             global function  boolean getbit(readonly ulong num, readonly uint bit)
    //         and NOTHING ELSE. This is the honest position and it is worth stating plainly: the
    //         file is declaration-only, the body is inside the closed pfw.dll, NO call site
    //         anywhere in the legacy tree exercises this member, and no comment records the index
    //         base. Unlike BitXor, BitNot, BitRsh and MakeWord - equally uncalled, but whose names
    //         admit exactly one meaning - "the bit at position n" is ambiguous precisely because n
    //         needs an origin.
    //
    // WHAT IS DECLARED, AND THEREFORE WHAT IS PROVABLE. The position operand is 16-bit while
    // BitTest's mask operand is 32-bit. That width contrast is real evidence, it is pinned in
    // Region 0, and it is the one thing about this member the repository does establish: whatever
    // the base turns out to be, this parameter is a POSITION and not a mask.
    //
    // TWO PORT-SIDE INFERENCES ARE PINNED BELOW, AND BOTH ARE LABELLED AS INFERENCES. The two
    // tests are named exactly as Bits.cs names them against its I-1 and I-2, so those
    // cross-references resolve and a later characterization run can find the choices deliberately
    // instead of discovering them by accident. Neither is presented as verified legacy behaviour,
    // and each is the single place to revise if the behavioural oracle contradicts it.
    //
    // C-B - THE FINDING, REPORTED RATHER THAN ENSHRINED.
    // The as-built GetBit carries a range guard that the prototype does not promise: a position
    // outside 1 to 32 returns false. That is reported here as a finding. It is nonetheless covered
    // by a test, and the reasoning for that is worth being explicit about, because the alternative
    // - leaving the case untested - would be worse in a specific way.
    //
    //     The guard adds NO throw, NO clamp and NO error code, so GetBit remains the total function
    //     every other member in this file is. Nothing improved-over-legacy is being locked in, and
    //     no test below asserts that any member rejects an argument.
    //
    //     Leaving it uncovered would not make the port neutral, it would make it silently
    //     arbitrary. Without the guard the shift count wraps, and the answer would be about a
    //     DIFFERENT bit than the caller named: position 0 would report on bit 32 and position 33 on
    //     bit 1, each with full confidence. That is categorically unlike the shift wrap accepted in
    //     Region 7, where the wrap IS the operation's defined behaviour and has a native analogue.
    //
    //     So the choice is pinned as a labelled INFERENCE rather than asserted as behaviour, which
    //     keeps the finding visible and revisable instead of burying it.
    // ==========================================================================================

    /// <summary>
    /// The thirty-two valid one-based bit positions of a 32-bit value.
    /// </summary>
    /// <returns>A row for each position from 1 to 32 inclusive.</returns>
    public static TheoryData<ushort> BitPositions()
    {
        TheoryData<ushort> rows = [];

        for (ushort position = 1; position <= 32; position++)
        {
            rows.Add(position);
        }

        return rows;
    }

    /// <summary>
    /// Single-bit-test rows, as (value, one-based position, expected result).
    /// </summary>
    /// <returns>Rows covering both ends of the width, the empty operand, and out-of-range positions.</returns>
    public static TheoryData<uint, ushort, bool> SingleBitRows()
    {
        TheoryData<uint, ushort, bool> rows = [];

        // ---- Position 1 is the least significant bit under the inferred one-based reading ----
        rows.Add(0x00000001u, (ushort)1, true);
        rows.Add(0x00000001u, (ushort)2, false);
        rows.Add(0x00000002u, (ushort)2, true);
        rows.Add(0x00000002u, (ushort)1, false);

        // ---- Position 32 is the most significant bit. Under a ZERO-based reading 32 would be out
        // of range and both of these would be false, so the pair is the decisive discrimination.
        rows.Add(0x80000000u, (ushort)32, true);
        rows.Add(0x80000000u, (ushort)31, false);
        rows.Add(0x40000000u, (ushort)31, true);

        // ---- A saturated operand holds every position, and an empty one holds none.
        rows.Add(0xFFFFFFFFu, (ushort)1, true);
        rows.Add(0xFFFFFFFFu, (ushort)16, true);
        rows.Add(0xFFFFFFFFu, (ushort)32, true);
        rows.Add(0u, (ushort)1, false);
        rows.Add(0u, (ushort)32, false);

        // ---- OUT OF RANGE, on a SATURATED operand deliberately. The operand matters: on an empty
        // operand these would return false whether or not the port guards the range, so they would
        // prove nothing. On a saturated operand an unguarded port wraps the shift count and returns
        // TRUE, so only these rows discriminate. This is inference I-2; see the named test below.
        rows.Add(0xFFFFFFFFu, (ushort)0, false);
        rows.Add(0xFFFFFFFFu, (ushort)33, false);
        rows.Add(0xFFFFFFFFu, (ushort)64, false);
        rows.Add(0xFFFFFFFFu, ushort.MaxValue, false);

        return rows;
    }

    /// <summary>
    /// Proves that <see cref="Bits.GetBit(uint, ushort)"/> tests the single bit at the given
    /// one-based position, across the full 32-bit width.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="position">The one-based bit position.</param>
    /// <param name="expected">Whether that bit is set.</param>
    [Theory]
    [MemberData(nameof(SingleBitRows))]
    public void SingleBitTestReadsTheBitAtTheGivenPosition(uint value, ushort position, bool expected)
    {
        Assert.Equal(expected, Bits.GetBit(value, position));
    }

    /// <summary>
    /// Proves that the thirty-two valid positions map one-to-one onto the thirty-two bits of the
    /// operand, so no two positions can name the same bit and none is unreachable.
    /// </summary>
    /// <param name="position">The one-based position under test.</param>
    [Theory]
    [MemberData(nameof(BitPositions))]
    public void SingleBitTestSelectsExactlyOneBitAtEveryPositionOfTheWidth(ushort position)
    {
        // The operand is built with BitLsh, which Region 7 pins independently. Using it here buys a
        // second property beyond the bijection: it states that BitLsh and GetBit AGREE on where bit
        // n lives, which no test of either member alone can establish. The one-position offset is
        // the inferred one-based origin made explicit.
        uint isolated = Bits.BitLsh(1u, (ushort)(position - 1));

        Assert.True(Bits.GetBit(isolated, position));

        // No OTHER position may see this bit. Sweeping all thirty-one alternatives is what upgrades
        // the claim from "this position works" to "the mapping is injective", which is the property
        // that would be violated by an off-by-one in either direction: an off-by-one port would
        // find the bit at position + 1 or position - 1 and fail here rather than only at a boundary.
        for (ushort other = 1; other <= 32; other++)
        {
            if (other == position)
            {
                continue;
            }

            Assert.False(Bits.GetBit(isolated, other));
        }

        // And the complement holds the exact inverse at every position, which pins that the member
        // reads the bit rather than merely detecting a non-zero operand.
        uint inverted = Bits.BitNot(isolated);

        Assert.False(Bits.GetBit(inverted, position));

        for (ushort other = 1; other <= 32; other++)
        {
            if (other == position)
            {
                continue;
            }

            Assert.True(Bits.GetBit(inverted, other));
        }
    }

    /// <summary>
    /// Pins the INFERRED index base of <see cref="Bits.GetBit(uint, ushort)"/>: positions are
    /// numbered from ONE, so position 1 is the least significant bit and position 32 the most
    /// significant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZED FROM THE PORT, NOT FROM THE LEGACY. This test is named exactly as
    /// <c>Bits.cs</c> names it against its inference I-1, so that cross-reference resolves. The
    /// repository does not adjudicate the base: <c>getbit.srf</c> is declaration-only over the
    /// closed <c>pfw.dll</c>, no call site anywhere exercises the member, and no comment records
    /// it. One-based was chosen because PowerBuilder is pervasively one-based - arrays start at 1
    /// and its upper-bound function returns the last valid index - and because the plan singles out
    /// one-based to zero-based translation as the single most dangerous mechanical hazard in this
    /// refactor, which makes a one-based reading the likelier intent for a PowerBuilder-facing
    /// helper. It remains an inference, and this test is the single place to revise if the
    /// behavioural oracle contradicts it.
    /// </para>
    /// <para>
    /// Callers who need certainty today should prefer <see cref="Bits.BitTest(uint, uint)"/>, whose
    /// mask convention is derived from its call-site corpus rather than inferred, and which is
    /// pinned in Region 8.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetBitInferredIndexBaseIsOneBased()
    {
        // THE DECISIVE PAIR AT THE BOTTOM OF THE WIDTH. Under a zero-based reading, position 1
        // would name the SECOND bit, so the first assertion would be false and the second true.
        // Asserting both directions is what makes this a discrimination rather than a sample.
        Assert.True(Bits.GetBit(0x00000001u, (ushort)1));
        Assert.False(Bits.GetBit(0x00000002u, (ushort)1));

        Assert.True(Bits.GetBit(0x00000002u, (ushort)2));
        Assert.False(Bits.GetBit(0x00000001u, (ushort)2));

        // THE DECISIVE PAIR AT THE TOP OF THE WIDTH, which is the stronger of the two because a
        // zero-based reading would place position 32 out of range entirely and return false.
        Assert.True(Bits.GetBit(0x80000000u, (ushort)32));
        Assert.False(Bits.GetBit(0x80000000u, (ushort)31));

        Assert.True(Bits.GetBit(0x40000000u, (ushort)31));
        Assert.False(Bits.GetBit(0x40000000u, (ushort)32));

        // Position 0 belongs to no bit under a one-based reading, whereas a zero-based port would
        // read the least significant bit there and return true. Stated on a saturated operand so
        // that the answer is discriminating; see the note in SingleBitRows.
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)0));
    }

    /// <summary>
    /// Pins the INFERRED answer for a position outside the valid range 1 to 32: it returns
    /// <see langword="false"/> and does not throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZED FROM THE PORT, NOT FROM THE LEGACY, and reported as a C-B finding in the
    /// Region 9 banner. This test is named exactly as <c>Bits.cs</c> names it against its inference
    /// I-2, so that cross-reference resolves. The legacy prototype promises no validation of any
    /// kind, and nothing asserted here is a validation behaviour: the member stays total, returns a
    /// value for every input, and never throws or clamps.
    /// </para>
    /// <para>
    /// The case has to be decided explicitly rather than left to the shift arithmetic, because the
    /// arithmetic's answer is actively misleading: a shift count for a 32-bit operand is masked to
    /// its low five bits, so an unguarded port would answer position 0 by reporting on bit 32 and
    /// position 33 by reporting on bit 1, each with complete confidence about the wrong bit. False
    /// is also the correct SHAPE for the answer and not merely the safe one, since a position that
    /// does not exist cannot hold a set bit.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetBitInferredIndexBaseOutOfRangeReturnsFalse()
    {
        // EVERY ASSERTION HERE USES A SATURATED OPERAND, AND THAT IS ESSENTIAL. On an empty operand
        // an unguarded port would also return false, so such an assertion would pass either way and
        // pin nothing. On a saturated operand an unguarded port wraps the count and returns TRUE,
        // so these are the only assertions that discriminate.

        // Below the range. An unguarded port shifts by a count that masks to 31 and reports bit 32.
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)0));

        // One past the top. An unguarded port shifts by a count that masks to 0 and reports bit 1.
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)33));

        // Further out, including a full second width and the maximum the 16-bit position parameter
        // can carry. Nothing is rejected and nothing throws; the answer is simply false.
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)34));
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)64));
        Assert.False(Bits.GetBit(0xFFFFFFFFu, (ushort)1000));
        Assert.False(Bits.GetBit(0xFFFFFFFFu, ushort.MaxValue));

        // The boundary positions immediately INSIDE the range must still be true on the same
        // operand. Without these the guard could be too wide - rejecting 1 and 32 as well - and
        // every assertion above would still pass. This is what pins the range as exactly 1 to 32.
        Assert.True(Bits.GetBit(0xFFFFFFFFu, (ushort)1));
        Assert.True(Bits.GetBit(0xFFFFFFFFu, (ushort)32));
    }
}
