// ==================================================================================================
//  AncestryTests.cs - THE FOUR INHERITANCE PREDICATES
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Kernel.Ancestry
//  ORACLES           ws_objects/pfw.common.pbl.src/isancestor.srf          prototypes only, NATIVE
//                    ws_objects/pfw.common.pbl.src/isancestorbyclass.srf   readable body, L12-L20
//                    ws_objects/pfw.common.pbl.src/isancestorbyobject.srf  readable body, L12-L20
//                    ws_objects/pfw.shared.pbl.src/isvalidobject.srf       the object-path guard
//
//  THE ORACLE IS THESE FOUR FILES AND NOTHING ELSE. There is NO legacy test window for these
//  primitives - no w_test_ancestry exists among the 47 w_test_*.srw files in
//  ws_objects/pfw.tests.pbl.src/ - so the two readable PowerScript bodies are the entire behavioural
//  specification. Every expectation below therefore cites the `<file>.srf:L` line it comes from, or
//  is explicitly labelled as CHARACTERIZING THE PORT where the legacy body lives in a closed binary
//  and there is nothing to cite. Nothing here is read from disk at run time (constraint C-C): the
//  legacy tree is the read-only oracle, quoted in comments, never opened by a test.
//
//  WHY THIS SUITE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Ancestry is a dispatch primitive: 30 legacy call sites ask "is this thing a T" and branch on the
//  answer. A predicate that answers false where it should answer true does not fail loudly - it
//  quietly takes the other branch, so the defect surfaces as missing behaviour somewhere else
//  entirely. That makes an executable characterization of the whole four-member surface the least
//  affordable gap in this library, and it is why the negative rows below matter as much as the
//  positive ones.
//
//  WHAT THE ORACLE ACTUALLY SAYS
//  ------------------------------------------------------------------------------------------------
//  Two of the three oracle files have readable bodies and they are byte-identical from L15 to L20:
//
//      L12   if <arg> = "" or parentCls = "" then return false     -- or Not IsValidObject(object)
//      L14   clsDef = FindClassDefinition(cls)                     -- or object.ClassDefinition
//      L15   do while IsValid(clsDef)
//      L16      if clsDef.name = parentCls then return true
//      L17      clsDef = clsDef.Ancestor
//      L18   loop
//      L20   return false
//
//  Four properties follow from those six lines, and every one is asserted below:
//
//   1. THE WALK IS SELF-INCLUSIVE. `clsDef` is seeded from the argument and tested BEFORE any
//      ancestor is taken [isancestorbyclass.srf:L14-L17], so a type IS its own ancestor. All 30
//      legacy call sites depend on it - they ask "is this a T", and an exclusive walk would answer
//      false for an exact T.
//   2. THE CHAIN IS SINGLE INHERITANCE. `clsDef.Ancestor` [L17] walks base classes only.
//   3. THE NAME IS UNQUALIFIED AND THE COMPARISON IS ORDINAL [L16].
//   4. NOTHING THROWS. The oracle has no error path at all: an empty name, an absent class and an
//      invalid object are all just false [L12, L20].
//
//  C-K - THE SUBSTITUTION THIS SUITE PINS, AND WHY IT IS DELIBERATELY NARROWER THAN THE OBVIOUS ONE
//  ------------------------------------------------------------------------------------------------
//  The legacy decides the relationship by comparing `ClassDefinition.name` while walking
//  `ClassDefinition.Ancestor`. The managed substitute is a `System.Type` walk over `Type.BaseType`
//  comparing `Type.Name`:
//
//      ClassDefinition             System.Type              clsDef.name        type.Name
//      object.ClassDefinition      object.GetType()         clsDef.Ancestor    type.BaseType
//      IsValid(clsDef)             type is not null         FindClassDefinition  a name lookup
//
//  THAT IS NARROWER THAN `Type.IsAssignableFrom`, ON PURPOSE. A base-type walk never traverses an
//  implemented interface, and `ClassDefinition.Ancestor` cannot yield one either - PowerBuilder has
//  no interfaces in this sense at all - so answering true across an interface would report a
//  relationship the oracle has no way to express. `IsAssignableFrom` also does not FIT the inputs:
//  the target arrives only ever as a runtime string, and a name is not a Type.
//
//  `IsAssignableFrom` is shorter, reads better and is wrong here, which makes it the single most
//  likely "simplification" a future author will reach for. THE INTERFACE-NEGATIVE FACTS EXIST TO
//  HOLD THAT LINE, and they are written next to an `IsAssignableFrom` assertion that answers TRUE on
//  the same two types, so the narrowing is legible in one screen rather than inferred from an
//  absence. If those facts ever go green against an assignability-based implementation, they have
//  stopped doing their job.
//
//  C-K - THE CASING DECISION, READ OFF THE IMPLEMENTATION RATHER THAN ASSUMED
//  ------------------------------------------------------------------------------------------------
//  PowerBuilder normalises object names to lower case, which is visible in the export headers
//  (`string objectname = "pfwexception"` at ws_objects/pfw.shared.pbl.src/pfwexception.sru:L8),
//  whereas C# `Type.Name` preserves the declared casing. The legacy comparison at L16 is a plain
//  PowerScript `=`, so the port had a choice to make and it made it explicitly:
//
//      THE PORT COMPARES WITH StringComparison.Ordinal - CASE-SENSITIVE - AT BOTH SITES,
//      the chain comparison and the class-name lookup.
//
//  Two reasons, both from Ancestry.cs's DECISION 2: PowerScript `=` on strings is a byte-for-byte
//  comparison, so ordinal is the faithful match; and a culture-sensitive comparison would make the
//  answer depend on the ambient culture, which the characterization model cannot tolerate because a
//  recording taken on one host would not be comparable with one taken on another.
//
//  The suite pins that decision in a way that DISTINGUISHES the two conventions rather than passing
//  under either: a name differing only in case answers FALSE, and it would answer TRUE under any
//  ignore-case comparison. A premise assertion states outright that the two spellings ARE a
//  case-insensitive match, so the negative row cannot go quietly vacuous after a fixture rename. The
//  culture-independence half is exercised under tr-TR, whose dotted and dotless i is the standard
//  demonstration that casing rules are not universal.
//
//  C-B - TWO BEHAVIOURS THAT LOOK LIKE SLOPPINESS AND ARE NOT. DO NOT "FIX" EITHER.
//  ------------------------------------------------------------------------------------------------
//   * THE GUARDS RETURN false, THEY DO NOT THROW [isancestorbyclass.srf:L12,
//     isancestorbyobject.srf:L12]. An empty class name, an empty parent name and an invalid object
//     are answered, not rejected. Adding an ArgumentException here would convert a dispatch test
//     that quietly takes the other branch into one that takes down its call site.
//   * THE WALK IS SELF-INCLUSIVE, so `IsAncestorByClass(X, X)` is true. The everyday meaning of
//     "ancestor" excludes the thing itself, which is exactly why this is the property most likely to
//     be "corrected" into a proper-ancestor check.
//  If either behaviour ever changes, the change is a behavioural regression under constraint C-B and
//  the tests below are the specification, not the obstacle. Do not adjust an expectation here to
//  make a rewritten implementation pass.
//
//  C-H - THE BUILD CONSTRAINTS THIS FILE IS WRITTEN UNDER
//  ------------------------------------------------------------------------------------------------
//  Nullable reference types are enabled and TreatWarningsAsErrors is on for test projects exactly as
//  it is for the library, so a warning here is a build failure. Consequently this file contains NO
//  null-forgiving `!` operator, NO `#pragma` and NO analyzer suppression of any kind; where a value
//  is nullable it is handled, not asserted away. The one place the oracle's guard can only be
//  reached by a null arriving at a non-nullable parameter - which a nullable-aware caller cannot
//  write - is reached through reflection instead, because that is how such a call actually happens
//  at run time. Every fixture type declared below is USED, and none declares an underscore: the
//  repository's .editorconfig scopes its CA1707 and IDE1006 suppressions to nine named
//  implementation files and covers no test file at all.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns exactly one line, "No user rules provided.", so NO user-specified rule
//  governs this file. That is a finding rather than latitude: the enterprise baseline applies in
//  their place, and the binding constraints cited inline are the refactor's own - C-B (replicate
//  behaviour, never improve), C-C (the legacy tree is the read-only oracle), C-H (nullable and
//  warnings-as-errors, with a coverage gate) and C-K (document every technology-specific decision).
//
//  SHAPE
//  ------------------------------------------------------------------------------------------------
//  Table-driven parity matrices expressed as theories with member data, which is the prescribed
//  shape for parity work in this refactor and the shape every sibling suite in this project uses.
//  Targeted facts carry the individual pinned decisions, where a single row would hide which
//  property was being asserted.
// ==================================================================================================

using System.Globalization;
using System.Reflection;

using Xunit;

// A BLOCK-SCOPED namespace, deliberately, where every sibling suite in this project uses a
// file-scoped one. The reason is structural rather than stylistic: SECTION F has to declare two
// DISTINCT types that share an unqualified name in two DIFFERENT namespaces, to pin the port's
// same-name tie-break, and a file-scoped namespace declaration permits no second namespace in the
// file. The repository's .editorconfig sets `csharp_style_namespace_declarations = block_scoped`,
// so this is also the configured preference rather than a deviation from it.
namespace PowerFramework.Shared.Kernel.Tests
{
    /// <summary>
    /// Characterization tests for <see cref="Ancestry"/>'s four inheritance predicates, ported from
    /// <c>isancestor.srf</c>, <c>isancestorbyclass.srf</c> and <c>isancestorbyobject.srf</c> under
    /// <c>ws_objects/pfw.common.pbl.src/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two expectations here pin behaviour that looks wrong and is not, because the refactor
    /// replicates behaviour rather than improving it: the walk is SELF-INCLUSIVE, so a type is its
    /// own ancestor, and every guard answers <see langword="false"/> instead of throwing. Read this
    /// file's header before changing either.
    /// </para>
    /// <para>
    /// The centrepiece is the interface-negative pair in SECTION C. The port walks
    /// <see cref="System.Type.BaseType"/> and compares <see cref="System.Type.Name"/>, which is
    /// deliberately narrower than <see cref="System.Type.IsAssignableFrom(System.Type)"/>; those
    /// facts are what stop the narrowing being "simplified" away.
    /// </para>
    /// <para>
    /// No <c>using PowerFramework.Shared.Kernel;</c> directive appears above: this namespace is
    /// nested inside it, so simple-name lookup walks outward and finds both <see cref="Ancestry"/>
    /// and <see cref="Predicates"/> there.
    /// </para>
    /// </remarks>
    public sealed class AncestryTests
    {
        // ==========================================================================================
        //  THE FIXTURE
        //  ----------------------------------------------------------------------------------------
        //  A three-level chain, an interface, a standalone implementer of that interface and an
        //  unrelated type. Declared nested and private so no other suite's type can accidentally
        //  satisfy a lookup here, and named distinctively so that a lookup for "ChainMiddle" can
        //  only mean this one - the port resolves a bare name across every loaded assembly, so a
        //  fixture called "Base" would be a genuine hazard.
        //
        //  NESTED TYPES ARE USED ON PURPOSE. Type.Name reports the UNQUALIFIED name for a nested
        //  type, with no enclosing-type prefix, so a nested fixture proves the port reads Type.Name
        //  rather than Type.FullName: if it read FullName, every positive row below would answer
        //  false, because a FullName here is
        //  "PowerFramework.Shared.Kernel.Tests.AncestryTests+ChainRoot".
        //
        //  Each type carries no members beyond what a type-identity test needs, which is none.
        // ==========================================================================================

