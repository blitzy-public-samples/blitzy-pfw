// =====================================================================================================
//  PinyinFirstLetterMatcherTests.cs
//
//  Suite for Expressions/PinyinFirstLetterMatcher.cs - the managed substitute for the native
//  `pfwPinyinFirstLetterLike` DataWindow filter function.
//
//  READ THIS HEADER BEFORE ADDING OR CHANGING A CASE. This file is not an ordinary parity suite and the
//  asymmetry between what it asserts and what it deliberately refuses to assert is the point of it.
//
// -----------------------------------------------------------------------------------------------------
//  1. THIS UNIT CARRIES OPEN RISK R1 - THE SINGLE GENUINE PARITY RISK IN THE WHOLE IN-SCOPE SET
// -----------------------------------------------------------------------------------------------------
//  ORACLE, all READ-ONLY, never an edit target:
//    ws_objects/pfw.utility.pbl.src/pinyinfirstletterlike.srf              the two prototypes
//    ws_objects/pfw.shared.pbl.src/enums.sru:L1144-L1151                   the three [flags] bits
//    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L313-L326
//                                                                          the SOLE call site
//    ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw          the oracle workflow
//    ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_dddw.srd                   the DDDW fixture
//
//  `pinyinfirstletterlike.srf` is ELEVEN LINES and has an EMPTY type body [:L3-L4]. Its whole behaviour
//  is two forward prototypes bound straight to a closed binary:
//
//      global function boolean PinyinFirstLetterLike (readonly string text, readonly string py)
//          system library "pfw.dll" alias for "pfwPinyinFirstLetterLike"                        [:L7]
//      global function boolean PinyinFirstLetterLike (readonly string text, readonly string py,
//          readonly unsignedlong flags)
//          system library "pfw.dll" alias for "pfwPinyinFirstLetterLike"                        [:L8]
//
//  There is NO PowerScript to port and no C++ source for pfw.dll anywhere in the tree. That single fact
//  is the whole of risk R1, and docs/PARITY.md ("R1") states the required response without hedging:
//
//      "Report the pinyin filter as BLOCKED. Do not approximate it."
//
//  The reasoning is specific rather than cautious, and it is what shapes every case below. A substitute
//  pinyin table gets most characters right and a minority wrong, so an approximated filter returns
//  SUBTLY DIFFERENT ROW SETS - mostly correct, occasionally missing a row, occasionally including one.
//  That is a regression that LOOKS like correct behaviour: it passes review, it passes a unit test
//  written against the substitute's own table, and it is found by a user rather than by a suite. A
//  BLOCKED report is a KNOWN gap; an approximation is an UNKNOWN one.
//
//  NO THIRD-PARTY PINYIN PACKAGE MAY BE ADDED, and none is used here. The dependency inventory excludes
//  every pinyin package by name, because when the acceptance criterion is bit-exact parity with a closed
//  table, trusting a DIFFERENT table converts a known gap into an unknown one while looking like
//  progress. This file adds no package, and Directory.Packages.props carries no pinyin PackageVersion
//  for one to be referenced from.
//
// -----------------------------------------------------------------------------------------------------
//  2. WHAT IS DOCUMENTED, AND WHAT IS CLOSED - THE BOUNDARY THIS SUITE IS BUILT AROUND
// -----------------------------------------------------------------------------------------------------
//  DOCUMENTED, therefore asserted exactly (constraints C-B / G2 - where behaviour IS characterized,
//  assert it):
//    * The three flag bits and their meanings [enums.sru:L1146-L1149], under the source's own heading
//      `//PinyinFirstLetterLike:[flags]`. The literal 7 the sole call site passes is fully explained by
//      them, so it is DECOMPOSED here rather than treated as a magic number.
//    * The prototype pair's shape: two overloads, `boolean` return, and a 32-bit unsigned flag argument.
//    * The three fuzzy-sound pairs the flag comment names: l=n, f=h, r=l.
//    * How the function is REACHED: as a named function inside a DataWindow filter expression, never as
//      a PowerScript call.
//
//  CLOSED, therefore never guessed - each has a seam, and each unconfigured seam is asserted to produce
//  the defined BLOCKED outcome:
//    (a) THE LOOKUP TABLE. No table, no data file and no source for one exists in the tree.
//    (b) THE MATCHING RELATION. "Like" is not a definition. Prefix, substring and whole are all
//        candidate readings and the repository never says which.
//    (c) THE TWO-ARGUMENT OVERLOAD'S EFFECTIVE FLAGS. Both prototypes alias ONE native export, so the
//        binary decides what the omitted argument becomes. Zero and seven are both plausible.
//    (d) THE FUZZY SET's EXHAUSTIVENESS AND CLOSURE. Three pairs are named; whether there are more, and
//        whether `l`~`n` plus `r`~`l` implies `n`~`r`, are both unstated - and the two readings return
//        different row sets.
//
// -----------------------------------------------------------------------------------------------------
//  3. THE LABELLING POLICY - ALGORITHM TESTS ARE NOT PARITY TESTS
// -----------------------------------------------------------------------------------------------------
//  Some cases here drive a real comparison, and they can only do that through an INJECTED table. No such
//  case is evidence of bit-exact fidelity to pfw.dll, and none of them may ever be read as such. Two
//  devices keep that unmistakable:
//
//    * Every such case lives in a class or region whose name says ALGORITHM, and its comment says so
//      again.
//    * The injected table is DELIBERATELY WRONG BY CONSTRUCTION. `SyntheticPinyinFirstLetterTable` maps
//      甲 (whose real initial is `j`) to `a`, 乙 (`y`) to `b`, 戊 (`w`) to `l`, and so on - not one
//      assignment is the character's actual pinyin initial. So a reader cannot mistake the fixture for
//      the closed table, and a matcher that had smuggled in a real table of its own would FAIL these
//      cases rather than pass them. Proving the seam drives everything is exactly what makes the unit
//      characterizable at all.
//
//  There is therefore NO hardcoded pinyin mapping anywhere in this file presented as parity, and no
//  assertion that any Han character has any particular initial.
//
// -----------------------------------------------------------------------------------------------------
//  4. BLOCKED IS AN OUTCOME, NOT AN ABSENCE - AND NEVER `false`
// -----------------------------------------------------------------------------------------------------
//  The unit answers an unconfigured seam with PinyinMatchOutcome.Unavailable plus a
//  PinyinMatchUnavailableReason naming WHICH seam, and this suite asserts that three-way discipline:
//
//    * NOT a silent non-match. A caller that cannot tell "this row does not match" from "matching is not
//      available" filters rows away and reports success. So the cases below assert
//      `Outcome == Unavailable` and `Outcome != NoMatch`; NOT ONE of them asserts that a blocked
//      comparison "is false", because that assertion is the very conflation the mandate forbids.
//    * NOT an exception out of expression evaluation. TryInvokeFromExpression returns false with a
//      structured ExpressionParseError carrying RetCode.E_NO_IMPLEMENTATION.
//    * NOT unobservable. The two `bool`-returning parity overloads cannot express a third state - their
//      signature is fixed by the oracle - so they throw the DEFINED PinyinMatchUnavailableException.
//
//  A deliberately-skipped, SELF-ACTIVATING characterization theory closes the loop: see
//  PinyinOracleCharacterizationHookTests. The moment a legacy oracle recording appears under
//  characterization/recordings/legacy/<workflowId>/ it un-skips itself, so nobody has to remember it.
//
// -----------------------------------------------------------------------------------------------------
//  5. HOUSE RULES OBSERVED HERE
// -----------------------------------------------------------------------------------------------------
//    * The three flag constants are consumed BY NAME from PowerFramework.Shared.Kernel.Enums. They are
//      redeclared nowhere - not in the unit, not here - and a reflection case asserts that. The numerals
//      1, 2 and 4 appear in exactly ONE case, whose entire subject is the catalogue's correspondence to
//      [enums.sru:L1147-L1149]; every other case names the constants. That single case is what lets all
//      the others use names safely, so it is a requirement rather than an exception to the rule.
//    * Flag decomposition goes through Bits.BitTest - the ported primitive the legacy itself uses - and
//      never through arithmetic. Note BitTest is ANY-bit, so "all three set" is proved bit by bit.
//    * Matrices are xunit theories with TheoryData member data; assertions are plain xunit `Assert`
//      (there is no mocking or fluent-assertion package in the inventory, by design).
//    * This file declares no SCREAMING_SNAKE identifier, so it needs no .editorconfig naming suppression.
//    * Nullable reference types and warnings-as-errors apply here exactly as they do to the unit.
// =====================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

// =====================================================================================================
//  TEST INFRASTRUCTURE
// =====================================================================================================

/// <summary>
/// The synthetic characters this suite matches over, and the letters it maps them to.
/// </summary>
/// <remarks>
/// <para>
/// EVERY ASSIGNMENT IS DELIBERATELY NOT THE CHARACTER'S REAL PINYIN INITIAL. That is the safeguard, not
/// an accident: it makes it impossible to read this fixture as a claim about the closed
/// <c>pfw.dll</c> table, and it means a unit that had smuggled in a real table would fail the algorithm
/// cases instead of passing them.
/// </para>
/// <para>
/// The characters are the Ten Heavenly Stems, chosen only because they are ordinary, unambiguous BMP
/// ideographs. Their actual initials are given beside each member so the divergence is visible on the
/// page rather than taken on trust.
/// </para>
/// </remarks>
internal static class PinyinSyntheticAlphabet
{
    /// <summary>甲 U+7532. Real initial <c>j</c>; mapped here to <c>a</c>.</summary>
    public const char MapsToA = '甲';

    /// <summary>乙 U+4E59. Real initial <c>y</c>; mapped here to <c>b</c>.</summary>
    public const char MapsToB = '乙';

    /// <summary>丙 U+4E19. Real initial <c>b</c>; mapped here to <c>c</c>.</summary>
    public const char MapsToC = '丙';

    /// <summary>丁 U+4E01. Real initial <c>d</c>; mapped here to <c>e</c>.</summary>
    public const char MapsToE = '丁';

    /// <summary>戊 U+620A. Real initial <c>w</c>; mapped here to <c>l</c> - a fuzzy-pair letter.</summary>
    public const char MapsToL = '戊';

    /// <summary>己 U+5DF1. Real initial <c>j</c>; mapped here to <c>n</c> - a fuzzy-pair letter.</summary>
    public const char MapsToN = '己';

    /// <summary>庚 U+5E9A. Real initial <c>g</c>; mapped here to <c>f</c> - a fuzzy-pair letter.</summary>
    public const char MapsToF = '庚';

    /// <summary>辛 U+8F9B. Real initial <c>x</c>; mapped here to <c>h</c> - a fuzzy-pair letter.</summary>
    public const char MapsToH = '辛';

    /// <summary>癸 U+7678. Real initial <c>g</c>; mapped here to <c>r</c> - a fuzzy-pair letter.</summary>
    public const char MapsToR = '癸';

    /// <summary>壬 U+58EC. Real initial <c>r</c>; deliberately given NO reading in any fixture.</summary>
    /// <remarks>
    /// The uncovered character. It exists so a case can provoke
    /// <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/> without disturbing any other
    /// mapping.
    /// </remarks>
    public const char Uncovered = '壬';

    /// <summary>
    /// A SUPPLEMENTARY-PLANE ideograph, U+20000, which is a surrogate PAIR in UTF-16.
    /// </summary>
    /// <remarks>
    /// Present to prove the unit offers the table one <see cref="Rune"/> rather than two surrogate
    /// halves. A table keyed by <c>char</c> could never answer for it, which is why the seam is keyed by
    /// <see cref="Rune"/>.
    /// </remarks>
    public static Rune SupplementaryPlane { get; } = new(0x20000);

    /// <summary>The letter <see cref="SupplementaryPlane"/> is mapped to.</summary>
    public const char SupplementaryPlaneLetter = 'q';
}

/// <summary>
/// A deterministic, INJECTED first-letter table. THE PRINCIPAL CHARACTERIZATION SEAM, exercised.
/// </summary>
/// <remarks>
/// <para>
/// NOT A PINYIN TABLE AND NOT A SUBSTITUTE FOR ONE. It carries whatever a case teaches it, and the
/// standard fixture teaches it deliberately wrong readings - see <see cref="PinyinSyntheticAlphabet"/>.
/// Its <see cref="SourceDescription"/> says so in words, so the string turns up in any diagnostic a
/// failing case prints.
/// </para>
/// <para>
/// It RECORDS every character it was asked for, which is what lets a case assert that iteration is by
/// rune and that matching is driven entirely by the seam rather than by anything the unit knows on its
/// own.
/// </para>
/// <para>
/// The production interface requires implementations to be thread-safe; this one is not, because it
/// accumulates its request log. Each case constructs its own instance and drives it from one thread.
/// </para>
/// </remarks>
internal sealed class SyntheticPinyinFirstLetterTable : IPinyinFirstLetterTable
{
    /// <summary>
    /// The default source description, worded so that it cannot be mistaken for the real table.
    /// </summary>
    public const string SyntheticSource = "synthetic-non-linguistic-test-fixture (NOT the pfw.dll table)";

    private readonly Dictionary<Rune, ImmutableArray<char>> _readings = [];
    private readonly List<Rune> _requested = [];

    /// <summary>
    /// Creates an EMPTY table - one that covers nothing at all.
    /// </summary>
    /// <param name="sourceDescription">The description to report. Defaults to
    /// <see cref="SyntheticSource"/>.</param>
    /// <remarks>
    /// An empty table is a legitimate and distinct state: it says "the recording covered nothing",
    /// whereas a null configuration says "there is no recording". The unit reports the two differently
    /// and a case below asserts that.
    /// </remarks>
    public SyntheticPinyinFirstLetterTable(string sourceDescription = SyntheticSource) =>
        SourceDescription = sourceDescription;

    /// <inheritdoc/>
    public string SourceDescription { get; }

    /// <summary>
    /// Every character the unit asked about, in the order it asked, including repeats.
    /// </summary>
    public IReadOnlyList<Rune> RequestedCharacters => _requested;

    /// <summary>
    /// Teaches this table one BMP character's readings.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <param name="letters">Its readings, in order. More than one models a polyphonic character.</param>
    /// <returns>This instance, so fixtures read as a single expression.</returns>
    public SyntheticPinyinFirstLetterTable With(char character, params char[] letters) =>
        With(new Rune(character), letters);

    /// <summary>
    /// Teaches this table one character's readings, addressed by rune so supplementary-plane characters
    /// are expressible.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <param name="letters">Its readings, in order.</param>
    /// <returns>This instance.</returns>
    public SyntheticPinyinFirstLetterTable With(Rune character, params char[] letters)
    {
        _readings[character] = [.. letters];
        return this;
    }

    /// <summary>
    /// Teaches this table that a character is present but has NO readings - a contract violation the
    /// unit is required to survive.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>This instance.</returns>
    /// <remarks>
    /// The interface says an implementation must not return an empty array on a <see langword="true"/>
    /// return. This method breaks that on purpose, so a case can prove the unit reports
    /// <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/> rather than deriving an initial
    /// string with a hole in it.
    /// </remarks>
    public SyntheticPinyinFirstLetterTable WithNoReadings(char character)
    {
        _readings[new Rune(character)] = [];
        return this;
    }

    /// <inheritdoc/>
    public bool TryGetFirstLetters(Rune character, out ImmutableArray<char> letters)
    {
        _requested.Add(character);
        return _readings.TryGetValue(character, out letters);
    }
}

/// <summary>
/// Builders for the matcher configurations this suite drives.
/// </summary>
/// <remarks>
/// Every builder makes the CLOSED inputs explicit at the call site. There is no builder that quietly
/// supplies a table or a strategy, because a case that did not state which relation it was exercising
/// would be asserting something nobody could name.
/// </remarks>
internal static class PinyinMatcherFixture
{
    /// <summary>
    /// The standard synthetic table: the nine mapped characters plus the supplementary-plane one, and
    /// NOT <see cref="PinyinSyntheticAlphabet.Uncovered"/>.
    /// </summary>
    /// <returns>A fresh table for one case.</returns>
    public static SyntheticPinyinFirstLetterTable ScrambledTable() =>
        new SyntheticPinyinFirstLetterTable()
            .With(PinyinSyntheticAlphabet.MapsToA, 'a')
            .With(PinyinSyntheticAlphabet.MapsToB, 'b')
            .With(PinyinSyntheticAlphabet.MapsToC, 'c')
            .With(PinyinSyntheticAlphabet.MapsToE, 'e')
            .With(PinyinSyntheticAlphabet.MapsToL, 'l')
            .With(PinyinSyntheticAlphabet.MapsToN, 'n')
            .With(PinyinSyntheticAlphabet.MapsToF, 'f')
            .With(PinyinSyntheticAlphabet.MapsToH, 'h')
            .With(PinyinSyntheticAlphabet.MapsToR, 'r')
            .With(
                PinyinSyntheticAlphabet.SupplementaryPlane,
                PinyinSyntheticAlphabet.SupplementaryPlaneLetter);

    /// <summary>
    /// Builds a matcher whose closed inputs are all supplied - the shape a characterization recording
    /// would eventually produce.
    /// </summary>
    /// <param name="strategy">The matching relation to exercise. Stated explicitly, never defaulted to
    /// a guess.</param>
    /// <param name="table">The injected table. Defaults to <see cref="ScrambledTable"/>.</param>
    /// <param name="defaultFlags">
    /// The two-argument overload's effective flags, or <see langword="null"/> to leave that one closed
    /// input BLOCKED while the rest are open.
    /// </param>
    /// <param name="fuzzySound">
    /// The fuzzy relation. Defaults to <see cref="PinyinFuzzySoundEquivalence.Documented"/> - the three
    /// oracle pairs read literally.
    /// </param>
    /// <returns>The matcher.</returns>
    public static PinyinFirstLetterMatcher Matcher(
        PinyinMatchStrategy strategy,
        IPinyinFirstLetterTable? table = null,
        uint? defaultFlags = null,
        PinyinFuzzySoundEquivalence? fuzzySound = null) =>
        new(new PinyinMatchConfiguration
        {
            Table = table ?? ScrambledTable(),
            Strategy = strategy,
            CharacterizedDefaultFlags = defaultFlags,
            FuzzySound = fuzzySound ?? PinyinFuzzySoundEquivalence.Documented,
        });

    /// <summary>
    /// The flag mask the sole legacy call site passes, named rather than written as a numeral.
    /// </summary>
    /// <remarks>
    /// [n_cst_dwsvc_dropdownsearch.sru:L323] writes the literal <c>7</c>; the unit composes it from the
    /// three named constants, and this alias keeps every case below reading the composed value.
    /// </remarks>
    public static uint CallSiteFlags => PinyinFirstLetterMatcher.LegacyCallSiteFlags;

    /// <summary>
    /// A mask with no flag set - the "no relaxations at all" end of the power set.
    /// </summary>
    public static uint NoFlags => 0u;

    /// <summary>
    /// Composes a mask from the named catalogue constants, narrowed to the 32-bit parameter width.
    /// </summary>
    /// <param name="ignoreCase">Whether to set <c>PY_LIKE_IGNORE_CASE</c>.</param>
    /// <param name="ignoreWidth">Whether to set <c>PY_LIKE_IGNORE_WIDTH</c>.</param>
    /// <param name="fuzzySound">Whether to set <c>PY_LIKE_FUZZY_SOUND</c>.</param>
    /// <returns>The composed mask.</returns>
    /// <remarks>
    /// Composition goes through <see cref="Bits.BitOr(uint, uint)"/> - the ported primitive - so no case
    /// in this file has to add or or-together raw numerals to build a mask.
    /// </remarks>
    public static uint Flags(bool ignoreCase, bool ignoreWidth, bool fuzzySound)
    {
        uint mask = 0u;

        if (ignoreCase)
        {
            mask = Bits.BitOr(mask, (uint)Enums.PY_LIKE_IGNORE_CASE);
        }

        if (ignoreWidth)
        {
            mask = Bits.BitOr(mask, (uint)Enums.PY_LIKE_IGNORE_WIDTH);
        }

        if (fuzzySound)
        {
            mask = Bits.BitOr(mask, (uint)Enums.PY_LIKE_FUZZY_SOUND);
        }

        return mask;
    }

