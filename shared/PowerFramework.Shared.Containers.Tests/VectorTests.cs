// ==============================================================================================
//  VectorTests - the ONE-BASED INDEXED surface and the BULK/TRANSFER surface of
//  PowerFramework.Shared.Containers.Vector
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  shared/PowerFramework.Shared.Containers/Vector.cs
//  LEGACY DECLARATION ws_objects/pfw.utility.container.pbl.src/n_vector.sru (54 lines, READ ONLY)
//                     A DECLARATION, not an oracle: 33 prototypes and NO bodies. It settles
//                     signatures and settles no edge behaviour. See DP-7 EVIDENCE STATUS below.
//  SIBLING CLASS      VectorCursorTests.cs owns the cursor state machine and the stack-usage
//                     pattern. The split is enumerated in the LEDGER below so that neither class
//                     duplicates the other and neither leaves a member untested.
//
//  DP-7 EVIDENCE STATUS - READ THIS BEFORE CITING ANY ASSERTION AS LEGACY PARITY
//  --------------------------------------------------------------------------------------------
//  THIS SUITE IS NOT GOLDEN-MASTER PROOF. It is a target regression guard. A Golden-Master test
//  compares the port against a RECORDING of the legacy; every expectation here was derived from
//  the port plus a prototype list, because no legacy body exists to consult. The two are
//  indistinguishable when they agree and silently divergent when they do not, so each assertion's
//  tier is stated rather than inferred:
//
//    TIER 1  TRACEABLE - settled by a cited ws_objects/** locator: a member name, an arity, a
//            parameter type, a return type. A prototype is a complete statement of a signature.
//    TIER 3  TARGET-CHARACTERIZED - settled by the port, because the prototype does not settle it.
//            Every edge behaviour is here: the inclusive one-based bounds, out-of-range returns,
//            the copy and transfer semantics, Purge/Reverse/Reserve/Resize outcomes, and the
//            MaxSize contract. DEFINED, REPRODUCIBLE AND COVERED - never verified.
//
//  THE ORACLE-CAPTURE PREREQUISITE. Promoting a TIER 3 assertion to parity evidence needs a
//  paired recording under characterization/recordings/{legacy,dotnet}/<workflowId>/, taken against
//  one unrecreated persistence-db volume state. Until then no TIER 3 expectation here may be
//  reported as verified legacy behaviour. IF A RECORDING LATER CONTRADICTS ONE, THE RECORDING WINS
//  and the test is corrected - it is not defended on the grounds that it currently passes.
//
//  WHY THIS CLASS EXISTS: THE SUBSTITUTION IS WHERE THE RISK IS
//  --------------------------------------------------------------------------------------------
//  Vector is a SUBSTITUTION, NOT A PORT, and that is a measured fact rather than a description.
//  n_vector.sru:L8 declares
//
//      global type n_vector from nonvisualobject native "pfw.dll"
//
//  and the export contains ZERO function or subroutine bodies: every line of legacy behaviour
//  lives inside the closed-source pfw.dll, for which no C++ source exists anywhere in this
//  repository. The plan's native-binding matrix therefore records this type as SUBSTITUTE onto
//  base class library types with "insertion order and positional access preserved", and the
//  33 prototypes at L9-L41 are the whole of the available specification.
//
//  The substitution is built on System.Collections.Generic.List<object?>, which is ZERO-BASED,
//  while every legacy index is ONE-BASED. The plan names that translation the single most
//  dangerous mechanical hazard in the whole refactor, for a specific reason: an off-by-one here
//  is INDISTINGUISHABLE FROM A BEHAVIOURAL REGRESSION, because every element count still
//  matches. This class and its sibling are the only place that bridge is verified.
//
//  The stakes reach past this library. In
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru the only in-scope
//  consumer joins the container into a call-stack string with an inclusive one-based walk
//
//      L753   nCount = _vecCalcStack.Count()
//      L754   for nIndex = 1 to nCount
//      L755      sCallStack += _vecCalcStack.GetAt(nIndex) + ">"
//      L756   next
//
//  and publishes that string at L757-L758 through OnColumnExpTrace(row, dwo, sCallStack, sExp,
//  ...), which is carried on the expression-trace channel of cross-service contract C-04. A
//  zero-based or reversed GetAt would therefore corrupt an OBSERVABLE WIRE PAYLOAD, not merely an
//  internal index. That is why the one-based fidelity asserted below matters beyond this library,
//  and why it is asserted exhaustively rather than sampled.
//
//  ONE-BASEDNESS IS PROVEN FROM CONSUMER CODE, NOT ASSUMED FROM CONVENTION
//  --------------------------------------------------------------------------------------------
//  Same consumer, the push and pop of the recursion stack:
//
//      L296   _vecCalcStack.Append(colName)     push
//      L297   k = _vecCalcStack.Count()         k is taken as the index OF THE JUST-APPENDED
//                                               element, with NO adjustment
//      L318   _vecCalcStack.RemoveAt(k)         pop exactly that element
//
//  Under zero-based indexing RemoveAt(Count()) would be one past the end, so the push would be
//  followed by a pop of nothing and the stack would grow without bound. The last element's index
//  therefore EQUALS Count(), so valid indices run 1 .. Count() INCLUSIVE. Corroborated by a
//  second inclusive loop that never touches index 0, the recursion guard at L684-L688, and by
//  the pre-allocation at L2421-L2422 where the consumer creates its own instance and immediately
//  calls Reserve(20).
//
//  THE LEDGER: EVERY ONE OF THE 33 DECLARATIONS AT n_vector.sru:L9-L41, ACCOUNTED FOR
//  --------------------------------------------------------------------------------------------
//  Transcribed in the source's own declaration order so this file can be diffed against the
//  oracle top to bottom. Each line resolves to exactly one of three dispositions - covered HERE,
//  covered by the SIBLING, or DELIBERATELY ABSENT - and nothing is left unaccounted for.
//
//      L9   long copyfromlist(...)                      ABSENT   omitted; see C-D below
//      L10  long copyfromvector(n_vector obj)           HERE     CopyFromVector
//      L11  long copytoarray(ref any values[])          HERE     CopyToArray
//      L12  ulong count()                               HERE     Count
//      L13  ulong maxsize()                             HERE     MaxSize
//      L14  subroutine purge()                          HERE     Purge
//      L15  subroutine reverse()                        HERE     Reverse
//      L16  boolean reserve(ulong size)                 HERE     Reserve
//      L17  boolean resize(ulong size)                  HERE     Resize
//      L18  boolean exists(any value)                   HERE     Exists
//      L19  subroutine move(ulong index)                HERE     Move - INDEX semantics only
//      L20  subroutine append(any value)                HERE     Append
//      L21  any get()                                   SIBLING  instrument only, see below
//      L22  any getat(ulong index)                      HERE     GetAt
//      L23  subroutine set(any value)                   SIBLING
//      L24  subroutine setat(ulong index, any value)    HERE     SetAt
//      L25  subroutine remove()                         SIBLING
//      L26  subroutine removeat(ulong index)            HERE     RemoveAt
//      L27  any getnext()                               SIBLING
//      L28  any getprevious()                           SIBLING
//      L29  any getandnext()                            SIBLING
//      L30  subroutine rewind()                         SIBLING
//      L31  any getfirst()                              HERE     return value only
//      L32  any getlast()                               HERE     return value only
//      L33  subroutine prepend(any value)               HERE     Prepend
//      L34  subroutine insertbefore(ulong, any)         HERE     InsertBefore
//      L35  subroutine insertafter(ulong, any)          HERE     InsertAfter
//      L36  boolean hasnext()                           SIBLING
//      L37  ulong position()                            SIBLING  instrument only, see below
//      L38  ulong position(any value)                   HERE     Position(value)
//      L39  subroutine sort()                           HERE     Sort()
//      L40  subroutine sort(powerobject, string)        ABSENT   replaced; see C-D below
//      L41  subroutine unique()                         HERE     Unique
//
//  Move, GetFirst and GetLast each unavoidably touch the cursor, so the split is drawn inside
//  those members rather than around them: the INDEX semantics and the RETURN VALUES are asserted
//  here, and the cursor consequences belong to the sibling. Where an assertion here needs to see
//  where the cursor landed, Get() and Position() are used as OBSERVATION INSTRUMENTS and are
//  flagged as such at the call site; they are not under test here.
//
//  THE TWO DELIBERATELY ABSENT MEMBERS, SO NOBODY READS THIS AS MISSED COVERAGE
//  --------------------------------------------------------------------------------------------
//  n_vector.sru:L9 - copy-from-the-sibling-list-container - IS OMITTED FROM THE PORT ENTIRELY,
//  and nothing is asserted about it here because there is nothing to assert. Its parameter type
//  is the sibling list container exported from the same legacy library, and the plan's full-estate
//  mapping assigns that type to the DEFERRED Documents service, recording that it has no in-scope
//  consumer. Constraint C-D forbids implementing any part of a deferred service, "even partially,
//  even to stub them out", so the member is absent from Vector.cs and no test can exist for it.
//
//  The closure argument is measured, not assumed. A whole-word search for that type across every
//  .sru, .srw and .srf under ws_objects/, excluding its own export, returns EXACTLY ONE hit: the
//  prototype at n_vector.sru:L9 itself. Omitting that single member therefore leaves the deferred
//  type with ZERO references from in-scope code. A test would itself be such a reference, which
//  is why none is written - and it is also why this file DOES NOT SPELL THE TYPE'S IDENTIFIER
//  ANYWHERE, not even in a comment: the one-hit closure has to survive this file too. Nor is there
//  a skipped-test placeholder standing in for it - no test in this file carries a skip reason,
//  because a skipped test is still a reference to the capability it names.
//
//  n_vector.sru:L40 - sort(powerobject apo_comparator, string cmpfunc) - IS REPLACED, NOT
//  OMITTED, and its replacement IS covered here. The legacy overload dispatches to a comparison
//  routine BY METHOD-NAME STRING on an arbitrary object, resolving that name at run time on every
//  comparison. Reproducing that faithfully would mean building dynamic invocation by name, and
//  the plan assigns the legacy dynamic-invocation family to the DEFERRED ScriptBridge service, so
//  C-D rules it out. Vector.cs substitutes the .NET comparison contract instead - an
//  IComparer<object?> overload and a Comparison<object?> overload - and BOTH are exercised below,
//  including a reverse ordering and a case that reveals what the sort does with elements the
//  comparator calls equal. What has no analogue and therefore no test is the method-name-string
//  dispatch mechanism itself.
//
//  GOVERNING CONSTRAINTS
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line, "No user rules provided", so NO USER RULE GOVERNS THIS
//  FILE. That is a FINDING, not an omission: the plan's own rules section records the same result
//  and states that the rule-mandated file list is empty, and that enterprise-standard best
//  practice applies in their place. The binding constraints are therefore the plan's own
//  inventory, honoured here exactly as rules would have been, and nothing below is an invented
//  rule. Each is named, never quoted.
//
//  C-B  Behaviour is asserted AS OBSERVED, never as tidied. One-based indices stay one-based in a
//       zero-based language. Count() and MaxSize() stay ulong and are compared against ulong
//       literals. The not-found result of the value search stays the 0 SENTINEL and is not
//       upgraded into an expected exception. An out-of-range index stays a silent no-op or a null
//       return rather than being asserted as a throw. Unique's surviving-occurrence rule and
//       Sort()'s default ordering are pinned to what the implementation does, not to what a
//       sensible container would do - which is why Unique is asserted to leave a SCATTERED
//       duplicate in place. Every expected value below was first MEASURED against the compiled
//       assembly; none was predicted.
//  C-C  The legacy tree is read only and is the behavioural oracle. Every .sru path and line
//       number in this file appears in a COMMENT only. No test opens, reads, parses or writes
//       anything under ws_objects/** or anywhere else: there is no File, Path, Directory or
//       Stream use in this file, and the tests are pure in-memory. The test project's own file
//       carries no Content, None or EmbeddedResource item either, so nothing can reach the oracle
//       through the build.
//  C-D  The strongest constraint on this file, discharged as described above: no deferred type is
//       referenced, named as a type, constructed or asserted about; the omitted member's absence
//       is discharged by documentation alone; and there is no skipped-test placeholder anywhere.
//  C-H  This class carries the bulk of Vector's breadth coverage, so every member in the ledger
//       marked HERE is exercised with BEHAVIOURAL assertions rather than smoke-instantiated -
//       including the reachable false paths of Reserve and Resize and the null-argument guards,
//       which exist in the implementation precisely so that they can be tested. Nullable
//       reference types and warnings-as-errors are inherited from the repository-root property
//       sheet and are not relaxed here.
//  C-K  Every technology-specific decision is documented at the point it is made: the
//       substitution and its List<object?> backing above, the one-based contract with its
//       consumer locators and its C-04 wire consequence, the comparator-delegate substitution for
//       method-name-string dispatch, the two absent members, the instrument dependency order
//       described next, and the two places where an assertion is deliberately made against a
//       CONTRACT rather than against a base-class-library implementation detail (MaxSize and sort
//       stability, each explained where it occurs).
//
//  INSTRUMENT DEPENDENCY ORDER, STATED BECAUSE IT WOULD OTHERWISE BE CIRCULAR
//  --------------------------------------------------------------------------------------------
//  Most structural assertions below read the container through Render, which walks 1 .. Count()
//  through GetAt. GetAt is therefore the instrument as well as a member under test, so it is
//  pinned FIRST by direct element-by-element assertions that use no helper at all. Every later
//  test may then rely on it. Render is a lossless projection for the labels used here - they
//  contain neither the separator nor the null token - and null has its own distinct token so a
//  slot padded by Resize can never be mistaken for an empty label.
//
//  A FRESH LOCAL Vector IS CONSTRUCTED IN EVERY TEST, AND THAT IS DELIBERATE
//  --------------------------------------------------------------------------------------------
//  There is no field, no static, no fixture and no shared instance in this class. Beyond the
//  ordinary isolation argument, it is the hazard the port itself avoids: n_vector.sru:L43 declares
//  a global auto-instance whose name shadows its own type name, and the plan's collision rule
//  keeps the descriptive type name while turning the instance into a constructed or injected
//  dependency. The only in-scope consumer corroborates that by creating its own instance at
//  n_cst_dwsvc_columnexp.sru:L2421 and never touching the global. Tests that shared one instance
//  would reintroduce exactly the coupling the port removed.
//
//  No mocking, assertion or auto-fixture package is used; the plan's excluded-package list rules
//  them out and xunit.v3 already carries Assert. The comparator cases use a plain delegate and one
//  small private comparer class, which is all they need.
// ==============================================================================================