        /// <summary>
        /// A marker interface, implemented both from INSIDE the chain (<see cref="ChainMiddle"/>) and
        /// from outside it (<see cref="MarkerImplementer"/>), so the interface-negative facts can be
        /// asserted from both directions.
        /// </summary>
        private interface IAncestryMarker;

        /// <summary>The root of the fixture chain. Its own base type is <see cref="object"/>.</summary>
        private class ChainRoot;

        /// <summary>
        /// Level two, and the type that DECLARES <see cref="IAncestryMarker"/> - so an
        /// interface-walking implementation would answer true for it and for everything below it.
        /// </summary>
        private class ChainMiddle : ChainRoot, IAncestryMarker;

        /// <summary>
        /// Level three, so a match at depth two proves the walk takes more than one step rather than
        /// checking a single base type.
        /// </summary>
        private sealed class ChainLeaf : ChainMiddle;

        /// <summary>A type with no relationship to the chain at all.</summary>
        private sealed class UnrelatedFixture;

        /// <summary>
        /// Implements <see cref="IAncestryMarker"/> and inherits from NOTHING in the chain - its base
        /// type is <see cref="object"/>. This is the fixture that makes the interface-negative case
        /// unambiguous: for it, the interface is the ONLY relationship there is, so a false answer
        /// cannot be explained by anything else in the hierarchy.
        /// </summary>
        private sealed class MarkerImplementer : IAncestryMarker;

        // ==========================================================================================
        //  NAMED INPUTS
        //  ----------------------------------------------------------------------------------------
        //  Type names are written as `nameof(...)` throughout, so a fixture rename cannot silently
        //  turn a positive row into an unresolvable-name row. The three constants below are the only
        //  hand-written name literals in the file, and each is a name that deliberately does NOT
        //  denote a fixture: two are case variants used to distinguish an ordinal comparison from a
        //  case-insensitive one, and one denotes nothing at all. Their pairing with the real names is
        //  itself asserted, in SECTION E, so they cannot drift out of alignment unnoticed.
        // ==========================================================================================

        /// <summary>
        /// A name that resolves to no loaded type, for the guard arm at
        /// <c>isancestorbyclass.srf:L14-L15</c> where <c>FindClassDefinition</c> finds nothing.
        /// </summary>
        private const string AbsentTypeName = "NoSuchTypeExistsAnywhereInThisProcess";

        /// <summary>
        /// <see cref="ChainLeaf"/>'s name in lower case. Not the name of any type; it exists to make
        /// an ordinal comparison distinguishable from a case-insensitive one.
        /// </summary>
        private const string ChainLeafNameLowerCased = "chainleaf";

        /// <summary>
        /// <see cref="ChainLeaf"/>'s name with one letter's case flipped, so the ordinal rows cover a
        /// mixed-case variant as well as a wholly lower-cased one.
        /// </summary>
        private const string ChainLeafNameMixedCased = "ChainLeaF";

        /// <summary>
        /// Materialises the fixture instance named by <paramref name="fixtureName"/>.
        /// </summary>
        /// <param name="fixtureName">
        /// The unqualified name of one of the instantiable fixture types declared above.
        /// </param>
        /// <returns>A new instance of that fixture.</returns>
        /// <remarks>
        /// Theory rows carry the fixture NAME rather than an instance, for two reasons. Names are
        /// serializable, so every row is individually discoverable and re-runnable by the test
        /// runner. And carrying the name lets one row assert BOTH families at once - the object path
        /// seeded from the instance and the class-name path seeded from the same name - which is what
        /// makes the seed-equivalence property observable per row instead of only in aggregate.
        /// The unmatched arm throws rather than returning null: an unknown key is a defect in the
        /// row, not an input worth answering.
        /// </remarks>
        private static object CreateFixture(string fixtureName)
        {
            return fixtureName switch
            {
                nameof(ChainRoot) => new ChainRoot(),
                nameof(ChainMiddle) => new ChainMiddle(),
                nameof(ChainLeaf) => new ChainLeaf(),
                nameof(UnrelatedFixture) => new UnrelatedFixture(),
                nameof(MarkerImplementer) => new MarkerImplementer(),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(fixtureName),
                    fixtureName,
                    "Not an instantiable fixture declared by AncestryTests. Theory rows whose seed "
                    + "cannot be instantiated - an interface name, or a name that resolves to "
                    + "nothing - belong in the class-name-only tables instead."),
            };
        }

        // ==========================================================================================
        //  SECTION A - THE CHAIN WALK, AS ONE TABLE
        //  ----------------------------------------------------------------------------------------
        //  One row per (seed, parent) question, one expected answer, asserted through all four
        //  members. The whole relation is legible as a single artifact, so a change to it shows up as
        //  a changed ROW rather than as a changed assertion buried in a method body.
        //
        //  WHY ONE EXPECTED VALUE COVERS ALL FOUR MEMBERS. The object path seeds the walk from
        //  `object.ClassDefinition` [isancestorbyobject.srf:L14] and the class-name path seeds it from
        //  `FindClassDefinition(cls)` [isancestorbyclass.srf:L14]; the loop from L15 to L20 is then
        //  byte-identical in both oracle files. So a row that answered differently on the two paths
        //  would mean the port had grown a second copy of the walk, and a single `expected` column is
        //  what makes that visible. The two `IsAncestor` overloads are asserted here too because they
        //  delegate to those two members, which is how the port expresses an inference about a native
        //  body it cannot read [isancestor.srf:L3].
        //
        //  Rows whose seed cannot be instantiated - an interface name, an absent name, an empty name -
        //  are NOT in this table. They live in SECTION F and SECTION G, where only the class-name
        //  family applies and the expected answers are stated for that family alone.
        // ==========================================================================================

        /// <summary>
        /// One row per ancestry question: the seed fixture's name, the parent class name being looked
        /// for, and the answer the oracle's loop gives. [isancestorbyclass.srf:L14-L20,
        /// isancestorbyobject.srf:L14-L20]
        /// </summary>
        public static TheoryData<string, string, bool> ChainWalkRows()
        {
            TheoryData<string, string, bool> rows = [];

            // The name every .NET chain terminates at. Written as typeof(object).Name rather than as
            // the literal "Object" so it stays correct by construction, and reached through the
            // framework type name because the walk compares Type.Name, which spells it that way.
            string chainRootName = typeof(object).Name;

            //  SELF-INCLUSIVE, THE PROPERTY MOST LIKELY TO BE "CORRECTED". The oracle compares at L16
            //  BEFORE advancing at L17, so the seed itself is tested and a type is its own ancestor.
            //  These three rows are what a proper-ancestor rewrite would turn red.
            //  [isancestorbyclass.srf:L15-L16]
            rows.Add(nameof(ChainLeaf), nameof(ChainLeaf), true);
            rows.Add(nameof(ChainMiddle), nameof(ChainMiddle), true);
            rows.Add(nameof(ChainRoot), nameof(ChainRoot), true);
            rows.Add(nameof(UnrelatedFixture), nameof(UnrelatedFixture), true);

            //  ONE STEP UP. The direct base type. [isancestorbyclass.srf:L17]
            rows.Add(nameof(ChainLeaf), nameof(ChainMiddle), true);
            rows.Add(nameof(ChainMiddle), nameof(ChainRoot), true);

            //  TWO STEPS UP, which is the row that proves the walk LOOPS rather than checking a
            //  single base type. A three-level fixture exists solely so this row can be written; a
            //  two-level one would be satisfied by an implementation that advanced once and stopped.
            //  [isancestorbyclass.srf:L15-L18]
            rows.Add(nameof(ChainLeaf), nameof(ChainRoot), true);

            //  THE ROOT OF THE CHAIN, AND THE LOOP'S TERMINATION FROM THE INSIDE. CHARACTERIZES THE
            //  PORT: the oracle's chain ends at PowerBuilder's own root and the .NET chain ends at
            //  System.Object, so "Object" is in EVERY class's chain and matches everything. The shape
            //  is preserved - `Type.BaseType` yields null above System.Object exactly as
            //  `ClassDefinition.Ancestor` yields an invalid handle at the legacy root
            //  [isancestorbyclass.srf:L15] - while the root's spelling is a platform fact, not a
            //  choice. Reaching it also proves the loop terminates instead of running forever.
            rows.Add(nameof(ChainLeaf), chainRootName, true);
            rows.Add(nameof(ChainMiddle), chainRootName, true);
            rows.Add(nameof(ChainRoot), chainRootName, true);
            rows.Add(nameof(UnrelatedFixture), chainRootName, true);
            rows.Add(nameof(MarkerImplementer), chainRootName, true);

            //  THE RELATION IS DIRECTIONAL, NOT SYMMETRIC. The walk climbs and never descends. These
            //  rows matter because a symmetric implementation - assignability called the wrong way
            //  round, say - satisfies every positive row above while getting these wrong.
            rows.Add(nameof(ChainMiddle), nameof(ChainLeaf), false);
            rows.Add(nameof(ChainRoot), nameof(ChainMiddle), false);
            rows.Add(nameof(ChainRoot), nameof(ChainLeaf), false);

            //  UNRELATED IN BOTH DIRECTIONS, however well-formed both names are.
            rows.Add(nameof(ChainLeaf), nameof(UnrelatedFixture), false);
            rows.Add(nameof(UnrelatedFixture), nameof(ChainRoot), false);
            rows.Add(nameof(UnrelatedFixture), nameof(ChainLeaf), false);

            //  THE INTERFACE ROWS - THE POINT OF THE WHOLE SUITE, tabled here and then asserted again
            //  as explicit facts in SECTION C beside the assignability comparison that makes the
            //  narrowing legible. `ClassDefinition.Ancestor` [isancestorbyclass.srf:L17] is single
            //  inheritance and PowerBuilder has no interfaces for it to reach, so the port walks
            //  Type.BaseType only and an implemented interface is OUT OF REACH BY CONSTRUCTION.
            //  Type.IsAssignableFrom would answer true for all three of these.
            rows.Add(nameof(ChainMiddle), nameof(IAncestryMarker), false);
            rows.Add(nameof(ChainLeaf), nameof(IAncestryMarker), false);
            rows.Add(nameof(MarkerImplementer), nameof(IAncestryMarker), false);

            //  ...and the standalone implementer is otherwise unrelated to the chain, so its false
            //  above cannot be explained by anything except the interface not being walked.
            rows.Add(nameof(MarkerImplementer), nameof(ChainRoot), false);
            rows.Add(nameof(ChainLeaf), nameof(MarkerImplementer), false);

            //  ORDINAL COMPARISON [isancestorbyclass.srf:L16]. Three case variants of a name that
            //  DOES match in its declared spelling, so each row would answer true under any
            //  ignore-case comparison. SECTION D asserts the premise that these really are
            //  case-insensitive matches of the fixture's name, so a rename cannot make them vacuous.
            rows.Add(nameof(ChainLeaf), ChainLeafNameLowerCased, false);
            rows.Add(nameof(ChainLeaf), ChainLeafNameMixedCased, false);
            rows.Add(nameof(ChainLeaf), nameof(ChainLeaf).ToUpperInvariant(), false);

            //  THE COMPARISON IS EQUALITY, NOT CONTAINMENT OR PREFIX MATCHING, and it does not trim.
            //  PowerScript `=` on strings compares the whole value, so a substring of a name in the
            //  chain is not a name in the chain and neither is a padded one.
            rows.Add(nameof(ChainLeaf), "Chain", false);
            rows.Add(nameof(ChainLeaf), "Leaf", false);
            rows.Add(nameof(ChainLeaf), " " + nameof(ChainLeaf), false);
            rows.Add(nameof(ChainLeaf), nameof(ChainLeaf) + " ", false);

            //  AN UNRESOLVABLE TARGET is simply absent from the chain, so the loop runs to the top
            //  and falls through to L20's `return false` rather than failing in any other way.
            rows.Add(nameof(ChainLeaf), AbsentTypeName, false);

            return rows;
        }

