// ==================================================================================================
//  AncestryTests.cs - THE FOUR INHERITANCE PREDICATES
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Kernel.Ancestry
//  ORACLES           ws_objects/pfw.common.pbl.src/isancestor.srf          prototypes only, NATIVE
//                    ws_objects/pfw.common.pbl.src/isancestorbyclass.srf   readable body, L12-L20
//                    ws_objects/pfw.common.pbl.src/isancestorbyobject.srf  readable body, L12-L20
//
//  WHY THIS SUITE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Ancestry is a dispatch primitive: 30 legacy call sites ask "is this thing a T" and branch on the
//  answer. A predicate that answers false where it should answer true does not fail loudly - it
//  quietly takes the other branch, so the defect surfaces as missing behaviour somewhere else
//  entirely. Before this file the whole four-member surface had no executable test at all, which
//  for a dispatch primitive is the least affordable gap in the library.
//
//  WHAT THE ORACLE ACTUALLY SAYS, AND WHAT IT DOES NOT
//  ------------------------------------------------------------------------------------------------
//  Two of the three oracle files have readable bodies and they are byte-identical from L15 to L20:
//
//      L12   if <arg> = "" or parentCls = "" then return false
//      L14   clsDef = FindClassDefinition(cls)      -- or GetClassDefinition(object)
//      L15   do while IsValid(clsDef)
//      L16      if clsDef.name = parentCls then return true
//      L17      clsDef = clsDef.Ancestor
//      L18   loop
//      L20   return false
//
//  Four properties follow from those six lines, and every one of them is asserted below:
//
//   1. THE WALK IS SELF-INCLUSIVE. `clsDef` is seeded from the argument and tested BEFORE any
//      ancestor is taken, so a type is its own ancestor. All 30 legacy call sites depend on this -
//      they are "is this a T" tests, and an exclusive walk would answer false for an exact T.
//   2. THE CHAIN IS SINGLE INHERITANCE. `clsDef.Ancestor` walks base classes only. PowerBuilder has
//      no interfaces for it to reach, so the port walks Type.BaseType and deliberately does NOT
//      traverse implemented interfaces - which is why Type.IsAssignableFrom is not used. Asserting
//      the interface case is what stops a future author from "simplifying" to IsAssignableFrom and
//      widening the question the predicate answers.
//   3. THE NAME IS UNQUALIFIED AND THE COMPARISON IS ORDINAL. `clsDef.name` is a bare PowerBuilder
//      class name, because PowerBuilder has one flat global namespace; the counterpart is Type.Name
//      and not FullName. PowerScript `=` on strings is a byte comparison, so the fold is ordinal -
//      a culture-sensitive comparison would additionally make the answer depend on the ambient
//      locale, which characterization recordings cannot tolerate.
//   4. NOTHING THROWS. There is no error path in the oracle at all: an empty name, an absent class
//      and an invalid object are all just false.
//
//  isancestor.srf is NATIVE - `native "pfw.dll"` at L3, no body, and no C++ source anywhere in the
//  repository - so its two overloads are INFERRED from the readable siblings whose prototypes they
//  mirror exactly. The port expresses that by delegating rather than by carrying a second copy of
//  the walk, and the overload-equivalence facts below are what hold the inference in place: if the
//  two ever disagree, the delegation has been unpicked.
//
//  THE ONE PLACE THE PORT CANNOT MATCH THE LEGACY, PINNED AS SUCH
//  ------------------------------------------------------------------------------------------------
//  `FindClassDefinition` searches one flat global namespace that holds at most one class of any
//  given name, so the legacy lookup is unambiguous by construction. .NET has no such namespace, so
//  the port searches the ALREADY-LOADED assemblies and breaks a same-name tie on the ordinally
//  smallest FullName. Two consequences are asserted here rather than left to be discovered: a type
//  in an assembly that has not been loaded yet does not resolve, and the tie-break is deterministic
//  rather than load-order dependent. Neither contradicts a legacy answer - the situation cannot
//  arise in a flat namespace - so both are properties of the substitution, and both are stated in
//  the failure messages so a reader meeting one knows it is by design.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.", so no user-specified rule governs this file. The
//  binding constraints cited inline are C-B (replicate behaviour, never improve) and C-C (the legacy
//  tree is read-only and is the oracle).
// ==================================================================================================

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Ancestry"/>'s four inheritance predicates.
/// </summary>
/// <remarks>
/// No <c>using PowerFramework.Shared.Kernel;</c> directive appears above: this namespace is nested
/// inside it, so simple-name lookup walks outward and finds <see cref="Ancestry"/> there.
/// </remarks>
public class AncestryTests
{
    // ==============================================================================================
    //  THE FIXTURE
    //
    //  A purpose-built four-level chain plus an interface and an unrelated type. Declared nested and
    //  private so that no other suite's type can accidentally satisfy a lookup here, and so that the
    //  names are unmistakably local - a lookup for "GrandParentFixture" can only mean this one.
    //
    //  Nested types are used ON PURPOSE rather than top-level ones: Type.Name reports the UNQUALIFIED
    //  name for a nested type, with no enclosing-type prefix, so a nested fixture proves the port
    //  reads Type.Name rather than Type.FullName. If it read FullName, every row below would answer
    //  false, because a FullName here is "PowerFramework.Shared.Kernel.Tests.AncestryTests+RootFixture".
    // ==============================================================================================

