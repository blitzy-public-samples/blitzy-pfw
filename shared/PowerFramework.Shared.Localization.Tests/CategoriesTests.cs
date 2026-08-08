// ==============================================================================================
//  CategoriesTests - the derivation suite for the two PowerFramework localization categories
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.Categories
//                    (shared/PowerFramework.Shared.Localization/Categories.cs)
//  BEHAVIOURAL       ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17, together with
//  ORACLE            the base those two lines are offset from,
//                    ws_objects/pfw.shared.pbl.src/enums.sru:L123. Both files are READ ONLY, and
//                    together they are the ONLY specification for these values: the estate ships no
//                    w_test_i18n characterization window, so the declarations ARE the spec in full
//                    and the [file:Lnnn] locator on every table row below is the entire evidence
//                    trail for that row.
//
//  WHAT THIS SUITE PUTS UNDER TEST IS A DERIVATION, NOT TWO NUMBERS
//  --------------------------------------------------------------------------------------------
//  The oracle declares both categories as arithmetic over a base declared in a DIFFERENT object:
//
//      ne_cst_i18n.sru:L16    Constant Long CAT_MSGBOX = Enums.I18N_CAT_CUSTOM + 1
//      ne_cst_i18n.sru:L17    Constant Long CAT_DWSVC  = Enums.I18N_CAT_CUSTOM + 2
//
//  Categories.cs transcribes that arithmetic rather than evaluating it, and this suite is the
//  mechanism that file's own DECISION 2 names when it delegates the checking of the derivation to
//  "the sibling test project". So the property under test is the RELATIONSHIP between the two
//  categories and Enums.I18N_CAT_CUSTOM - that they are the first and second categories a consumer
//  may define above the framework's own five - and not the integers that relationship happens to
//  produce on today's base.
//
//  C-B - WHY EVERY EXPECTATION IS ARITHMETIC AND NEVER A LITERAL
//  --------------------------------------------------------------------------------------------
//  This is the whole reason the suite exists in this shape, so it is stated rather than left to be
//  inferred: DO NOT SIMPLIFY THESE ASSERTIONS INTO LITERAL COMPARISONS. Every expected value below
//  is computed as `Enums.I18N_CAT_CUSTOM + offset`. A hand-written integer would be a WEAKER test in
//  two distinct ways, and both matter:
//
//      1. It would pass against a hardcoded implementation. If Categories.cs abandoned the
//         arithmetic and declared the evaluated integers directly, a literal expectation would
//         agree with it and report success - so the suite would certify precisely the substitution
//         the transformation plan forbids for that file. The arithmetic form does not: move
//         Enums.I18N_CAT_CUSTOM and a correctly derived Categories moves with it and stays green,
//         while a hardcoded one immediately fails. That asymmetry IS the behaviour being preserved.
//      2. It would pin the wrong thing. Two hand-copied numbers can only ever be compared with
//         themselves. Re-deriving the expectation from the base means a transcription slip in either
//         operand cannot cancel out, because the base is independently pinned by the kernel's own
//         suite and is annotated in Enums.cs as frozen precisely because these two offsets depend on
//         it.
//
//  A COROLLARY THAT IS EASY TO GET WRONG: THE EVALUATED VALUES ARE NOT WRITTEN DOWN HERE AT ALL.
//  Not in an assertion, not in a table, and not in a comment dressed up as an expected value. They
//  are documented once, on each member of Categories.cs, which is the right place for them; a
//  reader who wants a number opens the declaration. Repeating them here would create a second
//  unenforced copy and, worse, would sit in this file looking exactly like an expectation waiting to
//  be promoted into one. The omission is deliberate - please do not "complete" it.
//
//  AAP 0.4.5.3 - THE IDENTIFIER SPELLINGS ARE PART OF THE CONTRACT, AND THIS SUITE ENFORCES THEM
//  --------------------------------------------------------------------------------------------
//  CAT_MSGBOX and CAT_DWSVC keep their legacy SCREAMING_SNAKE spelling in deliberate defiance of
//  .NET naming convention, because those exact identifier strings travel in serialized payloads on
//  the new service contracts, in log records, and in the characterization recordings that are the
//  parity evidence for the whole migration. CAT_DWSVC is reached from inside the DataWindow
//  item-validation path, so it appears in recorded validation output; renaming it would not restyle
//  a symbol, it would silently invalidate every stored comparison that mentions it, and a
//  golden-master suite cannot report a comparison it can no longer make.
//
//  The reflection theories below turn that requirement from a convention into something the build
//  enforces: they look each member up by its exact, case-sensitive, underscore-bearing spelling. So
//  a rename fails twice over, which is intentional - once at COMPILE time, because the arithmetic
//  theories reference the members directly, and once at RUN time, because the metadata lookup finds
//  no member under the transcribed name.
//
//  IF A NAMING ANALYZER EVER COMPLAINS ABOUT THOSE SPELLINGS, THE FIX IS NOT A RENAME. It is the
//  scoped suppression that already exists in the repository-root .editorconfig for
//  shared/PowerFramework.Shared.Localization/Categories.cs, which turns CA1707 and IDE1006 off for
//  that one declaring file. Nothing needs adding here, and nothing may be: CA1707 reports
//  DECLARATIONS and never uses, and this file only ever CONSUMES the constants - as identifier
//  strings in `string` values and as direct member reads - so it raises nothing.
//
//  C-H - WARNINGS ARE ERRORS HERE TOO
//  --------------------------------------------------------------------------------------------
//  Directory.Build.props sets TreatWarningsAsErrors, Nullable and EnableNETAnalyzers for every
//  project including this one, and that covers analyzer diagnostics as well as compiler ones. Two
//  consequences are visible in the code below. Every identifier this file DECLARES - class, field,
//  member-data provider, test method, parameter, tuple element and local - is conventional
//  PascalCase or camelCase with no underscore, so no naming diagnostic has anything to report;
//  preserved spellings appear only inside string literals. And there is deliberately no
//  `#pragma warning disable`, no `NoWarn` and no suppression of any kind in this file or its
//  project: obeying the analyzers is cheaper than silencing them, and a suppression here would be
//  the seam through which the next test file quietly adopts non-conventional names.
//
//  Both members of the catalogue are exercised - each is read directly, each is resolved by its
//  spelling through metadata, and each is asserted for constness - so removing either one fails this
//  suite by name rather than merely lowering a number.
//
//  A COVERAGE FACT THAT WILL OTHERWISE LOOK LIKE A GAP, RECORDED SO NOBODY "FIXES" IT: Categories
//  DOES NOT APPEAR IN A COVERAGE REPORT AT ALL, AND NO TEST CAN MAKE IT APPEAR. A C# `const` emits
//  metadata and no IL - no method body, no sequence point - so a type consisting solely of constants
//  has nothing for a line-coverage instrument to attribute a line to. Consumers do not help either,
//  for the same reason the read-path note below gives: a constant is inlined into the caller, so not
//  even this assembly contains an instruction that reads one.
//
//  This was verified rather than assumed, and it is uniform across the repository's three constant
//  catalogues. A coverage run over the kernel's suite - 1,303 lines of tests asserting 156 constants
//  exhaustively - reports Bits, Exceptions, Formatting, PfwException, Predicates and Text, and omits
//  RetCode and Enums entirely, exactly as this project's run omits Categories. The right response is
//  to leave both alone: adding a member to Categories purely to give an instrument something to
//  measure would put a metric ahead of the port, and that file's own "WHAT IS DELIBERATELY ABSENT"
//  decision already rules out every such addition by name. (Cited by title rather than by its
//  number, so that no digit in this file can be mistaken for an expected value - see the corollary
//  under C-B above.)
//
//  C-C - THE LEGACY TREE IS READ ONLY AND IS NEVER TOUCHED AT RUNTIME
//  --------------------------------------------------------------------------------------------
//  ne_cst_i18n.sru and enums.sru are referenced by line number in comments only. This file opens no
//  path under ws_objects/**, reads no file at all, and touches neither the translation resource that
//  this project links into its output directory nor any provider that would load it. Every assertion
//  here is constant arithmetic plus reflection over types that are already loaded - no I/O, no
//  clock, no environment, no network, no host, no database. That determinism is not a nicety: these
//  expectations are the baseline the characterization recordings are compared against, and a suite
//  that could disagree with itself between two runs would be useless as a parity oracle.
//
//  AAP 0.6.7 - TABLE DRIVEN THEORIES WITH MEMBER DATA
//  --------------------------------------------------------------------------------------------
//  Parity suites in this refactor are expressed as theories with member data, and a Fact is used
//  only where a theory would carry a single row - which here is exactly the two whole-catalogue
//  audits and the pair-shaped relationship between the two categories. Member data makes a failure
//  name the offending category instead of reporting an anonymous number mismatch.
//
//  Following the sibling suites, the tables hold identifier spellings as STRING LITERALS rather than
//  as `nameof(...)` expressions. A `nameof` table is derived from the implementation, so an audit
//  built on it would compare the implementation with itself; a string literal is an independent
//  transcription of the oracle, which is what makes the inventory audit a genuine three-way check of
//  oracle spelling, declared spelling and declared value.
//
//  TWO INDEPENDENT READ PATHS, AND WHY BOTH ARE ASSERTED
//  --------------------------------------------------------------------------------------------
//  A C# `const` is INLINED into every assembly that reads it, so `Categories.CAT_DWSVC` written in
//  this file is baked into this test assembly at ITS compile time rather than fetched from the
//  library at run time. That is worth knowing here, because it means a value assertion written only
//  that way proves something narrower than it appears to: it compares two numbers that were both
//  frozen into the same binary.
//
//  So each category is checked twice, by two paths that can disagree:
//
//      inlined path      `Categories.CAT_DWSVC` and `Enums.I18N_CAT_CUSTOM` as the compiler baked
//                        them into this assembly - what a consumer compiled against these libraries
//                        actually gets.
//      metadata path     the same two constants read back out of the COMPILED METADATA of
//                        PowerFramework.Shared.Localization and PowerFramework.Shared.Kernel by
//                        reflection - what the shipped libraries actually declare.
//
//  Agreement between them is the property that matters, and disagreement is a real failure mode
//  rather than a hypothetical one: it is exactly what a stale reference or a mismatched assembly
//  version produces, and inlining is what makes that failure silent everywhere else.
// ==============================================================================================

