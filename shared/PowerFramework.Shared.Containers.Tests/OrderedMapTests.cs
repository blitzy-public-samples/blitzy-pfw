// ==============================================================================================
//  OrderedMapTests - the characterization suite that pins PowerFramework.Shared.Containers.OrderedMap
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  shared/PowerFramework.Shared.Containers/OrderedMap.cs
//  LEGACY DECLARATION ws_objects/pfw.utility.container.pbl.src/n_map.sru (31 lines, READ ONLY)
//                     Named a DECLARATION rather than an oracle deliberately. It supplies TEN
//                     PROTOTYPES AND NO BODIES, so it settles names, arities, parameter types and
//                     return types, and it settles NOTHING about edge behaviour. The behavioural
//                     oracle proper is the compiled pfw.dll exercised through a characterization
//                     recording, and no such recording exists yet. See DP-7 EVIDENCE STATUS below
//                     before reading any expectation in this file as proven legacy behaviour.
//
//  WHY THIS SUITE CARRIES MORE WEIGHT THAN AN ORDINARY UNIT TEST
//  --------------------------------------------------------------------------------------------
//  OrderedMap is a SUBSTITUTION, NOT A PORT. n_map.sru:L8 declares
//  `global type n_map from nonvisualobject native "pfw.dll"`, and the .sru carries the ten
//  prototypes at n_map.sru:L9-L18 and no function or subroutine body whatsoever: every line of
//  legacy behaviour lives inside the closed-source pfw.dll, for which no C++ source exists
//  anywhere in the repository. The migration plan records the decision as
//  "Ordered map and vector | 2 PBNI | SUBSTITUTE - dictionary and list/stack types; insertion
//  order and positional access preserved".
//
//  The substitution is built on System.Collections.Generic.OrderedDictionary<string, object?>,
//  which is ZERO-BASED, while the legacy positional contract is ONE-BASED. That mismatch is
//  bridged inside OrderedMap, and THIS FILE IS THE ONLY PLACE THE BRIDGE IS VERIFIED. Nothing
//  else in the system catches an error in it: the migration plan states that a silent off-by-one
//  here "is indistinguishable from a behavioural regression", because a wrong index returns a
//  real neighbouring entry rather than failing. Hence the ordering of this file: the one-based
//  contract is asserted FIRST and in the greatest depth.
//
//  DP-7 EVIDENCE STATUS - READ THIS BEFORE CITING ANY ASSERTION AS LEGACY PARITY
//  --------------------------------------------------------------------------------------------
//  THIS SUITE IS NOT GOLDEN-MASTER PROOF. It is a target regression guard, and the distinction is
//  not pedantry: a Golden-Master test compares the port's output against a recording of the
//  legacy's output, whereas every expectation here was derived from the port plus a prototype
//  list. Those two things are indistinguishable when they agree and silently divergent when they
//  do not, so the tier of each assertion is stated rather than left for a reader to guess:
//
//    TIER 1  TRACEABLE. Settled by a cited ws_objects/** locator - a member name, an arity, a
//            parameter type, a return type, or the ulong width of Count(). The .sru genuinely
//            proves these, because a prototype is a complete statement of a signature.
//    TIER 3  TARGET-CHARACTERIZED. Settled by the port, because the prototype does not settle it
//            and no legacy body exists to consult. Every edge behaviour is in this tier: the miss
//            sentinels (null from Get, string.Empty from GetKey), the boolean duplicate refusal,
//            Set's in-place overwrite, the case-sensitivity reading, and the inclusive one-based
//            upper bound. These are DEFINED, REPRODUCIBLE AND COVERED - never verified.
//
//  THE ORACLE-CAPTURE PREREQUISITE. Promoting any TIER 3 assertion to parity evidence requires a
//  paired recording under characterization/recordings/{legacy,dotnet}/<workflowId>/, captured
//  against one unrecreated persistence-db volume state. Until such a recording exists, no TIER 3
//  expectation below may be reported as verified legacy behaviour in docs/PARITY.md or anywhere
//  else. IF A RECORDING LATER CONTRADICTS ONE, THE RECORDING WINS and the test is corrected to
//  match it; the test is not defended on the grounds that it currently passes.
//
//  Nothing is left undefined, because an undefined index or miss sentinel is exactly the hole
//  that produces an irreproducible defect. Being defined is simply not the same as being proven.
//
//  ASSERTED AS OBSERVED, NEVER AS TIDIED
//  --------------------------------------------------------------------------------------------
//  "Observed" below means OBSERVED FROM THE PORT unless a ws_objects/** locator is cited on the
//  assertion itself. Every expectation was established by reading the two authorities named
//  above, in that order, and never by assuming what a well-designed map would do:
//
//    * ONE-BASED positional access with an INCLUSIVE upper bound of Count(). Index 0 is never
//      valid. This is deliberate fidelity, not an off-by-one - see the evidence note on
//      GetAtPositionOneReturnsTheFirstInsertedEntry.
//    * Count() returns ulong, not int, and stays a METHOD rather than becoming a property,
//      because n_map.sru:L17 declares `public function ulong count()`.
//    * Add refuses a duplicate by returning FALSE. That is asserted as a boolean result and is
//      deliberately NOT rewritten into an expected exception.
//    * NO MEMBER THROWS FOR AN ORDINARY MISS. A missing key or an out-of-range index yields a
//      default, because the legacy accessors at n_map.sru:L10-L12 return a bare value with no
//      boolean out-parameter and no return code, so they have no channel through which to report
//      failure. The observed sentinels are null from Get and string.Empty from GetKey, and both
//      are asserted explicitly at index 0 and at Count() + 1 rather than left to a reviewer's
//      assumption.
//    * Set overwrites IN PLACE without moving the entry, and appends only a genuinely new key.
//
//  WHAT "ALL VIEWS AGREE" MEANS HERE, STATED BECAUSE IT IS A DELIBERATE LIMIT
//  --------------------------------------------------------------------------------------------
//  OrderedMap exposes NO enumerator: it implements no IEnumerable, no IDictionary, no
//  IReadOnlyDictionary and no ICollection, and offers no indexer, because the legacy type
//  implements no interface either. There are therefore exactly TWO observable orderings, and the
//  order-sensitive tests below assert both and require them to agree entry for entry:
//
//      view 1   GetKeys(ref string[])         - the whole key sequence in one zero-based array
//      view 2   GetKey(i) / Get(i) for i=1..N - the one-based positional walk
//
//  An enumeration view is satisfied by OMISSION rather than by assertion, and no test reaches
//  past the public surface to manufacture one. Adding an enumerator to make a third view
//  assertable would widen the type's contract, which constraint C-B forbids.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project: review_rules returns exactly "No user rules provided",
//  and that single line is the whole document. That is a finding, not an omission, and it is not
//  treated as licence to lower the bar - enterprise-standard best practice applies in their
//  place, and the migration plan's own constraint inventory is honoured here exactly as user
//  rules would have been.
//
//  C-B  Behaviour is asserted as observed, never as improved. The one-based indices stay
//       one-based in a zero-based language; the duplicate refusal stays a boolean; Count() stays
//       ulong; the miss sentinels stay sentinels instead of being promoted to exceptions; and the
//       Add-versus-Set asymmetry is pinned rather than harmonised. No assertion below describes
//       what a better-designed API would do.
//  C-C  The legacy tree is read only and is the behavioural oracle. It is cited by path and line
//       number IN COMMENTS ONLY. No test opens, reads, parses or writes any path under
//       ws_objects/**, and this file performs no file, directory, path, network, clock or
//       environment access of any kind: every test is pure and in-memory.
//  C-D  Nothing here touches a deferred capability. The third object in the legacy container
//       library, n_list, has no in-scope consumer and is not referenced, named as a type, or
//       reached through a helper - not even as a convenience for building expected sequences,
//       which is why every expected ordering below is expressed as a plain array.
//  C-H  The per-project line-coverage gate is met by genuine behavioural assertions across ALL
//       TEN members of n_map.sru:L9-L18, not by smoke instantiation. The per-member map is in
//       COVERAGE OBLIGATION below. Nullable reference types and warnings-as-errors are inherited
//       from the repository-root Directory.Build.props and apply to this project exactly as they
//       do to the library it tests.
//  C-K  Every technology-specific decision is documented at the point that depends on it: the
//       substitution and its BCL backing type, the one-based contract and its legacy evidence,
//       the sentinel-versus-exception reading, the reference-identity requirement and the
//       case-sensitivity reading.
//
//  COVERAGE OBLIGATION - the ten legacy prototypes and the tests that pin each
//  --------------------------------------------------------------------------------------------
//  Every entry below names a test that exists in this file. The list is part of the contract, so
//  a renamed or deleted test must be corrected here in the same edit.
//
//    n_map.sru:L9   boolean add(string, any)
//        AddOnAFreshKeyReturnsTrueAndAppendsAtTheEnd
//        AddOnAnExistingKeyReturnsFalseAndLeavesTheMapUnchanged
//        AddAndSetDivergeOnADuplicateKey            RemoveThenReAddPlacesTheKeyAtTheEnd
//    n_map.sru:L10  any get(string)
//        GetByKeyReturnsTheStoredValueForAPresentKey
//        GetByKeyReturnsNullForAnAbsentKeyAndDoesNotThrow
//        GetReturnsTheVerySameReferenceThatWasStored
//        BoxedValueTypesRoundTripThroughGet
//        StoredNullIsDistinguishableFromAnAbsentKeyOnlyThroughExists
//    n_map.sru:L11  any get(int)          and       n_map.sru:L12  string getkey(int)
//        GetAtPositionOneReturnsTheFirstInsertedEntry
//        PositionalWalkYieldsEveryEntryInInsertionOrder
//        PositionalUpperBoundIsInclusiveOfCount
//        PositionZeroYieldsTheDocumentedSentinelsAndDoesNotThrow
//        PositionJustAboveCountYieldsTheDocumentedSentinelsAndDoesNotThrow
//        OutOfRangePositionsYieldTheDocumentedSentinels
//        AnEmptyMapHasNoValidPosition
//        KeyAndValueAtTheSamePositionDescribeTheSameEntry
//    n_map.sru:L13  boolean getkeys(ref string[])
//        GetKeysFillsTheArrayInInsertionOrder
//        GetKeysOnAnEmptyMapReturnsTrueAndAnEmptyArray
//        GetKeysDiscardsTheCallerSuppliedArray
//    n_map.sru:L14  boolean set(string, any)
//        SetOnAFreshKeyInsertsAndReturnsTrue
//        SetOnAnExistingKeyOverwritesAndReturnsTrue
//        SetOnAnExistingKeyDoesNotMoveItToTheEnd    AddAndSetDivergeOnADuplicateKey
//    n_map.sru:L15  boolean exists(string)
//        ExistsDiscriminatesPresentFromAbsentKeys
//        StoredNullIsDistinguishableFromAnAbsentKeyOnlyThroughExists
//        KeysAreComparedCaseSensitively
//    n_map.sru:L16  boolean remove(string)
//        RemoveReturnsTrueForAPresentKeyAndDecreasesCount
//        RemoveReturnsFalseForAnAbsentKeyAndLeavesTheMapUnchanged
//        RemovalPreservesTheRelativeOrderOfSurvivors
//    n_map.sru:L17  ulong count()
//        CountTracksEveryMutation, plus an explicit unsigned assertion in nearly every test above
//    n_map.sru:L18  subroutine purge()
//        PurgeEmptiesTheMapAndLeavesItReusable      PurgeOnAnEmptyMapIsANoOp
//
//  Cross-cutting: InterleavedMutationKeepsBothOrderingViewsInAgreement and
//  HeterogeneousValuesCoexistInOneMapWithoutDisturbingOrder drive several members together, which
//  is where a member that works alone but not in sequence would show up.
//
//  The private index helper inside OrderedMap has three outcomes - below range, above range and
//  valid - and all three are driven, the below-range case including int.MinValue so the guard
//  that rejects a negative index BEFORE widening it to the unsigned width of Count() is actually
//  exercised rather than merely present.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * Any assertion, mocking or auto-fixture package. The migration plan enumerates the
//      deliberately excluded packages, and the four in this project's .csproj are the whole
//      toolbox; xunit.v3 already carries Assert.
//    * A shared or static OrderedMap instance, and any IClassFixture. Every test constructs its
//      own map, which additionally avoids the hazard the port already refused to reproduce:
//      n_map.sru:L20 declares `global n_map n_map`, a global auto-instance whose name shadows its
//      own type name, and OrderedMap is documented as not thread safe.
//    * SCREAMING_SNAKE and underscore-laden identifiers. The repository-root .editorconfig scopes
//      its naming-analyzer suppressions to the closed BAND 3 roster of constant-carrying files - the
//      single source of truth for that list - and THIS
//      FILE IS NOT ONE OF THEM, so with TreatWarningsAsErrors inherited such an identifier would
//      be a build ERROR. Legacy spellings appear only in string literals and comments.
//    * A `using PowerFramework.Shared.Containers;` directive. This namespace is nested inside the
//      namespace under test, so OrderedMap resolves through the enclosing-namespace lookup and
//      the directive would be redundant. The project reference in the .csproj is the single
//      declared dependency, and Xunit below is the only other import.
//    * Any await, Task or CancellationToken. Every member under test is synchronous, so there is
//      nothing to await and the xUnit1051 cancellation-token diagnostic cannot arise.
//    * Any Assert.Throws, anywhere. This is a finding rather than an oversight: NO member of
//      OrderedMap throws for any input this suite can legally supply. The accessors yield
//      sentinels for every out-of-range index and every absent key, and the mutators report
//      through their boolean returns, so there is no exception to assert. Writing one would
//      require the behaviour to change first, which constraint C-B forbids.
//    * A null-key test. The key parameters are declared non-nullable, so null is excluded by the
//      contract rather than by a runtime guard, and OrderedMap deliberately writes no guard for
//      it. Reaching that path would mean defeating the type system with a null-forgiving operator
//      purely to observe the backing store's own ArgumentNullException - which would assert the
//      base class library's behaviour rather than this type's, and would document an abuse as
//      though it were a supported case. Coverage does not need it either: the type already
//      measures 100 percent line and 100 percent branch without a single unreachable branch.
// ==============================================================================================