        /// <summary>
        /// Asserts the whole ancestry relation a row at a time, through all four public members.
        /// [isancestorbyclass.srf:L14-L20, isancestorbyobject.srf:L14-L20, isancestor.srf:L7-L8]
        /// </summary>
        /// <param name="fixtureName">The unqualified name of the fixture the walk is seeded with.</param>
        /// <param name="parentClass">The class name being looked for.</param>
        /// <param name="expected">The answer the oracle's loop gives for that pair.</param>
        [Theory]
        [MemberData(nameof(ChainWalkRows))]
        public void TheChainWalkReproducesTheLegacyRelation(
            string fixtureName,
            string parentClass,
            bool expected)
        {
            object instance = CreateFixture(fixtureName);

            // isancestorbyobject.srf:L14 - `clsDef = object.ClassDefinition`, then the walk at
            // L15-L20. The port seeds from value.GetType(), which is why the answer is the runtime
            // type's own and not the declared type's.
            Assert.Equal(expected, Ancestry.IsAncestorByObject(instance, parentClass));

            // isancestorbyclass.srf:L14 - `clsDef = FindClassDefinition(cls)`, then the same walk.
            // Getting the same answer from a NAME as from an INSTANCE is the seed-equivalence
            // property: the two oracle bodies differ only in their seed and their guard.
            Assert.Equal(expected, Ancestry.IsAncestorByClass(fixtureName, parentClass));

            // isancestor.srf:L7 - the native, object-shaped prototype. `isancestor.srf:L3` binds the
            // whole function object to pfw.dll and the file has no body, so its behaviour is INFERRED
            // from the readable sibling whose prototype it mirrors; the port delegates rather than
            // duplicating the walk, and this assertion is what keeps the inference honest.
            Assert.Equal(expected, Ancestry.IsAncestor(instance, parentClass));

            // isancestor.srf:L8 - the native, string-shaped prototype, inferred the same way.
            Assert.Equal(expected, Ancestry.IsAncestor(fixtureName, parentClass));
        }

        // ==========================================================================================
        //  SECTION B - THE SELF-INCLUSIVE WALK, AS ITS OWN FACT
        //  ----------------------------------------------------------------------------------------
        //  Tabled in SECTION A as well, and restated here as a fact for one reason: it is the single
        //  behaviour in this file most likely to be changed by someone who believes they are fixing
        //  it, and a fact carries the reasoning at the point of failure where a table row can only
        //  carry a row number.
        // ==========================================================================================

        /// <summary>
        /// A type is its own ancestor, on every overload. [isancestorbyclass.srf:L15-L16,
        /// isancestorbyobject.srf:L15-L16]
        /// </summary>
        /// <remarks>
        /// <para>
        /// C-B - PRESERVED LEGACY BEHAVIOUR, NOT AN OVERSIGHT. The oracle's loop compares at L16 and
        /// only then advances at L17, so the seed is tested before any base type is. The everyday
        /// meaning of "ancestor" excludes the thing itself, which is exactly why this is the property
        /// a well-intentioned rewrite breaks first.
        /// </para>
        /// <para>
        /// It is also what makes the legacy work. All 30 real call sites are dispatch tests of the
        /// form "is this thing a <c>T</c>" - the canonical one being
        /// <c>IsAncestor(target,"n_cst_dwsvc")</c> at
        /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L603</c> - and an EXACT
        /// <c>n_cst_dwsvc</c> must answer yes there just as a subclass must. Narrowing this to
        /// proper-ancestor-only would silently turn every one of those into "is this a STRICT subclass
        /// of <c>T</c>", a different question with a different answer for the commonest input.
        /// </para>
        /// </remarks>
        [Fact]
        public void ATypeIsItsOwnAncestorOnEveryOverload()
        {
            ChainLeaf leaf = new();

            Assert.True(
                Ancestry.IsAncestorByObject(leaf, nameof(ChainLeaf)),
                "The oracle seeds the loop with the object's own class [isancestorbyobject.srf:L14] "
                + "and compares before advancing [L16-L17], so a type is its own ancestor.");

            Assert.True(
                Ancestry.IsAncestorByClass(nameof(ChainLeaf), nameof(ChainLeaf)),
                "The oracle seeds the loop with the resolved class itself "
                + "[isancestorbyclass.srf:L14] and compares before advancing [L16-L17].");

            Assert.True(Ancestry.IsAncestor(leaf, nameof(ChainLeaf)));
            Assert.True(Ancestry.IsAncestor(nameof(ChainLeaf), nameof(ChainLeaf)));
        }

        // ==========================================================================================
        //  SECTION C - INTERFACES ARE NOT TRAVERSED. THE CENTREPIECE.
        //  ----------------------------------------------------------------------------------------
        //  C-K. The port walks Type.BaseType and compares Type.Name, which is DELIBERATELY NARROWER
        //  than Type.IsAssignableFrom. Two independent reasons, either sufficient on its own:
        //
        //    1. IT DOES NOT FIT THE INPUTS. IsAssignableFrom needs the target as a resolved Type, and
        //       here the target arrives only ever as a runtime STRING. Verified against the tree
        //       rather than assumed: two call sites pass a CONSTANT rather than a literal,
        //       `IsAncestor(object,CLS_TABPAGE)` at
        //       ws_objects/pfw.ui.controls.pbl.src/u_cst_tabcontrol.sru:L3816 and :L4668; and one
        //       passes an ARRAY ELEMENT, `IsAncestor(control,strValidCls[index])` at
        //       ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_thememanager.sru:L138, inside a
        //       `for index = 1 to UpperBound(strValidCls)` loop over candidate class names. A name is
        //       not a Type and cannot become one without a lookup whose result may be a DIFFERENT type
        //       that merely shares the name.
        //    2. IT WOULD ANSWER A WIDER QUESTION. `ClassDefinition.Ancestor`
        //       [isancestorbyclass.srf:L17] is single inheritance and PowerBuilder has no interfaces
        //       in this sense for it to traverse, so answering true across one would report a
        //       relationship the oracle has no way to express. That is a widening, not added fidelity.
        //
        //  The facts below are what hold that line. Each one puts the ancestry answer and the
        //  assignability answer SIDE BY SIDE on the same two types, so the narrowing is a visible
        //  disagreement rather than an inference from an absence. A rewrite to IsAssignableFrom turns
        //  them red immediately, which is the entire point of the section.
        // ==========================================================================================

        /// <summary>
        /// An implemented interface is NOT an ancestor - while
        /// <see cref="System.Type.IsAssignableFrom(System.Type)"/> says the relationship exists.
        /// [isancestorbyclass.srf:L17]
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two assertion families are written next to each other on purpose. The first says what
        /// this predicate answers; the second says what a wider predicate would answer; the gap
        /// between them IS the documented substitution decision. Read them together.
        /// </para>
        /// <para>
        /// Three seeds cover the three ways an interface can be reached: the type that DECLARES it,
        /// a type that inherits it through the chain, and a standalone implementer with no other
        /// relationship at all. The third is the unambiguous one - for
        /// <see cref="MarkerImplementer"/> the interface is the only relationship there is, so a
        /// false answer cannot be attributed to anything else in the hierarchy.
        /// </para>
        /// </remarks>
        [Fact]
        public void AnImplementedInterfaceIsNotAnAncestorEvenThoughItIsAssignable()
        {
            ChainLeaf leaf = new();
            ChainMiddle middle = new();
            MarkerImplementer standalone = new();

            // WHAT THIS PREDICATE ANSWERS. The walk is Type.BaseType only, so the interface is never
            // a link in the chain.
            Assert.False(
                Ancestry.IsAncestorByObject(middle, nameof(IAncestryMarker)),
                "The declaring type must not report its interface as an ancestor: the oracle's "
                + "Ancestor chain [isancestorbyclass.srf:L17] is single inheritance.");
            Assert.False(Ancestry.IsAncestorByObject(leaf, nameof(IAncestryMarker)));
            Assert.False(
                Ancestry.IsAncestorByObject(standalone, nameof(IAncestryMarker)),
                "The standalone implementer's ONLY relationship is the interface, so a true here "
                + "could only mean the implementation had started walking interfaces.");

            Assert.False(Ancestry.IsAncestorByClass(nameof(ChainMiddle), nameof(IAncestryMarker)));
            Assert.False(Ancestry.IsAncestorByClass(nameof(ChainLeaf), nameof(IAncestryMarker)));
            Assert.False(
                Ancestry.IsAncestorByClass(nameof(MarkerImplementer), nameof(IAncestryMarker)));

            // ...and through the two native-shaped overloads, which delegate to the two above.
            Assert.False(Ancestry.IsAncestor(standalone, nameof(IAncestryMarker)));
            Assert.False(Ancestry.IsAncestor(nameof(MarkerImplementer), nameof(IAncestryMarker)));

            // WHAT A WIDER PREDICATE WOULD ANSWER. Assignability holds for all three seeds, so the
            // interface really is implemented and the rows above are a statement about THE WALK
            // rather than an accident of a mis-declared fixture. This is the assertion that makes an
            // IsAssignableFrom rewrite impossible to ship quietly: it would have to make every
            // assertion above disagree with every assertion here.
            Assert.True(typeof(IAncestryMarker).IsAssignableFrom(typeof(ChainMiddle)));
            Assert.True(typeof(IAncestryMarker).IsAssignableFrom(typeof(ChainLeaf)));
            Assert.True(typeof(IAncestryMarker).IsAssignableFrom(typeof(MarkerImplementer)));
        }

        /// <summary>
        /// The standalone implementer's base type really is <see cref="object"/>, so nothing but the
        /// interface connects it to <see cref="ChainMiddle"/>.
        /// </summary>
        /// <remarks>
        /// The premise behind the third seed of the fact above, asserted rather than assumed. Without
        /// it, a later edit that gave <see cref="MarkerImplementer"/> a base class would leave the
        /// interface-negative assertion still passing while it had stopped being about interfaces at
        /// all. It also states the class-versus-interface asymmetry the port relies on: assignability
        /// holds between the two implementers' shared interface, while neither is in the other's base
        /// chain.
        /// </remarks>
        [Fact]
        public void TheStandaloneImplementerSharesOnlyTheInterface()
        {
            Assert.Equal(typeof(object), typeof(MarkerImplementer).BaseType);
            Assert.False(typeof(MarkerImplementer).IsSubclassOf(typeof(ChainRoot)));
            Assert.False(typeof(ChainMiddle).IsSubclassOf(typeof(MarkerImplementer)));
            Assert.False(typeof(MarkerImplementer).IsSubclassOf(typeof(ChainMiddle)));

            // The relationship that DOES exist between them, and the only one.
            Assert.True(typeof(IAncestryMarker).IsAssignableFrom(typeof(MarkerImplementer)));
            Assert.True(typeof(IAncestryMarker).IsAssignableFrom(typeof(ChainMiddle)));

            // ...and Ancestry reports neither direction between them, which is the behaviour the
            // shared interface would otherwise have created.
            Assert.False(
                Ancestry.IsAncestorByClass(nameof(MarkerImplementer), nameof(ChainMiddle)));
            Assert.False(
                Ancestry.IsAncestorByClass(nameof(ChainMiddle), nameof(MarkerImplementer)));
        }