using System.Reflection;

using PowerFramework.Shared.Kernel;

using Xunit;

// NOTE ON IMPORTS. `using PowerFramework.Shared.Localization;` is deliberately absent: this
// namespace is nested inside it, so C# name lookup walks outward and resolves `Categories` from the
// project reference without any directive, and adding one would be an unnecessary using directive in
// a project that treats warnings as errors. `PowerFramework.Shared.Kernel` IS required and IS used -
// it is not an enclosing namespace of this one, and `Enums` lives there; it arrives transitively
// through the single project reference this project declares. `System.Reflection` supplies
// BindingFlags and FieldInfo for the metadata path, `Xunit` supplies the attributes and assertions,
// and System.Collections.Generic plus System.Linq arrive through the SDK's implicit usings. Those
// are the only dependencies this suite has.
namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Characterization tests for <see cref="Categories"/>, the two localization categories ported from
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// The subject of the suite is the DERIVATION the oracle declares - each category is an offset from
/// <see cref="Enums.I18N_CAT_CUSTOM"/> - rather than the integers that derivation currently
/// produces. Every expectation is therefore computed from that base, so the suite stays green when
/// the base moves and a correctly derived catalogue moves with it, and fails when a catalogue
/// abandons the arithmetic for hand-written integers. The file header states the reasoning in full
/// and explains why the evaluated values are deliberately written nowhere in this file.
/// </para>
/// <para>
/// Every test here is deterministic and side-effect free: constant arithmetic and reflection over
/// already-loaded types, with no file, clock, environment, network or database access of any kind.
/// </para>
/// </remarks>
public sealed class CategoriesTests
{
    // ------------------------------------------------------------------------------------------
    //  Audit constant.
    //
    //  The number of categories the oracle declares, counted from it rather than assumed:
    //  ne_cst_i18n.sru declares two and the estate defines no others. It is asserted rather than
    //  merely documented, so a third category added to Categories.cs without a row in the table
    //  below fails the inventory audit instead of slipping through untested.
    // ------------------------------------------------------------------------------------------
    private const int ExpectedFrameworkCategoryCount = 2;