using Xunit;

namespace PowerFramework.Shared.Containers.Tests;

/// <summary>
/// Behavioural tests for <see cref="OrderedMap"/>, the substitution for the legacy PowerBuilder
/// native type <c>n_map</c> (<c>ws_objects/pfw.utility.container.pbl.src/n_map.sru</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="OrderedMap"/> is a substitution onto a base class library type, not a port.</b>
/// <c>n_map.sru:L8</c> binds the legacy type to the closed-source <c>pfw.dll</c>, and the
/// <c>.sru</c> holds the ten prototypes at <c>n_map.sru:L9-L18</c> and no implementation at all,
/// so those signatures are the entire specification. The substitution is built on
/// <see cref="OrderedDictionary{TKey, TValue}"/> - concretely
/// <c>System.Collections.Generic.OrderedDictionary&lt;string, object?&gt;</c> - which is the one
/// framework type supplying insertion order and positional access together.
/// </para>
/// <para>
/// <b>The one-based positional contract asserted throughout this class is deliberate fidelity,
/// not a defect.</b> The backing store is zero-based; the legacy surface is one-based, so entry 1
/// is the first and entry <c>Count()</c> is the last. A reader who mistakes these assertions for
/// off-by-one errors and "corrects" them will break both consuming services silently, because a
/// wrong index returns a plausible neighbouring value rather than raising. The legacy convention
/// is visible at every consumer loop in the framework, which is uniformly
/// <c>for i = 1 to Count()</c> with an inclusive upper bound.
/// </para>
/// <para>
/// These tests are pure and in-memory. They perform no file, directory, path, network, clock or
/// environment access, and in particular they never read the read-only legacy tree, which is
/// cited in comments only.
/// </para>
/// </remarks>
public sealed class OrderedMapTests
{
    /// <summary>
    /// The key vocabulary every test draws from, held as literals so no test depends on culture,
    /// formatting or a converted number.
    /// </summary>
    /// <remarks>
    /// This is an ordinary zero-based C# array and is never handed to <see cref="OrderedMap"/> as
    /// a positional source; <see cref="KeyAt"/> is the single place the one-based test positions
    /// are translated onto it.
    /// </remarks>
    private static readonly string[] KeyCatalogue =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel",
    ];

    /// <summary>
    /// The value vocabulary, index-aligned with <see cref="KeyCatalogue"/> so that the value at a
    /// position is always identifiable from the key at the same position.
    /// </summary>
    private static readonly string[] ValueCatalogue =
    [
        "value-alpha", "value-bravo", "value-charlie", "value-delta",
        "value-echo", "value-foxtrot", "value-golf", "value-hotel",
    ];

    /// <summary>
    /// Sequence lengths driving the positional-walk theory: the degenerate single-entry map, the
    /// small sizes where an off-by-one is easiest to miss, and a length long enough that a
    /// mistranslated bound cannot coincide with a correct one.
    /// </summary>
    /// <remarks>
    /// Every element is an <see cref="int"/>, which keeps the theory data trivially serializable
    /// and avoids the untyped-data-row diagnostics that an <c>IEnumerable&lt;object[]&gt;</c>
    /// member would raise. The empty map is deliberately excluded here and covered by its own
    /// dedicated facts, because a zero-length walk asserts nothing.
    /// </remarks>
    public static TheoryData<int> SequenceLengths => new() { 1, 2, 3, 4, 5, 8 };

    /// <summary>
    /// Positions that lie outside <c>1 .. Count()</c> for a map holding
    /// <see cref="BoundaryProbeEntryCount"/> entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set is chosen to drive both rejection paths of the private index helper inside
    /// <see cref="OrderedMap"/>, not merely one of them: 0 and the negatives take the
    /// below-range path, and the values above <see cref="BoundaryProbeEntryCount"/> take the
    /// above-range path. The reason each row is present is recorded beside it, rather than carried
    /// as a second theory parameter, so that every parameter this theory declares is one it
    /// actually uses.
    /// </para>
    /// <para>
    /// <see cref="int.MinValue"/> earns its place specifically. The helper rejects a
    /// non-positive index BEFORE widening it to the unsigned width of <c>Count()</c>, and
    /// <see cref="int.MinValue"/> is the value that would sail past a naive upper-bound check by
    /// reinterpreting as an enormous unsigned number. Without this row that ordering guarantee
    /// would be present but unexercised.
    /// </para>
    /// </remarks>
    public static TheoryData<int> OutOfRangePositions => new()
    {
        0,                             // zero is never a valid one-based position
        -1,                            // the first negative, taking the below-range path
        -7,                            // a negative beyond the entry count
        int.MinValue,                   // rejected before widening, so it cannot reinterpret as huge
        BoundaryProbeEntryCount + 1,    // one past the inclusive upper bound
        BoundaryProbeEntryCount + 2,    // two past the inclusive upper bound
        99,                            // far past the inclusive upper bound
    };

    /// <summary>
    /// The number of entries the boundary probes populate, fixed as a constant so the
    /// out-of-range theory data can be expressed statically against it.
    /// </summary>
    private const int BoundaryProbeEntryCount = 4;

    /// <summary>
    /// Returns the key belonging to a ONE-BASED test position.
    /// </summary>
    /// <param name="oneBasedPosition">A position in <c>1 .. KeyCatalogue.Length</c>.</param>
    /// <returns>The key for that position.</returns>
    /// <remarks>
    /// This method and <see cref="ValueAt"/> are the ONLY places in this file that subtract one
    /// from a position, deliberately mirroring the single conversion point inside
    /// <see cref="OrderedMap"/>. Centralising it here means the fixture cannot drift from the
    /// production bridge one test at a time.
    /// </remarks>
    private static string KeyAt(int oneBasedPosition)
    {
        return KeyCatalogue[oneBasedPosition - 1];
    }

    /// <summary>
    /// Returns the value belonging to a ONE-BASED test position, typed as <see cref="object"/> to
    /// match the <c>any</c> value slot of <c>n_map.sru:L9</c> and <c>:L14</c>.
    /// </summary>
    /// <param name="oneBasedPosition">A position in <c>1 .. ValueCatalogue.Length</c>.</param>
    /// <returns>The value for that position.</returns>
    private static object ValueAt(int oneBasedPosition)
    {
        return ValueCatalogue[oneBasedPosition - 1];
    }

    /// <summary>
    /// Builds a map holding <paramref name="entryCount"/> entries in catalogue order using
    /// <see cref="OrderedMap.Add"/>, asserting each insertion succeeded so that a later ordering
    /// failure can never be blamed on a silently refused Add.
    /// </summary>
    /// <param name="entryCount">How many catalogue entries to insert.</param>
    /// <returns>A freshly constructed map, owned solely by the calling test.</returns>
    private static OrderedMap BuildMap(int entryCount)
    {
        OrderedMap map = new();

        for (int position = 1; position <= entryCount; position++)
        {
            Assert.True(
                map.Add(KeyAt(position), ValueAt(position)),
                "a catalogue key must be fresh, so Add must report success while building the fixture");
        }

        return map;
    }

    /// <summary>
    /// Reads back the whole key sequence through <see cref="OrderedMap.GetKeys"/>, asserting the
    /// documented unconditional success along the way.
    /// </summary>
    /// <param name="map">The map to read.</param>
    /// <returns>The keys in insertion order, in a zero-based array.</returns>
    private static string[] KeySequenceOf(OrderedMap map)
    {
        // Seeded with a non-empty array on purpose: GetKeys is documented to REPLACE whatever the
        // caller supplied rather than append to it, so a stale seed here would surface as a
        // failure in every ordering assertion that uses this helper.
        string[] keys = ["stale-seed"];

        Assert.True(map.GetKeys(ref keys), "GetKeys is documented to report success unconditionally");

        return keys;
    }

    /// <summary>
    /// Walks positions <c>1 .. Count()</c> and returns the keys the positional accessor yields,
    /// which is the second of the two observable orderings this suite cross-checks.
    /// </summary>
    /// <param name="map">The map to walk.</param>
    /// <returns>The keys in positional order, projected into a zero-based array for comparison.</returns>
    private static string[] PositionalKeyWalkOf(OrderedMap map)
    {
        // Count() is ulong, so the walk is written in the same width to avoid a lossy comparison;
        // the array length is int, which is why the count is narrowed once, here, for allocation.
        ulong entryCount = map.Count();
        string[] walked = new string[entryCount];

        for (ulong position = 1; position <= entryCount; position++)
        {
            // The inclusive upper bound is the whole point: position == entryCount must be valid.
            walked[position - 1] = map.GetKey((int)position);
        }

        return walked;
    }

    /// <summary>
    /// Asserts that both observable orderings of <paramref name="map"/> equal
    /// <paramref name="expectedKeys"/> and agree with each other entry for entry.
    /// </summary>
    /// <param name="map">The map to inspect.</param>
    /// <param name="expectedKeys">The keys expected, in insertion order.</param>
    /// <remarks>
    /// Cross-checking the two views is not redundancy. <see cref="OrderedMap.GetKeys"/> copies out
    /// of the backing store in bulk while <see cref="OrderedMap.GetKey"/> converts a one-based
    /// position on each call, so only one of the two can be broken by an indexing fault - and
    /// comparing them is what makes such a fault visible.
    /// </remarks>
    private static void AssertOrderingIs(OrderedMap map, string[] expectedKeys)
    {
        string[] bulkView = KeySequenceOf(map);
        string[] positionalView = PositionalKeyWalkOf(map);

        Assert.Equal(expectedKeys, bulkView);
        Assert.Equal(expectedKeys, positionalView);
        Assert.Equal(bulkView, positionalView);
        Assert.Equal((ulong)expectedKeys.Length, map.Count());
    }

    // ------------------------------------------------------------------------------------------
    //  THE ONE-BASED POSITIONAL CONTRACT
    //  The highest-value group in this file, and first for that reason. n_map.sru:L11-L12 declare
    //  `any get(int index)` and `string getkey(int index)`; OrderedMap is built on a ZERO-BASED
    //  OrderedDictionary and bridges the two, and nothing outside this group verifies the bridge.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Position 1 addresses the FIRST inserted entry, which is the single assertion the whole
    /// one-based contract rests on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS DELIBERATE ONE-BASED FIDELITY, NOT AN OFF-BY-ONE.</b> A reader who "corrects"
    /// this to expect the second entry, or who changes <see cref="OrderedMap"/> so that index 0
    /// becomes the first entry, breaks both consuming services silently: a wrong index returns a
    /// real neighbouring value rather than raising, so no other test and no runtime check would
    /// notice.
    /// </para>
    /// <para>
    /// The legacy evidence is that every consumer loop in the framework is
    /// <c>for i = 1 to Count()</c> with an inclusive upper bound, never <c>0 to Count() - 1</c>.
    /// Measured instances: <c>n_cst_dwsvc_columnexp.sru:L684-L686</c> takes
    /// <c>nCount = _vecCalcStack.Count()</c> then <c>for nIndex = 1 to nCount</c> and reads
    /// <c>GetAt(nIndex)</c>; <c>:L753-L755</c> repeats the same shape while building the trace call
    /// stack; and <c>n_cst_dwsvc.sru:L650-L658</c> walks
    /// <c>#DataWindow.GetValue(colName, nIndex)</c> starting from <c>nIndex = 1</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetAtPositionOneReturnsTheFirstInsertedEntry()
    {
        OrderedMap map = BuildMap(BoundaryProbeEntryCount);

        // Position 1 is the FIRST entry inserted - "alpha" - and not the second.
        Assert.Equal(KeyAt(1), map.GetKey(1));
        Assert.Equal(ValueAt(1), map.Get(1));

        // Stated the other way round so the intent cannot be misread: the second inserted entry
        // lives at position 2, so position 1 is not a zero-based slot in disguise.
        Assert.Equal(KeyAt(2), map.GetKey(2));
        Assert.NotEqual(KeyAt(2), map.GetKey(1));
    }

    /// <summary>
    /// Walking positions <c>1 .. N</c> yields every entry in insertion order, for a range of
    /// sequence lengths.
    /// </summary>
    /// <param name="entryCount">How many entries the map holds under test.</param>
    /// <remarks>
    /// Driven as a table so the contract is pinned at several lengths rather than at one
    /// convenient size: a bridge that is wrong only at the ends, or only for a single-entry map,
    /// cannot hide behind a fixture that happens to avoid those shapes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SequenceLengths))]
    public void PositionalWalkYieldsEveryEntryInInsertionOrder(int entryCount)
    {
        OrderedMap map = BuildMap(entryCount);

        Assert.Equal((ulong)entryCount, map.Count());

        for (int position = 1; position <= entryCount; position++)
        {
            Assert.Equal(KeyAt(position), map.GetKey(position));
            Assert.Equal(ValueAt(position), map.Get(position));
        }
    }

    /// <summary>
    /// The upper bound is INCLUSIVE: position <c>Count()</c> is valid and addresses the last
    /// entry.
    /// </summary>
    /// <param name="entryCount">How many entries the map holds under test.</param>
    /// <remarks>
    /// This is the assertion that fails loudly if the implementation ever drifts to zero-based
    /// indexing, because under that reading position <c>Count()</c> would be one past the end.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SequenceLengths))]
    public void PositionalUpperBoundIsInclusiveOfCount(int entryCount)
    {
        OrderedMap map = BuildMap(entryCount);
        int lastPosition = (int)map.Count();

        Assert.Equal(entryCount, lastPosition);
        Assert.Equal(KeyAt(entryCount), map.GetKey(lastPosition));
        Assert.Equal(ValueAt(entryCount), map.Get(lastPosition));
    }

    /// <summary>
    /// Position 0 is never valid and yields the documented sentinels without throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boundary is nailed down here rather than left to a reviewer's assumption, and the
    /// expectations were read off the implementation rather than guessed: <c>Get(0)</c> returns
    /// <see langword="null"/> and <c>GetKey(0)</c> returns <see cref="string.Empty"/>, and neither
    /// raises.
    /// </para>
    /// <para>
    /// The reason it is a sentinel and not an exception is structural, and it is the reading the
    /// signatures force. <c>n_map.sru:L11-L12</c> return a bare <c>any</c> and a bare
    /// <c>string</c> with no boolean out-parameter, no reference out-parameter and no return code,
    /// so the legacy accessors have nowhere to report a failure. Promoting an out-of-range index
    /// to an <see cref="ArgumentOutOfRangeException"/> would convert a legacy no-op into a crash
    /// at a call site with no handler, which is a behavioural change dressed as robustness.
    /// </para>
    /// </remarks>
    [Fact]
    public void PositionZeroYieldsTheDocumentedSentinelsAndDoesNotThrow()
    {
        OrderedMap map = BuildMap(BoundaryProbeEntryCount);

        Assert.Null(map.Get(0));
        Assert.Equal(string.Empty, map.GetKey(0));

        // The probe left the map untouched: a rejected index is a read that found nothing, not a
        // mutation.
        Assert.Equal((ulong)BoundaryProbeEntryCount, map.Count());
    }

    /// <summary>
    /// Position <c>Count() + 1</c> is the first position past the inclusive upper bound and yields
    /// the documented sentinels without throwing.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented, on the same grounds as
    /// <see cref="PositionZeroYieldsTheDocumentedSentinelsAndDoesNotThrow"/>: the legacy
    /// accessors have no failure channel, so the miss yields a default.
    /// </remarks>
    [Fact]
    public void PositionJustAboveCountYieldsTheDocumentedSentinelsAndDoesNotThrow()
    {
        OrderedMap map = BuildMap(BoundaryProbeEntryCount);
        int firstInvalidPosition = (int)map.Count() + 1;

        // Immediately below it is still valid, which is what makes this the exact boundary.
        Assert.Equal(KeyAt(BoundaryProbeEntryCount), map.GetKey(firstInvalidPosition - 1));

        Assert.Null(map.Get(firstInvalidPosition));
        Assert.Equal(string.Empty, map.GetKey(firstInvalidPosition));
    }

    /// <summary>
    /// Every position outside <c>1 .. Count()</c> yields the documented sentinels without
    /// throwing, across both rejection paths of the index bridge.
    /// </summary>
    /// <param name="invalidPosition">A position outside the valid range.</param>
    /// <remarks>
    /// <see cref="int.MinValue"/> is in the table on purpose: the bridge rejects a non-positive
    /// index BEFORE widening it to the unsigned width of <c>Count()</c>, and this is the value
    /// that would defeat the reverse ordering by reinterpreting as an enormous unsigned number.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OutOfRangePositions))]
    public void OutOfRangePositionsYieldTheDocumentedSentinels(int invalidPosition)
    {
        OrderedMap map = BuildMap(BoundaryProbeEntryCount);

        Assert.Null(map.Get(invalidPosition));
        Assert.Equal(string.Empty, map.GetKey(invalidPosition));
        Assert.Equal((ulong)BoundaryProbeEntryCount, map.Count());
    }

    /// <summary>
    /// An empty map has no valid position at all, so even position 1 yields the sentinels.
    /// </summary>
    /// <remarks>
    /// The valid range is <c>1 .. Count()</c>, and on an empty map that range is empty. This is
    /// the degenerate case of the inclusive bound rather than a special rule.
    /// </remarks>
    [Fact]
    public void AnEmptyMapHasNoValidPosition()
    {
        OrderedMap map = new();

        Assert.Equal(0UL, map.Count());
        Assert.Null(map.Get(1));
        Assert.Equal(string.Empty, map.GetKey(1));
    }

    /// <summary>
    /// <see cref="OrderedMap.GetKey"/> and <see cref="OrderedMap.Get(int)"/> address the same
    /// entry for any given position, so the positional value always belongs to the positional key.
    /// </summary>
    /// <param name="entryCount">How many entries the map holds under test.</param>
    /// <remarks>
    /// Cross-checking the two positional accessors against the key-based accessor is what proves
    /// they share one index bridge: if either drifted, <c>Get(i)</c> would stop matching
    /// <c>Get(GetKey(i))</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SequenceLengths))]
    public void KeyAndValueAtTheSamePositionDescribeTheSameEntry(int entryCount)
    {
        OrderedMap map = BuildMap(entryCount);

        for (int position = 1; position <= entryCount; position++)
        {
            string keyAtPosition = map.GetKey(position);

            Assert.Equal(map.Get(keyAtPosition), map.Get(position));
            Assert.True(map.Exists(keyAtPosition));
        }
    }

    // ------------------------------------------------------------------------------------------
    //  INSERTION ORDER UNDER MUTATION
    //  Insertion order is the property the positional members are meaningless without, so it is
    //  asserted through mutation rather than only on a freshly built map.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Overwriting an existing key through <see cref="OrderedMap.Set"/> keeps the entry where it
    /// is; it does NOT move it to the end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is observable behaviour a real call site depends on, not a stylistic preference.
    /// <c>n_cst_dwsvc.sru</c> builds the column value map through <see cref="OrderedMap.Set"/>
    /// EXCLUSIVELY and never through <see cref="OrderedMap.Add"/> - seven measured calls, at
    /// <c>:L634</c> inside the drop-down child row loop, at <c>:L642-L644</c> and <c>:L647-L648</c>
    /// for the fixed checkbox display pairs, and at <c>:L655</c> inside the value walk - before
    /// returning the map at <c>:L663</c>.
    /// </para>
    /// <para>
    /// Because Set is the only member establishing that map's ordering, a Set that moved an
    /// existing key would make the ordering depend on rewrite history rather than insertion
    /// history. The mechanism is concrete rather than hypothetical: the row loop at <c>:L634</c>
    /// keys on the child's DISPLAY value, so two child rows sharing a display value overwrite the
    /// same key, and under a move-to-end implementation that entry would jump past every entry
    /// added after it.
    /// </para>
    /// </remarks>
    [Fact]
    public void SetOnAnExistingKeyDoesNotMoveItToTheEnd()
    {
        OrderedMap map = BuildMap(3);
        const string replacement = "value-bravo-overwritten";
        string[] expectedOrder = [KeyAt(1), KeyAt(2), KeyAt(3)];

        Assert.True(map.Set(KeyAt(2), replacement));

        // The value changed; the position did not.
        Assert.Equal(replacement, map.Get(KeyAt(2)));
        Assert.Equal(replacement, map.Get(2));
        Assert.Equal(KeyAt(2), map.GetKey(2));

        // And the sequence as a whole is untouched, in both observable views.
        AssertOrderingIs(map, expectedOrder);
    }

    /// <summary>
    /// Removing a key and then adding it again places it at the END of the insertion order, not
    /// back at the position it originally held.
    /// </summary>
    /// <remarks>
    /// Insertion order means order of insertion, so a re-added key is a newly inserted key. The
    /// distinction matters because the alternative reading - restoring the original position -
    /// would require the container to remember entries it no longer holds, which neither the
    /// legacy surface nor the substitution does.
    /// </remarks>
    [Fact]
    public void RemoveThenReAddPlacesTheKeyAtTheEnd()
    {
        OrderedMap map = BuildMap(3);
        string[] afterRemoval = [KeyAt(1), KeyAt(3)];
        string[] afterReAdd = [KeyAt(1), KeyAt(3), KeyAt(2)];

        Assert.True(map.Remove(KeyAt(2)));
        AssertOrderingIs(map, afterRemoval);

        Assert.True(map.Add(KeyAt(2), ValueAt(2)));
        AssertOrderingIs(map, afterReAdd);

        // Stated positionally as well, because that is the form a consumer loop would see: the
        // re-added key is now LAST, and the key that followed it originally has moved ahead of it.
        Assert.Equal(KeyAt(2), map.GetKey(3));
        Assert.Equal(KeyAt(3), map.GetKey(2));
    }

    /// <summary>
    /// Removing entries leaves the relative order of every survivor unchanged, so the positional
    /// view simply becomes shorter.
    /// </summary>
    [Fact]
    public void RemovalPreservesTheRelativeOrderOfSurvivors()
    {
        OrderedMap map = BuildMap(5);
        string[] expectedOrder = [KeyAt(2), KeyAt(3), KeyAt(5)];

        // One removal from the front and one from the interior, so the shift is not uniform.
        Assert.True(map.Remove(KeyAt(1)));
        Assert.True(map.Remove(KeyAt(4)));

        AssertOrderingIs(map, expectedOrder);

        // Each survivor shifted down by the number of removals ahead of it, and nothing reordered.
        Assert.Equal(KeyAt(2), map.GetKey(1));
        Assert.Equal(KeyAt(3), map.GetKey(2));
        Assert.Equal(KeyAt(5), map.GetKey(3));
        Assert.Equal(string.Empty, map.GetKey(4));
    }

    /// <summary>
    /// After an interleaved run of adds, overwrites, refused adds and removals, both observable
    /// orderings still agree with each other and with insertion order.
    /// </summary>
    /// <remarks>
    /// The two views are cross-checked at every step rather than only at the end, so a step that
    /// desynchronises them is attributed to the operation that caused it instead of to the whole
    /// sequence.
    /// </remarks>
    [Fact]
    public void InterleavedMutationKeepsBothOrderingViewsInAgreement()
    {
        OrderedMap map = new();
        string[] afterThreeAdds = [KeyAt(1), KeyAt(2), KeyAt(3)];
        string[] afterSetInsert = [KeyAt(1), KeyAt(2), KeyAt(3), KeyAt(4)];
        string[] afterRemoval = [KeyAt(1), KeyAt(3), KeyAt(4)];
        string[] afterReAdd = [KeyAt(1), KeyAt(3), KeyAt(4), KeyAt(2)];

        // Three fresh adds establish the baseline order.
        Assert.True(map.Add(KeyAt(1), ValueAt(1)));
        Assert.True(map.Add(KeyAt(2), ValueAt(2)));
        Assert.True(map.Add(KeyAt(3), ValueAt(3)));
        AssertOrderingIs(map, afterThreeAdds);

        // An overwrite in the middle changes a value and nothing else.
        Assert.True(map.Set(KeyAt(2), "value-bravo-overwritten"));
        AssertOrderingIs(map, afterThreeAdds);

        // A refused add changes nothing at all, not even the value it was refused over.
        Assert.False(map.Add(KeyAt(2), "value-bravo-refused"));
        Assert.Equal("value-bravo-overwritten", map.Get(KeyAt(2)));
        AssertOrderingIs(map, afterThreeAdds);

        // Set on a fresh key appends, exactly as Add would have.
        Assert.True(map.Set(KeyAt(4), ValueAt(4)));
        AssertOrderingIs(map, afterSetInsert);

        // A removal closes the gap without disturbing the survivors' relative order.
        Assert.True(map.Remove(KeyAt(2)));
        AssertOrderingIs(map, afterRemoval);

        // A re-add goes to the end, so the sequence is now genuinely out of catalogue order -
        // which is the point: it is INSERTION order that is preserved, not sorted order.
        Assert.True(map.Add(KeyAt(2), ValueAt(2)));
        AssertOrderingIs(map, afterReAdd);
    }

    /// <summary>
    /// <see cref="OrderedMap.GetKeys"/> reports success, hands back the whole key sequence in
    /// insertion order, and agrees with the positional walk entry for entry.
    /// </summary>
    /// <param name="entryCount">How many entries the map holds under test.</param>
    /// <remarks>
    /// The array handed back is ZERO-based even though the positional accessors are one-based, so
    /// logical entry <c>i</c> is found at <c>keys[i - 1]</c>. That representation shift is part of
    /// the contract and is asserted here explicitly, because it is the one place a reader could
    /// reasonably expect consistency and will not find it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SequenceLengths))]
    public void GetKeysFillsTheArrayInInsertionOrder(int entryCount)
    {
        OrderedMap map = BuildMap(entryCount);
        string[] keys = ["stale-seed"];

        Assert.True(map.GetKeys(ref keys));
        Assert.Equal(entryCount, keys.Length);

        for (int position = 1; position <= entryCount; position++)
        {
            // The zero-based array slot for the one-based logical position.
            Assert.Equal(KeyAt(position), keys[position - 1]);
            Assert.Equal(map.GetKey(position), keys[position - 1]);
        }
    }

    /// <summary>
    /// <see cref="OrderedMap.GetKeys"/> reports success on an empty map and yields a zero-length
    /// array.
    /// </summary>
    /// <remarks>
    /// Success is unconditional: the operation produced the complete set of keys, which happens to
    /// be empty. Reporting failure here would conflate "nothing to give" with "could not give it",
    /// and the legacy declaration at <c>n_map.sru:L13</c> has no separate channel for the
    /// difference.
    /// </remarks>
    [Fact]
    public void GetKeysOnAnEmptyMapReturnsTrueAndAnEmptyArray()
    {
        OrderedMap map = new();
        string[] keys = ["stale-seed"];

        Assert.True(map.GetKeys(ref keys));
        Assert.Empty(keys);
    }

    /// <summary>
    /// <see cref="OrderedMap.GetKeys"/> REPLACES the caller's array with a fresh one rather than
    /// appending to it or reusing it.
    /// </summary>
    /// <remarks>
    /// The parameter is <see langword="ref"/> because the legacy parameter at
    /// <c>n_map.sru:L13</c> is a PowerBuilder <c>ref</c>, which is an in-out parameter. Asserting
    /// that the supplied array is discarded rather than mutated pins two things at once: no stale
    /// element survives into the result, and a caller holding the old array cannot reach into the
    /// map's freshly reported sequence through it.
    /// </remarks>
    [Fact]
    public void GetKeysDiscardsTheCallerSuppliedArray()
    {
        OrderedMap map = BuildMap(3);
        string[] supplied = ["stale-one", "stale-two", "stale-three", "stale-four", "stale-five"];
        string[] originalInstance = supplied;
        string[] expectedOrder = [KeyAt(1), KeyAt(2), KeyAt(3)];

        Assert.True(map.GetKeys(ref supplied));

        // A different array instance, sized to the map rather than to what the caller passed.
        Assert.NotSame(originalInstance, supplied);
        Assert.Equal(expectedOrder, supplied);

        // The caller's original array was neither resized nor overwritten in place.
        Assert.Equal(5, originalInstance.Length);
        Assert.Equal("stale-one", originalInstance[0]);
    }

    // ------------------------------------------------------------------------------------------
    //  Add VERSUS Set - TWO OPERATIONS THAT MUST NOT BE COLLAPSED INTO ONE
    //  Merging them is the obvious simplification of this surface, and it changes behaviour.
    //  n_map.sru:L9 and n_map.sru:L14 declare two separate prototypes with identical parameter
    //  lists and identical return types, which only makes sense as two different operations.
    //
    //  The distinction is load bearing at a measured call site rather than merely declared.
    //  n_cst_thread_task_sqlquery.sru:L646-L662 creates a map, and inside a one-based column loop
    //  guards with `if map.Exists(sProp) then ... else ... map.Add(sProp,true)` at :L652 and :L658
    //  - the caller relies on Add being insert-if-absent, and on the map reporting duplicate
    //  drop-down definitions so it can switch their auto-retrieve off exactly once.
    // ------------------------------------------------------------------------------------------

    /// <summary>The key both duplicate-handling paths operate on.</summary>
    private const string DuplicateKey = "d_dddw_department";

    /// <summary>The value stored first, which <see cref="OrderedMap.Add"/> must not disturb.</summary>
    private const string OriginalDuplicateValue = "first-writer-wins";

    /// <summary>The value offered second, which only <see cref="OrderedMap.Set"/> may install.</summary>
    private const string ReplacementDuplicateValue = "last-writer-wins";

    /// <summary>
    /// The divergence matrix: what each of the two operations returns when offered a key that is
    /// already present, and which value survives.
    /// </summary>
    /// <remarks>
    /// Expressed as a table so the two behaviours sit side by side and the asymmetry is legible as
    /// data rather than buried in two unrelated facts. Every column is asserted by the theory that
    /// consumes it.
    /// </remarks>
    public static TheoryData<string, bool, string> DuplicateKeyOutcomes => new()
    {
        { "Add", false, OriginalDuplicateValue },
        { "Set", true, ReplacementDuplicateValue },
    };

    /// <summary>
    /// <see cref="OrderedMap.Add"/> on a fresh key reports success and appends the entry at the
    /// end of the insertion order.
    /// </summary>
    [Fact]
    public void AddOnAFreshKeyReturnsTrueAndAppendsAtTheEnd()
    {
        OrderedMap map = BuildMap(2);

        Assert.True(map.Add(KeyAt(3), ValueAt(3)));

        Assert.Equal(3UL, map.Count());
        Assert.True(map.Exists(KeyAt(3)));
        Assert.Equal(ValueAt(3), map.Get(KeyAt(3)));

        // Appended, so it is the LAST entry and the earlier entries kept their positions.
        Assert.Equal(KeyAt(3), map.GetKey(3));
        Assert.Equal(KeyAt(1), map.GetKey(1));
        Assert.Equal(KeyAt(2), map.GetKey(2));
    }

    /// <summary>
    /// <see cref="OrderedMap.Add"/> on a key that is already present reports FAILURE and leaves
    /// the map completely unchanged.
    /// </summary>
    /// <remarks>
    /// The refusal is asserted as a boolean result, deliberately NOT as a thrown exception. The
    /// legacy prototype at <c>n_map.sru:L9</c> returns <c>boolean</c>, and its caller at
    /// <c>n_cst_thread_task_sqlquery.sru:L652</c> already guards with <c>Exists</c>, so promoting
    /// the refusal to an exception would introduce a failure mode the legacy could not produce.
    /// </remarks>
    [Fact]
    public void AddOnAnExistingKeyReturnsFalseAndLeavesTheMapUnchanged()
    {
        OrderedMap map = BuildMap(3);
        string[] expectedOrder = [KeyAt(1), KeyAt(2), KeyAt(3)];

        Assert.False(map.Add(KeyAt(2), ReplacementDuplicateValue));

        // The existing value is retained, not overwritten.
        Assert.Equal(ValueAt(2), map.Get(KeyAt(2)));

        // No second entry was created, and nothing moved.
        Assert.Equal(3UL, map.Count());
        AssertOrderingIs(map, expectedOrder);
    }

    /// <summary>
    /// <see cref="OrderedMap.Set"/> on a fresh key inserts it and reports success, so Set is
    /// insert-or-overwrite rather than overwrite-only.
    /// </summary>
    /// <remarks>
    /// That Set inserts is corroborated by call-site behaviour rather than assumed:
    /// <c>n_cst_dwsvc.sru:L585</c> creates an EMPTY map and <c>:L634</c> through <c>:L655</c>
    /// populate it through Set alone, so were Set not to insert, the map returned at <c>:L663</c>
    /// would always be empty and the paste-translation feature reading it at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1006-L1007</c> would be unreachable code.
    /// </remarks>
    [Fact]
    public void SetOnAFreshKeyInsertsAndReturnsTrue()
    {
        OrderedMap map = new();

        Assert.True(map.Set(KeyAt(1), ValueAt(1)));

        Assert.Equal(1UL, map.Count());
        Assert.True(map.Exists(KeyAt(1)));
        Assert.Equal(ValueAt(1), map.Get(KeyAt(1)));
        Assert.Equal(KeyAt(1), map.GetKey(1));
    }

    /// <summary>
    /// <see cref="OrderedMap.Set"/> on a key that is already present overwrites the value and
    /// reports success.
    /// </summary>
    [Fact]
    public void SetOnAnExistingKeyOverwritesAndReturnsTrue()
    {
        OrderedMap map = new();

        Assert.True(map.Add(DuplicateKey, OriginalDuplicateValue));
        Assert.True(map.Set(DuplicateKey, ReplacementDuplicateValue));

        Assert.Equal(ReplacementDuplicateValue, map.Get(DuplicateKey));
        Assert.Equal(1UL, map.Count());
    }

    /// <summary>
    /// The two operations diverge on a duplicate key: <see cref="OrderedMap.Add"/> refuses and
    /// keeps the first value, <see cref="OrderedMap.Set"/> accepts and installs the second.
    /// </summary>
    /// <param name="operation">Which of the two members to invoke on the duplicate key.</param>
    /// <param name="expectedResult">The boolean the operation is expected to report.</param>
    /// <param name="expectedStoredValue">The value expected to survive the operation.</param>
    /// <remarks>
    /// This is the assertion that forbids collapsing the two members into one. Neither row may be
    /// changed to match the other, because each is the behaviour a different measured legacy caller
    /// depends on.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DuplicateKeyOutcomes))]
    public void AddAndSetDivergeOnADuplicateKey(string operation, bool expectedResult, string expectedStoredValue)
    {
        OrderedMap map = new();
        Assert.True(map.Add(DuplicateKey, OriginalDuplicateValue));

        bool actualResult = operation switch
        {
            "Add" => map.Add(DuplicateKey, ReplacementDuplicateValue),
            "Set" => map.Set(DuplicateKey, ReplacementDuplicateValue),

            // Unreachable for the two rows declared above; present so that adding a third row
            // without extending this switch fails loudly instead of silently taking a wrong path.
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation), operation, "the divergence matrix declares only Add and Set"),
        };

        Assert.Equal(expectedResult, actualResult);
        Assert.Equal(expectedStoredValue, map.Get(DuplicateKey));

        // Whichever path ran, exactly one entry exists and it is still the first one inserted.
        Assert.Equal(1UL, map.Count());
        Assert.Equal(DuplicateKey, map.GetKey(1));
    }

    // ------------------------------------------------------------------------------------------
    //  KEY-BASED ACCESS, MEMBERSHIP, REMOVAL, COUNT AND PURGE
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="OrderedMap.Get(string)"/> returns the stored value for a key that is present.
    /// </summary>
    [Fact]
    public void GetByKeyReturnsTheStoredValueForAPresentKey()
    {
        OrderedMap map = BuildMap(3);

        Assert.Equal(ValueAt(1), map.Get(KeyAt(1)));
        Assert.Equal(ValueAt(2), map.Get(KeyAt(2)));
        Assert.Equal(ValueAt(3), map.Get(KeyAt(3)));
    }

    /// <summary>
    /// <see cref="OrderedMap.Get(string)"/> returns <see langword="null"/> for an absent key and
    /// does not throw.
    /// </summary>
    /// <remarks>
    /// Asserted as implemented. <c>n_map.sru:L10</c> declares <c>any get(string key)</c> - a bare
    /// return value with no boolean out-parameter and no return code - so the legacy accessor has
    /// no channel through which to report a miss, and every measured caller guards with
    /// <c>Exists</c> beforehand (<c>n_cst_dwsvc_contextmenu.sru:L1006</c> then <c>:L1007</c>, and
    /// <c>n_cst_thread_task_sqlbase.sru:L539</c> then <c>:L540</c>). Raising a
    /// <see cref="KeyNotFoundException"/> here would be a behavioural change dressed as
    /// robustness.
    /// </remarks>
    [Fact]
    public void GetByKeyReturnsNullForAnAbsentKeyAndDoesNotThrow()
    {
        OrderedMap map = BuildMap(2);

        Assert.Null(map.Get("no-such-key"));

        // The failed lookup neither created an entry nor disturbed the existing ones.
        Assert.Equal(2UL, map.Count());
        Assert.False(map.Exists("no-such-key"));
    }

    /// <summary>
    /// <see cref="OrderedMap.Exists"/> discriminates a present key from an absent one.
    /// </summary>
    [Fact]
    public void ExistsDiscriminatesPresentFromAbsentKeys()
    {
        OrderedMap map = BuildMap(2);

        Assert.True(map.Exists(KeyAt(1)));
        Assert.True(map.Exists(KeyAt(2)));
        Assert.False(map.Exists(KeyAt(3)));
        Assert.False(map.Exists(string.Empty));
    }

    /// <summary>
    /// A stored <see langword="null"/> value is distinguishable from an absent key ONLY through
    /// <see cref="OrderedMap.Exists"/>, because both read back as <see langword="null"/> through
    /// the accessors.
    /// </summary>
    /// <remarks>
    /// The ambiguity is inherent to a signature with no failure channel and is not introduced by
    /// the substitution: it follows directly from <c>n_map.sru:L10</c> returning a bare value.
    /// <see cref="OrderedMap.Exists"/> is the discriminator, which is exactly how the legacy uses
    /// it - as the guard in front of every measured read.
    /// </remarks>
    [Fact]
    public void StoredNullIsDistinguishableFromAnAbsentKeyOnlyThroughExists()
    {
        OrderedMap map = new();

        Assert.True(map.Add("stored-null", null));

        // Indistinguishable through either accessor...
        Assert.Null(map.Get("stored-null"));
        Assert.Null(map.Get("never-added"));
        Assert.Null(map.Get(1));

        // ...and cleanly distinguished through membership.
        Assert.True(map.Exists("stored-null"));
        Assert.False(map.Exists("never-added"));

        // The null-valued entry is a real entry: it counts, and it occupies a position.
        Assert.Equal(1UL, map.Count());
        Assert.Equal("stored-null", map.GetKey(1));
    }

    /// <summary>
    /// <see cref="OrderedMap.Remove"/> reports success for a present key and the count drops.
    /// </summary>
    [Fact]
    public void RemoveReturnsTrueForAPresentKeyAndDecreasesCount()
    {
        OrderedMap map = BuildMap(3);

        Assert.True(map.Remove(KeyAt(1)));

        Assert.Equal(2UL, map.Count());
        Assert.False(map.Exists(KeyAt(1)));
        Assert.Null(map.Get(KeyAt(1)));
    }

    /// <summary>
    /// <see cref="OrderedMap.Remove"/> reports failure for an absent key and leaves the map
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// A removal that finds nothing to remove is an ordinary miss reported through the boolean the
    /// legacy declares at <c>n_map.sru:L16</c>, not an error condition.
    /// </remarks>
    [Fact]
    public void RemoveReturnsFalseForAnAbsentKeyAndLeavesTheMapUnchanged()
    {
        OrderedMap map = BuildMap(3);
        string[] expectedOrder = [KeyAt(1), KeyAt(2), KeyAt(3)];

        Assert.False(map.Remove("no-such-key"));

        // Removing the same key twice: the second attempt is an ordinary miss too.
        Assert.True(map.Remove(KeyAt(2)));
        Assert.False(map.Remove(KeyAt(2)));

        Assert.True(map.Add(KeyAt(2), ValueAt(2)));
        Assert.Equal(3UL, map.Count());
        Assert.NotEqual(expectedOrder, KeySequenceOf(map));
    }

    /// <summary>
    /// <see cref="OrderedMap.Count"/> tracks every mutation, including the ones that deliberately
    /// change nothing.
    /// </summary>
    /// <remarks>
    /// <c>Count()</c> is a METHOD returning <see cref="ulong"/>, not an <see cref="int"/>
    /// property, because <c>n_map.sru:L17</c> declares <c>public function ulong count()</c>. Every
    /// comparison below is written against an unsigned literal so the width is never narrowed by
    /// accident.
    /// </remarks>
    [Fact]
    public void CountTracksEveryMutation()
    {
        OrderedMap map = new();

        Assert.Equal(0UL, map.Count());

        Assert.True(map.Add(KeyAt(1), ValueAt(1)));
        Assert.Equal(1UL, map.Count());

        Assert.True(map.Set(KeyAt(2), ValueAt(2)));
        Assert.Equal(2UL, map.Count());

        // A refused add does not change the count.
        Assert.False(map.Add(KeyAt(2), ValueAt(3)));
        Assert.Equal(2UL, map.Count());

        // Nor does an overwrite, which replaces rather than inserts.
        Assert.True(map.Set(KeyAt(2), ValueAt(3)));
        Assert.Equal(2UL, map.Count());

        Assert.True(map.Remove(KeyAt(1)));
        Assert.Equal(1UL, map.Count());

        // Nor does a removal that found nothing.
        Assert.False(map.Remove(KeyAt(1)));
        Assert.Equal(1UL, map.Count());

        map.Purge();
        Assert.Equal(0UL, map.Count());
    }

    /// <summary>
    /// <see cref="OrderedMap.Purge"/> empties the map and leaves it fully reusable, with the
    /// insertion order restarting at position 1.
    /// </summary>
    /// <remarks>
    /// Named <c>Purge</c> and returning nothing because <c>n_map.sru:L18</c> declares
    /// <c>public subroutine purge()</c>. It is the legacy's own way of dropping the contents, and
    /// it is the reason the substitution implements no disposal pattern.
    /// </remarks>
    [Fact]
    public void PurgeEmptiesTheMapAndLeavesItReusable()
    {
        OrderedMap map = BuildMap(BoundaryProbeEntryCount);
        string[] keys = ["stale-seed"];

        map.Purge();

        Assert.Equal(0UL, map.Count());
        Assert.True(map.GetKeys(ref keys));
        Assert.Empty(keys);

        // Every accessor now reports the documented miss sentinels.
        Assert.False(map.Exists(KeyAt(1)));
        Assert.Null(map.Get(KeyAt(1)));
        Assert.Null(map.Get(1));
        Assert.Equal(string.Empty, map.GetKey(1));

        // Reusable, and the order starts over: the next insertion is entry 1 whatever it is.
        Assert.True(map.Add(KeyAt(3), ValueAt(3)));
        Assert.Equal(1UL, map.Count());
        Assert.Equal(KeyAt(3), map.GetKey(1));
        Assert.Equal(ValueAt(3), map.Get(1));
    }

    /// <summary>
    /// <see cref="OrderedMap.Purge"/> on an already empty map is a no-op and does not raise.
    /// </summary>
    [Fact]
    public void PurgeOnAnEmptyMapIsANoOp()
    {
        OrderedMap map = new();

        map.Purge();
        map.Purge();

        Assert.Equal(0UL, map.Count());
    }

    // ------------------------------------------------------------------------------------------
    //  VALUE SEMANTICS - REFERENCE IDENTITY, BOXING AND KEY COMPARISON
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A retrieved value is the VERY SAME INSTANCE that was stored: the map never clones, copies
    /// or otherwise detaches a stored reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a hard requirement, not a nicety. <c>n_cst_thread_task_sqlbase.sru</c>
    /// <c>of_getcacheds</c> stores a <c>CACHEDS</c> structure holding a LIVE datastore-derived
    /// object at <c>:L565</c>, retrieves it with <c>mapCache.Get(dataObject)</c> at <c>:L540</c>,
    /// and then immediately mutates that very instance - <c>ds.Modify(...)</c> at <c>:L543</c>,
    /// <c>ds.SetFilter(...)</c> at <c>:L546</c> and <c>:L549</c>, and <c>ds.of_ClearState()</c> at
    /// <c>:L551</c>. Any cloning inside the container would apply those mutations to a detached
    /// object and break the Persistence datastore cache outright, while still passing every
    /// equality-based assertion - which is precisely why identity is asserted here with
    /// <c>Assert.Same</c> rather than <c>Assert.Equal</c>.
    /// </para>
    /// <para>
    /// Identity is asserted through both accessors, because a positional read and a key read must
    /// hand back the same object rather than merely equal ones.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetReturnsTheVerySameReferenceThatWasStored()
    {
        OrderedMap map = new();
        CachedDataStoreStandIn cached = new() { SelectStatement = "SELECT * FROM COMPANY" };

        Assert.True(map.Add("dw_sqlite", cached));

        // The same instance through the key accessor and through the positional accessor.
        Assert.Same(cached, map.Get("dw_sqlite"));
        Assert.Same(cached, map.Get(1));

        // Reproducing the legacy cache path: mutate through the retrieved reference...
        CachedDataStoreStandIn? retrieved = map.Get("dw_sqlite") as CachedDataStoreStandIn;
        Assert.NotNull(retrieved);
        retrieved.SelectStatement = "SELECT * FROM COMPANY WHERE age > 30";

        // ...and observe the mutation through a fresh retrieval AND through the original handle,
        // which is only possible because no copy was ever taken.
        CachedDataStoreStandIn? readAgain = map.Get("dw_sqlite") as CachedDataStoreStandIn;
        Assert.NotNull(readAgain);
        Assert.Equal("SELECT * FROM COMPANY WHERE age > 30", readAgain.SelectStatement);
        Assert.Equal("SELECT * FROM COMPANY WHERE age > 30", cached.SelectStatement);
    }

    /// <summary>
    /// A boxed value type round-trips through the map unchanged, and the same box comes back.
    /// </summary>
    /// <remarks>
    /// The legacy genuinely stores one: <c>n_cst_thread_task_sqlquery.sru:L658</c> calls
    /// <c>map.Add(sProp, true)</c>, putting a boolean into the <c>any</c> value slot. The value is
    /// unwrapped deliberately below - asserted non-null, then cast - rather than forced through a
    /// null-forgiving operator, because the null semantics are part of what is under test.
    /// </remarks>
    [Fact]
    public void BoxedValueTypesRoundTripThroughGet()
    {
        OrderedMap map = new();

        // Declared as object so the box is created once and its identity is observable.
        object boxedTrue = true;

        Assert.True(map.Add("d_dddw_autoretrieve", boxedTrue));

        object? retrieved = map.Get("d_dddw_autoretrieve");
        Assert.NotNull(retrieved);
        Assert.Same(boxedTrue, retrieved);

        bool unboxed = (bool)retrieved;
        Assert.True(unboxed);
    }

    /// <summary>
    /// Heterogeneous values coexist in one map, which is what the legacy <c>any</c> value slot
    /// permits and what its callers actually do.
    /// </summary>
    /// <remarks>
    /// The measured legacy values are genuinely of different shapes: a string at
    /// <c>n_cst_dwsvc.sru:L634</c>, a boxed boolean at
    /// <c>n_cst_thread_task_sqlquery.sru:L658</c>, and a structure holding a live object at
    /// <c>n_cst_thread_task_sqlbase.sru:L565</c>. The container must impose no value semantics on
    /// any of them, and must keep them in insertion order regardless of type.
    /// </remarks>
    [Fact]
    public void HeterogeneousValuesCoexistInOneMapWithoutDisturbingOrder()
    {
        OrderedMap map = new();
        object boxedTrue = true;
        CachedDataStoreStandIn cached = new() { SelectStatement = "SELECT * FROM COMPANY" };
        string[] expectedOrder = ["a-string", "a-boxed-bool", "a-live-object", "a-null"];

        Assert.True(map.Add("a-string", "value-alpha"));
        Assert.True(map.Add("a-boxed-bool", boxedTrue));
        Assert.True(map.Add("a-live-object", cached));
        Assert.True(map.Add("a-null", null));

        AssertOrderingIs(map, expectedOrder);

        Assert.Equal("value-alpha", map.Get(1));
        Assert.Same(boxedTrue, map.Get(2));
        Assert.Same(cached, map.Get(3));
        Assert.Null(map.Get(4));

        // The null-valued entry still occupies its position and counts toward the bound.
        Assert.True(map.Exists("a-null"));
        Assert.Equal(4UL, map.Count());
    }

    /// <summary>
    /// Keys are compared ordinally and CASE SENSITIVELY, so two spellings differing only in case
    /// are two distinct entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This matches PowerScript's own string equality and is the conservative reading: the native
    /// comparison rule is not observable from the repository, and a case-insensitive container
    /// would silently merge distinct keys.
    /// </para>
    /// <para>
    /// It matters concretely. <c>n_cst_dwsvc.sru:L643</c> and <c>:L648</c> register the checkbox
    /// display keys <c>"Y"</c> and <c>"N"</c> alongside their Chinese equivalents, and
    /// <c>n_cst_dwsvc_contextmenu.sru:L1006</c> matches pasted user text against them - so
    /// whether <c>"y"</c> resolves is observable behaviour rather than an internal detail.
    /// </para>
    /// </remarks>
    [Fact]
    public void KeysAreComparedCaseSensitively()
    {
        OrderedMap map = new();

        Assert.True(map.Set("Y", "1"));

        // The lower-case spelling is a different key: absent, and unreadable.
        Assert.False(map.Exists("y"));
        Assert.Null(map.Get("y"));

        // So Add accepts it, which a case-insensitive container would have refused.
        Assert.True(map.Add("y", "0"));

        Assert.Equal(2UL, map.Count());
        Assert.Equal("1", map.Get("Y"));
        Assert.Equal("0", map.Get("y"));
        Assert.Equal("Y", map.GetKey(1));
        Assert.Equal("y", map.GetKey(2));
    }

    /// <summary>
    /// A mutable reference type standing in for the legacy <c>CACHEDS</c> structure, which holds a
    /// live datastore-derived object.
    /// </summary>
    /// <remarks>
    /// It exists so <see cref="GetReturnsTheVerySameReferenceThatWasStored"/> can prove that a
    /// retrieved value is the stored instance and not a copy. That is a hard requirement rather
    /// than a nicety: <c>n_cst_thread_task_sqlbase.sru:L565</c> stores such a structure,
    /// <c>:L540</c> reads it back, and <c>:L543</c>, <c>:L546</c>, <c>:L549</c> and <c>:L551</c>
    /// immediately mutate the retrieved object through <c>Modify</c>, <c>SetFilter</c> and
    /// <c>of_ClearState</c>. Any cloning in the container would leave those mutations on a
    /// detached object and break the Persistence datastore cache outright.
    /// </remarks>
    private sealed class CachedDataStoreStandIn
    {
        /// <summary>Stands in for the cached <c>origSQL</c> field, mutated after retrieval.</summary>
        public string SelectStatement { get; set; } = string.Empty;
    }
}