    /// <summary>
    /// Builds a one-character string from a synthetic alphabet member.
    /// </summary>
    /// <param name="characters">The characters.</param>
    /// <returns>The text.</returns>
    public static string Text(params char[] characters) => new(characters);
}

// =====================================================================================================
//  1. SIGNATURE PARITY WITH THE NATIVE PROTOTYPES
//     ORACLE ws_objects/pfw.utility.pbl.src/pinyinfirstletterlike.srf:L7-L8
// =====================================================================================================

/// <summary>
/// Asserts that the managed substitute reproduces the oracle's prototype pair exactly.
/// </summary>
/// <remarks>
/// <para>
/// THE SIGNATURE IS THE ONE PART OF THIS FUNCTION THE REPOSITORY FULLY SPECIFIES, so it is asserted
/// structurally by reflection rather than merely exercised. The prototypes are declaration-only - the
/// <c>.srf</c>'s type body is empty [<c>:L3-L4</c>] - so their shape is the entire portable content of
/// the source file, and a divergence in arity, order, width or return type would be a divergence from
/// 100% of what the oracle states.
/// </para>
/// <para>
/// These are PARITY cases. They need no lookup table, because a signature has nothing to do with the
/// closed inputs.
/// </para>
/// </remarks>
public sealed class PinyinFirstLetterMatcherSignatureParityTests
{
    /// <summary>
    /// The declared parity overloads, shortest arity first.
    /// </summary>
    /// <returns>The overloads.</returns>
    private static MethodInfo[] ParityOverloads() =>
        [.. typeof(PinyinFirstLetterMatcher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(
                method.Name,
                nameof(PinyinFirstLetterMatcher.PinyinFirstLetterLike),
                StringComparison.Ordinal))
            .OrderBy(method => method.GetParameters().Length)];

    /// <summary>
    /// EXACTLY TWO overloads exist, matching the two prototypes and no more.
    /// </summary>
    /// <remarks>
    /// The count matters in both directions. Fewer would drop a declared entry point; a third would
    /// invent an arity the oracle does not have, and callers reached through a DataWindow filter
    /// expression - which is how this function is reached at all - resolve by arity.
    /// </remarks>
    [Fact]
    public void TheOracleDeclaresTwoOverloadsAndExactlyTwoArePresent()
    {
        MethodInfo[] overloads = ParityOverloads();

        Assert.Equal(2, overloads.Length);
        Assert.Equal(2, overloads[0].GetParameters().Length);
        Assert.Equal(3, overloads[1].GetParameters().Length);
    }

    /// <summary>
    /// Both overloads return <see cref="bool"/>, which is the prototypes' <c>boolean</c>.
    /// </summary>
    [Fact]
    public void BothOverloadsReturnBoolean()
    {
        foreach (MethodInfo overload in ParityOverloads())
        {
            Assert.Equal(typeof(bool), overload.ReturnType);
        }
    }

    /// <summary>
    /// The two-argument overload is <c>(string text, string py)</c> - types, order AND parameter names.
    /// </summary>
    /// <remarks>
    /// The NAMES are asserted because they are part of the published surface in C#: a caller may pass
    /// arguments by name, and the oracle spells them <c>text</c> and <c>py</c> [<c>:L7</c>]. Renaming
    /// either would be a silent source-breaking change with no oracle warrant.
    /// </remarks>
    [Fact]
    public void TheTwoArgumentOverloadTakesTextThenPattern()
    {
        ParameterInfo[] parameters = ParityOverloads()[0].GetParameters();

        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("text", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("py", parameters[1].Name);
    }

    /// <summary>
    /// The three-argument overload adds <c>uint flags</c> - a THIRTY-TWO BIT UNSIGNED parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE WIDTH IS A CONTRACT, NOT A STYLE CHOICE, and it is the one place a plausible-looking mistake
    /// is easy to make. The oracle declares <c>readonly unsignedlong flags</c> [<c>:L8</c>], and
    /// PowerBuilder's <c>unsignedlong</c> is 32 bits wide - so the managed type is <see cref="uint"/>
    /// and emphatically NOT <see cref="ulong"/>, whose NAME coincides with the PowerBuilder type while
    /// its width does not.
    /// </para>
    /// <para>
    /// <see cref="long"/> or <see cref="int"/> would be wrong for a second, independent reason: flag
    /// testing goes through the ported <see cref="Bits"/> primitives, whose own operands are 32-bit
    /// unsigned. A signed or 64-bit parameter would need a conversion at every bit test, which is
    /// exactly where a sign-extension or truncation defect hides. That claim is CHECKED rather than
    /// asserted - the case reads <see cref="Bits.BitTest(uint, uint)"/>'s own parameter types below.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeArgumentOverloadsFlagParameterIsThirtyTwoBitUnsigned()
    {
        ParameterInfo[] parameters = ParityOverloads()[1].GetParameters();

        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("text", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("py", parameters[1].Name);

        Assert.Equal(typeof(uint), parameters[2].ParameterType);
        Assert.Equal("flags", parameters[2].Name);
        Assert.NotEqual(typeof(ulong), parameters[2].ParameterType);
        Assert.NotEqual(typeof(long), parameters[2].ParameterType);
        Assert.NotEqual(typeof(int), parameters[2].ParameterType);

        // The shared kernel's bit primitive this width has to agree with. Both operands are uint, so
        // the matcher's flag parameter reaches BitTest with no conversion at all.
        MethodInfo bitTest = typeof(Bits).GetMethod(
            nameof(Bits.BitTest),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.All(bitTest.GetParameters(), parameter =>
            Assert.Equal(typeof(uint), parameter.ParameterType));
    }

    /// <summary>
    /// The two are DISTINCT OVERLOADS, not one method with an optional third parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is load-bearing rather than cosmetic. The oracle declares two prototypes, and the arity is
    /// OBSERVABLE: an optional parameter would let a caller omit the argument and silently receive
    /// whichever default the port had nominated - while the two-argument form is precisely the one whose
    /// effective flags are a closed input and is therefore BLOCKED. Collapsing the pair would delete the
    /// distinction the BLOCKED state depends on.
    /// </para>
    /// <para>
    /// Also asserted: no parameter is <c>ref</c>, <c>out</c> or <c>in</c>. PowerBuilder's
    /// <c>readonly</c> forbids assignment to the parameter, which a by-value immutable
    /// <see cref="string"/> already guarantees, so nothing here should be passed by reference.
    /// </para>
    /// </remarks>
    [Fact]
    public void NeitherOverloadHasAnOptionalOrByReferenceParameter()
    {
        foreach (MethodInfo overload in ParityOverloads())
        {
            Assert.All(overload.GetParameters(), parameter =>
            {
                Assert.False(parameter.IsOptional, parameter.Name);
                Assert.False(parameter.ParameterType.IsByRef, parameter.Name);
                Assert.False(parameter.IsOut, parameter.Name);
            });
        }
    }

    /// <summary>
    /// The name is spelled as the LEGACY writes it, not as the native export is aliased.
    /// </summary>
    /// <remarks>
    /// [<c>pinyinfirstletterlike.srf:L7-L8</c>] binds the PowerScript name
    /// <c>PinyinFirstLetterLike</c> to the export <c>pfwPinyinFirstLetterLike</c>. It is the
    /// PowerScript-visible name that appears in the filter string the drop-down search service builds
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L323</c>], and therefore the name an expression resolves. The
    /// export alias is an implementation detail of a binary this port does not load.
    /// </remarks>
    [Fact]
    public void TheManagedNameIsThePowerScriptNameAndNotTheNativeAlias()
    {
        Assert.Equal(
            nameof(PinyinFirstLetterMatcher.PinyinFirstLetterLike),
            PinyinFirstLetterMatcher.ExpressionFunctionName);

        Assert.NotEqual("pfwPinyinFirstLetterLike", PinyinFirstLetterMatcher.ExpressionFunctionName);
    }

    /// <summary>
    /// THE TWO-ARGUMENT OVERLOAD'S DEFAULT IS PINNED AS "NOT CHARACTERIZED", which is what the
    /// implementation declares and what the repository supports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy default is not documented anywhere: both prototypes alias ONE native export, so
    /// <c>pfw.dll</c> - not the PowerScript - decides what the omitted argument becomes. Zero and seven
    /// are both plausible and they select different rows. The implementation therefore refuses to
    /// nominate one, and this case pins that refusal so it cannot be "helpfully" filled in later.
    /// </para>
    /// <para>
    /// PRECEDENCE IS PART OF THE PIN. The check runs BEFORE the table and strategy seams are consulted,
    /// so the reported reason names the arity-specific gap rather than a downstream one. On the shipped
    /// BLOCKED matcher - which has no table either - the reason is still
    /// <see cref="PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized"/>, and that ordering is
    /// what makes the diagnostic actionable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoArgumentOverloadIsBlockedUntilItsDefaultFlagsAreCharacterized()
    {
        Assert.Null(PinyinMatchConfiguration.Blocked.CharacterizedDefaultFlags);
        Assert.False(PinyinFirstLetterMatcher.Blocked.IsDefaultFlagSetCharacterized);

        PinyinMatchResult result = PinyinFirstLetterMatcher.Blocked.TryMatch(
            PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
            "a");

        Assert.Equal(PinyinMatchOutcome.Unavailable, result.Outcome);
        Assert.Equal(PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized, result.UnavailableReason);

        // Reported before the table seam, even though no table is configured either.
        Assert.NotEqual(PinyinMatchUnavailableReason.LookupTableNotConfigured, result.UnavailableReason);

        // No mask was chosen, so none is reported. Nothing was assumed.
        Assert.Equal(0u, result.Flags);

        // A fully characterized matcher that still withholds the default keeps ONLY that form blocked.
        PinyinFirstLetterMatcher partial = PinyinMatcherFixture.Matcher(PinyinMatchStrategy.Prefix);
        string covered = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        Assert.True(partial.CanMatch);
        Assert.False(partial.IsDefaultFlagSetCharacterized);
        Assert.Equal(
            PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized,
            partial.TryMatch(covered, "a").UnavailableReason);
        Assert.True(partial.TryMatch(covered, "a", PinyinMatcherFixture.CallSiteFlags).IsAvailable);
    }

    /// <summary>
    /// Once the default IS characterized, the two-argument form behaves exactly as the three-argument
    /// form called with that mask.
    /// </summary>
    /// <remarks>
    /// The equivalence is the whole meaning of "the effective flag set", so it is asserted rather than
    /// assumed - including that the reported <see cref="PinyinMatchResult.Flags"/> is the characterized
    /// mask and not zero. Fields are compared individually because
    /// <see cref="PinyinMatchResult.DerivedInitials"/> is an <see cref="ImmutableArray{T}"/>, whose
    /// default equality compares the underlying array by reference and would therefore never call two
    /// independently-built results equal.
    /// </remarks>
    [Fact]
    public void ACharacterizedDefaultMakesTheTwoArgumentFormEquivalentToTheExplicitMask()
    {
        uint mask = PinyinMatcherFixture.CallSiteFlags;
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: mask);

        string text = PinyinMatcherFixture.Text(
            PinyinSyntheticAlphabet.MapsToA,
            PinyinSyntheticAlphabet.MapsToB);

        PinyinMatchResult implicitFlags = matcher.TryMatch(text, "ab");
        PinyinMatchResult explicitFlags = matcher.TryMatch(text, "ab", mask);

        Assert.True(matcher.IsDefaultFlagSetCharacterized);
        Assert.Equal(explicitFlags.Outcome, implicitFlags.Outcome);
        Assert.Equal(explicitFlags.UnavailableReason, implicitFlags.UnavailableReason);
        Assert.Equal(mask, implicitFlags.Flags);
        Assert.Equal(explicitFlags.Flags, implicitFlags.Flags);
        Assert.Equal(explicitFlags.Text, implicitFlags.Text);
        Assert.Equal(explicitFlags.Pattern, implicitFlags.Pattern);
        Assert.Equal(explicitFlags.Diagnostic, implicitFlags.Diagnostic);
        Assert.Equal(
            (IEnumerable<string>)explicitFlags.DerivedInitials,
            (IEnumerable<string>)implicitFlags.DerivedInitials);
    }

    /// <summary>
    /// The parity overloads reject <see langword="null"/>; the reporting entry points tolerate it.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate and worth pinning. A <c>bool</c>-returning method has no way to
    /// describe a bad argument, so it throws; <c>TryMatch</c> exists to REPORT rather than to police, and
    /// a null reaching a filter predicate should produce a describable outcome instead of an exception
    /// from inside expression evaluation.
    /// </remarks>
    [Fact]
    public void TheParityOverloadsRejectNullWhileTryMatchTreatsItAsEmpty()
    {
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: PinyinMatcherFixture.CallSiteFlags);

        Assert.Throws<ArgumentNullException>(() => matcher.PinyinFirstLetterLike(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => matcher.PinyinFirstLetterLike("a", null!));
        Assert.Throws<ArgumentNullException>(
            () => matcher.PinyinFirstLetterLike(null!, "a", PinyinMatcherFixture.CallSiteFlags));
        Assert.Throws<ArgumentNullException>(
            () => matcher.PinyinFirstLetterLike("a", null!, PinyinMatcherFixture.CallSiteFlags));

        PinyinMatchResult bothNull = matcher.TryMatch(null, null, PinyinMatcherFixture.CallSiteFlags);

        Assert.True(bothNull.IsAvailable);
        Assert.Equal(string.Empty, bothNull.Text);
        Assert.Equal(string.Empty, bothNull.Pattern);
        Assert.Equal(PinyinMatchUnavailableReason.Unspecified, bothNull.UnavailableReason);

        // The two-argument reporting form tolerates null as well, and still reports its own gap.
        Assert.Equal(
            PinyinMatchOutcome.Unavailable,
            PinyinFirstLetterMatcher.Blocked.TryMatch(null, null).Outcome);
    }

    /// <summary>
    /// The constructor refuses a null configuration, so a matcher is never in an undefined state.
    /// </summary>
    [Fact]
    public void TheConstructorRefusesANullConfiguration() =>
        Assert.Throws<ArgumentNullException>(() => new PinyinFirstLetterMatcher(null!));
}

// =====================================================================================================
//  2. THE FLAG CONSTANTS, CONSUMED BY NAME, AND THE CALL SITE'S LITERAL 7
//     ORACLE ws_objects/pfw.shared.pbl.src/enums.sru:L1144-L1151
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323
// =====================================================================================================

/// <summary>
/// Asserts the flag contract: where the constants live, that they are not duplicated, that the sole call
/// site's literal decomposes into all three of them, and that the matcher's predicates agree with the
/// ported bit primitive across the whole mask power set.
/// </summary>
/// <remarks>
/// <para>
/// PARITY CASES. The flags are DOCUMENTED by the oracle - under its own heading
/// <c>//PinyinFirstLetterLike:[flags]</c> [<c>enums.sru:L1146</c>] - so unlike the table and the
/// matching relation, nothing here is characterized from a recording. This is the part of the risk that
/// is already closed, and asserting it precisely is what keeps the remaining risk correctly bounded.
/// </para>
/// <para>
/// The numerals 1, 2 and 4 appear exactly once in this file, in
/// <see cref="TheCatalogueCarriesTheOracleBitValues"/>, whose entire subject IS that correspondence to
/// the source. Every other case in the file names the constants instead - which is only safe BECAUSE
/// that one case pins their values.
/// </para>
/// </remarks>
public sealed class PinyinFlagConstantTests
{
    /// <summary>
    /// The three catalogue constants carry the oracle's own bit values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY PLACE IN THIS FILE THE FLAG NUMERALS ARE WRITTEN. The oracle declares, verbatim:
    /// </para>
    /// <code>
    /// constant long PY_LIKE_IGNORE_CASE  = 1   // 忽略大小写                    [enums.sru:L1147]
    /// constant long PY_LIKE_IGNORE_WIDTH = 2   // 忽略全角半角                  [enums.sru:L1148]
    /// constant long PY_LIKE_FUZZY_SOUND  = 4   // 匹配模糊发音（l=n，f=h，r=l）  [enums.sru:L1149]
    /// </code>
    /// <para>
    /// Also asserted: each is a single, distinct bit, and the three are pairwise disjoint. That is what
    /// makes them composable at all, and it is the property the sole call site's literal relies on.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCatalogueCarriesTheOracleBitValues()
    {
        Assert.Equal(1L, Enums.PY_LIKE_IGNORE_CASE);
        Assert.Equal(2L, Enums.PY_LIKE_IGNORE_WIDTH);
        Assert.Equal(4L, Enums.PY_LIKE_FUZZY_SOUND);

        // THREE CONSECUTIVE ASCENDING SINGLE BITS, expressed as the shift relation between them, which
        // together with the three values above is a complete characterization: each flag is its
        // predecessor shifted left by one, so none overlaps and none skips a position.
        //
        // Bits.GetBit is deliberately NOT used here even though it reads as the obvious primitive. Its
        // own documentation records that its index base is an INFERRED one-based choice, unverified
        // against the behavioural oracle and pending characterization. Asserting a documented pinyin fact
        // through an undocumented index base would make this case fail for an unrelated reason the day
        // that inference is revised. BitLsh and BitTest have established semantics, so they are used.
        Assert.Equal(
            (uint)Enums.PY_LIKE_IGNORE_WIDTH,
            Bits.BitLsh((uint)Enums.PY_LIKE_IGNORE_CASE, 1));
        Assert.Equal(
            (uint)Enums.PY_LIKE_FUZZY_SOUND,
            Bits.BitLsh((uint)Enums.PY_LIKE_IGNORE_WIDTH, 1));

        Assert.False(Bits.BitTest((uint)Enums.PY_LIKE_IGNORE_CASE, (uint)Enums.PY_LIKE_IGNORE_WIDTH));
        Assert.False(Bits.BitTest((uint)Enums.PY_LIKE_IGNORE_CASE, (uint)Enums.PY_LIKE_FUZZY_SOUND));
        Assert.False(Bits.BitTest((uint)Enums.PY_LIKE_IGNORE_WIDTH, (uint)Enums.PY_LIKE_FUZZY_SOUND));
    }

    /// <summary>
    /// The constants are declared in the shared catalogue ONLY - neither the matcher's namespace nor
    /// this test assembly redeclares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second declaration is the failure mode this case exists to prevent: two copies of a bit value
    /// drift, and the copy nearer the comparison wins silently. The catalogue is the single source, and
    /// the matcher's own <c>LegacyCallSiteFlags</c> is required to COMPOSE from it rather than restate a
    /// number.
    /// </para>
    /// <para>
    /// Asserted by scanning both assemblies for any member whose name carries the <c>PY_LIKE</c> prefix,
    /// which catches a redeclaration under any accessibility - including a private const, which is where
    /// such a copy would most plausibly appear.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFlagConstantsAreDeclaredInTheSharedCatalogueOnly()
    {
        const BindingFlags everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        List<string> redeclarations = [];

        Assembly[] assemblies =
        [
            typeof(PinyinFirstLetterMatcher).Assembly,
            typeof(PinyinFlagConstantTests).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MemberInfo member in type.GetMembers(everything))
                {
                    if (member.Name.Contains("PY_LIKE", StringComparison.Ordinal))
                    {
                        redeclarations.Add(type.FullName + "." + member.Name);
                    }
                }
            }
        }

        Assert.Empty(redeclarations);

