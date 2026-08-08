// ==============================================================================================
//  TextTests - the parity suite for PowerFramework.Shared.Kernel.Text
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  PowerFramework.Shared.Kernel.Text, in
//                   shared/PowerFramework.Shared.Kernel/Text.cs
//
//  NAMED OBLIGATION OF THIS SUITE: Text.Iif, ported from
//                   ws_objects/pfw.shared.pbl.src/iif.srf
//
//  THE ONE BEHAVIOUR THIS SUITE EXISTS TO PIN
//  --------------------------------------------------------------------------------------------
//  BOTH ARGUMENTS ARE EVALUATED, EAGERLY, WHATEVER THE CONDITION.
//
//  That is the whole point, and it is worth being blunt about why a suite is needed for something
//  that looks so obvious. `Iif` is the single most inviting target for a well-meant "simplification"
//  in the entire shared kernel: a reviewer who sees
//
//      public static long Iif(bool condition, long truePart, long falsePart)
//      {
//          if (condition) { return truePart; }
//          return falsePart;
//      }
//
//  will be tempted to collapse it to `condition ? truePart : falsePart`, or to make it lazy by
//  taking `Func<T>` parameters, or to delete it outright and rewrite its call sites as `?:`. Every
//  one of those changes SHORT CIRCUITS, and short circuiting is a BEHAVIOURAL CHANGE, not an
//  optimisation - it suppresses the side effects and the exceptions of the unselected branch at the
//  104 legacy call sites `iif` has in ws_objects/.
//
//  A test that only checked WHICH VALUE COMES BACK would not notice any of that: `?:` returns the
//  same value. Only counting the evaluations distinguishes them, which is why the centrepiece of
//  this file counts, and why it counts with an INTEGER rather than a boolean flag. A flag cannot
//  tell one evaluation from two, and exactly one evaluation is what the `?:` rewrite produces.
//
//  THE ORACLE, AND ITS LIMITS
//  --------------------------------------------------------------------------------------------
//  `iif.srf` is READ ONLY and is the behavioural oracle. It is also the WHOLE oracle for this
//  function: there is no `w_test_iif` among the 47 `w_test_*.srw` windows in
//  ws_objects/pfw.tests.pbl.src, so the legacy corpus contains no characterization window for these
//  primitives at all. The nine prototypes at `iif.srf:L7-L15` and the nine bodies at
//  `iif.srf:L18-L79` are therefore the entire specification, and every case below cites the line it
//  was taken from. Nothing in this file opens a path under ws_objects/ at run time; the locators are
//  comments, read once by a human, never by the test host.
//
//  VERIFIED AGAINST THE SOURCE WHILE WRITING THIS SUITE
//      iif.srf:L7-L15   nine forward prototypes, in this order:
//                       string, boolean, long, integer, date, time, datetime, decimal, double.
//                       Every parameter is `readonly`, and the shape is uniformly
//                       (readonly boolean abExpression, readonly T aTruePart, readonly T aFalsePart)
//                       returning T.
//      iif.srf:L18-L79  nine bodies, every one the same three lines:
//                       `if abExpression then return <true part> else return <false part> end if`.
//                       No delegate, no deferred expression, no lazy form anywhere.
//      iif.srf:L3       `global type iif from function_object` with NO `native` clause, so unlike
//                       `replaceall` the body is readable PowerScript and nothing here is inferred.
//      Text.cs          exposes nine matching overloads and no others. The cross check passes;
//                       there is no missing overload to report.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement, that no user rules were provided. That is a FINDING, not
//  latitude: no rule is invented or back-filled here, and the bar is the plan's enterprise baseline
//  and its non-rule constraint inventory instead.
//
//      C-B  No behaviour improvements; behaviour is preserved exactly. Eagerness is a PRESERVED
//           BEHAVIOUR that looks like an inefficiency, so the assertion that pins it carries a
//           comment saying so in as many words, at the assertion itself, where an author about to
//           "fix" `Iif` will actually read it. Without that note the implementation and its test get
//           "fixed" together and the regression ships green.
//      C-C  The legacy tree is read only and is the oracle. Locators are cited in comments only.
//           No legacy file was edited to write this suite and no legacy path is opened at run time.
//      C-H  Nullable reference types and warnings-as-errors apply to test projects exactly as they
//           apply to the library. This file therefore contains no null-forgiving `!`, no `#pragma`,
//           no analyzer suppression and no unused member. Zero warnings is the standard, not zero
//           errors.
//      C-K  Every technology-specific decision is documented where it is made: why `Iif` must stay
//           a METHOD, why PowerBuilder `integer` maps to `short` and not to `int`, why `decimal` is
//           not `double`, and why the overload-coverage table is shaped the way it is.
//      0.6.7  Prescribed test shape: table-driven parity matrices expressed as theories with member
//           data. Regions 2, 3 and 4 are those matrices; Region 1 is a Fact because counting
//           evaluations is a single indivisible observation, not a row in a table.
//
//  NAMING, WHICH IS A BUILD REQUIREMENT HERE RATHER THAN A PREFERENCE
//  --------------------------------------------------------------------------------------------
//  The repository root .editorconfig scopes its naming-analyzer suppressions BY FILE GLOB to the
//  ten implementation files that genuinely carry preserved legacy SCREAMING_SNAKE identifiers.
//  No test file is covered by any of them, and TreatWarningsAsErrors is on. Every identifier
//  declared in this file is therefore conventionally named - PascalCase types and members,
//  camelCase locals - and this file declares no SCREAMING_SNAKE identifier of any kind.
// ==============================================================================================

using System.Globalization;
using System.Reflection;

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Text"/>, whose named obligation is pinning the eager argument
/// evaluation of <see cref="Text.Iif(bool, string?, string?)"/> and its eight sibling overloads.
/// </summary>
/// <remarks>
/// <para>
/// The type under test resolves by simple name without a using directive: this file's namespace,
/// <c>PowerFramework.Shared.Kernel.Tests</c>, is nested inside <c>PowerFramework.Shared.Kernel</c>,
/// so simple-name lookup walks up into the enclosing namespace and finds <c>Text</c> there. A
/// using-namespace-directive imports the TYPES of a namespace and not its nested namespaces, so
/// <c>Text</c> cannot be confused with <c>System.Text</c> even though <c>System</c> is implicitly
/// imported. The redundant directive is deliberately omitted rather than added and suppressed.
/// </para>
/// <para>
/// Every test here is synchronous and in process. Nothing awaits, so the ambient test cancellation
/// token has no call to be threaded through; nothing touches the file system, a socket, a clock or
/// the current culture, so the suite is deterministic and reproducible, which is the one hard
/// prerequisite of the characterization model this refactor is measured by.
/// </para>
/// </remarks>
public sealed class TextTests
{
    // ==========================================================================================
    // THE PROBE FIXTURE
    // ==========================================================================================
    //
    // A side-effecting stand-in for a real argument expression. Its whole job is to make the ACT
    // of evaluating an argument observable, which is the only way to distinguish an eager method
    // from a short-circuiting conditional.
    //
    // WHY A COUNTER AND NOT A BOOLEAN FLAG
    // A flag records "was this evaluated at all", which both an eager and a lazy implementation
    // would set for the SELECTED branch. The distinction being tested is one evaluation versus two,
    // so the fixture must count. `EvaluationCount` is what makes the `?:` rewrite detectable.
    //
    // WHY EACH ACCESSOR RETURNS THE RUNNING COUNT
    // Returning the count makes the two evaluations DISTINGUISHABLE, which buys a second property
    // for free: because C# guarantees that the expressions in an argument list are evaluated left
    // to right, the first accessor call in the argument list is always the one that produces 1 and
    // the second is always the one that produces 2. Asserting which value comes back therefore
    // proves the EVALUATION ORDER as well as the branch selection.
    //
    // The three accessors exist because eagerness has to be pinned on more than one overload; see
    // the comment on IifEvaluatesBothBranchesEagerlyForTheBooleanOverload for why. Each is used.
    private sealed class EvaluationProbe
    {
        /// <summary>
        /// Gets the number of argument expressions that have been evaluated through this probe.
        /// </summary>
        public int EvaluationCount { get; private set; }