    /// <summary>
    /// The framework categories, transcribed from the oracle: identifier spelling, the value the
    /// catalogue declares, and the offset from <see cref="Enums.I18N_CAT_CUSTOM"/> that the oracle
    /// writes for it. [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <remarks>
    /// The OFFSET is the transcribed part, and it is the only integer in this table that comes from
    /// the oracle's text. The declared value is a member read, not a literal, so the row states the
    /// relationship the oracle states and nothing more. Rows are in the oracle's declaration order.
    /// </remarks>
    private static readonly (string Identifier, long Declared, long Offset)[] FrameworkCategories =
    [
        ("CAT_MSGBOX", Categories.CAT_MSGBOX, 1),   // ne_cst_i18n.sru:L16
        ("CAT_DWSVC", Categories.CAT_DWSVC, 2),     // ne_cst_i18n.sru:L17
    ];

    /// <summary>
    /// The six categories the framework declares for itself, which the two framework categories
    /// above must not collide with. [enums.sru:L118-L123]
    /// </summary>
    /// <remarks>
    /// The values are READ FROM <see cref="Enums"/> rather than restated as literals. Pinning what
    /// those six values ARE belongs to the kernel's own suite, which owns <c>enums.sru</c>; this
    /// suite owns the RELATIONSHIP between them and the two categories layered above them, and
    /// duplicating the numbers here would create a second copy to drift. Rows are in the oracle's
    /// declaration order, which is also ascending value order.
    /// </remarks>
    private static readonly (string Identifier, long Value)[] BuiltInCategories =
    [
        ("I18N_CAT_WINDOW", Enums.I18N_CAT_WINDOW),                     // enums.sru:L118
        ("I18N_CAT_TABCONTROL", Enums.I18N_CAT_TABCONTROL),             // enums.sru:L119
        ("I18N_CAT_RIBBONBAR", Enums.I18N_CAT_RIBBONBAR),               // enums.sru:L120
        ("I18N_CAT_SPLITCONTAINER", Enums.I18N_CAT_SPLITCONTAINER),     // enums.sru:L121
        ("I18N_CAT_DATAWINDOW", Enums.I18N_CAT_DATAWINDOW),             // enums.sru:L122
        ("I18N_CAT_CUSTOM", Enums.I18N_CAT_CUSTOM),                     // enums.sru:L123
    ];

