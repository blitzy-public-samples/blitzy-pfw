// ==================================================================================================
//  AssertGuardTests.cs - THE SIX ASSERTION GUARDS, AND THE TRI-STATE HOLE THEY INHERIT
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Diagnostics.Assertions - the six Assert guard overloads
//  ORACLES           ws_objects/pfw.common.pbl.src/assert.srf:L89-L127   the six guards themselves
//                    ws_objects/pfw.common.pbl.src/assert.srf:L16-L87    AssertFailed, the producer
//                    ws_objects/pfw.shared.pbl.src/issucceeded.srf       the numeric guard predicate
//                    ws_objects/pfw.shared.pbl.src/isvalidobject.srf     the object guard predicate
//                    ws_objects/pfw.shared.pbl.src/retcode.sru           the constant catalogue
//                    ws_objects/pfw.tests.pbl.src/w_test_assert.srw      the behavioural oracle
//
//  Every path above is READ ONLY (C-C). They are the specification and the parity oracle, never a
//  build or run time input: this suite constructs every fixture in code and opens no file at all.
//
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE IS FOR, IN ONE SENTENCE
//  ------------------------------------------------------------------------------------------------
//  The guards are two statements each - an early return on a satisfied condition, then a delegation
//  to AssertFailed - so the only thing that can be got wrong is WHICH VALUES SATISFY THE CONDITION.
//  This suite is the table that answers that, value by value, for all three argument shapes and both
//  arities, and it exists because the answer is COUNTER-INTUITIVE in three places.
//
//  ------------------------------------------------------------------------------------------------
//  THE HEADLINE OBLIGATION: THE TRI-STATE HOLE REACHES OUT OF Kernel AND INTO Diagnostics
//  ------------------------------------------------------------------------------------------------
//  The numeric guard is `if IsSucceeded(rtcode) then return` [assert.srf:L101,L107], and the success
//  predicate's test is `rtCode >= RetCode.OK` [issucceeded.srf:L12] after a null guard that answers
//  false [issucceeded.srf:L11]. Three consequences follow, all three of which look like bugs, and
//  ALL THREE OF WHICH ARE DELIBERATELY PRESERVED LEGACY BEHAVIOUR (C-B):
//
//      1. Assert(RetCode.PREVENT) NEVER FIRES. PREVENT is 1 [retcode.sru:L42], which satisfies
//         `>= 0`, so a value whose whole meaning is "stop" is classified as a success and the
//         assertion is silently skipped. This is the named case in this file's requirement, and the
//         row that pins it carries its own comment saying so.
//
//      2. Assert(RetCode.CANCELLED) DOES FIRE - even though the same algebra's IsFailed explicitly
//         EXCLUDES cancelled from failure. A value that is officially not a failure therefore still
//         trips an assertion. The hole is resolved towards firing here, and towards not-a-failure
//         there. That asymmetry is not reconciled; it is recorded.
//
//      3. Assert((long?)null) DOES FIRE, because the predicate answers false on null. Null is the
//         THIRD state of the algebra - neither succeeded nor failed - and the guard nonetheless
//         treats it as assert-worthy. AAP 0.4.5.4 is explicit that collapsing null to zero in the
//         port would turn "neither" into "succeeded" and silently disable the guard, which is why
//         the parameter is `long?` and why a null row appears in the matrix below.
//
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  IT DOES NOT RE-TEST Kernel's PREDICATES. Predicates.IsSucceeded and Predicates.IsValidObject are
//  owned, exhaustively, by shared/PowerFramework.Shared.Kernel.Tests/PredicatesTests.cs. Everything
//  below asserts the CONSEQUENCE AT THE GUARD - whether the assertion fires - and never the predicate
//  in isolation. The single place a predicate is read directly is the cancelled-asymmetry fact, where
//  the predicate's answer is quoted as the CONTEXT that makes the asymmetry visible; even there the
//  thing being asserted is the guard's behaviour, and the predicate's own parity is not duplicated.
//
//  IT DOES NOT ASSERT THE PAYLOAD'S SUFFIX OR ITS FIELD LAYOUT. These guards run against a REAL
//  captured stack, so a failure's Info normally ends with the source-location suffix
//  `"\nat " + Object + "::" + ObjectEvent + "(" + Line + ")"` [assert.srf:L71]. Info assertions below
//  are therefore made on the PREFIX, or on the line-feed-delimited segments, and never by full
//  equality. The neighbouring responsibilities live elsewhere and are NOT duplicated here:
//
//      AssertionFailureTests          the carrier itself - defaults, mutability, SetMessage, the
//                                     shadowed StackTrace, and full-equality assertions that involve
//                                     the location suffix
//      AssertPayloadProtocolTests     the two payload SHAPES - the seven-field deep form and the
//                                     two-field shallow form - and the CRLF field protocol
//
//  ------------------------------------------------------------------------------------------------
//  WHY THE CLASS UNDER TEST IS CALLED Assertions AND NOT Assert (C-K)
//  ------------------------------------------------------------------------------------------------
//  The legacy PowerBuilder function object is named `assert` and declares a subroutine also named
//  `assert` [assert.srf:L3,L8], so the legacy-visible contract reads `Assert.Assert(...)`. C# CANNOT
//  EXPRESS THAT: a class may not contain a member whose name matches the class name unless the member
//  is a constructor, which is compiler error CS0542, and the restriction applies to static members
//  too. The port therefore renamed the CONTAINING TYPE to Assertions - role-named and plural, exactly
//  what AAP 0.4.5.2 prescribes for a `*.srf` global function and exactly the convention the sibling
//  Kernel project already set with Predicates, Bits, Formatting, Text and Ancestry - while PRESERVING
//  the legacy-visible member names Assert and AssertFailed and the file name Assert.cs.
//
//  That rename is also what lets this file exist at all. This suite sits in namespace
//  PowerFramework.Shared.Diagnostics.Tests, so the enclosing PowerFramework.Shared.Diagnostics
//  namespace is in scope, and a type named Assert there would collide with Xunit.Assert on every
//  single line below. The bare identifier `Assert` in this file is therefore Xunit's, unambiguously,
//  and the unit under test is always spelled `Assertions`.
//
//  ------------------------------------------------------------------------------------------------
//  RULES POSITION, STATED SO IT IS NOT MISTAKEN FOR AN OMISSION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided." NO USER RULE GOVERNS THIS FILE. Nothing is
//  invented or back-filled from convention in their place, and the absence is not read as licence to
//  lower the bar. The binding constraints are AAP 0.7.2, the enterprise-standard baseline, and
//  AAP 0.7.3, the twelve non-rule constraints. Four bind this file and are cited where they apply:
//
//      C-B  Replicate legacy behaviour, including defects; correct nothing. Every expectation below
//           that looks wrong carries a comment saying it is deliberately preserved legacy behaviour,
//           so a future reader cannot mistake it for a mistake and "fix" it.
//      C-C  The legacy tree is read only and is the only specification. See the oracle list above.
//      C-H  80 percent line coverage per project, measured from coverage.cobertura.xml. All SIX guard
//           overloads are exercised on BOTH branches and in BOTH arities; six times two is the floor,
//           and the matrices below exceed it.
//      C-K  Name the technology-specific decisions in the tests that cover them. Three are named: the
//           CS0542 rename above, the bare-null overload binding, and the destroyed-object state that
//           has no managed analogue.
//
//  NAMING. The repository-root .editorconfig scopes its CA1707 and IDE1006 suppressions to ten NAMED
//  PRODUCTION files and does not cover test files. No identifier declared here is therefore
//  SCREAMING_SNAKE, and every legacy constant VALUE is referenced through
//  PowerFramework.Shared.Kernel.RetCode rather than retyped as a literal - a retyped literal would
//  silently survive a constant-value regression, which is the one failure this matrix must catch.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Characterization tests for the six <c>Assert</c> guard overloads on
/// <see cref="Assertions"/>.
/// </summary>
/// <remarks>
/// Table-driven throughout, as AAP 0.6.7 prescribes: the parity matrices are theories fed from
/// member data, so a value's expected outcome is readable as a row rather than buried in a body.
/// </remarks>
public class AssertGuardTests
{
    /// <summary>
    /// Payload field 2's fixed opening text, before any info continuation or location suffix
    /// [assert.srf:L24].
    /// </summary>
    private const string BareAssertionText = "Assertion failed";