        // And the one legitimate declaration site really is the shared kernel.
        Assert.Equal(
            typeof(Enums).Assembly,
            typeof(Bits).Assembly);
        Assert.NotNull(typeof(Enums).GetField(
            "PY_LIKE_IGNORE_CASE",
            BindingFlags.Public | BindingFlags.Static));
    }

    /// <summary>
    /// The call site's literal <c>7</c> decomposes into ALL THREE flags, bit by bit, through
    /// <see cref="Bits.BitTest(uint, uint)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sole call site in the entire repository is
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L323</c>], which emits
    /// </para>
    /// <code>
    /// sFilter += " OR PinyinFirstLetterLike(" + _editCtx.dddw.dispColName + ",'" + sData + "',7)"
    /// </code>
    /// <para>
    /// EACH BIT IS TESTED SEPARATELY, and that is required rather than fussy:
    /// <see cref="Bits.BitTest(uint, uint)"/> is ANY-bit - it reports whether the operand shares AT
    /// LEAST ONE bit with the mask - so a single test against a combined mask would pass with only one
    /// of the three set and would prove nothing. Three separate tests prove all three.
    /// </para>
    /// <para>
    /// The complement is asserted too, with <see cref="Bits.BitClear(uint, uint)"/>: once the three
    /// documented bits are cleared, NOTHING remains. So <c>7</c> is exactly these three flags and carries
    /// no fourth, undocumented bit.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCallSiteLiteralIsAllThreeFlagsAndNothingElse()
    {
        uint callSite = PinyinFirstLetterMatcher.LegacyCallSiteFlags;

        Assert.True(Bits.BitTest(callSite, (uint)Enums.PY_LIKE_IGNORE_CASE));
        Assert.True(Bits.BitTest(callSite, (uint)Enums.PY_LIKE_IGNORE_WIDTH));
        Assert.True(Bits.BitTest(callSite, (uint)Enums.PY_LIKE_FUZZY_SOUND));

        uint remainder = Bits.BitClear(
            Bits.BitClear(
                Bits.BitClear(callSite, (uint)Enums.PY_LIKE_IGNORE_CASE),
                (uint)Enums.PY_LIKE_IGNORE_WIDTH),
            (uint)Enums.PY_LIKE_FUZZY_SOUND);

        Assert.Equal(0u, remainder);

        // The constant is the composition of the three named constants, which is what makes the legacy
        // literal PROVEN rather than restated.
        Assert.Equal(
            (uint)(Enums.PY_LIKE_IGNORE_CASE
                | Enums.PY_LIKE_IGNORE_WIDTH
                | Enums.PY_LIKE_FUZZY_SOUND),
            callSite);

        // The literal as the DataWindow filter text spells it, which is how a reader of the oracle sees
        // it. Rendered from the composed constant rather than typed, so the two cannot drift.
        Assert.Equal(
            "7",
            PinyinFirstLetterMatcher.LegacyCallSiteFlags.ToString(CultureInfo.InvariantCulture));

        // And the matcher's own predicates agree that all three relaxations are in force.
        Assert.True(PinyinFirstLetterMatcher.IgnoresCase(callSite));
        Assert.True(PinyinFirstLetterMatcher.IgnoresWidth(callSite));
        Assert.True(PinyinFirstLetterMatcher.UsesFuzzySound(callSite));
    }

    /// <summary>
    /// Every combination of the three flags, with each predicate cross-checked against
    /// <see cref="Bits.BitTest(uint, uint)"/>.
    /// </summary>
    /// <returns>The eight masks, described by which flags they set.</returns>
    public static TheoryData<bool, bool, bool> MaskPowerSet()
    {
        TheoryData<bool, bool, bool> data = [];

        foreach (bool ignoreCase in new[] { false, true })
        {
            foreach (bool ignoreWidth in new[] { false, true })
            {
                foreach (bool fuzzySound in new[] { false, true })
                {
                    data.Add(ignoreCase, ignoreWidth, fuzzySound);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// Each predicate reports exactly its own bit, over the whole eight-member power set.
    /// </summary>
    /// <param name="ignoreCase">Whether the mask sets <c>PY_LIKE_IGNORE_CASE</c>.</param>
    /// <param name="ignoreWidth">Whether the mask sets <c>PY_LIKE_IGNORE_WIDTH</c>.</param>
    /// <param name="fuzzySound">Whether the mask sets <c>PY_LIKE_FUZZY_SOUND</c>.</param>
    /// <remarks>
    /// Exhaustive rather than sampled: the three flags are INDEPENDENT, and the only way to show a
    /// predicate does not read a neighbouring bit is to vary all three. The expected answers come from
    /// the composition, not from a table of literals.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MaskPowerSet))]
    public void EachFlagPredicateReadsOnlyItsOwnBit(bool ignoreCase, bool ignoreWidth, bool fuzzySound)
    {
        uint mask = PinyinMatcherFixture.Flags(ignoreCase, ignoreWidth, fuzzySound);

        Assert.Equal(ignoreCase, PinyinFirstLetterMatcher.IgnoresCase(mask));
        Assert.Equal(ignoreWidth, PinyinFirstLetterMatcher.IgnoresWidth(mask));
        Assert.Equal(fuzzySound, PinyinFirstLetterMatcher.UsesFuzzySound(mask));

        Assert.Equal(
            Bits.BitTest(mask, (uint)Enums.PY_LIKE_IGNORE_CASE),
            PinyinFirstLetterMatcher.IgnoresCase(mask));
        Assert.Equal(
            Bits.BitTest(mask, (uint)Enums.PY_LIKE_IGNORE_WIDTH),
            PinyinFirstLetterMatcher.IgnoresWidth(mask));
        Assert.Equal(
            Bits.BitTest(mask, (uint)Enums.PY_LIKE_FUZZY_SOUND),
            PinyinFirstLetterMatcher.UsesFuzzySound(mask));
    }

    /// <summary>
    /// Undocumented bits are IGNORED, neither rejected nor allowed to switch a documented relaxation on.
    /// </summary>
    /// <remarks>
    /// Only bits 1, 2 and 4 are documented, and what the closed binary does with any other bit is
    /// unknown. Refusing such a mask would invent a validation the oracle does not perform; honouring it
    /// would invent a behaviour. Ignoring it is the only reading that adds nothing, so it is pinned here.
    /// </remarks>
    [Fact]
    public void UndocumentedBitsAreIgnoredRatherThanRejected()
    {
        uint undocumented = Bits.BitLsh(1u, 3);
        uint mask = Bits.BitOr(undocumented, (uint)Enums.PY_LIKE_IGNORE_CASE);

        Assert.True(PinyinFirstLetterMatcher.IgnoresCase(mask));
        Assert.False(PinyinFirstLetterMatcher.IgnoresWidth(mask));
        Assert.False(PinyinFirstLetterMatcher.UsesFuzzySound(mask));

        // A mask of nothing but undocumented bits turns on no relaxation at all, and still decides.
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(PinyinMatchStrategy.Prefix);
        PinyinMatchResult result = matcher.TryMatch(
            PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
            "a",
            undocumented);

        Assert.True(result.IsAvailable);
        Assert.Equal(undocumented, result.Flags);
    }

    /// <summary>
    /// <see cref="PinyinFirstLetterMatcher.ToFlags(long)"/> narrows the catalogue's 64-bit constants onto
    /// the 32-bit parameter, and refuses anything that does not fit.
    /// </summary>
    /// <remarks>
    /// THREE WIDTHS MEET AT THIS BOUNDARY and all three are right in their own context: the catalogue
    /// declares the bits as <see cref="long"/> to match PowerBuilder's <c>long</c>, the published
    /// contract carries a 64-bit unsigned value, and this function's parameter is PowerBuilder
    /// <c>unsignedlong</c>, which is 32 bits. Narrowing THROWS rather than truncating, because a value
    /// that does not fit was never a legal mask and discarding its high bits would silently change which
    /// comparison ran.
    /// </remarks>
    [Fact]
    public void FlagNarrowingRoundTripsTheCatalogueAndRefusesAnythingThatDoesNotFit()
    {
        Assert.Equal(
            (uint)Enums.PY_LIKE_IGNORE_CASE,
            PinyinFirstLetterMatcher.ToFlags(Enums.PY_LIKE_IGNORE_CASE));
        Assert.Equal(
            (uint)Enums.PY_LIKE_IGNORE_WIDTH,
            PinyinFirstLetterMatcher.ToFlags(Enums.PY_LIKE_IGNORE_WIDTH));
        Assert.Equal(
            (uint)Enums.PY_LIKE_FUZZY_SOUND,
            PinyinFirstLetterMatcher.ToFlags(Enums.PY_LIKE_FUZZY_SOUND));
        Assert.Equal(
            PinyinFirstLetterMatcher.LegacyCallSiteFlags,
            PinyinFirstLetterMatcher.ToFlags(
                Enums.PY_LIKE_IGNORE_CASE
                | Enums.PY_LIKE_IGNORE_WIDTH
                | Enums.PY_LIKE_FUZZY_SOUND));

        Assert.Equal(0u, PinyinFirstLetterMatcher.ToFlags(0L));
        Assert.Equal(uint.MaxValue, PinyinFirstLetterMatcher.ToFlags(uint.MaxValue));

        Assert.Throws<ArgumentOutOfRangeException>(() => PinyinFirstLetterMatcher.ToFlags(-1L));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PinyinFirstLetterMatcher.ToFlags((long)uint.MaxValue + 1L));
    }
}

// =====================================================================================================
//  3. WHAT EACH FLAG ACTUALLY DOES TO A COMPARISON
//     ORACLE ws_objects/pfw.shared.pbl.src/enums.sru:L1147-L1149
// =====================================================================================================

/// <summary>
/// Asserts the observable effect of each documented flag on a comparison.
/// </summary>
/// <remarks>
/// <para>
/// A COMPARISON NEEDS A TABLE, SO THESE CASES DRIVE AN INJECTED ONE. What is asserted is the FLAG
/// SEMANTICS - which are documented [<c>enums.sru:L1147-L1149</c>] - and never the table's contents,
/// which are not. The injected table maps its characters to deliberately wrong letters
/// (<see cref="PinyinSyntheticAlphabet"/>), so no row below can be read as a claim about any character's
/// real pinyin initial.
/// </para>
/// <para>
/// The matching RELATION is held constant at <see cref="PinyinMatchStrategy.Prefix"/> throughout, because
/// it is orthogonal to the flags and is varied on its own in
/// <see cref="PinyinMatcherAlgorithmTests"/>. Every text and pattern here is one character long, so all
/// three candidate relations would agree and the relation cannot be confounding the result.
/// </para>
/// </remarks>
public sealed class PinyinFlagEffectTests
{
    /// <summary>
    /// A matcher over the standard scrambled table, with the prefix relation.
    /// </summary>
    /// <param name="fuzzySound">The fuzzy relation to install, or <see langword="null"/> for the
    /// documented one.</param>
    /// <returns>The matcher.</returns>
    private static PinyinFirstLetterMatcher Matcher(PinyinFuzzySoundEquivalence? fuzzySound = null) =>
        PinyinMatcherFixture.Matcher(PinyinMatchStrategy.Prefix, fuzzySound: fuzzySound);

    // -------------------------------------------------------------------------------------------------
    //  PY_LIKE_IGNORE_CASE - 忽略大小写 [enums.sru:L1147]
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// An upper-case pattern letter against a lower-case derived initial, over the whole mask power set.
    /// </summary>
    /// <returns>Each mask, described by its three bits.</returns>
    public static TheoryData<bool, bool, bool> MaskPowerSet() => PinyinFlagConstantTests.MaskPowerSet();

    /// <summary>
    /// Case is ignored when <c>PY_LIKE_IGNORE_CASE</c> is set - AND ALSO, as an emergent consequence,
    /// whenever <c>PY_LIKE_FUZZY_SOUND</c> is set.
    /// </summary>
    /// <param name="ignoreCase">Whether the mask sets <c>PY_LIKE_IGNORE_CASE</c>.</param>
    /// <param name="ignoreWidth">Whether the mask sets <c>PY_LIKE_IGNORE_WIDTH</c>.</param>
    /// <param name="fuzzySound">Whether the mask sets <c>PY_LIKE_FUZZY_SOUND</c>.</param>
    /// <remarks>
    /// <para>
    /// THE FUZZY FLAG SUBSUMES THE CASE FLAG. This is not what a reader of the flag names would predict,
    /// so it is pinned here rather than left to be discovered. The mechanism is exact: the per-character
    /// comparison only case-folds when the case bit is set, but the fuzzy relation folds BOTH of its
    /// operands to lower case invariantly - it has to, because the documented pairs are recorded in lower
    /// case - and it is reflexive. So with fuzzy set and case clear, an upper-case pattern letter still
    /// matches its own lower-case self through the fuzzy relation.
    /// </para>
    /// <para>
    /// IT IS RECORDED, NOT CORRECTED (constraint C-B). Nothing in the repository says the closed binary
    /// behaves differently, and the sole call site passes BOTH bits
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L323</c>], so the legacy never distinguishes the two cases at
    /// all. Anyone who later characterizes the binary and finds otherwise changes the unit and this case
    /// together.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MaskPowerSet))]
    public void CaseIsIgnoredUnderTheCaseFlagAndAlsoUnderTheFuzzyFlag(
        bool ignoreCase,
        bool ignoreWidth,
        bool fuzzySound)
    {
        uint mask = PinyinMatcherFixture.Flags(ignoreCase, ignoreWidth, fuzzySound);
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        PinyinMatchResult upperCasePattern = Matcher().TryMatch(text, "A", mask);

        Assert.True(upperCasePattern.IsAvailable);
        Assert.Equal(
            ignoreCase || fuzzySound ? PinyinMatchOutcome.Match : PinyinMatchOutcome.NoMatch,
            upperCasePattern.Outcome);

        // The exactly-equal pattern matches under every mask, so the row above is isolating case alone.
        Assert.Equal(PinyinMatchOutcome.Match, Matcher().TryMatch(text, "a", mask).Outcome);
    }

    // -------------------------------------------------------------------------------------------------
    //  PY_LIKE_IGNORE_WIDTH - 忽略全角半角 [enums.sru:L1148]
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A fullwidth pattern letter matches a halfwidth derived initial only under
    /// <c>PY_LIKE_IGNORE_WIDTH</c>.
    /// </summary>
    /// <remarks>
    /// Note the fullwidth form is NOT reached by the case or fuzzy flags: neither folds width, so this
    /// case isolates the width bit even with the other two set.
    /// </remarks>
    [Fact]
    public void AFullWidthPatternMatchesOnlyUnderTheWidthFlag()
    {
        // U+FF41, the fullwidth small a. Its halfwidth equivalent is the derived initial 'a'.
        const string fullWidthPattern = "\uFF41";
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        uint withWidth = PinyinMatcherFixture.Flags(
            ignoreCase: false,
            ignoreWidth: true,
            fuzzySound: false);
        uint withoutWidth = PinyinMatcherFixture.Flags(
            ignoreCase: true,
            ignoreWidth: false,
            fuzzySound: true);

        Assert.Equal(
            PinyinMatchOutcome.Match,
            Matcher().TryMatch(text, fullWidthPattern, withWidth).Outcome);

        PinyinMatchResult unfolded = Matcher().TryMatch(text, fullWidthPattern, withoutWidth);

        Assert.True(unfolded.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, unfolded.Outcome);

        // A fullwidth CAPITAL needs the width flag AND one of the two case-folding flags.
        const string fullWidthCapital = "\uFF21";

        Assert.Equal(
            PinyinMatchOutcome.NoMatch,
            Matcher().TryMatch(text, fullWidthCapital, withWidth).Outcome);
        Assert.Equal(
            PinyinMatchOutcome.Match,
            Matcher().TryMatch(text, fullWidthCapital, PinyinMatcherFixture.CallSiteFlags).Outcome);
    }

    /// <summary>
    /// The width flag folds the TEXT before the table is consulted, so it changes AVAILABILITY and not
    /// merely the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the sharpest available demonstration that folding happens on the way IN to the lookup
    /// rather than at the comparison. The table is taught the HALFWIDTH <c>A</c> only. With the width
    /// flag, a fullwidth <c>Ａ</c> in the text folds to <c>A</c>, the table answers, and the comparison
    /// decides. Without it, the table is asked about <c>Ａ</c>, has no reading, and the unit reports
    /// <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/> - NOT a non-match.
    /// </para>
    /// <para>
    /// The requested-character log proves which character was actually looked up, so the claim rests on
    /// observation rather than inference.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWidthFlagFoldsTheTextBeforeTheTableIsConsulted()
    {
        const char fullWidthCapitalA = '\uFF21';
        const char halfWidthCapitalA = 'A';

        SyntheticPinyinFirstLetterTable table =
            new SyntheticPinyinFirstLetterTable().With(halfWidthCapitalA, 'x');
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            table);

        uint withWidth = PinyinMatcherFixture.Flags(
            ignoreCase: false,
            ignoreWidth: true,
            fuzzySound: false);

        PinyinMatchResult folded = matcher.TryMatch(
            PinyinMatcherFixture.Text(fullWidthCapitalA),
            "x",
            withWidth);

        Assert.Equal(PinyinMatchOutcome.Match, folded.Outcome);
        Assert.Contains(new Rune(halfWidthCapitalA), table.RequestedCharacters);
        Assert.DoesNotContain(new Rune(fullWidthCapitalA), table.RequestedCharacters);

        SyntheticPinyinFirstLetterTable unfoldedTable =
            new SyntheticPinyinFirstLetterTable().With(halfWidthCapitalA, 'x');
        PinyinMatchResult unfolded = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix, unfoldedTable)
            .TryMatch(PinyinMatcherFixture.Text(fullWidthCapitalA), "x", PinyinMatcherFixture.NoFlags);

        Assert.Equal(PinyinMatchOutcome.Unavailable, unfolded.Outcome);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableIncomplete, unfolded.UnavailableReason);
        Assert.NotEqual(PinyinMatchOutcome.NoMatch, unfolded.Outcome);
        Assert.Contains(new Rune(fullWidthCapitalA), unfoldedTable.RequestedCharacters);
    }

    /// <summary>
    /// The fullwidth-to-halfwidth fold, at every boundary of the range it is defined over.
    /// </summary>
    /// <returns>Input character and the character it folds to.</returns>
    /// <remarks>
    /// The relation is the Unicode halfwidth-and-fullwidth block's own: <c>U+FF01</c> through
    /// <c>U+FF5E</c> map onto <c>U+0021</c> through <c>U+007E</c> by a fixed offset, and the ideographic
    /// space <c>U+3000</c> maps to the ordinary space. BOTH BOUNDARIES ARE TESTED FROM OUTSIDE as well as
    /// inside, because an off-by-one at either end would fold a character the flag says nothing about.
    /// </remarks>
    public static TheoryData<char, char> WidthFolds() => new()
    {
        // Inside the contiguous range: first, last, and two ordinary members.
        { '\uFF01', '!' },
        { '\uFF5E', '~' },
        { '\uFF41', 'a' },
        { '\uFF21', 'A' },
        // The ideographic space, which sits outside the contiguous range and is handled separately.
        { '\u3000', ' ' },
        // Immediately below and immediately above the range - unchanged.
        { '\uFF00', '\uFF00' },
        { '\uFF5F', '\uFF5F' },
        // Already halfwidth, and an ideograph: both unchanged.
        { 'a', 'a' },
        { 'Z', 'Z' },
        { PinyinSyntheticAlphabet.MapsToA, PinyinSyntheticAlphabet.MapsToA },
    };

    /// <summary>
    /// Each character folds to exactly its documented halfwidth equivalent, and nothing else folds.
    /// </summary>
    /// <param name="input">The character to fold.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [MemberData(nameof(WidthFolds))]
    public void WidthFoldingCoversTheFullWidthBlockAndNothingBeyondIt(char input, char expected) =>
        Assert.Equal(expected, PinyinFirstLetterMatcher.FoldWidth(input));

    // -------------------------------------------------------------------------------------------------
    //  PY_LIKE_FUZZY_SOUND - 匹配模糊发音（l=n，f=h，r=l） [enums.sru:L1149]
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The three documented fuzzy pairs, each in BOTH directions.
    /// </summary>
    /// <returns>The synthetic text character, the letter it derives to, and the partner letter used as
    /// the pattern.</returns>
    /// <remarks>
    /// The oracle writes the pairs with <c>=</c> [<c>enums.sru:L1149</c>], and equality is symmetric, so
    /// both directions are required rather than optional. Six rows: three pairs times two directions.
    /// </remarks>
    public static TheoryData<char, char, char> FuzzyPairs() => new()
    {
        // l = n
        { PinyinSyntheticAlphabet.MapsToL, 'l', 'n' },
        { PinyinSyntheticAlphabet.MapsToN, 'n', 'l' },
        // f = h
        { PinyinSyntheticAlphabet.MapsToF, 'f', 'h' },
        { PinyinSyntheticAlphabet.MapsToH, 'h', 'f' },
        // r = l
        { PinyinSyntheticAlphabet.MapsToR, 'r', 'l' },
        { PinyinSyntheticAlphabet.MapsToL, 'l', 'r' },
    };

    /// <summary>
    /// Each documented pair matches under <c>PY_LIKE_FUZZY_SOUND</c> and decidedly does NOT match with
    /// the flag clear.
    /// </summary>
    /// <param name="textCharacter">The synthetic character in the text.</param>
    /// <param name="derivedLetter">The letter the injected table derives from it.</param>
    /// <param name="partnerLetter">The pair's other letter, used as the pattern.</param>
    /// <remarks>
    /// BOTH HALVES MATTER. Asserting only the match would pass even if the unit ignored the flag and
    /// always applied the relation, which would silently widen every filter; asserting the flag-clear
    /// non-match is what makes the flag observable. The flag-clear result is also asserted to be
    /// AVAILABLE, so it is a real, decided negative rather than a reported gap.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FuzzyPairs))]
    public void EachDocumentedFuzzyPairMatchesOnlyWhenTheFuzzyFlagIsSet(
        char textCharacter,
        char derivedLetter,
        char partnerLetter)
    {
        string text = PinyinMatcherFixture.Text(textCharacter);
        string pattern = PinyinMatcherFixture.Text(partnerLetter);

        uint withFuzzy = PinyinMatcherFixture.Flags(
            ignoreCase: false,
            ignoreWidth: false,
            fuzzySound: true);

        PinyinMatchResult fuzzy = Matcher().TryMatch(text, pattern, withFuzzy);

        Assert.Equal(PinyinMatchOutcome.Match, fuzzy.Outcome);

        // The injected table really did derive the letter this row names - so the row is exercising the
        // fuzzy relation between two letters, not an accident of the fixture.
        Assert.Equal([PinyinMatcherFixture.Text(derivedLetter)], fuzzy.DerivedInitials);

        PinyinMatchResult exact = Matcher().TryMatch(text, pattern, PinyinMatcherFixture.NoFlags);

        Assert.True(exact.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, exact.Outcome);

        // And the letter still matches itself with the flag clear, so the negative above is about the
        // PAIR rather than about the fixture failing to derive anything.
        Assert.Equal(
            PinyinMatchOutcome.Match,
            Matcher()
                .TryMatch(text, PinyinMatcherFixture.Text(derivedLetter), PinyinMatcherFixture.NoFlags)
                .Outcome);
    }

    /// <summary>
    /// THE FUZZY SET IS NOT TRANSITIVELY CLOSED BY DEFAULT: <c>n</c> and <c>r</c> do not match each
    /// other, even though each matches <c>l</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the subtle half of closed input (d) and it is easy to miss. The oracle names
    /// <c>l=n</c>, <c>f=h</c> and <c>r=l</c>, and <c>l</c> appears in TWO of them. Read as an equivalence
    /// relation the classes are <c>{l,n,r}</c> and <c>{f,h}</c>, so <c>n</c> matches <c>r</c>. Read as
    /// unordered pairs only, <c>l</c>~<c>n</c> and <c>l</c>~<c>r</c> hold while <c>n</c>~<c>r</c> does
    /// not. THE TWO READINGS RETURN DIFFERENT ROW SETS and nothing in the repository settles which the
    /// closed binary implements.
    /// </para>
    /// <para>
    /// So the NON-IMPLICATION is asserted rather than closure assumed: the shipped reading is the
    /// literal one, exactly the three pairs as written, because defaulting to the closure would infer a
    /// property the source does not state. The alternative reading is also asserted, through the seam,
    /// so that whichever the oracle turns out to support is a configuration change rather than a rewrite
    /// - and so that a future reader can see both behaviours pinned side by side.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFuzzySetIsPairwiseByDefaultAndTransitiveOnlyWhenConfigured()
    {
        // The shipped reading, on the relation itself.
        Assert.Equal(PinyinFuzzySoundClosure.PairwiseOnly, PinyinFuzzySoundEquivalence.Documented.Closure);
        Assert.Equal(
            PinyinFuzzySoundClosure.PairwiseOnly,
            PinyinMatchConfiguration.Blocked.FuzzySound.Closure);

        Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('l', 'n'));
        Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('l', 'r'));
        Assert.False(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('n', 'r'));
        Assert.False(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('r', 'n'));

        uint withFuzzy = PinyinMatcherFixture.Flags(
            ignoreCase: false,
            ignoreWidth: false,
            fuzzySound: true);

        string textDerivingN = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToN);
        string textDerivingR = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToR);

        // Through the matcher: the non-implication holds end to end, in both directions.
        Assert.Equal(PinyinMatchOutcome.NoMatch, Matcher().TryMatch(textDerivingN, "r", withFuzzy).Outcome);
        Assert.Equal(PinyinMatchOutcome.NoMatch, Matcher().TryMatch(textDerivingR, "n", withFuzzy).Outcome);

        // The other reading, supplied through the seam rather than inferred.
        PinyinFuzzySoundEquivalence closed = PinyinFuzzySoundEquivalence.Documented with
        {
            Closure = PinyinFuzzySoundClosure.TransitiveClosure,
        };

        Assert.True(closed.AreEquivalent('n', 'r'));
        Assert.True(closed.AreEquivalent('r', 'n'));
        Assert.Equal(
            PinyinMatchOutcome.Match,
            Matcher(closed).TryMatch(textDerivingN, "r", withFuzzy).Outcome);

        // Closure merges only the two classes that share `l`. It does not join `f`/`h` to them.
        Assert.False(closed.AreEquivalent('f', 'l'));
        Assert.False(closed.AreEquivalent('h', 'n'));
        Assert.True(closed.AreEquivalent('f', 'h'));
    }

    /// <summary>
    /// The relation is reflexive and symmetric under both closures, and carries exactly the three oracle
    /// pairs in the oracle's own order.
    /// </summary>
    /// <remarks>
    /// REFLEXIVITY IS WHAT MAKES THE FUZZY FLAG SAFE: because every letter is equivalent to itself, the
    /// flag changes WHICH relation is used and never WHETHER one is used, so setting it can only ever
    /// widen a match and never lose one.
    /// </remarks>
    [Fact]
    public void TheDocumentedRelationIsReflexiveSymmetricAndCarriesTheOraclePairsInOrder()
    {
        Assert.Equal(3, PinyinFuzzySoundEquivalence.DocumentedPairs.Length);
        Assert.Equal(new PinyinFuzzySoundPair('l', 'n'), PinyinFuzzySoundEquivalence.DocumentedPairs[0]);
        Assert.Equal(new PinyinFuzzySoundPair('f', 'h'), PinyinFuzzySoundEquivalence.DocumentedPairs[1]);
        Assert.Equal(new PinyinFuzzySoundPair('r', 'l'), PinyinFuzzySoundEquivalence.DocumentedPairs[2]);

        Assert.Equal(
            (IEnumerable<PinyinFuzzySoundPair>)PinyinFuzzySoundEquivalence.DocumentedPairs,
            (IEnumerable<PinyinFuzzySoundPair>)PinyinFuzzySoundEquivalence.Documented.Pairs);

        PinyinFuzzySoundEquivalence closed = PinyinFuzzySoundEquivalence.Documented with
        {
            Closure = PinyinFuzzySoundClosure.TransitiveClosure,
        };

        foreach (char letter in "lnfhrxa")
        {
            Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent(letter, letter));
            Assert.True(closed.AreEquivalent(letter, letter));
        }

        foreach (PinyinFuzzySoundPair pair in PinyinFuzzySoundEquivalence.DocumentedPairs)
        {
            Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent(pair.First, pair.Second));
            Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent(pair.Second, pair.First));

            // Case-insensitive on both sides, because the pairs are recorded in lower case.
            Assert.True(PinyinFuzzySoundEquivalence.Documented.AreEquivalent(
                char.ToUpperInvariant(pair.First),
                pair.Second));
        }

        // A letter in no pair is equivalent to nothing but itself.
        Assert.False(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('a', 'b'));
    }

    /// <summary>
    /// The pair set is REPLACEABLE, and its exhaustiveness flag is an explicit statement of the open
    /// question rather than a gate on matching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's comment names three pairs and does not claim to be complete; conventions in the wild
    /// routinely also fold <c>z</c>/<c>zh</c>, <c>c</c>/<c>ch</c> and <c>s</c>/<c>sh</c>. So the set is a
    /// seam - a characterization run can supply more pairs without this suite or the unit guessing that
    /// they apply.
    /// </para>
    /// <para>
    /// THE FLAG DELIBERATELY DOES NOT GATE MATCHING, and the asymmetry against the table is the
    /// interesting part: the three documented pairs ARE documented, so refusing to use them would
    /// discard evidence the repository actually contains. Only whether there are MORE is unknown. The
    /// table gates matching because there no evidence exists at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePairSetIsInjectableAndItsExhaustivenessFlagDoesNotGateMatching()
    {
        Assert.False(PinyinFuzzySoundEquivalence.Documented.ExhaustivenessConfirmed);

        // Unconfirmed, yet a comparison still decides - the flag is a statement, not a gate.
        uint withFuzzy = PinyinMatcherFixture.Flags(
            ignoreCase: false,
            ignoreWidth: false,
            fuzzySound: true);

        Assert.True(Matcher()
            .TryMatch(PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToL), "n", withFuzzy)
            .IsAvailable);

        // An additional pair supplied through the seam takes effect; one not supplied does not.
        PinyinFuzzySoundEquivalence extended = PinyinFuzzySoundEquivalence.Documented with
        {
            Pairs = [.. PinyinFuzzySoundEquivalence.DocumentedPairs, new PinyinFuzzySoundPair('a', 'e')],
            ExhaustivenessConfirmed = true,
        };

        Assert.True(extended.ExhaustivenessConfirmed);
        Assert.True(extended.AreEquivalent('a', 'e'));
        Assert.False(PinyinFuzzySoundEquivalence.Documented.AreEquivalent('a', 'e'));
        Assert.Equal(
            PinyinMatchOutcome.Match,
            Matcher(extended)
                .TryMatch(PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA), "e", withFuzzy)
                .Outcome);
    }

    /// <summary>
    /// The relation renders itself for a log and for the parity record, in both closures and both
    /// confirmation states.
    /// </summary>
    [Fact]
    public void TheRelationDescribesItselfStably()
    {
        Assert.Equal(
            "l=n,f=h,r=l (pairwise, exhaustiveness unconfirmed)",
            PinyinFuzzySoundEquivalence.Documented.Describe());

        PinyinFuzzySoundEquivalence settled = PinyinFuzzySoundEquivalence.Documented with
        {
            Closure = PinyinFuzzySoundClosure.TransitiveClosure,
            ExhaustivenessConfirmed = true,
        };

        Assert.Equal("l=n,f=h,r=l (transitive, exhaustiveness confirmed)", settled.Describe());
    }

    // -------------------------------------------------------------------------------------------------
    //  THE EMPTY MASK
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// With no flag set, NONE of the three relaxations applies and the comparison is exact.
    /// </summary>
    /// <remarks>
    /// The floor of the power set, asserted as one case so the three negatives are visible together. Each
    /// is a DECIDED non-match, not a reported gap, which is what distinguishes "the flag is off" from
    /// "matching is unavailable".
    /// </remarks>
    [Fact]
    public void AnEmptyMaskAppliesNoneOfTheThreeRelaxations()
    {
        uint none = PinyinMatcherFixture.NoFlags;

        Assert.False(PinyinFirstLetterMatcher.IgnoresCase(none));
        Assert.False(PinyinFirstLetterMatcher.IgnoresWidth(none));
        Assert.False(PinyinFirstLetterMatcher.UsesFuzzySound(none));

        string derivesA = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        string derivesL = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToL);

        // Case: not ignored.
        PinyinMatchResult wrongCase = Matcher().TryMatch(derivesA, "A", none);
        Assert.True(wrongCase.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, wrongCase.Outcome);

        // Width: not folded.
        PinyinMatchResult wrongWidth = Matcher().TryMatch(derivesA, "\uFF41", none);
        Assert.True(wrongWidth.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, wrongWidth.Outcome);

        // Fuzzy sound: not applied.
        PinyinMatchResult wrongSound = Matcher().TryMatch(derivesL, "n", none);
        Assert.True(wrongSound.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, wrongSound.Outcome);

        // And the exact letter still matches, so the three negatives above are about the flags.
        Assert.Equal(PinyinMatchOutcome.Match, Matcher().TryMatch(derivesA, "a", none).Outcome);
    }
}