    /// <summary>
    /// The identifier under which the oracle declares the custom-category base that both framework
    /// categories are offset from. [enums.sru:L123]
    /// </summary>
    /// <remarks>
    /// Held as a string literal so the metadata path resolves the base by an independent
    /// transcription of the oracle's spelling rather than by a <c>nameof</c> of the implementation.
    /// </remarks>
    private const string CustomCategoryBaseIdentifier = "I18N_CAT_CUSTOM";

    /// <summary>
    /// Every public constant <see cref="Categories"/> declares, keyed by its exact identifier
    /// spelling, read from the library's compiled metadata.
    /// </summary>
    /// <remarks>
    /// This is the metadata read path described in the file header. It is built once because
    /// reflecting over a type is not free and the result cannot change during a run.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, FieldInfo> DeclaredCategoryFields =
        BuildDeclaredFields(typeof(Categories));

    // ==========================================================================================
    //  MEMBER DATA PROVIDERS
    //  Each projects one of the tables above into a strongly typed TheoryData, so the compiler
    //  checks each row against the theory's parameter list rather than deferring the mismatch to a
    //  run-time cast. Typed TheoryData is used in preference to IEnumerable<object[]> throughout
    //  this repository's suites for that reason.
    // ==========================================================================================

    /// <summary>Identifier, declared value and oracle offset, one row per framework category.</summary>
    /// <returns>Two rows, in the oracle's declaration order. [ne_cst_i18n.sru:L16-L17]</returns>
    public static TheoryData<string, long, long> FrameworkCategoryRows()
    {
        TheoryData<string, long, long> rows = [];
        foreach ((string identifier, long declared, long offset) in FrameworkCategories)
        {
            rows.Add(identifier, declared, offset);
        }

        return rows;
    }

    /// <summary>Identifier spelling only, one row per framework category.</summary>
    /// <returns>Two rows, in the oracle's declaration order. [ne_cst_i18n.sru:L16-L17]</returns>
    public static TheoryData<string> FrameworkCategoryIdentifierRows()
    {
        TheoryData<string> rows = [];
        foreach ((string identifier, _, _) in FrameworkCategories)
        {
            rows.Add(identifier);
        }

        return rows;
    }

    /// <summary>Identifier and value, one row per built-in framework category.</summary>
    /// <returns>Six rows, in the oracle's declaration order. [enums.sru:L118-L123]</returns>
    public static TheoryData<string, long> BuiltInCategoryRows()
    {
        TheoryData<string, long> rows = [];
        foreach ((string identifier, long value) in BuiltInCategories)
        {
            rows.Add(identifier, value);
        }

        return rows;
    }

    // ==========================================================================================
    //  THE DERIVATION - ne_cst_i18n.sru:L16-L17
    // ==========================================================================================

    /// <summary>
    /// Each framework category equals <see cref="Enums.I18N_CAT_CUSTOM"/> plus the offset the oracle
    /// writes for it. [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <param name="declared">The value the catalogue declares, read as an inlined constant.</param>
    /// <param name="offset">The offset from the custom-category base, transcribed from the oracle.</param>
    /// <remarks>
    /// THE CENTRAL TEST OF THE SUITE, AND THE ONE WHOSE FORM MATTERS AS MUCH AS ITS RESULT. The
    /// expectation is computed - `base + offset` - and is never a hand-written integer, so the test
    /// discriminates between a catalogue that transcribed the oracle's arithmetic and one that
    /// evaluated it: move <see cref="Enums.I18N_CAT_CUSTOM"/> and the first stays green while the
    /// second fails on the next run. A literal expectation could not tell them apart, which is why
    /// this assertion must not be "simplified" into one. See C-B in the file header.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameworkCategoryRows))]
    public void EachCategoryIsTheCustomCategoryBasePlusItsOracleOffset(
        string identifier,
        long declared,
        long offset)
    {
        Assert.Equal(Enums.I18N_CAT_CUSTOM + offset, declared);

        // The row is also checked against the declaration it claims to describe, so a row naming one
        // category while carrying another's value cannot agree with itself into a pass.
        Assert.Equal(declared, DeclaredValueOf(identifier));
    }