    /// <summary>
    /// The separator used INSIDE a payload field: a bare line feed, never CRLF [assert.srf:L26,L71].
    /// </summary>
    private const string IntraFieldSeparator = "\n";

    /// <summary>
    /// The payload FIELD delimiter [assert.srf:L19]. It must never appear inside
    /// <see cref="AssertionFailure.Info"/>, which is a single field.
    /// </summary>
    private const string FieldDelimiter = "\r\n";

    /// <summary>
    /// The opening of the source-location suffix the deep payload shape appends to
    /// <see cref="AssertionFailure.Info"/> [assert.srf:L71].
    /// </summary>
    private const string LocationSuffixOpening = "at ";

    /// <summary>
    /// The oracle's own info string [w_test_assert.srw:L42], used verbatim so the suite mirrors a
    /// real legacy scenario rather than an invented one.
    /// </summary>
    private const string OracleInfo = "Invalid Number!";

    /// <summary>Case key for the <c>bool</c> guard family.</summary>
    private const string BooleanFamily = "boolean";

    /// <summary>Case key for the <c>long?</c> guard family.</summary>
    private const string NumericFamily = "numeric";

    /// <summary>Case key for the <c>object?</c> guard family.</summary>
    private const string ObjectFamily = "object";

    /// <summary>
    /// The three guard families, which every info-propagation theory below runs across so that no
    /// family can be left with an untested arity.
    /// </summary>
    public static TheoryData<string> GuardFamilies =>
        new(BooleanFamily, NumericFamily, ObjectFamily);

    // ==============================================================================================
    //  1. THE NUMERIC GUARD - THE INHERITED TRI-STATE HOLE
    // ==============================================================================================

    /// <summary>
    /// The numeric parity matrix: a return code, and whether the guard fires for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read as a single statement: the guard fires for exactly the values
    /// <see cref="Predicates.IsSucceeded(long?)"/> rejects, which is every negative value and
    /// <see langword="null"/>, and for nothing else.
    /// </para>
    /// <para>
    /// Every legacy value is referenced through <see cref="RetCode"/> rather than retyped, so a
    /// change to any constant's VALUE moves the matrix with it instead of leaving a stale literal
    /// that still passes. The only bare literals are the three values chosen precisely BECAUSE they
    /// are outside the catalogue - two positive and one large negative - and they carry no legacy
    /// meaning to drift away from.
    /// </para>
    /// </remarks>
    public static TheoryData<long?, bool> NumericGuardMatrix
    {
        get
        {
            TheoryData<long?, bool> matrix = [];

            // ---------- DOES NOT FIRE: the guard returns normally ----------

            // The zero code [retcode.sru:L39]. ONE ROW PER DISTINCT VALUE, DELIBERATELY.
            //
            // Its two alias spellings SUCCESS and ALLOW [retcode.sru:L40-L41] are the same number, so
            // rows for them would be byte-identical to this one - and the xunit runner assigns a
            // theory row's identity from its arguments and SKIPS a row whose identity duplicates an
            // earlier one. Adding the aliases here would therefore produce rows that are reported as
            // present and never actually execute, which is precisely the vacuous pass this matrix
            // exists to prevent. Every legacy spelling is instead exercised, and executed, by
            // EveryLegacySpellingOfTheZeroCodeAndOfCancelledReachesTheSameGuardOutcome below. The
            // same reasoning applies to CANCELED and CANCELLED in the firing half.
            matrix.Add(RetCode.OK, false);

            // PRESERVED LEGACY BEHAVIOUR, DELIBERATE - DO NOT "FIX" THIS ROW (C-B).
            //
            // Assert(RetCode.PREVENT) DOES NOT FIRE. This is the named case in this suite's
            // requirement and the single most counter-intuitive expectation in the file.
            //
            // MECHANISM. The guard tests the SUCCESS predicate, not equality with the zero code
            // [assert.srf:L101,L107]. The success predicate admits ANY value at or above zero,
            // because its test is `rtCode >= RetCode.OK` and not `rtCode = RetCode.OK`
            // [issucceeded.srf:L12]. PREVENT is 1 [retcode.sru:L42]. One is at or above zero.
            // A PREVENTION IS THEREFORE CLASSIFIED AS A SUCCESS AND THE ASSERTION IS SILENTLY
            // SKIPPED - even though a prevention's entire meaning is "stop".
            //
            // This is legacy behaviour reproduced on purpose. It is NOT an oversight in the port and
            // it is NOT a gap in this matrix. It must not be repaired by narrowing the guard to
            // `== RetCode.OK`, by special-casing PREVENT, or by flipping this row to true.
            matrix.Add(RetCode.PREVENT, false);

            // Positive values beyond the constant catalogue, for the same reason and by the same
            // mechanism: `>=` admits every one of them. Deliberately bare literals - they are chosen
            // BECAUSE no legacy constant names them.
            matrix.Add(1_000_000L, false);
            matrix.Add(long.MaxValue, false);

            // ---------- DOES FIRE: AssertionFailure is thrown ----------

            // The failed code [retcode.sru:L43]. The one intuitive row in this half of the table.
            matrix.Add(RetCode.FAILED, true);

            // PRESERVED LEGACY BEHAVIOUR, DELIBERATE (C-B). Cancelled FIRES.
            //
            // Why this is a defect-pinning row and not an ordinary one: the algebra's own IsFailed
            // EXPLICITLY EXCLUDES cancelled from failure, so cancelled is neither succeeded nor
            // failed. The guard nonetheless fires on it. See the dedicated fact below, which records
            // that asymmetry rather than reconciling it.
            //
            // One row again, for the runner-identity reason given at the zero code above: CANCELED and
            // CANCELLED are the same number [retcode.sru:L44-L45], so a second row would be collapsed
            // and skipped. BOTH SPELLINGS ARE STILL EXERCISED - in the alias fact below - because AAP
            // 0.4.5.3 requires both identifiers to survive the port verbatim and a compile-time
            // reference to each is what catches a rename of either.
            matrix.Add(RetCode.CANCELLED, true);

            // The contiguous negative error block [retcode.sru:L46-L76], sampled at its FIRST member,
            // one from the middle, and its LAST member. The endpoints matter: a block that grew or
            // shrank at either end would leave a gap that a middle-only sample cannot see.
            matrix.Add(RetCode.E_INVALID_ARGUMENT, true);
            matrix.Add(RetCode.E_OUT_OF_MEMORY, true);
            matrix.Add(RetCode.E_RETRY, true);

            // The two sentinels that sit far below the contiguous block [retcode.sru:L77-L78].
            matrix.Add(RetCode.E_NO_SUPPORT, true);
            matrix.Add(RetCode.E_NO_IMPLEMENTATION, true);

            // The unknown sentinel [retcode.sru:L79].
            matrix.Add(RetCode.UNKNOWN, true);

            // A large negative outside the catalogue. Bare literal for the same reason as its
            // positive counterpart above: it is deliberately unnamed by any constant.
            matrix.Add(long.MinValue, true);

            // PRESERVED LEGACY BEHAVIOUR, DELIBERATE (C-B). NULL FIRES.
            //
            // This is the THIRD STATE of the return-code algebra reaching Diagnostics. The success
            // predicate answers false on a null input [issucceeded.srf:L11] and the failure predicate
            // answers false on it too, so null is NEITHER SUCCEEDED NOR FAILED - and yet the guard
            // treats it as assert-worthy, because the guard asks only whether the value SUCCEEDED.
            //
            // The parameter is `long?` and not `long` precisely so this row is expressible. AAP
            // 0.4.5.4 is explicit that collapsing null to zero would convert "neither" into
            // "succeeded" and SILENTLY DISABLE THE ASSERTION - a regression no compiler diagnostic
            // and no row-count assertion would report. Do not remove this row and do not narrow the
            // parameter.
            matrix.Add(null, true);

            return matrix;
        }
    }

