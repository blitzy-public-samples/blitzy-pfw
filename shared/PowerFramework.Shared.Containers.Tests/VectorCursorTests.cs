// ==============================================================================================
//  VectorCursorTests - the stateful cursor of PowerFramework.Shared.Containers.Vector, and its
//  use as the column-expression engine's calculation and recursion stack
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST   shared/PowerFramework.Shared.Containers/Vector.cs
//  LEGACY DECLARATIONS ws_objects/pfw.utility.container.pbl.src/n_vector.sru               (54 L)
//                      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//                                                                                       (2,435 L)
//                      The first is a PROTOTYPE LIST with no bodies and settles signatures only.
//                      The second is REAL PowerScript and is genuinely readable, so the stack
//                      USAGE PATTERN it demonstrates is traceable evidence even though the
//                      vector's own per-member behaviour is not. That asymmetry is why the two
//                      are tiered separately below rather than cited as one authority.
//
//  DP-7 EVIDENCE STATUS - READ THIS BEFORE CITING ANY ASSERTION AS LEGACY PARITY
//  --------------------------------------------------------------------------------------------
//  THIS SUITE IS NOT GOLDEN-MASTER PROOF. It is a target regression guard. No recording of the
//  legacy's behaviour exists, so nothing here was compared against one:
//
//    TIER 1  TRACEABLE - settled by a cited ws_objects/** locator. Two distinct kinds qualify:
//            signatures from n_vector.sru's prototypes, AND the stack-usage sequence read
//            directly out of n_cst_dwsvc_columnexp.sru, which has real bodies. The latter is the
//            strongest evidence in this file, because it shows how the legacy actually DRIVES the
//            cursor rather than merely how the cursor is declared.
//    TIER 3  TARGET-CHARACTERIZED - settled by the port, because no legacy body defines it. The
//            cursor's own state machine is here: where the cursor sits after each mutation, what
//            an exhausted or unpositioned cursor reports, and how reset interacts with an
//            in-progress walk. DEFINED, REPRODUCIBLE AND COVERED - never verified.
//
//  THE ORACLE-CAPTURE PREREQUISITE. Promoting a TIER 3 assertion to parity evidence needs a paired
//  recording under characterization/recordings/{legacy,dotnet}/<workflowId>/, taken against one
//  unrecreated persistence-db volume state. Until then no TIER 3 expectation here may be reported
//  as verified legacy behaviour. IF A RECORDING LATER CONTRADICTS ONE, THE RECORDING WINS and the
//  test is corrected rather than defended.
//
//  WHY THIS FILE EXISTS SEPARATELY FROM ITS SIBLING
//  --------------------------------------------------------------------------------------------
//  Vector has two independent halves and they fail in different ways. The indexed half - GetAt,
//  SetAt, RemoveAt, InsertBefore, InsertAfter, Position(value), CopyFromVector, CopyToArray,
//  MaxSize, Purge, Reverse, Reserve, Resize, Exists, Append and Prepend as plain mutators,
//  GetFirst, GetLast, Sort and Unique - belongs to VectorTests. THIS file owns the CURSOR half
//  and the stack idiom built on it, and nothing here re-asserts a contract that belongs next
//  door. Where an indexed member appears below it appears as an INSTRUMENT - a one-based GetAt
//  walk is how the effect of an at-cursor write is read back, and Append plus RemoveAt are how
//  the legacy consumer pushes and pops - never as the subject of the assertion.
//
//  VECTOR IS A SUBSTITUTION, NOT A PORT, AND THE CURSOR IS THE PART MOST AT RISK
//  --------------------------------------------------------------------------------------------
//  The legacy type is a PBNI binding: n_vector.sru:L8 reads
//
//      global type n_vector from nonvisualobject native "pfw.dll"
//
//  and the file carries the 33 prototypes at L9-L41 and NOT ONE LINE of implementation. Every
//  line of legacy behaviour lives inside the closed-source pfw.dll, for which no C++ source
//  exists anywhere in this repository, so the decision recorded for this type is SUBSTITUTE onto
//  base class library types rather than transliterate.
//
//  The substituted storage is System.Collections.Generic.List<object?> - and a plain
//  System.Collections.Generic.List<T> HAS NO CURSOR AT ALL. That single fact is why this file
//  exists. Of the whole substituted surface the cursor is the part with the least support from
//  the underlying base class library type and therefore the most room to be quietly dropped: a
//  later "simplification" to a bare list would compile, would keep every element count correct,
//  and would break the column-expression engine's recursion tracking. These tests are what make
//  that simplification fail loudly instead.
//
//  Eleven members read or write the single position that persists between calls. The ten this
//  file owns, with the prototype each was taken from:
//
//      n_vector.sru:L19   subroutine move(ulong index)      Move(ulong)      reposition
//      n_vector.sru:L21   any get()                         Get()            READ AT THE CURSOR
//      n_vector.sru:L23   subroutine set(any value)         Set(object?)     WRITE AT THE CURSOR
//      n_vector.sru:L25   subroutine remove()               Remove()         DELETE AT THE CURSOR
//      n_vector.sru:L27   any getnext()                     GetNext()        advance, then read
//      n_vector.sru:L28   any getprevious()                 GetPrevious()    retreat, then read
//      n_vector.sru:L29   any getandnext()                  GetAndNext()     read, then advance
//      n_vector.sru:L30   subroutine rewind()               Rewind()         reset the cursor
//      n_vector.sru:L36   boolean hasnext()                 HasNext()        forward-walk guard
//      n_vector.sru:L37   ulong position()                  Position()       observe the cursor
//
//  THE NO-ARG TRIO MEANS "AT THE CURSOR", AND THE DECLARATION PROVES IT STRUCTURALLY
//  --------------------------------------------------------------------------------------------
//  In n_vector.sru each no-arg form sits directly beside its positional counterpart:
//
//      L21  any get()                            L22  any getat(ulong index)
//      L23  subroutine set(any value)            L24  subroutine setat(ulong index, any value)
//      L25  subroutine remove()                  L26  subroutine removeat(ulong index)
//
//  Two forms of one operation exist for exactly one reason: one targets the cursor and the other
//  targets an explicit index. A test that let Get() mean "the first element" would pass against
//  a cursor-free reimplementation and would therefore endorse the collapse this file is written
//  to prevent, so every at-cursor assertion below is taken with the cursor STRICTLY INTERIOR -
//  neither on the head nor on the tail - over four elements with distinct values, so that a
//  head/tail confusion cannot pass by coincidence.
//
//  ONE-BASED INDEXING IS A DELIBERATE FIDELITY DECISION, AND IT IS OBSERVABLE ON THE WIRE
//  --------------------------------------------------------------------------------------------
//  One-based to zero-based translation is named the single most dangerous mechanical hazard in
//  this refactor, and for this type the sole in-scope consumer settles it mechanically rather
//  than by convention. In n_cst_dwsvc_columnexp.sru:
//
//      L110         n_vector _vecCalcStack                  the calculation and recursion stack
//      L296         _vecCalcStack.Append(colName)           PUSH
//      L297         k = _vecCalcStack.Count()               k is the index OF THE JUST-PUSHED
//                                                           frame, with no adjustment whatever
//      L316         _of_CalcItem(row,n)                     the recursive call - pushes NEST
//      L318         _vecCalcStack.RemoveAt(k)               POP exactly that frame
//      L684-L688    nCount = Count() : for nIndex = 1 to nCount :
//                   if GetAt(nIndex) = ColExpDatas[index].name then return false : end if
//                                                           the recursion guard
//      L753-L755    nCount = Count() : for nIndex = 1 to nCount :
//                   sCallStack += GetAt(nIndex) + ">"       the trace call stack
//      L757         sCallStack += ColExpDatas[index].dwo.name
//      L758         #DataWindow.Event OnColumnExpTrace(row, dwo, sCallStack, sExp, ...)
//      L2421-L2422  _vecCalcStack = Create n_vector : Reserve(20)
//
//  Under zero-based indexing RemoveAt(Count()) would be one past the end, so every push would be
//  followed by a pop of nothing and the recursion stack would grow without bound until the guard
//  at L684-L688 began rejecting legitimate columns. The last element's index therefore EQUALS
//  Count() and valid indices run 1 .. Count() inclusive - a fidelity decision, not an accident,
//  and one this file pins rather than tidies.
//
//  The stakes are not internal. The string the L753-L755 loop builds is completed at L757 and
//  handed straight to OnColumnExpTrace(row, dwo, sCallStack, sExp, ...) at L758, which is carried
//  on the expression-trace channel of cross-service contract C-04. THE BOTTOM-TO-TOP ORDERING ASSERTED
//  IN THE STACK SECTION BELOW IS THEREFORE AN OBSERVABLE WIRE PAYLOAD. A reversed or zero-based
//  enumeration would corrupt it while every element count still matched, which is precisely the
//  class of silent regression a count-based assertion can never catch.
//
//  ASSERT WHAT IS IMPLEMENTED, NOT WHAT WOULD BE CONVENIENT
//  --------------------------------------------------------------------------------------------
//  Because n_vector.sru is signatures only, the oracle constrains the SHAPE of each member and
//  not its edge behaviour; Vector.cs labels each such choice a DOCUMENTED INTERPRETATION at its
//  point of reproduction. There is consequently no disagreement between the legacy declaration
//  and the implementation to report - the declaration is silent on all of it - and this file
//  records the implemented behaviour verbatim so that a later characterization recording can
//  contradict one of those interpretations DELIBERATELY, by turning a red test, rather than by
//  accident. Nothing below upgrades a null sentinel into an exception, corrects a silent clamp
//  into a throw, normalises the cursor into something more predictable than it is, or merges
//  GetAndNext with GetNext because the two look similar. The observed edges, all pinned below:
//
//      * a freshly constructed Vector is REWOUND, so Position() is 0 and Get() is null
//      * running off the end returns null and SETTLES the cursor on the last element
//      * running past the front returns null and settles the cursor rewound at 0
//      * GetAndNext alone can leave the cursor one PAST the end, at Count() + 1
//      * an out-of-range Move is a NO-OP that leaves the previous position intact
//      * every shrinking mutation KEEPS the cursor's number and clamps it down if it now
//        exceeds the count - a silent clamp, never a throw and never a reset to 0
//
//  GOVERNING CONSTRAINTS
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line, "No user rules provided", so NO USER RULE GOVERNS THIS
//  FILE. That is a finding and not an omission - the plan's own scope section records
//  "Rule-Mandated Files: None" - and enterprise-standard best practice applies in their place.
//  The binding constraints are the plan's own inventory, honoured here exactly as rules would
//  have been, and nothing below is an invented rule:
//
//  C-B  No behaviour improvement. The cursor's edge behaviour is asserted AS OBSERVED, per the
//       paragraph above.
//  C-C  The legacy tree is read only and is the behavioural oracle. It is cited by path and line
//       in comments ONLY. No test here opens, reads, parses or writes anything under
//       ws_objects/**, or anything else at all: these are pure in-memory tests with ZERO file
//       I/O, no File, Path or Directory use, no temporary directory and no fixture on disk.
//  C-D  The four deferred services are not implemented, even partially. The deferred sibling
//       container declared at n_vector.sru:L9 - copyfromlist's parameter type, which belongs to
//       the deferred Documents capability - is not named, referenced or used anywhere below, and
//       the stack is modelled with Vector's OWN members rather than by reaching for a list type.
//  C-H  The per-project line-coverage gate. Every one of the ten owned members is exercised with
//       behavioural assertions rather than a smoke call, because together with the sibling class
//       this is all the coverage Vector gets. Nullable reference types and warnings-as-errors are
//       inherited from the repository-root Directory.Build.props and are not relaxed here.
//  C-K  Every technology-specific and boundary-specific decision is documented where it is
//       exercised: the substitution and the base class library type it rests on, the absent
//       cursor in a plain list, the one-based fidelity decision with its locators, the C-04
//       trace-payload consequence, and each pinned interpretation.
//
//  Constraints with no subject matter here, recorded so nothing is invented to satisfy them: C-A
//  and C-I and C-J (a test project for a pure library is neither a service nor a deployable),
//  C-E (no database), C-F (no configuration, no secret and no credential-shaped literal), C-G
//  (no boundary is opened, so there is nothing to authenticate).
//
//  STYLE AND ISOLATION
//  --------------------------------------------------------------------------------------------
//  Ordinary C# naming throughout. The repository-root .editorconfig scopes its naming-analyzer
//  suppressions to the closed BAND 3 roster of files that genuinely carry preserved legacy constants
//  - that roster being the single source of truth for the list -
//  and this file is not among them; with warnings as errors a SCREAMING_SNAKE or underscore-laden
//  identifier here would be a build error, so legacy spellings appear only in string literals,
//  comments and locators.
//
//  EVERY TEST CONSTRUCTS ITS OWN Vector. There is no static, shared or fixture-held instance,
//  because cursor state is exactly the thing that would leak between tests and a leak would show
//  up as an order-dependent pass. The legacy's own global auto-instance at n_vector.sru:L43 -
//  `global n_vector n_vector`, which shadows its own type name - is a hazard the substitution
//  deliberately does not reproduce, and this file does not reintroduce it. No mocking, assertion
//  or auto-fixture package is used either; xunit.v3 already carries Assert.
// ==============================================================================================