        /// <summary>
        /// CHARACTERIZES THE PORT: an interface named as the CLASS argument has a chain of exactly one
        /// entry - itself - so it matches its own name and reaches nothing above it, not even
        /// <c>"Object"</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A .NET-only corollary of walking <see cref="System.Type.BaseType"/>, called out so it is
        /// not mistaken for a defect: <c>typeof(ISomething).BaseType</c> is <see langword="null"/>,
        /// so the loop at <c>isancestorbyclass.srf:L15</c> runs exactly once. The self-inclusive rule
        /// then makes the interface its own ancestor, while the chain that would carry it to
        /// <see cref="object"/> does not exist.
        /// </para>
        /// <para>
        /// The legacy has no interfaces at all, so there is no legacy answer being contradicted here
        /// in either direction. Asserted anyway because the asymmetry with a class - which DOES reach
        /// <c>"Object"</c> - is surprising enough that a caller meeting it deserves to find it written
        /// down. A framework interface is used alongside the fixture one so the expectation cannot
        /// drift with this repository.
        /// </para>
        /// </remarks>
        [Fact]
        public void AnInterfaceNamedAsTheClassArgumentHasAChainOfOneEntry()
        {
            string chainRootName = typeof(object).Name;

            // The premise: an interface genuinely has no base type, which is what makes the chain one
            // entry long.
            Assert.Null(typeof(IAncestryMarker).BaseType);
            Assert.Null(typeof(IDisposable).BaseType);

            // Self-inclusive, so it matches its own name...
            Assert.True(
                Ancestry.IsAncestorByClass(nameof(IAncestryMarker), nameof(IAncestryMarker)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(IDisposable), nameof(IDisposable)));

            // ...and reaches nothing at all above itself.
            Assert.False(
                Ancestry.IsAncestorByClass(nameof(IAncestryMarker), chainRootName),
                "An interface does not reach System.Object through Type.BaseType, so the chain "
                + "root's name is not in its chain - unlike every class, for which it is.");
            Assert.False(Ancestry.IsAncestorByClass(nameof(IDisposable), chainRootName));

            // The asymmetry, stated in one line: a class does reach it.
            Assert.True(Ancestry.IsAncestorByClass(nameof(ChainLeaf), chainRootName));
        }

        // ==========================================================================================
        //  SECTION D - THE COMPARISON IS ORDINAL, AND THE ANSWER DOES NOT MOVE WITH THE CULTURE
        //  ----------------------------------------------------------------------------------------
        //  C-K. THE CASING DECISION, READ OFF Ancestry.cs RATHER THAN ASSUMED. Both comparison sites
        //  in the port - the chain comparison and the class-name lookup - use
        //  StringComparison.Ordinal, so the predicate is CASE-SENSITIVE. Two reasons, from that
        //  file's own DECISION 2: PowerScript `=` on strings [isancestorbyclass.srf:L16] is a
        //  byte-for-byte comparison, so ordinal is the faithful match; and a culture-sensitive
        //  comparison would make the answer depend on the ambient culture, which the parity model
        //  cannot tolerate because a recording taken on one host would not be comparable with one
        //  taken on another.
        //
        //  It is worth knowing WHY this needed deciding at all rather than following automatically.
        //  PowerBuilder normalises object names to lower case - visible in every export header, for
        //  instance `string objectname = "pfwexception"` at
        //  ws_objects/pfw.shared.pbl.src/pfwexception.sru:L8 - while C# Type.Name preserves the
        //  declared casing. So the legacy comparison was between two already-lower-cased strings and
        //  the managed one is not, which is precisely the gap a case-insensitive port would have been
        //  reaching for. The port did not take it, and this section pins that.
        //
        //  THE ASSERTIONS DISTINGUISH THE TWO CONVENTIONS RATHER THAN PASSING UNDER EITHER. Every
        //  negative row here would answer TRUE under any ignore-case comparison, and the premise
        //  assertion states outright that the spellings involved really are case-insensitive matches,
        //  so the rows cannot go quietly vacuous after a fixture rename.
        // ==========================================================================================

        /// <summary>
        /// The premise behind every case-variant row: the two hand-written variants really are
        /// case-insensitive matches of <see cref="ChainLeaf"/>'s name, and really are not ordinal
        /// matches of it.
        /// </summary>
        /// <remarks>
        /// Without this fact a fixture rename would leave the case-variant rows in SECTION A still
        /// passing - as unresolvable-name rows - while they had stopped saying anything at all about
        /// case sensitivity. Asserting the pairing is what keeps them meaningful. The constants are
        /// the only hand-written type-name literals in the file for exactly this reason: a
        /// deliberately wrong spelling cannot come from <c>nameof</c>.
        /// </remarks>
        [Fact]
        public void TheCaseVariantsUsedByTheOrdinalRowsAreGenuineCaseVariants()
        {
            Assert.True(
                StringComparer.OrdinalIgnoreCase.Equals(ChainLeafNameLowerCased, nameof(ChainLeaf)),
                "The lower-cased constant must remain a case-insensitive match of the fixture's "
                + "name, or the ordinal rows that use it stop discriminating.");

            Assert.True(
                StringComparer.OrdinalIgnoreCase.Equals(ChainLeafNameMixedCased, nameof(ChainLeaf)),
                "The mixed-case constant must remain a case-insensitive match of the fixture's "
                + "name, or the ordinal rows that use it stop discriminating.");

            // ...and neither is an ordinal match, which is the other half of the pairing.
            Assert.False(
                StringComparer.Ordinal.Equals(ChainLeafNameLowerCased, nameof(ChainLeaf)));
            Assert.False(
                StringComparer.Ordinal.Equals(ChainLeafNameMixedCased, nameof(ChainLeaf)));
        }

        /// <summary>
        /// The class-NAME LOOKUP is ordinal too, not merely the chain comparison.
        /// [isancestorbyclass.srf:L14]
        /// </summary>
        /// <remarks>
        /// <para>
        /// The complement of the case-variant rows in SECTION A, one level down: those check the
        /// comparison made AGAINST the chain at L16, this one checks the resolution of the chain's
        /// STARTING POINT at L14. The two are separate code paths in the port and each carries its own
        /// <see cref="StringComparison"/> argument, so a change to one would not be caught by the
        /// other.
        /// </para>
        /// <para>
        /// The discrimination is exact: under a case-insensitive lookup the lower-cased spelling would
        /// resolve to <see cref="ChainLeaf"/> and every assertion below would answer
        /// <see langword="true"/>.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheClassNameLookupIsOrdinal()
        {
            // The declared spelling resolves and walks.
            Assert.True(Ancestry.IsAncestorByClass(nameof(ChainLeaf), nameof(ChainRoot)));

            // A case variant resolves to nothing, so the loop at L15 is never entered and control
            // falls through to L20's `return false`.
            Assert.False(
                Ancestry.IsAncestorByClass(ChainLeafNameLowerCased, nameof(ChainRoot)),
                "A case-insensitive lookup would resolve this to ChainLeaf and answer true.");
            Assert.False(Ancestry.IsAncestorByClass(ChainLeafNameMixedCased, nameof(ChainRoot)));
            Assert.False(
                Ancestry.IsAncestorByClass(nameof(ChainLeaf).ToUpperInvariant(), nameof(ChainRoot)));

            // Not even against its own case-variant spelling, which rules out an implementation that
            // was case-insensitive consistently on both sides and therefore self-consistent.
            Assert.False(
                Ancestry.IsAncestorByClass(ChainLeafNameLowerCased, ChainLeafNameLowerCased),
                "Both arguments lower-cased must still answer false: the seed does not resolve, so "
                + "there is no chain to compare anything against.");
        }