    /// <summary>
    /// The numeric guard fires for exactly the codes the success predicate rejects, in BOTH arities.
    /// </summary>
    /// <param name="code">The return code under test, or <see langword="null"/>.</param>
    /// <param name="expectedToFire">
    /// <see langword="true"/> when the guard must throw <see cref="AssertionFailure"/>;
    /// <see langword="false"/> when it must return normally.
    /// </param>
    /// <remarks>
    /// <para>
    /// Each row is driven through <see cref="Assertions.Assert(long?)"/> AND
    /// <see cref="Assertions.Assert(long?, string)"/>, because the two overloads must agree: they
    /// differ only in the info string they forward [assert.srf:L103 versus :L109], so a divergence
    /// between them could only be an implementation error. Driving both from one table is also what
    /// covers both branches of both overloads without a second matrix that could drift from this one.
    /// </para>
    /// <para>
    /// The local is declared <c>long?</c> so that overload resolution is decided by the DECLARED type
    /// and not by the literal at the call site. That matters: a bare <c>null</c> literal binds to this
    /// same overload, but only by the better-conversion-target rule, and a matrix that relied on that
    /// would be asserting overload resolution rather than guard behaviour. The dedicated binding fact
    /// below asserts resolution on purpose; this theory does not.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NumericGuardMatrix))]
    public void TheNumericGuardFiresForExactlyTheCodesTheSuccessPredicateRejects(
        long? code,
        bool expectedToFire)
    {
        if (expectedToFire)
        {
            AssertionFailure infoLess = Assert.Throws<AssertionFailure>(() => Assertions.Assert(code));
            AssertionFailure infoCarrying =
                Assert.Throws<AssertionFailure>(() => Assertions.Assert(code, OracleInfo));

            // The info-less arity appends nothing; the info-carrying arity appends the caller's text.
            // Prefix assertions only - the real captured stack contributes the location suffix, whose
            // exact content AssertionFailureTests owns.
            Assert.StartsWith(BareAssertionText, infoLess.Info, StringComparison.Ordinal);
            Assert.StartsWith(
                BareAssertionText + IntraFieldSeparator + OracleInfo,
                infoCarrying.Info,
                StringComparison.Ordinal);
            return;
        }

        Assert.Null(Record.Exception(() => Assertions.Assert(code)));
        Assert.Null(Record.Exception(() => Assertions.Assert(code, OracleInfo)));
    }