using System.Text;
using PowerFramework.Shared.Containers;
using Xunit;

namespace PowerFramework.Shared.Containers.Tests;

/// <summary>
/// Behavioural tests for the stateful cursor of <see cref="Vector"/> and for its use as the
/// column-expression engine's calculation and recursion stack.
/// </summary>
/// <remarks>
/// See the file header for the full rationale. In short: <see cref="Vector"/> is a substitution
/// onto <c>System.Collections.Generic.List&lt;object?&gt;</c> rather than a port of the closed
/// native <c>n_vector</c>, a plain list has no cursor, and the cursor is therefore the
/// substituted behaviour most at risk of being dropped. The sibling <c>VectorTests</c> owns the
/// indexed and bulk surface; this class owns the cursor and the stack idiom.
/// </remarks>
public sealed class VectorCursorTests
{
    // ------------------------------------------------------------------------------------------
    //  Fixture vocabulary
    //  ----------------------------------------------------------------------------------------
    //  Four DISTINCT values, so that a head/tail/interior confusion cannot pass by coincidence,
    //  and enough of them that the cursor can sit STRICTLY INTERIOR with an element on each side.
    // ------------------------------------------------------------------------------------------

    /// <summary>Element 1 of the four-element fixture - the head.</summary>
    private const string Alpha = "alpha";

    /// <summary>Element 2 of the four-element fixture - interior.</summary>
    private const string Bravo = "bravo";

    /// <summary>Element 3 of the four-element fixture - interior.</summary>
    private const string Charlie = "charlie";

    /// <summary>Element 4 of the four-element fixture - the tail.</summary>
    private const string Delta = "delta";

    /// <summary>The value written by an at-cursor or positional overwrite.</summary>
    private const string Replaced = "replaced";

    /// <summary>The value added by an insertion, distinct from every fixture element.</summary>
    private const string Inserted = "inserted";

    /// <summary>The value added by a prepend, distinct from every fixture element.</summary>
    private const string Prepended = "prepended";

    /// <summary>
    /// The one-based index of the STRICTLY INTERIOR position used by the at-cursor tests: element
    /// 3 of 4, so there are elements both before and after it.
    /// </summary>
    private const ulong InteriorIndex = 3UL;

    /// <summary>
    /// A four-element vector holding <see cref="Alpha"/>, <see cref="Bravo"/>,
    /// <see cref="Charlie"/> and <see cref="Delta"/> at one-based indices 1 to 4, with the cursor
    /// in the state a freshly constructed <see cref="Vector"/> has: rewound.
    /// </summary>
    /// <returns>A brand new instance. Never a shared or cached one - see the file header.</returns>
    /// <remarks>
    /// Built with <c>Append</c>, which is the same member the legacy consumer pushes with
    /// [n_cst_dwsvc_columnexp.sru:L296], so the fixture's element order is also its push order.
    /// </remarks>
    private static Vector FourDistinctElements()
    {
        Vector vector = new();

        vector.Append(Alpha);
        vector.Append(Bravo);
        vector.Append(Charlie);
        vector.Append(Delta);

        return vector;
    }

    /// <summary>
    /// Deliberately unwraps an element read out of a <see cref="Vector"/>, asserting on the way
    /// that it really is the <see cref="string"/> the fixture put there.
    /// </summary>
    /// <param name="element">
    /// The value returned by <see cref="Vector.Get"/>, <see cref="Vector.GetAt(ulong)"/> or one of
    /// the walking members - typed <c>object?</c> because the legacy element type is <c>any</c>.
    /// </param>
    /// <returns>The element as a <see cref="string"/>.</returns>
    /// <remarks>
    /// The legacy surface stores <c>any</c>, which maps to <c>object?</c>, so every read has to be
    /// unwrapped somewhere. It is done HERE, once, through an assertion rather than through a
    /// null-forgiving operator: a wrong type then fails as a test failure naming the value instead
    /// of as a NullReferenceException from inside an assertion argument.
    /// </remarks>
    private static string ElementText(object? element) => Assert.IsType<string>(element);

    /// <summary>
    /// Renders the whole vector as a space-separated, bottom-first snapshot by walking the
    /// one-based indices <c>1 .. Count()</c> inclusive.
    /// </summary>
    /// <param name="vector">The vector to render. Its cursor is not moved.</param>
    /// <returns>
    /// The elements in index order separated by single spaces, or the empty string when the vector
    /// is empty.
    /// </returns>
    /// <remarks>
    /// A verification instrument, not a subject: it exists so that an at-cursor write can be
    /// checked to have left every OTHER element untouched, which is the assertion that
    /// distinguishes a cursor-targeted operation from a whole-container one. The inclusive
    /// <c>1 .. Count()</c> bound is the one-based contract from the file header, and reading
    /// through <see cref="Vector.GetAt(ulong)"/> deliberately leaves the cursor alone so a
    /// snapshot can be taken mid-test without perturbing what is under test.
    /// </remarks>
    private static string Snapshot(Vector vector)
    {
        StringBuilder rendered = new();
        ulong count = vector.Count();

        for (ulong index = 1UL; index <= count; index++)
        {
            if (rendered.Length > 0)
            {
                rendered.Append(' ');
            }

            rendered.Append(ElementText(vector.GetAt(index)));
        }

        return rendered.ToString();
    }

    // ==========================================================================================
    //  PART 1 - THE CURSOR IS STATEFUL, SO IT IS ASSERTED AS STATE
    //  ----------------------------------------------------------------------------------------
    //  Every test in this part reads Position() alongside the value, because Position() is the
    //  only member that can distinguish "the cursor is not on an element" from "the element under
    //  the cursor is null" - Get() returns null for both. Asserting the value alone would leave
    //  the cursor's movement unverified, which is the half a cursor-free reimplementation would
    //  get wrong.
    // ==========================================================================================