        /// <summary>
        /// Records an evaluation and returns a textual value identifying which evaluation it was.
        /// </summary>
        /// <returns>
        /// <c>evaluation-1</c> for the first call, <c>evaluation-2</c> for the second.
        /// </returns>
        public string NextText()
        {
            return "evaluation-" + Advance().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Records an evaluation and returns a numeric value identifying which evaluation it was.
        /// </summary>
        /// <returns>1 for the first call, 2 for the second, and so on.</returns>
        public long NextNumber()
        {
            return Advance();
        }

        /// <summary>
        /// Records an evaluation and returns a boolean value identifying which evaluation it was.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for the FIRST evaluation only, and <see langword="false"/> for
        /// every later one. The asymmetry is deliberate: it is what makes the two evaluations of a
        /// boolean argument list distinguishable from one another.
        /// </returns>
        public bool NextFlag()
        {
            return Advance() == 1;
        }

        private int Advance()
        {
            EvaluationCount++;

            return EvaluationCount;
        }
    }

    // ==========================================================================================
    // REGION 1 - EAGERNESS. THE CENTREPIECE OF THIS SUITE.
    // Oracle: ws_objects/pfw.shared.pbl.src/iif.srf:L7-L15 (the prototypes) and L18-L79 (the
    //         bodies). `iif` is declared `from function_object`, so it is a FUNCTION, and a
    //         PowerScript function's arguments are evaluated at the CALL SITE before control
    //         enters it.
    // ==========================================================================================

    /// <summary>
    /// Proves that <see cref="Text.Iif(bool, string?, string?)"/> evaluates BOTH branch arguments
    /// regardless of the condition, and that it evaluates them left to right.
    /// </summary>
    [Fact]
    public void IifEvaluatesBothBranchesEagerly()
    {
        // ---- A true condition still evaluates the false part ----
        EvaluationProbe whenConditionHolds = new();

        string? selectedWhenTrue = Text.Iif(
            true,
            whenConditionHolds.NextText(),
            whenConditionHolds.NextText());

        // C-B - BEHAVIOUR PRESERVATION, NOT AN INEFFICIENCY TO BE FIXED.
        // This assertion deliberately pins LEGACY BEHAVIOUR taken from
        // ws_objects/pfw.shared.pbl.src/iif.srf, where `iif` is a `function_object` (L3) whose nine
        // bodies at L18-L79 are reached by an ordinary function call. PowerScript evaluates a
        // function's argument expressions at the call site BEFORE entering the function, so both
        // the true part and the false part are always evaluated. `iif` does not short circuit and
        // never did.
        //
        // Converting Text.Iif to `condition ? truePart : falsePart` - or to a `Func<T>`-taking
        // overload, or deleting it and rewriting its call sites as `?:` - WOULD BE A BEHAVIOURAL
        // CHANGE AND NOT AN OPTIMISATION. It would suppress the side effects and the exceptions of
        // the unselected branch. If this assertion is ever seen to fail, the correct response is to
        // restore the eager method, never to relax the assertion to 1.
        //
        // C-K - WHY Iif MUST REMAIN A METHOD. The eagerness is a property of the legacy CALL
        // CONVENTION, not of the function body: the body merely picks one of two values that have
        // already been computed. There is no lazy-argument mechanism anywhere in PowerScript, so
        // there is nothing lazy to reproduce and nothing to be gained by modelling one. An ordinary
        // C# method reproduces the convention exactly, because C# likewise evaluates arguments
        // before the call. That equivalence is the entire reason the port is a method, and it is
        // why the port must STAY a method.
        Assert.Equal(2, whenConditionHolds.EvaluationCount);

        // Both ran, AND the right one was chosen: `evaluation-1` is the value produced by the FIRST
        // accessor call in the argument list, which is the true part. That is also the left-to-right
        // evaluation order, which C# guarantees for an argument list.
        Assert.Equal("evaluation-1", selectedWhenTrue);

        // ---- A false condition still evaluates the true part ----
        EvaluationProbe whenConditionFails = new();

        string? selectedWhenFalse = Text.Iif(
            false,
            whenConditionFails.NextText(),
            whenConditionFails.NextText());

        // Same C-B and C-K reasoning as above, asserted on the other side of the branch so that a
        // one-sided lazy implementation cannot pass. A `?:` rewrite scores 1 here as well.
        Assert.Equal(2, whenConditionFails.EvaluationCount);

        // `evaluation-2` is the value produced by the SECOND accessor call, which is the false part.
        // Taken together with the assertion above, this pins both the selection and the order.
        Assert.Equal("evaluation-2", selectedWhenFalse);
    }

    /// <summary>
    /// Repeats the eagerness proof on the <see cref="bool"/> overload, so that no single overload
    /// can be made lazy while its eight siblings are left alone.
    /// </summary>
    /// <remarks>
    /// The boolean overload is chosen as the second subject deliberately. It is the one that reads
    /// most like a conditional expression already - <c>Iif(condition, trueFlag, falseFlag)</c> is
    /// almost the literal spelling of <c>condition ? trueFlag : falseFlag</c> - so it is the overload
    /// a "simplification" would reach for first. Oracle: <c>iif.srf:L8</c> for the prototype,
    /// <c>iif.srf:L25-L30</c> for the body.
    /// </remarks>
    [Fact]
    public void IifEvaluatesBothBranchesEagerlyForTheBooleanOverload()
    {
        EvaluationProbe whenConditionHolds = new();

        bool selectedWhenTrue = Text.Iif(
            true,
            whenConditionHolds.NextFlag(),
            whenConditionHolds.NextFlag());

        // C-B. Same preserved behaviour, same reasoning as IifEvaluatesBothBranchesEagerly, asserted
        // on a second overload so that eagerness is a property of the FAMILY and not of one member.
        // `NextFlag` returns true only for the first evaluation, so a true condition must yield true
        // - the value of the FIRST argument - and the counter must still reach 2.
        Assert.Equal(2, whenConditionHolds.EvaluationCount);
        Assert.True(selectedWhenTrue);

        EvaluationProbe whenConditionFails = new();

        bool selectedWhenFalse = Text.Iif(
            false,
            whenConditionFails.NextFlag(),
            whenConditionFails.NextFlag());

        // A false condition must yield false, which is the value of the SECOND argument, while the
        // counter again reaches 2. A `?:` implementation reaches 1 on both halves of this test.
        Assert.Equal(2, whenConditionFails.EvaluationCount);
        Assert.False(selectedWhenFalse);
    }