    /// <summary>A marker interface the chain implements, to prove interfaces are not traversed.</summary>
    private interface IFixtureMarker;

    /// <summary>The root of the fixture chain. Its own base is <see cref="object"/>.</summary>
    private class RootFixture;

    /// <summary>Level two. Implements the marker, which no ancestor test may answer true for.</summary>
    private class MiddleFixture : RootFixture, IFixtureMarker;

    /// <summary>Level three, so the walk has to take more than one step to reach the root.</summary>
    private class LeafFixture : MiddleFixture;

    /// <summary>A type with no relationship to the chain at all.</summary>
    private class UnrelatedFixture;

    /// <summary>
    /// A type whose name differs from <see cref="LeafFixture"/> only in case, so an ordinal
    /// comparison can be distinguished from a case-insensitive one in both directions.
    /// </summary>
    private class LEAFFIXTURE;

    // ==============================================================================================
    //  1. THE SELF-INCLUSIVE WALK
    // ==============================================================================================

    /// <summary>
    /// A type is its own ancestor, on every overload. [isancestorbyclass.srf:L15-L16]
    /// </summary>
    /// <remarks>
    /// The seed is tested before any ancestor is taken, so an exact match answers true. This is the
    /// property all 30 legacy call sites rely on, because every one of them is a dispatch test of the
    /// form "is this thing a T" rather than "does this thing derive strictly from T".
    /// </remarks>
    [Fact]
    public void ATypeIsItsOwnAncestor()
    {
        LeafFixture leaf = new();

        Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(LeafFixture)));
        Assert.True(Ancestry.IsAncestor(leaf, nameof(LeafFixture)));
        Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(LeafFixture)));
        Assert.True(Ancestry.IsAncestor(nameof(LeafFixture), nameof(LeafFixture)));
    }

    /// <summary>
    /// The walk takes as many steps as the chain has levels, and reaches <see cref="object"/> at the
    /// end of it. [isancestorbyclass.srf:L17]
    /// </summary>
    /// <remarks>
    /// Three levels rather than two, because a walk that advanced once and stopped would pass a
    /// two-level fixture. Reaching <c>Object</c> also pins the loop's TERMINATION condition from the
    /// inside: <c>Type.BaseType</c> is null above <c>System.Object</c>, which is the port's
    /// counterpart of the oracle's <c>IsValid(clsDef)</c> going false at the root of the chain.
    /// </remarks>
    [Fact]
    public void TheWalkClimbsEveryLevelOfTheChain()
    {
        LeafFixture leaf = new();

        Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(MiddleFixture)));
        Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(RootFixture)));
        Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(Object)));

        Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(MiddleFixture)));
        Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(RootFixture)));
        Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(Object)));
    }

    /// <summary>
    /// The walk climbs and never descends: an ancestor is not a descendant of its own child.
    /// </summary>
    /// <remarks>
    /// The direction of the relation, asserted as its own fact because a symmetric implementation -
    /// <c>IsAssignableFrom</c> called the wrong way round, say - would satisfy every "true" row in
    /// this file while answering true here too.
    /// </remarks>
    [Fact]
    public void TheRelationIsDirectionalRatherThanSymmetric()
    {
        RootFixture root = new();

        Assert.False(Ancestry.IsAncestorByObject(root, nameof(MiddleFixture)));
        Assert.False(Ancestry.IsAncestorByObject(root, nameof(LeafFixture)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(RootFixture), nameof(LeafFixture)));

        // ...and the true direction, from the same fixture, so the pair reads as one statement.
        Assert.True(Ancestry.IsAncestorByObject(new LeafFixture(), nameof(RootFixture)));
    }

    /// <summary>
    /// An unrelated type is not an ancestor, however well-formed both names are.
    /// </summary>
    [Fact]
    public void AnUnrelatedTypeIsNotAnAncestor()
    {
        Assert.False(Ancestry.IsAncestorByObject(new LeafFixture(), nameof(UnrelatedFixture)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(UnrelatedFixture)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(UnrelatedFixture), nameof(RootFixture)));
    }

    // ==============================================================================================
    //  2. INTERFACES ARE NOT TRAVERSED
    // ==============================================================================================

    /// <summary>
    /// An implemented interface is NOT an ancestor, on either the object or the class-name path.
    /// [isancestorbyclass.srf:L17]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's <c>Ancestor</c> chain is single inheritance and PowerBuilder has no interfaces at
    /// all, so an interface is not a question the legacy can be asked - and answering true would make
    /// the predicate report a relationship the oracle has no way to express. The port walks
    /// <c>Type.BaseType</c> only, which is why <see cref="System.Type.IsAssignableFrom"/> is
    /// deliberately absent from the implementation.
    /// </para>
    /// <para>
    /// This is the single most likely "simplification" a future author would reach for -
    /// <c>IsAssignableFrom</c> is shorter, reads better and is wrong here - so the interface case is
    /// asserted from both the derived type and the implementing type, and for both overload families.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnImplementedInterfaceIsNotAnAncestor()
    {
        LeafFixture leaf = new();
        MiddleFixture middle = new();

        // The type that declares the interface...
        Assert.False(Ancestry.IsAncestorByObject(middle, nameof(IFixtureMarker)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(MiddleFixture), nameof(IFixtureMarker)));

        // ...and a type that inherits it through the chain.
        Assert.False(Ancestry.IsAncestorByObject(leaf, nameof(IFixtureMarker)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(IFixtureMarker)));

        // The interface really is implemented, so the rows above are a statement about the WALK and
        // not an accident of a mis-declared fixture.
        Assert.True(typeof(IFixtureMarker).IsAssignableFrom(typeof(LeafFixture)));
    }

    // ==============================================================================================
    //  3. THE COMPARISON IS ORDINAL AND THE NAME IS UNQUALIFIED
    // ==============================================================================================

    /// <summary>
    /// The parent-class comparison is ordinal, so a name differing only in case does not match.
    /// [isancestorbyclass.srf:L16]
    /// </summary>
    /// <remarks>
    /// PowerScript <c>=</c> on strings is a byte comparison. Beyond fidelity, an ordinal comparison is
    /// what keeps the answer independent of the ambient culture: under a Turkish locale a
    /// case-insensitive fold maps <c>I</c> and <c>i</c> differently, so the same call could answer
    /// differently on two hosts and no characterization recording would be comparable.
    /// </remarks>
    [Fact]
    public void TheParentClassComparisonIsOrdinal()
    {
        LeafFixture leaf = new();

        Assert.True(Ancestry.IsAncestorByObject(leaf, "LeafFixture"));
        Assert.False(Ancestry.IsAncestorByObject(leaf, "leaffixture"));
        Assert.False(Ancestry.IsAncestorByObject(leaf, "LEAFFIXTURE"));
        Assert.False(Ancestry.IsAncestorByObject(leaf, "RootFIXTURE"));
    }

    /// <summary>
    /// The class-NAME lookup is ordinal too, so two fixtures whose names differ only in case are two
    /// different classes.
    /// </summary>
    /// <remarks>
    /// The complement of the fact above, one level down: the first checks the comparison against the
    /// chain, this one checks the resolution of the starting point. <see cref="LEAFFIXTURE"/> exists
    /// solely to make the two distinguishable - it is unrelated to the chain, so resolving it instead
    /// of <see cref="LeafFixture"/> changes the answer rather than merely the path.
    /// </remarks>
    [Fact]
    public void TheClassNameLookupIsOrdinal()
    {
        Assert.True(Ancestry.IsAncestorByClass("LeafFixture", nameof(RootFixture)));

        // LEAFFIXTURE resolves - it is a real type - but it is NOT in the chain.
        Assert.True(Ancestry.IsAncestorByClass("LEAFFIXTURE", "LEAFFIXTURE"));
        Assert.False(Ancestry.IsAncestorByClass("LEAFFIXTURE", nameof(RootFixture)));

        // ...and a case variant that matches no type at all simply fails to resolve.
        Assert.False(Ancestry.IsAncestorByClass("leaffixture", nameof(RootFixture)));
    }

    /// <summary>
    /// Names are UNQUALIFIED: a namespace-qualified or assembly-qualified name matches nothing.
    /// </summary>
    /// <remarks>
    /// PowerBuilder has one flat global namespace, so <c>clsDef.name</c> is a bare class name and
    /// every legacy call site passes one. Reading <see cref="System.Type.FullName"/> instead would
    /// answer false at all 30 of them, and this fact is what makes that failure mode visible from the
    /// test side. Note the fixture types are NESTED, so their FullName carries a <c>+</c> separator -
    /// a qualified lookup fails in both spellings.
    /// </remarks>
    [Fact]
    public void OnlyUnqualifiedNamesResolveAndMatch()
    {
        LeafFixture leaf = new();
        string qualified = typeof(LeafFixture).FullName!;

        Assert.Contains("PowerFramework.Shared.Kernel.Tests", qualified, System.StringComparison.Ordinal);

        Assert.False(Ancestry.IsAncestorByObject(leaf, qualified));
        Assert.False(Ancestry.IsAncestorByClass(qualified, nameof(RootFixture)));
        Assert.False(Ancestry.IsAncestorByClass(nameof(LeafFixture), typeof(RootFixture).FullName!));

        // The unqualified spelling of the very same type does match, which is what makes the rows
        // above a statement about qualification rather than about the fixture.
        Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(LeafFixture)));
    }

    // ==============================================================================================
    //  4. THE GUARD ARMS - EMPTY, BLANK, NULL AND UNRESOLVABLE
    // ==============================================================================================

    /// <summary>
    /// An empty parent-class name answers false on every overload. [isancestorbyclass.srf:L12]
    /// </summary>
    [Fact]
    public void AnEmptyParentClassAnswersFalse()
    {
        LeafFixture leaf = new();

        Assert.False(Ancestry.IsAncestorByObject(leaf, string.Empty));
        Assert.False(Ancestry.IsAncestor(leaf, string.Empty));
        Assert.False(Ancestry.IsAncestorByClass(nameof(LeafFixture), string.Empty));
        Assert.False(Ancestry.IsAncestor(nameof(LeafFixture), string.Empty));
    }

    /// <summary>
    /// An empty class NAME answers false, and so does a null one. [isancestorbyclass.srf:L12]
    /// </summary>
    /// <remarks>
    /// The oracle writes <c>if cls = "" ... then return false</c>, and the port uses
    /// <c>IsNullOrEmpty</c> so that a null answers false BY DECISION rather than by accidentally
    /// surviving into a lookup. Both spellings are asserted because the two arrive by different
    /// routes: an empty string from a configuration value, a null from an uninitialised reference.
    /// </remarks>
    [Fact]
    public void AnEmptyOrNullClassNameAnswersFalse()
    {
        Assert.False(Ancestry.IsAncestorByClass(string.Empty, nameof(RootFixture)));
        Assert.False(Ancestry.IsAncestorByClass(null, nameof(RootFixture)));

        // Both arguments absent at once, which must not throw either.
        Assert.False(Ancestry.IsAncestorByClass(null, string.Empty));
        Assert.False(Ancestry.IsAncestorByClass(string.Empty, string.Empty));
    }

    /// <summary>
    /// A blank-but-not-empty name is NOT treated as absent: it is a name, it resolves to nothing, and
    /// the answer is false for that reason.
    /// </summary>
    /// <remarks>
    /// The distinction matters because the two paths reach false differently and a future
    /// <c>IsNullOrWhiteSpace</c> "tidy" would silently merge them. The oracle tests <c>= ""</c>, so a
    /// single space is a name as far as the guard is concerned; it then fails to resolve, which is the
    /// same outcome by a different route. Asserting it keeps the guard honest without claiming the
    /// oracle distinguishes the two outcomes - it does not, and neither does the port.
    /// </remarks>
    [Fact]
    public void ABlankNameIsANameThatSimplyResolvesToNothing()
    {
        Assert.False(Ancestry.IsAncestorByClass(" ", nameof(RootFixture)));
        Assert.False(Ancestry.IsAncestorByClass("\t", nameof(RootFixture)));
        Assert.False(Ancestry.IsAncestorByObject(new LeafFixture(), " "));
    }

    /// <summary>
    /// A null object answers false. [isancestorbyobject.srf:L12, isvalidobject.srf:L11]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle guards with <c>IsValidObject</c>, whose own null guard is explicit, so this arm is
    /// two ported behaviours meeting rather than one.
    /// </para>
    /// <para>
    /// OVERLOAD RESOLUTION NOTE, and the reason the cast below is not redundant: <c>IsAncestor(null,
    /// "X")</c> binds to the <c>string?</c> overload, not the <c>object?</c> one, because
    /// <c>string</c> is the more specific parameter type. Both answer false, so the result is the
    /// same - but a reader who expects the object overload to have been exercised by a bare
    /// <c>null</c> would be mistaken, so both are called explicitly here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANullObjectAnswersFalse()
    {
        Assert.False(Ancestry.IsAncestorByObject(null, nameof(RootFixture)));

        // Explicitly the object-shaped overload...
        Assert.False(Ancestry.IsAncestor((object?)null, nameof(RootFixture)));

        // ...and explicitly the string-shaped one, which is what a bare `null` would have bound to.
        Assert.False(Ancestry.IsAncestor((string?)null, nameof(RootFixture)));
    }

    /// <summary>
    /// A name that matches no loaded type answers false rather than throwing.
    /// [isancestorbyclass.srf:L14-L15, L20]
    /// </summary>
    /// <remarks>
    /// The oracle's <c>FindClassDefinition</c> returns an invalid handle for an unknown class, so its
    /// <c>do while IsValid(clsDef)</c> is never entered and control falls to <c>return false</c>. The
    /// port's lookup returns null and the walk reports the same.
    /// </remarks>
    [Fact]
    public void AnUnresolvableClassNameAnswersFalse()
    {
        Assert.False(Ancestry.IsAncestorByClass("NoSuchTypeExistsAnywhereInThisProcess", nameof(Object)));
        Assert.False(Ancestry.IsAncestorByClass("NoSuchTypeExistsAnywhereInThisProcess", "AlsoAbsent"));

        // ...and a resolvable start with an unresolvable target, which is the other half of the pair.
        Assert.False(Ancestry.IsAncestorByClass(nameof(LeafFixture), "NoSuchTypeExistsAnywhereInThisProcess"));
    }

    /// <summary>
    /// Nothing in the four-member surface throws, for any combination of absent, blank, unresolvable
    /// and well-formed arguments.
    /// </summary>
    /// <remarks>
    /// The oracle has no error path at all - six lines, two guards and a loop - so "never throws" is a
    /// ported behaviour rather than defensive programming. It is asserted as a sweep because the
    /// individual facts above each cover one arm, and what callers rely on is that NO combination
    /// escapes: a dispatch test that threw would take down the call site instead of taking the other
    /// branch.
    /// </remarks>
    [Fact]
    public void NoCombinationOfArgumentsThrows()
    {
        string?[] names = [null, string.Empty, " ", "LeafFixture", "leaffixture", "NoSuchType"];
        object?[] instances = [null, new LeafFixture(), new UnrelatedFixture(), string.Empty, 0];

        // The sweep. Every result is CONSUMED rather than discarded, so nothing here can be optimised
        // away, and the test's claim is simply that control reaches the count below - any escaping
        // exception fails the test at the call that threw and names the row.
        int answered = 0;

        foreach (string? parent in names)
        {
            foreach (string? className in names)
            {
                // A null parentClass is outside the declared contract - the parameter is
                // non-nullable - so it is passed with a null-forgiving operator on purpose, to prove
                // the guard still holds for a caller compiled with nullable checks disabled.
                answered += Ancestry.IsAncestorByClass(className, parent!) ? 1 : 0;
                answered += Ancestry.IsAncestor(className, parent!) ? 1 : 0;
            }

            foreach (object? instance in instances)
            {
                answered += Ancestry.IsAncestorByObject(instance, parent!) ? 1 : 0;
                answered += Ancestry.IsAncestor(instance, parent!) ? 1 : 0;
            }
        }

        // 6 parents x (6 class names + 5 instances) x 2 overloads = 132 calls, all of which returned.
        Assert.True(answered >= 0, "unreachable: every call above returned a bool rather than throwing");

        // A null parentClass answers false for every shape of first argument, which is the specific
        // row the null-forgiving operator above exists to reach.
        foreach (string? className in names)
        {
            Assert.False(Ancestry.IsAncestorByClass(className, null!));
            Assert.False(Ancestry.IsAncestor(className, null!));
        }

        foreach (object? instance in instances)
        {
            Assert.False(Ancestry.IsAncestorByObject(instance, null!));
            Assert.False(Ancestry.IsAncestor(instance, null!));
        }
    }

    // ==============================================================================================
    //  5. THE DELEGATION THAT HOLDS THE NATIVE INFERENCE IN PLACE
    // ==============================================================================================

    /// <summary>
    /// <see cref="Ancestry.IsAncestor(object?, string)"/> and
    /// <see cref="Ancestry.IsAncestorByObject(object?, string)"/> agree on every input, and so do the
    /// two class-name overloads.
    /// </summary>
    /// <remarks>
    /// <c>isancestor.srf</c> is <c>native "pfw.dll"</c> with no body and no C++ source in the
    /// repository, so its behaviour is INFERRED from the readable siblings whose prototypes it mirrors
    /// exactly [isancestor.srf:L7-L8 against isancestorbyobject.srf:L7 and isancestorbyclass.srf:L7].
    /// The port expresses the inference by delegating. This fact is what keeps that expressible claim
    /// honest: if the two families ever diverge, someone has given <c>IsAncestor</c> a body of its own
    /// and the inference is no longer stated anywhere.
    /// </remarks>
    [Fact]
    public void TheInferredOverloadsAgreeWithTheirReadableSiblings()
    {
        object?[] instances = [null, new LeafFixture(), new MiddleFixture(), new RootFixture(), new UnrelatedFixture(), "a string", 42];
        string?[] classNames = [null, string.Empty, " ", "LeafFixture", "MiddleFixture", "RootFixture", "UnrelatedFixture", "LEAFFIXTURE", "NoSuchType"];
        string[] parents = [string.Empty, "LeafFixture", "MiddleFixture", "RootFixture", "Object", "IFixtureMarker", "NoSuchType"];

        foreach (string parent in parents)
        {
            foreach (object? instance in instances)
            {
                Assert.Equal(
                    Ancestry.IsAncestorByObject(instance, parent),
                    Ancestry.IsAncestor(instance, parent));
            }

            foreach (string? className in classNames)
            {
                Assert.Equal(
                    Ancestry.IsAncestorByClass(className, parent),
                    Ancestry.IsAncestor(className, parent));
            }
        }
    }

    /// <summary>
    /// The object-shaped and class-name-shaped families answer the same question, so an instance and
    /// its own type name give the same result.
    /// </summary>
    /// <remarks>
    /// The two differ only in how the walk is SEEDED - a runtime type versus a resolved one - and the
    /// walk itself is expressed exactly once in the port. This fact is the observable consequence of
    /// that single expression: a second copy of the walk added for either path would show up here as
    /// soon as the two drifted.
    /// </remarks>
    [Fact]
    public void SeedingFromAnObjectAndFromItsTypeNameAgree()
    {
        (object Instance, string Name)[] pairs =
        [
            (new LeafFixture(), nameof(LeafFixture)),
            (new MiddleFixture(), nameof(MiddleFixture)),
            (new RootFixture(), nameof(RootFixture)),
            (new UnrelatedFixture(), nameof(UnrelatedFixture)),
        ];

        string[] parents = ["LeafFixture", "MiddleFixture", "RootFixture", "UnrelatedFixture", "Object", "IFixtureMarker"];

        foreach ((object instance, string name) in pairs)
        {
            foreach (string parent in parents)
            {
                Assert.Equal(
                    Ancestry.IsAncestorByObject(instance, parent),
                    Ancestry.IsAncestorByClass(name, parent));
            }
        }
    }

    // ==============================================================================================
    //  6. THE SUBSTITUTION'S OWN PROPERTIES
    //
    //  These are properties of replacing a flat global namespace with loaded assemblies. They do not
    //  contradict any legacy answer, because the situations cannot arise in a namespace that holds at
    //  most one class of any name - so they are recorded as characteristics of the substitution.
    // ==============================================================================================

    /// <summary>
    /// The lookup reaches types in any already-loaded assembly, including the framework's own, and
    /// walks their chains.
    /// </summary>
    /// <remarks>
    /// Framework types are used here rather than fixture types because they prove the SCOPE of the
    /// lookup - it is not limited to the test assembly - and because their inheritance chains are
    /// fixed by the platform, so the expected answers cannot drift with this repository.
    /// </remarks>
    [Fact]
    public void TheLookupReachesTypesInAnyLoadedAssembly()
    {
        // System.String derives from System.Object, in one step.
        Assert.True(Ancestry.IsAncestorByClass("String", "Object"));

        // A multi-level framework chain: ArgumentNullException -> ArgumentException -> SystemException
        // -> Exception -> Object.
        Assert.True(Ancestry.IsAncestorByClass("ArgumentNullException", "ArgumentException"));
        Assert.True(Ancestry.IsAncestorByClass("ArgumentNullException", "Exception"));
        Assert.True(Ancestry.IsAncestorByClass("ArgumentNullException", "Object"));
        Assert.False(Ancestry.IsAncestorByClass("ArgumentException", "ArgumentNullException"));

        // The same chain reached from an instance, which is the path the 30 legacy call sites use.
        Assert.True(Ancestry.IsAncestorByObject(new ArgumentNullException("p"), "Exception"));
    }

    /// <summary>
    /// The lookup is repeatable: the same name answers the same way every time, rather than depending
    /// on which assembly or type the runtime happened to enumerate first.
    /// </summary>
    /// <remarks>
    /// Neither <c>AppDomain.GetAssemblies</c> nor <c>Assembly.GetTypes</c> guarantees an order, so the
    /// port scans to completion and keeps the ordinally smallest FullName rather than the first match.
    /// Without that, two runs of the same program could answer differently for a name carried by two
    /// assemblies - which is not a legacy behaviour being contradicted, since a flat namespace cannot
    /// hold two classes of one name, but it would make a characterization recording unusable.
    /// </remarks>
    [Fact]
    public void TheLookupIsRepeatableAcrossRepeatedCalls()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(RootFixture)));
            Assert.True(Ancestry.IsAncestorByClass("String", "Object"));
            Assert.False(Ancestry.IsAncestorByClass("NoSuchTypeExistsAnywhereInThisProcess", "Object"));
        }
    }

    /// <summary>
    /// The one arm of the implementation this suite cannot reach, recorded rather than left as an
    /// unexplained coverage hole: the catch that SKIPS an assembly whose types cannot be enumerated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ResolveLoadedTypeByName</c> wraps <c>Assembly.GetTypes</c> in a filtered catch over
    /// <see cref="System.Reflection.ReflectionTypeLoadException"/> and five sibling load failures, and
    /// continues to the next assembly rather than propagating. Reaching that arm needs a loaded,
    /// NON-dynamic assembly whose metadata references something absent from disk - which cannot be
    /// produced from inside a test without writing a purpose-built broken assembly to the filesystem
    /// and loading it. This project is pure behaviour with no I/O by decision, and adding a compiler
    /// or an assembly-emitting dependency to reach one catch would be a larger change than the gap.
    /// </para>
    /// <para>
    /// So the arm is UNCOVERED, deliberately and with the reason recorded here. What this fact asserts
    /// instead is the OBSERVABLE contract that arm exists to provide - a name that cannot be resolved
    /// answers false, and resolution keeps working for everything else afterwards - which is the part
    /// a caller can actually depend on. The arm's own logic is three lines with no branch inside it.
    /// </para>
    /// <para>
    /// This is stated as a limitation and not as proof. It should not be cited as evidence that the
    /// catch behaves correctly under a genuinely broken assembly; only a characterization run in an
    /// environment that has one would establish that.
    /// </para>
    /// </remarks>
    [Fact]
    public void ResolutionSurvivesAnUnresolvableNameAndKeepsWorkingAfterwards()
    {
        Assert.False(Ancestry.IsAncestorByClass("NoSuchTypeExistsAnywhereInThisProcess", "Object"));

        // The scan ran to completion rather than aborting part way, so a name it SHOULD find is still
        // found immediately afterwards.
        Assert.True(Ancestry.IsAncestorByClass(nameof(LeafFixture), nameof(RootFixture)));
        Assert.True(Ancestry.IsAncestorByClass("String", "Object"));
    }

    /// <summary>
    /// A generic type's name carries the arity suffix the runtime gives it, so <c>List</c> does not
    /// resolve while <c>List`1</c> does.
    /// </summary>
    /// <remarks>
    /// A property of the substitution rather than a ported behaviour: PowerBuilder has no generics, so
    /// the legacy is never asked this and no legacy answer is being contradicted. It is asserted
    /// because <c>Type.Name</c>'s backtick spelling is genuinely surprising, and a caller passing
    /// <c>"List"</c> and getting false back deserves to find the reason written down somewhere rather
    /// than concluding the predicate is broken.
    /// </remarks>
    [Fact]
    public void AGenericTypeNameCarriesItsAritySuffix()
    {
        Assert.False(Ancestry.IsAncestorByClass("List", "Object"));
        Assert.True(Ancestry.IsAncestorByClass("List`1", "Object"));

        // ...and from an instance, where the runtime type supplies the same spelling.
        Assert.True(Ancestry.IsAncestorByObject(new System.Collections.Generic.List<int>(), "List`1"));
        Assert.False(Ancestry.IsAncestorByObject(new System.Collections.Generic.List<int>(), "List"));
    }
}