    /// <summary>
    /// The derivation also holds between the two libraries as they were COMPILED, not merely between
    /// the constants this test assembly inlined. [ne_cst_i18n.sru:L16-L17, enums.sru:L123]
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <param name="declared">The inlined value, re-checked here against the metadata value.</param>
    /// <param name="offset">The offset from the custom-category base, transcribed from the oracle.</param>
    /// <remarks>
    /// The metadata read path from the file header. Both operands are read back out of compiled
    /// metadata - the offset's base from PowerFramework.Shared.Kernel and the category from
    /// PowerFramework.Shared.Localization - so the derivation is re-verified against what the shipped
    /// libraries declare rather than against what this assembly baked in when it was built. Because a
    /// C# constant is inlined at the CONSUMER's compile time, this is the only assertion in the suite
    /// that can catch a stale or mismatched reference; the theory above would keep passing against
    /// one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameworkCategoryRows))]
    public void TheDerivationHoldsInCompiledMetadataAndNotOnlyInInlinedConstants(
        string identifier,
        long declared,
        long offset)
    {
        long baseFromMetadata = CustomCategoryBaseFromMetadata();
        long categoryFromMetadata = DeclaredValueOf(identifier);

        Assert.Equal(baseFromMetadata + offset, categoryFromMetadata);
        Assert.Equal(declared, categoryFromMetadata);
    }

    /// <summary>
    /// The two framework categories are distinct, contiguous, and sit immediately above the
    /// custom-category base in the oracle's declaration order. [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <remarks>
    /// A Fact because the property is about the PAIR, so a theory would carry a single row. The
    /// contiguity assertion is what pins the offsets as 1 and 2 rather than as any two distinct
    /// numbers: a catalogue that reordered them, or that left a gap between them, satisfies the
    /// per-category theory's shape but fails here. `CAT_MSGBOX + 1` is deliberately written in terms
    /// of the sibling constant rather than as an absolute value, for the reason given in C-B.
    /// </remarks>
    [Fact]
    public void TheTwoCategoriesAreDistinctContiguousAndAboveTheCustomCategoryBase()
    {
        Assert.NotEqual(Categories.CAT_MSGBOX, Categories.CAT_DWSVC);
        Assert.Equal(Categories.CAT_MSGBOX + 1, Categories.CAT_DWSVC);

        Assert.True(
            Categories.CAT_MSGBOX > Enums.I18N_CAT_CUSTOM,
            "Categories.CAT_MSGBOX must sit ABOVE Enums.I18N_CAT_CUSTOM, because the framework " +
            "reserves everything above that base for categories defined on top of it " +
            "[enums.sru:L118-L123].");
        Assert.True(
            Categories.CAT_DWSVC > Enums.I18N_CAT_CUSTOM,
            "Categories.CAT_DWSVC must sit ABOVE Enums.I18N_CAT_CUSTOM, because the framework " +
            "reserves everything above that base for categories defined on top of it " +
            "[enums.sru:L118-L123].");
    }

    // ==========================================================================================
    //  NO COLLISION WITH THE FRAMEWORK'S OWN CATEGORY SET - enums.sru:L118-L123
    // ==========================================================================================

    /// <summary>
    /// Neither framework category equals any of the six categories the framework declares for
    /// itself. [enums.sru:L118-L123]
    /// </summary>
    /// <param name="identifier">The built-in category's identifier spelling, from the oracle.</param>
    /// <param name="builtIn">The value that built-in category carries.</param>
    /// <remarks>
    /// This matters behaviourally rather than merely tidily. Each concrete provider dispatches on the
    /// category with a `choose case` - the English provider at n_cst_i18n_en.sru:L34-L47 and the
    /// Traditional Chinese provider at n_cst_i18n_cht.sru:L35-L48 - so two categories sharing a value
    /// would make one arm unreachable and silently route its lookups to the other's translation
    /// element. The switch is a value dispatch, so a collision is not a compile error anywhere; this
    /// theory is the only place it would be reported.
    /// </remarks>
    [Theory]
    [MemberData(nameof(BuiltInCategoryRows))]
    public void NeitherCategoryCollidesWithABuiltInFrameworkCategory(string identifier, long builtIn)
    {
        // The row is verified against the kernel's compiled metadata first, so the comparison below
        // is made against the value the framework really declares under that spelling.
        Assert.Equal(builtIn, KernelConstantValueOf(identifier));

        // The category leads because xUnit2000 requires the constant operand in the `expected`
        // position; inequality is symmetric, so the argument order carries no meaning beyond that.
        Assert.NotEqual(Categories.CAT_MSGBOX, builtIn);
        Assert.NotEqual(Categories.CAT_DWSVC, builtIn);
    }