    /// <summary>
    /// Repeats the eagerness proof on a numeric overload, covering the third distinct argument
    /// shape - a value type carrying an ordinary number rather than a reference or a flag.
    /// </summary>
    /// <remarks>
    /// Oracle: <c>iif.srf:L9</c> for the prototype, <c>iif.srf:L32-L37</c> for the body. This is the
    /// shape the legacy colour-clamping idiom uses, where the unselected branch is an arithmetic
    /// expression that the legacy still evaluates, for example
    /// <c>iif(Int(r + 10) &gt; 255, 255, r + 10)</c>.
    /// </remarks>
    [Fact]
    public void IifEvaluatesBothBranchesEagerlyForTheNumericOverload()
    {
        EvaluationProbe whenConditionHolds = new();

        long selectedWhenTrue = Text.Iif(
            true,
            whenConditionHolds.NextNumber(),
            whenConditionHolds.NextNumber());

        // C-B. Preserved eagerness again, on the numeric family. The returned value doubles as the
        // ordinal of the evaluation that produced it, so 1 here is simultaneously "the true part was
        // returned" and "the true part was evaluated first".
        Assert.Equal(2, whenConditionHolds.EvaluationCount);
        Assert.Equal(1L, selectedWhenTrue);

        EvaluationProbe whenConditionFails = new();

        long selectedWhenFalse = Text.Iif(
            false,
            whenConditionFails.NextNumber(),
            whenConditionFails.NextNumber());

        Assert.Equal(2, whenConditionFails.EvaluationCount);
        Assert.Equal(2L, selectedWhenFalse);
    }

    // ==========================================================================================
    // REGION 2 - SELECTION. Table-driven, per 0.6.7.
    // The point of this region is CORRECT SELECTION rather than eagerness: given a condition and
    // two already-evaluated parts, the right part comes back. Three of the nine overloads are
    // exercised here as tables; all nine are covered by Region 3.
    // ==========================================================================================

    /// <summary>
    /// Selection cases for the <see cref="string"/> overload.
    /// </summary>
    /// <remarks>
    /// Oracle for every row: <c>iif.srf:L7</c> (prototype) and <c>iif.srf:L18-L23</c> (body). The
    /// null rows matter because the legacy parameters are PowerScript strings, which may be null,
    /// and the body returns whichever part was selected WITHOUT inspecting it - so a null selection
    /// is returned as a null rather than coerced to the empty string. The empty-string rows are the
    /// same point for a value the port could plausibly have been tempted to normalise.
    /// </remarks>
    public static TheoryData<bool, string?, string?, string?> StringSelectionCases =>
        new()
        {
            { true, "chosen", "rejected", "chosen" },
            { false, "chosen", "rejected", "rejected" },
            { true, null, "rejected", null },
            { false, "chosen", null, null },
            { true, "", "rejected", "" },
            { false, "chosen", "", "" },
            { true, "chosen", "chosen", "chosen" },
        };

    /// <summary>
    /// Verifies that the <see cref="string"/> overload returns the selected part unchanged,
    /// including when the selected part is null or empty.
    /// </summary>
    /// <param name="condition">The condition under test.</param>
    /// <param name="truePart">The value offered as the true part.</param>
    /// <param name="falsePart">The value offered as the false part.</param>
    /// <param name="expected">The part the legacy body would have returned.</param>
    [Theory]
    [MemberData(nameof(StringSelectionCases))]
    public void IifSelectsTheExpectedPartForTheStringOverload(
        bool condition,
        string? truePart,
        string? falsePart,
        string? expected)
    {
        Assert.Equal(expected, Text.Iif(condition, truePart, falsePart));
    }

    /// <summary>
    /// Selection cases for the <see cref="long"/> overload.
    /// </summary>
    /// <remarks>
    /// Oracle for every row: <c>iif.srf:L9</c> (prototype) and <c>iif.srf:L32-L37</c> (body). The
    /// extreme rows are not decoration: they would overflow every narrower overload, so they also
    /// establish that this overload really carries a 64-bit value rather than silently truncating.
    /// </remarks>
    public static TheoryData<bool, long, long, long> LongSelectionCases =>
        new()
        {
            { true, 1L, 2L, 1L },
            { false, 1L, 2L, 2L },
            { true, long.MaxValue, long.MinValue, long.MaxValue },
            { false, long.MaxValue, long.MinValue, long.MinValue },
            { true, 0L, 0L, 0L },
            { false, -1L, 0L, 0L },
        };

    /// <summary>
    /// Verifies that the <see cref="long"/> overload returns the selected part unchanged.
    /// </summary>
    /// <param name="condition">The condition under test.</param>
    /// <param name="truePart">The value offered as the true part.</param>
    /// <param name="falsePart">The value offered as the false part.</param>
    /// <param name="expected">The part the legacy body would have returned.</param>
    [Theory]
    [MemberData(nameof(LongSelectionCases))]
    public void IifSelectsTheExpectedPartForTheLongOverload(
        bool condition,
        long truePart,
        long falsePart,
        long expected)
    {
        Assert.Equal(expected, Text.Iif(condition, truePart, falsePart));
    }

    /// <summary>
    /// Selection cases for the <see cref="double"/> overload.
    /// </summary>
    /// <remarks>
    /// Oracle for every row: <c>iif.srf:L15</c> (prototype) and <c>iif.srf:L74-L79</c> (body). The
    /// special-value rows exist because the legacy body returns the selected part WITHOUT comparing
    /// it to anything, so the IEEE-754 values that compare unequal to themselves have to pass
    /// straight through. A "helpful" implementation that normalised not-a-number, or that used a
    /// tolerance comparison anywhere, would fail the last row.
    /// </remarks>
    public static TheoryData<bool, double, double, double> DoubleSelectionCases =>
        new()
        {
            { true, 1.5d, 2.5d, 1.5d },
            { false, 1.5d, 2.5d, 2.5d },
            { true, double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity },
            { false, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity },
            { true, double.MaxValue, double.MinValue, double.MaxValue },
            { false, 0d, -0d, -0d },
        };

    /// <summary>
    /// Verifies that the <see cref="double"/> overload returns the selected part unchanged.
    /// </summary>
    /// <param name="condition">The condition under test.</param>
    /// <param name="truePart">The value offered as the true part.</param>
    /// <param name="falsePart">The value offered as the false part.</param>
    /// <param name="expected">The part the legacy body would have returned.</param>
    [Theory]
    [MemberData(nameof(DoubleSelectionCases))]
    public void IifSelectsTheExpectedPartForTheDoubleOverload(
        bool condition,
        double truePart,
        double falsePart,
        double expected)
    {
        Assert.Equal(expected, Text.Iif(condition, truePart, falsePart));
    }