    /// <summary>
    /// A freshly constructed <see cref="Vector"/> is REWOUND: the cursor sits BEFORE the first
    /// element at position <c>0</c> rather than on element <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Pinned rather than assumed. Starting rewound is what makes the documented forward-walk
    /// idiom - rewind, then <c>while (HasNext()) GetNext()</c> - visit element 1 on its opening
    /// iteration; a cursor that started ON element 1 would make that same loop skip it. Position
    /// <c>0</c> is a legal cursor value and never a legal element index, which is the property the
    /// one-based contract is built on.
    /// </remarks>
    [Fact]
    public void AFreshVectorStartsRewoundBeforeTheFirstElement()
    {
        Vector vector = FourDistinctElements();

        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());
        Assert.Equal(4UL, vector.Count());
    }

    /// <summary>
    /// <see cref="Vector.Rewind"/> returns the cursor to position <c>0</c> from wherever it was,
    /// leaves the elements untouched, and makes the next forward read land on element <c>1</c>.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L30 - subroutine rewind()</c>. The "first read lands on
    /// the FIRST element" half is the point: it is only true because <c>Rewind</c> goes to <c>0</c>
    /// and <see cref="Vector.GetNext"/> advances before reading. Rewinding is also asserted not to
    /// empty the container, which separates it from <c>Purge</c>.
    /// </remarks>
    [Fact]
    public void RewindReturnsTheCursorBeforeTheFirstElementSoTheNextForwardReadIsElementOne()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);
        Assert.Equal(InteriorIndex, vector.Position());

        vector.Rewind();

        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());
        Assert.True(vector.HasNext());
        Assert.Equal(4UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));

        Assert.Equal(Alpha, ElementText(vector.GetNext()));
        Assert.Equal(1UL, vector.Position());
    }

    /// <summary>
    /// A full forward walk with <see cref="Vector.GetNext"/> from the rewound state visits every
    /// element exactly once, in index order, and the cursor tracks the walk step by step.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L27 - any getnext()</c>. This member ADVANCES FIRST and
    /// then reads, so from position <c>0</c> the opening call yields element <c>1</c> and leaves
    /// the cursor at <c>1</c>. The position is asserted after every single step rather than only at
    /// the end, because a walk that returned the right four values while mistracking the cursor
    /// would otherwise pass.
    /// </remarks>
    [Fact]
    public void GetNextWalksForwardOverEveryElementAndTheCursorTracksEachStep()
    {
        Vector vector = FourDistinctElements();

        Assert.Equal(Alpha, ElementText(vector.GetNext()));
        Assert.Equal(1UL, vector.Position());

        Assert.Equal(Bravo, ElementText(vector.GetNext()));
        Assert.Equal(2UL, vector.Position());

        Assert.Equal(Charlie, ElementText(vector.GetNext()));
        Assert.Equal(3UL, vector.Position());

        Assert.Equal(Delta, ElementText(vector.GetNext()));
        Assert.Equal(4UL, vector.Position());
    }

    /// <summary>
    /// Walking off the end with <see cref="Vector.GetNext"/> returns <see langword="null"/> and
    /// SETTLES the cursor on the last element - it does not throw, and it does not run past the
    /// end.
    /// </summary>
    /// <remarks>
    /// The implemented end-of-walk behaviour, pinned verbatim under C-B. Three separate facts are
    /// asserted because a reimplementation could get any one of them wrong on its own: the result
    /// is a null SENTINEL rather than an exception; the cursor comes to rest exactly at
    /// <c>Count()</c>, so <see cref="Vector.Get"/> still returns the last element afterwards; and
    /// the state is STABLE, so a caller that keeps calling past exhaustion keeps getting
    /// <see langword="null"/> from the same position instead of drifting. Note the deliberate
    /// asymmetry with <see cref="Vector.GetAndNext"/>, which is the only member that can leave the
    /// cursor at <c>Count() + 1</c>.
    /// </remarks>
    [Fact]
    public void GetNextPastTheLastElementReturnsNullAndSettlesTheCursorOnTheLastElement()
    {
        Vector vector = FourDistinctElements();

        vector.Move(4UL);
        Assert.False(vector.HasNext());

        Assert.Null(vector.GetNext());
        Assert.Equal(4UL, vector.Position());
        Assert.Equal(Delta, ElementText(vector.Get()));

        Assert.Null(vector.GetNext());
        Assert.Equal(4UL, vector.Position());
    }

    /// <summary>
    /// A full backward walk with <see cref="Vector.GetPrevious"/> from the last element visits
    /// every earlier element exactly once, in reverse index order, and the cursor tracks each step.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L28 - any getprevious()</c>, the exact mirror of
    /// <see cref="Vector.GetNext"/>: it RETREATS FIRST and then reads, so starting from the tail at
    /// position <c>4</c> the opening call yields element <c>3</c> rather than element <c>4</c>
    /// again. Reverse iteration is called out in this refactor as the most dangerous shape to
    /// translate from a one-based world, so the position is asserted at every step here too.
    /// </remarks>
    [Fact]
    public void GetPreviousWalksBackwardOverEveryElementAndTheCursorTracksEachStep()
    {
        Vector vector = FourDistinctElements();

        vector.Move(4UL);
        Assert.Equal(4UL, vector.Position());
        Assert.Equal(Delta, ElementText(vector.Get()));

        Assert.Equal(Charlie, ElementText(vector.GetPrevious()));
        Assert.Equal(3UL, vector.Position());

        Assert.Equal(Bravo, ElementText(vector.GetPrevious()));
        Assert.Equal(2UL, vector.Position());

        Assert.Equal(Alpha, ElementText(vector.GetPrevious()));
        Assert.Equal(1UL, vector.Position());
    }

    /// <summary>
    /// Retreating past the front with <see cref="Vector.GetPrevious"/> returns
    /// <see langword="null"/> and settles the cursor REWOUND at <c>0</c>, stably.
    /// </summary>
    /// <remarks>
    /// The mirror of the end-of-walk edge, and pinned for the same reason. Position <c>0</c> is
    /// exactly the rewound state, so a backward walk that runs out leaves the vector ready for a
    /// forward walk - and repeated calls from there keep returning <see langword="null"/> from
    /// <c>0</c> rather than underflowing.
    /// </remarks>
    [Fact]
    public void GetPreviousPastTheFirstElementReturnsNullAndSettlesTheCursorRewound()
    {
        Vector vector = FourDistinctElements();

        vector.Move(1UL);

        Assert.Null(vector.GetPrevious());
        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());

        Assert.Null(vector.GetPrevious());
        Assert.Equal(0UL, vector.Position());
    }

    /// <summary>
    /// <see cref="Vector.GetAndNext"/> reads the element AT the cursor and only THEN advances,
    /// which makes it a genuinely different member from <see cref="Vector.GetNext"/> rather than an
    /// alias for it.
    /// </summary>
    /// <remarks>
    /// Legacy counterparts <c>n_vector.sru:L27</c> and <c>:L29</c>, two prototypes that differ by
    /// the ORDER of the read and the move. Merging them is the natural mistake, so this test drives
    /// two vectors identically prepared to the same interior position and asserts the results
    /// DIVERGE: from position 3, <c>GetAndNext</c> yields element 3 while <c>GetNext</c> yields
    /// element 4, and both leave the cursor at 4. The shared landing position is what makes the
    /// difference easy to miss and is exactly why the returned values are compared as well.
    /// </remarks>
    [Fact]
    public void GetAndNextReadsAtTheCursorWhereasGetNextReadsTheFollowingElement()
    {
        Vector readThenAdvance = FourDistinctElements();
        Vector advanceThenRead = FourDistinctElements();

        readThenAdvance.Move(InteriorIndex);
        advanceThenRead.Move(InteriorIndex);

        object? fromGetAndNext = readThenAdvance.GetAndNext();
        object? fromGetNext = advanceThenRead.GetNext();

        Assert.Equal(Charlie, ElementText(fromGetAndNext));
        Assert.Equal(Delta, ElementText(fromGetNext));
        Assert.NotEqual(ElementText(fromGetAndNext), ElementText(fromGetNext));

        Assert.Equal(4UL, readThenAdvance.Position());
        Assert.Equal(4UL, advanceThenRead.Position());
    }

    /// <summary>
    /// From the REWOUND state the two walking members diverge in the sharpest way available:
    /// <see cref="Vector.GetAndNext"/> returns <see langword="null"/> and does not move at all,
    /// while <see cref="Vector.GetNext"/> returns element <c>1</c>.
    /// </summary>
    /// <remarks>
    /// A second, independent proof that the two are not aliases, and the one that would catch a
    /// merge in either direction. Position <c>0</c> is not on an element, so a read-first member
    /// has nothing to read and correctly declines to move; an advance-first member has an element
    /// ahead of it and correctly returns it. An implementation that collapsed the pair would have
    /// to pick one of these two answers and would fail the other.
    /// </remarks>
    [Fact]
    public void GetAndNextDeclinesToMoveFromTheRewoundStateWhereasGetNextAdvancesOntoElementOne()
    {
        Vector readThenAdvance = FourDistinctElements();
        Vector advanceThenRead = FourDistinctElements();

        Assert.Null(readThenAdvance.GetAndNext());
        Assert.Equal(0UL, readThenAdvance.Position());

        Assert.Equal(Alpha, ElementText(advanceThenRead.GetNext()));
        Assert.Equal(1UL, advanceThenRead.Position());
    }

    /// <summary>
    /// A repeated-<see cref="Vector.GetAndNext"/> walk from element <c>1</c> visits every element
    /// exactly once and then terminates, because advancing off the tail leaves the cursor ONE PAST
    /// THE END at <c>Count() + 1</c>.
    /// </summary>
    /// <remarks>
    /// The past-the-end position is asserted explicitly, at <c>5</c> for four elements, because it
    /// is the one cursor value that has no element behind it and it is reachable ONLY through this
    /// member. It exists so that this idiom terminates: were the cursor clamped to <c>Count()</c>
    /// instead, the final element would repeat for ever. From there the member returns
    /// <see langword="null"/> WITHOUT moving again, so the state is stable, and
    /// <see cref="Vector.HasNext"/> is <see langword="false"/> - all pinned as implemented under
    /// C-B rather than tidied into a clamp.
    /// </remarks>
    [Fact]
    public void GetAndNextRunsOnePastTheEndSoARepeatedWalkVisitsEachElementExactlyOnce()
    {
        Vector vector = FourDistinctElements();

        vector.Move(1UL);

        Assert.Equal(Alpha, ElementText(vector.GetAndNext()));
        Assert.Equal(2UL, vector.Position());

        Assert.Equal(Bravo, ElementText(vector.GetAndNext()));
        Assert.Equal(3UL, vector.Position());

        Assert.Equal(Charlie, ElementText(vector.GetAndNext()));
        Assert.Equal(4UL, vector.Position());

        Assert.Equal(Delta, ElementText(vector.GetAndNext()));
        Assert.Equal(5UL, vector.Position());
        Assert.Equal(vector.Count() + 1UL, vector.Position());

        Assert.Null(vector.GetAndNext());
        Assert.Equal(5UL, vector.Position());
        Assert.Null(vector.Get());
        Assert.False(vector.HasNext());
    }

    /// <summary>
    /// From the past-the-end position, <see cref="Vector.GetNext"/> returns <see langword="null"/>
    /// and pulls the cursor BACK onto the last element, while <see cref="Vector.GetPrevious"/>
    /// returns the last element directly.
    /// </summary>
    /// <remarks>
    /// The recovery paths out of the one cursor state that has no element under it, pinned as
    /// implemented. They are asymmetric on purpose and neither is normalised: the forward member
    /// reports exhaustion with <see langword="null"/> yet still repositions to <c>Count()</c>,
    /// whereas the backward member treats past-the-end as "one beyond the tail" and hands back the
    /// tail itself. Both leave the cursor at <c>Count()</c>, which is what keeps the two walking
    /// directions consistent with one another afterwards.
    /// </remarks>
    [Fact]
    public void FromPastTheEndGetNextPullsBackOntoTheLastElementAndGetPreviousReturnsIt()
    {
        Vector pulledBackByGetNext = FourDistinctElements();
        Vector steppedBackByGetPrevious = FourDistinctElements();

        pulledBackByGetNext.Move(4UL);
        Assert.Equal(Delta, ElementText(pulledBackByGetNext.GetAndNext()));
        Assert.Equal(5UL, pulledBackByGetNext.Position());

        steppedBackByGetPrevious.Move(4UL);
        Assert.Equal(Delta, ElementText(steppedBackByGetPrevious.GetAndNext()));
        Assert.Equal(5UL, steppedBackByGetPrevious.Position());

        Assert.Null(pulledBackByGetNext.GetNext());
        Assert.Equal(4UL, pulledBackByGetNext.Position());
        Assert.Equal(Delta, ElementText(pulledBackByGetNext.Get()));

        Assert.Equal(Delta, ElementText(steppedBackByGetPrevious.GetPrevious()));
        Assert.Equal(4UL, steppedBackByGetPrevious.Position());
    }


    /// <summary>
    /// <see cref="Vector.HasNext"/> is <see langword="true"/> at every point of a forward walk that
    /// still has an element ahead of it, and <see langword="false"/> from the moment the cursor
    /// reaches the last element onwards.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L36 - boolean hasnext()</c>. Its contract is stated in
    /// terms of <see cref="Vector.GetNext"/>: it is <see langword="true"/> exactly when that member
    /// would return an element. The transition therefore happens ON arrival at the tail and not
    /// after overrunning it, which is what makes the guarded loop stop having consumed every element
    /// and no more - so the boundary is asserted on both sides of the tail rather than only mid-walk.
    /// </remarks>
    [Fact]
    public void HasNextIsTrueWhileAnElementRemainsAheadAndFalseFromTheTailOnwards()
    {
        Vector vector = FourDistinctElements();

        Assert.True(vector.HasNext());

        vector.Move(1UL);
        Assert.True(vector.HasNext());

        vector.Move(InteriorIndex);
        Assert.True(vector.HasNext());

        vector.Move(4UL);
        Assert.False(vector.HasNext());

        Assert.Equal(Delta, ElementText(vector.GetAndNext()));
        Assert.Equal(5UL, vector.Position());
        Assert.False(vector.HasNext());
    }

    /// <summary>
    /// A guarded forward walk - rewind, then loop while <see cref="Vector.HasNext"/> calling
    /// <see cref="Vector.GetNext"/> - consumes exactly the four elements and stops.
    /// </summary>
    /// <remarks>
    /// The documented traversal idiom driven as a caller would drive it, which is the only way to
    /// verify that the guard and the walker agree with one another. Asserting the collected element
    /// count as well as the values is what would catch an off-by-one in either member: a guard that
    /// was one step too generous would append a null, and one step too mean would drop the tail.
    /// </remarks>
    [Fact]
    public void TheGuardedForwardWalkIdiomConsumesEveryElementExactlyOnceAndThenStops()
    {
        Vector vector = FourDistinctElements();
        List<string> visited = [];

        vector.Rewind();

        while (vector.HasNext())
        {
            visited.Add(ElementText(vector.GetNext()));
        }

        Assert.Equal(4, visited.Count);
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", string.Join(' ', visited));
        Assert.Equal(4UL, vector.Position());
        Assert.False(vector.HasNext());
    }

    /// <summary>
    /// On an EMPTY vector the cursor has nowhere to be: <see cref="Vector.Position"/> is <c>0</c>,
    /// <see cref="Vector.HasNext"/> is <see langword="false"/>, and every cursor move is a no-op
    /// that leaves it at <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The empty case is covered deliberately because it is the state a recursion stack spends most
    /// of its life in - fully unwound - and because it is where an implementation is most tempted to
    /// throw. Nothing here throws: the reads return <see langword="null"/> and the moves do nothing,
    /// which is the same no-failure-channel posture the whole cursor surface takes.
    /// </remarks>
    [Fact]
    public void OnAnEmptyVectorTheCursorStaysAtZeroAndEveryCursorMemberIsHarmless()
    {
        Vector vector = new();

        Assert.Equal(0UL, vector.Count());
        Assert.Equal(0UL, vector.Position());
        Assert.False(vector.HasNext());
        Assert.Null(vector.Get());

        Assert.Null(vector.GetNext());
        Assert.Equal(0UL, vector.Position());

        Assert.Null(vector.GetPrevious());
        Assert.Equal(0UL, vector.Position());

        Assert.Null(vector.GetAndNext());
        Assert.Equal(0UL, vector.Position());

        vector.Move(1UL);
        Assert.Equal(0UL, vector.Position());

        vector.Rewind();
        Assert.Equal(0UL, vector.Position());

        vector.Set(Replaced);
        vector.Remove();
        Assert.Equal(0UL, vector.Count());
        Assert.Equal(0UL, vector.Position());
    }

    /// <summary>
    /// The no-argument <see cref="Vector.Position"/> reports the CURSOR, and is not to be confused
    /// with the search overload <c>Position(object?)</c>, which reports where a VALUE is and moves
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Two prototypes share the name and differ only by arity - <c>n_vector.sru:L37 ulong
    /// position()</c> and <c>:L38 ulong position(any value)</c> - so a reimplementation could route
    /// one to the other and still return plausible numbers. This test drives both on the same vector
    /// with the cursor deliberately parked somewhere the search answer differs, then asserts the
    /// cursor is UNMOVED afterwards, which is the cursor-side consequence this file owns. The search
    /// overload's own contract, including its not-found sentinel, belongs to the sibling class.
    /// </remarks>
    [Fact]
    public void TheNoArgumentPositionReportsTheCursorAndTheSearchOverloadDoesNotMoveIt()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);

        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(1UL, vector.Position(Alpha));
        Assert.Equal(4UL, vector.Position(Delta));

        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(Charlie, ElementText(vector.Get()));
    }

    /// <summary>
    /// <see cref="Vector.Move(ulong)"/> repositions the cursor so that the no-argument
    /// <see cref="Vector.Get"/> returns the element at that ONE-BASED index - for every valid index,
    /// including the last, which is <c>Count()</c> and never <c>Count() - 1</c>.
    /// </summary>
    /// <param name="index">The one-based index to move to.</param>
    /// <param name="expected">The element the fixture holds at that index.</param>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L19 - subroutine move(ulong index)</c>. Table-driven
    /// across the whole valid range so the one-based contract is pinned at both ends in one place:
    /// index <c>1</c> is the head, and index <c>4</c> - equal to <c>Count()</c> - is the tail. That
    /// upper bound is the same inclusive bound the legacy consumer relies on when it pops with
    /// <c>RemoveAt(Count())</c> [n_cst_dwsvc_columnexp.sru:L297, L318], so a zero-based cursor would
    /// fail the last row here for exactly the reason it would break the recursion stack.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryValidCursorIndex))]
    public void MoveRepositionsTheCursorOntoTheElementAtThatOneBasedIndex(ulong index, string expected)
    {
        Vector vector = FourDistinctElements();

        vector.Move(index);

        Assert.Equal(index, vector.Position());
        Assert.Equal(expected, ElementText(vector.Get()));
    }

    /// <summary>
    /// The complete set of valid cursor indices for the four-element fixture, paired with the
    /// element each one addresses.
    /// </summary>
    /// <remarks>
    /// Deliberately exhaustive rather than a sample: the head and the tail are the two rows a
    /// one-based to zero-based slip would break, and the two interior rows are what prove the
    /// mapping is not merely correct at the ends by accident.
    /// </remarks>
    public static TheoryData<ulong, string> EveryValidCursorIndex => new()
    {
        { 1UL, Alpha },
        { 2UL, Bravo },
        { 3UL, Charlie },
        { 4UL, Delta },
    };

    /// <summary>
    /// <c>Move(0)</c> is the rewound state - equivalent to <see cref="Vector.Rewind"/> - because
    /// <c>0</c> is never a valid element index.
    /// </summary>
    /// <remarks>
    /// The one input that is neither a valid element index nor out of range. It is accepted and
    /// means "before the first element", which is what keeps <c>0</c> usable as the cursor's own
    /// resting value and as the not-found sentinel of the search overload.
    /// </remarks>
    [Fact]
    public void MoveToZeroIsTheRewoundStateBecauseZeroIsNeverAValidElementIndex()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);
        Assert.Equal(Charlie, ElementText(vector.Get()));

        vector.Move(0UL);

        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());
        Assert.True(vector.HasNext());
        Assert.Equal(4UL, vector.Count());
    }

    /// <summary>
    /// <c>Move</c> to an index beyond the last element is a NO-OP that leaves the previous cursor
    /// position exactly where it was - it does not clamp to the tail and it does not throw.
    /// </summary>
    /// <remarks>
    /// Pinned as implemented under C-B, and the reason matters: clamping would move the cursor to a
    /// position the caller never asked for, after which the no-argument <see cref="Vector.Get"/>
    /// would return a PLAUSIBLE WRONG element instead of leaving the previous one intact. Asserted
    /// from a known interior position so that "unchanged" is distinguishable from "reset", which a
    /// test starting from the rewound state could not tell apart.
    /// </remarks>
    [Fact]
    public void MoveBeyondTheLastElementIsANoOpAndLeavesThePreviousPositionIntact()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);

        vector.Move(5UL);
        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(Charlie, ElementText(vector.Get()));

        vector.Move(99UL);
        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(Charlie, ElementText(vector.Get()));

        Assert.Equal(4UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));
    }


    // ==========================================================================================
    //  PART 2 - THE NO-ARGUMENT TRIO OPERATES AT THE CURSOR
    //  ----------------------------------------------------------------------------------------
    //  Get(), Set(value) and Remove() each sit directly beside a positional counterpart in the
    //  legacy declaration - L21/L22, L23/L24, L25/L26 - and the whole reason both forms exist is
    //  that one targets the cursor and the other targets an explicit index. Every test in this
    //  part therefore parks the cursor STRICTLY INTERIOR, at element 3 of 4, so that a head
    //  confusion, a tail confusion and a whole-container confusion each produce a different wrong
    //  answer and none of them can pass by coincidence.
    // ==========================================================================================

    /// <summary>
    /// The no-argument <see cref="Vector.Get"/> returns the element UNDER THE CURSOR - not the
    /// head, not the tail, and not the first element.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L21 - any get()</c>. The head and tail are asserted to be
    /// DIFFERENT from the answer rather than merely leaving them unmentioned, because "Get() means
    /// the first element" is the specific misreading that would let a cursor-free reimplementation
    /// pass. Repeating the call also pins that reading does not advance anything: <c>Get</c> is a
    /// pure read, and the walking members are the ones that move the cursor.
    /// </remarks>
    [Fact]
    public void GetReturnsTheElementUnderTheCursorAndNeitherTheHeadNorTheTail()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);

        Assert.Equal(Charlie, ElementText(vector.Get()));
        Assert.NotEqual(Alpha, ElementText(vector.Get()));
        Assert.NotEqual(Delta, ElementText(vector.Get()));

        Assert.Equal(Charlie, ElementText(vector.Get()));
        Assert.Equal(InteriorIndex, vector.Position());
    }

    /// <summary>
    /// The no-argument <see cref="Vector.Set(object?)"/> overwrites ONLY the element under the
    /// cursor, leaves every other element untouched, and leaves both the element count and the
    /// cursor unchanged.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L23 - subroutine set(any value)</c>. Verified by a
    /// one-based <c>GetAt</c> walk over the whole container, because "replaced the right element" and
    /// "replaced every element" and "appended instead of replacing" are three distinct failures and
    /// only a whole-container read distinguishes them. The count assertion is what rules out the
    /// append reading in particular.
    /// </remarks>
    [Fact]
    public void SetOverwritesOnlyTheElementUnderTheCursorAndChangesNothingElse()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);
        vector.Set(Replaced);

        Assert.Equal($"{Alpha} {Bravo} {Replaced} {Delta}", Snapshot(vector));
        Assert.Equal(4UL, vector.Count());
        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(Replaced, ElementText(vector.Get()));
    }

    /// <summary>
    /// The no-argument <see cref="Vector.Remove"/> takes out the element under the cursor and KEEPS
    /// the cursor's number, so the cursor then addresses what was the FOLLOWING element.
    /// </summary>
    /// <remarks>
    /// Legacy counterpart <c>n_vector.sru:L25 - subroutine remove()</c>. The kept-number behaviour is
    /// pinned as implemented and is deliberate rather than incidental: it is what makes the member
    /// usable inside a forward scan, where removing the current element leaves the cursor already
    /// sitting on the next one with no compensating move. A reimplementation that helpfully stepped
    /// the cursor back would silently skip an element on every removal, and a row count would not
    /// reveal it.
    /// </remarks>
    [Fact]
    public void RemoveTakesOutTheElementUnderTheCursorAndTheCursorThenAddressesTheFollowingOne()
    {
        Vector vector = FourDistinctElements();

        vector.Move(InteriorIndex);
        Assert.Equal(Charlie, ElementText(vector.Get()));

        vector.Remove();

        Assert.Equal(3UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Delta}", Snapshot(vector));
        Assert.Equal(InteriorIndex, vector.Position());
        Assert.Equal(Delta, ElementText(vector.Get()));
    }

    /// <summary>
    /// Removing the element under the cursor when it is the LAST element clamps the cursor down onto
    /// the new last element - silently, with no exception and no reset to the rewound state.
    /// </summary>
    /// <remarks>
    /// The one case where keeping the cursor's number is not enough, since the kept number would
    /// otherwise point one past the new end. The clamp is pinned exactly as implemented under C-B:
    /// the position becomes <c>Count()</c> of the SHORTENED container, and the element under the
    /// cursor is the new tail rather than <see langword="null"/>.
    /// </remarks>
    [Fact]
    public void RemovingTheLastElementUnderTheCursorClampsTheCursorOntoTheNewLastElement()
    {
        Vector vector = FourDistinctElements();

        vector.Move(4UL);

        vector.Remove();

        Assert.Equal(3UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie}", Snapshot(vector));
        Assert.Equal(3UL, vector.Position());
        Assert.Equal(vector.Count(), vector.Position());
        Assert.Equal(Charlie, ElementText(vector.Get()));
    }

    /// <summary>
    /// Repeated <see cref="Vector.Remove"/> from one interior position drains the container from the
    /// cursor forward and then, once the tail is reached, keeps clamping until the vector is empty
    /// and the cursor is back at <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The kept-number rule and the clamp rule driven together to their conclusion, which is the only
    /// way to show they compose. Removing at position 2 four times walks the container down
    /// <c>4 -&gt; 3 -&gt; 2 -&gt; 1 -&gt; 0</c> without a single explicit cursor move, and the
    /// position follows <c>2, 2, 1, 0</c> as the clamp starts biting. The empty end state is asserted
    /// too, because that is where a further call has to be harmless.
    /// </remarks>
    [Fact]
    public void RepeatedRemoveDrainsTheContainerFromTheCursorForwardAndThenClampsToEmpty()
    {
        Vector vector = FourDistinctElements();

        vector.Move(2UL);

        vector.Remove();
        Assert.Equal(3UL, vector.Count());
        Assert.Equal(2UL, vector.Position());
        Assert.Equal(Charlie, ElementText(vector.Get()));

        vector.Remove();
        Assert.Equal(2UL, vector.Count());
        Assert.Equal(2UL, vector.Position());
        Assert.Equal(Delta, ElementText(vector.Get()));

        vector.Remove();
        Assert.Equal(1UL, vector.Count());
        Assert.Equal(1UL, vector.Position());
        Assert.Equal(Alpha, ElementText(vector.Get()));

        vector.Remove();
        Assert.Equal(0UL, vector.Count());
        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());

        vector.Remove();
        Assert.Equal(0UL, vector.Count());
        Assert.Equal(0UL, vector.Position());
    }

    /// <summary>
    /// While the cursor is REWOUND the no-argument <see cref="Vector.Set(object?)"/> and
    /// <see cref="Vector.Remove"/> are no-ops: nothing is written, nothing is removed, and the
    /// cursor does not move.
    /// </summary>
    /// <remarks>
    /// Pinned as implemented under C-B. Position <c>0</c> is a legal cursor value that is not on an
    /// element, so both subroutines have nothing to act on; the prototypes have no failure channel to
    /// report that through, so doing nothing is the whole behaviour. The two alternatives a
    /// reimplementation might reach for are both ruled out here by assertion: writing to the head -
    /// which the snapshot would catch - and appending, which the count would catch.
    /// </remarks>
    [Fact]
    public void SetAndRemoveAreNoOpsWhileTheCursorIsRewound()
    {
        Vector vector = FourDistinctElements();

        vector.Rewind();

        vector.Set(Replaced);
        Assert.Equal(4UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));
        Assert.Equal(0UL, vector.Position());

        vector.Remove();
        Assert.Equal(4UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));
        Assert.Equal(0UL, vector.Position());
    }

    /// <summary>
    /// While the cursor sits PAST THE END the no-argument trio is equally inert: the read is
    /// <see langword="null"/> and the write and the removal do nothing.
    /// </summary>
    /// <remarks>
    /// The second of the two cursor values that are legal but not on an element, reachable only
    /// through <see cref="Vector.GetAndNext"/>. Covered separately from the rewound case because the
    /// two sit at opposite ends of the cursor's range and an implementation could easily guard one
    /// and not the other - a range check written as "greater than zero" would let this one through
    /// and index past the end.
    /// </remarks>
    [Fact]
    public void TheNoArgumentTrioIsInertWhileTheCursorSitsPastTheEnd()
    {
        Vector vector = FourDistinctElements();

        vector.Move(4UL);
        Assert.Equal(Delta, ElementText(vector.GetAndNext()));
        Assert.Equal(5UL, vector.Position());

        Assert.Null(vector.Get());

        vector.Set(Replaced);
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));

        vector.Remove();
        Assert.Equal(4UL, vector.Count());
        Assert.Equal($"{Alpha} {Bravo} {Charlie} {Delta}", Snapshot(vector));
        Assert.Equal(5UL, vector.Position());
    }

    /// <summary>
    /// Each member of the no-argument trio is cross-checked against its positional counterpart on
    /// equivalently prepared vectors, proving the two forms are genuinely different operations and
    /// not aliases.
    /// </summary>
    /// <remarks>
    /// The structural claim from the file header, turned into an assertion. With the cursor parked at
    /// index 3, the no-argument form and the positional form disagree about WHICH element they touch
    /// whenever the index passed is not the cursor's own - so <c>Get()</c> differs from
    /// <c>GetAt(1)</c>, <c>Set</c> writes where <c>SetAt(1, ...)</c> does not, and <c>Remove</c>
    /// removes a different element from <c>RemoveAt(1)</c>. Each pair is driven on its own instance
    /// so that one mutation cannot mask the other, and the agreement case - the positional call aimed
    /// AT the cursor - is asserted too, because a correct implementation must agree there and only
    /// there.
    /// </remarks>
    [Fact]
    public void TheNoArgumentTrioIsNotAnAliasForItsPositionalCounterpart()
    {
        Vector reader = FourDistinctElements();
        reader.Move(InteriorIndex);
        Assert.Equal(Charlie, ElementText(reader.Get()));
        Assert.Equal(Alpha, ElementText(reader.GetAt(1UL)));
        Assert.NotEqual(ElementText(reader.GetAt(1UL)), ElementText(reader.Get()));
        Assert.Equal(ElementText(reader.GetAt(InteriorIndex)), ElementText(reader.Get()));

        Vector cursorWriter = FourDistinctElements();
        cursorWriter.Move(InteriorIndex);
        cursorWriter.Set(Replaced);

        Vector positionalWriter = FourDistinctElements();
        positionalWriter.Move(InteriorIndex);
        positionalWriter.SetAt(1UL, Replaced);

        Assert.Equal($"{Alpha} {Bravo} {Replaced} {Delta}", Snapshot(cursorWriter));
        Assert.Equal($"{Replaced} {Bravo} {Charlie} {Delta}", Snapshot(positionalWriter));
        Assert.NotEqual(Snapshot(positionalWriter), Snapshot(cursorWriter));

        Vector cursorRemover = FourDistinctElements();
        cursorRemover.Move(InteriorIndex);
        cursorRemover.Remove();

        Vector positionalRemover = FourDistinctElements();
        positionalRemover.Move(InteriorIndex);
        positionalRemover.RemoveAt(1UL);

        Assert.Equal($"{Alpha} {Bravo} {Delta}", Snapshot(cursorRemover));
        Assert.Equal($"{Bravo} {Charlie} {Delta}", Snapshot(positionalRemover));
        Assert.NotEqual(Snapshot(positionalRemover), Snapshot(cursorRemover));
    }


    // ==========================================================================================
    //  PART 3 - WHAT THE CURSOR DOES WHEN THE CONTAINER IS MUTATED AT, BEFORE AND AFTER IT
    //  ----------------------------------------------------------------------------------------
    //  This is the part a cursor-free reimplementation cannot fake at all, because a plain
    //  System.Collections.Generic.List<T> has no position to maintain across an insertion or a
    //  removal. Vector's implemented rule is uniform and is asserted here rather than described:
    //
    //      the cursor KEEPS ITS NUMBER, and is CLAMPED DOWN to the new count only if that number
    //      would otherwise point beyond the end.
    //
    //  Two consequences follow, and they are the two halves of this matrix. A mutation BEFORE the
    //  cursor shifts every later element, so the kept number now addresses a DIFFERENT element -
    //  element identity is not preserved, and that is the implemented behaviour, not a defect to
    //  be corrected into an auto-adjusting cursor. A mutation AFTER the cursor shifts nothing at
    //  or below the cursor, so element identity IS preserved.
    //
    //  Written as one table-driven theory so the whole matrix is legible in a single place, and
    //  every expectation is the OBSERVED behaviour rather than the convenient one - C-B territory.
    // ==========================================================================================

    /// <summary>
    /// The mutation applied by <see cref="TheCursorSurvivesMutationExactlyAsImplemented"/>, named by
    /// its relationship to the cursor rather than by the member it calls.
    /// </summary>
    /// <remarks>
    /// The relationship is what determines the outcome, so it is what the case is named after. Each
    /// member is mapped to a concrete call, expressed relative to the cursor's own position, in
    /// <see cref="ApplyMutation(Vector, CursorMutation)"/>.
    /// </remarks>
    public enum CursorMutation
    {
        /// <summary>Overwrite the element under the cursor positionally, with <c>SetAt</c>.</summary>
        SetAtTheCursor,

        /// <summary>Remove the element under the cursor positionally, with <c>RemoveAt</c>.</summary>
        RemoveAtTheCursor,

        /// <summary>Insert an element immediately before the one under the cursor.</summary>
        InsertBeforeTheCursor,

        /// <summary>Remove the head, which lies strictly before the cursor.</summary>
        RemoveBeforeTheCursor,

        /// <summary>Insert a new head, which lies strictly before the cursor.</summary>
        PrependBeforeTheCursor,

        /// <summary>Insert an element strictly after the one under the cursor.</summary>
        InsertAfterTheCursor,

        /// <summary>Remove the tail, which lies strictly after the cursor.</summary>
        RemoveAfterTheCursor,
    }

    /// <summary>
    /// Applies one mutation to a four-element fixture whose cursor has already been positioned,
    /// expressing every index RELATIVE TO THE CURSOR so that the case name and the call cannot drift
    /// apart.
    /// </summary>
    /// <param name="vector">The vector to mutate. Its cursor is read but never moved by this method.</param>
    /// <param name="mutation">Which mutation to apply.</param>
    /// <remarks>
    /// Deriving each index from <see cref="Vector.Position"/> is deliberate: it is what makes
    /// "before", "at" and "after" mechanically true of the call rather than merely asserted in a
    /// comment, and it means a change to a row's cursor column cannot leave the mutation pointing
    /// somewhere the case name no longer describes. Every call here is positional, so none of them
    /// moves the cursor itself - the position changes observed by the theory are entirely the
    /// container's doing.
    /// <para>
    /// THE TWO INSERTIONS PASS THE CURSOR'S OWN INDEX, NOT AN OFFSET FROM IT, AND THAT IS A
    /// CORRECTION WORTH RECORDING. An earlier form of this method called
    /// <c>InsertBefore(cursor - 1)</c> and <c>InsertAfter(cursor + 1)</c>, on the reasoning that the
    /// index should name the neighbouring element. It should not: <c>Vector.InsertBefore(index)</c>
    /// resolves the index to a slot and inserts AT it, and <c>InsertAfter(index)</c> inserts at that
    /// slot PLUS ONE - the before/after semantics are the container's, applied to the index given.
    /// Adding an offset applied them twice, so each insertion landed two positions from the cursor
    /// instead of adjacent to it. The matrix expectations encoded that same displacement, so the
    /// theory passed while testing a relationship neither case name described - the exact failure
    /// mode a table-driven test is supposed to make impossible.
    /// </para>
    /// </remarks>
    private static void ApplyMutation(Vector vector, CursorMutation mutation)
    {
        ulong cursor = vector.Position();

        switch (mutation)
        {
            case CursorMutation.SetAtTheCursor:
                vector.SetAt(cursor, Replaced);
                break;

            case CursorMutation.RemoveAtTheCursor:
                vector.RemoveAt(cursor);
                break;

            case CursorMutation.InsertBeforeTheCursor:
                vector.InsertBefore(cursor, Inserted);
                break;

            case CursorMutation.RemoveBeforeTheCursor:
                vector.RemoveAt(1UL);
                break;

            case CursorMutation.PrependBeforeTheCursor:
                vector.Prepend(Prepended);
                break;

            case CursorMutation.InsertAfterTheCursor:
                vector.InsertAfter(cursor, Inserted);
                break;

            case CursorMutation.RemoveAfterTheCursor:
                vector.RemoveAt(vector.Count());
                break;

            default:
                // Unreachable while the enum and the table agree, and a hard failure rather than a
                // silent skip if they ever stop agreeing: a mutation that quietly did nothing would
                // turn every assertion below into a check on the UNMUTATED fixture and the theory
                // would go green while testing nothing at all.
                Assert.Fail($"Unhandled {nameof(CursorMutation)} value: {mutation}.");
                break;
        }
    }

    /// <summary>
    /// The cursor survives every mutation at, before and after it exactly as
    /// <c>Vector.cs</c> implements it: the number is kept, and it is clamped down only when it would
    /// otherwise point beyond the shortened container.
    /// </summary>
    /// <param name="mutation">Which mutation to apply, relative to the cursor.</param>
    /// <param name="cursorBefore">The one-based cursor position to park on before mutating.</param>
    /// <param name="elementUnderCursorBefore">
    /// The fixture element at <paramref name="cursorBefore"/>, asserted before the mutation so each
    /// row states its own starting point instead of leaving it to be inferred.
    /// </param>
    /// <param name="expectedCountAfter">The element count the mutation should leave behind.</param>
    /// <param name="expectedPositionAfter">The cursor position the mutation should leave behind.</param>
    /// <param name="expectedElementUnderCursorAfter">
    /// The element the no-argument <see cref="Vector.Get"/> should return afterwards, or
    /// <see langword="null"/> if the cursor should end up on no element at all.
    /// </param>
    /// <param name="expectedContentsAfter">
    /// The whole container afterwards, rendered bottom-first, so that the matrix pins WHERE the
    /// mutation landed as well as what the cursor made of it.
    /// </param>
    /// <remarks>
    /// Reading <see cref="Vector.Get"/> after the mutation is the assertion that matters most,
    /// because a position number alone cannot show whether the cursor still addresses the same
    /// ELEMENT. Comparing that value against
    /// <paramref name="elementUnderCursorBefore"/> is what makes each row's identity claim visible:
    /// the two agree for the after-the-cursor rows and disagree for the before-the-cursor rows.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CursorAfterMutationCases))]
    public void TheCursorSurvivesMutationExactlyAsImplemented(
        CursorMutation mutation,
        ulong cursorBefore,
        string elementUnderCursorBefore,
        ulong expectedCountAfter,
        ulong expectedPositionAfter,
        string? expectedElementUnderCursorAfter,
        string expectedContentsAfter)
    {
        Vector vector = FourDistinctElements();

        vector.Move(cursorBefore);
        Assert.Equal(cursorBefore, vector.Position());
        Assert.Equal(elementUnderCursorBefore, ElementText(vector.Get()));

        ApplyMutation(vector, mutation);

        Assert.Equal(expectedCountAfter, vector.Count());
        Assert.Equal(expectedPositionAfter, vector.Position());
        Assert.Equal(expectedContentsAfter, Snapshot(vector));

        object? elementUnderCursorAfter = vector.Get();

        if (expectedElementUnderCursorAfter is null)
        {
            Assert.Null(elementUnderCursorAfter);
        }
        else
        {
            Assert.Equal(expectedElementUnderCursorAfter, ElementText(elementUnderCursorAfter));
        }
    }

    /// <summary>
    /// The mutation matrix: one row per relationship between the mutation and the cursor, over the
    /// four-element fixture <c>alpha bravo charlie delta</c>.
    /// </summary>
    /// <remarks>
    /// Column order is (mutation, cursor before, element under the cursor before, count after,
    /// position after, element under the cursor after, contents after). Reading the matrix:
    /// <list type="bullet">
    /// <item>
    /// AT the cursor - the overwrite keeps the count and the position and changes the element in
    /// place; the removal keeps the position, which then addresses <c>delta</c> because
    /// <c>charlie</c> is gone.
    /// </item>
    /// <item>
    /// BEFORE the cursor - all three rows keep the position NUMERICALLY and therefore change which
    /// element it addresses. This is the implemented behaviour, pinned rather than corrected: the
    /// cursor does not chase its element. The insertion row is the sharpest case of it, because the
    /// new element lands ON the cursor's own number: <c>InsertBefore(3)</c> with the cursor at
    /// <c>3</c> leaves the cursor addressing <c>inserted</c> rather than the <c>charlie</c> it was
    /// parked on.
    /// </item>
    /// <item>
    /// AFTER the cursor - both rows keep the position AND the element, since nothing at or below the
    /// cursor moved. Element identity is preserved here and only here.
    /// </item>
    /// <item>
    /// THE TWO INSERTION ROWS PRODUCE THE SAME CONTENTS FROM OPPOSITE SIDES OF ONE GAP, which is a
    /// fact about the container rather than a coincidence in the table: <c>InsertBefore(3)</c> and
    /// <c>InsertAfter(2)</c> resolve to the same slot, so both yield
    /// <c>alpha bravo inserted charlie delta</c>. What separates them is entirely the cursor - one
    /// inserts at the cursor's number and displaces what it addressed, the other inserts above the
    /// cursor's number and leaves it addressing the same element. Two rows whose CONTENTS agree and
    /// whose IDENTITY outcome differs are exactly what this matrix exists to hold apart.
    /// </item>
    /// <item>
    /// INVALIDATION - the last row removes the element the cursor is ON while it is also the tail, so
    /// the kept number would point past the new end. It is CLAMPED down to the new count, silently:
    /// no exception, and no reset to the rewound state. Note this row shares its mutation with the
    /// second row and differs only in where the cursor was parked, which is precisely the point -
    /// invalidation is a property of the position, not of the call.
    /// </item>
    /// </list>
    /// </remarks>
    public static TheoryData<CursorMutation, ulong, string, ulong, ulong, string?, string>
        CursorAfterMutationCases => new()
    {
        {
            CursorMutation.SetAtTheCursor, 3UL, Charlie,
            4UL, 3UL, Replaced, $"{Alpha} {Bravo} {Replaced} {Delta}"
        },
        {
            CursorMutation.RemoveAtTheCursor, 3UL, Charlie,
            3UL, 3UL, Delta, $"{Alpha} {Bravo} {Delta}"
        },
        {
            CursorMutation.InsertBeforeTheCursor, 3UL, Charlie,
            5UL, 3UL, Inserted, $"{Alpha} {Bravo} {Inserted} {Charlie} {Delta}"
        },
        {
            CursorMutation.RemoveBeforeTheCursor, 3UL, Charlie,
            3UL, 3UL, Delta, $"{Bravo} {Charlie} {Delta}"
        },
        {
            CursorMutation.PrependBeforeTheCursor, 2UL, Bravo,
            5UL, 2UL, Alpha, $"{Prepended} {Alpha} {Bravo} {Charlie} {Delta}"
        },
        {
            CursorMutation.InsertAfterTheCursor, 2UL, Bravo,
            5UL, 2UL, Bravo, $"{Alpha} {Bravo} {Inserted} {Charlie} {Delta}"
        },
        {
            CursorMutation.RemoveAfterTheCursor, 2UL, Bravo,
            3UL, 2UL, Bravo, $"{Alpha} {Bravo} {Charlie}"
        },
        {
            CursorMutation.RemoveAtTheCursor, 4UL, Delta,
            3UL, 3UL, Charlie, $"{Alpha} {Bravo} {Charlie}"
        },
    };

    /// <summary>
    /// Removing the SOLE element while the cursor is on it empties the container and leaves the
    /// cursor rewound at <c>0</c> with nothing under it.
    /// </summary>
    /// <remarks>
    /// The limiting case of cursor invalidation, and the only one where the clamp produces a position
    /// that is not on an element - because there is no element left for it to be on. It needs its own
    /// test rather than a matrix row because it needs a one-element fixture. Asserted as implemented:
    /// the position becomes <c>0</c> by the same clamp that produced <c>Count()</c> in every other
    /// case, so the two rules are one rule.
    /// </remarks>
    [Fact]
    public void RemovingTheSoleElementUnderTheCursorLeavesTheContainerEmptyAndTheCursorRewound()
    {
        Vector vector = new();
        vector.Append(Alpha);

        vector.Move(1UL);
        Assert.Equal(1UL, vector.Position());
        Assert.Equal(Alpha, ElementText(vector.Get()));

        vector.Remove();

        Assert.Equal(0UL, vector.Count());
        Assert.Equal(0UL, vector.Position());
        Assert.Null(vector.Get());
        Assert.False(vector.HasNext());
    }


    // ==========================================================================================
    //  PART 4 - THE STACK USAGE PATTERN, DRIVEN EXACTLY AS THE SOLE IN-SCOPE CONSUMER DRIVES IT
    //  ----------------------------------------------------------------------------------------
    //  Vector is not merely stack-CAPABLE; being the column-expression engine's calculation and
    //  recursion stack is its reason for being in scope at all. So this part drives it the way
    //  that one consumer does, using the members that actually exist, transcribed from
    //  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:
    //
    //      L110         n_vector _vecCalcStack                 the stack itself, a private field
    //      L2421-L2422  Create n_vector : Reserve(20)          construction, pre-sized
    //      L296         _vecCalcStack.Append(colName)          PUSH
    //      L297         k = _vecCalcStack.Count()              Count() is SIMULTANEOUSLY the depth
    //                                                          and the one-based index of the frame
    //                                                          just pushed - no adjustment at all
    //      L316         _of_CalcItem(row,n)                    recursion, so pushes NEST
    //      L318         _vecCalcStack.RemoveAt(k)              POP exactly the frame this level
    //                                                          pushed
    //      L684-L688    for nIndex = 1 to Count() :            the RECURSION GUARD - an inclusive
    //                   if GetAt(nIndex) = ... then            one-based scan that never touches 0
    //                   return false : end if
    //      L753-L755    for nIndex = 1 to Count() :            the TRACE CALL STACK, built
    //                   sCallStack += GetAt(nIndex) + ">"      BOTTOM-TO-TOP, so enumeration order
    //                                                          equals append order
    //      L757         sCallStack += ...dwo.name              the current column, appended AFTER
    //                                                          the loop - which is what the
    //                                                          trailing separator joins to
    //      L758         OnColumnExpTrace(row, dwo, sCallStack, sExp, ...)
    //
    //  There is deliberately NO Push/Pop/Peek sugar to reach for: the legacy surface has none, the
    //  stack role is the CONSUMER's reading of Append/Count/RemoveAt, and reproducing that reading
    //  is the whole point of this part. Nor is any list type used to model the stack - C-D.
    //
    //  These are the assertions with reach beyond this project. The string built by the L753-L755
    //  loop is completed at L757 and passed to OnColumnExpTrace(row, dwo, sCallStack, sExp, ...) at
    //  L758, and travels
    //  on the expression-trace channel of contract C-04, so the bottom-to-top ordering asserted
    //  below is an OBSERVABLE WIRE PAYLOAD rather than an internal detail.
    // ==========================================================================================

    /// <summary>The outermost calculation frame - pushed first, so it sits at the stack bottom.</summary>
    private const string OuterFrame = "salary";

    /// <summary>The middle calculation frame, pushed by the first level of recursion.</summary>
    private const string MiddleFrame = "bonus";

    /// <summary>The innermost calculation frame - pushed last, so it sits at the stack top.</summary>
    private const string InnerFrame = "total";

    /// <summary>A column name that is never pushed, used as the recursion guard's negative case.</summary>
    private const string AbsentFrame = "headcount";

    /// <summary>The capacity the legacy consumer reserves at construction [n_cst_dwsvc_columnexp.sru:L2422].</summary>
    private const ulong ReservedDepth = 20UL;

    /// <summary>
    /// Asserts the whole stack bottom-first: the depth, and the frame at every one-based level from
    /// <c>1</c> to <see cref="Vector.Count"/> inclusive.
    /// </summary>
    /// <param name="stack">The stack to inspect. Read only; its cursor is not moved.</param>
    /// <param name="expectedFramesBottomFirst">
    /// The frames expected at levels <c>1, 2, 3, ...</c> in that order - bottom first, exactly the
    /// order the trace loop at <c>n_cst_dwsvc_columnexp.sru:L753-L755</c> walks.
    /// </param>
    /// <remarks>
    /// Asserting the depth AND the value at every level is what the requirement asks for, and the two
    /// together are what prove CONTIGUITY: every level from <c>1</c> to the depth holds the frame it
    /// should, with none missing and none left over. A depth-only assertion would pass against a
    /// stack whose frames had been reordered or shifted by one, which is exactly the failure the
    /// recursion guard and the trace payload would then inherit.
    /// </remarks>
    private static void AssertStackFramesBottomFirst(Vector stack, params string[] expectedFramesBottomFirst)
    {
        Assert.Equal((ulong)expectedFramesBottomFirst.Length, stack.Count());

        for (int level = 0; level < expectedFramesBottomFirst.Length; level++)
        {
            // The one-based conversion, made explicit at the single place it happens in this file.
            ulong oneBasedLevel = (ulong)level + 1UL;

            Assert.Equal(expectedFramesBottomFirst[level], ElementText(stack.GetAt(oneBasedLevel)));
        }
    }

    /// <summary>
    /// Reproduces the recursion guard of <c>n_cst_dwsvc_columnexp.sru:L684-L688</c>: an inclusive
    /// one-based scan of the stack looking for a column name that is already being calculated.
    /// </summary>
    /// <param name="stack">The calculation stack to scan.</param>
    /// <param name="frame">The column name to look for.</param>
    /// <returns>
    /// <see langword="true"/> when the name is already on the stack - which is what makes the legacy
    /// guard return <c>false</c> and abandon the calculation rather than recurse for ever.
    /// </returns>
    /// <remarks>
    /// Written as the legacy writes it, with the bound taken from <see cref="Vector.Count"/> and the
    /// loop running <c>1 .. Count()</c> INCLUSIVE, rather than with a search helper. The point is to
    /// exercise the same index arithmetic the consumer performs: a zero-based reading would skip the
    /// bottom frame and read one past the top, so the guard would miss a genuine recursion and admit
    /// a spurious one.
    /// </remarks>
    private static bool StackAlreadyContains(Vector stack, string frame)
    {
        ulong depth = stack.Count();

        for (ulong level = 1UL; level <= depth; level++)
        {
            if (string.Equals(ElementText(stack.GetAt(level)), frame, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reproduces the trace call stack of <c>n_cst_dwsvc_columnexp.sru:L753-L755</c>: every frame
    /// from the bottom up, each followed by the same separator the legacy appends.
    /// </summary>
    /// <param name="stack">The calculation stack to render.</param>
    /// <returns>
    /// The frames bottom-first with a trailing <c>&gt;</c> after each - so a three-deep stack renders
    /// as <c>outer&gt;middle&gt;inner&gt;</c>, trailing separator included.
    /// </returns>
    /// <remarks>
    /// The trailing separator is faithful, not sloppy. The legacy appends <c>GetAt(nIndex) + "&gt;"</c>
    /// on every iteration and then appends the current column's own name after the loop
    /// [<c>:L757</c>], so the trailing separator is what joins the stack to that final name. Rendering
    /// it as a joiner instead would produce a subtly different payload on contract C-04, which is why
    /// this is kept separate from <see cref="Snapshot(Vector)"/> rather than sharing one helper.
    /// </remarks>
    private static string BuildTraceCallStack(Vector stack)
    {
        StringBuilder callStack = new();
        ulong depth = stack.Count();

        for (ulong level = 1UL; level <= depth; level++)
        {
            callStack.Append(ElementText(stack.GetAt(level))).Append('>');
        }

        return callStack.ToString();
    }

    /// <summary>
    /// <c>Reserve(20)</c> at construction pre-sizes the stack WITHOUT making it look one deep: the
    /// depth stays <c>0</c>, the cursor stays rewound, and the first frame pushed lands at level
    /// <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The legacy consumer really does reserve immediately after construction
    /// [n_cst_dwsvc_columnexp.sru:L2421-L2422], so this is the state every calculation starts from.
    /// Confusing reserved CAPACITY with element COUNT is the specific mistake this test exists to
    /// catch: a reserved-but-empty stack that reported a depth would make the recursion guard scan
    /// twenty phantom levels and would make the first push land at level <c>21</c>, so both the depth
    /// and the landing level are asserted.
    /// </remarks>
    [Fact]
    public void ReservingCapacityLeavesTheStackEmptySoTheFirstPushStillLandsAtLevelOne()
    {
        Vector stack = new();

        stack.Reserve(ReservedDepth);

        Assert.Equal(0UL, stack.Count());
        Assert.Equal(0UL, stack.Position());
        Assert.False(stack.HasNext());
        Assert.Null(stack.Get());

        stack.Append(OuterFrame);

        Assert.Equal(1UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame);
    }

    /// <summary>
    /// Pushing one frame makes <see cref="Vector.Count"/> both the depth AND the one-based index of
    /// the frame just pushed - the identity the legacy consumer depends on at
    /// <c>n_cst_dwsvc_columnexp.sru:L296-L297</c>.
    /// </summary>
    /// <remarks>
    /// The consumer takes <c>k = Count()</c> straight after the append and later pops with
    /// <c>RemoveAt(k)</c> [<c>:L318</c>], with no adjustment anywhere between. This test asserts that
    /// identity directly rather than inferring it: the value returned by <c>Count()</c> is used as an
    /// index and must address the frame that was just appended.
    /// </remarks>
    [Fact]
    public void PushingAFrameMakesCountBothTheDepthAndTheOneBasedIndexOfThatFrame()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        ulong frameIndex = stack.Count();

        Assert.Equal(1UL, frameIndex);
        Assert.Equal(OuterFrame, ElementText(stack.GetAt(frameIndex)));
        AssertStackFramesBottomFirst(stack, OuterFrame);
    }

    /// <summary>
    /// Nested pushes enumerate BOTTOM FIRST, in push order - which is the order the trace call stack
    /// is built in, so it is the order the payload on contract C-04 carries.
    /// </summary>
    /// <remarks>
    /// Three levels deep, mirroring the recursive descent at <c>n_cst_dwsvc_columnexp.sru:L316</c>
    /// where each level pushes before recursing. The bottom-first claim is asserted positively, level
    /// by level, and then negatively against the reversed rendering, because "reversed" is the failure
    /// mode that keeps every element count correct and would therefore survive any count-based check.
    /// </remarks>
    [Fact]
    public void NestedPushesEnumerateBottomFirstInPushOrder()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        stack.Append(MiddleFrame);
        stack.Append(InnerFrame);

        Assert.Equal(3UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame, MiddleFrame, InnerFrame);

        Assert.Equal($"{OuterFrame}>{MiddleFrame}>{InnerFrame}>", BuildTraceCallStack(stack));
        Assert.NotEqual($"{InnerFrame}>{MiddleFrame}>{OuterFrame}>", BuildTraceCallStack(stack));
    }

    /// <summary>
    /// Popping with <c>RemoveAt(Count())</c> removes the top frame and ONLY the top frame, leaving
    /// every frame below it unchanged and still contiguous from level <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The literal pop of <c>n_cst_dwsvc_columnexp.sru:L318</c>, with the index taken from
    /// <see cref="Vector.Count"/> exactly as the consumer takes it. Accepting <c>Count()</c> as a
    /// valid index is the contract rather than a leniency: a zero-based implementation would treat
    /// this very call as out of range, remove nothing, and let the recursion stack grow on every
    /// calculation until the guard at <c>:L684-L688</c> began rejecting legitimate columns.
    /// </remarks>
    [Fact]
    public void PoppingWithRemoveAtCountRemovesTheTopFrameAndOnlyTheTopFrame()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        stack.Append(MiddleFrame);
        stack.Append(InnerFrame);

        stack.RemoveAt(stack.Count());

        Assert.Equal(2UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame, MiddleFrame);
        Assert.Equal($"{OuterFrame}>{MiddleFrame}>", BuildTraceCallStack(stack));
        Assert.False(StackAlreadyContains(stack, InnerFrame));
    }

    /// <summary>
    /// A full nested push and pop sequence tracks BOTH the depth and the frame at every level, at
    /// every step of the descent and of the unwind.
    /// </summary>
    /// <remarks>
    /// The complete legacy idiom driven end to end: each level appends, takes its own frame index from
    /// <see cref="Vector.Count"/> [<c>:L296-L297</c>], recurses [<c>:L316</c>], and finally pops
    /// precisely the frame it pushed [<c>:L318</c>] - so the three saved indices are used in reverse
    /// order on the way out. Asserting the depth alone at each step would not show that the frames
    /// below are intact, and asserting only at the end would not show that the intermediate states are
    /// right either, so both are checked at every step.
    /// </remarks>
    [Fact]
    public void ANestedPushAndPopSequenceTracksBothTheDepthAndTheFrameAtEveryLevel()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        ulong outerIndex = stack.Count();
        Assert.Equal(1UL, outerIndex);
        AssertStackFramesBottomFirst(stack, OuterFrame);

        stack.Append(MiddleFrame);
        ulong middleIndex = stack.Count();
        Assert.Equal(2UL, middleIndex);
        AssertStackFramesBottomFirst(stack, OuterFrame, MiddleFrame);

        stack.Append(InnerFrame);
        ulong innerIndex = stack.Count();
        Assert.Equal(3UL, innerIndex);
        AssertStackFramesBottomFirst(stack, OuterFrame, MiddleFrame, InnerFrame);

        stack.RemoveAt(innerIndex);
        Assert.Equal(2UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame, MiddleFrame);

        stack.RemoveAt(middleIndex);
        Assert.Equal(1UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame);

        stack.RemoveAt(outerIndex);
        Assert.Equal(0UL, stack.Count());
        AssertStackFramesBottomFirst(stack);
    }

    /// <summary>
    /// Fully unwinding the stack returns the depth to <c>0</c>, and a stack that has been unwound is
    /// indistinguishable from a freshly reserved one.
    /// </summary>
    /// <remarks>
    /// The state the engine returns to after every completed calculation, so it has to be exactly the
    /// state the next calculation can start from: an empty depth, a rewound cursor, and a recursion
    /// guard that finds nothing. The reuse half is asserted by pushing again afterwards and checking
    /// the frame lands at level <c>1</c> - a stale frame or a stranded cursor would show up there and
    /// nowhere else.
    /// </remarks>
    [Fact]
    public void FullyUnwindingTheStackReturnsTheDepthToZeroAndLeavesItReusable()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        stack.Append(MiddleFrame);
        stack.Append(InnerFrame);

        while (stack.Count() > 0UL)
        {
            stack.RemoveAt(stack.Count());
        }

        Assert.Equal(0UL, stack.Count());
        Assert.Equal(0UL, stack.Position());
        Assert.False(stack.HasNext());
        Assert.Null(stack.Get());
        Assert.Equal(string.Empty, BuildTraceCallStack(stack));
        Assert.False(StackAlreadyContains(stack, OuterFrame));

        stack.Append(OuterFrame);
        Assert.Equal(1UL, stack.Count());
        AssertStackFramesBottomFirst(stack, OuterFrame);
    }

    /// <summary>
    /// The recursion guard's scan finds a column name that is already on the stack and does not find
    /// one that is absent - including after that name has been popped.
    /// </summary>
    /// <remarks>
    /// The behaviour of <c>n_cst_dwsvc_columnexp.sru:L684-L688</c>, which is the reason this container
    /// is the recursion stack rather than merely a list of names. All three frames are probed, not just
    /// one, because a one-based slip would miss the bottom frame specifically while finding the others
    /// - and the popped-then-absent case is what shows the guard tracks the CURRENT depth rather than
    /// everything ever pushed.
    /// </remarks>
    [Fact]
    public void TheRecursionGuardScanFindsFramesOnTheStackAndMissesAbsentOnes()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        stack.Append(MiddleFrame);
        stack.Append(InnerFrame);

        Assert.True(StackAlreadyContains(stack, OuterFrame));
        Assert.True(StackAlreadyContains(stack, MiddleFrame));
        Assert.True(StackAlreadyContains(stack, InnerFrame));
        Assert.False(StackAlreadyContains(stack, AbsentFrame));

        stack.RemoveAt(stack.Count());

        Assert.True(StackAlreadyContains(stack, OuterFrame));
        Assert.True(StackAlreadyContains(stack, MiddleFrame));
        Assert.False(StackAlreadyContains(stack, InnerFrame));
    }

    /// <summary>
    /// The trace call stack is built bottom-to-top and grows and shrinks with the depth, at every
    /// depth from empty to three deep and back.
    /// </summary>
    /// <remarks>
    /// This is the assertion with the longest reach in the file. The string built here is what the
    /// legacy completes at <c>n_cst_dwsvc_columnexp.sru:L757</c> and then hands to
    /// <c>OnColumnExpTrace</c> at <c>:L758</c>, carried on the expression-trace channel of contract
    /// C-04, so its ORDER is part of a published payload.
    /// Every intermediate depth is checked because a correct three-deep render does not by itself prove
    /// the loop bound is right at one and two deep, and the empty case is checked because an empty
    /// stack must render as the empty string rather than as a lone separator.
    /// </remarks>
    [Fact]
    public void TheTraceCallStackIsBuiltBottomToTopAtEveryDepth()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        Assert.Equal(string.Empty, BuildTraceCallStack(stack));

        stack.Append(OuterFrame);
        Assert.Equal($"{OuterFrame}>", BuildTraceCallStack(stack));

        stack.Append(MiddleFrame);
        Assert.Equal($"{OuterFrame}>{MiddleFrame}>", BuildTraceCallStack(stack));

        stack.Append(InnerFrame);
        Assert.Equal($"{OuterFrame}>{MiddleFrame}>{InnerFrame}>", BuildTraceCallStack(stack));

        stack.RemoveAt(stack.Count());
        Assert.Equal($"{OuterFrame}>{MiddleFrame}>", BuildTraceCallStack(stack));

        stack.RemoveAt(stack.Count());
        Assert.Equal($"{OuterFrame}>", BuildTraceCallStack(stack));

        stack.RemoveAt(stack.Count());
        Assert.Equal(string.Empty, BuildTraceCallStack(stack));
    }

    /// <summary>
    /// A cursor walk over the stack visits the frames bottom-first too, so the cursor and the indexed
    /// scan agree about the stack's order.
    /// </summary>
    /// <remarks>
    /// The two halves of <see cref="Vector"/> meet here. The recursion guard and the trace builder both
    /// scan by INDEX [<c>:L684-L688</c>, <c>:L753-L755</c>], while the cursor walks by POSITION - and if
    /// the two disagreed about direction, code that used one to decide and the other to report would be
    /// self-inconsistent in a way neither a depth check nor a single scan would reveal. Asserted by
    /// walking the cursor from the rewound state and comparing the frames it yields, in order, against
    /// the trace string built from the indexed scan.
    /// </remarks>
    [Fact]
    public void ACursorWalkOverTheStackAgreesWithTheIndexedScanAboutBottomFirstOrder()
    {
        Vector stack = new();
        stack.Reserve(ReservedDepth);

        stack.Append(OuterFrame);
        stack.Append(MiddleFrame);
        stack.Append(InnerFrame);

        StringBuilder walked = new();
        stack.Rewind();

        while (stack.HasNext())
        {
            walked.Append(ElementText(stack.GetNext())).Append('>');
        }

        Assert.Equal($"{OuterFrame}>{MiddleFrame}>{InnerFrame}>", walked.ToString());
        Assert.Equal(BuildTraceCallStack(stack), walked.ToString());
        Assert.Equal(stack.Count(), stack.Position());
    }

}