    // ==========================================================================================
    //  THE PRESERVED IDENTIFIER SPELLINGS - AAP 0.4.5.3
    // ==========================================================================================

    /// <summary>
    /// Each category is declared under its verbatim legacy spelling: same case, underscore included.
    /// [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <remarks>
    /// THIS IS WHAT MAKES THE VERBATIM-SPELLING REQUIREMENT BUILD-ENFORCEABLE. The lookup is ordinal
    /// and exact, so a member renamed to PascalCase - CatMsgbox for CAT_MSGBOX, say - fails here even
    /// though it would still carry the right value. That is the intended outcome: the spellings travel
    /// in serialized payloads, log records and characterization recordings, so a rename invalidates
    /// every stored comparison that mentions them, which is a behavioural change and not a restyling.
    /// <para>
    /// If a naming analyzer ever objects to these spellings, the correct response is the scoped
    /// CA1707 / IDE1006 suppression that the repository-root .editorconfig already carries for
    /// shared/PowerFramework.Shared.Localization/Categories.cs - NEVER a rename, and never a
    /// suppression added to this file, which declares no non-conventional identifier of its own.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameworkCategoryIdentifierRows))]
    public void EachCategoryKeepsItsVerbatimScreamingSnakeSpelling(string identifier)
    {
        FieldInfo field = DeclaredFieldOf(identifier);

        // Ordinal, so the match is case-sensitive: Assert.Equal(string, string) does not fold case.
        Assert.Equal(identifier, field.Name);

        // The two properties a .NET-conventional rename would destroy, asserted separately so a
        // failure says which one was lost.
        Assert.Contains("_", field.Name, StringComparison.Ordinal);
        Assert.Equal(field.Name.ToUpperInvariant(), field.Name);
    }

    /// <summary>
    /// Each category is a compile-time constant, matching the oracle's <c>Constant Long</c>: a
    /// literal field, never a property, a mutable static field or a <c>static readonly</c>.
    /// [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <remarks>
    /// The oracle declares both as `Constant`, so the value cannot be reassigned at run time and both
    /// are usable wherever a compile-time constant is required - which the catalogue itself relies on,
    /// since `Enums.I18N_CAT_CUSTOM + 1` is only a legal initializer for a `const` because its base is
    /// one too. A `static readonly` field would compile and would carry the same number while
    /// quietly losing both properties, and a property would additionally add a call where the legacy
    /// has a value; reflection is the only way to tell any of them apart from the outside.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameworkCategoryIdentifierRows))]
    public void EachCategoryIsAPublicCompileTimeConstantOfTypeLong(string identifier)
    {
        FieldInfo field = DeclaredFieldOf(identifier);

        Assert.True(
            field.IsLiteral,
            $"Categories.{identifier} must be a `const` - IsLiteral - because the oracle declares " +
            "it `Constant Long` [ne_cst_i18n.sru:L16-L17]. A `static readonly` field carries the " +
            "same value but is not a compile-time constant, so it could not initialize another " +
            "constant and could differ between two readers of the same catalogue.");
        Assert.False(
            field.IsInitOnly,
            $"Categories.{identifier} must not be `static readonly`; a literal constant is never " +
            "IsInitOnly, so this reports the weaker form having been substituted.");
        Assert.True(field.IsStatic, $"Categories.{identifier} must be static.");
        Assert.True(field.IsPublic, $"Categories.{identifier} must be public.");

        // PB `Long` is a 32-bit signed integer, widened to C# long so a category needs no conversion
        // at the call site, where the parameter is itself a long [i18n.srf:L8-L9].
        Assert.Equal(typeof(long), field.FieldType);
        Assert.IsType<long>(field.GetRawConstantValue());

        // Not a property and not a method: the legacy declares a value, not an accessor.
        Assert.Null(typeof(Categories).GetProperty(identifier, PublicStaticDeclaredOnly));
        Assert.Null(typeof(Categories).GetMethod(identifier, PublicStaticDeclaredOnly));
    }

    // ==========================================================================================
    //  WHOLE-CATALOGUE AUDITS
    //  Two Facts, because each is a single statement about the type as a whole.
    // ==========================================================================================