    /// <summary>
    /// Verifies that the <see cref="double"/> overload passes not-a-number through untouched, on
    /// both sides of the branch.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="DoubleSelectionCases"/> rather than added as a row, because
    /// not-a-number does not compare equal to itself and so cannot be asserted with an equality
    /// comparison. Oracle: <c>iif.srf:L74-L79</c>, whose body performs no comparison at all.
    /// </remarks>
    [Fact]
    public void IifPassesNotANumberThroughUnchangedForTheDoubleOverload()
    {
        Assert.True(double.IsNaN(Text.Iif(true, double.NaN, 1d)));
        Assert.True(double.IsNaN(Text.Iif(false, 1d, double.NaN)));
    }

    // ==========================================================================================
    // REGION 3 - OVERLOAD COVERAGE. One row per legacy prototype, nine rows, nine overloads.
    // ==========================================================================================
    //
    // C-K - WHY THIS TABLE IS SHAPED THE WAY IT IS
    // The nine overloads take nine DIFFERENT types, so a single theory cannot pass their arguments
    // through its own parameter list: the compiler would have to pick one overload for all nine
    // rows, which is the very thing under test. The table therefore carries the legacy TYPE NAME as
    // its only theory datum - a plain string, so the theory data stays fully serializable and the
    // failing row names itself in the test output - and each name keys an entry that holds a
    // strongly-typed invoker. Each invoker calls ONE overload with arguments of that overload's own
    // type, so the C# compiler performs the overload selection AT COMPILE TIME, once per entry,
    // exactly as a real call site would. The invoker boxes its result, which is what lets the test
    // assert the runtime type that came back and so catch a silent widening.
    //
    // C-K - THE LEGACY-TO-C# TYPE MAPPING, WHICH IS NOT ALL OBVIOUS
    //     string   -> string?    a PowerScript string may be null, so the port is nullable both ways
    //     boolean  -> bool
    //     long     -> long       PowerBuilder `long` is the 32-bit type and `longlong` is the 64-bit
    //                            one, but iif.srf declares only `long`, which the plan maps to C#
    //                            `long`. Nothing is lost: every 32-bit value fits.
    //     integer  -> short      THE SURPRISING ROW. PowerBuilder `integer` is 16-BIT SIGNED, so it
    //                            maps to C# `short` and NOT to C# `int`. An `int` here would be a
    //                            silent widening of the legacy surface.
    //     date     -> DateOnly   a calendar date with no time component and no zone
    //     time     -> TimeOnly   a time of day with no date component and no zone
    //     datetime -> DateTime   PowerBuilder has no time-zone concept at all, which is why every
    //                            DateTime below is constructed as DateTimeKind.Unspecified rather
    //                            than as Utc or Local. Choosing either would invent a zone the
    //                            oracle does not have.
    //     decimal  -> decimal    base-10, NOT double. See Region 4 for why that is load-bearing.
    //     double   -> double

    /// <summary>
    /// One legacy <c>iif</c> prototype, paired with the C# overload it maps to.
    /// </summary>
    private sealed class OverloadCase
    {
        /// <summary>Gets the CLR type the overload is expected to return.</summary>
        public required Type ExpectedClrType { get; init; }

        /// <summary>Gets the boxed value the invoker offers as the true part.</summary>
        public required object TruePart { get; init; }

        /// <summary>Gets the boxed value the invoker offers as the false part.</summary>
        public required object FalsePart { get; init; }

        /// <summary>
        /// Gets a delegate that calls exactly one <c>Iif</c> overload with
        /// <see cref="TruePart"/> and <see cref="FalsePart"/>, and boxes what comes back.
        /// </summary>
        public required Func<bool, object?> Invoke { get; init; }
    }

    /// <summary>
    /// The nine legacy prototypes of <c>iif.srf:L7-L15</c>, each paired with its C# overload.
    /// Exposed as a read-only view so the table cannot be mutated by a test.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, OverloadCase> OverloadCases =
        new Dictionary<string, OverloadCase>(StringComparer.Ordinal)
        {
            // iif.srf:L7 prototype, iif.srf:L18-L23 body.
            ["string"] = new OverloadCase
            {
                ExpectedClrType = typeof(string),
                TruePart = "true-part",
                FalsePart = "false-part",
                Invoke = static condition => Text.Iif(condition, "true-part", "false-part"),
            },

            // iif.srf:L8 prototype, iif.srf:L25-L30 body.
            ["boolean"] = new OverloadCase
            {
                ExpectedClrType = typeof(bool),
                TruePart = true,
                FalsePart = false,
                Invoke = static condition => Text.Iif(condition, true, false),
            },

            // iif.srf:L9 prototype, iif.srf:L32-L37 body. The extreme values would not fit any
            // narrower overload, so this row also proves the 64-bit width really is carried.
            ["long"] = new OverloadCase
            {
                ExpectedClrType = typeof(long),
                TruePart = long.MaxValue,
                FalsePart = long.MinValue,
                Invoke = static condition => Text.Iif(condition, long.MaxValue, long.MinValue),
            },

            // iif.srf:L10 prototype, iif.srf:L39-L44 body. PowerBuilder `integer` is 16-bit signed,
            // so the expected CLR type is Int16. This is the row that catches a silent narrowing
            // fix-up, in either direction: an `int` overload would report Int32 here.
            ["integer"] = new OverloadCase
            {
                ExpectedClrType = typeof(short),
                TruePart = short.MaxValue,
                FalsePart = short.MinValue,
                Invoke = static condition => Text.Iif(condition, short.MaxValue, short.MinValue),
            },

            // iif.srf:L11 prototype, iif.srf:L46-L51 body. A leap day is used as the true part so a
            // date coerced through a lossy intermediate representation would not survive.
            ["date"] = new OverloadCase
            {
                ExpectedClrType = typeof(DateOnly),
                TruePart = new DateOnly(2024, 2, 29),
                FalsePart = new DateOnly(1999, 12, 31),
                Invoke = static condition =>
                    Text.Iif(condition, new DateOnly(2024, 2, 29), new DateOnly(1999, 12, 31)),
            },

            // iif.srf:L12 prototype, iif.srf:L53-L58 body.
            ["time"] = new OverloadCase
            {
                ExpectedClrType = typeof(TimeOnly),
                TruePart = new TimeOnly(23, 59, 59),
                FalsePart = new TimeOnly(0, 0, 0),
                Invoke = static condition =>
                    Text.Iif(condition, new TimeOnly(23, 59, 59), new TimeOnly(0, 0, 0)),
            },

            // iif.srf:L13 prototype, iif.srf:L60-L65 body. Unspecified kind, because PowerBuilder
            // `datetime` carries no zone and inventing one would not be behaviour preservation.
            ["datetime"] = new OverloadCase
            {
                ExpectedClrType = typeof(DateTime),
                TruePart = new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Unspecified),
                FalsePart = new DateTime(1999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
                Invoke = static condition => Text.Iif(
                    condition,
                    new DateTime(2024, 2, 29, 23, 59, 59, DateTimeKind.Unspecified),
                    new DateTime(1999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified)),
            },

            // iif.srf:L14 prototype, iif.srf:L67-L72 body. See Region 4 for the fidelity assertions
            // that a `double` substitution would fail; this row only pins type and selection.
            ["decimal"] = new OverloadCase
            {
                ExpectedClrType = typeof(decimal),
                TruePart = 123.45m,
                FalsePart = -67.89m,
                Invoke = static condition => Text.Iif(condition, 123.45m, -67.89m),
            },

            // iif.srf:L15 prototype, iif.srf:L74-L79 body.
            ["double"] = new OverloadCase
            {
                ExpectedClrType = typeof(double),
                TruePart = 1.5d,
                FalsePart = 2.5d,
                Invoke = static condition => Text.Iif(condition, 1.5d, 2.5d),
            },
        };