using Xunit;

namespace PowerFramework.Shared.Containers.Tests;

/// <summary>
/// Pins the one-based indexed surface and the bulk/transfer surface of
/// <see cref="Vector"/>, the managed substitution for the legacy PowerBuilder type declared at
/// <c>ws_objects/pfw.utility.container.pbl.src/n_vector.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Vector"/> is a substitution rather than a port: the legacy type is a PBNI binding
/// (<c>n_vector.sru:L8</c> reads <c>native "pfw.dll"</c>) whose export carries prototypes and no
/// bodies, and the managed replacement stores its elements in a
/// <see cref="System.Collections.Generic.List{T}"/> of nullable <see langword="object"/>. That
/// backing store is zero-based while every legacy index is one-based, so the one-based contract is
/// a deliberate fidelity decision and is the single most important thing this class verifies.
/// </para>
/// <para>
/// The evidence for one-basedness is the only in-scope consumer, which appends and then treats
/// <see cref="Vector.Count"/> as the index of the element it just appended
/// (<c>n_cst_dwsvc_columnexp.sru:L296-L297</c>) before popping exactly that element with
/// <see cref="Vector.RemoveAt(ulong)"/> (<c>L318</c>), and which reads the container back in
/// inclusive <c>1 .. Count()</c> loops at <c>L684-L688</c> and <c>L753-L756</c>. The second of
/// those builds the trace call stack published at <c>L757-L758</c>, which travels on the
/// expression-trace channel of contract C-04 - so the ordering asserted here is observable on the
/// wire, not merely internal.
/// </para>
/// <para>
/// The cursor state machine belongs to the sibling class <c>VectorCursorTests</c>. Two members of
/// the legacy surface are deliberately absent from the port and are therefore not asserted here:
/// the copy-from-the-sibling-list-container function at <c>n_vector.sru:L9</c>, omitted because
/// its parameter type belongs to a deferred service, and the method-name-string sort at
/// <c>L40</c>, replaced by the injected comparer and comparison overloads which <b>are</b>
/// covered below. See the file header for the full ledger and the reasoning.
/// </para>
/// </remarks>
public sealed class VectorTests
{
    /// <summary>
    /// The token <see cref="Render(Vector)"/> uses for a <see langword="null"/> element.
    /// </summary>
    /// <remarks>
    /// Distinct from every label used in this class, so that a slot padded with
    /// <see langword="null"/> by <see cref="Vector.Resize(ulong)"/> can never be mistaken for an
    /// element holding an empty string.
    /// </remarks>
    private const string NullToken = "(null)";

    /// <summary>
    /// The separator <see cref="Render(Vector)"/> puts between rendered elements.
    /// </summary>
    /// <remarks>
    /// Chosen because no label in this class contains it, which is what makes the projection
    /// lossless and therefore safe to assert against.
    /// </remarks>
    private const string Separator = "|";

    /// <summary>
    /// Builds a vector holding the supplied values in the supplied order.
    /// </summary>
    /// <param name="values">The elements, in the order they should occupy positions 1 upward.</param>
    /// <returns>A newly constructed vector; never a shared or cached instance.</returns>
    /// <remarks>
    /// Uses <see cref="Vector.Append(object?)"/> because that is the legacy push idiom
    /// (<c>n_cst_dwsvc_columnexp.sru:L296</c>) and because appending in argument order is what
    /// makes position 1 the first argument - the very property the one-based tests then assert.
    /// </remarks>
    private static Vector VectorOf(params object?[] values)
    {
        Vector vector = new();

        foreach (object? value in values)
        {
            vector.Append(value);
        }

        return vector;
    }

    /// <summary>
    /// The label this class uses for the element at a given one-based position.
    /// </summary>
    /// <param name="oneBasedPosition">The one-based position the label stands for.</param>
    /// <returns>A label that encodes its own expected position, such as <c>e1</c>.</returns>
    /// <remarks>
    /// Encoding the position into the label is what makes an off-by-one visible in an assertion
    /// message rather than merely detected: a failure reads "expected e1, actual e2" instead of
    /// comparing two opaque values.
    /// </remarks>
    private static string Label(int oneBasedPosition) => "e" + oneBasedPosition;

    /// <summary>
    /// Builds a vector of <paramref name="length"/> position-encoding labels, so that the element
    /// at one-based position <c>i</c> is <c>Label(i)</c>.
    /// </summary>
    /// <param name="length">The element count to build. Zero yields an empty vector.</param>
    /// <returns>A newly constructed vector.</returns>
    private static Vector LabelledVector(int length)
    {
        Vector vector = new();

        for (int position = 1; position <= length; position++)
        {
            vector.Append(Label(position));
        }

        return vector;
    }