    /// <summary>
    /// Every legacy spelling of the zero code, and both legacy spellings of cancelled, reach the same
    /// guard outcome - and every one of them is actually executed here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS FACT EXISTS RATHER THAN MORE MATRIX ROWS. The runner derives a theory row's identity
    /// from its arguments, so two rows carrying the same value are the same row and the second is
    /// SKIPPED. Aliases are by definition the same value, so alias rows in
    /// <see cref="NumericGuardMatrix"/> would be reported as present and never run. A fact has no such
    /// identity collapse, so all five spellings below genuinely execute.
    /// </para>
    /// <para>
    /// WHAT IT PROTECTS. AAP 0.4.5.3 requires the legacy identifier spellings to survive the port
    /// verbatim, because they appear in serialized payloads, log records and characterization
    /// recordings where a rename would silently invalidate every stored comparison. A compile-time
    /// reference to each spelling is what turns a rename into a build failure, so the value of this
    /// fact is as much in the five identifiers it MENTIONS as in the calls it makes.
    /// </para>
    /// <para>
    /// WHAT IT DELIBERATELY DOES NOT DUPLICATE. The catalogue's own value parity - that
    /// <see cref="RetCode.SUCCESS"/> and <see cref="RetCode.ALLOW"/> really are zero and that
    /// <see cref="RetCode.CANCELED"/> really equals <see cref="RetCode.CANCELLED"/> - is owned by
    /// <c>shared/PowerFramework.Shared.Kernel.Tests/RetCodeTests.cs</c>. What is asserted here is only
    /// the GUARD's outcome for each spelling, which is this suite's subject.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLegacySpellingOfTheZeroCodeAndOfCancelledReachesTheSameGuardOutcome()
    {
        // The three spellings of the zero code [retcode.sru:L39-L41]: none fires, in either arity.
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.OK)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.OK, OracleInfo)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.SUCCESS)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.SUCCESS, OracleInfo)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.ALLOW)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.ALLOW, OracleInfo)));

        // Both spellings of cancelled [retcode.sru:L44-L45]: both fire, in either arity. Preserved
        // legacy behaviour - see the asymmetry fact immediately below for why firing is the surprise.
        Assert.Throws<AssertionFailure>(() => Assertions.Assert((long?)RetCode.CANCELED));
        Assert.Throws<AssertionFailure>(() => Assertions.Assert((long?)RetCode.CANCELED, OracleInfo));
        Assert.Throws<AssertionFailure>(() => Assertions.Assert((long?)RetCode.CANCELLED));
        Assert.Throws<AssertionFailure>(() => Assertions.Assert((long?)RetCode.CANCELLED, OracleInfo));
    }

    /// <summary>
    /// Cancelled FIRES at the guard even though the algebra's own failure predicate EXCLUDES it from
    /// failure. The asymmetry is recorded, not reconciled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY BEHAVIOUR, DELIBERATE (C-B). This fact exists because it is the reason this
    /// whole suite is necessary. A reader who knows only that
    /// <see cref="Predicates.IsFailed(long?)"/> answers <see langword="false"/> for
    /// <see cref="RetCode.CANCELLED"/> would reasonably conclude that an assertion cannot fire on a
    /// value that is not a failure. IT DOES. The guard asks whether the code SUCCEEDED, not whether
    /// it FAILED [assert.srf:L101], and cancelled answers false to the first question while also
    /// answering false to the second. It falls through the hole in the middle, and the hole is
    /// resolved towards firing here and towards not-a-failure there.
    /// </para>
    /// <para>
    /// The failure predicate is READ here purely as the CONTEXT that makes the asymmetry visible. Its
    /// own parity - the explicit cancelled exclusion, the null handling, the boolean overloads - is
    /// owned in full by <c>shared/PowerFramework.Shared.Kernel.Tests/PredicatesTests.cs</c> and is
    /// deliberately not duplicated here. The thing this fact ASSERTS is the guard's behaviour.
    /// </para>
    /// <para>
    /// Do not "harmonise" this by teaching the guard about cancellation, and do not delete the fact
    /// because it looks contradictory. The contradiction is the legacy's, faithfully carried across.
    /// </para>
    /// </remarks>
    [Fact]
    public void CancelledFiresTheGuardAlthoughTheAlgebraDoesNotCallItAFailure()
    {
        // CONTEXT, not the assertion's subject: the algebra does not classify cancelled as a failure.
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));

        // THE ASSERTION: the guard fires on it anyway, in both arities.
        Assert.Throws<AssertionFailure>(() => Assertions.Assert((long?)RetCode.CANCELLED));
        Assert.Throws<AssertionFailure>(
            () => Assertions.Assert((long?)RetCode.CANCELLED, OracleInfo));

        // And the mirror image, which completes the picture: PREVENT is not a success in any ordinary
        // reading, yet the guard lets it through. Both halves of the hole in one place.
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.PREVENT)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.PREVENT, OracleInfo)));
    }

    /// <summary>
    /// A bare <c>null</c> literal binds to the <c>long?</c> overload and NOT to the <c>object?</c>
    /// one. Recorded because the binding is silent and the two overloads are not interchangeable in
    /// meaning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TECHNOLOGY-SPECIFIC DECISION, NAMED AS C-K REQUIRES. Given overloads over <c>bool</c>,
    /// <c>long?</c> and <c>object?</c>, <c>Assertions.Assert(null)</c> is neither ambiguous nor a
    /// compile error: <c>null</c> has no conversion to non-nullable <c>bool</c>, and between
    /// <c>long?</c> and <c>object?</c> the better-conversion-target rule prefers <c>long?</c>,
    /// because <c>long?</c> converts to <c>object?</c> while the reverse does not.
    /// </para>
    /// <para>
    /// The OBSERVABLE OUTCOME is identical either way - both predicates answer
    /// <see langword="false"/> on null, so both overloads fire - which is exactly why this needs
    /// asserting rather than leaving to a behavioural test: no behavioural test can tell the two
    /// apart. The binding is read off the compiler's own resolution through an expression tree, so
    /// the assertion is about what the COMPILER chose and not about what runs.
    /// </para>
    /// <para>
    /// Callers who mean the object guard must say so with <c>Assert((object?)null)</c>. Every call
    /// site in this file spells its intended overload explicitly for that reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABareNullLiteralBindsToTheNumericOverloadAndNotTheObjectOverload()
    {
        Expression<Action> oneArgument = () => Assertions.Assert(null);
        Expression<Action> twoArguments = () => Assertions.Assert(null, OracleInfo);

        Assert.Equal(
            typeof(long?),
            ((MethodCallExpression)oneArgument.Body).Method.GetParameters()[0].ParameterType);
        Assert.Equal(
            typeof(long?),
            ((MethodCallExpression)twoArguments.Body).Method.GetParameters()[0].ParameterType);

        // And it fires, so the silent binding is at least not a silent PASS. Both overloads would
        // fire on null; that is the point of the paragraph above, and it is asserted here so the
        // outcome is pinned alongside the binding.
        Assert.Throws<AssertionFailure>(() => Assertions.Assert(null));
        Assert.Throws<AssertionFailure>(() => Assertions.Assert(null, OracleInfo));
    }

    // ==============================================================================================
    //  2. THE BOOLEAN GUARD
    // ==============================================================================================

    /// <summary>
    /// The boolean parity matrix: a condition, and whether the guard fires for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole of the legacy guard is <c>if condition then return</c> [assert.srf:L89,L95], so the
    /// table is complete at two rows: the domain of <c>bool</c> has exactly two inhabitants and both
    /// are here. A matrix that is exhaustive rather than sampled needs no justification for what it
    /// left out.
    /// </para>
    /// <para>
    /// NO NULL ROW EXISTS, AND THAT IS A SIGNATURE FACT RATHER THAN AN OMISSION (C-K). The ported
    /// parameter is non-nullable <c>bool</c> [Assert.cs, <c>Assert(bool condition)</c>], so a null
    /// condition IS NOT REPRESENTABLE at this call site and cannot be passed. In PowerScript it was:
    /// a null boolean makes <c>if condition then return</c> fall through, so the legacy would have
    /// fired. That case is unreachable through the ported signature, so NO ROW IS FABRICATED FOR IT -
    /// inventing one would mean either changing the signature to accommodate a test or writing a test
    /// that cannot compile. The legacy null-condition path is reachable only through the numeric
    /// guard, whose parameter IS nullable and whose matrix does carry a null row.
    /// </para>
    /// </remarks>
    public static TheoryData<bool, bool> BooleanGuardMatrix =>
        new()
        {
            // A satisfied condition returns immediately [assert.srf:L89].
            { true, false },

            // An unsatisfied condition falls through to the failure [assert.srf:L91].
            { false, true },
        };

    /// <summary>
    /// The boolean guard fires on <see langword="false"/> and returns silently on
    /// <see langword="true"/>, in BOTH arities.
    /// </summary>
    /// <param name="condition">The condition under test.</param>
    /// <param name="expectedToFire">
    /// <see langword="true"/> when the guard must throw; <see langword="false"/> when it must return.
    /// </param>
    /// <remarks>
    /// This is the only guard family with live production callers in the in-scope estate - the
    /// Persistence threading proxies use the info-carrying arity - and the oracle exercises both
    /// arities, at [w_test_assert.srw:L39] without info and [w_test_assert.srw:L42] with it. Both are
    /// driven from every row for the same reason the numeric theory does: the two overloads differ
    /// only in the string they forward, so any divergence between them is an implementation error.
    /// </remarks>
    [Theory]
    [MemberData(nameof(BooleanGuardMatrix))]
    public void TheBooleanGuardFiresOnFalseAndReturnsSilentlyOnTrue(bool condition, bool expectedToFire)
    {
        if (expectedToFire)
        {
            AssertionFailure infoLess =
                Assert.Throws<AssertionFailure>(() => Assertions.Assert(condition));
            AssertionFailure infoCarrying =
                Assert.Throws<AssertionFailure>(() => Assertions.Assert(condition, OracleInfo));

            Assert.StartsWith(BareAssertionText, infoLess.Info, StringComparison.Ordinal);
            Assert.StartsWith(
                BareAssertionText + IntraFieldSeparator + OracleInfo,
                infoCarrying.Info,
                StringComparison.Ordinal);
            return;
        }

        Assert.Null(Record.Exception(() => Assertions.Assert(condition)));
        Assert.Null(Record.Exception(() => Assertions.Assert(condition, OracleInfo)));
    }

    // ==============================================================================================
    //  3. THE OBJECT GUARD
    // ==============================================================================================

    /// <summary>
    /// The object parity matrix, keyed by case name: which reference shape is under test, and whether
    /// the guard fires for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows carry a CASE KEY rather than the instance itself, and the instance is built inside the
    /// test from <see cref="ResolveObjectCase(string)"/>. Two reasons, both practical: a theory row
    /// must be serializable for the runner to name and re-run it, and an <c>object</c> row is not
    /// reliably serializable; and a key gives each row a readable name in the test output, which
    /// <c>System.Object</c> would not.
    /// </para>
    /// <para>
    /// The non-firing side deliberately spans a plain object, a string, an empty string, a boxed
    /// integer, a boxed boolean, a boxed return code and an exception instance. That spread exists to
    /// prove the guard is NOT TYPE-SENSITIVE and NOT VALUE-SENSITIVE: the legacy tests reference
    /// validity and nothing else [isvalidobject.srf:L15-L16], so an empty string, a boxed zero and a
    /// boxed <see cref="RetCode.FAILED"/> are all perfectly valid objects and none of them fires. The
    /// boxed failure code is the sharpest of those: routed through the OBJECT guard it passes, while
    /// the same value routed through the NUMERIC guard fires. The three guards are not
    /// interchangeable, and this row is where that is pinned.
    /// </para>
    /// <para>
    /// THE DESTROYED-OBJECT STATE IS OUT OF THIS SUITE'S REACH, AND IS NOT INVENTED (C-K).
    /// PowerBuilder's <c>IsValid</c> also answers false for an object that was created and has since
    /// been <c>Destroy</c>ed - a reference that is non-null yet unusable. .NET has no such state: a
    /// managed reference is either null or points at an object the garbage collector guarantees is
    /// alive. <see cref="Predicates.IsValidObject(object?)"/> accordingly defines exactly one
    /// condition, <c>value is not null</c>, and models NO disposed-or-invalid sentinel of any kind.
    /// This matrix therefore asserts only what that predicate actually defines. There is no
    /// destroyed-object row, no disposed-object row and no probe for one, because there is no
    /// behaviour there to assert - and a row that disposed something and expected a fire would be
    /// asserting invented behaviour rather than ported behaviour.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool> ObjectGuardMatrix =>
        new()
        {
            // DOES FIRE. Null is the entire firing condition of this family
            // [isvalidobject.srf:L15, assert.srf:L113,L119].
            { "null", true },

            // DOES NOT FIRE. Any live reference satisfies the guard, whatever its type or value.
            { "plain-object", false },
            { "non-empty-string", false },
            { "empty-string", false },
            { "boxed-int-zero", false },
            { "boxed-bool-false", false },
            { "boxed-failed-code", false },
            { "exception-instance", false },
        };

    /// <summary>
    /// The object guard fires on <see langword="null"/> and returns silently for any live reference,
    /// in BOTH arities.
    /// </summary>
    /// <param name="caseKey">The case key, resolved by <see cref="ResolveObjectCase(string)"/>.</param>
    /// <param name="expectedToFire">
    /// <see langword="true"/> when the guard must throw; <see langword="false"/> when it must return.
    /// </param>
    [Theory]
    [MemberData(nameof(ObjectGuardMatrix))]
    public void TheObjectGuardFiresOnNullAndReturnsSilentlyForAnyLiveReference(
        string caseKey,
        bool expectedToFire)
    {
        // Declared object? so that overload resolution is decided by the declared type, exactly as in
        // the numeric theory, and not by whatever the resolver happened to return.
        object? instance = ResolveObjectCase(caseKey);

        if (expectedToFire)
        {
            AssertionFailure infoLess =
                Assert.Throws<AssertionFailure>(() => Assertions.Assert(instance));
            AssertionFailure infoCarrying =
                Assert.Throws<AssertionFailure>(() => Assertions.Assert(instance, OracleInfo));

            Assert.StartsWith(BareAssertionText, infoLess.Info, StringComparison.Ordinal);
            Assert.StartsWith(
                BareAssertionText + IntraFieldSeparator + OracleInfo,
                infoCarrying.Info,
                StringComparison.Ordinal);
            return;
        }

        Assert.Null(Record.Exception(() => Assertions.Assert(instance)));
        Assert.Null(Record.Exception(() => Assertions.Assert(instance, OracleInfo)));
    }

    // ==============================================================================================
    //  4. INFO PROPAGATION ACROSS BOTH ARITIES, FOR ALL THREE FAMILIES
    // ==============================================================================================
    //  WHAT IS ASSERTED, AND WHAT IS DELIBERATELY LEFT TO ANOTHER SUITE
    //  --------------------------------------------------------------------------------------------
    //  The producer builds payload field 2 as the fixed text "Assertion failed" [assert.srf:L24] and
    //  then appends a BARE LINE FEED plus the caller's info ONLY IF that info is not the empty string
    //  [assert.srf:L25-L27]. Field 2 is copied into Info [assert.srf:L35], and in the deep payload
    //  shape a source-location suffix is appended to Info afterwards [assert.srf:L71].
    //
    //  The guards below run against a REAL captured stack, so that suffix is normally present. Every
    //  assertion here is therefore made on the PREFIX of Info, or on its line-feed-delimited
    //  SEGMENTS, and never by full equality. FULL-EQUALITY ASSERTIONS THAT INVOLVE THE SUFFIX BELONG
    //  TO AssertionFailureTests, and THE TWO PAYLOAD SHAPES BELONG TO AssertPayloadProtocolTests.
    //  Neither is duplicated here; this section owns only the question of whether the caller's info
    //  reached field 2, and how.
    //
    //  THE SHALLOW SHAPE IS TOLERATED RATHER THAN REQUIRED. The suffix appears only when the frame
    //  count exceeds two [assert.srf:L37]; a failed capture leaves it at zero and produces a payload
    //  with no suffix at all. Assertions below accept either shape - a segment count of one or two for
    //  the info-less arity, two or three for the info-carrying one - so this suite pins the info
    //  contract without accidentally also pinning stack-capture depth, which is not its subject.
    //
    //  LINE FEED, NEVER CRLF. The info separator is a bare line feed and the FIELD delimiter is CRLF
    //  [assert.srf:L19,L26]. Info is a single field, so the delimiter must not appear inside it - a
    //  CRLF there would split one field into two and defeat the consumer's exactly-seven-fields test.
    //  Every case below asserts the delimiter is absent.
    //
    //  A NULL INFO IS NOT TESTED, BECAUSE IT IS NOT REPRESENTABLE (C-K). The ported parameter is
    //  non-nullable `string info` in all three info-carrying overloads, so null is not a legal
    //  argument and no row is fabricated for it. For the record, the legacy test is `info <> ""`
    //  [assert.srf:L25] and a PowerScript comparison against a null string yields null rather than
    //  true, so the legacy would have suppressed the continuation for null exactly as it does for the
    //  empty string - but that path is unreachable through the ported signature, and the port
    //  deliberately reproduces the empty-string test rather than a null-or-whitespace test.
    // ==============================================================================================

    /// <summary>
    /// The info-less arity produces a failure whose detail text is exactly the bare assertion text,
    /// with nothing appended by the caller.
    /// </summary>
    /// <param name="family">The guard family under test.</param>
    /// <remarks>
    /// The legacy one-argument forms delegate with the EMPTY string [assert.srf:L91,L103,L115], and the
    /// producer's append is guarded on a non-empty info [assert.srf:L25], so no continuation line
    /// exists. Asserted three ways: the first segment is the bare text exactly; any second segment is
    /// the location suffix rather than an info line; and there is no third segment for an info line to
    /// hide in.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuardFamilies))]
    public void TheInfoLessArityAppendsNothingToTheBareAssertionText(string family)
    {
        AssertionFailure failure = Assert.Throws<AssertionFailure>(() => FireInfoLess(family));

        AssertNoContinuationLine(failure);
    }

    /// <summary>
    /// The rows for the info-carrying arity: a guard family paired with a non-empty info string.
    /// </summary>
    /// <remarks>
    /// The first info in every family is the ORACLE'S OWN STRING [w_test_assert.srw:L42], so the suite
    /// mirrors a real legacy scenario rather than only invented ones. The remaining strings probe that
    /// the info is forwarded VERBATIM: surrounding whitespace is not trimmed, a leading dash is not
    /// interpreted, and a string that happens to look like the location suffix is still treated as
    /// info. None contains a line feed or a CRLF, because either would legitimately change the segment
    /// count and this theory's subject is the separator the PRODUCER inserts, not one the caller does.
    /// </remarks>
    public static TheoryData<string, string> InfoCarryingMatrix
    {
        get
        {
            TheoryData<string, string> matrix = [];

            foreach (string family in new[] { BooleanFamily, NumericFamily, ObjectFamily })
            {
                matrix.Add(family, OracleInfo);
                matrix.Add(family, "  padded  ");
                matrix.Add(family, "-1");
                matrix.Add(family, LocationSuffixOpening + "not really a location");
            }

            return matrix;
        }
    }

    /// <summary>
    /// The info-carrying arity produces a failure whose detail text is the bare assertion text, then a
    /// SINGLE LINE FEED, then the caller's info verbatim.
    /// </summary>
    /// <param name="family">The guard family under test.</param>
    /// <param name="info">The non-empty info string the caller supplies.</param>
    /// <remarks>
    /// The separator is a BARE LINE FEED and not a carriage-return/line-feed pair [assert.srf:L26].
    /// That distinction is load-bearing rather than cosmetic: CRLF is the payload FIELD delimiter
    /// [assert.srf:L19], so a CRLF here would split field 2 in two and cost the consumer the window,
    /// the object, the event, the line number and the whole trace at once. Both halves are asserted -
    /// the line feed is present, and the delimiter is absent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InfoCarryingMatrix))]
    public void TheInfoCarryingArityAppendsOneLineFeedThenTheInfoVerbatim(string family, string info)
    {
        AssertionFailure failure = Assert.Throws<AssertionFailure>(() => FireWithInfo(family, info));

        // The prefix, asserted as one string so the separator's identity is part of the assertion.
        Assert.StartsWith(
            BareAssertionText + IntraFieldSeparator + info,
            failure.Info,
            StringComparison.Ordinal);

        // The field delimiter must not appear inside a single field.
        Assert.DoesNotContain(FieldDelimiter, failure.Info, StringComparison.Ordinal);

        string[] segments = failure.Info.Split(IntraFieldSeparator);

        // Two segments in the shallow payload shape, three in the deep one where the location suffix
        // follows. Never more: a fourth would mean an extra separator the producer does not insert.
        Assert.InRange(segments.Length, 2, 3);
        Assert.Equal(BareAssertionText, segments[0]);
        Assert.Equal(info, segments[1]);

        if (segments.Length == 3)
        {
            Assert.StartsWith(LocationSuffixOpening, segments[2], StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An EMPTY info string behaves exactly like the info-less arity: no trailing line feed is
    /// appended.
    /// </summary>
    /// <param name="family">The guard family under test.</param>
    /// <remarks>
    /// <para>
    /// The producer's append is guarded on <c>info &lt;&gt; ""</c> [assert.srf:L25], so the empty
    /// string is not merely a degenerate input - it is the NORMAL value the three info-less overloads
    /// forward [assert.srf:L91,L103,L115]. The two arities therefore converge on one observable
    /// result, and that convergence is asserted rather than assumed.
    /// </para>
    /// <para>
    /// The interesting failure this catches is an unconditional append: an implementation that always
    /// concatenated the separator would leave a trailing line feed here, adding an empty segment that
    /// no legacy payload has. That would be invisible to a test asserting only the prefix, which is
    /// why the segment count is asserted too.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(GuardFamilies))]
    public void AnEmptyInfoBehavesExactlyLikeTheInfoLessArity(string family)
    {
        AssertionFailure failure =
            Assert.Throws<AssertionFailure>(() => FireWithInfo(family, string.Empty));

        AssertNoContinuationLine(failure);
    }

    // ==============================================================================================
    //  5. THE SHAPE OF FAILURE ITSELF
    // ==============================================================================================

    /// <summary>
    /// The thrown type is exactly <see cref="AssertionFailure"/> - not a base type and not a derived
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exactness matters because the legacy consumer discriminates on the identity of the thrower
    /// [pfw.sra:L114] and the managed analogue of that test is catching this precise type. A thrown
    /// subclass, or a base <see cref="Exception"/>, would pass a lenient catch and then fail the
    /// consumer's discrimination.
    /// </para>
    /// <para>
    /// <c>Assert.Throws&lt;T&gt;</c> already demands the exact type, which is why every firing
    /// assertion in this file uses it rather than <c>ThrowsAny</c>. This fact states the requirement
    /// explicitly, once, so the choice is legible rather than incidental - and adds the runtime-type
    /// comparison and the base-type check that <c>Throws</c> does not make.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThrownTypeIsExactlyAssertionFailure()
    {
        AssertionFailure failure = Assert.Throws<AssertionFailure>(() => Assertions.Assert(false));

        Assert.Equal(typeof(AssertionFailure), failure.GetType());

        // It is a throwable, and its immediate base is Exception - so a lenient catch WOULD catch it.
        // That is precisely why the exact-type assertion above is the one that carries the contract.
        Assert.IsAssignableFrom<Exception>(failure);
        Assert.Equal(typeof(Exception), typeof(AssertionFailure).BaseType);
    }

    /// <summary>
    /// Every guard overload is a void subroutine, and failure is signalled ONLY by the throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy declares all seven members as <c>global subroutine</c> - procedures with no return
    /// value at all [assert.srf:L7-L13] - and that is a CONTRACT rather than an accident of
    /// PowerScript. A guard that also returned a code would give callers a second, silent way to
    /// respond to a failed assertion, which is the opposite of fail-fast.
    /// </para>
    /// <para>
    /// Asserted in four parts: there are exactly six <c>Assert</c> overloads; the parameter shapes are
    /// the three argument types in both arities, with <c>string</c> as the second parameter of each
    /// info-carrying form; every declared public member returns <c>void</c>, so there is no return
    /// code and no boolean result anywhere on the surface; and no member's name begins with
    /// <c>Try</c>, so there is no <c>TryAssert</c> variant offering a non-throwing alternative.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryGuardOverloadIsAVoidSubroutineAndFailureIsSignalledOnlyByTheThrow()
    {
        MethodInfo[] declared = DeclaredPublicStaticMethods();
        MethodInfo[] guards = GuardOverloads();

        Assert.Equal(6, guards.Length);

        // The six parameter shapes the legacy prototype list declares [assert.srf:L8-L13], as a set:
        // three argument types, each in an info-less and an info-carrying arity.
        Type[][] expectedShapes =
        [
            [typeof(bool)],
            [typeof(bool), typeof(string)],
            [typeof(long?)],
            [typeof(long?), typeof(string)],
            [typeof(object)],
            [typeof(object), typeof(string)],
        ];

        List<Type[]> actualShapes = guards
            .Select(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray())
            .ToList();

        Assert.Equal(expectedShapes.Length, actualShapes.Count);

        foreach (Type[] expected in expectedShapes)
        {
            Assert.Contains(actualShapes, actual => actual.SequenceEqual(expected));
        }

        // No return code, no boolean result, and in particular nothing returning the failure type -
        // the failure can only ever arrive by being thrown.
        Assert.All(declared, method => Assert.Equal(typeof(void), method.ReturnType));
        Assert.DoesNotContain(declared, method => method.ReturnType == typeof(AssertionFailure));

        // No non-throwing alternative is published under any name.
        Assert.DoesNotContain(
            declared,
            method => method.Name.StartsWith("Try", StringComparison.Ordinal));
    }

    /// <summary>
    /// The non-firing path has no observable effect: it does not throw, it cannot have constructed an
    /// <see cref="AssertionFailure"/>, and it does not reach the termination seam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NO THROW, for all six overloads with their conditions satisfied. That is the directly
    /// observable half.
    /// </para>
    /// <para>
    /// NO <see cref="AssertionFailure"/> CONSTRUCTED, proved deterministically rather than by
    /// measuring allocations - a byte-count assertion would be flaky, because ambient runtime
    /// bookkeeping allocates on some calls and not others, and a flaky assertion is worse than none.
    /// The deterministic argument has two premises, both asserted here: the ONLY published producer of
    /// an <see cref="AssertionFailure"/> is <see cref="Assertions.AssertFailed(string)"/>, since no
    /// declared member returns that type (asserted in the surface fact above); and
    /// <c>AssertFailed</c> carries <see cref="DoesNotReturnAttribute"/>, so it cannot return normally.
    /// A guard that RETURNED therefore provably never called it, and no failure was built.
    /// </para>
    /// <para>
    /// NO STATE TO DISTURB. <see cref="Assertions"/> declares no field of any accessibility, so
    /// repeated non-firing calls have nothing to mutate and no call can influence a later one. That is
    /// asserted rather than assumed, because a cached failure or a hit counter would be exactly the
    /// kind of well-meant addition this constraint forbids.
    /// </para>
    /// <para>
    /// THE TERMINATION SEAM IS NOT REACHED - not on the non-firing path, and not on the firing path
    /// either. The legacy consumer decodes the payload and then executes <c>HALT CLOSE</c>
    /// [pfw.sra:L143], but that terminating half belongs to the Gateway composition root and is
    /// deliberately NOT reproduced in this layer: this project RAISES and the composition root
    /// TERMINATES. The assertion is that a firing guard produces a CATCHABLE exception and execution
    /// continues afterwards - if the seam lived here, the process would be gone and the line after the
    /// catch would never run.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNonFiringPathHasNoObservableEffectAndNeitherPathTerminatesTheProcess()
    {
        object live = new();

        // All six overloads, conditions satisfied, both arities. RetCode.OK is referenced rather than
        // a bare zero so these calls track the constant if its value ever changes.
        Assert.Null(Record.Exception(() => Assertions.Assert(true)));
        Assert.Null(Record.Exception(() => Assertions.Assert(true, OracleInfo)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.OK)));
        Assert.Null(Record.Exception(() => Assertions.Assert((long?)RetCode.OK, OracleInfo)));
        Assert.Null(Record.Exception(() => Assertions.Assert((object?)live)));
        Assert.Null(Record.Exception(() => Assertions.Assert((object?)live, OracleInfo)));

        // Premise of the no-failure-constructed argument: the sole producer cannot return.
        MethodInfo assertFailed = typeof(Assertions).GetMethod(
            nameof(Assertions.AssertFailed),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string)])!;

        Assert.NotNull(assertFailed);
        Assert.Single(assertFailed.GetCustomAttributes<DoesNotReturnAttribute>(inherit: false));

        // And no guard carries that attribute, because every guard CAN return - which is the whole
        // point of there being a non-firing path at all.
        Assert.All(
            GuardOverloads(),
            guard => Assert.Empty(guard.GetCustomAttributes<DoesNotReturnAttribute>(inherit: false)));

        // Nothing to mutate: no fields of any accessibility, so the type is stateless by construction.
        const BindingFlags everyField = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance;

        Assert.Empty(typeof(Assertions).GetFields(everyField));

        // The termination seam is absent from this layer: a firing guard is catchable and execution
        // continues past the catch. If HALT CLOSE lived here, this method could not reach its end.
        bool reachedTheLineAfterTheCatch = false;

        try
        {
            Assertions.Assert(false);
        }
        catch (AssertionFailure)
        {
            reachedTheLineAfterTheCatch = true;
        }

        Assert.True(reachedTheLineAfterTheCatch);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Asserts that a failure's detail text carries the bare assertion text and NO caller-supplied
    /// continuation line, tolerating either payload shape.
    /// </summary>
    /// <param name="failure">The captured failure.</param>
    /// <remarks>
    /// Shared by the info-less arity and the empty-info cases precisely BECAUSE the two must agree
    /// [assert.srf:L25]. Holding the expectation in one helper means the two theories cannot drift
    /// into asserting subtly different things about the same required outcome.
    /// </remarks>
    private static void AssertNoContinuationLine(AssertionFailure failure)
    {
        Assert.StartsWith(BareAssertionText, failure.Info, StringComparison.Ordinal);
        Assert.DoesNotContain(FieldDelimiter, failure.Info, StringComparison.Ordinal);

        string[] segments = failure.Info.Split(IntraFieldSeparator);

        // One segment in the shallow payload shape, two in the deep one where the location suffix
        // follows. A third would mean a continuation line was appended, which is the failure this
        // asserts against.
        Assert.InRange(segments.Length, 1, 2);
        Assert.Equal(BareAssertionText, segments[0]);

        if (segments.Length == 2)
        {
            Assert.StartsWith(LocationSuffixOpening, segments[1], StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Fires the info-less overload of the named guard family, from a real frame of its own.
    /// </summary>
    /// <param name="family">One of the three family keys.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not a known key.</exception>
    /// <remarks>
    /// Marked <see cref="MethodImplOptions.NoInlining"/> for the same reason every capture helper in
    /// this project is: the payload names the frame two above the innermost [assert.srf:L38], so if
    /// the runtime inlined this method into its caller the reported frame would shift and the test
    /// would behave differently in a release build than in a debug one.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FireInfoLess(string family)
    {
        switch (family)
        {
            case BooleanFamily:
                Assertions.Assert(false);
                return;

            case NumericFamily:
                Assertions.Assert((long?)RetCode.FAILED);
                return;

            case ObjectFamily:
                Assertions.Assert((object?)null);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(family),
                    family,
                    "Unknown guard family key.");
        }
    }

    /// <summary>
    /// Fires the info-carrying overload of the named guard family, from a real frame of its own.
    /// </summary>
    /// <param name="family">One of the three family keys.</param>
    /// <param name="info">The info string to forward, which may be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not a known key.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FireWithInfo(string family, string info)
    {
        switch (family)
        {
            case BooleanFamily:
                Assertions.Assert(false, info);
                return;

            case NumericFamily:
                Assertions.Assert((long?)RetCode.FAILED, info);
                return;

            case ObjectFamily:
                Assertions.Assert((object?)null, info);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(family),
                    family,
                    "Unknown guard family key.");
        }
    }

    /// <summary>
    /// Resolves an object-guard case key to the reference the guard is to be given.
    /// </summary>
    /// <param name="caseKey">A key from <see cref="ObjectGuardMatrix"/>.</param>
    /// <returns>The reference under test, which is <see langword="null"/> for the null case.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="caseKey"/> is not a known key.</exception>
    /// <remarks>
    /// Each non-null case is a distinct reference SHAPE rather than a distinct value, because the
    /// guard's only question is validity [isvalidobject.srf:L15-L16]. The boxed cases exist to prove
    /// the guard does not consult the boxed value: a boxed zero, a boxed <see langword="false"/> and a
    /// boxed <see cref="RetCode.FAILED"/> are all valid references and none of them fires.
    /// </remarks>
    private static object? ResolveObjectCase(string caseKey)
    {
        return caseKey switch
        {
            "null" => null,
            "plain-object" => new object(),
            "non-empty-string" => "a live string",
            "empty-string" => string.Empty,
            "boxed-int-zero" => 0,
            "boxed-bool-false" => false,
            "boxed-failed-code" => RetCode.FAILED,
            "exception-instance" => new AssertionFailure(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(caseKey),
                caseKey,
                "Unknown object guard case key."),
        };
    }

    /// <summary>
    /// The public static members <see cref="Assertions"/> declares in its own right.
    /// </summary>
    /// <returns>The declared public static methods, excluding anything inherited.</returns>
    /// <remarks>
    /// <see cref="BindingFlags.DeclaredOnly"/> is essential: without it the query would also return
    /// <see cref="object"/>'s static members and the counts asserted above would be wrong for a reason
    /// that has nothing to do with the ported surface.
    /// </remarks>
    private static MethodInfo[] DeclaredPublicStaticMethods()
    {
        return typeof(Assertions).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
    }

    /// <summary>
    /// The six <c>Assert</c> guard overloads, isolated from the rest of the published surface.
    /// </summary>
    /// <returns>The declared public static methods named <c>Assert</c>.</returns>
    /// <remarks>
    /// The name is taken with <c>nameof</c> rather than written as a string literal, so renaming the
    /// member would break compilation here instead of silently reducing this query to zero results and
    /// letting a count assertion pass over an empty set.
    /// </remarks>
    private static MethodInfo[] GuardOverloads()
    {
        return DeclaredPublicStaticMethods()
            .Where(method => string.Equals(
                method.Name,
                nameof(Assertions.Assert),
                StringComparison.Ordinal))
            .ToArray();
    }
}