    /// <summary>
    /// The nine legacy type names of <c>iif.srf:L7-L15</c>, in the order the prototypes declare
    /// them, used as the theory rows for <see cref="IifCoversEveryLegacyOverload"/>.
    /// </summary>
    public static TheoryData<string> LegacyOverloadTypes =>
        new()
        {
            "string",
            "boolean",
            "long",
            "integer",
            "date",
            "time",
            "datetime",
            "decimal",
            "double",
        };

    /// <summary>
    /// Verifies, one row per legacy prototype, that every <c>Iif</c> overload returns the expected
    /// CLR type and selects the expected branch.
    /// </summary>
    /// <param name="legacyType">The PowerScript type name naming the row under test.</param>
    [Theory]
    [MemberData(nameof(LegacyOverloadTypes))]
    public void IifCoversEveryLegacyOverload(string legacyType)
    {
        OverloadCase overloadCase = OverloadCases[legacyType];

        object? whenTrue = overloadCase.Invoke(true);
        object? whenFalse = overloadCase.Invoke(false);

        // The returned TYPE is asserted, not just the value. Without this, an implementation that
        // widened `short` to `int` or substituted `double` for `decimal` would still return a value
        // that compared equal, and the narrowing would ship unnoticed.
        Assert.Equal(overloadCase.ExpectedClrType, whenTrue?.GetType());
        Assert.Equal(overloadCase.ExpectedClrType, whenFalse?.GetType());

        // iif.srf:L18-L79 - `if abExpression then return <true part> else return <false part>`.
        Assert.Equal(overloadCase.TruePart, whenTrue);
        Assert.Equal(overloadCase.FalsePart, whenFalse);
    }

    /// <summary>
    /// Verifies that the coverage table above still describes the whole of the legacy surface, and
    /// that <see cref="Text"/> still declares exactly the nine overloads the oracle declares.
    /// </summary>
    /// <remarks>
    /// This is the assertion that turns "a missing overload is a finding to report, not a test to
    /// omit" into something the build enforces rather than something a reader has to notice.
    /// <c>iif.srf:L7-L15</c> declares nine prototypes and <c>iif.srf:L18-L79</c> nine bodies, so
    /// nine is the number: a tenth overload widens the surface beyond the oracle, and an eighth
    /// drops a legacy call shape. Either way this test goes red and the discrepancy gets decided
    /// deliberately instead of silently.
    /// </remarks>
    [Fact]
    public void IifDeclaresExactlyTheNineLegacyOverloads()
    {
        const int legacyPrototypeCount = 9;

        Assert.Equal(legacyPrototypeCount, OverloadCases.Count);
        Assert.Equal(legacyPrototypeCount, LegacyOverloadTypes.Count);

        int declaredOverloads = typeof(Text)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Count(method =>
                string.Equals(method.Name, nameof(Text.Iif), StringComparison.Ordinal));

        Assert.Equal(legacyPrototypeCount, declaredOverloads);
    }

    // ==========================================================================================
    // REGION 4 - decimal FIDELITY.
    // Oracle: iif.srf:L14 prototype, iif.srf:L67-L72 body.
    // ==========================================================================================
    //
    // C-K - WHY THESE TWO ASSERTIONS EXIST AT ALL
    // The plan maps PowerBuilder `dec` and `decimal(n)` to C# `decimal` SPECIFICALLY to avoid a
    // binary floating-point substitution. A row that merely round-tripped 1.5 would assert nothing,
    // because `double` would pass it too. Both assertions below are therefore chosen to be values a
    // `double` implementation CANNOT reproduce: one needs more significant digits than a double
    // carries, and the other needs base-10 scale, which a double has no concept of.

    /// <summary>
    /// Verifies that the <see cref="decimal"/> overload round-trips a value carrying more
    /// significant digits than a <see cref="double"/> can represent.
    /// </summary>
    [Fact]
    public void IifPreservesDecimalPrecisionBeyondBinaryFloatingPointRange()
    {
        // 28 significant digits, which is inside decimal's range and far outside double's.
        const decimal highPrecision = 123456789012345678901234567.8m;

        Assert.Equal(highPrecision, Text.Iif(true, highPrecision, 0m));
        Assert.Equal(highPrecision, Text.Iif(false, 0m, highPrecision));

        // This is what makes the assertions above meaningful rather than decorative: routed through
        // a double, the same value does not come back. An implementation that substituted `double`
        // for `decimal` would therefore fail the two assertions above rather than passing them by
        // accident, which is exactly the property the plan's type mapping is protecting.
        Assert.NotEqual(highPrecision, (decimal)(double)highPrecision);
    }

    /// <summary>
    /// Verifies that the <see cref="decimal"/> overload preserves the base-10 scale of its
    /// argument, including trailing zeros.
    /// </summary>
    /// <remarks>
    /// A <see cref="decimal"/> carries its scale as part of its representation, so <c>1.50m</c> and
    /// <c>1.5m</c> compare equal yet render differently. A <see cref="double"/> has no scale at all
    /// and would render <c>1.5</c>, so this assertion is a second, independent way of catching a
    /// binary floating-point substitution. The comparison is made through the invariant culture so
    /// the host locale cannot influence the result.
    /// </remarks>
    [Fact]
    public void IifPreservesDecimalScaleIncludingTrailingZeros()
    {
        decimal selected = Text.Iif(true, 1.50m, 9.99m);

        Assert.Equal("1.50", selected.ToString(CultureInfo.InvariantCulture));
    }

    // ==========================================================================================
    // REGION 5 - OVERLOAD RESOLUTION ACROSS THE NUMERIC WIDTHS.
    // ==========================================================================================

