// ==============================================================================================
//  PredicatesTests - the characterization suite for PowerFramework.Shared.Kernel.Predicates
//  --------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS, AND WHY ITS EXPECTATIONS LOOK WRONG
//
//  This is a CHARACTERIZATION suite, not a specification suite. It asserts what the ported code
//  ACTUALLY does, which is what the legacy ACTUALLY did - including where that is indefensible on
//  its own terms. Several expectations below are therefore deliberately "wrong": a prevention is
//  asserted to be a success, a cancellation is asserted not to be a failure, one predicate is
//  asserted to answer true for null while its five neighbours answer false, and two boolean
//  overloads are asserted to be indistinguishable from one another.
//
//  NONE OF THOSE IS A MISTAKE IN THIS FILE. Each is a legacy defect that the refactor's binding
//  constraint C-B requires be replicated and documented rather than corrected, and each carries an
//  annotation at the assertion itself naming the defect and citing the ws_objects locator that
//  proves it. If you are here because a predicate "looks broken", read the locator before changing
//  anything: the sequence that C-B exists to prevent is exactly "fix Predicates.cs, then fix this
//  test to match", which would silently delete behaviour that four services depend on.
//
//  PROVENANCE - SEVEN READ-ONLY LEGACY OBJECTS, ALL UNDER ws_objects/pfw.shared.pbl.src/
//  --------------------------------------------------------------------------------------------
//      issucceeded.srf     18 lines   2 overloads (long, boolean)
//      isfailed.srf        18 lines   2 overloads (long, boolean)
//      isprevented.srf     18 lines   2 overloads (long, boolean)
//      isallowed.srf       16 lines   2 overloads (long, boolean)
//      iscancelled.srf     11 lines   1 overload  (long)            <-- the odd one
//      isvalidobject.srf   18 lines   2 overloads (any, powerobject)
//      retcode.sru        221 lines   the constant catalogue the six compare against
//
//  ORACLE STATUS, AND THE ONE THING THAT MAKES THE LOCATORS LOAD-BEARING. Per constraint C-C the
//  legacy tree is READ ONLY and is the behavioural oracle: it is never edited, and it is never
//  opened at run time by this suite - every locator below appears in a COMMENT, never in a path,
//  a resource, a fixture or a file read. That matters more here than in most suites because
//  THERE IS NO LEGACY TEST WINDOW FOR THE KERNEL PRIMITIVES. Of the 47 w_test_*.srw windows in
//  ws_objects/pfw.tests.pbl.src/ not one exercises these six functions; there is no
//  w_test_predicates to compare against. The function BODY is therefore the entire specification,
//  and the locator is the entire provenance chain. An expectation here that cannot be traced to a
//  cited line is an expectation with no evidence behind it at all.
//
//  THE CONTRACT UNDER TEST - THE COMPLETE TRUTH TABLE
//  --------------------------------------------------------------------------------------------
//  Starred cells are the preserved defects. Every one is pinned by a named test below, so a
//  regression in any of them fails a test whose name states what was lost.
//
//      NUMERIC (long?)                     Succeeded  Failed  Prevented  Allowed  Cancelled
//      null                                  false     false    false     TRUE*     false
//      0      OK / SUCCESS / ALLOW           true      false    false     true      false
//      1      PREVENT                        TRUE*     false    true      false     false
//      -1     FAILED                         false     true     false     false     false
//      -2     CANCELED / CANCELLED           false     FALSE*   false     false     true
//      -3     E_INVALID_ARGUMENT             false     true     false     false     false
//      -33    E_RETRY                        false     true     false     false     false
//      -2000  E_NO_SUPPORT                   false     true     false     false     false
//      -2001  E_NO_IMPLEMENTATION            false     true     false     false     false
//      -4000  UNKNOWN                        false     true     false     false     false
//      256    SQLITE_OK_LOAD_PERMANENTLY     true      false    false     false     false
//      1000                                  true      false    false     FALSE*    false
//      1001                                  true      false    false     TRUE*     false
//
//      BOOLEAN (bool?)                     Succeeded  Failed  Prevented  Allowed
//      null                                  false     false    false     TRUE*
//      true                                  true      false    false     true
//      false                                 false     true     TRUE*     false
//
//  The three readings that matter, each of which has its own test below: the Allowed column
//  disagrees with every other column on null; the Failed column and the Succeeded column BOTH
//  reject -2, so that value is neither; and Failed and Prevented are identical in every row of
//  the boolean table while being cleanly distinct in the numeric one.
//
//  WHY THE NULL EXPECTATIONS ARE TEN SEPARATE FACTS AND NOT ONE TABLE  (constraint C-K)
//  --------------------------------------------------------------------------------------------
//  This is the single most important structural decision in the file, so it is stated rather than
//  left to be inferred from the layout.
//
//  Null handling is NON-UNIFORM across the six functions, and the asymmetry is the contract. Four
//  of them guard null explicitly to false; isallowed.srf has NO guard and folds IsNull into the
//  RESULT, so it answers TRUE; iscancelled.srf has no guard either and answers false only because
//  an unguarded comparison against null falls out that way. A single parameterised theory
//  asserting "every predicate returns false for null" would be shorter, would read as tidier, and
//  WOULD PASS AGAINST A WRONG IMPLEMENTATION - because the most likely real-world defect in this
//  port is precisely that someone copies the three-line null guard from IsSucceeded, IsFailed and
//  IsPrevented into IsAllowed, where it does not belong. A uniform table cannot detect the one
//  case it has averaged away.
//
//  So each nullable entry point gets its OWN fact, its OWN name stating the expected answer, and
//  its OWN locator. Ten facts covering nine predicate entry points plus IsValidObject. The
//  duplication is the point: it is what makes the odd one out visible as an odd one out.
//
//  For the same reason NULL IS DELIBERATELY ABSENT FROM THE TWO ALGEBRA MATRICES below. Mixing it
//  into the shared tables would put the asymmetry back inside a structure whose whole purpose is
//  to state a uniform rule per column, and would let a future edit delete a null row without any
//  named test disappearing with it.
//
//  TWO PLACES WHERE THE PORT'S SHAPE DIFFERS FROM THE LEGACY'S, AND WHAT IS ASSERTED INSTEAD
//  --------------------------------------------------------------------------------------------
//  Both were resolved by reading the as-built Predicates.cs rather than by assuming the legacy
//  shape carried over, because asserting an invented signature would fail to compile and
//  asserting a guessed answer would pin a fiction.
//
//  1. IsCancelled AND NULL. iscancelled.srf:L9 is one unguarded comparison, and in PowerScript a
//     comparison involving null yields null, which coerces to false at a `boolean` return - so the
//     legacy's null answer arrives by PROPAGATION, not by a decision. The port reproduces both the
//     answer and the shape with a lifted Nullable<long> comparison, which answers false when the
//     left operand has no value. The assertion below is therefore false, and its comment records
//     that the answer comes from the lifting rather than from a guard, so nobody later "adds the
//     missing guard for consistency" and reports a fix where there was no defect.
//
//  2. IsValidObject IS ONE MEMBER, NOT TWO. isvalidobject.srf declares `readonly any object` at L7
//     and `readonly powerobject object` at L8 with byte-identical bodies at L11-L13 and L15-L17.
//     The refactor's type map renders those as `object?` and `object`, and C# cannot distinguish
//     two overloads that differ only in a reference parameter's nullable annotation - nullability
//     is not part of a signature - so declaring both does not compile. The port collapses them into
//     a single `object?` member, which narrows the SIGNATURE and not the BEHAVIOUR: the two legacy
//     bodies agree, so no caller could observe which one it reached.
//
//     The consequence for this suite is that "null is false in both overloads" is discharged by
//     ONE assertion rather than two, and the requirement behind it - that both legacy call shapes
//     work - is discharged by driving both ARGUMENT shapes through the single member: a typed
//     reference for the `powerobject` shape and a boxed value for the `any` shape. A reflection
//     test pins the collapse itself, so a well-meaning future edit that "restores" the second
//     overload fails a test that explains why it cannot exist.
//
//  NULLABLE TYPING, AND WHY THERE IS NO `!` AND NO `#pragma` ANYWHERE BELOW  (constraint C-H)
//  --------------------------------------------------------------------------------------------
//  Directory.Build.props sets Nullable=enable and TreatWarningsAsErrors=true for every project in
//  the tree, test projects included, so a nullable-annotation warning here is a BUILD FAILURE, not
//  advice. This suite passes null deliberately and often, which makes that a real constraint
//  rather than a theoretical one.
//
//  It is satisfied structurally: every member-data column that can carry null is declared `long?`
//  or `bool?`, matching the parameter type of the member under test, and every null argument is a
//  typed local or an explicit cast so the intended overload is unambiguous. Nothing below reaches
//  for the null-forgiving operator or a pragma to quiet a warning - where a warning threatened,
//  the SIGNATURE was restructured instead. The clearest instance is the boolean-negation theory:
//  rather than compute the expected answer as `!flag.Value`, which would need `!` on a `bool?`,
//  the expected answer travels as its own column.
//
//  A COMPILATION HAZARD THIS FILE HAS ALREADY HANDLED
//  --------------------------------------------------------------------------------------------
//  IsSucceeded, IsFailed, IsPrevented and IsAllowed each have BOTH a `long?` and a `bool?`
//  overload, so a bare `null` literal is ambiguous between them - CS0121, a hard error, not a
//  warning. Every null argument below is consequently either a typed local (`long? code = null;`)
//  or an explicit cast. This also makes each null fact self-documenting about WHICH overload it
//  pins, which is necessary because the numeric and boolean overloads of IsAllowed have separate
//  locators (isallowed.srf:L11 and L14) and could in principle diverge.
//
//  THE MUTATION SET THIS SUITE IS BUILT TO KILL
//  --------------------------------------------------------------------------------------------
//  A characterization suite is only worth its runtime if a plausible wrong implementation fails
//  it. These five are the plausible ones - each is a defensible-looking "cleanup" of the ported
//  code - together with the specific test that detects each. Any future edit that removes a row
//  named here removes the detection with it.
//
//      IsSucceeded as `== RetCode.OK`         killed by the PREVENT row of the numeric matrix and
//                                             by PreventionIsClassifiedAsASuccess
//      IsFailed as `< RetCode.OK` alone,      killed by the CANCELLED row of the numeric matrix
//        dropping the cancelled exclusion     and by CancelledIsNotClassifiedAsAFailure
//      IsAllowed with a `return false` null   killed by IsAllowedReturnsTrueForANullCode and
//        guard copied from its neighbours     IsAllowedReturnsTrueForANullFlag - the single most
//                                             likely real defect, and the reason the null
//                                             expectations are not tabled
//      IsAllowed with `>= 1000`               killed by the 1000 row of the threshold theory and
//                                             by AllowedThresholdExcludesExactlyOneThousand
//      IsPrevented(bool?) as `return rtCode`  killed by the false row of the boolean matrix and by
//        instead of `Not rtCode`              BooleanFailureAndPreventionAreIndistinguishable
//
//  NAMING DISCIPLINE - A BUILD CONSTRAINT, NOT A STYLE PREFERENCE
//  --------------------------------------------------------------------------------------------
//  The repository-root .editorconfig scopes its CA1707 and IDE1006 suppressions BY FILE GLOB to
//  ten implementation files that genuinely declare the preserved legacy SCREAMING_SNAKE constant
//  spellings. No test file is among them, and Directory.Build.props forbids any project-wide
//  suppression. So this file REFERENCES RetCode.PREVENT, RetCode.CANCELLED, RetCode.E_RETRY and
//  the rest freely - CA1707 reports declarations, not uses - and DECLARES nothing that is not
//  conventional C#. Every member-data method, theory parameter, local and nested helper below is
//  PascalCase or camelCase accordingly.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * Any `async` or `await`. Every member under test is a synchronous pure function, so there is
//      nothing to await - and the xunit.v3 analyzer diagnostic requiring the ambient test
//      cancellation token on an awaited call is an ERROR in this repository, so a gratuitous async
//      signature would be a build failure for no benefit.
//    * `object?` as a TheoryData type argument. IsValidObject is covered by facts rather than a
//      theory, because a theory column typed as `object` invites the xunit analyzer's
//      non-serializable-data warning while adding nothing: the interesting inputs are four named
//      cases, not a matrix.
//    * `Assert.Equal(true, ...)` / `Assert.Equal(false, ...)`. Boolean literals go through
//      Assert.True and Assert.False; only the matrix theories compare against a bool VARIABLE,
//      which is the form the analyzer permits and the only form that can express a table.
//    * Any test of a member the legacy does not have. There is no IsCancelled(bool?) case because
//      there is no such member - iscancelled.srf declares one prototype at L6 - and the surface
//      test below asserts that absence rather than quietly working around it.
//    * Any shared "all predicates agree" helper. See the C-K note above: averaging the six
//      together is the one thing this suite must not do.
//    * Any mock, stub, fixture, fake or IDisposable of my own. These are ten pure static functions
//      over value types and one reference test; there is nothing to isolate.
// ==============================================================================================