// =====================================================================================================
//  4. THE ALGORITHM - *** ALGORITHM CASES, NOT PARITY CASES ***
// =====================================================================================================

/// <summary>
/// Exercises the matcher's ALGORITHM over an injected table.
/// </summary>
/// <remarks>
/// <para>
/// *** NOTHING IN THIS CLASS IS EVIDENCE OF BIT-EXACT FIDELITY TO <c>pfw.dll</c>. *** Every case here
/// supplies its own table and its own matching relation and then asserts that the unit applies them
/// consistently. The unit's ALGORITHMIC properties are testable to full precision; its FIDELITY is not,
/// because the real table and the real relation live only inside the closed binary. Confusing the two is
/// exactly the mistake risk R1 warns about, so this class is named and commented to make the confusion
/// impossible.
/// </para>
/// <para>
/// THE FIXTURE IS WRONG ON PURPOSE. <see cref="PinyinSyntheticAlphabet"/> maps every character to a
/// letter that is NOT its pinyin initial. Two consequences, both deliberate: no reader can mistake these
/// rows for a claim about Chinese, and a unit that had smuggled in a table of its own would FAIL these
/// cases rather than pass them. <see cref="MatchingIsDrivenEntirelyByTheInjectedTable"/> makes that
/// second consequence an explicit assertion.
/// </para>
/// </remarks>
public sealed class PinyinMatcherAlgorithmTests
{
    /// <summary>
    /// Text whose injected reduction is <c>abc</c> - the subject of the relation matrix.
    /// </summary>
    private static string ThreeCharacterText => PinyinMatcherFixture.Text(
        PinyinSyntheticAlphabet.MapsToA,
        PinyinSyntheticAlphabet.MapsToB,
        PinyinSyntheticAlphabet.MapsToC);

    /// <summary>
    /// The flag mask the algorithm cases use: the sole call site's, so the relation is exercised under
    /// the only mask the legacy actually passes.
    /// </summary>
    private static uint Flags => PinyinMatcherFixture.CallSiteFlags;

    /// <summary>
    /// PROOF THAT THE SEAM DRIVES EVERYTHING: the same character answers differently under two different
    /// injected tables, and the answer is never the character's real pinyin initial unless a table says
    /// so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 甲's real initial is <c>j</c>. The first table here says <c>z</c> and the second says <c>j</c>, and
    /// the unit follows whichever it was given. A unit carrying a built-in table would answer <c>j</c>
    /// under BOTH and would fail the first half of this case - which is precisely why this case exists.
    /// It is also what makes the unit CHARACTERIZABLE: an oracle-derived table can be loaded through the
    /// same seam and will drive the comparison in the same way.
    /// </para>
    /// <para>
    /// The request log is asserted too, so the claim rests on the table having actually been consulted
    /// rather than on the answer merely coming out right.
    /// </para>
    /// </remarks>
    [Fact]
    public void MatchingIsDrivenEntirelyByTheInjectedTable()
    {
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        SyntheticPinyinFirstLetterTable saysZ =
            new SyntheticPinyinFirstLetterTable().With(PinyinSyntheticAlphabet.MapsToA, 'z');
        PinyinFirstLetterMatcher underZ = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            saysZ);

        Assert.Equal(PinyinMatchOutcome.Match, underZ.TryMatch(text, "z", Flags).Outcome);
        Assert.Equal(PinyinMatchOutcome.NoMatch, underZ.TryMatch(text, "j", Flags).Outcome);
        Assert.Contains(new Rune(PinyinSyntheticAlphabet.MapsToA), saysZ.RequestedCharacters);