    /// <summary>
    /// Projects a vector to a string by walking it from <c>1</c> to <see cref="Vector.Count"/>
    /// inclusive through <see cref="Vector.GetAt(ulong)"/>.
    /// </summary>
    /// <param name="vector">The vector to project.</param>
    /// <returns>
    /// The elements joined by <see cref="Separator"/>, with <see langword="null"/> rendered as
    /// <see cref="NullToken"/>. An empty vector renders as the empty string.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ONE-BASED WALK, and the instrument almost every structural assertion in this class
    /// reads the container through. It deliberately starts at <c>1</c> and ends at
    /// <see cref="Vector.Count"/> inclusive, mirroring the consumer loops at
    /// <c>n_cst_dwsvc_columnexp.sru:L684-L688</c> and <c>L753-L756</c> rather than a conventional
    /// zero-based enumeration, so that any drift in the index contract shows up in every test at
    /// once instead of only where it is named.
    /// </para>
    /// <para>
    /// Because it is built on <see cref="Vector.GetAt(ulong)"/>, that member is pinned first by
    /// direct assertions that use no helper - see the instrument dependency note in the file
    /// header. It reads the container without moving the cursor, so a test may use it freely
    /// without disturbing anything the sibling class asserts.
    /// </para>
    /// </remarks>
    private static string Render(Vector vector)
    {
        List<string> rendered = [];

        for (ulong position = 1; position <= vector.Count(); position++)
        {
            object? element = vector.GetAt(position);

            rendered.Add(element is null ? NullToken : element.ToString() ?? NullToken);
        }

        return string.Join(Separator, rendered);
    }

    /// <summary>
    /// Renders the expected projection of a sequence of position-encoding labels.
    /// </summary>
    /// <param name="length">How many labels the expected sequence holds.</param>
    /// <returns>The expected value of <see cref="Render(Vector)"/> for that sequence.</returns>
    private static string ExpectedLabels(int length)
    {
        List<string> expected = [];

        for (int position = 1; position <= length; position++)
        {
            expected.Add(Label(position));
        }

        return string.Join(Separator, expected);
    }

    /// <summary>
    /// The value the insertion and replacement tests put into the container, chosen so it can
    /// never collide with a position-encoding label.
    /// </summary>
    private const string Inserted = "X";

    // ==========================================================================================
    //  PHASE 1 - THE ONE-BASED INDEXED CONTRACT
    //
    //  Written and asserted first, because every later test in this class reads the container
    //  through the one-based walk. GetAt is pinned here by direct element-by-element assertions
    //  that use no helper, so that the instrument the rest of the class relies on cannot be
    //  circular.
    // ==========================================================================================

    /// <summary>
    /// Element counts the one-based walk is driven over, as a table-driven theory rather than a
    /// single hand-picked length.
    /// </summary>
    /// <remarks>
    /// <c>1</c> is the degenerate case where the first and last element coincide, <c>2</c> and
    /// <c>3</c> are the smallest cases that can distinguish a forward walk from a reversed one, and
    /// <c>5</c> and <c>8</c> carry the walk far enough that an off-by-one at either end cannot be
    /// masked by the boundary tests alone.
    /// </remarks>
    public static TheoryData<int> OneBasedWalkLengths => new() { 1, 2, 3, 5, 8 };

    /// <summary>
    /// Index values that reference no element of a three-element vector, so that every positional
    /// member can be driven over the same boundary set.
    /// </summary>
    /// <remarks>
    /// <c>0</c> is the index a zero-based reading would treat as the first element, <c>4</c> is
    /// <c>Count() + 1</c>, and <see cref="ulong.MaxValue"/> confirms the range test cannot be
    /// tripped into wrapping.
    /// </remarks>
    public static TheoryData<ulong> IndicesOutsideAThreeElementVector => new() { 0UL, 4UL, ulong.MaxValue };

    /// <summary>
    /// <c>GetAt</c> is one-based across the whole sequence: position <c>1</c> is the FIRST element
    /// and position <c>Count()</c> is the last, with the upper bound INCLUSIVE.
    /// </summary>
    /// <remarks>
    /// The central assertion of this class. The loop mirrors the consumer's own inclusive walk at
    /// <c>n_cst_dwsvc_columnexp.sru:L684-L688</c> and <c>L753-L756</c>, and it uses no helper so
    /// that the instrument the rest of the class depends on is established here first.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OneBasedWalkLengths))]
    public void GetAtWalksEveryPositionFromOneToCountInclusive(int length)
    {
        Vector vector = LabelledVector(length);

        Assert.Equal((ulong)length, vector.Count());

        for (int position = 1; position <= length; position++)
        {
            Assert.Equal(Label(position), vector.GetAt((ulong)position));
        }

        // Stated separately from the loop because these two are the claims that fail first under a
        // zero-based reading: index 1 would yield the second element and index Count() would be one
        // past the end.
        Assert.Equal(Label(1), vector.GetAt(1));
        Assert.Equal(Label(length), vector.GetAt(vector.Count()));
    }

    /// <summary>
    /// The last valid index EQUALS <c>Count()</c> rather than <c>Count() - 1</c>.
    /// </summary>
    /// <remarks>
    /// This is the exact property the legacy push and pop idiom rests on: the consumer appends,
    /// takes <c>Count()</c> as the index of what it just appended, and later pops with that same
    /// value [<c>n_cst_dwsvc_columnexp.sru:L296-L297, L318</c>]. <c>Count() - 1</c> is asserted to
    /// be the second-to-last element, which is what makes the inclusive claim unambiguous.
    /// </remarks>
    [Fact]
    public void GetAtTreatsCountAsTheLastValidIndexAndCountMinusOneAsTheOneBefore()
    {
        Vector vector = LabelledVector(3);

        Assert.Equal(Label(3), vector.GetAt(vector.Count()));
        Assert.Equal(Label(2), vector.GetAt(vector.Count() - 1));
    }

    /// <summary>
    /// An out-of-range index makes <c>GetAt</c> return <see langword="null"/>; it does not throw.
    /// </summary>
    /// <remarks>
    /// Asserted as the implementation behaves rather than as a conventional .NET container would.
    /// Vector.cs records the reason: the legacy prototype at <c>n_vector.sru:L22</c> returns a
    /// value and has no failure channel through which a bad index could be reported, so there is no
    /// legacy exception to reproduce. Constraint C-B forbids upgrading that into a throw.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IndicesOutsideAThreeElementVector))]
    public void GetAtReturnsNullOutOfRangeRatherThanThrowing(ulong index)
    {
        Vector vector = LabelledVector(3);

        Assert.Null(vector.GetAt(index));
        Assert.Equal(ExpectedLabels(3), Render(vector));
    }

    /// <summary>
    /// Expected renderings after replacing exactly one element of a three-label vector.
    /// </summary>
    public static TheoryData<ulong, string> SetAtReplacements => new()
    {
        { 1UL, "X|e2|e3" },
        { 2UL, "e1|X|e3" },
        { 3UL, "e1|e2|X" },
    };