using System.Reflection;

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Characterization tests for <see cref="Predicates"/>, the tri-state return-code algebra ported
/// from the six <c>is*.srf</c> global function objects under <c>ws_objects/pfw.shared.pbl.src/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Several expectations here deliberately pin LEGACY DEFECTS as correct behaviour, because the
/// refactor replicates defects rather than correcting them. The four named ones are that a
/// prevention reads as a success, that a cancellation is neither succeeded nor failed, that
/// <see cref="Predicates.IsAllowed(long?)"/> answers <see langword="true"/> for
/// <see langword="null"/> where its neighbours answer <see langword="false"/>, and that the boolean
/// overloads of <see cref="Predicates.IsFailed(bool?)"/> and
/// <see cref="Predicates.IsPrevented(bool?)"/> are indistinguishable. Read this file's header
/// before changing any expectation below.
/// </para>
/// </remarks>
public sealed class PredicatesTests
{
    // ==========================================================================================
    //  SECTION A - THE NUMERIC ALGEBRA, AS ONE TABLE
    //  ----------------------------------------------------------------------------------------
    //  One row per distinct return code, one column per predicate, so the whole algebra is legible
    //  as a single artifact and any change to it shows up as a changed row rather than as a
    //  changed assertion buried in a method. Rows are distinct VALUES rather than distinct
    //  identifiers - RetCode.OK, SUCCESS and ALLOW are all 0 [retcode.sru:L39-L41], so tabling all
    //  three would produce three identical test cases; the aliases are covered by their own fact
    //  in SECTION G instead.
    //
    //  NULL IS NOT A ROW HERE. It is asserted per predicate in SECTION D, for the reason given at
    //  length in this file's header: a shared table states a uniform rule per column, and the null
    //  rule is not uniform.
    // ==========================================================================================