        SyntheticPinyinFirstLetterTable saysJ =
            new SyntheticPinyinFirstLetterTable().With(PinyinSyntheticAlphabet.MapsToA, 'j');
        PinyinFirstLetterMatcher underJ = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            saysJ);

        Assert.Equal(PinyinMatchOutcome.Match, underJ.TryMatch(text, "j", Flags).Outcome);
        Assert.Equal(PinyinMatchOutcome.NoMatch, underJ.TryMatch(text, "z", Flags).Outcome);

        // The table's identity travels with any diagnostic, so a surprising answer is attributable.
        Assert.Equal(SyntheticPinyinFirstLetterTable.SyntheticSource, saysJ.SourceDescription);
    }

    /// <summary>
    /// The three candidate readings of "Like", over a reduction of <c>abc</c>.
    /// </summary>
    /// <returns>The relation, the pattern, and whether it matches.</returns>
    /// <remarks>
    /// WHICH OF THESE THREE THE BINARY IMPLEMENTS IS UNKNOWN - that is closed input (b), and it is why
    /// <see cref="PinyinMatchStrategy.NotCharacterized"/> is the zero of that enumeration and why a
    /// configuration that omits the relation is BLOCKED rather than quietly taking one of these. The
    /// matrix asserts that each relation, ONCE CHOSEN, is applied correctly - not that any of them is the
    /// right one.
    /// </remarks>
    public static TheoryData<PinyinMatchStrategy, string, bool> Relations() => new()
    {
        // Prefix: the reduction must START WITH the pattern.
        { PinyinMatchStrategy.Prefix, "a", true },
        { PinyinMatchStrategy.Prefix, "ab", true },
        { PinyinMatchStrategy.Prefix, "abc", true },
        { PinyinMatchStrategy.Prefix, "b", false },
        { PinyinMatchStrategy.Prefix, "bc", false },
        { PinyinMatchStrategy.Prefix, "c", false },
        { PinyinMatchStrategy.Prefix, "abcd", false },
        { PinyinMatchStrategy.Prefix, "", true },

        // Substring: the pattern must occur ANYWHERE within the reduction.
        { PinyinMatchStrategy.Substring, "a", true },
        { PinyinMatchStrategy.Substring, "b", true },
        { PinyinMatchStrategy.Substring, "c", true },
        { PinyinMatchStrategy.Substring, "ab", true },
        { PinyinMatchStrategy.Substring, "bc", true },
        { PinyinMatchStrategy.Substring, "abc", true },
        // Not contiguous, so not a substring - the case that separates substring from subsequence.
        { PinyinMatchStrategy.Substring, "ac", false },
        { PinyinMatchStrategy.Substring, "abcd", false },
        { PinyinMatchStrategy.Substring, "", true },

        // Whole: the reduction must EQUAL the pattern.
        { PinyinMatchStrategy.Whole, "abc", true },
        { PinyinMatchStrategy.Whole, "a", false },
        { PinyinMatchStrategy.Whole, "ab", false },
        { PinyinMatchStrategy.Whole, "bc", false },
        { PinyinMatchStrategy.Whole, "abcd", false },
        { PinyinMatchStrategy.Whole, "", false },
    };

    /// <summary>
    /// Each relation, once configured, is applied exactly.
    /// </summary>
    /// <param name="strategy">The relation.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="expected">Whether the reduction <c>abc</c> matches it.</param>
    /// <remarks>
    /// Every row also asserts the result is AVAILABLE, so a relation fault can never be mistaken for the
    /// BLOCKED outcome, and the reduction is asserted so a row that failed to derive <c>abc</c> could not
    /// pass by accident.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Relations))]
    public void EachRelationIsAppliedExactly(
        PinyinMatchStrategy strategy,
        string pattern,
        bool expected)
    {
        PinyinMatchResult result = PinyinMatcherFixture
            .Matcher(strategy)
            .TryMatch(ThreeCharacterText, pattern, Flags);

        Assert.True(result.IsAvailable);
        Assert.Equal(["abc"], result.DerivedInitials);
        Assert.Equal(
            expected ? PinyinMatchOutcome.Match : PinyinMatchOutcome.NoMatch,
            result.Outcome);
    }

    /// <summary>
    /// The three CHARACTERIZED relations - every member of
    /// <see cref="PinyinMatchStrategy"/> except the not-characterized zero.
    /// </summary>
    /// <returns>The relations.</returns>
    /// <remarks>
    /// Derived from the enumeration rather than listed, so a relation added later is exercised by every
    /// theory below automatically instead of being silently skipped. The zero is excluded because it is
    /// BLOCKED by definition and never reaches a comparison; <see cref="PinyinBlockedOutcomeTests"/> owns
    /// it.
    /// </remarks>
    public static TheoryData<PinyinMatchStrategy> CharacterizedRelations()
    {
        TheoryData<PinyinMatchStrategy> data = [];

        foreach (PinyinMatchStrategy strategy in Enum.GetValues<PinyinMatchStrategy>())
        {
            if (strategy != PinyinMatchStrategy.NotCharacterized)
            {
                data.Add(strategy);
            }
        }

        return data;
    }

    /// <summary>
    /// A pattern longer than the reduction never matches, under any relation.
    /// </summary>
    /// <param name="strategy">The relation.</param>
    /// <remarks>
    /// Called out separately from the matrix because it is the one shape where an unguarded index
    /// arithmetic would throw rather than answer, and a filter predicate that threw would abort a whole
    /// retrieval. The answer must be a decided non-match.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CharacterizedRelations))]
    public void APatternLongerThanTheReductionIsADecidedNonMatch(PinyinMatchStrategy strategy)
    {
        PinyinMatchResult result = PinyinMatcherFixture
            .Matcher(strategy)
            .TryMatch(ThreeCharacterText, "abcdefghij", Flags);

        Assert.True(result.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, result.Outcome);
    }

    /// <summary>
    /// Empty text HAS a reduction - the empty string - and the table is never consulted for it.
    /// </summary>
    /// <param name="strategy">The relation.</param>
    /// <remarks>
    /// <para>
    /// A SINGLE EMPTY CANDIDATE, NOT AN EMPTY CANDIDATE SET. The two mean different things: one says the
    /// reduction is the empty string, the other says no reduction exists. Under prefix and substring
    /// every string contains the empty string, so an empty pattern matches; under the whole relation it
    /// matches only an empty reduction - which empty text has. That falls out of the relations rather
    /// than from a special rule, which is why no special rule is asserted.
    /// </para>
    /// <para>
    /// The EMPTY TABLE is used here on purpose, to show that empty text decides even when the table
    /// covers nothing - and, by contrast with the BLOCKED cases, that an empty table and an ABSENT one
    /// are different findings.
    /// </para>
    /// <para>
    /// The legacy never reaches this shape: its call site is guarded by a non-empty test
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L315</c>] and by an ASCII-letter test [<c>:L322</c>]. Both
    /// guards live upstream in the drop-down search model, not in the matcher, so the matcher must still
    /// answer coherently for any other caller.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CharacterizedRelations))]
    public void EmptyTextReducesToTheEmptyStringWithoutConsultingTheTable(
        PinyinMatchStrategy strategy)
    {
        SyntheticPinyinFirstLetterTable emptyTable = new();
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(strategy, emptyTable);

        PinyinMatchResult empty = matcher.TryMatch(string.Empty, string.Empty, Flags);

        Assert.True(empty.IsAvailable);
        Assert.Equal([string.Empty], empty.DerivedInitials);
        Assert.Empty(emptyTable.RequestedCharacters);

        // All three relations agree here, and for three different reasons rather than by coincidence:
        // every string starts with and contains the empty string, and the empty reduction EQUALS the
        // empty pattern.
        Assert.Equal(PinyinMatchOutcome.Match, empty.Outcome);

        // A non-empty pattern cannot match an empty reduction under any relation.
        PinyinMatchResult nonEmptyPattern = matcher.TryMatch(string.Empty, "a", Flags);

        Assert.True(nonEmptyPattern.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.NoMatch, nonEmptyPattern.Outcome);
    }

    /// <summary>
    /// An empty PATTERN against a non-empty reduction follows the relation, with no special case.
    /// </summary>
    [Fact]
    public void AnEmptyPatternFollowsTheRelation()
    {
        Assert.Equal(
            PinyinMatchOutcome.Match,
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix)
                .TryMatch(ThreeCharacterText, string.Empty, Flags)
                .Outcome);
        Assert.Equal(
            PinyinMatchOutcome.Match,
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Substring)
                .TryMatch(ThreeCharacterText, string.Empty, Flags)
                .Outcome);
        Assert.Equal(
            PinyinMatchOutcome.NoMatch,
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Whole)
                .TryMatch(ThreeCharacterText, string.Empty, Flags)
                .Outcome);
    }

    /// <summary>
    /// MIXED HAN AND LATIN INPUT: a Latin character in the text must be in the table too - it is NOT
    /// passed through as itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The temptation is to treat a non-Han character as its own initial, and that would be an
    /// APPROXIMATION of a closed behaviour: what the binary does with a Latin character in the middle of
    /// a Chinese display value is simply unrecorded. So the unit declines - the derivation fails and the
    /// gap is reported - and the moment a recording says what really happens, the table expresses it
    /// directly.
    /// </para>
    /// <para>
    /// This shape is REACHABLE from the legacy fixture rather than hypothetical: the DDDW fixture's
    /// display column carries Chinese values while its data column carries Latin ones
    /// [<c>dw_test_dwsvc_dddw.srd</c>], and mixed values are ordinary in that setting.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALatinCharacterInTheTextIsNotPassedThroughAsItsOwnInitial()
    {
        string mixed = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA, 'z');

        PinyinMatchResult unmapped = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(mixed, "az", Flags);

        Assert.Equal(PinyinMatchOutcome.Unavailable, unmapped.Outcome);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableIncomplete, unmapped.UnavailableReason);
        Assert.NotEqual(PinyinMatchOutcome.NoMatch, unmapped.Outcome);

        // Once the table covers it - because a recording said so - the mixed value reduces and matches.
        SyntheticPinyinFirstLetterTable withLatin = PinyinMatcherFixture.ScrambledTable().With('z', 'z');
        PinyinMatchResult mapped = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix, withLatin)
            .TryMatch(mixed, "az", Flags);

        Assert.Equal(PinyinMatchOutcome.Match, mapped.Outcome);
        Assert.Equal(["az"], mapped.DerivedInitials);
    }

    /// <summary>
    /// A character the table has no reading for fails the WHOLE derivation, and the diagnostic names it.
    /// </summary>
    /// <remarks>
    /// Neither skipped nor substituted. Skipping would shorten the reduction and silently change what a
    /// prefix or whole match means; substituting would invent a reading. Failing produces a reported gap,
    /// and the reported code point is what tells an operator which character to characterize next.
    /// </remarks>
    [Fact]
    public void AnUncoveredCharacterFailsTheDerivationAndIsNamedInTheDiagnostic()
    {
        SyntheticPinyinFirstLetterTable table = PinyinMatcherFixture.ScrambledTable();
        PinyinMatchResult result = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix, table)
            .TryMatch(
                PinyinMatcherFixture.Text(
                    PinyinSyntheticAlphabet.MapsToA,
                    PinyinSyntheticAlphabet.Uncovered),
                "a",
                Flags);

        Assert.Equal(PinyinMatchOutcome.Unavailable, result.Outcome);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableIncomplete, result.UnavailableReason);
        Assert.Empty(result.DerivedInitials);

        // 壬 is U+58EC. The code point is rendered so an operator can act on it.
        Assert.Contains("U+58EC", result.Diagnostic, StringComparison.Ordinal);
        Assert.Contains(
            SyntheticPinyinFirstLetterTable.SyntheticSource,
            result.Diagnostic,
            StringComparison.Ordinal);

        // A table that answers "yes" with no letters violates its own contract; the unit survives it the
        // same way rather than deriving a reduction with a hole in it.
        SyntheticPinyinFirstLetterTable lying = PinyinMatcherFixture
            .ScrambledTable()
            .WithNoReadings(PinyinSyntheticAlphabet.Uncovered);

        Assert.Equal(
            PinyinMatchUnavailableReason.LookupTableIncomplete,
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix, lying)
                .TryMatch(PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.Uncovered), "a", Flags)
                .UnavailableReason);
    }

    /// <summary>
    /// A POLYPHONIC character contributes several candidate reductions, and a match against ANY of them
    /// is a match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one-to-many shape of the table seam is evidence-driven: <c>logfile.md:L376</c> records that the
    /// native function supports 多音字 - polyphonic characters. The changelog is not a specification and
    /// is stale (risk R8), so it fixes only the SHAPE of the contract and never its content: WHICH
    /// readings a character has stays a characterization output. But the shape has to be right before any
    /// recording can be loaded through it, and a one-to-one seam would be structurally incapable of
    /// carrying what the oracle does.
    /// </para>
    /// <para>
    /// The candidate ORDER is asserted as well, because a recording compares derived reductions and an
    /// unstable order would make that comparison unreliable.
    /// </para>
    /// </remarks>
    [Fact]
    public void APolyphonicCharacterProducesOneCandidatePerReading()
    {
        SyntheticPinyinFirstLetterTable table = new SyntheticPinyinFirstLetterTable()
            .With(PinyinSyntheticAlphabet.MapsToA, 'a', 'x')
            .With(PinyinSyntheticAlphabet.MapsToB, 'b');

        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Whole,
            table);
        string text = PinyinMatcherFixture.Text(
            PinyinSyntheticAlphabet.MapsToA,
            PinyinSyntheticAlphabet.MapsToB);

        PinyinMatchResult first = matcher.TryMatch(text, "ab", Flags);

        Assert.Equal(PinyinMatchOutcome.Match, first.Outcome);
        Assert.Equal(["ab", "xb"], first.DerivedInitials);

        // The SECOND reading matches too - the readings are alternatives, not conjuncts.
        Assert.Equal(PinyinMatchOutcome.Match, matcher.TryMatch(text, "xb", Flags).Outcome);

        // And a reduction no reading produces still does not match.
        Assert.Equal(PinyinMatchOutcome.NoMatch, matcher.TryMatch(text, "yb", Flags).Outcome);
    }

    /// <summary>
    /// Iteration is by <see cref="Rune"/>: a supplementary-plane character is offered to the table ONCE,
    /// not as two surrogate halves.
    /// </summary>
    /// <remarks>
    /// A table keyed by <see cref="char"/> could never answer for a character outside the basic
    /// multilingual plane, because each half of a surrogate pair is meaningless on its own. Note honestly
    /// that whether the closed binary itself works per code point or per UTF-16 code unit is ANOTHER
    /// unverified detail; the difference is observable only for supplementary-plane input, and the seam is
    /// shaped so a recording can settle it.
    /// </remarks>
    [Fact]
    public void SupplementaryPlaneCharactersAreOfferedToTheTableAsOneRune()
    {
        SyntheticPinyinFirstLetterTable table = PinyinMatcherFixture.ScrambledTable();
        string text = PinyinSyntheticAlphabet.SupplementaryPlane.ToString()
            + PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        // Two runes, four UTF-16 code units - so the counts below are decisive rather than incidental.
        Assert.Equal(3, text.Length);

        PinyinMatchResult result = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Whole, table)
            .TryMatch(
                text,
                PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.SupplementaryPlaneLetter, 'a'),
                Flags);

        Assert.Equal(PinyinMatchOutcome.Match, result.Outcome);
        Assert.Equal(2, table.RequestedCharacters.Count);
        Assert.Equal(PinyinSyntheticAlphabet.SupplementaryPlane, table.RequestedCharacters[0]);
        Assert.Equal(new Rune(PinyinSyntheticAlphabet.MapsToA), table.RequestedCharacters[1]);

        // No lone surrogate was ever looked up.
        Assert.All(table.RequestedCharacters, rune => Assert.True(Rune.IsValid(rune.Value)));
    }

    /// <summary>
    /// The polyphonic expansion has a CEILING, and crossing it is reported rather than truncated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Readings multiply: text of <c>n</c> characters each with <c>k</c> readings has <c>k</c> to the
    /// power <c>n</c> candidate reductions. Without a bound, a long run of multi-reading characters would
    /// exhaust memory inside a per-row filter predicate. The bound is 4096 combinations.
    /// </para>
    /// <para>
    /// CROSSING IT DECLINES RATHER THAN TRUNCATING, which is the same rule as everywhere else in this
    /// unit: truncating the candidate set would silently drop readings and turn a match into a non-match.
    /// Both sides of the boundary are asserted - twelve two-reading characters expand to exactly 4096 and
    /// decide, thirteen report the gap - because a ceiling asserted from one side only does not pin where
    /// it is.
    /// </para>
    /// </remarks>
    [Fact]
    public void CrossingThePolyphonicCeilingIsReportedRatherThanTruncated()
    {
        SyntheticPinyinFirstLetterTable table = new SyntheticPinyinFirstLetterTable()
            .With(PinyinSyntheticAlphabet.MapsToA, 'a', 'b');
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            table);

        string atTheCeiling = new(PinyinSyntheticAlphabet.MapsToA, 12);
        PinyinMatchResult decided = matcher.TryMatch(atTheCeiling, new string('a', 12), Flags);

        Assert.True(decided.IsAvailable);
        Assert.Equal(PinyinMatchOutcome.Match, decided.Outcome);
        Assert.Equal(4096, decided.DerivedInitials.Length);

        string overTheCeiling = new(PinyinSyntheticAlphabet.MapsToA, 13);
        PinyinMatchResult refused = matcher.TryMatch(overTheCeiling, new string('a', 13), Flags);

        Assert.Equal(PinyinMatchOutcome.Unavailable, refused.Outcome);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableIncomplete, refused.UnavailableReason);
        Assert.Empty(refused.DerivedInitials);
        Assert.Contains("4096", refused.Diagnostic, StringComparison.Ordinal);
    }

    /// <summary>
    /// A decided result carries its inputs, its reduction and a complete log-ready sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The diagnostic is asserted verbatim, and doing so PINS THE ONE-BASED PLACEHOLDER CONVENTION of the
    /// ported formatter. Its <c>{1}</c> is the FIRST argument, unlike .NET composite formatting, and an
    /// index with no argument renders as the empty string instead of throwing - so a <c>{0}</c> written
    /// out of .NET habit would shift every argument by one position and silently drop the last. Only an
    /// exact assertion catches that.
    /// </para>
    /// <para>
    /// Several reductions are joined with a vertical bar, which is what makes a polyphonic derivation
    /// legible in a log.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADecidedResultCarriesItsInputsAndALogReadySentence()
    {
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        PinyinMatchResult matched = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(text, "a", Flags);

        Assert.Equal(text, matched.Text);
        Assert.Equal("a", matched.Pattern);
        Assert.Equal(Flags, matched.Flags);
        Assert.Equal(PinyinMatchUnavailableReason.Unspecified, matched.UnavailableReason);
        Assert.Equal(
            "PinyinFirstLetterLike(甲,'a',7) = true. Derived initials: a.",
            matched.Diagnostic);

        PinyinMatchResult notMatched = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(text, "b", Flags);

        Assert.Equal(
            "PinyinFirstLetterLike(甲,'b',7) = false. Derived initials: a.",
            notMatched.Diagnostic);

        SyntheticPinyinFirstLetterTable polyphonic = new SyntheticPinyinFirstLetterTable()
            .With(PinyinSyntheticAlphabet.MapsToA, 'a', 'x');

        Assert.Equal(
            "PinyinFirstLetterLike(甲,'a',7) = true. Derived initials: a|x.",
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix, polyphonic)
                .TryMatch(text, "a", Flags)
                .Diagnostic);
    }

    /// <summary>
    /// The parity overloads return the decided answer, so the <c>boolean</c> signature works normally
    /// whenever the closed inputs are supplied.
    /// </summary>
    /// <remarks>
    /// The BLOCKED behaviour of these two overloads is asserted in
    /// <see cref="PinyinBlockedOutcomeTests"/>; this case is the other half, showing that throwing is
    /// reserved for unavailability and is not how they normally behave.
    /// </remarks>
    [Fact]
    public void TheParityOverloadsReturnTheDecidedAnswerWhenEverythingIsSupplied()
    {
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: PinyinMatcherFixture.CallSiteFlags);
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        Assert.True(matcher.PinyinFirstLetterLike(text, "a", Flags));
        Assert.False(matcher.PinyinFirstLetterLike(text, "b", Flags));
        Assert.True(matcher.PinyinFirstLetterLike(text, "a"));
        Assert.False(matcher.PinyinFirstLetterLike(text, "b"));
    }
}

// =====================================================================================================
//  5. THE BLOCKED PATH - THE DELIVERABLE OF RISK R1
// =====================================================================================================