    /// <summary>
    /// <see cref="Categories"/> is a static catalogue of literals and holds nothing else: no
    /// constructor, property, method, event, nested type or non-public field. [ne_cst_i18n.sru]
    /// </summary>
    /// <remarks>
    /// The oracle declares no function, no event script and no instance variable, so the two
    /// constants ARE the object; this audit is what keeps the port that shape. It also pins the
    /// property the wire contracts depend on - everything the catalogue exposes is a compile-time
    /// literal, so no consumer can observe a different value than another.
    /// </remarks>
    [Fact]
    public void CategoriesIsAStaticCatalogueOfLiteralsWithNoOtherMember()
    {
        Type catalogue = typeof(Categories);

        // A C# `static class` is encoded as abstract plus sealed, which is what makes it impossible
        // to instantiate or derive from - the legacy object is reached as a type-qualified read off a
        // global auto-instance [ne_cst_i18n.sru:L10], never constructed by a consumer.
        Assert.True(catalogue.IsAbstract);
        Assert.True(catalogue.IsSealed);
        Assert.Empty(catalogue.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

        Assert.Empty(catalogue.GetProperties(PublicStaticDeclaredOnly));
        Assert.Empty(catalogue.GetMethods(PublicStaticDeclaredOnly));
        Assert.Empty(catalogue.GetEvents(PublicStaticDeclaredOnly));
        Assert.Empty(catalogue.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Empty(catalogue.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly));

        Assert.All(
            catalogue.GetFields(PublicStaticDeclaredOnly),
            field =>
            {
                Assert.True(field.IsLiteral, $"Categories.{field.Name} is not a compile-time constant.");
                Assert.Equal(typeof(long), field.FieldType);
            });
    }

    /// <summary>
    /// Every constant <see cref="Categories"/> declares is asserted by this suite, and the catalogue
    /// holds exactly the two the oracle declares. [ne_cst_i18n.sru:L16-L17]
    /// </summary>
    /// <remarks>
    /// THE AUDIT THAT MAKES THE REST OF THE SUITE TRUSTWORTHY. Comparing the declared identifier set
    /// with the transcribed table closes both directions of the gap a table-driven suite otherwise
    /// leaves open: a category added to Categories.cs without a row here fails, and a row here naming
    /// a category that does not exist fails too. Without it, "the suite passes" would only mean "the
    /// categories I happened to list are right", which is the weaker claim that lets a new category
    /// reach the localization contract unexamined. The comparison is against string literals
    /// transcribed from the oracle rather than against `nameof`, so it is a genuine three-way check
    /// and not the implementation compared with itself.
    /// </remarks>
    [Fact]
    public void EveryConstantDeclaredByCategoriesIsAssertedByThisSuite()
    {
        Assert.Equal(ExpectedFrameworkCategoryCount, FrameworkCategories.Length);
        Assert.Equal(ExpectedFrameworkCategoryCount, DeclaredCategoryFields.Count);

        string[] transcribed = [.. FrameworkCategories.Select(row => row.Identifier).Order(StringComparer.Ordinal)];
        string[] declared = [.. DeclaredCategoryFields.Keys.Order(StringComparer.Ordinal)];

        Assert.Equal(transcribed, declared);

        // Distinct spellings and distinct values: two rows for one constant, or two constants sharing
        // a value, would both make one of the theories above vacuous.
        Assert.Distinct(transcribed, StringComparer.Ordinal);
        Assert.Distinct(FrameworkCategories.Select(row => row.Declared));
    }

    // ==========================================================================================
    //  PRIVATE HELPERS
    // ==========================================================================================

    /// <summary>
    /// The binding flags every metadata lookup in this suite uses: public, static, and declared on
    /// the type itself.
    /// </summary>
    /// <remarks>
    /// <c>DeclaredOnly</c> is load-bearing rather than defensive. Without it an instance-flagged
    /// lookup would surface the members every type inherits from <see cref="object"/>, so a
    /// "no other member" audit would report members the catalogue never declared and the assertion
    /// would have to be weakened to accommodate them.
    /// </remarks>
    private const BindingFlags PublicStaticDeclaredOnly =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Reads every public static field a catalogue type declares, keyed by its exact identifier
    /// spelling.
    /// </summary>
    /// <param name="catalogue">The constant catalogue to reflect over.</param>
    /// <returns>An ordinal-keyed map from identifier spelling to the declaring field.</returns>
    /// <remarks>
    /// The map is deliberately keyed with <see cref="StringComparer.Ordinal"/>: a case-insensitive
    /// comparer would let a PascalCase rename resolve, which is exactly the failure the spelling
    /// theory exists to catch. Fields are returned rather than values so one lookup serves both the
    /// value assertions and the constness assertions.
    /// </remarks>
    private static IReadOnlyDictionary<string, FieldInfo> BuildDeclaredFields(Type catalogue)
    {
        FieldInfo[] fields = catalogue.GetFields(PublicStaticDeclaredOnly);
        Dictionary<string, FieldInfo> declared = new(fields.Length, StringComparer.Ordinal);

        foreach (FieldInfo field in fields)
        {
            declared.Add(field.Name, field);
        }

        return declared;
    }

    /// <summary>
    /// Returns the field <see cref="Categories"/> declares under <paramref name="identifier"/>,
    /// failing the calling test if no member carries that exact spelling.
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <returns>The declaring field, read from compiled metadata.</returns>
    private static FieldInfo DeclaredFieldOf(string identifier)
    {
        Assert.True(
            DeclaredCategoryFields.TryGetValue(identifier, out FieldInfo? field),
            $"PowerFramework.Shared.Localization.Categories declares no public static member named " +
            $"'{identifier}'. The identifier spellings are part of the contract - they travel in " +
            "serialized payloads, log records and characterization recordings - so a missing or " +
            "renamed constant is a behavioural change, not a restyling. Check the spelling against " +
            "ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17 rather than editing this " +
            "expectation, and if a naming analyzer prompted the rename, use the scoped suppression " +
            "the repository-root .editorconfig already carries for Categories.cs instead.");

        // Not redundant after the assertion above: Assert.True carries the diagnostic message but
        // tells the compiler nothing about the out parameter, whereas Assert.NotNull is annotated so
        // the non-null return needs neither a cast nor a null-forgiving operator. Under
        // TreatWarningsAsErrors that distinction is the difference between compiling and not.
        Assert.NotNull(field);

        return field;
    }

    /// <summary>
    /// Returns the compile-time constant value <see cref="Categories"/> declares under
    /// <paramref name="identifier"/>, read from compiled metadata rather than inlined.
    /// </summary>
    /// <param name="identifier">The identifier spelling transcribed from the oracle.</param>
    /// <returns>The declared constant value.</returns>
    private static long DeclaredValueOf(string identifier) => ConstantValueOf(DeclaredFieldOf(identifier));

    /// <summary>
    /// Returns the custom-category base the two framework categories are offset from, read out of
    /// <see cref="Enums"/>'s compiled metadata. [enums.sru:L123]
    /// </summary>
    /// <returns>The declared base value.</returns>
    private static long CustomCategoryBaseFromMetadata() => KernelConstantValueOf(CustomCategoryBaseIdentifier);

    /// <summary>
    /// Returns the compile-time constant value <see cref="Enums"/> declares under
    /// <paramref name="identifier"/>, read from compiled metadata rather than inlined.
    /// </summary>
    /// <param name="identifier">The kernel identifier spelling transcribed from the oracle.</param>
    /// <returns>The declared constant value.</returns>
    /// <remarks>
    /// Reflecting over <see cref="Enums"/> rather than reading the member directly is what makes the
    /// metadata read path independent of this assembly's own compilation; see the file header.
    /// </remarks>
    private static long KernelConstantValueOf(string identifier)
    {
        FieldInfo? field = typeof(Enums).GetField(identifier, PublicStaticDeclaredOnly);

        Assert.True(
            field is not null,
            $"PowerFramework.Shared.Kernel.Enums declares no public static member named " +
            $"'{identifier}'. The framework category set and the base the localization categories " +
            "are offset from are declared at ws_objects/pfw.shared.pbl.src/enums.sru:L118-L123; " +
            "check the spelling against the oracle rather than editing this expectation.");

        // Carries the nullability post-condition the message-bearing assertion above cannot; see the
        // note on DeclaredFieldOf.
        Assert.NotNull(field);

        return ConstantValueOf(field);
    }

    /// <summary>
    /// Reads a field's compile-time constant value as a <see langword="long"/>.
    /// </summary>
    /// <param name="field">A field expected to be a <see langword="long"/> literal constant.</param>
    /// <returns>The declared constant value.</returns>
    /// <remarks>
    /// The type check is an assertion rather than a cast so that a catalogue declaring the wrong width
    /// - or declaring a <c>static readonly</c> field, which has no constant value to read at all -
    /// reports what it actually did instead of surfacing as an opaque cast or reflection exception.
    /// </remarks>
    private static long ConstantValueOf(FieldInfo field)
    {
        Assert.True(
            field.IsLiteral,
            $"{field.DeclaringType?.FullName}.{field.Name} carries no compile-time constant value, " +
            "so it is not a `const`. The oracle declares these values `Constant Long`.");

        return Assert.IsType<long>(field.GetRawConstantValue());
    }
}