    /// <summary>
    /// One row per distinct return code: the code, then the expected answer from
    /// <see cref="Predicates.IsSucceeded(long?)"/>, <see cref="Predicates.IsFailed(long?)"/>,
    /// <see cref="Predicates.IsPrevented(long?)"/>, <see cref="Predicates.IsAllowed(long?)"/> and
    /// <see cref="Predicates.IsCancelled(long?)"/> in that order.
    /// </summary>
    public static TheoryData<long?, bool, bool, bool, bool, bool> NumericAlgebraRows()
    {
        TheoryData<long?, bool, bool, bool, bool, bool> rows = [];

        //                                    succeeded  failed  prevented  allowed  cancelled
        //  Zero. The three names for it are the boundary of the success test and the first arm of
        //  the permission test at once. [retcode.sru:L39-L41]
        rows.Add(RetCode.OK, true, false, false, true, false);

        //  PRESERVED DEFECT. PREVENT is 1 [retcode.sru:L42] and the success test is `>= RetCode.OK`
        //  [issucceeded.srf:L12], so a veto reads as a success while ALSO reading as a prevention.
        //  This row is what kills an `== RetCode.OK` rewrite of IsSucceeded.
        rows.Add(RetCode.PREVENT, true, false, true, false, false);

        //  An unnamed positive immediately above PREVENT, confirming the success test is a RANGE
        //  and not an enumeration of the two known non-negative codes.
        rows.Add(2L, true, false, false, false, false);

        //  The largest catalogued positive: an extended SQLite code, SQLITE_OK + (1 * 256)
        //  [retcode.sru:L206; RetCode.cs:1103 in the port]. It is a success, and - the point of
        //  including it - it
        //  does NOT reach IsAllowed's undocumented 1000 threshold, so no catalogued code does.
        rows.Add(RetCode.SQLITE_OK_LOAD_PERMANENTLY, true, false, false, false, false);

        //  The three values that bracket that threshold. See SECTION E for the boundary in detail.
        rows.Add(999L, true, false, false, false, false);
        rows.Add(1000L, true, false, false, false, false);
        rows.Add(1001L, true, false, false, true, false);

        //  Plain failure. [retcode.sru:L43]
        rows.Add(RetCode.FAILED, false, true, false, false, false);

        //  PRESERVED DEFECT - THE TRI-STATE HOLE. CANCELLED is -2 [retcode.sru:L44-L45]. It fails
        //  the success test AND is excluded from the failure test by an explicit second conjunct
        //  [isfailed.srf:L12], so it is NEITHER. This row is what kills a bare `< RetCode.OK`
        //  rewrite of IsFailed.
        rows.Add(RetCode.CANCELLED, false, false, false, false, true);

        //  The head of the contiguous error block, and three points spread through the rest of the
        //  catalogue, confirming the failure test is a range rather than a list.
        //  [retcode.sru:L46, L76, L77, L78, L79]
        rows.Add(RetCode.E_INVALID_ARGUMENT, false, true, false, false, false);
        rows.Add(RetCode.E_RETRY, false, true, false, false, false);
        rows.Add(RetCode.E_NO_SUPPORT, false, true, false, false, false);
        rows.Add(RetCode.E_NO_IMPLEMENTATION, false, true, false, false, false);
        rows.Add(RetCode.UNKNOWN, false, true, false, false, false);

        //  An uncatalogued negative, proving nothing in the algebra depends on a value being known.
        rows.Add(-5L, false, true, false, false, false);

        //  The extremes. Neither is reachable from the catalogue; both are included because the
        //  comparisons are unbounded and an implementation that clamped or overflowed would be
        //  invisible to every row above.
        rows.Add(long.MaxValue, true, false, false, true, false);
        rows.Add(long.MinValue, false, true, false, false, false);

        return rows;
    }

    /// <summary>
    /// Asserts the complete numeric algebra a row at a time: five predicates, one table.
    /// [issucceeded.srf:L12, isfailed.srf:L12, isprevented.srf:L12, isallowed.srf:L11,
    /// iscancelled.srf:L9]
    /// </summary>
    [Theory]
    [MemberData(nameof(NumericAlgebraRows))]
    public void NumericPredicatesReproduceTheLegacyAlgebra(
        long? code,
        bool expectedSucceeded,
        bool expectedFailed,
        bool expectedPrevented,
        bool expectedAllowed,
        bool expectedCancelled)
    {
        // Each assertion is written against its own oracle line so a single failing row still
        // identifies which of the six legacy functions diverged.

        // issucceeded.srf:L12 - `return (rtCode >= RetCode.OK)`
        Assert.Equal(expectedSucceeded, Predicates.IsSucceeded(code));

        // isfailed.srf:L12 - `return (rtCode < RetCode.OK and rtCode <> RetCode.CANCELLED)`
        Assert.Equal(expectedFailed, Predicates.IsFailed(code));

        // isprevented.srf:L12 - `return (rtCode = RetCode.PREVENT)`
        Assert.Equal(expectedPrevented, Predicates.IsPrevented(code));

        // isallowed.srf:L11 - `return (rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode > 1000)`
        Assert.Equal(expectedAllowed, Predicates.IsAllowed(code));

        // iscancelled.srf:L9 - `return rtCode = RetCode.CANCELLED`
        Assert.Equal(expectedCancelled, Predicates.IsCancelled(code));
    }

    // ==========================================================================================
    //  SECTION B - THE FOUR NAMED DEFECTS, EACH AS ITS OWN FACT
    //  ----------------------------------------------------------------------------------------
    //  SECTION A already covers every one of these inside a row. They are restated here as named
    //  facts on purpose, and the duplication is deliberate: a row in a table is anonymous in a test
    //  report, whereas a failing fact called PreventionIsClassifiedAsASuccess tells whoever reads
    //  the CI log exactly which piece of legacy behaviour has just been lost. These four are the
    //  defects the refactor calls out by name as behaviour to preserve, so they get names.
    // ==========================================================================================

    /// <summary>
    /// PRESERVED DEFECT: a prevention is classified as a SUCCESS.
    /// [issucceeded.srf:L12, retcode.sru:L42]
    /// </summary>
    [Fact]
    public void PreventionIsClassifiedAsASuccess()
    {
        // PRESERVED DEFECT (C-B - replicate, never correct).
        // issucceeded.srf:L12 is `return (rtCode >= RetCode.OK)` - a RANGE test, not an equality
        // test - and PREVENT is 1 [retcode.sru:L42]. So 1 >= 0 holds and a veto answers true to
        // "did this succeed?". This is the first of the three defects the refactor names, and the
        // one with the longest reach: every caller that branches on IsSucceeded treats a prevented
        // operation as a completed one.
        //
        // DO NOT narrow the implementation to `== RetCode.OK` to make this look sensible. That is
        // the silent correction C-B forbids, and this assertion exists to fail if it happens.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
    }

    /// <summary>
    /// PRESERVED DEFECT: a cancellation is NOT classified as a failure. [isfailed.srf:L12]
    /// </summary>
    [Fact]
    public void CancelledIsNotClassifiedAsAFailure()
    {
        // PRESERVED DEFECT (C-B).
        // isfailed.srf:L12 is `return (rtCode < RetCode.OK and rtCode <> RetCode.CANCELLED)`. The
        // second conjunct exists for no documented reason and excludes -2 [retcode.sru:L44-L45]
        // from failure even though it is negative. Removing it would look like a simplification
        // and would change the answer for exactly one value, which is why this fact names that
        // value.
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
    }