/// <summary>
/// Asserts that an unconfigured closed input produces a DEFINED, REPORTABLE outcome that no caller can
/// mistake for a non-match.
/// </summary>
/// <remarks>
/// <para>
/// THIS CLASS IS THE POINT OF THE WHOLE FILE. docs/PARITY.md ("R1") requires the pinyin filter be
/// reported as BLOCKED rather than approximated, and "reported" has to mean something a caller can act
/// on. The shipped configuration is deliberately BLOCKED, so these are the cases that describe what this
/// refactor actually ships.
/// </para>
/// <para>
/// NOT ONE CASE BELOW ASSERTS THAT A BLOCKED COMPARISON "IS FALSE". That assertion would encode the exact
/// conflation the mandate forbids - "cannot tell" collapsing into "does not match" - so unavailability is
/// always asserted through <see cref="PinyinMatchResult.Outcome"/> and
/// <see cref="PinyinMatchResult.IsAvailable"/>, and every case that could be read the wrong way also
/// asserts <c>Outcome != NoMatch</c> explicitly.
/// </para>
/// </remarks>
public sealed class PinyinBlockedOutcomeTests
{
    /// <summary>
    /// Provokes each unavailable reason with the smallest configuration that produces it.
    /// </summary>
    /// <param name="reason">The reason to provoke.</param>
    /// <returns>The unavailable result.</returns>
    /// <remarks>
    /// One factory rather than four fixtures, so the matrix rows differ only in the reason they name and
    /// the shape that provokes it is visible in one place.
    /// </remarks>
    private static PinyinMatchResult Provoke(PinyinMatchUnavailableReason reason)
    {
        string covered = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        uint flags = PinyinMatcherFixture.CallSiteFlags;

        return reason switch
        {
            // No table at all. The SHIPPED state.
            PinyinMatchUnavailableReason.LookupTableNotConfigured =>
                new PinyinFirstLetterMatcher(new PinyinMatchConfiguration
                {
                    Strategy = PinyinMatchStrategy.Prefix,
                    CharacterizedDefaultFlags = flags,
                }).TryMatch(covered, "a", flags),

            // A table, but the relation is still unknown.
            PinyinMatchUnavailableReason.MatchStrategyNotCharacterized =>
                new PinyinFirstLetterMatcher(new PinyinMatchConfiguration
                {
                    Table = PinyinMatcherFixture.ScrambledTable(),
                    CharacterizedDefaultFlags = flags,
                }).TryMatch(covered, "a", flags),

            // The two-argument form with no characterized default.
            PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized =>
                PinyinMatcherFixture.Matcher(PinyinMatchStrategy.Prefix).TryMatch(covered, "a"),

            // A table that does not cover a character actually present in the text.
            PinyinMatchUnavailableReason.LookupTableIncomplete =>
                PinyinMatcherFixture
                    .Matcher(PinyinMatchStrategy.Prefix)
                    .TryMatch(
                        PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.Uncovered),
                        "a",
                        flags),

            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a blocking reason."),
        };
    }

    /// <summary>
    /// The four closed inputs, each with the phrase its diagnostic must carry.
    /// </summary>
    /// <returns>The reason and a distinctive fragment of its explanation.</returns>
    /// <remarks>
    /// The fragments are asserted because a BLOCKED report is only useful if it names WHICH
    /// characterization work would unblock it. "Unavailable" on its own is not actionable; "no pinyin
    /// lookup table is configured" is.
    /// </remarks>
    public static TheoryData<PinyinMatchUnavailableReason, string> BlockingReasons() => new()
    {
        {
            PinyinMatchUnavailableReason.LookupTableNotConfigured,
            "no pinyin lookup table is configured"
        },
        {
            PinyinMatchUnavailableReason.MatchStrategyNotCharacterized,
            "the matching relation has not been characterized"
        },
        {
            PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized,
            "the two-argument overload's effective flag set has not been characterized"
        },
        {
            PinyinMatchUnavailableReason.LookupTableIncomplete,
            "has no reading for a character present in the text"
        },
    };

    /// <summary>
    /// Every closed input is reachable, names itself, and yields an Unavailable outcome that is NOT a
    /// non-match.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <param name="expectedFragment">A distinctive fragment its diagnostic must carry.</param>
    [Theory]
    [MemberData(nameof(BlockingReasons))]
    public void EveryClosedInputProducesItsOwnNamedUnavailableOutcome(
        PinyinMatchUnavailableReason reason,
        string expectedFragment)
    {
        PinyinMatchResult result = Provoke(reason);

        Assert.Equal(PinyinMatchOutcome.Unavailable, result.Outcome);
        Assert.Equal(reason, result.UnavailableReason);

        // The three assertions that keep BLOCKED from degrading into a non-match.
        Assert.NotEqual(PinyinMatchOutcome.NoMatch, result.Outcome);
        Assert.NotEqual(PinyinMatchOutcome.Match, result.Outcome);
        Assert.False(result.IsAvailable);

        // A partial reduction is an approximation, so none is published.
        Assert.Empty(result.DerivedInitials);

        Assert.Contains(expectedFragment, result.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("UNAVAILABLE", result.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("docs/PARITY.md risk R1", result.Diagnostic, StringComparison.Ordinal);
        Assert.Contains(
            PinyinFirstLetterMatcher.ExpressionFunctionName,
            result.Diagnostic,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// BLOCKED AND "NO MATCH" ARE DISTINGUISHABLE AT THE TYPE LEVEL, which is the property the whole
    /// design exists to provide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are compared side by side over the SAME text and pattern, so nothing but the configuration
    /// differs. A caller can tell them apart three ways - the outcome, the availability flag and the
    /// reason - and the diagnostics differ in wording as well.
    /// </para>
    /// <para>
    /// Note what is deliberately NOT asserted: that the blocked result "is false". A boolean view of the
    /// blocked result would be indistinguishable from the real negative, and a caller that took it would
    /// filter rows away while reporting success. That is the regression risk R1 describes as looking like
    /// correct behaviour.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABlockedComparisonIsDistinguishableFromARealNonMatch()
    {
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        uint flags = PinyinMatcherFixture.CallSiteFlags;

        PinyinMatchResult realNegative = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(text, "b", flags);
        PinyinMatchResult blocked = PinyinFirstLetterMatcher.Blocked.TryMatch(text, "b", flags);

        // The real negative: the comparison RAN and came out negative.
        Assert.Equal(PinyinMatchOutcome.NoMatch, realNegative.Outcome);
        Assert.True(realNegative.IsAvailable);
        Assert.Equal(PinyinMatchUnavailableReason.Unspecified, realNegative.UnavailableReason);
        Assert.Equal(["a"], realNegative.DerivedInitials);

        // The blocked comparison: it never ran.
        Assert.Equal(PinyinMatchOutcome.Unavailable, blocked.Outcome);
        Assert.False(blocked.IsAvailable);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableNotConfigured, blocked.UnavailableReason);
        Assert.Empty(blocked.DerivedInitials);

        // And they are not the same value, on identical inputs.
        Assert.NotEqual(realNegative.Outcome, blocked.Outcome);
        Assert.NotEqual(realNegative.UnavailableReason, blocked.UnavailableReason);
        Assert.NotEqual(realNegative.Diagnostic, blocked.Diagnostic);
    }

    /// <summary>
    /// The shipped matcher is BLOCKED, and every part of that state is deliberate.
    /// </summary>
    /// <remarks>
    /// This is the configuration the service registers today, because the behavioural oracle has not been
    /// exercised in this environment - docs/PARITY.md records that. It is a first-class deliverable, not a
    /// failure to configure: the fuzzy relation IS present, because the three pairs are genuinely
    /// documented, while the table, the relation and the default flag set are absent because no evidence
    /// for them exists anywhere in the repository.
    /// </remarks>
    [Fact]
    public void TheShippedConfigurationIsBlockedAndSaysExactlyWhy()
    {
        Assert.Same(PinyinFirstLetterMatcher.Blocked, PinyinFirstLetterMatcher.Blocked);
        Assert.Same(PinyinMatchConfiguration.Blocked, PinyinFirstLetterMatcher.Blocked.Configuration);

        PinyinMatchConfiguration blocked = PinyinMatchConfiguration.Blocked;

        Assert.Null(blocked.Table);
        Assert.Equal(PinyinMatchStrategy.NotCharacterized, blocked.Strategy);
        Assert.Null(blocked.CharacterizedDefaultFlags);

        // Present, because the pairs are documented. Absent evidence is what blocks, not caution.
        Assert.Same(PinyinFuzzySoundEquivalence.Documented, blocked.FuzzySound);

        Assert.False(PinyinFirstLetterMatcher.Blocked.CanMatch);
        Assert.False(PinyinFirstLetterMatcher.Blocked.IsDefaultFlagSetCharacterized);

        // CanMatch reports the two seams a comparison needs, and reports them independently of the
        // two-argument overload's separate requirement.
        Assert.False(PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.NotCharacterized)
            .CanMatch);
        Assert.True(PinyinMatcherFixture.Matcher(PinyinMatchStrategy.Prefix).CanMatch);
        Assert.True(new PinyinFirstLetterMatcher(new PinyinMatchConfiguration
        {
            Table = PinyinMatcherFixture.ScrambledTable(),
            Strategy = PinyinMatchStrategy.Substring,
            CharacterizedDefaultFlags = PinyinMatcherFixture.CallSiteFlags,
        }).IsDefaultFlagSetCharacterized);
    }

    /// <summary>
    /// An EMPTY table and an ABSENT table are different findings, and the unit reports them differently.
    /// </summary>
    /// <remarks>
    /// An empty table says "the recording covered nothing"; a null configuration says "there is no
    /// recording". Normalising one into the other would erase a distinction an operator needs, so the
    /// first reports <see cref="PinyinMatchUnavailableReason.LookupTableIncomplete"/> per character and
    /// the second reports <see cref="PinyinMatchUnavailableReason.LookupTableNotConfigured"/> up front.
    /// </remarks>
    [Fact]
    public void AnEmptyTableAndAnAbsentTableReportDifferently()
    {
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        uint flags = PinyinMatcherFixture.CallSiteFlags;

        PinyinMatchResult absent = PinyinFirstLetterMatcher.Blocked.TryMatch(text, "a", flags);
        PinyinMatchResult empty = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix, new SyntheticPinyinFirstLetterTable())
            .TryMatch(text, "a", flags);

        Assert.Equal(PinyinMatchUnavailableReason.LookupTableNotConfigured, absent.UnavailableReason);
        Assert.Equal(PinyinMatchUnavailableReason.LookupTableIncomplete, empty.UnavailableReason);
        Assert.Equal(PinyinMatchOutcome.Unavailable, absent.Outcome);
        Assert.Equal(PinyinMatchOutcome.Unavailable, empty.Outcome);
    }

    /// <summary>
    /// The seams are consulted in a fixed order, so the reported reason is always the MOST SPECIFIC one
    /// available.
    /// </summary>
    /// <remarks>
    /// Precedence is observable and therefore contract. The two-argument form reports its own gap before
    /// any other, so its diagnostic cannot be mistaken for a missing table; the table is reported before
    /// the relation, so a deployment with neither is told to load a table first; and an uncovered
    /// character is reported only once a table and a relation are both present.
    /// </remarks>
    [Fact]
    public void TheSeamsAreReportedMostSpecificFirst()
    {
        string covered = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        string uncovered = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.Uncovered);
        uint flags = PinyinMatcherFixture.CallSiteFlags;

        // Nothing configured at all, two-argument form: the arity-specific gap wins.
        Assert.Equal(
            PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized,
            PinyinFirstLetterMatcher.Blocked.TryMatch(covered, "a").UnavailableReason);

        // Nothing configured, three-argument form: the table wins over the relation.
        Assert.Equal(
            PinyinMatchUnavailableReason.LookupTableNotConfigured,
            PinyinFirstLetterMatcher.Blocked.TryMatch(covered, "a", flags).UnavailableReason);

        // Table present, relation absent: the relation is reported, and it names the table's source so
        // an operator can see the table was found.
        PinyinMatchResult relationMissing = new PinyinFirstLetterMatcher(new PinyinMatchConfiguration
        {
            Table = PinyinMatcherFixture.ScrambledTable(),
        }).TryMatch(uncovered, "a", flags);

        Assert.Equal(
            PinyinMatchUnavailableReason.MatchStrategyNotCharacterized,
            relationMissing.UnavailableReason);
        Assert.Contains(
            SyntheticPinyinFirstLetterTable.SyntheticSource,
            relationMissing.Diagnostic,
            StringComparison.Ordinal);

        // Table and relation present: only now is the uncovered character the finding.
        Assert.Equal(
            PinyinMatchUnavailableReason.LookupTableIncomplete,
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix)
                .TryMatch(uncovered, "a", flags)
                .UnavailableReason);
    }

    /// <summary>
    /// The <c>bool</c>-returning parity overloads throw the DEFINED exception rather than returning a
    /// value they cannot honestly produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SIGNATURE CONSTRAINT, NOT A DESIGN PREFERENCE. The oracle's prototypes return <c>boolean</c>
    /// [<c>pinyinfirstletterlike.srf:L7-L8</c>] and this port reproduces that exactly, so those two
    /// methods have nowhere to put a third state. The alternatives were to return the silent
    /// <see langword="false"/> the mandate forbids, or to throw. Throwing is the only one that keeps the
    /// answer honest at that signature.
    /// </para>
    /// <para>
    /// It is therefore EXPECTED, DOCUMENTED AND CATCHABLE, and it carries the whole result so a handler
    /// logs the reason and the inputs without reconstructing them. The message and the diagnostic are the
    /// same string by construction, so the exception text and the log text cannot drift apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheParityOverloadsThrowTheDefinedExceptionRatherThanReturningFalse()
    {
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);
        uint flags = PinyinMatcherFixture.CallSiteFlags;

        PinyinMatchUnavailableException threeArgument = Assert.Throws<PinyinMatchUnavailableException>(
            () => PinyinFirstLetterMatcher.Blocked.PinyinFirstLetterLike(text, "a", flags));

        Assert.Equal(PinyinMatchUnavailableReason.LookupTableNotConfigured, threeArgument.Reason);
        Assert.Equal(PinyinMatchOutcome.Unavailable, threeArgument.Result.Outcome);
        Assert.Equal(threeArgument.Result.Diagnostic, threeArgument.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(threeArgument);

        PinyinMatchUnavailableException twoArgument = Assert.Throws<PinyinMatchUnavailableException>(
            () => PinyinFirstLetterMatcher.Blocked.PinyinFirstLetterLike(text, "a"));

        Assert.Equal(PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized, twoArgument.Reason);

        // A decided comparison does NOT throw, so throwing is reserved for unavailability alone.
        PinyinFirstLetterMatcher characterized = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: flags);

        Assert.True(characterized.PinyinFirstLetterLike(text, "a", flags));
    }

    /// <summary>
    /// The exception's standard constructors exist for the conventional shape and report no reason.
    /// </summary>
    /// <remarks>
    /// Only the result-carrying constructor is used by the unit; the other three are present so the type
    /// has the shape a caller expects of an exception. Their <see cref="PinyinMatchResult.Outcome"/> is
    /// the default-initialised guard value, which is exactly what "no result was supplied" should look
    /// like.
    /// </remarks>
    [Fact]
    public void TheExceptionsStandardConstructorsCarryNoReason()
    {
        PinyinMatchUnavailableException bare = new();
        PinyinMatchUnavailableException withMessage = new("blocked");
        PinyinMatchUnavailableException withInner = new("blocked", new InvalidOperationException("why"));

        Assert.Equal(PinyinMatchUnavailableReason.Unspecified, bare.Reason);
        Assert.Equal(PinyinMatchOutcome.Unspecified, bare.Result.Outcome);
        Assert.Equal("blocked", withMessage.Message);
        Assert.Equal(PinyinMatchUnavailableReason.Unspecified, withMessage.Reason);
        Assert.Equal("blocked", withInner.Message);
        Assert.NotNull(withInner.InnerException);
    }

    /// <summary>
    /// The EXPRESSION path never throws for unavailability - it reports a structured error instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the requirement that shapes the signature. An exception escaping a filter predicate would
    /// abort a whole retrieval rather than reporting a gap in one function, so unavailability becomes an
    /// <c>ExpressionParseError</c> the caller can surface on the published contract.
    /// </para>
    /// <para>
    /// AND THE BOOLEAN RETURN REPORTS AVAILABILITY, NOT THE PREDICATE. The predicate's value lives on the
    /// result, where it cannot be confused with it - which is why a blocked invocation returns
    /// <see langword="false"/> from <c>TryInvokeFromExpression</c> while the RESULT is Unavailable rather
    /// than a non-match.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExpressionPathReportsAStructuredErrorInsteadOfThrowing()
    {
        object?[] arguments =
        [
            PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
            "a",
            PinyinMatcherFixture.CallSiteFlags,
        ];

        bool available = PinyinFirstLetterMatcher.Blocked.TryInvokeFromExpression(
            arguments,
            out PinyinMatchResult result,
            out ExpressionParseError? error);

        Assert.False(available);
        Assert.Equal(PinyinMatchOutcome.Unavailable, result.Outcome);
        Assert.NotEqual(PinyinMatchOutcome.NoMatch, result.Outcome);
        Assert.NotNull(error);

        // A refactor-introduced condition, so it carries NO legacy site: the native function always has
        // its table in process and has no unavailable state, so no legacy line can be cited for it.
        Assert.Equal(ExpressionErrorSite.Unspecified, error.Site);
        Assert.Equal(ExpressionErrorFamily.PlainMessage, error.Family);

        // The general expression-evaluation failure. Deliberately NOT the category reserved for the
        // foreign-variable narrowing, which is an unrelated gap.
        Assert.Equal(ExpressionErrorCategory.Expression, error.Category);
        Assert.NotEqual(ExpressionErrorCategory.ForeignReferenceBlocked, error.Category);

        // The severity every one of the 28 legacy dialog sites carries, kept for consistency with them.
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);
        Assert.Equal(result.Diagnostic, error.Text);

        // Not localized, matching the preserved non-localization of the 28 legacy sites.
        Assert.False(error.Localized);
        Assert.Equal(0L, error.LocalizationCategory);

        // The defined code: an operation that exists but has no available implementation.
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, error.ReturnCode);

        // A plain-message error carries no expression, position, convention or marker.
        Assert.Null(error.Expression);
        Assert.Null(error.CaretPosition);
        Assert.Equal(CaretPositionConvention.Unspecified, error.CaretConvention);
        Assert.Null(error.RenderedMarker);
        Assert.Equal("{1}", error.FormatTemplate);
        Assert.Equal([result.Diagnostic], error.FormatArguments);
    }

    /// <summary>
    /// A malformed expression invocation is reported the same way rather than thrown.
    /// </summary>
    /// <returns>The argument list to pass.</returns>
    /// <remarks>
    /// A filter expression is DATA assembled by string concatenation
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L319-L334</c>], so a caller may legitimately have got it wrong.
    /// Wrong arity and an unusable flag argument are therefore reported, not thrown - the same discipline
    /// as unavailability, for the same reason.
    /// </remarks>
    public static TheoryData<object?[]> MalformedInvocations() => new()
    {
        // Arity: the oracle declares two and three only.
        new object?[] { },
        new object?[] { "only-one" },
        new object?[] { "a", "b", 7, "too-many" },
        // A third argument that is not a usable 32-bit mask.
        new object?[] { "a", "b", "not-a-number" },
        new object?[] { "a", "b", -1 },
        new object?[] { "a", "b", (long)uint.MaxValue + 1L },
        new object?[] { "a", "b", 1.5d },
        new object?[] { "a", "b", null },
        new object?[] { "a", "b", new object() },
    };

    /// <summary>
    /// Each malformed invocation returns false with an error, and never throws.
    /// </summary>
    /// <param name="arguments">The argument list.</param>
    [Theory]
    [MemberData(nameof(MalformedInvocations))]
    public void AMalformedExpressionInvocationIsReportedRatherThanThrown(object?[] arguments)
    {
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: PinyinMatcherFixture.CallSiteFlags);

        bool available = matcher.TryInvokeFromExpression(
            arguments,
            out PinyinMatchResult result,
            out ExpressionParseError? error);

        Assert.False(available);
        Assert.Equal(PinyinMatchOutcome.Unavailable, result.Outcome);
        Assert.NotNull(error);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, error.ReturnCode);
        Assert.NotEmpty(result.Diagnostic);
    }

    /// <summary>
    /// A well-formed expression invocation over a characterized matcher reports availability and carries
    /// the predicate on the result.
    /// </summary>
    /// <remarks>
    /// The counterpart of the malformed matrix, and the case that shows the flag argument is accepted from
    /// whichever integral form a DataWindow expression's numeric literal happens to arrive as - the
    /// evaluator has no fixed managed type for one.
    /// </remarks>
    [Fact]
    public void AWellFormedExpressionInvocationReportsAvailabilityAndCarriesThePredicate()
    {
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            defaultFlags: PinyinMatcherFixture.CallSiteFlags);
        string text = PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA);

        object?[] flagForms =
        [
            PinyinMatcherFixture.CallSiteFlags,
            (int)PinyinMatcherFixture.CallSiteFlags,
            (long)PinyinMatcherFixture.CallSiteFlags,
            (decimal)PinyinMatcherFixture.CallSiteFlags,
            (double)PinyinMatcherFixture.CallSiteFlags,
            PinyinMatcherFixture.CallSiteFlags.ToString(CultureInfo.InvariantCulture),
        ];

        foreach (object? flags in flagForms)
        {
            Assert.True(matcher.TryInvokeFromExpression(
                [text, "a", flags],
                out PinyinMatchResult result,
                out ExpressionParseError? error));

            Assert.Null(error);
            Assert.Equal(PinyinMatchOutcome.Match, result.Outcome);
            Assert.True(result.IsMatch);
            Assert.Equal(PinyinMatcherFixture.CallSiteFlags, result.Flags);
        }

        // The two-argument form goes through the characterized default.
        Assert.True(matcher.TryInvokeFromExpression(
            [text, "a"],
            out PinyinMatchResult twoArgument,
            out ExpressionParseError? twoArgumentError));
        Assert.Null(twoArgumentError);
        Assert.Equal(PinyinMatchOutcome.Match, twoArgument.Outcome);

        // A null column value becomes the empty string, which is how a DataWindow expression treats a
        // null in a string context - and it reduces without consulting the table.
        Assert.True(matcher.TryInvokeFromExpression(
            [null, string.Empty, PinyinMatcherFixture.CallSiteFlags],
            out PinyinMatchResult nullColumn,
            out _));
        Assert.Equal(PinyinMatchOutcome.Match, nullColumn.Outcome);

        // A null argument LIST is a programming fault rather than bad expression data, so it throws.
        Assert.Throws<ArgumentNullException>(
            () => matcher.TryInvokeFromExpression(null!, out _, out _));
    }

    /// <summary>
    /// A DECIDED result is a VALUE and must never be reported as a fault.
    /// </summary>
    /// <remarks>
    /// The guard exists because the error projection is public: a caller that passed a match into it would
    /// be turning a legitimate answer into an error, which is the mirror image of the silent-false defect
    /// this file exists to prevent.
    /// </remarks>
    [Fact]
    public void OnlyAnUnavailableResultCanBecomeAStructuredError()
    {
        PinyinMatchResult decided = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(
                PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
                "a",
                PinyinMatcherFixture.CallSiteFlags);

        Assert.Equal(PinyinMatchOutcome.Match, decided.Outcome);
        Assert.Throws<ArgumentException>(
            () => PinyinFirstLetterMatcher.CreateUnavailableExpressionError(decided));

        PinyinMatchResult negative = PinyinMatcherFixture
            .Matcher(PinyinMatchStrategy.Prefix)
            .TryMatch(
                PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
                "b",
                PinyinMatcherFixture.CallSiteFlags);

        Assert.Throws<ArgumentException>(
            () => PinyinFirstLetterMatcher.CreateUnavailableExpressionError(negative));

        // The unavailable result projects cleanly.
        ExpressionParseError error = PinyinFirstLetterMatcher.CreateUnavailableExpressionError(
            Provoke(PinyinMatchUnavailableReason.LookupTableNotConfigured));

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, error.ReturnCode);
    }

    /// <summary>
    /// Every outcome carries a non-empty diagnostic, and the guard value is never produced.
    /// </summary>
    /// <remarks>
    /// <see cref="PinyinMatchOutcome.Unspecified"/> exists to make a default-initialised result
    /// recognisable; no code path may produce it, because a caller reading it would have no idea what
    /// happened. Asserted over every shape this suite can produce.
    /// </remarks>
    [Fact]
    public void EveryProducedOutcomeIsOneOfTheThreeRealStatesAndCarriesADiagnostic()
    {
        List<PinyinMatchResult> results =
        [
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix)
                .TryMatch(
                    PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
                    "a",
                    PinyinMatcherFixture.CallSiteFlags),
            PinyinMatcherFixture
                .Matcher(PinyinMatchStrategy.Prefix)
                .TryMatch(
                    PinyinMatcherFixture.Text(PinyinSyntheticAlphabet.MapsToA),
                    "b",
                    PinyinMatcherFixture.CallSiteFlags),
            Provoke(PinyinMatchUnavailableReason.LookupTableNotConfigured),
            Provoke(PinyinMatchUnavailableReason.MatchStrategyNotCharacterized),
            Provoke(PinyinMatchUnavailableReason.DefaultFlagsNotCharacterized),
            Provoke(PinyinMatchUnavailableReason.LookupTableIncomplete),
        ];

        Assert.All(results, result =>
        {
            Assert.NotEqual(PinyinMatchOutcome.Unspecified, result.Outcome);
            Assert.NotEmpty(result.Diagnostic);
            Assert.Contains(
                PinyinFirstLetterMatcher.ExpressionFunctionName,
                result.Diagnostic,
                StringComparison.Ordinal);
        });

        // The guard value is what a default-initialised result reports, and only that.
        Assert.Equal(PinyinMatchOutcome.Unspecified, default(PinyinMatchResult).Outcome);
        Assert.Equal(
            PinyinMatchUnavailableReason.Unspecified,
            default(PinyinMatchResult).UnavailableReason);
    }
}