    /// <summary>
    /// <c>SetAt</c> replaces exactly the addressed element, one-based, changing neither the length
    /// nor the order of the others.
    /// </summary>
    /// <remarks>
    /// Covers both ends of the range through the table: index <c>1</c> is the first element and
    /// index <c>3</c> is <c>Count()</c>. Legacy prototype <c>n_vector.sru:L24</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SetAtReplacements))]
    public void SetAtReplacesExactlyTheAddressedElement(ulong index, string expected)
    {
        Vector vector = LabelledVector(3);

        vector.SetAt(index, Inserted);

        Assert.Equal(expected, Render(vector));
        Assert.Equal(Inserted, vector.GetAt(index));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// An out-of-range index makes <c>SetAt</c> a silent no-op.
    /// </summary>
    /// <remarks>
    /// Nailed down rather than left to assumption, and asserted in both directions: the contents
    /// are unchanged AND the element count is unchanged, so the member can never be used to extend
    /// the container. In particular a mistaken <c>0</c> does NOT overwrite the first element, which
    /// is the failure a zero-based habit would produce.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IndicesOutsideAThreeElementVector))]
    public void SetAtOutOfRangeChangesNothing(ulong index)
    {
        Vector vector = LabelledVector(3);

        vector.SetAt(index, Inserted);

        Assert.Equal(ExpectedLabels(3), Render(vector));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// Expected renderings after removing exactly one element of a three-label vector.
    /// </summary>
    public static TheoryData<ulong, string> RemoveAtSurvivors => new()
    {
        { 1UL, "e2|e3" },
        { 2UL, "e1|e3" },
        { 3UL, "e1|e2" },
    };

    /// <summary>
    /// <c>RemoveAt</c> removes exactly the addressed element, one-based, and the survivors keep
    /// their relative order.
    /// </summary>
    /// <remarks>
    /// Index <c>1</c> removes the FIRST element and index <c>3</c> - which is <c>Count()</c> -
    /// removes the last. Accepting <c>Count()</c> is not leniency but the contract, because the
    /// consumer pops with exactly that value [<c>n_cst_dwsvc_columnexp.sru:L318</c>]. Legacy
    /// prototype <c>n_vector.sru:L26</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RemoveAtSurvivors))]
    public void RemoveAtRemovesExactlyTheAddressedElement(ulong index, string expected)
    {
        Vector vector = LabelledVector(3);

        vector.RemoveAt(index);

        Assert.Equal(expected, Render(vector));
        Assert.Equal(2UL, vector.Count());
    }

    /// <summary>
    /// After a removal the survivors re-index one-based with no hole: the range is again
    /// <c>1 .. Count()</c> and nothing sits beyond it.
    /// </summary>
    /// <remarks>
    /// The re-index is asserted position by position rather than through the projection, because
    /// the specific failure being excluded is a surviving element left addressable at its OLD
    /// index - which a rendering that stops at the new <c>Count()</c> would hide.
    /// </remarks>
    [Fact]
    public void RemoveAtLeavesTheSurvivorsReindexedFromOneWithNoHole()
    {
        Vector vector = LabelledVector(3);

        vector.RemoveAt(1);

        Assert.Equal(2UL, vector.Count());
        Assert.Equal(Label(2), vector.GetAt(1));
        Assert.Equal(Label(3), vector.GetAt(2));
        Assert.Null(vector.GetAt(3));
    }

    /// <summary>
    /// An out-of-range index makes <c>RemoveAt</c> a silent no-op.
    /// </summary>
    /// <remarks>
    /// The <c>0</c> case is the one that matters: a caller that mistook the not-found sentinel of
    /// the value search for a valid index removes NOTHING rather than removing the first element.
    /// Vector.cs records that as the reason the sentinel is safe.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IndicesOutsideAThreeElementVector))]
    public void RemoveAtOutOfRangeChangesNothing(ulong index)
    {
        Vector vector = LabelledVector(3);

        vector.RemoveAt(index);

        Assert.Equal(ExpectedLabels(3), Render(vector));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// Expected renderings after inserting before each position of a three-label vector.
    /// </summary>
    public static TheoryData<ulong, string> InsertBeforePlacements => new()
    {
        { 1UL, "X|e1|e2|e3" },
        { 2UL, "e1|X|e2|e3" },
        { 3UL, "e1|e2|X|e3" },
    };

    /// <summary>
    /// <c>InsertBefore</c> puts the value AT the addressed index and shifts the previous occupant
    /// up to <c>index + 1</c>.
    /// </summary>
    /// <remarks>
    /// Both ends are covered by the table: <c>1</c> inserts at the front and <c>3</c> is
    /// <c>Count()</c>. The occupant shift is asserted explicitly as well as through the projection,
    /// because "before" is only unambiguous if the displaced element is named. Legacy prototype
    /// <c>n_vector.sru:L34</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InsertBeforePlacements))]
    public void InsertBeforePlacesTheValueAtTheIndexAndShiftsTheOccupantUp(ulong index, string expected)
    {
        Vector vector = LabelledVector(3);
        object? displaced = vector.GetAt(index);

        vector.InsertBefore(index, Inserted);

        Assert.Equal(expected, Render(vector));
        Assert.Equal(Inserted, vector.GetAt(index));
        Assert.Equal(displaced, vector.GetAt(index + 1));
        Assert.Equal(4UL, vector.Count());
    }

    /// <summary>
    /// Expected renderings after inserting after each position of a three-label vector.
    /// </summary>
    public static TheoryData<ulong, string> InsertAfterPlacements => new()
    {
        { 1UL, "e1|X|e2|e3" },
        { 2UL, "e1|e2|X|e3" },
        { 3UL, "e1|e2|e3|X" },
    };

    /// <summary>
    /// <c>InsertAfter</c> puts the value at <c>index + 1</c>, leaving the addressed element where
    /// it was.
    /// </summary>
    /// <remarks>
    /// Both ends are covered: <c>1</c> inserts into the middle and <c>3</c> is <c>Count()</c>,
    /// where the member coincides with an append. Legacy prototype <c>n_vector.sru:L35</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InsertAfterPlacements))]
    public void InsertAfterPlacesTheValueOneIndexPastTheAddressedElement(ulong index, string expected)
    {
        Vector vector = LabelledVector(3);
        object? addressed = vector.GetAt(index);

        vector.InsertAfter(index, Inserted);

        Assert.Equal(expected, Render(vector));
        Assert.Equal(addressed, vector.GetAt(index));
        Assert.Equal(Inserted, vector.GetAt(index + 1));
        Assert.Equal(4UL, vector.Count());
    }

    /// <summary>
    /// <c>InsertAfter(Count(), value)</c> produces exactly what <c>Append(value)</c> produces, on a
    /// non-empty container.
    /// </summary>
    /// <remarks>
    /// The one place two members of the surface coincide, asserted so that the equivalence is a
    /// checked property rather than a claim in a comment. It holds only for a non-empty container,
    /// which the next test pins from the other side.
    /// </remarks>
    [Fact]
    public void InsertAfterAtCountMatchesAppendOnANonEmptyContainer()
    {
        Vector inserted = LabelledVector(3);
        Vector appended = LabelledVector(3);

        inserted.InsertAfter(inserted.Count(), Inserted);
        appended.Append(Inserted);

        Assert.Equal(Render(appended), Render(inserted));
        Assert.Equal(appended.Count(), inserted.Count());
    }

    /// <summary>
    /// An out-of-range index makes <c>InsertBefore</c> a silent no-op.
    /// </summary>
    [Theory]
    [MemberData(nameof(IndicesOutsideAThreeElementVector))]
    public void InsertBeforeOutOfRangeChangesNothing(ulong index)
    {
        Vector vector = LabelledVector(3);

        vector.InsertBefore(index, Inserted);

        Assert.Equal(ExpectedLabels(3), Render(vector));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// An out-of-range index makes <c>InsertAfter</c> a silent no-op.
    /// </summary>
    /// <remarks>
    /// Note the asymmetry this pins down: <c>InsertAfter(Count())</c> appends, but
    /// <c>InsertAfter(Count() + 1)</c> does nothing. Both members require the index to reference an
    /// EXISTING element, which is what makes the boundary a hard edge rather than a gradual one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IndicesOutsideAThreeElementVector))]
    public void InsertAfterOutOfRangeChangesNothing(ulong index)
    {
        Vector vector = LabelledVector(3);

        vector.InsertAfter(index, Inserted);

        Assert.Equal(ExpectedLabels(3), Render(vector));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// On an EMPTY container every insertion index is out of range, so both insertion members do
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// A direct consequence of both members requiring an existing element: an empty container has
    /// <c>Count()</c> of <c>0</c>, so <c>1</c> is already past the end and <c>0</c> is never valid.
    /// <see cref="Vector.Prepend(object?)"/> and <see cref="Vector.Append(object?)"/> are the
    /// members that seed an empty container, so nothing is unreachable through the surface as a
    /// whole.
    /// </remarks>
    [Fact]
    public void InsertionsOnAnEmptyContainerAreAlwaysOutOfRange()
    {
        Vector vector = new();

        vector.InsertBefore(0, Inserted);
        vector.InsertBefore(1, Inserted);
        vector.InsertAfter(0, Inserted);
        vector.InsertAfter(1, Inserted);

        Assert.Equal(0UL, vector.Count());
        Assert.Equal(string.Empty, Render(vector));
    }

    /// <summary>
    /// The value search returns the ONE-BASED index of the first match.
    /// </summary>
    /// <remarks>
    /// One-based here too, and first-match rather than last: the duplicate at position <c>3</c> is
    /// what makes "first" a checked property. Legacy prototype <c>n_vector.sru:L38</c>.
    /// </remarks>
    [Fact]
    public void PositionOfAValueReturnsTheOneBasedIndexOfTheFirstMatch()
    {
        Vector vector = VectorOf("a", "b", "a");

        Assert.Equal(1UL, vector.Position("a"));
        Assert.Equal(2UL, vector.Position("b"));
    }

    /// <summary>
    /// A miss returns the <c>0</c> SENTINEL; it does not throw.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented, per constraint C-B: this is a sentinel and is deliberately not
    /// upgraded into an expected exception. It is unambiguous precisely BECAUSE indices are
    /// one-based - <c>0</c> can never address an element, which the paired assertion on
    /// <c>GetAt(0)</c> states outright - so a miss can never be confused with a hit at the front.
    /// </remarks>
    [Fact]
    public void PositionOfAnAbsentValueReturnsTheZeroSentinelRatherThanThrowing()
    {
        Vector vector = VectorOf("a", "b");

        Assert.Equal(0UL, vector.Position("absent"));

        // The sentinel is only safe because 0 addresses nothing. Stated here, at the point the
        // sentinel is asserted, rather than left to the reader to connect.
        Assert.Null(vector.GetAt(0));
    }

    /// <summary>
    /// <see langword="null"/> is a searchable VALUE, not a missing argument, and is found at its
    /// one-based position.
    /// </summary>
    [Fact]
    public void PositionFindsANullElementAsASearchableValue()
    {
        Vector vector = VectorOf("a", null, "b");

        Assert.Equal(2UL, vector.Position(null));
    }

    /// <summary>
    /// The search compares by VALUE for boxed scalars and for strings, not by reference.
    /// </summary>
    /// <remarks>
    /// Vector.cs records this as a substantive choice rather than a detail: reference equality
    /// alone would silently fail to find an element that is present, and no count-based assertion
    /// would reveal it. The string is built at run time so the compiler cannot intern it into the
    /// same instance as the literal, which is what makes this a genuine value-equality assertion
    /// rather than an accidental reference match.
    /// </remarks>
    [Fact]
    public void PositionComparesByValueForBoxedScalarsAndStrings()
    {
        string built = new(['a', 'b', 'c']);
        Vector vector = VectorOf(42, built);

        Assert.Equal(1UL, vector.Position(42));
        Assert.Equal(2UL, vector.Position("abc"));
    }

    /// <summary>
    /// One-based positions <c>Move</c> accepts, paired with the element each one addresses.
    /// </summary>
    public static TheoryData<ulong, string> MoveTargets => new()
    {
        { 1UL, "e1" },
        { 2UL, "e2" },
        { 3UL, "e3" },
    };

    /// <summary>
    /// <c>Move</c> takes a ONE-BASED index, so <c>1</c> addresses the first element and
    /// <c>Count()</c> the last.
    /// </summary>
    /// <remarks>
    /// The INDEX semantics of <c>n_vector.sru:L19</c> are asserted here; the cursor state machine
    /// belongs to the sibling class. Since the member's only effect IS the cursor, the position
    /// query and the current-element accessor are used purely as OBSERVATION INSTRUMENTS - the
    /// first reports the raw index that was stored, which is the index arithmetic under test, and
    /// the second confirms that index maps to the expected element. Neither is under test here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MoveTargets))]
    public void MoveAcceptsOneBasedIndices(ulong index, string expectedElement)
    {
        Vector vector = LabelledVector(3);

        vector.Move(index);

        Assert.Equal(index, vector.Position());
        Assert.Equal(expectedElement, vector.Get());
    }

    /// <summary>
    /// <c>Move(0)</c> selects the rewound state rather than the first element.
    /// </summary>
    /// <remarks>
    /// The counterpart of the one-based claim: because <c>0</c> is not an element index, it is free
    /// to mean "before the first element". Asserted as implemented - this is deliberately NOT a
    /// no-op, unlike every other out-of-range index, which is why it is separated from the
    /// boundary theory that follows.
    /// </remarks>
    [Fact]
    public void MoveWithZeroSelectsTheRewoundStateRatherThanTheFirstElement()
    {
        Vector vector = LabelledVector(3);

        vector.Move(2);
        vector.Move(0);

        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());
    }

    /// <summary>
    /// Index values above the last element that <c>Move</c> must ignore.
    /// </summary>
    /// <remarks>
    /// <c>0</c> is deliberately excluded: for this member alone it is meaningful, and the test
    /// above pins it.
    /// </remarks>
    public static TheoryData<ulong> IndicesAboveAThreeElementVector => new() { 4UL, ulong.MaxValue };

    /// <summary>
    /// An index past the last element makes <c>Move</c> a no-op that leaves the position exactly
    /// where it was.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented rather than as a clamp to the last element. Vector.cs records the
    /// reasoning: a subroutine has no failure channel, so between two silent outcomes the least
    /// destructive is the honest one - clamping would leave a subsequent read returning a plausible
    /// WRONG element instead of the element the caller had already selected.
    /// </remarks>
    [Theory]
    [MemberData(nameof(IndicesAboveAThreeElementVector))]
    public void MoveAboveTheLastIndexIsANoOp(ulong index)
    {
        Vector vector = LabelledVector(3);

        vector.Move(2);
        vector.Move(index);

        Assert.Equal(2UL, vector.Position());
        Assert.Equal(Label(2), vector.Get());
    }

    // ==========================================================================================
    //  PHASE 2 - THE REMAINING SURFACE
    //
    //  Growth and shrinkage, the capacity pair, the bulk transfers, and the two algorithms. From
    //  here on the one-based walk established in Phase 1 is used as the reading instrument.
    // ==========================================================================================

    /// <summary>
    /// <c>Append</c> adds at the END, so the new element is the one at index <c>Count()</c>.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L20</c>. The post-condition asserted here is the exact one
    /// the consumer's push idiom depends on [<c>n_cst_dwsvc_columnexp.sru:L296-L297</c>].
    /// </remarks>
    [Fact]
    public void AppendAddsAtTheEndAndRaisesCount()
    {
        Vector vector = VectorOf("a", "b");

        vector.Append("c");

        Assert.Equal(3UL, vector.Count());
        Assert.Equal("a|b|c", Render(vector));
        Assert.Equal("c", vector.GetAt(vector.Count()));
    }

    /// <summary>
    /// Appending, taking <c>Count()</c> as the index of what was just appended, and popping with
    /// that same index removes EXACTLY that element and restores the container.
    /// </summary>
    /// <remarks>
    /// The legacy push and pop idiom reproduced end to end
    /// [<c>n_cst_dwsvc_columnexp.sru:L296-L297</c> and <c>L318</c>], and the single most important
    /// integration property in this class: under a zero-based reading the pop would remove nothing
    /// and the recursion stack would grow on every calculation until the recursion guard at
    /// <c>L684-L688</c> began rejecting legitimate columns.
    /// </remarks>
    [Fact]
    public void AppendThenCountThenRemoveAtReproducesTheLegacyPushAndPopIdiom()
    {
        Vector stack = VectorOf("outer");

        stack.Append("inner");
        ulong pushed = stack.Count();

        Assert.Equal(2UL, pushed);
        Assert.Equal("inner", stack.GetAt(pushed));

        stack.RemoveAt(pushed);

        Assert.Equal(1UL, stack.Count());
        Assert.Equal("outer", Render(stack));
    }

    /// <summary>
    /// <c>Prepend</c> adds at the FRONT and shifts every existing element up by one.
    /// </summary>
    /// <remarks>Legacy prototype <c>n_vector.sru:L33</c>.</remarks>
    [Fact]
    public void PrependAddsAtTheFrontAndShiftsTheRestUp()
    {
        Vector vector = VectorOf("a", "b");

        vector.Prepend("z");

        Assert.Equal(3UL, vector.Count());
        Assert.Equal("z|a|b", Render(vector));
        Assert.Equal("z", vector.GetAt(1));
        Assert.Equal("a", vector.GetAt(2));
    }

    /// <summary>
    /// <c>Count</c> starts at <c>0</c> and tracks every mutation, growth and shrinkage alike.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L12</c>, returning <c>ulong</c>, so every expected value is
    /// a <c>ulong</c> literal - constraint C-B keeps the legacy width rather than narrowing it to a
    /// more convenient <c>int</c>.
    /// </remarks>
    [Fact]
    public void CountStartsAtZeroAndTracksEveryMutation()
    {
        Vector vector = new();

        Assert.Equal(0UL, vector.Count());

        vector.Append("a");
        Assert.Equal(1UL, vector.Count());

        vector.Prepend("b");
        Assert.Equal(2UL, vector.Count());
        Assert.Equal("b|a", Render(vector));

        vector.InsertAfter(1, "c");
        Assert.Equal(3UL, vector.Count());

        vector.InsertBefore(1, "d");
        Assert.Equal(4UL, vector.Count());
        Assert.Equal("d|b|c|a", Render(vector));

        vector.RemoveAt(vector.Count());
        Assert.Equal(3UL, vector.Count());

        Assert.True(vector.Resize(6));
        Assert.Equal(6UL, vector.Count());

        Assert.True(vector.Resize(2));
        Assert.Equal(2UL, vector.Count());
        Assert.Equal("d|b", Render(vector));

        vector.Purge();
        Assert.Equal(0UL, vector.Count());
    }

    /// <summary>
    /// <c>GetFirst</c> and <c>GetLast</c> return the boundary elements of a populated container.
    /// </summary>
    /// <remarks>
    /// Legacy prototypes <c>n_vector.sru:L31</c> and <c>L32</c>. Only the RETURN VALUES are
    /// asserted here; both members also position the cursor, and where it lands belongs to the
    /// sibling class.
    /// </remarks>
    [Fact]
    public void GetFirstAndGetLastReturnTheBoundaryElements()
    {
        Vector vector = LabelledVector(3);

        Assert.Equal(Label(1), vector.GetFirst());
        Assert.Equal(Label(3), vector.GetLast());
    }

    /// <summary>
    /// On an EMPTY container both boundary accessors return <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented: neither throws and neither returns a default-constructed value,
    /// because the legacy prototypes return a value and have no failure channel.
    /// </remarks>
    [Fact]
    public void GetFirstAndGetLastReturnNullOnAnEmptyContainer()
    {
        Vector vector = new();

        Assert.Null(vector.GetFirst());
        Assert.Null(vector.GetLast());
        Assert.Equal(0UL, vector.Count());
    }

    /// <summary>
    /// <c>MaxSize</c> is never below <c>Count</c>, at every point in a mutation sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy prototype <c>n_vector.sru:L13</c>. WHAT THIS MEMBER MEANS FOR THE SUBSTITUTION, and
    /// why the assertion is a relation rather than a number: the legacy value came from the closed
    /// native container, and Vector.cs reads the prototype as ALLOCATED CAPACITY - the natural
    /// partner of <see cref="Vector.Reserve(ulong)"/> - which for this substitution is the capacity
    /// of the backing list. That capacity's absolute value is a base-class-library allocation
    /// detail, not a behavioural contract: growing a list by one element may raise it by any amount
    /// the runtime chooses. Pinning an exact number here would therefore lock a test to an
    /// allocation strategy rather than to the behaviour of this type, so the contract is asserted
    /// instead - it is at least the element count, and reserving raises it. Vector.cs additionally
    /// records that no in-scope consumer calls this member, so the parity risk of the reading is
    /// nil.
    /// </para>
    /// </remarks>
    [Fact]
    public void MaxSizeIsNeverBelowCountAcrossAMutationSequence()
    {
        Vector vector = new();

        Assert.True(vector.MaxSize() >= vector.Count(), "A newly constructed vector reports a capacity below its count.");

        for (int position = 1; position <= 10; position++)
        {
            vector.Append(Label(position));

            Assert.True(vector.MaxSize() >= vector.Count(), "Appending left the reported capacity below the element count.");
        }

        Assert.True(vector.Resize(3));
        Assert.True(vector.MaxSize() >= vector.Count(), "Shrinking left the reported capacity below the element count.");

        vector.Purge();
        Assert.True(vector.MaxSize() >= vector.Count(), "Purging left the reported capacity below the element count.");
    }

    /// <summary>
    /// <c>Reserve</c> raises the reported capacity WITHOUT changing <c>Count</c>, so a vector that
    /// has reserved but holds nothing still reports <c>0</c>.
    /// </summary>
    /// <remarks>
    /// Exactly the confusion this assertion exists to prevent. The only in-scope consumer calls
    /// <c>Reserve(20)</c> immediately after construction
    /// [<c>n_cst_dwsvc_columnexp.sru:L2421-L2422</c>], sizing its recursion stack for the expression
    /// depth it expects, and the stack is EMPTY at that moment. A reading in which reserving
    /// populated twenty slots would break the very first push. Legacy prototype
    /// <c>n_vector.sru:L16</c>.
    /// </remarks>
    [Fact]
    public void ReserveRaisesCapacityWithoutChangingCount()
    {
        Vector vector = new();

        Assert.True(vector.Reserve(20));

        Assert.Equal(0UL, vector.Count());
        Assert.True(vector.MaxSize() >= 20UL, "Reserving twenty elements did not raise the reported capacity to at least twenty.");
        Assert.Equal(string.Empty, Render(vector));
        Assert.Null(vector.GetAt(1));
    }

    /// <summary>
    /// <c>Reserve</c> leaves existing contents and their order completely undisturbed.
    /// </summary>
    [Fact]
    public void ReserveDoesNotDisturbExistingContents()
    {
        Vector vector = LabelledVector(3);

        Assert.True(vector.Reserve(64));

        Assert.Equal(3UL, vector.Count());
        Assert.Equal(ExpectedLabels(3), Render(vector));
    }

    /// <summary>
    /// A request larger than the biggest array this runtime can allocate makes <c>Reserve</c>
    /// report <see langword="false"/> and change nothing.
    /// </summary>
    /// <remarks>
    /// A genuine, reachable failure path rather than a constant <see langword="true"/>: unlike the
    /// subroutines on this surface, this prototype HAS a boolean return, so Vector.cs uses it as a
    /// failure channel instead of throwing. Asserting it is what keeps that branch from being dead
    /// code under constraint C-H.
    /// </remarks>
    [Fact]
    public void ReserveBeyondTheRuntimeArrayLimitReportsFalseAndChangesNothing()
    {
        Vector vector = LabelledVector(2);

        Assert.False(vector.Reserve((ulong)Array.MaxLength + 1UL));

        Assert.Equal(2UL, vector.Count());
        Assert.Equal(ExpectedLabels(2), Render(vector));
    }

    /// <summary>
    /// <c>Purge</c> empties the container.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L14</c>. The cursor reset is asserted here because
    /// Vector.cs defines it FOR THIS MEMBER specifically - purging leaves no element for a position
    /// to mean anything relative to, so it is documented as a reset to the rewound state rather
    /// than a clamp. The general cursor state machine still belongs to the sibling class; the
    /// position query is used here only as the instrument that makes this member's own documented
    /// effect checkable.
    /// </remarks>
    [Fact]
    public void PurgeEmptiesTheContainerAndResetsThePositionToZero()
    {
        Vector vector = LabelledVector(3);
        vector.Move(2);

        vector.Purge();

        Assert.Equal(0UL, vector.Count());
        Assert.Equal(string.Empty, Render(vector));
        Assert.Null(vector.GetAt(1));
        Assert.Equal(0UL, vector.Position());
    }

    /// <summary>
    /// <c>Reverse</c> reverses the one-based walk.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L15</c>. Asserted against the walk rather than against the
    /// backing order, because the walk is what the consumer sees - and it is the walk whose
    /// direction reaches the trace payload at <c>n_cst_dwsvc_columnexp.sru:L753-L758</c>.
    /// </remarks>
    [Fact]
    public void ReverseReversesTheOneBasedWalk()
    {
        Vector vector = LabelledVector(3);

        vector.Reverse();

        Assert.Equal("e3|e2|e1", Render(vector));
        Assert.Equal(Label(3), vector.GetAt(1));
        Assert.Equal(Label(1), vector.GetAt(vector.Count()));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// Element counts at which <c>Reverse</c> and <c>Unique</c> have nothing to do.
    /// </summary>
    public static TheoryData<int> DegenerateLengths => new() { 0, 1 };

    /// <summary>
    /// <c>Reverse</c> on an empty or single-element container changes nothing and does not throw.
    /// </summary>
    [Theory]
    [MemberData(nameof(DegenerateLengths))]
    public void ReverseOnAnEmptyOrSingleElementContainerChangesNothing(int length)
    {
        Vector vector = LabelledVector(length);

        vector.Reverse();

        Assert.Equal((ulong)length, vector.Count());
        Assert.Equal(ExpectedLabels(length), Render(vector));
    }

    /// <summary>
    /// <c>Resize</c> growth raises <c>Count</c> and pads the new slots with <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L17</c>, and one of the two members that separate this type
    /// from its sibling list container. The padding is asserted to be REAL ELEMENTS rather than
    /// merely absent: the value search finds the first of them at its one-based position, which an
    /// out-of-range index could never produce. That distinction matters because
    /// <see cref="Vector.GetAt(ulong)"/> answers <see langword="null"/> both for a padded slot and
    /// for an index past the end, so the count and the search together are what tell them apart.
    /// </remarks>
    [Fact]
    public void ResizeGrowthPadsWithNullAndRaisesCount()
    {
        Vector vector = VectorOf("a", "b");

        Assert.True(vector.Resize(4));

        Assert.Equal(4UL, vector.Count());
        Assert.Equal("a|b|(null)|(null)", Render(vector));
        Assert.Null(vector.GetAt(3));
        Assert.Null(vector.GetAt(4));
        Assert.Equal(3UL, vector.Position(null));
    }

    /// <summary>
    /// <c>Resize</c> shrinkage drops the TAIL and keeps the leading elements in order.
    /// </summary>
    [Fact]
    public void ResizeShrinkDropsTheTail()
    {
        Vector vector = LabelledVector(4);

        Assert.True(vector.Resize(2));

        Assert.Equal(2UL, vector.Count());
        Assert.Equal(ExpectedLabels(2), Render(vector));
        Assert.Null(vector.GetAt(3));
    }

    /// <summary>
    /// <c>Resize(0)</c> empties the container.
    /// </summary>
    [Fact]
    public void ResizeToZeroEmptiesTheContainer()
    {
        Vector vector = LabelledVector(3);

        Assert.True(vector.Resize(0));

        Assert.Equal(0UL, vector.Count());
        Assert.Equal(string.Empty, Render(vector));
    }

    /// <summary>
    /// Resizing to the count the container already holds changes nothing.
    /// </summary>
    [Fact]
    public void ResizeToTheCurrentCountChangesNothing()
    {
        Vector vector = LabelledVector(3);

        Assert.True(vector.Resize(vector.Count()));

        Assert.Equal(3UL, vector.Count());
        Assert.Equal(ExpectedLabels(3), Render(vector));
    }

    /// <summary>
    /// A request larger than the biggest array this runtime can allocate makes <c>Resize</c> report
    /// <see langword="false"/> and change nothing.
    /// </summary>
    /// <remarks>
    /// The counterpart of the reserve failure path, and reachable for the same reason: the
    /// prototype has a boolean return, so an impossible request is reported rather than thrown.
    /// </remarks>
    [Fact]
    public void ResizeBeyondTheRuntimeArrayLimitReportsFalseAndChangesNothing()
    {
        Vector vector = LabelledVector(2);

        Assert.False(vector.Resize((ulong)Array.MaxLength + 1UL));

        Assert.Equal(2UL, vector.Count());
        Assert.Equal(ExpectedLabels(2), Render(vector));
    }

    /// <summary>
    /// Values and the membership answer each one must produce in a three-element vector.
    /// </summary>
    public static TheoryData<string, bool> ExistsCases => new()
    {
        { "e1", true },
        { "e2", true },
        { "e3", true },
        { "e4", false },
        { "E1", false },
        { "", false },
    };

    /// <summary>
    /// <c>Exists</c> reports membership by value.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L18</c>. The <c>E1</c> case pins the comparison as
    /// case-sensitive and the empty-string case pins it as a genuine value comparison rather than a
    /// null-or-empty test.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExistsCases))]
    public void ExistsReportsMembershipByValue(string value, bool expected)
    {
        Vector vector = LabelledVector(3);

        Assert.Equal(expected, vector.Exists(value));
    }

    /// <summary>
    /// <see langword="null"/> is a searchable value for <c>Exists</c> too: present when an element
    /// is <see langword="null"/>, absent when none is.
    /// </summary>
    /// <remarks>
    /// Both directions are asserted, because a membership test that always answered
    /// <see langword="true"/> for <see langword="null"/> - or always <see langword="false"/> -
    /// would pass a one-sided test. The <see langword="null"/>-bearing vector is built through
    /// <see cref="Vector.Resize(ulong)"/> padding as well as by appending, so the two ways a
    /// <see langword="null"/> can enter the container are both covered.
    /// </remarks>
    [Fact]
    public void ExistsTreatsNullAsASearchableValue()
    {
        Vector withoutNull = LabelledVector(2);
        Vector withAppendedNull = VectorOf("a", null);
        Vector withPaddedNull = VectorOf("a");

        Assert.True(withPaddedNull.Resize(2));

        Assert.False(withoutNull.Exists(null));
        Assert.True(withAppendedNull.Exists(null));
        Assert.True(withPaddedNull.Exists(null));
    }

    // ==========================================================================================
    //  PHASE 2 (CONTINUED) - THE BULK TRANSFER SURFACE
    //
    //  The vector-to-vector copy and the vector-to-array copy. The second of these is the other
    //  place in the type where the one-based world meets a zero-based one, so it is the natural
    //  place an off-by-one shows up as a wrong array length or a leading default element.
    // ==========================================================================================

    /// <summary>
    /// <c>CopyFromVector</c> REPLACES the target's contents with the source's, in order, and
    /// returns the resulting element count.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L10</c> declares a <c>long</c> return, so a value is
    /// returned rather than nothing, and Vector.cs documents it as the number of elements copied.
    /// Replace rather than append is the reading recorded there: a copy that left existing elements
    /// in place would make the returned count ambiguous between elements copied and elements held.
    /// The target starts LONGER than the source so that a failure to clear would be visible as a
    /// trailing remnant rather than hidden.
    /// </remarks>
    [Fact]
    public void CopyFromVectorReplacesTheContentsAndReturnsTheNewCount()
    {
        Vector source = VectorOf("a", "b");
        Vector target = VectorOf("x", "y", "z");

        long copied = target.CopyFromVector(source);

        Assert.Equal(2L, copied);
        Assert.Equal(2UL, target.Count());
        Assert.Equal("a|b", Render(target));
    }

    /// <summary>
    /// <c>CopyFromVector</c> leaves the SOURCE untouched.
    /// </summary>
    /// <remarks>
    /// Asserted separately because a copy implemented by moving rather than duplicating would pass
    /// the previous test and silently empty the source.
    /// </remarks>
    [Fact]
    public void CopyFromVectorLeavesTheSourceUntouched()
    {
        Vector source = VectorOf("a", "b");
        Vector target = new();

        target.CopyFromVector(source);

        Assert.Equal(2UL, source.Count());
        Assert.Equal("a|b", Render(source));
    }

    /// <summary>
    /// Copying from an EMPTY source clears the target and returns <c>0</c>.
    /// </summary>
    [Fact]
    public void CopyFromVectorFromAnEmptySourceClearsTheTargetAndReturnsZero()
    {
        Vector target = LabelledVector(3);

        long copied = target.CopyFromVector(new Vector());

        Assert.Equal(0L, copied);
        Assert.Equal(0UL, target.Count());
        Assert.Equal(string.Empty, Render(target));
    }

    /// <summary>
    /// Copying a vector from ITSELF preserves its contents and returns its count.
    /// </summary>
    /// <remarks>
    /// The self-copy case, and it is not academic: Vector.cs snapshots the source before clearing
    /// precisely so that this call does not discard the very elements it is about to copy. An
    /// implementation that cleared first would empty the container and return <c>0</c>, which is
    /// exactly what this assertion excludes.
    /// </remarks>
    [Fact]
    public void CopyFromVectorFromItselfPreservesTheContentsAndReturnsTheCount()
    {
        Vector vector = LabelledVector(3);

        long copied = vector.CopyFromVector(vector);

        Assert.Equal(3L, copied);
        Assert.Equal(3UL, vector.Count());
        Assert.Equal(ExpectedLabels(3), Render(vector));
    }

    /// <summary>
    /// <c>CopyFromVector</c> rejects a <see langword="null"/> source.
    /// </summary>
    /// <remarks>
    /// A parameter contract this SUBSTITUTION introduces rather than a legacy behaviour being
    /// changed - the legacy parameter is a PowerBuilder object reference and the prototype has no
    /// channel to report a bad one - so ordinary .NET conventions govern it and Vector.cs throws.
    /// The null-forgiving operator here is deliberate and is the only way to reach a guard whose
    /// whole purpose is to reject the value the type system otherwise forbids; it is applied to an
    /// ARGUMENT, never to unwrap an element, which is the distinction that keeps element
    /// nullability honest everywhere else in this class.
    /// </remarks>
    [Fact]
    public void CopyFromVectorRejectsANullSource()
    {
        Vector vector = LabelledVector(2);

        Assert.Throws<ArgumentNullException>(() => vector.CopyFromVector(null!));

        Assert.Equal(2UL, vector.Count());
        Assert.Equal(ExpectedLabels(2), Render(vector));
    }

    /// <summary>
    /// <c>CopyToArray</c> produces a zero-based array whose ORDER matches the one-based walk and
    /// whose LENGTH equals <c>Count()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy prototype <c>n_vector.sru:L11</c>, whose <c>ref</c> parameter stays a <c>ref</c>
    /// parameter in the substitution rather than becoming a return value.
    /// </para>
    /// <para>
    /// THE SECOND ONE-BASED BOUNDARY, and the natural place a slip surfaces: an off-by-one shows up
    /// here as an array one element too long, as a leading default element, or as a dropped first
    /// element. All three are excluded - the length is asserted against <c>Count()</c>, slot
    /// <c>0</c> is asserted to hold logical element <c>1</c>, and the mapping
    /// <c>values[i - 1] == GetAt(i)</c> is asserted for every position. The mapping is walked
    /// explicitly rather than compared through the projection so that the index arithmetic being
    /// verified is visible in the test itself.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OneBasedWalkLengths))]
    public void CopyToArrayProducesAZeroBasedArrayMatchingTheOneBasedWalk(int length)
    {
        Vector vector = LabelledVector(length);
        object?[] values = [];

        long written = vector.CopyToArray(ref values);

        Assert.Equal((long)length, written);
        Assert.Equal(vector.Count(), (ulong)values.Length);
        Assert.Equal(Label(1), values[0]);
        Assert.Equal(Label(length), values[length - 1]);

        for (int position = 1; position <= length; position++)
        {
            Assert.Equal(vector.GetAt((ulong)position), values[position - 1]);
        }
    }

    /// <summary>
    /// <c>CopyToArray</c> OVERWRITES whatever array it is handed rather than appending to it or
    /// reusing it.
    /// </summary>
    /// <remarks>
    /// The incoming array is deliberately a different length from the result, so that a reuse or an
    /// append would leave a remnant this assertion catches.
    /// </remarks>
    [Fact]
    public void CopyToArrayOverwritesAnyIncomingArray()
    {
        Vector vector = LabelledVector(3);
        object?[] values = ["stale", "stale", "stale", "stale", "stale"];

        long written = vector.CopyToArray(ref values);

        Assert.Equal(3L, written);
        Assert.Equal(3, values.Length);
        Assert.DoesNotContain("stale", values);
    }

    /// <summary>
    /// On an EMPTY container <c>CopyToArray</c> yields an empty array rather than
    /// <see langword="null"/>, and returns <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The incoming array is non-empty, so the emptiness asserted afterwards is the result of the
    /// call rather than of the starting value. Asserting emptiness also settles the non-null claim
    /// Vector.cs makes, because an emptiness assertion cannot pass on a null reference - which
    /// keeps the check honest without a redundant null test on a non-nullable local.
    /// </remarks>
    [Fact]
    public void CopyToArrayOnAnEmptyContainerYieldsAnEmptyArray()
    {
        Vector vector = new();
        object?[] values = ["stale"];

        long written = vector.CopyToArray(ref values);

        Assert.Equal(0L, written);
        Assert.Empty(values);
    }

    // ==========================================================================================
    //  PHASE 2 (CONTINUED) - THE TWO ALGORITHMS
    //
    //  Duplicate collapsing and both sort forms. Every expectation below was measured against the
    //  compiled assembly before it was written down, per constraint C-B: neither algorithm is
    //  asserted as "what a sensible container would do".
    // ==========================================================================================

    /// <summary>
    /// <c>Unique</c> collapses only CONSECUTIVE runs of equal elements, keeping the FIRST of each
    /// run, and leaves a scattered repeat in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy prototype <c>n_vector.sru:L41</c>. ASSERTED AS IMPLEMENTED, AND THE DISTINCTION IS
    /// THE WHOLE POINT: this is NOT a global distinct. Vector.cs records the reading - the surface
    /// as a whole is a facade over the C++ standard vector and its algorithms, and in that
    /// vocabulary this member removes ADJACENT duplicates only. The trailing <c>a</c> in the input
    /// therefore SURVIVES, because the <c>b</c> before it breaks the run. A global-distinct
    /// implementation would produce three elements here instead of four, so this single assertion
    /// separates the two readings.
    /// </para>
    /// <para>
    /// The practical consequence for a caller is that sorting normally precedes this member, since
    /// sorting is what brings equal elements together; calling it on unsorted data is not a defect.
    /// </para>
    /// </remarks>
    [Fact]
    public void UniqueCollapsesOnlyConsecutiveRunsAndLeavesScatteredRepeatsInPlace()
    {
        Vector vector = VectorOf("a", "a", "b", "a", "a", "a", "c");

        vector.Unique();

        Assert.Equal(4UL, vector.Count());
        Assert.Equal("a|b|a|c", Render(vector));
    }

    /// <summary>
    /// It is the FIRST occurrence of a run that survives, not the last.
    /// </summary>
    /// <remarks>
    /// Value equality alone cannot answer which occurrence remained, since the survivors compare
    /// equal either way. The two strings are therefore built at run time so they are equal by value
    /// and distinct by reference, and the surviving element is asserted to be the same INSTANCE as
    /// the first of the run. That is what makes the survivor rule a checked property rather than an
    /// assumption, which the brief for this file explicitly asks for.
    /// </remarks>
    [Fact]
    public void UniqueKeepsTheFirstOccurrenceOfEachRun()
    {
        string first = new(['d', 'u', 'p']);
        string second = new(['d', 'u', 'p']);
        Vector vector = VectorOf(first, second, "tail");

        vector.Unique();

        Assert.Equal(2UL, vector.Count());
        Assert.Same(first, vector.GetAt(1));
        Assert.Equal("tail", vector.GetAt(2));
    }

    /// <summary>
    /// <c>Unique</c> compares by VALUE, so a run of equal boxed scalars collapses, and a run of
    /// <see langword="null"/> elements collapses too.
    /// </summary>
    /// <remarks>
    /// The same default equality comparer the value search uses, which is why the two members can
    /// never disagree about what "equal" means.
    /// </remarks>
    [Fact]
    public void UniqueUsesValueEqualityAndCollapsesRunsOfNull()
    {
        Vector boxed = VectorOf(1, 1, 2);
        Vector nulls = VectorOf(null, null, "a");

        boxed.Unique();
        nulls.Unique();

        Assert.Equal(2UL, boxed.Count());
        Assert.Equal("1|2", Render(boxed));

        Assert.Equal(2UL, nulls.Count());
        Assert.Equal("(null)|a", Render(nulls));
    }

    /// <summary>
    /// <c>Unique</c> on an empty or single-element container changes nothing and does not throw.
    /// </summary>
    /// <remarks>
    /// The single-element case matters structurally: the first element has no predecessor to be a
    /// duplicate of, so it must always be kept.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DegenerateLengths))]
    public void UniqueOnAnEmptyOrSingleElementContainerChangesNothing(int length)
    {
        Vector vector = LabelledVector(length);

        vector.Unique();

        Assert.Equal((ulong)length, vector.Count());
        Assert.Equal(ExpectedLabels(length), Render(vector));
    }

    /// <summary>
    /// <c>Sort()</c> orders elements with the default comparer.
    /// </summary>
    /// <remarks>
    /// Legacy prototype <c>n_vector.sru:L39</c>, the parameterless form. Boxed integers are used
    /// because their natural ordering is culture-independent, which keeps the assertion about the
    /// member rather than about a collation table.
    /// </remarks>
    [Fact]
    public void SortOrdersElementsWithTheDefaultComparer()
    {
        Vector vector = VectorOf(3, 1, 2);

        vector.Sort();

        Assert.Equal("1|2|3", Render(vector));
        Assert.Equal(3UL, vector.Count());
    }

    /// <summary>
    /// <c>Sort()</c> places <see langword="null"/> BEFORE every non-null element.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented rather than as "nulls are undefined": the default comparer treats
    /// <see langword="null"/> as smaller than everything, and since the legacy element type is
    /// itself nullable this is a reachable, ordinary case rather than an edge one.
    /// </remarks>
    [Fact]
    public void SortPlacesNullBeforeEveryNonNullElement()
    {
        Vector vector = VectorOf("b", null, "a");

        vector.Sort();

        Assert.Equal("(null)|a|b", Render(vector));
    }

    /// <summary>
    /// <c>Sort()</c> throws when two elements cannot be compared with one another.
    /// </summary>
    /// <remarks>
    /// This one IS an exception, and it is asserted because it is what the implementation does -
    /// not because a bad index throws anywhere else on this surface, which it does not. Vector.cs
    /// records the reasoning: the base class library's own behaviour for a heterogeneous collection
    /// is allowed to propagate rather than being swallowed or pre-empted by a type check, because
    /// papering over it would substitute an invented ordering for a clear failure.
    /// </remarks>
    [Fact]
    public void SortThrowsWhenTwoElementsCannotBeComparedWithOneAnother()
    {
        Vector vector = VectorOf(1, "a");

        Assert.Throws<InvalidOperationException>(() => vector.Sort());
    }

    /// <summary>
    /// The comparator form of the sort applies an INJECTED comparison delegate.
    /// </summary>
    /// <remarks>
    /// This delegate is the SUBSTITUTE for the legacy method-name-string dispatch at
    /// <c>n_vector.sru:L40</c>, which handed the sort an object plus the name of one of its
    /// functions and resolved that name at run time on every comparison. Reproducing that would
    /// mean building dynamic invocation by name, and the plan assigns the legacy dynamic-invocation
    /// family to a deferred service, so constraint C-D rules it out. Both an ascending and a
    /// descending comparison are driven through the same container so that the ordering is
    /// demonstrably the delegate's and not the elements' natural one.
    /// </remarks>
    [Fact]
    public void SortWithAComparisonDelegateAppliesTheInjectedOrdering()
    {
        Vector vector = VectorOf("b", "a", "c");

        vector.Sort((left, right) => string.CompareOrdinal(TextOf(left), TextOf(right)));

        Assert.Equal("a|b|c", Render(vector));

        vector.Sort((left, right) => string.CompareOrdinal(TextOf(right), TextOf(left)));

        Assert.Equal("c|b|a", Render(vector));
    }

    /// <summary>
    /// The comparator form also accepts a comparer OBJECT, and honours a reverse ordering that
    /// contradicts the elements' natural one.
    /// </summary>
    /// <remarks>
    /// The reverse case is what proves the injected comparison is actually consulted: boxed
    /// integers already sort ascending under the default comparer, so a descending result cannot be
    /// produced by ignoring the argument.
    /// </remarks>
    [Fact]
    public void SortWithAComparerAppliesTheInjectedOrderingIncludingAReverseOne()
    {
        Vector ascending = VectorOf("b", "a", "c");
        Vector descending = VectorOf("b", "a", "c");

        ascending.Sort(new OrdinalTextComparer(reverse: false));
        descending.Sort(new OrdinalTextComparer(reverse: true));

        Assert.Equal("a|b|c", Render(ascending));
        Assert.Equal("c|b|a", Render(descending));
    }

    /// <summary>
    /// The comparator form groups elements the comparator calls EQUAL and preserves every element,
    /// but it makes no promise about their relative order inside a group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STABILITY-REVEALING CASE, and what it reveals is measured rather than assumed: the sort
    /// is NOT stable. Twenty elements over two comparator-equal groups were sorted against the
    /// compiled assembly and came back with the members of each group in an order that is not their
    /// insertion order, because the backing list's sort falls back to an unstable algorithm above a
    /// small-input threshold. A six-element run of the same shape happened to come back in
    /// insertion order.
    /// </para>
    /// <para>
    /// So this test asserts the two properties that ARE the contract - every element the comparator
    /// ranks lower precedes every element it ranks higher, and no element is lost, duplicated or
    /// invented - and deliberately does NOT pin the intra-group order. Pinning the permutation
    /// observed today would lock the test to a base-class-library sorting strategy rather than to
    /// the behaviour of this type, and would fail on a future runtime for no behavioural reason.
    /// Recording the measurement here is what keeps the omission a documented decision rather than
    /// a gap, and a caller who needs order-preserving grouping must sort on a total key.
    /// </para>
    /// </remarks>
    [Fact]
    public void SortWithAComparerGroupsEqualElementsWithoutPromisingTheirRelativeOrder()
    {
        const int groupSize = 10;

        Vector vector = new();
        List<string> inserted = [];

        for (int index = 0; index < groupSize * 2; index++)
        {
            // Two comparator-equal groups interleaved on insertion, each element still uniquely
            // identifiable by its suffix so that loss or duplication is detectable.
            string element = (index % 2 == 0 ? "k0" : "k1") + "-" + (char)('a' + index);

            inserted.Add(element);
            vector.Append(element);
        }

        vector.Sort(new KeyPrefixComparer());

        Assert.Equal((ulong)inserted.Count, vector.Count());

        for (ulong position = 1; position <= groupSize; position++)
        {
            Assert.StartsWith("k0", Assert.IsType<string>(vector.GetAt(position)), StringComparison.Ordinal);
        }

        for (ulong position = groupSize + 1; position <= vector.Count(); position++)
        {
            Assert.StartsWith("k1", Assert.IsType<string>(vector.GetAt(position)), StringComparison.Ordinal);
        }

        foreach (string element in inserted)
        {
            Assert.True(vector.Exists(element), "Sorting lost the element " + element + ".");
        }
    }

    /// <summary>
    /// Both comparator forms reject a <see langword="null"/> comparison.
    /// </summary>
    /// <remarks>
    /// As with the vector-to-vector copy, this is a parameter contract the SUBSTITUTION introduces
    /// rather than a legacy behaviour being altered: the legacy prototype could not have had an
    /// opinion about a managed comparer reference. The casts are needed to select each overload
    /// unambiguously, and the null-forgiving operator is again applied only to an argument whose
    /// rejection is the behaviour under test.
    /// </remarks>
    [Fact]
    public void SortRejectsANullComparerAndANullComparison()
    {
        Vector vector = LabelledVector(2);

        Assert.Throws<ArgumentNullException>(() => vector.Sort((IComparer<object?>)null!));
        Assert.Throws<ArgumentNullException>(() => vector.Sort((Comparison<object?>)null!));

        Assert.Equal(ExpectedLabels(2), Render(vector));
    }

    /// <summary>
    /// Reads an element as text for the comparison cases, unwrapping the nullable
    /// <see langword="object"/> element type deliberately.
    /// </summary>
    /// <param name="value">The element to read.</param>
    /// <returns>
    /// The element's text, or the empty string when the element is <see langword="null"/> or is not
    /// text.
    /// </returns>
    /// <remarks>
    /// Elements are <see langword="object"/> that may be <see langword="null"/>, so this unwraps
    /// through a pattern rather than suppressing the nullability with a null-forgiving operator.
    /// Mapping a non-text or <see langword="null"/> element to the empty string keeps the injected
    /// comparisons total, so a comparison case can never fail for a reason unrelated to ordering.
    /// </remarks>
    private static string TextOf(object? value) => value as string ?? string.Empty;

    /// <summary>
    /// Orders elements by their text, ascending or descending. The comparer-object half of the
    /// substitution for the legacy method-name-string sort at <c>n_vector.sru:L40</c>.
    /// </summary>
    /// <param name="reverse">
    /// <see langword="true"/> to order descending, which is what proves the injected comparison is
    /// consulted rather than ignored in favour of the natural ordering.
    /// </param>
    /// <remarks>
    /// Hand-written rather than taken from a package: the plan's excluded-package list rules out
    /// mocking, assertion and auto-fixture libraries, and a comparison this small needs none of
    /// them. Ordinal comparison keeps the expected ordering independent of the current culture.
    /// </remarks>
    private sealed class OrdinalTextComparer(bool reverse) : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            int ordering = string.CompareOrdinal(TextOf(x), TextOf(y));

            return reverse ? -ordering : ordering;
        }
    }

    /// <summary>
    /// Orders elements by the first two characters of their text only, so that elements sharing a
    /// prefix compare EQUAL. Used to reveal what the sort does with a group of equal elements.
    /// </summary>
    /// <remarks>
    /// A deliberately partial ordering: it is what makes the grouping observable while leaving the
    /// intra-group order entirely to the sort, which is precisely the property under examination.
    /// </remarks>
    private sealed class KeyPrefixComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y) => string.CompareOrdinal(KeyOf(x), KeyOf(y));

        private static string KeyOf(object? value)
        {
            string text = TextOf(value);

            return text.Length >= 2 ? text[..2] : text;
        }
    }
}