    /// <summary>
    /// Pins which numeric overload the C# compiler selects for the three argument shapes a ported
    /// call site can have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K. Having both a <see cref="short"/> overload (from PowerBuilder <c>integer</c>) and a
    /// <see cref="long"/> overload means C# has a genuine choice to make, and which one it makes is
    /// OBSERVABLE at the call site through the type of the result. That makes it behaviour rather
    /// than a detail, so it is measured here rather than assumed. The three results, and the rule
    /// behind each:
    /// </para>
    /// <para>
    /// <c>Iif(condition, 1, 2)</c> selects <see cref="short"/>. An <see cref="int"/> CONSTANT that
    /// fits in a <see cref="short"/> converts to one implicitly, and <see cref="short"/> is the most
    /// specific applicable target because it converts to every other candidate while none converts
    /// back to it.
    /// </para>
    /// <para>
    /// <c>Iif(condition, 100000, 2)</c> selects <see cref="long"/>. 100000 does not fit a
    /// <see cref="short"/>, so that overload is not applicable at all and resolution falls to
    /// <see cref="long"/>, the most specific of what remains.
    /// </para>
    /// <para>
    /// <c>Iif(condition, intVariable, otherVariable)</c> selects <see cref="long"/>. A NON-CONSTANT
    /// <see cref="int"/> has no implicit conversion to <see cref="short"/>, so the 16-bit overload
    /// drops out. This is the shape almost every ported call site will have, which makes
    /// <see cref="long"/> the practical default and confines the <see cref="short"/> selection to
    /// literal arguments. It is recorded rather than smoothed over: adding an <see cref="int"/>
    /// overload would put a tenth member on a surface the oracle defines as nine.
    /// </para>
    /// </remarks>
    [Fact]
    public void IifOverloadResolutionAcrossNumericWidths()
    {
        object? fromInRangeConstants = Text.Iif(true, 1, 2);
        object? fromOutOfRangeConstant = Text.Iif(true, 100000, 2);

        int firstVariable = 1;
        int secondVariable = 2;
        object? fromNonConstantIntegers = Text.Iif(true, firstVariable, secondVariable);

        Assert.Equal(typeof(short), fromInRangeConstants?.GetType());
        Assert.Equal(typeof(long), fromOutOfRangeConstant?.GetType());
        Assert.Equal(typeof(long), fromNonConstantIntegers?.GetType());
    }

    // ==========================================================================================
    // REGION 6 - ReplaceAll. PRESENT FOR THE COVERAGE GATE, NOT AS THIS SUITE'S OBLIGATION.
    // Oracle: ws_objects/pfw.common.pbl.src/replaceall.srf:L7-L9 (the three prototypes).
    // ==========================================================================================
    //
    // WHY THIS REGION IS IN THIS FILE AT ALL, STATED PLAINLY SO THE SCOPE DECISION IS AUDITABLE
    // `Text.Iif` is the NAMED OBLIGATION of this suite, and Regions 1 to 5 above discharge it.
    // `Text.ReplaceAll` and `Text.ClassNameEx` are NOT that obligation, so they are here only
    // because the C-H coverage gate for the PowerFramework.Shared.Kernel assembly cannot otherwise
    // be met. That was MEASURED rather than assumed: with Regions 1 to 5 alone,
    // `dotnet test -c Release --collect:"XPlat Code Coverage"` reported Text.cs at 27 of 71
    // coverable lines, 38.0 percent, with every one of the 44 uncovered lines inside ReplaceAll or
    // ClassNameEx. Because the test folder's file set is one suite per implementation file, this is
    // the ONLY file that can cover Text.cs - no sibling suite owns it - so the gap could not be
    // closed anywhere else. The coverage is therefore added HERE, in this same file, rather than in
    // a new tenth file, which would have invented a member of a fixed file set.
    //
    // The tests below deliberately keep the names the implementation refers to by name in its own
    // header (the I-1 through I-6 inferred-choice notes at Text.cs:L88-L98, plus
    // Text.cs:L506 and Text.cs:L556). Renaming any of them would leave Text.cs pointing at a test
    // that does not exist.
    //
    // WHAT THE ORACLE CAN AND CANNOT SETTLE HERE. `replaceall.srf:L3` binds this function to the
    // closed pfw.dll and no C++ source exists anywhere in the repository, so there is NO readable
    // body - unlike `iif`, whose body is right there at L18-L79. Only the three prototypes at L7-L9
    // and the in-repo call sites are evidence. Each assertion below is therefore labelled with
    // whether it pins a behaviour DERIVED from the call-site corpus or an INFERRED choice made where
    // the corpus is silent, matching the D-1..D-3 and I-1..I-6 labels the implementation uses, so a
    // later characterization run against the behavioural oracle can revise exactly the inferences
    // and leave the derived facts alone.

    /// <summary>
    /// The substitution matrix for the five-argument form at <c>replaceall.srf:L9</c>, which is the
    /// arity the three-argument and four-argument forms both delegate to.
    /// </summary>
    /// <remarks>
    /// Rows are grouped by the behaviour they pin, and each group names its evidence label.
    /// </remarks>
    public static TheoryData<string?, string?, string?, bool, bool, string> ReplaceAllCases =>
        new()
        {
            // DERIVED D-1: one left-to-right pass, and inserted text is NEVER rescanned. Each of
            // these replacements CONTAINS its own search text, so a rescanning implementation could
            // not terminate. These are the dominant legacy call shapes: a line feed expanded to a
            // carriage-return pair, a quote doubled for SQL, and a backslash escaped.
            { "a\nb\nc", "\n", "\r\n", true, false, "a\r\nb\r\nc" },
            { "it's", "'", "''", true, false, "it''s" },
            { "\\", "\\", "\\\\", true, false, "\\\\" },

            // Ordinary multi-occurrence substitution, and the no-match case that returns the source.
            { "aXbXcX", "X", "-", true, false, "a-b-c-" },
            { "abc", "z", "y", true, false, "abc" },
            { "abc", "abc", "abc", true, false, "abc" },

            // DERIVED D-2: matchCase is case sensitivity, and both settings compare ORDINALLY.
            { "Abc abc", "abc", "X", true, false, "Abc X" },
            { "Abc abc", "abc", "X", false, false, "X X" },

            // INFERRED I-3: a null or empty source yields the empty string rather than throwing.
            { null, "a", "b", true, false, "" },
            { "", "a", "b", true, false, "" },

            // INFERRED I-4: a null or empty search text returns the source unchanged. The empty
            // case is also what guarantees termination - an empty search text would match at every
            // position without consuming a character.
            { "abc", null, "x", true, false, "abc" },
            { "abc", "", "x", true, false, "abc" },

            // INFERRED I-5: a null replacement behaves as the empty string, deleting occurrences.
            { "aXbXc", "X", null, true, false, "abc" },

            // DERIVED D-3 and INFERRED I-2: the keyword form replaces WHOLE TOKENS only. A token
            // continuation character is anything that is neither white space nor one of the legacy
            // expression parser's own delimiters, so the start and the end of the string are both
            // boundaries, and so are white space and every delimiter.
            { "$a", "$a", "1", true, true, "1" },
            { "x $a y", "$a", "1", true, true, "x 1 y" },
            { "($a)", "$a", "1", true, true, "(1)" },
            { "$a:1", "$a", "1", true, true, "1:1" },
            { "$a+$a", "$a", "1", true, true, "1+1" },

            // ...and the prefix collision the boundary rule exists to prevent: a short name must not
            // match inside a longer one. Letters, digits, the underscore and the dot all continue a
            // token, so none of these four is a whole-token match.
            { "$a + $ab", "$a", "1", true, true, "1 + $ab" },
            { "$a_b", "$a", "1", true, true, "$a_b" },
            { "$a1", "$a", "1", true, true, "$a1" },
            { "$a.b", "$a", "1", true, true, "$a.b" },

            // ...and a left-edge rejection whose retry then runs off the end of the string, which is
            // the other of the two ways the scan can conclude that nothing matched.
            { "x$a", "$a", "1", true, true, "x$a" },

            // The keyword flag set to false is defined to be identical to the four-argument form.
            { "$a + $ab", "$a", "1", true, false, "1 + 1b" },
        };