// =====================================================================================================
//  6. THE ORACLE CHARACTERIZATION HOOK - SKIPPED TODAY, SELF-ACTIVATING TOMORROW
// =====================================================================================================

/// <summary>
/// Which closed input a characterization row is responsible for unblocking.
/// </summary>
/// <remarks>
/// One member per closed input of risk R1. They are carried in the theory's member data so that when a
/// recording arrives the matrix reports, per row, exactly which piece of characterization is still
/// missing - instead of one opaque failure covering all four.
/// </remarks>
public enum PinyinClosedInput
{
    /// <summary>The character-to-initial lookup table. Closed input (a).</summary>
    LookupTable = 1,

    /// <summary>Prefix, substring or whole - the meaning of "Like". Closed input (b).</summary>
    MatchingRelation = 2,

    /// <summary>The two-argument overload's effective flag set. Closed input (c).</summary>
    DefaultFlagSet = 3,

    /// <summary>The fuzzy-sound set's exhaustiveness and closure. Closed input (d).</summary>
    FuzzySoundSet = 4,
}

/// <summary>
/// The deliberately-skipped characterization matrix that ACTIVATES ITSELF the moment a legacy oracle
/// recording appears.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ALWAYS-ACTIVE TRACKING MATRIX RATHER THAN A SKIPPED ONE. Skipping this matrix until a
/// legacy recording appears means that on the shipped state of this refactor - no recording - its
/// four rows execute nothing at all and protect nothing at all. A skip that never fires is a test that
/// does not exist. The rows therefore assert the EQUIVALENCE between two observable facts rather than one side of
/// it: for each closed input, <b>the input is characterized exactly when a paired oracle recording exists
/// for the workflow</b>. That single form is correct in both worlds and executable in both, and it is what
/// makes each direction a real guard:
/// </para>
/// <para>
/// * TODAY, with no recording, it asserts that the input is NOT characterized and that the shipped
/// composition still hands the expression evaluator <c>PinyinFirstLetterMatcher.Blocked</c>. That is the
/// ANTI-APPROXIMATION guard the parity mandate asks for: anyone who quietly supplies a table, a strategy, a
/// flag default or a fuzzy closure from a third-party source or from a guess - rather than from the oracle -
/// fails these rows immediately (AAP 0.6.5, risk R1).
/// </para>
/// <para>
/// * THE DAY A RECORDING LANDS, the same rows demand that the recorded data actually reached the
/// configuration, and FAIL until it has. Nobody has to remember it, and nobody can quietly ship a recording
/// without wiring it in.
/// </para>
/// <para>
/// * A HALF-CAPTURED PAIR FAILS RATHER THAN GOING UNNOTICED. Activation used to key on the legacy half
/// alone, so a target-side recording with no legacy counterpart left the matrix asleep. The pair rule is
/// asserted whenever EITHER half exists, which is the only reading of docs/PARITY.md 4.1 that catches the
/// unpaired case.
/// </para>
/// <para>
/// THE PAIRED-CAPTURE MODEL IT HOOKS INTO. docs/PARITY.md §4.1 keys recordings by workflow identifier -
/// <c>characterization/recordings/legacy/&lt;workflowId&gt;/</c> against
/// <c>characterization/recordings/dotnet/&lt;workflowId&gt;/</c> - and states that a recording whose
/// identifier does not exist on the other side "is not a comparison at all, and it must not be reported
/// as a pass". §4.2 adds the binding shared-volume rule: both halves of a pair must be captured against
/// the SAME <c>persistence-db</c> volume state, with no recreation or reseeding between them, or the pair
/// is void. Both halves must also stay inside one working tree. The first assertion below is that pair
/// rule, which is why a lone legacy recording fails rather than passes.
/// </para>
/// <para>
/// WHY THE WORKFLOW IDENTIFIER IS THE ROSTER'S, NOT THE ORACLE WINDOW'S. This constant previously held
/// the legacy window name <c>w_test_dwsvc_dropdownsearch</c> verbatim, on the reasoning that a
/// window-derived identifier is traceable where an invented one is arbitrary. The reasoning was right and
/// the spelling was wrong, for two independent reasons that were established rather than assumed:
/// </para>
/// <para>
/// FIRST, THAT SPELLING IS STRUCTURALLY ILLEGAL IN THIS STORE.
/// <c>characterization/workflows/workflow.schema.json</c> constrains <c>workflowId</c> to
/// <c>^[a-z][a-z0-9]*(-[a-z0-9]+)*$</c> - lower-case segments separated by single hyphens - and
/// instructs that a window name's underscores be rendered as hyphens for exactly this purpose. An
/// underscored identifier can never name a valid workflow directory, so this hook was watching a path
/// that could not come into existence however much oracle work was done.
/// </para>
/// <para>
/// SECOND, THE ROSTER ALREADY NAMES THIS WORKFLOW.
/// <c>characterization/workflows/dataservices-dwsvc-dropdownsearch.yaml</c> is the reviewed definition
/// covering the drop-down search service, and its identifier is the PAIRING KEY - the one thing that makes
/// two recordings comparable. A hook watching a different key would report a blocked oracle while a
/// recording sat under the roster key, and nothing would say so.
/// </para>
/// <para>
/// So the identifier is now the roster's, and traceability to the oracle window is kept where it belongs:
/// in the definition's own <c>oracleFixtures</c>, and in this file's header. The rename is safe precisely
/// because it is being done NOW - the schema states an identifier is never renamed once a recording exists
/// under it, since that orphans both halves of every pair already captured, and no recording exists under
/// either spelling. <see cref="TheWorkflowIdentifierAgreesWithTheStoresRoster"/> pins both facts so the
/// constant cannot drift back or drift onward.
/// </para>
/// </remarks>
public sealed class PinyinOracleCharacterizationHookTests
{
    /// <summary>
    /// The workflow identifier: the store roster entry whose oracle exercises the pinyin filter clause.
    /// </summary>
    /// <remarks>
    /// 🔴 DO NOT REPLACE THIS WITH THE ORACLE WINDOW NAME. The class remarks record why in full: the
    /// window name <c>w_test_dwsvc_dropdownsearch</c> cannot satisfy the store's own
    /// <c>workflowId</c> grammar, and this value is the pairing key that must match the reviewed
    /// definition. <see cref="TheWorkflowIdentifierAgreesWithTheStoresRoster"/> fails if either fact
    /// stops holding.
    /// </remarks>
    public const string WorkflowId = "dataservices-dwsvc-dropdownsearch";

    /// <summary>The legacy half of the pair, relative to the repository root.</summary>
    private const string LegacyRecordingDirectory = "characterization/recordings/legacy/" + WorkflowId;

    /// <summary>The target half of the pair, relative to the repository root.</summary>
    private const string DotnetRecordingDirectory = "characterization/recordings/dotnet/" + WorkflowId;

    /// <summary>
    /// The marker that identifies the repository root when walking up from the test binary.
    /// </summary>
    private const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>
    /// Whether a real legacy oracle recording exists for the pinyin workflow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ACTIVATION PREDICATE, and it is deliberately hard to trip by accident. An empty scaffold
    /// directory does NOT count, and neither does one holding only a readme or a placeholder dot-file:
    /// the AAP's own target tree creates the recording directories, so a predicate satisfied by their mere
    /// existence would start failing the build the moment the scaffold landed. It requires at least one
    /// ordinary file - which is what an actual capture produces.
    /// </para>
    /// <para>
    /// Filesystem faults answer <see langword="false"/> rather than propagating, because a predicate that
    /// throws would fail the run for an unrelated reason.
    /// </para>
    /// </remarks>
    public static bool LegacyOracleRecordingIsPresent
    {
        get
        {
            string? root = RepositoryRoot();

            return root is not null && CarriesAnOrdinaryFile(Resolve(root, LegacyRecordingDirectory));
        }
    }

    /// <summary>
    /// Whether <paramref name="directory"/> holds at least one file that an actual capture would have
    /// written, as opposed to scaffolding.
    /// </summary>
    /// <param name="directory">The recording directory to inspect.</param>
    /// <returns><see langword="true"/> when a real capture is present.</returns>
    /// <remarks>
    /// <para>
    /// ONE SCAN, USED BY BOTH HALVES OF THE PAIR AND BY THE SELF-TEST. It was written out three times, once
    /// per caller, which is exactly how the legacy half and the target half of a pair come to be judged by
    /// subtly different rules. The rule itself is unchanged: an empty scaffold directory does not count and
    /// neither does one holding only a readme or a dot-file.
    /// </para>
    /// <para>
    /// Filesystem faults answer <see langword="false"/> rather than propagating, for the same reason the
    /// predicate above does.
    /// </para>
    /// </remarks>
    private static bool CarriesAnOrdinaryFile(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(file);

                bool placeholder = name.StartsWith('.')
                    || string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase);

                if (!placeholder)
                {
                    return true;
                }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walks up from the test binary to the repository root.
    /// </summary>
    /// <returns>The root, or <see langword="null"/> when the marker is not found.</returns>
    private static string? RepositoryRoot()
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, RepositoryRootMarker)))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves a documentation-style relative path against the repository root.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="relative">The path as the parity documentation writes it, with forward slashes.</param>
    /// <returns>The resolved absolute path.</returns>
    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// One row per closed input, each carrying the paired recording locations that would settle it.
    /// </summary>
    /// <returns>The workflow identifier, the legacy and target recording locations, and the closed
    /// input.</returns>
    /// <remarks>
    /// The locations are member data rather than constants inside the body so they appear in the test
    /// name in a report: a reader of a CI log can see exactly which directory the suite is waiting for.
    /// </remarks>
    public static TheoryData<string, string, string, PinyinClosedInput> OracleRecordingLocations() => new()
    {
        { WorkflowId, LegacyRecordingDirectory, DotnetRecordingDirectory, PinyinClosedInput.LookupTable },
        {
            WorkflowId,
            LegacyRecordingDirectory,
            DotnetRecordingDirectory,
            PinyinClosedInput.MatchingRelation
        },
        {
            WorkflowId,
            LegacyRecordingDirectory,
            DotnetRecordingDirectory,
            PinyinClosedInput.DefaultFlagSet
        },
        {
            WorkflowId,
            LegacyRecordingDirectory,
            DotnetRecordingDirectory,
            PinyinClosedInput.FuzzySoundSet
        },
    };

    /// <summary>
    /// Every closed input is characterized in the shipped configuration exactly when a paired oracle
    /// recording exists for the workflow - so BLOCKED is asserted today and wiring is demanded the day a
    /// recording lands.
    /// </summary>
    /// <param name="workflowId">The workflow identifier joining the two halves of the pair.</param>
    /// <param name="legacyRecordingDirectory">The legacy half's location, relative to the repository
    /// root.</param>
    /// <param name="dotnetRecordingDirectory">The target half's location, relative to the repository
    /// root.</param>
    /// <param name="closedInput">The closed input this row is responsible for.</param>
    /// <remarks>
    /// <para>
    /// ACTIVE ON EVERY RUN, AND THAT IS THE CORRECTION. This matrix was <c>Skip</c>ped unless a legacy
    /// recording was present, so on the shipped state of this refactor its four rows contributed no
    /// executable protection whatsoever. It now asserts an EQUIVALENCE that is true in both worlds -
    /// characterized if and only if recorded - which executes in both and guards in both directions.
    /// </para>
    /// <para>
    /// TODAY THE ORACLE HAS NOT BEEN EXERCISED, which docs/PARITY.md records as the reason risk R1 is live,
    /// so every row asserts the BLOCKED state: no table, no characterized relation, no characterized flag
    /// default, no confirmed fuzzy closure, and a composition that still hands the expression evaluator
    /// <c>PinyinFirstLetterMatcher.Blocked</c>. BLOCKED is the correct reportable outcome for this refactor -
    /// not an approximation and not a silently missing test - and these rows are what stop an approximation
    /// arriving unnoticed: no third-party pinyin table and no guessed flag set can be substituted without
    /// failing here (AAP 0.6.5).
    /// </para>
    /// <para>
    /// THE DAY A RECORDING LANDS the same rows demand that the recorded data reached the configuration, and
    /// fail until it has - because a recording nobody wired in unblocks nothing.
    /// </para>
    /// <para>
    /// THE PAIR RULE IS ASSERTED WHENEVER EITHER HALF EXISTS. docs/PARITY.md 4.1 states that a recording
    /// whose identifier does not exist on the other side "is not a comparison at all, and it must not be
    /// reported as a pass", and 4.2 adds that both halves must be captured against the same
    /// <c>persistence-db</c> volume state inside one working tree. Keying activation on the legacy half alone
    /// would leave a target-only capture unexamined, so both halves are inspected by the same rule.
    /// </para>
    /// <para>
    /// IT DELIBERATELY DOES NOT INVENT A RECORDING FORMAT. The capture schema is an oracle deliverable, not
    /// something this suite may decide; what it can assert without inventing anything is that the pair exists
    /// and that the data reached the configuration. Once the schema exists, per-case value comparisons belong
    /// in this class beside this matrix.
    /// </para>
    /// <para>
    /// PAIRED WITH THE SIBLING ASSERTION OF TODAY'S STATE.
    /// <c>DataWindowExpressionEvaluatorTests.PinyinDispatchTests</c> asserts that the default evaluator
    /// carries <c>PinyinFirstLetterMatcher.Blocked</c>. That case and these rows now MOVE TOGETHER in one
    /// direction rather than describing opposite worlds: whoever lands a recording updates both, which is
    /// exactly the review moment this hook exists to force.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleRecordingLocations))]
    public void EveryClosedInputIsCharacterizedExactlyWhenThePairedOracleRecordingExists(
        string workflowId,
        string legacyRecordingDirectory,
        string dotnetRecordingDirectory,
        PinyinClosedInput closedInput)
    {
        string? root = RepositoryRoot();
        Assert.NotNull(root);

        string legacy = Resolve(root, legacyRecordingDirectory);
        string target = Resolve(root, dotnetRecordingDirectory);

        bool legacyRecorded = CarriesAnOrdinaryFile(legacy);
        bool targetRecorded = CarriesAnOrdinaryFile(target);

        // The legacy half is also reachable as a named predicate, and the two must agree: a divergence would
        // mean the pair rule below and the predicate were judging the same directory differently.
        Assert.Equal(legacyRecorded, LegacyOracleRecordingIsPresent);

        // docs/PARITY.md 4.1 - THE PAIR RULE, checked from EITHER side. A recording whose identifier does
        // not exist on the other side is not a partial comparison, it is not a comparison at all. Vacuous
        // while nothing is recorded, which is today.
        if (legacyRecorded || targetRecorded)
        {
            Assert.True(
                legacyRecorded,
                "Workflow " + workflowId + " has a target-side recording at " + target
                    + " but no legacy oracle recording at " + legacy
                    + ". docs/PARITY.md 4.1: an unpaired recording must not be reported as a pass.");
            Assert.True(
                targetRecorded,
                "Workflow " + workflowId + " has a legacy recording but no target-side recording at "
                    + target + ". docs/PARITY.md 4.1: an unpaired recording must not be reported as a pass, "
                    + "and 4.2 requires both halves be captured against the same persistence-db volume "
                    + "state inside one working tree.");
        }

        bool recorded = legacyRecorded && targetRecorded;

        DataWindowExpressionEvaluator evaluator = new(EvaluatorFixture.BuildSqliteFixture());
        PinyinFirstLetterMatcher matcher = evaluator.PinyinMatcher;

        // THE COMPOSITION TRACKS THE RECORDING. No pair means the shipped BLOCKED singleton is still what
        // the evaluator dispatches to and the matcher still reports that it cannot match; a pair means it
        // must be something else and must report that it can.
        Assert.Equal(recorded, !ReferenceEquals(PinyinFirstLetterMatcher.Blocked, matcher));
        Assert.Equal(recorded, matcher.CanMatch);

        PinyinMatchConfiguration configuration = matcher.Configuration;

        switch (closedInput)
        {
            case PinyinClosedInput.LookupTable:
                // (a) Null is the BLOCKED state and is MEANINGFUL: an absent table and an empty one are
                // different findings, so the assertion is on presence rather than on size - together with
                // the provenance string a real capture has to carry.
                Assert.Equal(recorded, configuration.Table is not null);
                Assert.Equal(recorded, configuration.Table?.SourceDescription.Length > 0);
                break;

            case PinyinClosedInput.MatchingRelation:
                // (b) NotCharacterized is the BLOCKED value, and it is the only value that cannot reach a
                // comparison at all.
                Assert.Equal(recorded, configuration.Strategy != PinyinMatchStrategy.NotCharacterized);
                break;

            case PinyinClosedInput.DefaultFlagSet:
                // (c) The two-argument overload's flag set. Null is BLOCKED; the reported predicate and the
                // underlying value must agree, since a divergence would let one of them lie.
                Assert.Equal(recorded, matcher.IsDefaultFlagSetCharacterized);
                Assert.Equal(recorded, configuration.CharacterizedDefaultFlags is not null);
                break;

            case PinyinClosedInput.FuzzySoundSet:
                // (d) THE DISCRIMINATING FACT IS EXHAUSTIVENESS, NOT PRESENCE. The documented pairs are
                // always in force - PinyinFuzzySoundEquivalence defaults Pairs to DocumentedPairs - so a
                // non-empty check would pass in the BLOCKED state too and would prove nothing. What the
                // oracle has to settle is whether that documented relation CLOSES, which is exactly what
                // ExhaustivenessConfirmed records.
                Assert.Equal(recorded, configuration.FuzzySound.ExhaustivenessConfirmed);
                Assert.NotEmpty(configuration.FuzzySound.Pairs);
                break;

            default:
                Assert.Fail("Unhandled closed input " + closedInput + " for workflow " + workflowId);
                break;
        }
    }

    /// <summary>
    /// The recording predicate agrees with the filesystem, and the repository-root locator it depends on
    /// actually found the root.
    /// </summary>
    /// <remarks>
    /// THE PREDICATE IS VERIFIED RATHER THAN TRUSTED, because every row of the matrix above pivots on it: a
    /// predicate broken into always-false would turn four two-directional guards into four assertions that
    /// the shipped state is BLOCKED and would never demand wiring again. Written against the filesystem
    /// directly rather than through the shared scan, so the two cannot be wrong in the same way, and phrased
    /// as an equality so it stays correct after a recording lands rather than becoming the thing that has to
    /// be remembered.
    /// </remarks>
    /// <summary>
    /// The workflow identifier is a legal store identifier AND names a workflow the roster declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS GUARD EXISTS BECAUSE THE CONSTANT WAS WRONG IN A WAY NOTHING DETECTED. It held the legacy
    /// oracle window name verbatim, underscores and all. Two independent facts made that unworkable, and
    /// this test pins both so neither can come back:
    /// </para>
    /// <para>
    /// (1) THE GRAMMAR. <c>characterization/workflows/workflow.schema.json</c> constrains
    /// <c>workflowId</c> to lower-case alphanumeric segments separated by single hyphens. An underscored
    /// identifier is not merely unconventional, it can never name a valid workflow directory - so this
    /// hook was watching a path that could not come into existence. The pattern is read FROM THE SCHEMA
    /// rather than restated here, because a restated pattern is a second thing to drift.
    /// </para>
    /// <para>
    /// (2) THE ROSTER. The identifier is the pairing key, so it has to be the key of the reviewed
    /// definition that covers this capability. A hook watching a key no definition declares would report
    /// a blocked oracle while a recording sat under the roster key, and nothing would say so.
    /// </para>
    /// <para>
    /// Both halves are asserted, and asserted separately, because a value can satisfy the grammar while
    /// naming nothing - which is precisely the failure a grammar-only check would wave through.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWorkflowIdentifierAgreesWithTheStoresRoster()
    {
        string? root = RepositoryRoot();

        Assert.NotNull(root);

        string schemaPath = Resolve(root, "characterization/workflows/workflow.schema.json");

        Assert.True(File.Exists(schemaPath), $"The store's schema is missing at '{schemaPath}'.");

        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        string pattern = schema.RootElement
            .GetProperty("properties")
            .GetProperty("workflowId")
            .GetProperty("pattern")
            .GetString()!;

        Assert.Matches(pattern, WorkflowId);

        // THE ROSTER IS THE SET OF DEFINITION FILES, read off disk. A hardcoded expectation here would
        // make this test agree with itself rather than with the store.
        string definition = Resolve(root, $"characterization/workflows/{WorkflowId}.yaml");

        Assert.True(
            File.Exists(definition),
            $"'{WorkflowId}' names no workflow definition. The identifier is the PAIRING KEY, so it must be "
            + $"the identifier of the reviewed definition covering this capability; '{definition}' does not "
            + "exist. Reconcile this constant with the roster rather than adding a definition to match it.");

        // ...and the definition must agree, in its own text, that this is its identifier. A file named
        // after an identifier whose content declares a different one would satisfy the check above while
        // still pairing recordings under the wrong key.
        Assert.Contains(
            $"workflowId: {WorkflowId}",
            File.ReadAllText(definition),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOracleRecordingPredicateAgreesWithTheFilesystem()
    {
        string? root = RepositoryRoot();

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, RepositoryRootMarker)));

        string legacy = Resolve(root, LegacyRecordingDirectory);
        bool hasOrdinaryFile = false;

        if (Directory.Exists(legacy))
        {
            foreach (string file in Directory.EnumerateFiles(legacy))
            {
                string name = Path.GetFileName(file);

                if (!name.StartsWith('.')
                    && !string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase))
                {
                    hasOrdinaryFile = true;
                    break;
                }
            }
        }

        Assert.Equal(hasOrdinaryFile, LegacyOracleRecordingIsPresent);
    }
}