    /// <summary>
    /// PRESERVED DEFECT: a prevention is not a failure either - it is positive. [isfailed.srf:L12]
    /// </summary>
    [Fact]
    public void PreventionIsNotClassifiedAsAFailure()
    {
        // PRESERVED DEFECT (C-B).
        // PREVENT is 1, so it fails the `< RetCode.OK` test [isfailed.srf:L12]. Taken with
        // PreventionIsClassifiedAsASuccess above, a veto is a success and not a failure - the
        // opposite of what the word "prevent" suggests to a caller, and the reason a caller that
        // needs to detect a veto must ask IsPrevented specifically.
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));
    }

    /// <summary>
    /// PRESERVED DEFECT: a cancellation is not a success either, completing the proof that it is
    /// NEITHER. [issucceeded.srf:L12, isfailed.srf:L12]
    /// </summary>
    [Fact]
    public void CancelledIsNotClassifiedAsASuccess()
    {
        // PRESERVED DEFECT (C-B) - the second half of the tri-state hole.
        // CANCELLED is -2, so `>= RetCode.OK` [issucceeded.srf:L12] is false. Combined with
        // CancelledIsNotClassifiedAsAFailure, this is the complete proof that -2 satisfies NEITHER
        // predicate: the algebra is nominally boolean but has a value that is outside both answers.
        // SECTION G states the consequence for callers.
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
    }

    // ==========================================================================================
    //  SECTION C - THE BOOLEAN OVERLOADS, AND THE COLLAPSE THE NUMERIC FORM DOES NOT HAVE
    //  ----------------------------------------------------------------------------------------
    //  Each of the four dual-overload predicates also accepts a flag, so PowerScript callers could
    //  use one predicate name over both a numeric code and a boolean result. Two of those four
    //  bodies are the SAME TEXT, which is the fourth quirk this suite pins.
    //
    //  The claim being tested has two halves and needs both: prevention and failure are
    //  INDISTINGUISHABLE in the boolean form, and they are DISTINCT in the numeric form. Asserting
    //  only the first would not detect an implementation that had collapsed the numeric forms too;
    //  asserting only the second would not detect one that had "fixed" the boolean form to test for
    //  prevention. There is a test for each half.
    // ==========================================================================================

    /// <summary>
    /// The boolean half of the truth table: the flag, then the expected answer from
    /// <see cref="Predicates.IsSucceeded(bool?)"/>, <see cref="Predicates.IsFailed(bool?)"/>,
    /// <see cref="Predicates.IsPrevented(bool?)"/> and <see cref="Predicates.IsAllowed(bool?)"/>.
    /// </summary>
    public static TheoryData<bool?, bool, bool, bool, bool> BooleanAlgebraRows()
    {
        TheoryData<bool?, bool, bool, bool, bool> rows = [];

        //                    succeeded  failed  prevented  allowed
        //  A true flag: the identity body [issucceeded.srf:L16] returns it unchanged, both
        //  negations [isfailed.srf:L16, isprevented.srf:L16] answer false, and the permission body
        //  [isallowed.srf:L14] answers true.
        rows.Add(true, true, false, false, true);

        //  A false flag. PRESERVED QUIRK: failed and prevented BOTH answer true, because both
        //  bodies are `return (Not rtCode)`. This row is what kills a `return rtCode` rewrite of
        //  IsPrevented(bool?).
        rows.Add(false, false, true, true, false);

        //  Null is deliberately not a row - see SECTION D and this file's header.
        return rows;
    }

    /// <summary>
    /// Asserts the boolean half of the algebra a row at a time. [issucceeded.srf:L16,
    /// isfailed.srf:L16, isprevented.srf:L16, isallowed.srf:L14]
    /// </summary>
    [Theory]
    [MemberData(nameof(BooleanAlgebraRows))]
    public void BooleanPredicatesReproduceTheLegacyAlgebra(
        bool? flag,
        bool expectedSucceeded,
        bool expectedFailed,
        bool expectedPrevented,
        bool expectedAllowed)
    {
        // issucceeded.srf:L16 - `return rtCode`  (an identity function on the non-null domain)
        Assert.Equal(expectedSucceeded, Predicates.IsSucceeded(flag));

        // isfailed.srf:L16 - `return (Not rtCode)`
        Assert.Equal(expectedFailed, Predicates.IsFailed(flag));

        // isprevented.srf:L16 - `return (Not rtCode)`  (the same text as the line above)
        Assert.Equal(expectedPrevented, Predicates.IsPrevented(flag));

        // isallowed.srf:L14 - `return (rtCode or IsNull(rtCode))`
        Assert.Equal(expectedAllowed, Predicates.IsAllowed(flag));
    }

    /// <summary>
    /// Rows carrying a flag and its negation, so the expected answer never has to be computed from
    /// the flag with the null-forgiving operator.
    /// </summary>
    /// <remarks>
    /// The second column is why this table exists rather than a single-column one: the parameter is
    /// <c>bool?</c> to match the member under test, so computing <c>!flag.Value</c> in the test body
    /// would need a null-forgiving <c>!</c>, which constraint C-H rules out. Carrying the expected
    /// answer as data restructures the signature instead of suppressing the warning.
    /// </remarks>
    public static TheoryData<bool?, bool> BooleanFlagNegationRows()
    {
        TheoryData<bool?, bool> rows = [];

        rows.Add(true, false);   // Not true  = false
        rows.Add(false, true);   // Not false = true

        return rows;
    }

    /// <summary>
    /// PRESERVED QUIRK: in the boolean form, failure and prevention are indistinguishable, because
    /// the two oracle bodies are textually identical. [isfailed.srf:L16, isprevented.srf:L16]
    /// </summary>
    [Theory]
    [MemberData(nameof(BooleanFlagNegationRows))]
    public void BooleanFailureAndPreventionAreIndistinguishable(bool? flag, bool expectedNegation)
    {
        bool failed = Predicates.IsFailed(flag);
        bool prevented = Predicates.IsPrevented(flag);

        // PRESERVED QUIRK (C-B). isfailed.srf:L15-L17 and isprevented.srf:L15-L17 are the SAME
        // TEXT - both `return (Not rtCode)` - so these two questions cannot be told apart once the
        // caller has reduced a return code to a flag. The port reproduces both bodies independently
        // rather than delegating one to the other, so this equality is a measured coincidence
        // rather than a tautology of the implementation, and it would break the moment either body
        // was "fixed".
        Assert.Equal(failed, prevented);

        // Both are the negation, not merely equal to each other. Without this line an
        // implementation that returned a constant from both would still satisfy the equality above.
        Assert.Equal(expectedNegation, failed);
        Assert.Equal(expectedNegation, prevented);
    }

    /// <summary>
    /// The other half of the same claim: the NUMERIC overloads keep failure and prevention cleanly
    /// distinct. [isfailed.srf:L12, isprevented.srf:L12]
    /// </summary>
    [Fact]
    public void NumericFailureAndPreventionRemainDistinct()
    {
        // The numeric bodies are a range test and an equality test against different values, so
        // unlike their boolean counterparts they disagree on both of the inputs that matter. This
        // is the half of the claim that detects a collapse of the numeric forms, and it is why
        // "indistinguishable in boolean, distinct in numeric" needs two tests rather than one.

        // isfailed.srf:L12 accepts FAILED; isprevented.srf:L12 does not.
        Assert.True(Predicates.IsFailed(RetCode.FAILED));
        Assert.False(Predicates.IsPrevented(RetCode.FAILED));

        // isprevented.srf:L12 accepts PREVENT; isfailed.srf:L12 does not, because 1 is positive.
        Assert.True(Predicates.IsPrevented(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));
    }

    // ==========================================================================================
    //  SECTION D - NULL SEMANTICS, ONE FACT PER ENTRY POINT
    //  ----------------------------------------------------------------------------------------
    //  READ THE HEADER NOTE BEFORE RESTRUCTURING THIS SECTION. Ten near-identical facts look like
    //  something waiting to be folded into a theory, and folding them is the one change that would
    //  destroy the section's purpose.
    //
    //  WHY (constraint C-K, and this is the reasoning, not a restatement of the rule):
    //  Null is handled by three DIFFERENT mechanisms across the six legacy functions, and they do
    //  not agree on the answer.
    //
    //      explicit guard, answers false   issucceeded.srf L11 L15, isfailed.srf L11 L15,
    //                                      isprevented.srf L11 L15, isvalidobject.srf L11 L15
    //      no guard, answers TRUE          isallowed.srf L11 L14 - IsNull is an arm of the RESULT
    //      no guard, answers false         iscancelled.srf L9 - by comparison propagation
    //
    //  A parameterised theory over "the predicates" would have to state one expected answer per
    //  row, so it would either encode the majority rule and be WRONG about IsAllowed, or carry a
    //  per-predicate expected column and become a lookup table with worse names than these ten
    //  method names. Worse, the failure mode it invites is the exact defect most likely to occur
    //  here: copying the three-line null guard from IsSucceeded into IsAllowed, where the oracle
    //  never had one. A uniform assertion cannot see that; a fact called
    //  IsAllowedReturnsTrueForANullCode fails immediately and says why in its name.
    //
    //  Ten facts, then, one per nullable entry point: nine predicate overloads that accept null
    //  plus IsValidObject. Each names its expected answer and cites its own line.
    //
    //  Every null argument is a TYPED LOCAL rather than a bare `null` literal, because IsSucceeded,
    //  IsFailed, IsPrevented and IsAllowed each have both a long? and a bool? overload and a bare
    //  null is ambiguous between them (CS0121, a hard error). The typed local also documents which
    //  overload each fact pins - necessary, since the two IsAllowed overloads have separate
    //  locators and could in principle diverge.
    // ==========================================================================================

    /// <summary>A null CODE is not a success. [issucceeded.srf:L11]</summary>
    [Fact]
    public void IsSucceededReturnsFalseForANullCode()
    {
        long? code = null;

        // issucceeded.srf:L11 - `if IsNull(rtCode) then return false`, an EXPLICIT guard.
        // Taken with IsFailedReturnsFalseForANullCode below, this is what makes null NEITHER
        // succeeded nor failed - the third value in the tri-state hole, alongside CANCELLED.
        Assert.False(Predicates.IsSucceeded(code));
    }

    /// <summary>A null FLAG is not a success. [issucceeded.srf:L15]</summary>
    [Fact]
    public void IsSucceededReturnsFalseForANullFlag()
    {
        bool? flag = null;

        // issucceeded.srf:L15 - the boolean overload carries its own explicit guard, so the identity
        // body at L16 is never reached with an absent flag.
        Assert.False(Predicates.IsSucceeded(flag));
    }

    /// <summary>A null CODE is not a failure. [isfailed.srf:L11]</summary>
    [Fact]
    public void IsFailedReturnsFalseForANullCode()
    {
        long? code = null;

        // isfailed.srf:L11 - `if IsNull(rtCode) then return false`, an EXPLICIT guard.
        // PRESERVED DEFECT (C-B): because IsSucceeded also answers false for null, an absent return
        // code is classified as neither outcome. Callers that treat `!IsFailed(x)` as "succeeded"
        // are wrong for null exactly as they are wrong for CANCELLED.
        Assert.False(Predicates.IsFailed(code));
    }

    /// <summary>A null FLAG is not a failure. [isfailed.srf:L15]</summary>
    [Fact]
    public void IsFailedReturnsFalseForANullFlag()
    {
        bool? flag = null;

        // isfailed.srf:L15 - the explicit guard precedes the negation at L16. Note the asymmetry
        // this creates: a FALSE flag is a failure, but an ABSENT flag is not, even though the
        // negation of null is nothing at all.
        Assert.False(Predicates.IsFailed(flag));
    }

    /// <summary>A null CODE is not a prevention. [isprevented.srf:L11]</summary>
    [Fact]
    public void IsPreventedReturnsFalseForANullCode()
    {
        long? code = null;

        // isprevented.srf:L11 - `if IsNull(rtCode) then return false`, an EXPLICIT guard.
        Assert.False(Predicates.IsPrevented(code));
    }

    /// <summary>A null FLAG is not a prevention. [isprevented.srf:L15]</summary>
    [Fact]
    public void IsPreventedReturnsFalseForANullFlag()
    {
        bool? flag = null;

        // isprevented.srf:L15 - the explicit guard, identical to isfailed.srf:L15. The two boolean
        // overloads agree on null for the same reason they agree everywhere else: the same text.
        Assert.False(Predicates.IsPrevented(flag));
    }

    /// <summary>
    /// PRESERVED DEFECT - THE ODD ONE OUT: a null CODE IS allowed. [isallowed.srf:L11]
    /// </summary>
    [Fact]
    public void IsAllowedReturnsTrueForANullCode()
    {
        long? code = null;

        // PRESERVED DEFECT (C-B), AND THE REASON THIS SECTION IS NOT A TABLE.
        // isallowed.srf:L11 has NO null guard. Its entire body is
        //     `return (rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode > 1000)`
        // so IsNull is an ARM OF THE RESULT rather than a precondition, and an absent return code
        // reads as PERMISSION GRANTED - the opposite of what IsSucceeded, IsFailed, IsPrevented,
        // IsCancelled and IsValidObject all do with the same input.
        //
        // This is the assertion that kills the single most likely real-world defect in the port:
        // copying the three-line `if (value is null) { return false; }` guard from the three
        // neighbouring predicates into this one, which would look like consistency and would invert
        // the answer for every caller that passes an uninitialised code. It is asserted here as its
        // own named fact precisely so that a uniform "all predicates reject null" table can never
        // average it away.
        Assert.True(Predicates.IsAllowed(code));
    }

    /// <summary>
    /// PRESERVED DEFECT - the boolean overload is the odd one out too: a null FLAG IS allowed.
    /// [isallowed.srf:L14]
    /// </summary>
    [Fact]
    public void IsAllowedReturnsTrueForANullFlag()
    {
        bool? flag = null;

        // PRESERVED DEFECT (C-B).
        // isallowed.srf:L14 is `return (rtCode or IsNull(rtCode))` - again no guard, again an arm of
        // the result. Asserted separately from the numeric overload above because they are separate
        // oracle lines: a port could plausibly guard one and not the other, and only a per-overload
        // fact would notice.
        Assert.True(Predicates.IsAllowed(flag));
    }

    /// <summary>
    /// A null CODE is not a cancellation - but by comparison propagation, not by a guard.
    /// [iscancelled.srf:L9]
    /// </summary>
    [Fact]
    public void IsCancelledReturnsFalseForANullCode()
    {
        long? code = null;

        // ASSERTED AGAINST THE AS-BUILT PORT, WITH THE MECHANISM RECORDED (C-B and C-K).
        // iscancelled.srf:L9 is the ENTIRE body - `return rtCode = RetCode.CANCELLED` - and it has
        // NO null guard of any kind. The answer is false all the same, and the distinction between
        // "false because the code says so" and "false because nothing said otherwise" is worth
        // preserving:
        //
        //     PowerScript   a comparison involving null yields NULL, and null coerces to false at a
        //                   `boolean` return. So the legacy answer arrives by PROPAGATION.
        //     C#            `long? == long` yields false when the left operand has no value. So the
        //                   ported answer arrives by NULLABLE LIFTING.
        //
        // Both languages let the null answer FALL OUT of an unguarded comparison, which is why the
        // port needs no guard to match and why adding one - though it would not change this
        // assertion - would still be wrong: it would misrepresent where the answer comes from and
        // invite a reader to record the missing guard as a defect that had been fixed.
        //
        // This is also the only member with no boolean overload, so there is no sibling fact for a
        // null flag; ThePublicSurfaceMatchesTheLegacyOverloadSet asserts that absence directly.
        Assert.False(Predicates.IsCancelled(code));
    }

    /// <summary>
    /// A null OBJECT is not valid, for both legacy overloads at once.
    /// [isvalidobject.srf:L11, L15]
    /// </summary>
    [Fact]
    public void IsValidObjectReturnsFalseForNull()
    {
        object? instance = null;

        // isvalidobject.srf:L11 - `if IsNull(object) then return false`  (the `any` overload)
        // isvalidobject.srf:L15 - `if IsNull(object) then return false`  (the `powerobject` one)
        //
        // ONE ASSERTION DISCHARGES BOTH LEGACY OVERLOADS, and that is a property of the port rather
        // than a gap in this test. The two legacy bodies are byte-identical, and the type map
        // renders their parameters as `object?` and `object` - which C# cannot tell apart, because
        // nullability is not part of a signature - so the port declares a single `object?` member
        // that both legacy call sites land on. Asserting twice would just call the same method
        // twice. IsValidObjectAcceptsBothLegacyArgumentShapes covers the two ARGUMENT shapes, and
        // ThePublicSurfaceMatchesTheLegacyOverloadSet pins the collapse itself.
        Assert.False(Predicates.IsValidObject(instance));
    }

    // ==========================================================================================
    //  SECTION E - THE UNDOCUMENTED 1000 THRESHOLD, ASSERTED AT ITS EXACT BOUNDARY
    //  ----------------------------------------------------------------------------------------
    //  isallowed.srf:L11 carries a third arm, `rtCode > 1000`, that NOTHING in the repository
    //  explains: no comment at the declaration, no mention in any of the five legacy documents, and
    //  no constant in retcode.sru equal to 1000 or 1001 that would hint at an intended meaning. It
    //  is reproduced verbatim, so it is tested verbatim.
    //
    //  WHY THE BOUNDARY ROWS EXIST (constraint C-K). The literal source expression is `> 1000`,
    //  strictly greater. `>= 1000` is a one-character edit, is what a reader who assumes the number
    //  is a threshold constant would naturally write, and differs from the oracle for EXACTLY ONE
    //  input in the entire 64-bit domain. Only an assertion at 1000 itself can detect it. 999 and
    //  1001 bracket it so that a failure localises the direction of the error, and long.MaxValue
    //  confirms the arm is genuinely unbounded above rather than a range.
    //
    //  This section overlaps SECTION A by design. There the threshold is three rows among many
    //  return codes; here it is the subject, with enough neighbouring values to pin the operator.
    // ==========================================================================================

    /// <summary>
    /// Codes spanning <see cref="Predicates.IsAllowed(long?)"/>'s three arms and the exact boundary
    /// of its undocumented third arm. [isallowed.srf:L11]
    /// </summary>
    public static TheoryData<long?, bool> AllowedThresholdRows()
    {
        TheoryData<long?, bool> rows = [];

        //  ARM ONE - equality with ALLOW, which is 0 [retcode.sru:L41]. Note that OK and SUCCESS
        //  share that value, so a plain success is also "allowed".
        rows.Add(RetCode.ALLOW, true);

        //  BETWEEN THE ARMS - every ordinary code answers false, including the positive ones. A veto
        //  is not permission, and neither is any small positive value.
        rows.Add(RetCode.PREVENT, false);
        rows.Add(2L, false);
        rows.Add(RetCode.SQLITE_OK_LOAD_PERMANENTLY, false);
        rows.Add(RetCode.FAILED, false);
        rows.Add(-5L, false);
        rows.Add(RetCode.CANCELLED, false);
        rows.Add(RetCode.UNKNOWN, false);
        rows.Add(long.MinValue, false);

        //  ARM THREE - THE BOUNDARY. `> 1000` is EXCLUSIVE.
        rows.Add(999L, false);
        rows.Add(1000L, false);   // the row a `>= 1000` implementation fails
        rows.Add(1001L, true);    // the first value the arm admits
        rows.Add(1002L, true);
        rows.Add(long.MaxValue, true);

        return rows;
    }

    /// <summary>
    /// Asserts <see cref="Predicates.IsAllowed(long?)"/> across all three arms of its single-line
    /// oracle body. [isallowed.srf:L11]
    /// </summary>
    [Theory]
    [MemberData(nameof(AllowedThresholdRows))]
    public void AllowedAcceptsOnlyZeroAndValuesAboveOneThousand(long? code, bool expectedAllowed)
    {
        // isallowed.srf:L11 - `return (rtCode = RetCode.ALLOW or IsNull(rtCode) or rtCode > 1000)`
        // The null arm is covered by IsAllowedReturnsTrueForANullCode, not from this table.
        Assert.Equal(expectedAllowed, Predicates.IsAllowed(code));
    }

    /// <summary>
    /// The threshold is strictly greater than 1000: 1000 is refused and 1001 is admitted.
    /// [isallowed.srf:L11]
    /// </summary>
    [Fact]
    public void AllowedThresholdExcludesExactlyOneThousand()
    {
        // The boundary restated as a named fact, so that a `>=` slip fails a test whose name says
        // what went wrong rather than an anonymous table row. These two assertions are the complete
        // difference between the oracle's `> 1000` and the plausible misreading `>= 1000`: they
        // disagree on one input out of 2^64.
        Assert.False(Predicates.IsAllowed(1000L));
        Assert.True(Predicates.IsAllowed(1001L));

        // And the arm is not the only route to true: zero still qualifies through the first arm.
        Assert.True(Predicates.IsAllowed(RetCode.ALLOW));
    }

    // ==========================================================================================
    //  SECTION F - IsValidObject, THE MOST HEAVILY USED MEMBER AND THE MOST SUBSTITUTED ONE
    //  ----------------------------------------------------------------------------------------
    //  472 legacy call sites across ws_objects reach these two overloads, which makes this the
    //  member whose behaviour is depended on most widely and therefore the one where a substitution
    //  has to be stated rather than assumed.
    //
    //  WHAT THE LEGACY ASKED. isvalidobject.srf:L12 and L16 both return `IsValid(object)`, the
    //  PowerBuilder intrinsic reporting whether a reference is LIVE - created and not yet
    //  `Destroy`ed. PowerBuilder object identity is a pointer with manual lifetime, so a variable
    //  can hold a non-null reference to an already-destroyed object, and IsValid is how a caller
    //  detects that state. It is what all 472 call sites are guarding against.
    //
    //  HOW THE PORT ANSWERS IT. .NET has no destroy-and-dangle state: a reference is either null or
    //  points at an object the garbage collector guarantees is alive. The exact managed equivalent
    //  of "created and not yet destroyed" is therefore "not null", and the port is a single
    //  reference test. Disposal is deliberately NOT consulted - the BCL publishes no general
    //  side-effect-free query for it, the per-type approximations that exist do not actually mean
    //  disposal, and a disposed object is in any case still a live valid reference. The residual gap
    //  that leaves is asserted below rather than left undocumented.
    // ==========================================================================================

    /// <summary>
    /// A minimal typed reference type, standing in for the argument shape the legacy
    /// <c>powerobject</c> overload accepted. [isvalidobject.srf:L8]
    /// </summary>
    /// <remarks>
    /// A purpose-built empty class rather than a borrowed BCL type, so that nothing about the
    /// assertions can depend on the probe having behaviour, state or a lifetime of its own. It
    /// deliberately does not implement <see cref="IDisposable"/>: the disposal case is covered with
    /// a BCL type below, and declaring a disposable type here would add design-analyzer surface for
    /// no gain.
    /// </remarks>
    private sealed class ReferenceProbe
    {
    }

    /// <summary>A live reference is valid. [isvalidobject.srf:L12, L16]</summary>
    [Fact]
    public void IsValidObjectReturnsTrueForALiveReference()
    {
        ReferenceProbe probe = new();

        // isvalidobject.srf:L12 and L16 - `return IsValid(object)`.
        // In managed terms a reference that is not null always denotes a live object, so the guard
        // and the IsValid call collapse into one test. This is the positive half of that collapse;
        // IsValidObjectReturnsFalseForNull in SECTION D is the negative half.
        Assert.True(Predicates.IsValidObject(probe));
    }

    /// <summary>
    /// Both legacy argument shapes - a typed reference and a boxed value - are valid through the one
    /// collapsed member. [isvalidobject.srf:L7-L8]
    /// </summary>
    [Fact]
    public void IsValidObjectAcceptsBothLegacyArgumentShapes()
    {
        // The oracle declared TWO overloads, `readonly any object` at L7 and
        // `readonly powerobject object` at L8, and the port collapses them into one `object?` member
        // because C# cannot distinguish parameters differing only in a nullable annotation. That
        // narrows the SIGNATURE, not the BEHAVIOUR - the two legacy bodies are byte-identical - so
        // the way to show nothing was lost is to drive both ARGUMENT shapes through the one member.

        // The `powerobject` shape: a typed reference to a class instance.
        ReferenceProbe typedReference = new();

        // The `any` shape: a BOXED VALUE. `any` could hold a value type where `powerobject` could
        // not, so this is the case that would break if the port had taken a constrained reference
        // parameter instead of `object?`. RetCode.CANCELLED is a long constant, boxed on assignment.
        object boxedValue = RetCode.CANCELLED;

        Assert.True(Predicates.IsValidObject(typedReference));
        Assert.True(Predicates.IsValidObject(boxedValue));

        // A boxed value is a reference to an object like any other, so the answer is about the
        // reference and not about what it wraps - note in particular that the answer is true even
        // though the wrapped value is the CANCELLED code, which every other predicate here treats
        // as significant. IsValidObject asks a question about identity, not about a return code.
    }

    /// <summary>
    /// DOCUMENTED SUBSTITUTION GAP: a disposed object is still reported valid, because disposal is
    /// not the state the oracle tested. [isvalidobject.srf:L12, L16]
    /// </summary>
    [Fact]
    public void IsValidObjectReportsADisposedObjectAsValid()
    {
        MemoryStream disposed = new();
        disposed.Dispose();

        // THE ONE PART OF `IsValid` NOT REPRODUCED, PINNED SO IT CANNOT DRIFT SILENTLY.
        // A destroyed PowerBuilder object answers false to IsValid; a disposed .NET object answers
        // true here. That is deliberate, not an oversight, and the reasoning is worth having at the
        // assertion because the "fix" is superficially attractive:
        //
        //   * There is no way to ask. IDisposable exposes only Dispose(); the BCL publishes no
        //     general, side-effect-free query for whether an arbitrary object has been disposed, and
        //     a try/catch probe is neither cheap nor exception-free on the most-called member in the
        //     project.
        //   * The per-type approximations mean something else. Stream.CanRead is false for a live
        //     WRITE-ONLY stream and SafeHandle.IsInvalid is true for a handle that was never valid,
        //     so consulting either would make this member answer false for objects the legacy calls
        //     valid - a behavioural CHANGE dressed up as fidelity, which C-B forbids.
        //   * Disposal is not the legacy concept. A disposed object is still a live reference with
        //     reachable members and intact identity; equating it with PowerBuilder destruction would
        //     import a distinction the oracle never drew.
        //
        // The gap is also unreachable from a faithful port of the 472 legacy call sites, none of
        // which can dispose an object and then ask whether it is valid, because the legacy has no
        // disposal concept to do it with. This assertion exists so the gap is a recorded decision
        // with a test attached rather than an accident someone discovers later.
        Assert.True(Predicates.IsValidObject(disposed));
    }

    // ==========================================================================================
    //  SECTION G - THE TRI-STATE HOLE STATED AS A WHOLE, AND THE SHAPE OF THE SURFACE
    //  ----------------------------------------------------------------------------------------
    //  SECTIONS A to F assert the predicates one at a time. The defect that matters most, though, is
    //  a property of the predicates TAKEN TOGETHER: two inputs satisfy neither of the two questions
    //  a caller thinks are exhaustive. That claim cannot be made by any single-predicate assertion,
    //  so it is made here.
    //
    //  This section also pins the SHAPE of the public surface by reflection. That is not
    //  box-ticking: two of the port's shape decisions are unobvious enough to invite a well-meaning
    //  "completion" - IsCancelled has no boolean overload, and IsValidObject has one member rather
    //  than two - and a test is the only artifact that will still be read when the header comment
    //  is not.
    // ==========================================================================================

    /// <summary>
    /// THE TRI-STATE HOLE: <see cref="RetCode.CANCELLED"/> satisfies neither
    /// <see cref="Predicates.IsSucceeded(long?)"/> nor <see cref="Predicates.IsFailed(long?)"/>.
    /// [issucceeded.srf:L12, isfailed.srf:L12]
    /// </summary>
    [Fact]
    public void CancelledIsNeitherSucceededNorFailed()
    {
        // PRESERVED DEFECT (C-B) - the whole claim in one place.
        // -2 is negative, so it fails `>= RetCode.OK`; and it is excluded by name from
        // `< RetCode.OK and rtCode <> RetCode.CANCELLED`. Both questions answer no.
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));

        // It is not that the value is unrecognised - the dedicated predicate identifies it exactly.
        // The hole is in the SUCCEEDED/FAILED pair, not in the algebra's knowledge of the value.
        Assert.True(Predicates.IsCancelled(RetCode.CANCELLED));

        // The consequence for callers, asserted rather than merely described. "Not succeeded" and
        // "failed" are DIFFERENT PROPOSITIONS for this input - the first is true, the second is
        // false - so negating either predicate does not yield the other, and no caller in the four
        // services may use one as a stand-in for the other.
        bool succeeded = Predicates.IsSucceeded(RetCode.CANCELLED);
        bool failed = Predicates.IsFailed(RetCode.CANCELLED);

        Assert.NotEqual(succeeded is false, failed);
    }

    /// <summary>
    /// Both spellings of the cancelled code fall in the same hole, because they are one value.
    /// [retcode.sru:L44-L45]
    /// </summary>
    [Fact]
    public void BothSpellingsOfCancelledBehaveIdentically()
    {
        // retcode.sru:L44 declares CANCELED and L45 declares CANCELLED, both as -2. The single-L
        // spelling is the one a caller is likelier to write and the double-L spelling is the one
        // isfailed.srf:L12 cites, so it is worth proving they cannot diverge behaviourally.
        Assert.Equal(RetCode.CANCELED, RetCode.CANCELLED);

        Assert.False(Predicates.IsSucceeded(RetCode.CANCELED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELED));
        Assert.True(Predicates.IsCancelled(RetCode.CANCELED));
    }

    /// <summary>
    /// A null code is the second value in the hole: neither succeeded nor failed.
    /// [issucceeded.srf:L11, isfailed.srf:L11]
    /// </summary>
    [Fact]
    public void NullIsNeitherSucceededNorFailed()
    {
        long? code = null;

        // PRESERVED DEFECT (C-B). Both predicates guard null to false, so an absent return code
        // joins CANCELLED outside both answers. Stated as its own fact because SECTION D proves each
        // half separately and this is the conjunction the callers actually trip over.
        Assert.False(Predicates.IsSucceeded(code));
        Assert.False(Predicates.IsFailed(code));

        // And null is emphatically not treated uniformly across the class: the same input that is
        // neither succeeded nor failed IS allowed. One input, three different answers.
        Assert.True(Predicates.IsAllowed(code));
    }

    /// <summary>
    /// A prevention satisfies both <see cref="Predicates.IsSucceeded(long?)"/> and
    /// <see cref="Predicates.IsPrevented(long?)"/> at once. [issucceeded.srf:L12, isprevented.srf:L12]
    /// </summary>
    [Fact]
    public void PreventionSatisfiesBothSucceededAndPrevented()
    {
        // PRESERVED DEFECT (C-B) - the mirror image of the hole. Where CANCELLED satisfies neither
        // question, PREVENT satisfies two that a caller would expect to be mutually exclusive: a
        // range test that admits it and an equality test that identifies it. A caller that checks
        // IsSucceeded first will never reach its veto handling.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.True(Predicates.IsPrevented(RetCode.PREVENT));

        // It is also allowed by neither arm of IsAllowed, which is the detail that makes the veto
        // legible at all: PREVENT is not zero and is not above 1000.
        Assert.False(Predicates.IsAllowed(RetCode.PREVENT));
    }

    /// <summary>
    /// The three names for zero behave identically across every predicate.
    /// [retcode.sru:L39-L41]
    /// </summary>
    [Fact]
    public void TheThreeNamesForZeroBehaveIdentically()
    {
        // OK, SUCCESS and ALLOW are one value declared three times [retcode.sru:L39-L41], and the
        // predicates cite different names for it - issucceeded.srf:L12 compares against OK while
        // isallowed.srf:L11 compares against ALLOW. Since both resolve to 0 the two comparisons are
        // the same comparison, which is worth pinning: a future edit that gave any of the three
        // aliases a distinct value would silently change which codes are successes AND which are
        // permitted.
        Assert.Equal(RetCode.OK, RetCode.SUCCESS);
        Assert.Equal(RetCode.OK, RetCode.ALLOW);

        Assert.True(Predicates.IsSucceeded(RetCode.SUCCESS));
        Assert.True(Predicates.IsAllowed(RetCode.OK));
        Assert.False(Predicates.IsFailed(RetCode.ALLOW));
        Assert.False(Predicates.IsPrevented(RetCode.OK));
        Assert.False(Predicates.IsCancelled(RetCode.OK));
    }

    /// <summary>
    /// The public surface is exactly the legacy surface: six function names, eleven legacy overloads
    /// collapsed to ten members. [issucceeded.srf:L7-L8, isfailed.srf:L7-L8, isprevented.srf:L7-L8,
    /// isallowed.srf:L7-L8, iscancelled.srf:L6, isvalidobject.srf:L7-L8]
    /// </summary>
    [Fact]
    public void ThePublicSurfaceMatchesTheLegacyOverloadSet()
    {
        MethodInfo[] members = typeof(Predicates)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Dictionary<string, int> overloadsByName = members
            .GroupBy(member => member.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        // Six legacy function objects, so six names and no more. A seventh name would mean a
        // convenience predicate had been invented - an IsOk, an IsNeither, an IsIndeterminate - and
        // the last of those in particular would be the tri-state hole routed around instead of
        // preserved, which is the behaviour this whole suite exists to protect.
        Assert.Equal(6, overloadsByName.Count);

        // Eleven legacy overloads become ten members. The four dual-form predicates keep both.
        Assert.Equal(10, members.Length);
        Assert.Equal(2, overloadsByName[nameof(Predicates.IsSucceeded)]);
        Assert.Equal(2, overloadsByName[nameof(Predicates.IsFailed)]);
        Assert.Equal(2, overloadsByName[nameof(Predicates.IsPrevented)]);
        Assert.Equal(2, overloadsByName[nameof(Predicates.IsAllowed)]);

        // ANOMALY ONE, ASSERTED: IsCancelled has exactly ONE overload. iscancelled.srf declares a
        // single prototype at L6 with no boolean form, and none is added for symmetry - "the other
        // five have two" is not evidence that this one should. Adding an IsCancelled(bool?) would
        // widen the surface past the legacy and fail here.
        Assert.Equal(1, overloadsByName[nameof(Predicates.IsCancelled)]);

        // ANOMALY TWO, ASSERTED: IsValidObject has exactly ONE member even though the oracle
        // declares two prototypes, because `any` and `powerobject` both render as an object
        // reference and C# cannot overload on a nullable annotation alone - declaring both does not
        // compile. This assertion is the guard rail for a future reader who counts the legacy
        // prototypes, notices one is missing, and tries to restore it.
        Assert.Equal(1, overloadsByName[nameof(Predicates.IsValidObject)]);
    }

    /// <summary>
    /// Every member is a unary function returning a definite <see cref="bool"/> - never
    /// <c>bool?</c>. [issucceeded.srf:L7, isfailed.srf:L7, isprevented.srf:L7, isallowed.srf:L7,
    /// iscancelled.srf:L6, isvalidobject.srf:L7]
    /// </summary>
    [Fact]
    public void EveryPredicateIsAUnaryFunctionReturningADefiniteBoolean()
    {
        MethodInfo[] members = typeof(Predicates)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(members);

        // The legacy prototypes are all declared `global function boolean`, so each produces a
        // definite answer even when its input is indefinite. Returning `bool?` would widen the
        // contract by letting an absent input propagate into the RESULT, which would turn every
        // caller's `if` into a three-way decision the legacy never had. Asserted for the whole
        // surface at once because it is a property of the algebra, not of any one predicate.
        Assert.All(members, member => Assert.Equal(typeof(bool), member.ReturnType));

        // Each takes exactly one argument: all six legacy functions are unary, so no member has
        // acquired an options parameter, a comparer, a default or a tolerance.
        Assert.All(members, member => Assert.Single(member.GetParameters()));
    }
}