    /// <summary>
    /// Verifies the five-argument substitution behaviour at <c>replaceall.srf:L9</c>.
    /// </summary>
    /// <param name="source">The text to search.</param>
    /// <param name="searchText">The text to look for.</param>
    /// <param name="replacementText">The text to substitute.</param>
    /// <param name="matchCase">Whether the comparison is case sensitive.</param>
    /// <param name="keywordReplace">Whether matches are restricted to whole tokens.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [MemberData(nameof(ReplaceAllCases))]
    public void ReplaceAllProducesTheExpectedSubstitution(
        string? source,
        string? searchText,
        string? replacementText,
        bool matchCase,
        bool keywordReplace,
        string expected)
    {
        Assert.Equal(
            expected,
            Text.ReplaceAll(source, searchText, replacementText, matchCase, keywordReplace));
    }

    /// <summary>
    /// Pins inferred choice I-1: the three-argument form at <c>replaceall.srf:L7</c> defaults to a
    /// case-sensitive comparison.
    /// </summary>
    /// <remarks>
    /// The prototype does not state what this arity uses for case sensitivity, so it had to be
    /// chosen. The corpus supports the choice without settling it: all three call sites of this arity
    /// escape a backslash, where case cannot matter, and of the 57 call sites that pass the flag
    /// explicitly, every one passes case sensitive and none passes case insensitive. The second
    /// assertion is the one that makes this test meaningful - it states what the default is NOT.
    /// </remarks>
    [Fact]
    public void ReplaceAllThreeArgumentOverloadDefaultsToCaseSensitiveInferred()
    {
        Assert.Equal("Abc X", Text.ReplaceAll("Abc abc", "abc", "X"));
        Assert.NotEqual("X X", Text.ReplaceAll("Abc abc", "abc", "X"));

        // The three-argument form must agree with the four-argument form asked for case sensitivity,
        // which is what proves it delegates rather than reimplementing.
        Assert.Equal(
            Text.ReplaceAll("Abc abc", "abc", "X", true),
            Text.ReplaceAll("Abc abc", "abc", "X"));
    }

    /// <summary>
    /// Pins inferred choice I-2: the token boundary definition the keyword form applies.
    /// </summary>
    /// <remarks>
    /// The boundary definition is reconstructed from the legacy expression parser that is this
    /// parameter's only consumer, not read from an implementation. A character CONTINUES a token
    /// unless it is white space or one of the parser's delimiters, so the macro sigils continue a
    /// token while the arithmetic and grouping characters break one. The four-argument form, which
    /// applies no boundary rule at all, is asserted alongside each case so the difference the flag
    /// makes is visible rather than implied.
    /// </remarks>
    [Fact]
    public void ReplaceAllKeywordBoundaryDefinitionInferred()
    {
        // A delimiter on both sides is a boundary, so the match is a whole token.
        Assert.Equal("(1)", Text.ReplaceAll("($a)", "$a", "1", true, true));

        // A letter on the right is not a boundary, so this is not a whole token...
        Assert.Equal("$ab", Text.ReplaceAll("$ab", "$a", "1", true, true));

        // ...whereas without the flag the same call corrupts the longer name.
        Assert.Equal("1b", Text.ReplaceAll("$ab", "$a", "1", true, false));

        // The underscore continues a token, so an identifier is not split.
        Assert.Equal("$a_b", Text.ReplaceAll("$a_b", "$a", "1", true, true));

        // White space is not a token character, so it is a boundary on both edges.
        Assert.Equal("x 1 y", Text.ReplaceAll("x $a y", "$a", "1", true, true));
    }

    /// <summary>
    /// Pins inferred choice I-2 for the case that matters most: the keyword form is what stops
    /// static expansion from silently rewriting a dynamic reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The static macro form is a SUBSTRING of the dynamic form, at offset one. Static expansion
    /// substitutes a variable's value at bind time and is applied through this function, whereas a
    /// dynamic reference must survive untouched so it can be resolved later at calculation time. A
    /// boundary rule that ignored the character BEFORE a match would rewrite every dynamic reference
    /// while statically expanding the static one, silently converting a deferred reference into a
    /// fixed value.
    /// </para>
    /// <para>
    /// C-B. That distinction is the static-versus-dynamic expansion contract the plan names one of
    /// the two hardest in the whole refactor, so this test is not a boundary-condition curiosity: it
    /// is the guard on a behaviour the refactor is explicitly required to preserve. The contrast
    /// assertion shows the corruption that occurs without the flag, so the flag's purpose cannot be
    /// mistaken for a micro-optimisation and removed.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReplaceAllKeywordReplaceProtectsDynamicFromStaticExpansion()
    {
        // The dynamic form is left completely alone by a static expansion of the static form.
        Assert.Equal("$$name", Text.ReplaceAll("$$name", "$name", "5", true, true));

        // Without the flag, the same substitution corrupts the dynamic form into a fixed value.
        Assert.Equal("$5", Text.ReplaceAll("$$name", "$name", "5", true, false));

        // And in one expression carrying both forms, the static one expands and the dynamic one
        // survives - which is the whole behaviour, in a single assertion.
        Assert.Equal("5 + $$name", Text.ReplaceAll("$name + $$name", "$name", "5", true, true));
    }

    /// <summary>
    /// Pins inferred choice I-3: a null source yields the empty string rather than throwing or
    /// propagating a null.
    /// </summary>
    /// <remarks>
    /// The legacy is a native function, and a native PowerBuilder function handed a null string does
    /// not raise. Returning the empty string keeps that posture while also guaranteeing the
    /// documented "never returns null" contract.
    /// </remarks>
    [Fact]
    public void ReplaceAllNullHandlingInferred()
    {
        Assert.Equal(string.Empty, Text.ReplaceAll(null, "a", "b"));
        Assert.Equal(string.Empty, Text.ReplaceAll(null, "a", "b", true));
        Assert.Equal(string.Empty, Text.ReplaceAll(null, "a", "b", true, true));
        Assert.Equal(string.Empty, Text.ReplaceAll(null, null, null, false, false));
    }