// =====================================================================================================
//  7. REGISTRATION AND THE REACHABLE CALL PATH
//     ORACLE ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L321-L324
// =====================================================================================================

/// <summary>
/// Asserts that the matcher is reached the way the legacy reaches it - through DataWindow expression
/// dispatch - and that the drop-down search model reaches it only INDIRECTLY, by emitting its name as
/// text.
/// </summary>
/// <remarks>
/// <para>
/// THE LEGACY NEVER CALLS THIS FUNCTION FROM POWERSCRIPT. It builds a string:
/// </para>
/// <code>
/// if BitTest(#FilterType,FILTER_DISP_PY) then                                          [:L321]
///     if Match(data,"[a-zA-Z]") then                                                   [:L322]
///         sFilter += " OR PinyinFirstLetterLike(" + _editCtx.dddw.dispColName
///                    + ",'" + sData + "',7)"                                           [:L323]
/// </code>
/// <para>
/// That is a DataWindow FILTER EXPRESSION, evaluated later by the expression evaluator. Two consequences,
/// and both are asserted here. The evaluator must REGISTER the function under the name the legacy embeds,
/// or the pinyin capability is unreachable however faithfully the matcher itself is ported. And the model
/// must NOT hold a reference to the matcher, because holding one would be a different architecture from
/// the one the oracle describes.
/// </para>
/// <para>
/// The byte-exact clause text belongs to the model and is asserted in <c>DropDownSearchModelTests</c>
/// across the whole filter-type power set. This class asserts only the registration and the indirection,
/// so the two suites do not duplicate each other's subject.
/// </para>
/// </remarks>
public sealed class PinyinExpressionRegistrationTests
{
    /// <summary>
    /// A table covering the letters of the fixture's first row, so an end-to-end expression evaluation can
    /// actually decide.
    /// </summary>
    /// <returns>The table.</returns>
    /// <remarks>
    /// The fixture's <c>name</c> column carries <c>Alice</c> on row 1 - Latin, not Han. The letters are
    /// taught EXPLICITLY here, which is the point: the unit passes nothing through on its own, so even a
    /// Latin character has to be in the table. No pinyin claim is made or implied by any row of it.
    /// </remarks>
    private static SyntheticPinyinFirstLetterTable LatinTable() =>
        new SyntheticPinyinFirstLetterTable("latin-only-test-fixture (NOT the pfw.dll table)")
            .With('A', 'a')
            .With('l', 'l')
            .With('i', 'i')
            .With('c', 'c')
            .With('e', 'e');

    /// <summary>
    /// The evaluator registers the function under the name the legacy embeds in its filter text, and
    /// resolves it case-insensitively.
    /// </summary>
    /// <remarks>
    /// DataWindow expression function names are not case-sensitive, so the registration is asserted under
    /// three casings. Resolution is ordinal rather than culture-aware, so no ambient culture can change
    /// which function an expression resolves to.
    /// </remarks>
    [Fact]
    public void TheEvaluatorRegistersTheNameTheLegacyEmbedsInItsFilterText()
    {
        DataWindowExpressionEvaluator evaluator = new(EvaluatorFixture.BuildSqliteFixture());

        Assert.True(evaluator.IsFunctionRegistered(PinyinFirstLetterMatcher.ExpressionFunctionName));
        Assert.True(evaluator.IsFunctionRegistered("PINYINFIRSTLETTERLIKE"));
        Assert.True(evaluator.IsFunctionRegistered("pinyinfirstletterlike"));
        Assert.Contains(PinyinFirstLetterMatcher.ExpressionFunctionName, evaluator.FunctionNames);

        // The native export alias is NOT a registered name - the expression resolves the PowerScript name.
        Assert.False(evaluator.IsFunctionRegistered("pfwPinyinFirstLetterLike"));

        Assert.True(PinyinFirstLetterMatcher.IsExpressionFunctionName(
            PinyinFirstLetterMatcher.ExpressionFunctionName));
        Assert.True(PinyinFirstLetterMatcher.IsExpressionFunctionName("pinyinfirstletterlike"));
        Assert.True(PinyinFirstLetterMatcher.IsExpressionFunctionName("PinyinFIRSTLetterLike"));
        Assert.False(PinyinFirstLetterMatcher.IsExpressionFunctionName("pfwPinyinFirstLetterLike"));
        Assert.False(PinyinFirstLetterMatcher.IsExpressionFunctionName(string.Empty));
        Assert.False(PinyinFirstLetterMatcher.IsExpressionFunctionName(null));
    }

    /// <summary>
    /// The registered function dispatches to the matcher the evaluator was CONSTRUCTED with, and its
    /// answer reaches the expression as a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clause shape is the legacy's own - a bare call with the display column, a quoted pattern and the
    /// literal flag argument [<c>:L323</c>] - and it is also exercised OR-ed with the plain display-column
    /// LIKE term [<c>:L319</c>], because that is how the predicate actually appears: one term of a
    /// compound expression.
    /// </para>
    /// <para>
    /// The contrast with the BLOCKED matcher is asserted in the same case on purpose. A characterized
    /// matcher yields a boolean; the BLOCKED one yields the invalid-expression sentinel and a structured
    /// error - NOT <c>false</c>. Seeing both in one place is what makes it obvious that blocked and
    /// negative are different answers at the expression level too, not just inside the matcher.
    /// <c>DataWindowExpressionEvaluatorTests.PinyinDispatchTests</c> owns the blocked-default and
    /// short-circuit cases in their own right.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRegisteredFunctionDispatchesToTheConfiguredMatcher()
    {
        PinyinFirstLetterMatcher matcher = PinyinMatcherFixture.Matcher(
            PinyinMatchStrategy.Prefix,
            LatinTable(),
            PinyinMatcherFixture.CallSiteFlags);
        DataWindowExpressionEvaluator evaluator = new(EvaluatorFixture.BuildSqliteFixture(), matcher);

        Assert.Same(matcher, evaluator.PinyinMatcher);

        // The legacy clause shape, on row 1 whose name is "Alice".
        Assert.Equal("true", evaluator.Evaluate("PinyinFirstLetterLike(name,'alice',7)", 1L));
        Assert.Equal("false", evaluator.Evaluate("PinyinFirstLetterLike(name,'zzz',7)", 1L));

        // The whole composed filter shape: the pinyin term OR-ed with the display-column LIKE term.
        Assert.Equal(
            "true",
            evaluator.Evaluate(
                "((Lower(name) LIKE '%zzz%') OR PinyinFirstLetterLike(name,'alice',7))",
                1L));
        Assert.Equal(
            "false",
            evaluator.Evaluate(
                "((Lower(name) LIKE '%zzz%') OR PinyinFirstLetterLike(name,'zzz',7))",
                1L));

        // The two-argument form goes through the characterized default rather than a guess.
        Assert.Equal("true", evaluator.Evaluate("PinyinFirstLetterLike(name,'alice')", 1L));

        // And the BLOCKED matcher answers the SAME expression with the invalid sentinel plus a structured
        // error - never with "false".
        DataWindowExpressionEvaluator blocked = new(EvaluatorFixture.BuildSqliteFixture());
        DataWindowExpressionResult result = blocked.TryEvaluate(
            "PinyinFirstLetterLike(name,'alice',7)",
            1L);

        Assert.Same(PinyinFirstLetterMatcher.Blocked, blocked.PinyinMatcher);
        Assert.Equal(ExpressionEvaluationOutcome.InvalidExpression, result.Outcome);
        Assert.Equal(DataWindowExpressionEvaluator.InvalidExpressionSentinel, result.Text);
        Assert.NotEqual("false", result.Text);
        Assert.NotNull(result.Error);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, result.Error.ReturnCode);
    }

    /// <summary>
    /// Every type in the service whose name carries the <c>Pinyin</c> marker, for the reference scans
    /// below.
    /// </summary>
    /// <returns>The pinyin types.</returns>
    private static HashSet<Type> PinyinTypes() =>
        [.. typeof(PinyinFirstLetterMatcher).Assembly
            .GetTypes()
            .Where(type => type.Name.Contains("Pinyin", StringComparison.Ordinal))];

    /// <summary>
    /// Every pinyin type named anywhere in a type's own member signatures.
    /// </summary>
    /// <param name="subject">The type to scan.</param>
    /// <returns>The member descriptions that name a pinyin type.</returns>
    /// <remarks>
    /// Declared members only, at every accessibility, covering field and property types, method return
    /// types and every parameter of every method and constructor. A dependency expressed in any of those
    /// places would be a direct reference; one expressed only inside a method body could not be, because
    /// the type would have to be named in a signature somewhere to be constructed or received.
    /// </remarks>
    private static List<string> PinyinReferencesIn(Type subject)
    {
        HashSet<Type> pinyinTypes = PinyinTypes();
        List<string> references = [];

        const BindingFlags everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        foreach (FieldInfo field in subject.GetFields(everything))
        {
            if (pinyinTypes.Contains(field.FieldType))
            {
                references.Add("field " + field.Name);
            }
        }

        foreach (PropertyInfo property in subject.GetProperties(everything))
        {
            if (pinyinTypes.Contains(property.PropertyType))
            {
                references.Add("property " + property.Name);
            }
        }

        foreach (MethodInfo method in subject.GetMethods(everything))
        {
            if (pinyinTypes.Contains(method.ReturnType))
            {
                references.Add("return of " + method.Name);
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (pinyinTypes.Contains(parameter.ParameterType))
                {
                    references.Add("parameter " + parameter.Name + " of " + method.Name);
                }
            }
        }

        foreach (ConstructorInfo constructor in subject.GetConstructors(everything))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (pinyinTypes.Contains(parameter.ParameterType))
                {
                    references.Add("constructor parameter " + parameter.Name);
                }
            }
        }

        return references;
    }

    /// <summary>
    /// THE SCAN IS MEANINGFUL: it really does detect a direct reference where one legitimately exists.
    /// </summary>
    /// <remarks>
    /// A negative result is only worth having if the instrument works, so this case is the positive
    /// control. The expression evaluator IS the legitimate holder of the matcher - it is what dispatches
    /// the registered function - so the scan must find it there. Without this case, the indirection
    /// assertion below could pass simply because the scan looked in the wrong place.
    /// </remarks>
    [Fact]
    public void TheReferenceScanDetectsTheEvaluatorsLegitimateDependency()
    {
        List<string> references = PinyinReferencesIn(typeof(DataWindowExpressionEvaluator));

        Assert.NotEmpty(references);
        Assert.NotEmpty(PinyinTypes());
        Assert.Contains(typeof(PinyinFirstLetterMatcher), PinyinTypes());
        Assert.Contains(typeof(IPinyinFirstLetterTable), PinyinTypes());
    }

    /// <summary>
    /// The drop-down search model reaches the matcher ONLY indirectly - it names the function in text and
    /// holds no reference to any pinyin type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the architectural shape the oracle dictates. The model's job is to COMPOSE a filter
    /// expression [<c>:L319-L334</c>]; the DataWindow evaluates it later and resolves the function then.
    /// A model that called the matcher directly would produce the right-looking answer through the wrong
    /// path, and the legacy's own behaviour - where the clause may be short-circuited away by the OR, or
    /// never evaluated at all if the filter is replaced first - would not be reproduced.
    /// </para>
    /// <para>
    /// The name is asserted to be present as a STRING CONSTANT, matched by VALUE rather than by field
    /// name, so the case pins the mechanism without pinning an implementation detail nobody promised.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDropDownSearchModelReachesTheMatcherOnlyByNamingItInText()
    {
        Assert.Empty(PinyinReferencesIn(typeof(DropDownSearchModel)));

        const BindingFlags everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        List<string> nameConstants = [];

        foreach (FieldInfo field in typeof(DropDownSearchModel).GetFields(everything))
        {
            bool namesTheFunction = field.IsLiteral
                && field.FieldType == typeof(string)
                && string.Equals(
                    (string?)field.GetRawConstantValue(),
                    PinyinFirstLetterMatcher.ExpressionFunctionName,
                    StringComparison.Ordinal);

            if (namesTheFunction)
            {
                nameConstants.Add(field.Name);
            }
        }

        // Exactly one constant carries the name, and it is the same name the evaluator registers - so the
        // clause the model emits resolves to the matcher and to nothing else.
        Assert.Single(nameConstants);
    }

    /// <summary>
    /// The matcher's own configuration is what varies between deployments; the registration never changes.
    /// </summary>
    /// <remarks>
    /// Asserted because it is what makes the BLOCKED state a CONFIGURATION rather than a missing feature:
    /// the function is registered and callable in every deployment, and whether it can answer depends on
    /// the seams alone. A deployment that unregistered the function while blocked would report a
    /// completely different and much less useful error - an unknown function rather than an
    /// uncharacterized table.
    /// </remarks>
    [Fact]
    public void TheFunctionIsRegisteredWhetherOrNotItCanAnswer()
    {
        DataWindowExpressionEvaluator blocked = new(EvaluatorFixture.BuildSqliteFixture());
        DataWindowExpressionEvaluator characterized = new(
            EvaluatorFixture.BuildSqliteFixture(),
            PinyinMatcherFixture.Matcher(
                PinyinMatchStrategy.Prefix,
                LatinTable(),
                PinyinMatcherFixture.CallSiteFlags));

        Assert.True(blocked.IsFunctionRegistered(PinyinFirstLetterMatcher.ExpressionFunctionName));
        Assert.True(characterized.IsFunctionRegistered(PinyinFirstLetterMatcher.ExpressionFunctionName));

        Assert.False(blocked.PinyinMatcher.CanMatch);
        Assert.True(characterized.PinyinMatcher.CanMatch);

        Assert.Equal(blocked.FunctionNames.Count, characterized.FunctionNames.Count);
    }
}