        /// <summary>
        /// The answer does not move with <see cref="CultureInfo.CurrentCulture"/> - demonstrated under
        /// tr-TR, whose casing rules differ from every other culture's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// C-K. This is the determinism half of the ordinal decision. An implementation that consulted
        /// the ambient culture could answer differently on two hosts, and the parity model's paired
        /// recordings would then not be comparable - so culture independence is a requirement of the
        /// characterization technique itself, not a stylistic preference.
        /// </para>
        /// <para>
        /// THE PREMISE IS ASSERTED, NOT ASSUMED. Turkish is the standard demonstration because it maps
        /// <c>i</c> to a DOTTED capital and <c>I</c> to a DOTLESS lower case, and <c>ChainLeaf</c>
        /// contains an <c>i</c>. Two premises are stated: that the culture really took effect, and
        /// that a culture-sensitive ignore-case comparison genuinely DISAGREES with an ordinal one
        /// here. Without them this test would still pass against a culture-sensitive implementation in
        /// a culture where the two happen to agree, and a reader would have no way to tell it had
        /// stopped proving anything. The <c>InvariantGlobalization</c> build property is deliberately
        /// not enabled anywhere in this repository - the port carries a localization surface plus
        /// culture-sensitive date and number parity - so ICU is live and the Turkish rules are
        /// genuinely in effect.
        /// </para>
        /// <para>
        /// The culture is restored in a <c>finally</c>. <see cref="CultureInfo.CurrentCulture"/> is
        /// per-thread and this test is synchronous, so the change is confined to this thread;
        /// restoring it keeps a pooled thread from carrying tr-TR into an unrelated test afterwards.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheComparisonIsCultureIndependentUnderTurkishCulture()
        {
            ChainLeaf leaf = new();
            string declaredName = nameof(ChainLeaf);
            string asciiUpperCased = declaredName.ToUpperInvariant();

            CultureInfo previousCulture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                // PREMISE 1 - the culture really took effect, and Turkish casing is genuinely
                // applied. U+0130 is LATIN CAPITAL LETTER I WITH DOT ABOVE, which only Turkish and
                // Azeri produce when upper-casing `i`. Under invariant globalization this would be a
                // plain ASCII "I" and the test would be vacuous, so the assertion is load-bearing.
                Assert.Equal("tr-TR", CultureInfo.CurrentCulture.Name);
                string turkishUpperCased = declaredName.ToUpper(CultureInfo.CurrentCulture);
                Assert.Contains("\u0130", turkishUpperCased, StringComparison.Ordinal);
                Assert.NotEqual(asciiUpperCased, turkishUpperCased);

                // PREMISE 2 - a culture-sensitive ignore-case comparison DISAGREES with an ordinal
                // ignore-case one for these exact spellings under this culture. So the comparison
                // mode is genuinely observable here: an implementation that consulted the culture
                // could not give the same answers as one that did not.
                Assert.True(
                    StringComparer.OrdinalIgnoreCase.Equals(asciiUpperCased, declaredName));
                Assert.False(
                    StringComparer.CurrentCultureIgnoreCase.Equals(asciiUpperCased, declaredName),
                    "The Turkish dotted/dotless i is what makes this test meaningful; if the two "
                    + "spellings compare equal culture-sensitively under tr-TR the premise has gone.");

                // THE BEHAVIOUR. Every answer is the one it is outside this block: the exact spelling
                // matches, and neither the ASCII upper-cased spelling nor the Turkish one does.
                Assert.True(Ancestry.IsAncestorByObject(leaf, declaredName));
                Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(ChainRoot)));
                Assert.False(Ancestry.IsAncestorByObject(leaf, asciiUpperCased));
                Assert.False(Ancestry.IsAncestorByObject(leaf, turkishUpperCased));

                Assert.True(Ancestry.IsAncestorByClass(declaredName, nameof(ChainRoot)));
                Assert.False(Ancestry.IsAncestorByClass(asciiUpperCased, nameof(ChainRoot)));
                Assert.False(Ancestry.IsAncestorByClass(turkishUpperCased, nameof(ChainRoot)));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }

            // AND THE SAME ANSWERS OUTSIDE THE BLOCK, which is the property the whole test exists to
            // state: the culture changed, the answers did not.
            Assert.True(Ancestry.IsAncestorByObject(leaf, declaredName));
            Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(ChainRoot)));
            Assert.False(Ancestry.IsAncestorByObject(leaf, asciiUpperCased));
            Assert.True(Ancestry.IsAncestorByClass(declaredName, nameof(ChainRoot)));
            Assert.False(Ancestry.IsAncestorByClass(asciiUpperCased, nameof(ChainRoot)));
        }

        // ==========================================================================================
        //  SECTION E - THE NAME IS UNQUALIFIED, AND WHAT THAT COSTS IN A TREE THAT HAS NAMESPACES
        //  ----------------------------------------------------------------------------------------
        //  C-K. `clsDef.name` [isancestorbyclass.srf:L16, isancestorbyobject.srf:L16] is a BARE
        //  PowerBuilder class name, because PowerBuilder has ONE FLAT GLOBAL NAMESPACE with no
        //  namespaces at all - symbol resolution is by the ordering of the library list in the target
        //  file, not by qualification. The counterpart is therefore Type.Name and NOT Type.FullName.
        //
        //  Upgrading to FullName is the most tempting change in the implementation, because FullName
        //  is "more correct" in isolation. It would be a total behavioural change: every real call
        //  site passes a bare legacy class name - "n_cst_dwsvc", "olecustomcontrol", "userobject",
        //  "menucascade" - and not one could ever match a namespaced FullName, so the predicate would
        //  answer false everywhere. The first fact below is what makes that failure mode visible from
        //  the test side.
        //
        //  THE HONEST CONSEQUENCE, PINNED RATHER THAN LEFT TO BE DISCOVERED. The .NET tree introduces
        //  namespaces where the legacy had none, so two DISTINCT types can share an unqualified name -
        //  a situation a flat namespace cannot produce, and therefore one with NO legacy answer to
        //  contradict. Under an unqualified comparison both satisfy the predicate against that name,
        //  and the port must still resolve exactly ONE of them for the class-name path, mirroring the
        //  oracle's single ClassDefinition handle. The tie-break it uses is the ordinally smallest
        //  FullName, chosen over "first match found" because neither assembly nor type enumeration
        //  order is guaranteed by the runtime and "first" would make the answer depend on load order -
        //  exactly the non-determinism a characterization recording cannot tolerate.
        //
        //  The two collision fixtures at the foot of this file exist for these facts and nothing else.
        // ==========================================================================================

        /// <summary>
        /// Names are UNQUALIFIED: a namespace-qualified name matches nothing, on either argument.
        /// [isancestorbyclass.srf:L16]
        /// </summary>
        /// <remarks>
        /// The fixture types are NESTED, so their <see cref="System.Type.FullName"/> carries a
        /// <c>+</c> separator as well as the namespace - and a nested type's
        /// <see cref="System.Type.Name"/> carries neither, which is what makes every positive row in
        /// SECTION A a statement that the port reads <c>Name</c>. Both argument positions are asserted
        /// because they are separate code paths in the port: the class argument goes through the name
        /// lookup, the parent argument through the chain comparison.
        /// </remarks>
        [Fact]
        public void OnlyUnqualifiedNamesResolveAndMatch()
        {
            ChainLeaf leaf = new();

            // FullName is null only for a few exotic type shapes, none of which a nested fixture is.
            // Coalescing rather than asserting non-null keeps this file free of the null-forgiving
            // operator; the NotEmpty assertion is what stops an empty string from making the negative
            // rows below pass for the wrong reason - an empty parent name is rejected by the guard at
            // L12, which would be a different behaviour proving nothing about qualification.
            string qualifiedLeafName = typeof(ChainLeaf).FullName ?? string.Empty;
            string qualifiedRootName = typeof(ChainRoot).FullName ?? string.Empty;

            Assert.NotEmpty(qualifiedLeafName);
            Assert.NotEmpty(qualifiedRootName);
            Assert.Contains(
                "PowerFramework.Shared.Kernel.Tests", qualifiedLeafName, StringComparison.Ordinal);
            Assert.Contains("+", qualifiedLeafName, StringComparison.Ordinal);
            Assert.NotEqual(qualifiedLeafName, typeof(ChainLeaf).Name);

            // A qualified PARENT name is compared against Type.Name and cannot match.
            Assert.False(
                Ancestry.IsAncestorByObject(leaf, qualifiedLeafName),
                "A FullName-based comparison would answer true here, and would then answer false at "
                + "all 30 legacy call sites, every one of which passes a bare class name.");
            Assert.False(Ancestry.IsAncestorByClass(nameof(ChainLeaf), qualifiedRootName));

            // A qualified CLASS name does not resolve, because the lookup compares Type.Name too.
            Assert.False(Ancestry.IsAncestorByClass(qualifiedLeafName, nameof(ChainRoot)));
            Assert.False(Ancestry.IsAncestorByClass(qualifiedLeafName, qualifiedLeafName));

            // The unqualified spelling of the very same types does match, which is what makes the
            // rows above a statement about qualification rather than about the fixture.
            Assert.True(Ancestry.IsAncestorByObject(leaf, nameof(ChainLeaf)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(ChainLeaf), nameof(ChainRoot)));
        }

        /// <summary>
        /// CHARACTERIZES THE PORT: two distinct types sharing an unqualified name BOTH satisfy the
        /// predicate against that name when the walk is seeded from an instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Not reachable in the legacy - a flat global namespace cannot hold two classes of one name -
        /// so no legacy answer is being contradicted. It is the faithful and sanctioned reading of an
        /// unqualified comparison, and it is pinned here precisely so nobody later reads it as a
        /// defect and "fixes" it to <see cref="System.Type.FullName"/>. That fix would break every
        /// real call site; see the fact above.
        /// </para>
        /// <para>
        /// The instance-seeded path is used for this fact because it involves no resolution at all:
        /// the walk starts from <c>object.ClassDefinition</c> [isancestorbyobject.srf:L14], so each
        /// instance is answered on its own chain and the tie-break of the next fact never enters.
        /// </para>
        /// </remarks>
        [Fact]
        public void TwoTypesSharingAnUnqualifiedNameBothSatisfyThePredicate()
        {
            AncestryCollisionFixtures.Alpha.CollisionFixture alpha = new();
            AncestryCollisionFixtures.Beta.CollisionFixture beta = new();
            string sharedName = nameof(AncestryCollisionFixtures.Alpha.CollisionFixture);

            // The premise: genuinely two types, genuinely one unqualified name.
            Assert.NotEqual(alpha.GetType(), beta.GetType());
            Assert.Equal(alpha.GetType().Name, beta.GetType().Name);
            Assert.Equal(sharedName, beta.GetType().Name);

            // Both answer true against the shared name, by the self-inclusive rule.
            Assert.True(Ancestry.IsAncestorByObject(alpha, sharedName));
            Assert.True(Ancestry.IsAncestorByObject(beta, sharedName));

            // ...and their chains are still distinct, because only one of them has an ancestor. This
            // is what shows the shared name widens the NAME comparison without merging the TYPES.
            string ancestorName = nameof(AncestryCollisionFixtures.Alpha.CollisionAncestor);
            Assert.True(Ancestry.IsAncestorByObject(alpha, ancestorName));
            Assert.False(Ancestry.IsAncestorByObject(beta, ancestorName));
        }

        /// <summary>
        /// CHARACTERIZES THE PORT: when two loaded types share an unqualified name the class-name
        /// lookup resolves exactly ONE of them, deterministically - the one whose
        /// <see cref="System.Type.FullName"/> is ordinally smallest. [isancestorbyclass.srf:L14]
        /// </summary>
        /// <remarks>
        /// <para>
        /// The oracle's <c>FindClassDefinition</c> yields a single handle and the port mirrors that:
        /// one type is resolved and only that one is walked. Answering "true if ANY same-named type
        /// qualifies" would replace a single resolution with a search and so widen a contract the
        /// oracle states in the singular.
        /// </para>
        /// <para>
        /// The tie-break is a rule this port DEFINES rather than inherits, because the situation has
        /// no legacy counterpart. Its whole purpose is repeatability: stopping at the first match
        /// found would make the answer depend on assembly and type enumeration order, neither of which
        /// the runtime guarantees, so the same input could answer differently across two runs of the
        /// same program. The repeated calls below are the observable form of that guarantee.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheSameNameTieBreakIsTheOrdinallySmallestFullNameAndIsRepeatable()
        {
            string sharedName = nameof(AncestryCollisionFixtures.Alpha.CollisionFixture);
            string ancestorName = nameof(AncestryCollisionFixtures.Alpha.CollisionAncestor);

            string alphaFullName =
                typeof(AncestryCollisionFixtures.Alpha.CollisionFixture).FullName ?? string.Empty;
            string betaFullName =
                typeof(AncestryCollisionFixtures.Beta.CollisionFixture).FullName ?? string.Empty;

            // The premise: two different qualified names for one unqualified name, with Alpha's the
            // ordinally smaller. If a third same-named type with a smaller FullName were ever loaded
            // this assertion is where it would be reported, rather than the behaviour below failing
            // for an unexplained reason.
            Assert.NotEmpty(alphaFullName);
            Assert.NotEmpty(betaFullName);
            Assert.NotEqual(alphaFullName, betaFullName);
            Assert.True(
                string.CompareOrdinal(alphaFullName, betaFullName) < 0,
                "The Alpha fixture must remain the ordinally smaller FullName, since that is the "
                + "tie-break the resolved type below is predicted from.");

            // THE BEHAVIOUR. Only Alpha's CollisionFixture derives from CollisionAncestor, so a true
            // here says the lookup resolved Alpha's - the tie-break winner - and walked that one.
            // Under a first-match-wins lookup this assertion would pass or fail depending on load
            // order, which is the defect the tie-break exists to remove.
            Assert.True(
                Ancestry.IsAncestorByClass(sharedName, ancestorName),
                "The ordinally smallest FullName wins the tie-break, and only that type is walked.");

            // Repeated, because determinism is the property being claimed rather than the answer.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Assert.True(Ancestry.IsAncestorByClass(sharedName, ancestorName));
                Assert.True(Ancestry.IsAncestorByClass(sharedName, sharedName));
                Assert.True(Ancestry.IsAncestorByClass(sharedName, typeof(object).Name));
            }
        }

        // ==========================================================================================
        //  SECTION F - THE GUARD ARMS. EVERY ONE ANSWERS false, AND NONE THROWS.
        //  ----------------------------------------------------------------------------------------
        //  C-B. `if cls = "" or parentCls = "" then return false` [isancestorbyclass.srf:L12] and
        //  `if Not IsValidObject(object) or parentCls = "" then return false`
        //  [isancestorbyobject.srf:L12]. Both legacy functions are declared `global function boolean`
        //  and every readable path returns true or false: bad input is ANSWERED, not rejected. There
        //  is no error path in the oracle at all.
        //
        //  This looks like missing validation and is not. Adding an ArgumentException here would turn
        //  a dispatch test that quietly takes the other branch into one that takes down its call site,
        //  which is a behavioural change dressed up as robustness. If a future reader adds argument
        //  validation, this table is the specification and the new throw is the regression.
        //
        //  ONE WIDENING, AND IT IS ANSWERABLE. The oracle tests `= ""`; the port tests
        //  string.IsNullOrEmpty, so a null arriving from a nullable-oblivious caller answers false BY
        //  DECISION rather than by accidentally surviving into a comparison. The answer is identical
        //  either way, which is what makes it a widening of the TEST and not of the BEHAVIOUR.
        // ==========================================================================================

        /// <summary>
        /// One row per guard arm of the class-name path: the class name, the parent class name, and the
        /// reason that pair answers <see langword="false"/>. [isancestorbyclass.srf:L12, L14-L15, L20]
        /// </summary>
        /// <remarks>
        /// Every row expects <see langword="false"/>, so there is no expectation column; the third
        /// column carries WHICH arm the row exercises instead, and it is surfaced in the assertion
        /// message so a failure names the arm rather than a row index. The arms are genuinely
        /// different code paths - an empty name is rejected by the guard at L12 while a blank one is a
        /// name that simply fails to resolve at L14 - and a later "tidy" from
        /// <c>IsNullOrEmpty</c> to <c>IsNullOrWhiteSpace</c> would silently merge them.
        /// </remarks>
        public static TheoryData<string?, string, string> ClassNameGuardArmRows()
        {
            TheoryData<string?, string, string> rows = [];

            //  THE EMPTY ARMS, all three combinations. [isancestorbyclass.srf:L12]
            rows.Add(string.Empty, nameof(ChainRoot), "empty class name, first conjunct of L12");
            rows.Add(nameof(ChainLeaf), string.Empty, "empty parent name, second conjunct of L12");
            rows.Add(string.Empty, string.Empty, "both empty, so both conjuncts of L12 hold");

            //  THE NULL ARMS. The oracle writes `= ""`; the port widens to IsNullOrEmpty so a null
            //  answers false by decision. Two spellings because the two arrive by different routes:
            //  an empty string from a configuration value, a null from an uninitialised reference.
            rows.Add(null, nameof(ChainRoot), "null class name, the IsNullOrEmpty widening of L12");
            rows.Add(null, string.Empty, "both arguments absent at once");

            //  BLANK IS A NAME, NOT AN ABSENCE, and it reaches false by the OTHER route: the guard at
            //  L12 lets it through because it is not empty, and it then resolves to nothing at L14 so
            //  the loop at L15 is never entered.
            rows.Add(" ", nameof(ChainRoot), "a single space is a name that resolves to nothing");
            rows.Add("\t", nameof(ChainRoot), "a tab is a name that resolves to nothing");
            rows.Add(nameof(ChainLeaf), " ", "a blank parent name is compared, never trimmed");

            //  UNRESOLVABLE NAMES. `FindClassDefinition` returns an invalid handle for an unknown
            //  class, so `do while IsValid(clsDef)` at L15 is never entered and control falls to L20.
            rows.Add(AbsentTypeName, nameof(ChainRoot), "the seed resolves to no loaded type");
            rows.Add(AbsentTypeName, AbsentTypeName, "neither name resolves to anything");
            rows.Add(
                AbsentTypeName,
                "AlsoAbsentFromThisProcess",
                "neither name resolves, spelled differently");
            rows.Add(
                nameof(ChainLeaf),
                AbsentTypeName,
                "the seed resolves; the target is absent from its chain");

            //  A QUALIFIED SEED does not resolve either, because the lookup compares Type.Name.
            //  Computed rather than written out, so a namespace move cannot leave a stale literal here.
            rows.Add(
                typeof(ChainLeaf).FullName ?? string.Empty,
                nameof(ChainRoot),
                "a namespace-qualified seed does not resolve against Type.Name");

            return rows;
        }

        /// <summary>
        /// Every guard arm of the class-name path answers <see langword="false"/> and none throws.
        /// [isancestorbyclass.srf:L12, L14-L15, L20]
        /// </summary>
        /// <param name="className">The class name for this arm, possibly absent or unresolvable.</param>
        /// <param name="parentClass">The parent class name for this arm.</param>
        /// <param name="arm">Which guard arm the row exercises, surfaced in the failure message.</param>
        [Theory]
        [MemberData(nameof(ClassNameGuardArmRows))]
        public void EveryClassNameGuardArmAnswersFalse(string? className, string parentClass, string arm)
        {
            // Reaching the assertion at all is half the claim: the oracle has no error path, so an
            // escaping exception fails here and names the arm rather than surfacing as a broken
            // dispatch somewhere downstream.
            Assert.False(Ancestry.IsAncestorByClass(className, parentClass), arm);
            Assert.False(Ancestry.IsAncestor(className, parentClass), arm);
        }

        /// <summary>
        /// A null object answers <see langword="false"/>, including when the parent name is also
        /// absent. [isancestorbyobject.srf:L12, isvalidobject.srf:L11]
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is two ported behaviours meeting rather than one. The oracle guards with
        /// <c>Not IsValidObject(object)</c>, and <c>isvalidobject.srf:L11</c> is itself
        /// <c>if IsNull(object) then return false</c> - so the observable "null answers false" is
        /// inherited from the guard the port CALLS rather than decided here. The next fact states
        /// that coupling exactly.
        /// </para>
        /// <para>
        /// OVERLOAD RESOLUTION NOTE, and the reason the casts below are not redundant:
        /// <c>IsAncestor(null, "X")</c> binds to the <c>string?</c> overload, NOT the <c>object?</c>
        /// one, because <c>string</c> is the more specific parameter type and a bare null literal
        /// converts to both. Both answer <see langword="false"/> so no caller is misled about the
        /// result, but a reader who assumed a bare null exercised the object path would be mistaken -
        /// so both are called explicitly.
        /// </para>
        /// </remarks>
        [Fact]
        public void ANullObjectAnswersFalse()
        {
            Assert.False(Ancestry.IsAncestorByObject(null, nameof(ChainRoot)));
            Assert.False(Ancestry.IsAncestorByObject(null, typeof(object).Name));

            // Both arms of the guard at L12 satisfied at once, which must still not throw.
            Assert.False(Ancestry.IsAncestorByObject(null, string.Empty));

            // The guard the oracle actually calls, answering the same way [isvalidobject.srf:L11].
            Assert.False(Predicates.IsValidObject(null));

            // Explicitly the object-shaped native overload [isancestor.srf:L7]...
            Assert.False(Ancestry.IsAncestor((object?)null, nameof(ChainRoot)));

            // ...and explicitly the string-shaped one [isancestor.srf:L8], which is what a bare null
            // would have bound to.
            Assert.False(Ancestry.IsAncestor((string?)null, nameof(ChainRoot)));
        }

        /// <summary>
        /// The object path's guard IS <see cref="Predicates.IsValidObject(object?)"/> - the one real
        /// intra-project dependency in <see cref="Ancestry"/>. [isancestorbyobject.srf:L12]
        /// </summary>
        /// <remarks>
        /// <para>
        /// C-K. The oracle guards with a call to another ported global function, so the port CALLS it
        /// rather than inlining an equivalent <c>value is null</c> test. An inline test would compile
        /// to the same thing today and still be wrong to write: <see cref="Predicates.IsValidObject"/>
        /// carries a documented substitution for PowerBuilder's <c>IsValid</c> - which reports whether
        /// a reference is LIVE rather than merely non-null - together with the decision that .NET
        /// disposal is deliberately not consulted. Duplicating the check would fork that decision into
        /// two places and let them drift, so a future refinement of what "valid" means would silently
        /// stop applying to the ancestry predicates.
        /// </para>
        /// <para>
        /// HOW THE COUPLING IS MADE OBSERVABLE FROM OUTSIDE. <c>"Object"</c> is in EVERY class's chain,
        /// because the .NET chain terminates at <see cref="object"/>. So
        /// <c>IsAncestorByObject(candidate, "Object")</c> can only be
        /// <see langword="false"/> when the GUARD rejected the candidate - the walk itself cannot
        /// produce a false for that name. Comparing that answer with
        /// <see cref="Predicates.IsValidObject(object?)"/> across a spread of candidates is therefore
        /// an exact statement that the two agree, with no access to internals required.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheObjectGuardAgreesWithPredicatesIsValidObject()
        {
            string chainRootName = typeof(object).Name;

            // A DISPOSED candidate is deliberately in the list. It is the one input on which a guard
            // that had quietly grown its OWN notion of validity - a disposal check, say - would
            // disagree with Predicates.IsValidObject, which does not consult disposal. Without it the
            // agreement below would hold for a diverged guard as readily as for a coupled one.
            MemoryStream disposedCandidate = new();
            disposedCandidate.Dispose();

            object?[] candidates =
            [
                null,
                new ChainLeaf(),
                new ChainMiddle(),
                new ChainRoot(),
                new UnrelatedFixture(),
                new MarkerImplementer(),
                new object(),
                string.Empty,
                "text",
                0,
                42,
                new int[] { 1, 2, 3 },
                disposedCandidate,
            ];

            foreach (object? candidate in candidates)
            {
                bool guardAdmitted = Predicates.IsValidObject(candidate);

                Assert.Equal(guardAdmitted, Ancestry.IsAncestorByObject(candidate, chainRootName));
                Assert.Equal(guardAdmitted, Ancestry.IsAncestor(candidate, chainRootName));
            }
        }

        /// <summary>
        /// A DISPOSED but non-null object is walked normally, because
        /// <see cref="Predicates.IsValidObject(object?)"/> does not consult disposal.
        /// [isancestorbyobject.srf:L12]
        /// </summary>
        /// <remarks>
        /// <para>
        /// A starred row of the port's behaviour table, and a behaviour INHERITED rather than chosen
        /// here: the BCL publishes no general side-effect-free query for whether an arbitrary object
        /// has been disposed, and the per-type approximations that exist do not actually mean disposal,
        /// so consulting them would make the guard answer <see langword="false"/> for objects the
        /// legacy calls valid. A disposed .NET object is in any case still a live, valid reference, so
        /// disposal is not the concept PowerBuilder's <c>IsValid</c> was testing.
        /// </para>
        /// <para>
        /// A BCL type is used rather than a purpose-built one so that the disposed state is genuinely
        /// observable - the premise assertions below read it - and so the chain being walked is fixed
        /// by the platform rather than by this repository.
        /// </para>
        /// </remarks>
        [Fact]
        public void ADisposedButNonNullObjectIsStillWalked()
        {
            MemoryStream disposed = new();
            disposed.Dispose();

            // The premise: it really is disposed, not merely constructed and abandoned.
            Assert.False(disposed.CanRead);
            Assert.False(disposed.CanWrite);
            Assert.False(disposed.CanSeek);

            // The guard admits it, which is the behaviour this fact inherits...
            Assert.True(Predicates.IsValidObject(disposed));

            // ...so the walk runs over its real chain: MemoryStream -> Stream -> MarshalByRefObject
            // -> Object.
            Assert.True(Ancestry.IsAncestorByObject(disposed, nameof(MemoryStream)));
            Assert.True(Ancestry.IsAncestorByObject(disposed, nameof(Stream)));
            Assert.True(Ancestry.IsAncestorByObject(disposed, nameof(MarshalByRefObject)));
            Assert.True(Ancestry.IsAncestorByObject(disposed, typeof(object).Name));

            // ...and still answers false for a name that is not in it, so the true answers above are
            // not a guard that admits everything.
            Assert.False(Ancestry.IsAncestorByObject(disposed, nameof(ChainLeaf)));
        }

        // ==========================================================================================
        //  SECTION G - THE DELEGATION THAT HOLDS THE NATIVE INFERENCE IN PLACE
        //  ----------------------------------------------------------------------------------------
        //  C-K. `isancestor.srf:L3` declares the whole function object `native "pfw.dll"`, the file
        //  contains NO body, and no C++ source for that binary exists anywhere in the repository - so
        //  its behaviour cannot be read, only INFERRED. The inference is unusually well grounded,
        //  because its two prototypes mirror the two readable siblings exactly:
        //
        //      isancestor.srf:L7   (readonly powerobject object, readonly string parentcls)
        //      isancestorbyobject.srf:L7   the same shape, WITH a readable body
        //
        //      isancestor.srf:L8   (readonly string cls, readonly string parentcls)
        //      isancestorbyclass.srf:L7    the same shape, WITH a readable body
        //
        //  The port therefore implements the two IsAncestor overloads as delegations, and these
        //  theories are what keep that expressible claim honest: if the two families ever diverge,
        //  someone has given IsAncestor a body of its own and the inference is no longer stated
        //  anywhere. An AGREEMENT theory is the honest way to cover a member whose legacy body is
        //  unobservable - it asserts the relationship the port actually claims, and claims nothing
        //  about what the closed binary does.
        //
        //  WHAT IS NOT CLAIMED: that the native body is KNOWN to behave this way. It is not. Only that
        //  the two readable bodies are the best available specification for it, and that the port does
        //  not diverge from them.
        //
        //  The second theory adds the SEED-EQUIVALENCE claim: an instance and its own type name are
        //  two seeds for one walk [isancestorbyobject.srf:L14 against isancestorbyclass.srf:L14], and
        //  the port expresses the walk exactly once. A second copy added for either path would show up
        //  here as soon as the two drifted.
        // ==========================================================================================

        /// <summary>
        /// The full cross product of class-name inputs and parent names, for the string-shaped
        /// overload pair. [isancestor.srf:L8 against isancestorbyclass.srf:L7]
        /// </summary>
        public static TheoryData<string?, string> StringOverloadAgreementRows()
        {
            TheoryData<string?, string> rows = [];

            // Deliberately mixed: real fixture names, an interface name, case variants, a blank, an
            // empty, a null, an absent name, and a qualified name. Agreement must hold on inputs the
            // guard rejects just as much as on inputs the walk answers.
            string?[] classNames =
            [
                null,
                string.Empty,
                " ",
                nameof(ChainLeaf),
                nameof(ChainMiddle),
                nameof(ChainRoot),
                nameof(UnrelatedFixture),
                nameof(MarkerImplementer),
                nameof(IAncestryMarker),
                ChainLeafNameLowerCased,
                AbsentTypeName,
                typeof(ChainLeaf).FullName ?? string.Empty,
            ];

            string[] parents =
            [
                string.Empty,
                " ",
                nameof(ChainLeaf),
                nameof(ChainMiddle),
                nameof(ChainRoot),
                nameof(UnrelatedFixture),
                nameof(MarkerImplementer),
                nameof(IAncestryMarker),
                typeof(object).Name,
                AbsentTypeName,
            ];

            foreach (string? className in classNames)
            {
                foreach (string parent in parents)
                {
                    rows.Add(className, parent);
                }
            }

            return rows;
        }

        /// <summary>
        /// The native string-shaped overload agrees with the readable sibling it was inferred from, on
        /// every input. [isancestor.srf:L8, isancestorbyclass.srf:L7]
        /// </summary>
        /// <param name="className">The class-name seed, possibly absent or unresolvable.</param>
        /// <param name="parentClass">The parent class name being looked for.</param>
        [Theory]
        [MemberData(nameof(StringOverloadAgreementRows))]
        public void TheNativeStringOverloadAgreesWithItsReadableSibling(
            string? className,
            string parentClass)
        {
            Assert.Equal(
                Ancestry.IsAncestorByClass(className, parentClass),
                Ancestry.IsAncestor(className, parentClass));
        }

        /// <summary>
        /// The cross product of instantiable fixtures and parent names, for the object-shaped overload
        /// pair and for seed equivalence. [isancestor.srf:L7 against isancestorbyobject.srf:L7]
        /// </summary>
        public static TheoryData<string, string> ObjectOverloadAgreementRows()
        {
            TheoryData<string, string> rows = [];

            string[] fixtureNames =
            [
                nameof(ChainLeaf),
                nameof(ChainMiddle),
                nameof(ChainRoot),
                nameof(UnrelatedFixture),
                nameof(MarkerImplementer),
            ];

            string[] parents =
            [
                string.Empty,
                nameof(ChainLeaf),
                nameof(ChainMiddle),
                nameof(ChainRoot),
                nameof(UnrelatedFixture),
                nameof(MarkerImplementer),
                nameof(IAncestryMarker),
                typeof(object).Name,
                ChainLeafNameLowerCased,
                AbsentTypeName,
            ];

            foreach (string fixtureName in fixtureNames)
            {
                foreach (string parent in parents)
                {
                    rows.Add(fixtureName, parent);
                }
            }

            return rows;
        }

        /// <summary>
        /// The native object-shaped overload agrees with the readable sibling it was inferred from, and
        /// seeding the walk from an instance agrees with seeding it from that instance's type name.
        /// [isancestor.srf:L7, isancestorbyobject.srf:L14, isancestorbyclass.srf:L14]
        /// </summary>
        /// <param name="fixtureName">The name of the fixture to seed from, by instance and by name.</param>
        /// <param name="parentClass">The parent class name being looked for.</param>
        [Theory]
        [MemberData(nameof(ObjectOverloadAgreementRows))]
        public void TheNativeObjectOverloadAgreesAndBothSeedsAgree(string fixtureName, string parentClass)
        {
            object instance = CreateFixture(fixtureName);

            bool fromInstance = Ancestry.IsAncestorByObject(instance, parentClass);

            // The delegation claim.
            Assert.Equal(fromInstance, Ancestry.IsAncestor(instance, parentClass));

            // The seed-equivalence claim: one walk, two seeds. The fixture names are unique across
            // every loaded assembly, so the name resolves to the instance's own type and the two paths
            // are asking the identical question.
            Assert.Equal(fromInstance, Ancestry.IsAncestorByClass(fixtureName, parentClass));
            Assert.Equal(fromInstance, Ancestry.IsAncestor(fixtureName, parentClass));
        }

        // ==========================================================================================
        //  SECTION H - PROPERTIES OF THE SUBSTITUTION ITSELF
        //  ----------------------------------------------------------------------------------------
        //  These are consequences of replacing a FLAT GLOBAL NAMESPACE with loaded .NET assemblies and
        //  PowerBuilder's ClassDefinition with System.Type. They contradict no legacy answer, because
        //  none of these situations can arise in a namespace that holds at most one class of any name
        //  and a language that has neither generics nor boxing. They are asserted so that a caller who
        //  meets one finds it written down rather than concluding the predicate is broken.
        // ==========================================================================================

        /// <summary>
        /// CHARACTERIZES THE PORT: the class-name lookup reaches types in ANY already-loaded assembly,
        /// not just this one. [isancestorbyclass.srf:L14]
        /// </summary>
        /// <remarks>
        /// Framework types are used deliberately: they prove the SCOPE of the lookup is the whole
        /// application domain, and their inheritance chains are fixed by the platform, so the expected
        /// answers cannot drift with this repository. The honest limit of that scope is the next fact.
        /// </remarks>
        [Fact]
        public void TheLookupReachesTypesInAnyLoadedAssembly()
        {
            string chainRootName = typeof(object).Name;

            // One step.
            Assert.True(Ancestry.IsAncestorByClass(nameof(String), chainRootName));

            // Four steps: ArgumentNullException -> ArgumentException -> SystemException -> Exception
            // -> Object, which is a deeper chain than any fixture in this file.
            Assert.True(Ancestry.IsAncestorByClass(nameof(ArgumentNullException), nameof(ArgumentException)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(ArgumentNullException), nameof(SystemException)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(ArgumentNullException), nameof(Exception)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(ArgumentNullException), chainRootName));

            // ...and the same chain is still directional.
            Assert.False(Ancestry.IsAncestorByClass(nameof(ArgumentException), nameof(ArgumentNullException)));

            // The same chain reached from an instance, which is the path all 30 legacy call sites use.
            Assert.True(
                Ancestry.IsAncestorByObject(new ArgumentNullException("parameter"), nameof(Exception)));
        }

        /// <summary>
        /// Resolution survives a name it cannot resolve and keeps working afterwards - and this fact
        /// records the ONE arm of the implementation this suite cannot reach, rather than leaving it as
        /// an unexplained coverage hole. [isancestorbyclass.srf:L14-L15, L20]
        /// </summary>
        /// <remarks>
        /// <para>
        /// The port's name lookup wraps <c>Assembly.GetTypes</c> in a filtered catch over
        /// <see cref="System.Reflection.ReflectionTypeLoadException"/> and five sibling load failures,
        /// and SKIPS the offending assembly so the scan continues over the rest. Reaching that arm
        /// needs a loaded, NON-dynamic assembly whose metadata references something absent from disk,
        /// which cannot be produced from inside a test without emitting a purpose-built broken assembly
        /// to the filesystem and loading it. Dynamic assemblies do not qualify: the implementation
        /// skips them before the call, deliberately, because enumerating them is not universally
        /// supported.
        /// </para>
        /// <para>
        /// SO THE ARM IS UNCOVERED, DELIBERATELY, WITH THE REASON RECORDED HERE. Measured rather than
        /// guessed: it is the only uncovered region of <see cref="Ancestry"/>, and the assembly's
        /// overall line coverage stays far above the gate without it. What this fact asserts instead is
        /// the OBSERVABLE contract the arm exists to provide - a name that cannot be resolved answers
        /// <see langword="false"/>, and resolution keeps working for everything else immediately
        /// afterwards, which is what "the scan continues" means to a caller.
        /// </para>
        /// <para>
        /// This is stated as a limitation and NOT as proof. It must not be cited as evidence that the
        /// catch behaves correctly under a genuinely broken assembly; only a run in an environment that
        /// has one would establish that.
        /// </para>
        /// </remarks>
        [Fact]
        public void ResolutionSurvivesAnUnresolvableNameAndKeepsWorkingAfterwards()
        {
            string chainRootName = typeof(object).Name;

            Assert.False(Ancestry.IsAncestorByClass(AbsentTypeName, chainRootName));

            // The scan ran to completion rather than aborting part way, so names it SHOULD find are
            // still found immediately afterwards - both a fixture type in this assembly and a
            // framework type in another one.
            Assert.True(Ancestry.IsAncestorByClass(nameof(ChainLeaf), nameof(ChainRoot)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(String), chainRootName));

            // ...and the order of the two does not matter, which is the other half of "continues".
            Assert.True(Ancestry.IsAncestorByClass(nameof(ChainLeaf), chainRootName));
            Assert.False(Ancestry.IsAncestorByClass(AbsentTypeName, nameof(ChainRoot)));
            Assert.True(Ancestry.IsAncestorByClass(nameof(String), chainRootName));
        }

        /// <summary>
        /// CHARACTERIZES THE PORT: a boxed value type is walked as its own type, so it reaches
        /// <c>ValueType</c> and <c>Object</c>. [isancestorbyobject.srf:L14]
        /// </summary>
        /// <remarks>
        /// The oracle's parameter is <c>powerobject</c>, which a PowerScript scalar is not, so this
        /// combination is not reachable in the legacy at all. The port's parameter is <c>object?</c> -
        /// which is the faithful rendering of <c>any</c> in the refactor's type map - so a boxed scalar
        /// CAN arrive, and answering it from its runtime type is the only coherent reading.
        /// </remarks>
        [Fact]
        public void ABoxedValueTypeIsWalkedAsItsOwnType()
        {
            object boxed = 42;

            Assert.True(Ancestry.IsAncestorByObject(boxed, nameof(Int32)));
            Assert.True(Ancestry.IsAncestorByObject(boxed, nameof(ValueType)));
            Assert.True(Ancestry.IsAncestorByObject(boxed, typeof(object).Name));

            // ...and not a name from some other chain, so the trues above are a walk and not a
            // permissive guard.
            Assert.False(Ancestry.IsAncestorByObject(boxed, nameof(String)));
            Assert.False(Ancestry.IsAncestorByObject(boxed, nameof(ChainLeaf)));

            // The same answers by name, so the boxed case is not a special path.
            Assert.True(Ancestry.IsAncestorByClass(nameof(Int32), nameof(ValueType)));
            Assert.False(Ancestry.IsAncestorByClass(nameof(Int32), nameof(String)));
        }

        /// <summary>
        /// CHARACTERIZES THE PORT: a constructed generic type's <see cref="System.Type.Name"/> carries
        /// the runtime's arity suffix, so <c>"List"</c> does not resolve while <c>"List`1"</c> does.
        /// </summary>
        /// <remarks>
        /// PowerBuilder has no generics, so the legacy is never asked this and no legacy answer is being
        /// contradicted. It is asserted because the backtick spelling is genuinely surprising, and a
        /// caller passing <c>"List"</c> and getting <see langword="false"/> back deserves to find the
        /// reason recorded rather than concluding the predicate is broken.
        /// </remarks>
        [Fact]
        public void AGenericTypeNameCarriesItsAritySuffix()
        {
            // The premise, so the expectation is derived from the runtime rather than hard-coded.
            string genericName = typeof(List<int>).Name;
            Assert.Contains("`", genericName, StringComparison.Ordinal);
            Assert.DoesNotContain("`", nameof(List<int>), StringComparison.Ordinal);

            Assert.False(Ancestry.IsAncestorByClass(nameof(List<int>), typeof(object).Name));
            Assert.True(Ancestry.IsAncestorByClass(genericName, typeof(object).Name));

            // ...and from an instance, where the runtime type supplies the same spelling.
            Assert.True(Ancestry.IsAncestorByObject(new List<int>(), genericName));
            Assert.False(Ancestry.IsAncestorByObject(new List<int>(), nameof(List<int>)));
        }

        /// <summary>
        /// No combination of absent, blank, unresolvable and well-formed arguments throws, on any of the
        /// four members. [isancestorbyclass.srf:L12, L20, isancestorbyobject.srf:L12, L20]
        /// </summary>
        /// <remarks>
        /// The individual facts and theories above each cover one arm; what a caller relies on is that
        /// NO combination escapes, because a dispatch test that threw would take down its call site
        /// instead of taking the other branch. Every answer is collected and the collection's size is
        /// asserted against the number of calls made, so the claim is that every call RETURNED rather
        /// than that some loop completed - an escaping exception fails at the call that threw, and a
        /// short collection would fail here.
        /// </remarks>
        [Fact]
        public void NoCombinationOfArgumentsThrows()
        {
            string?[] classNames =
            [
                null,
                string.Empty,
                " ",
                "\t",
                nameof(ChainLeaf),
                ChainLeafNameLowerCased,
                nameof(IAncestryMarker),
                AbsentTypeName,
            ];

            object?[] instances =
            [
                null,
                new ChainLeaf(),
                new UnrelatedFixture(),
                new MarkerImplementer(),
                string.Empty,
                0,
                new object(),
            ];

            // Parents are non-nullable, which is the declared contract. A null parent is outside it and
            // cannot be written by a nullable-aware caller, so it is exercised by the next fact through
            // reflection instead of by a null-forgiving operator here.
            string[] parents =
            [
                string.Empty,
                " ",
                nameof(ChainLeaf),
                nameof(ChainRoot),
                nameof(IAncestryMarker),
                typeof(object).Name,
                AbsentTypeName,
            ];

            List<bool> answers = [];

            foreach (string parent in parents)
            {
                foreach (string? className in classNames)
                {
                    answers.Add(Ancestry.IsAncestorByClass(className, parent));
                    answers.Add(Ancestry.IsAncestor(className, parent));
                }

                foreach (object? instance in instances)
                {
                    answers.Add(Ancestry.IsAncestorByObject(instance, parent));
                    answers.Add(Ancestry.IsAncestor(instance, parent));
                }
            }

            int expectedCallCount = parents.Length * 2 * (classNames.Length + instances.Length);
            Assert.Equal(expectedCallCount, answers.Count);
        }

        /// <summary>
        /// A null PARENT name answers <see langword="false"/> rather than throwing, on all four members
        /// - reached through reflection because a nullable-aware caller cannot write the call.
        /// [isancestorbyclass.srf:L12, isancestorbyobject.srf:L12]
        /// </summary>
        /// <remarks>
        /// <para>
        /// C-H. The parent parameter is declared non-nullable, so passing a null needs either a
        /// null-forgiving operator or a nullable-oblivious caller. This file uses no
        /// null-forgiving operator anywhere, and reflection is the honest model of the second case: it
        /// is how a null actually arrives at a non-nullable parameter at run time, from a caller
        /// compiled without nullable checks or from a dynamically bound call.
        /// </para>
        /// <para>
        /// The arm is worth reaching. The oracle tests <c>parentCls = ""</c>; the port widened that to
        /// <c>string.IsNullOrEmpty</c> precisely so a null answers <see langword="false"/> by decision,
        /// and without this fact the null half of that widening would be unexercised.
        /// </para>
        /// </remarks>
        [Fact]
        public void ANullParentClassAnswersFalseForANullableObliviousCaller()
        {
            MethodInfo[] members = typeof(Ancestry)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.NotEmpty(members);

            foreach (MethodInfo member in members)
            {
                ParameterInfo[] parameters = member.GetParameters();

                Assert.Equal(2, parameters.Length);
                Assert.Equal(typeof(string), parameters[1].ParameterType);

                // The first argument is shaped to whichever overload this is: a name for the two
                // string-shaped members, an instance for the two object-shaped ones.
                object firstArgument = parameters[0].ParameterType == typeof(string)
                    ? nameof(ChainLeaf)
                    : new ChainLeaf();

                bool answer = Assert.IsType<bool>(member.Invoke(null, [firstArgument, null]));

                Assert.False(
                    answer,
                    $"{member.Name} must answer false for a null parent class rather than throwing.");
            }
        }

        /// <summary>
        /// The public surface is exactly the four legacy entry points - no more, no fewer.
        /// [isancestor.srf:L7-L8, isancestorbyclass.srf:L7, isancestorbyobject.srf:L7]
        /// </summary>
        /// <remarks>
        /// <para>
        /// C-B. Four legacy prototypes become four members, one for one. The port deliberately offers
        /// no <c>IsAncestor(Type, string)</c> convenience overload, no generic
        /// <c>IsAncestor&lt;T&gt;</c>, no inverse and no params array of candidate names, on the
        /// grounds that surface beyond the legacy is not to be added. This fact is what makes that
        /// absence auditable: an added member fails here and has to be justified against the oracle
        /// rather than slipping in as a convenience.
        /// </para>
        /// <para>
        /// It also pins the shape every other test in this file assumes - two arguments, the second a
        /// non-nullable string, all static, all returning bool - so the reflection-driven fact above
        /// cannot silently start skipping a member.
        /// </para>
        /// </remarks>
        [Fact]
        public void ThePublicSurfaceIsExactlyTheFourLegacyEntryPoints()
        {
            MethodInfo[] members = typeof(Ancestry)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.Equal(4, members.Length);

            // The exact roster, compared as a sorted list so the assertion names what is missing or
            // extra rather than only reporting a count. Two IsAncestor entries because the native
            // function declares two prototypes [isancestor.srf:L7-L8]; one each for the two readable
            // siblings [isancestorbyclass.srf:L7, isancestorbyobject.srf:L7].
            string[] expectedMemberNames =
            [
                nameof(Ancestry.IsAncestor),
                nameof(Ancestry.IsAncestor),
                nameof(Ancestry.IsAncestorByClass),
                nameof(Ancestry.IsAncestorByObject),
            ];

            string[] actualMemberNames = [.. members.Select(member => member.Name).Order()];

            Assert.Equal(expectedMemberNames, actualMemberNames);

            foreach (MethodInfo member in members)
            {
                Assert.Equal(typeof(bool), member.ReturnType);
                Assert.True(member.IsStatic);

                ParameterInfo[] parameters = member.GetParameters();
                Assert.Equal(2, parameters.Length);
                Assert.Equal(typeof(string), parameters[1].ParameterType);
            }

            // A static class, so there is no instance surface to reason about either.
            Assert.True(typeof(Ancestry).IsAbstract);
            Assert.True(typeof(Ancestry).IsSealed);
            Assert.Empty(typeof(Ancestry).GetProperties(BindingFlags.Public | BindingFlags.Static));
            Assert.Empty(typeof(Ancestry).GetFields(BindingFlags.Public | BindingFlags.Static));
        }
    }

    // ==============================================================================================
    //  THE COLLISION FIXTURES - TWO TYPES, ONE UNQUALIFIED NAME, TWO NAMESPACES
    //  --------------------------------------------------------------------------------------------
    //  Declared OUTSIDE AncestryTests, in two sibling namespaces, because that is the only way to give
    //  two distinct types the same Type.Name and different Type.FullName - which is the exact
    //  situation SECTION E pins and the reason this file uses a block-scoped namespace.
    //
    //  The situation itself is new to the .NET tree: PowerBuilder's one flat global namespace cannot
    //  hold two classes of one name, so there is no legacy answer here to preserve. What the port
    //  DEFINES is that the class-name lookup still resolves exactly one type - mirroring the oracle's
    //  single ClassDefinition handle at isancestorbyclass.srf:L14 - and that which one it picks is
    //  deterministic rather than dependent on assembly load order.
    //
    //  THE ORDINAL ORDER OF THE TWO NAMESPACE NAMES IS LOAD-BEARING. "Alpha" sorts before "Beta"
    //  ordinally, so Alpha's fixture is the tie-break winner and is the one the lookup walks. Only
    //  Alpha's derives from CollisionAncestor, which is what makes the winner observable from outside.
    //  Renaming either namespace to change that order would invert the prediction; the test asserts the
    //  ordering as a premise so such a rename fails loudly instead of quietly.
    //
    //  `internal` rather than `private`, because a namespace cannot hold a private type; and both are
    //  visible to Assembly.GetTypes, which is what the port's lookup enumerates.
    // ==============================================================================================
    namespace AncestryCollisionFixtures.Alpha
    {
        /// <summary>
        /// The ancestor that only the Alpha fixture has, which is how the tie-break winner is made
        /// observable without reaching into <see cref="Ancestry"/>'s internals.
        /// </summary>
        internal class CollisionAncestor;

        /// <summary>
        /// One of the two types sharing the unqualified name <c>CollisionFixture</c>. This one has an
        /// ancestor, and its <see cref="System.Type.FullName"/> is the ordinally smaller of the two, so
        /// it is the type the class-name lookup resolves.
        /// </summary>
        internal sealed class CollisionFixture : CollisionAncestor;
    }

    namespace AncestryCollisionFixtures.Beta
    {
        /// <summary>
        /// The other type sharing the unqualified name <c>CollisionFixture</c>. Its base type is
        /// <see cref="object"/>, so it reaches no <c>CollisionAncestor</c> - which is what distinguishes
        /// it from the Alpha fixture when the walk is seeded from an instance.
        /// </summary>
        internal sealed class CollisionFixture;
    }
}