    /// <summary>
    /// Pins inferred choice I-4: a null or empty search text returns the source unchanged.
    /// </summary>
    /// <remarks>
    /// This arm is also what guarantees termination. An empty search text matches at every position
    /// without consuming a character, so a scan that accepted it would never advance.
    /// </remarks>
    [Fact]
    public void ReplaceAllEmptySearchTextReturnsSourceUnchangedInferred()
    {
        Assert.Equal("abc", Text.ReplaceAll("abc", "", "x"));
        Assert.Equal("abc", Text.ReplaceAll("abc", null, "x"));
        Assert.Equal("abc", Text.ReplaceAll("abc", "", "x", false));
        Assert.Equal("abc", Text.ReplaceAll("abc", null, "x", true, true));
    }

    /// <summary>
    /// Pins inferred choice I-5: a null replacement behaves as the empty string, so occurrences are
    /// deleted rather than the call failing.
    /// </summary>
    [Fact]
    public void ReplaceAllNullReplacementDeletesOccurrencesInferred()
    {
        Assert.Equal("abc", Text.ReplaceAll("aXbXc", "X", null));
        Assert.Equal("abc", Text.ReplaceAll("aXbXc", "X", null, true));
        Assert.Equal(string.Empty, Text.ReplaceAll("XX", "X", null, true, false));

        // Deleting is not the same as returning the source: the empty replacement really is applied.
        Assert.NotEqual("aXbXc", Text.ReplaceAll("aXbXc", "X", null));
    }

    /// <summary>
    /// Pins inferred choice I-6: <c>ReplaceAll</c> never throws and never returns null, on any
    /// arity and for any combination of degenerate arguments.
    /// </summary>
    /// <remarks>
    /// Every call below completing at all is the "never throws" half of the assertion; the
    /// null checks are the other half. The legacy contract this preserves is that a native
    /// PowerBuilder string function does not raise on degenerate input.
    /// </remarks>
    [Fact]
    public void ReplaceAllNeverThrowsInferred()
    {
        Assert.NotNull(Text.ReplaceAll(null, null, null));
        Assert.NotNull(Text.ReplaceAll(string.Empty, string.Empty, string.Empty));
        Assert.NotNull(Text.ReplaceAll(null, string.Empty, null, false));
        Assert.NotNull(Text.ReplaceAll(string.Empty, null, string.Empty, true, true));
        Assert.NotNull(Text.ReplaceAll("source", null, null, false, true));
        Assert.NotNull(Text.ReplaceAll("source", "source", null, true, true));
    }

    /// <summary>
    /// Verifies that a dollar sign in the replacement is inserted literally.
    /// </summary>
    /// <remarks>
    /// C-K. This is the assertion that pins the decision NOT to implement <c>ReplaceAll</c> over a
    /// regular expression. In a regex replacement the dollar sign is a substitution sigil, and that
    /// matters enormously here rather than being a theoretical nicety: the dollar sign is the column
    /// expression engine's own macro sigil, so the values this function is asked to substitute
    /// routinely begin with one. A hand-rolled ordinal scan cannot get the escaping wrong because it
    /// never interprets either string; a regex implementation would need a literal-safe replacement
    /// and would silently corrupt these inputs if it forgot.
    /// </remarks>
    [Fact]
    public void ReplaceAllInsertsDollarSignLiterally()
    {
        Assert.Equal("total = $0$1", Text.ReplaceAll("total = value", "value", "$0$1"));
        Assert.Equal("$$", Text.ReplaceAll("x", "x", "$$"));
        Assert.Equal("$name", Text.ReplaceAll("placeholder", "placeholder", "$name", true, true));
    }

    // ==========================================================================================
    // REGION 7 - ClassNameEx. ALSO PRESENT FOR THE COVERAGE GATE ONLY.
    // Oracle: ws_objects/pfw.shared.pbl.src/classnameex.srf:L13-L27 (the two-stage body).
    // ==========================================================================================
    //
    // WHAT THE LEGACY DID, so these assertions can be judged against it. `classnameex.srf:L13`
    // calls the built-in class-name function inside a try whose catch block is entirely empty.
    // `:L17` tests the result for empty or null, and if it is, `:L19-L20` DELIBERATELY PROVOKES A
    // RUNTIME ERROR - the `if` body is empty, and the statement exists only so that comparing the
    // value against a number fails - after which `:L22-L24` SCRAPES the type name out of the thrown
    // error's message text, between a colon and the next comma. `:L29` returns, possibly the empty
    // string, and the function never throws because both stages sit inside a try.
    //
    // C-K. The scrape depends on the exact wording, punctuation and field order of a PowerBuilder
    // runtime error message. Nothing in .NET produces that message, so the trick is SUBSTITUTED by
    // the runtime type's unqualified name rather than translated. The substitution is honest about
    // being MORE reliable than the original: where the legacy's two-stage dance failed twice and
    // returned the empty string, this returns a correct name. The empty result is still preserved for
    // the one input that can produce it.

    /// <summary>
    /// Verifies that a null value still yields the empty string, preserving the one input for which
    /// the legacy's "may return empty, never throws, never returns null" contract is observable.
    /// </summary>
    [Fact]
    public void ClassNameExReturnsTheEmptyStringForNull()
    {
        Assert.Equal(string.Empty, Text.ClassNameEx(null));
    }

    /// <summary>
    /// Verifies that the substitute answers uniformly for the kinds of value the legacy two-stage
    /// body struggled with.
    /// </summary>
    /// <remarks>
    /// The enumerated-type case is the important one. The changelog records at
    /// <c>logfile.md:L377-L378</c> that this function was added so that the type of ANY variable
    /// could be obtained, INCLUDING ENUMERATED TYPES, which is precisely what the built-in class-name
    /// function could not answer for and why the provoked-error scrape existed at all. The changelog
    /// is stale and is not a specification, so it is cited only as evidence of intent; the behaviour
    /// asserted here comes from <c>classnameex.srf</c> itself. The name is UNQUALIFIED, matching the
    /// shape the legacy returned rather than a namespace-qualified one.
    /// </remarks>
    [Fact]
    public void ClassNameExReturnsTheUnqualifiedRuntimeTypeName()
    {
        Assert.Equal("String", Text.ClassNameEx("text"));
        Assert.Equal("Int32", Text.ClassNameEx(42));
        Assert.Equal("Int16", Text.ClassNameEx((short)42));
        Assert.Equal("Decimal", Text.ClassNameEx(1.5m));
        Assert.Equal("Boolean", Text.ClassNameEx(true));

        // The enumerated type - the case that the legacy's second stage existed to reach.
        Assert.Equal("DayOfWeek", Text.ClassNameEx(DayOfWeek.Monday));

        // A reference type, named unqualified rather than with its namespace.
        Assert.Equal("TextTests", Text.ClassNameEx(this));
    }

    /// <summary>
    /// Verifies the one .NET-specific result that has no legacy counterpart: a constructed generic
    /// type reports the runtime's arity suffix.
    /// </summary>
    /// <remarks>
    /// Called out so it is not later mistaken for a defect. The legacy had no generics, so there is
    /// no legacy behaviour to compare against here, and the name deliberately does NOT name the type
    /// argument.
    /// </remarks>
    [Fact]
    public void ClassNameExReportsTheAritySuffixForAConstructedGenericType()
    {
        Assert.Equal("List`1", Text.ClassNameEx(new List<string>()));
    }
}
