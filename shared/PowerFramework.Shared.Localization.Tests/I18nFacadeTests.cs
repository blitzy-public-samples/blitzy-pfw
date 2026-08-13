// ==================================================================================================
//  I18nFacadeTests.cs - THE THREE PROOF OBLIGATIONS THAT SHARE ONE FACADE INSTANCE
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.I18n
//                    shared/PowerFramework.Shared.Localization/I18n.cs
//
//  BEHAVIOURAL       ws_objects/pfw.ui.pbl.src/i18n.srf         :L7-L9    the three prototypes
//  ORACLE            ws_objects/pfw.ui.pbl.src/i18n.srf         :L12-L14  the installer
//                    ws_objects/pfw.ui.pbl.src/i18n.srf         :L17-L18  the 2-argument translator
//                    ws_objects/pfw.ui.pbl.src/i18n.srf         :L21-L22  the 3-argument translator
//                    ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru   :L9       the event signature
//                    ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru   :L11      the shadowing global slot
//                    ws_objects/pfw.shared.pbl.src/retcode.sru  :L39,:L48 OK, E_INVALID_OBJECT
//                    ws_objects/pfw.shared.pbl.src/enums.sru    :L115-L123 source / category spaces
//
//                    Every path above is READ ONLY - it is the behavioural oracle for parity
//                    testing and never an edit target (C-C). This file opens none of them and reads
//                    no file at all: the locators appear in comments only.
//
//  THE THREE OBLIGATIONS THIS FILE DISCHARGES, AND WHY THEY SHARE A FILE
//  ------------------------------------------------------------------------------------------------
//  All three exercise THE SAME I18n instance through THE SAME injected-provider seam, so splitting
//  them across files would triple the fixture and hide the fact that they constrain one another:
//
//    A. SILENT PASSTHROUGH.        With no provider installed the text comes back unchanged and
//                                  NOTHING ELSE HAPPENS. Section 1.
//    B. THE INSTALLER OVERLOAD.    Which argument is rejected, with which code, and what a
//                                  rejection does NOT do to an incumbent provider. Section 2.
//    C. THE DISCARDED RETURN.      The provider's `long` is part of the published contract and the
//                                  facade throws it away. Section 3.
//
//  Section 4 then pins the one thing that distinguishes the two translating overloads from the
//  outside - which SOURCE each of them supplies - because A, B and C are all blind to it.
//
//  THE ORACLE, QUOTED IN FULL - IT IS NINE LINES AND EVERY ASSERTION BELOW TRACES TO ONE OF THEM
//  ------------------------------------------------------------------------------------------------
//      global function long i18n (n_cst_i18n n);if Not IsValid(n) then return RetCode.E_INVALID_OBJECT
//      n_cst_i18n = n                                                                          L13
//      return RetCode.OK                                                                       L14
//      end function
//
//      global function string i18n (readonly long category,string text);if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(Enums.I18N_SRC_PFW,category,ref text)
//      return text                                                                             L18
//      end function
//
//      global function string i18n (readonly long source,readonly long category,string text);if IsValid(n_cst_i18n) then n_cst_i18n.Event OnTranslate(source,category,ref text)
//      return text                                                                             L22
//      end function
//
//  Four mechanical facts are readable directly off those lines, and each is a test below:
//
//    1. L12 RETURNS BEFORE L13 ASSIGNS. A rejected install therefore cannot disturb an incumbent
//       provider. An implementation that assigned first and validated afterwards would answer the
//       same code while silently uninstalling a working provider, and the ONLY observable
//       difference is the next translation - so that is where the assertion goes.
//    2. L18 AND L22 SIT OUTSIDE THEIR GUARD. With no provider installed the body of each
//       translating overload is nothing but the return. That single fact IS the silent passthrough;
//       there is no else branch to inspect because there is no else branch.
//    3. L17 HARDCODES `Enums.I18N_SRC_PFW` WHILE L21 FORWARDS THE CALLER'S `source`. Both overloads
//       can produce identical text, so only a recording of the `source` argument tells them apart.
//    4. NEITHER L17 NOR L21 CAPTURES THE EVENT'S RESULT. The event is invoked as a bare statement.
//
//  C-B - NO NEW FEATURES, NO BEHAVIOUR IMPROVEMENTS; PRESERVE VERBATIM
//  ------------------------------------------------------------------------------------------------
//  This is the constraint most at risk of being inverted by a well-meaning author, because both
//  headline behaviours LOOK like defects and are not:
//
//    * THE SILENT PASSTHROUGH IS INTENDED BEHAVIOUR, NOT A GAP TO BE HARDENED. This suite asserts
//      it as CORRECT. Every item on the following list is something an engineer would reasonably
//      add and every one of them would be a behavioural regression here:
//
//          no throw when no provider is installed        no `[untranslated]` marker or sentinel
//          no log, trace, metric or diagnostic of a miss no null returned for a missed lookup
//          no out parameter or flag reporting a miss     no fallback chain to a second provider
//
//    * THE FACADE DISCARDING THE PROVIDER'S RETURN CODE IS INTENDED. This suite does NOT assert
//      that the facade surfaces the code, and does NOT assert that it acts on it. It asserts the
//      opposite: the answer is whatever the provider left in `text`, whatever code came back
//      alongside it.
//
//  C-K - DOCUMENT EVERY TECHNOLOGY-SPECIFIC AND BOUNDARY-SPECIFIC DECISION
//  ------------------------------------------------------------------------------------------------
//  The discarded-return decision is NAMED where this suite covers it - see the banner above
//  Section 3 - and the short form is recorded here too so a reader of the header does not have to
//  find it: `i18n.srf`:L17 and :L21 invoke the provider event as a bare statement and the very next
//  line, :L18 and :L22, returns `text` WITHOUT CAPTURING THE `long`. The `long` is nonetheless part
//  of `II18nProvider`'s published contract - `n_cst_i18n.sru`:L9 declares the event as returning
//  one, and all three shipped providers produce it with the documented alphabet 1 = handled,
//  0 = not handled. This suite therefore asserts THE CODE ON THE PROVIDER and THE TEXT ON THE
//  FACADE, which is the only split that states both halves truthfully.
//
//  C-H - NULLABLE, WARNINGS AS ERRORS, AND THE COVERAGE GATE
//  ------------------------------------------------------------------------------------------------
//  `Directory.Build.props` sets `Nullable`, `ImplicitUsings`, `EnableNETAnalyzers` and
//  `TreatWarningsAsErrors` for every project in the tree, and this project's file carries no
//  `NoWarn`. Consequences honoured below:
//
//    * The invalid-provider case is EXPECTED INPUT, not a caller defect: the oracle tests
//      `Not IsValid(n)` and returns a code for it [i18n.srf:L12]. It is exercised through an
//      EXPLICITLY NULLABLE LOCAL - `II18nProvider? invalid = null` - which is the correct handling.
//      There is no `#pragma warning disable`, no `SuppressMessage` attribute and no null-forgiving
//      `!` operator anywhere in this file; a file-wide suppression would have hidden real
//      diagnostics in the other twenty assertions.
//    * This suite is the main coverage contributor for `I18n.cs`, so ALL THREE overloads are
//      exercised: the installer directly, the 3-argument translator directly, and the 2-argument
//      translator directly (it delegates inward, but a caller must still enter through it or its
//      own body goes uncovered).
//    * Ordinal string comparison is stated explicitly rather than inherited from the assertion
//      library's default. `Assert.Equal(expected, actual, StringComparer.Ordinal)` is deliberately
//      NOT used: `StringComparer` implements `IEqualityComparer<string?>`, and passing it where
//      `IEqualityComparer<string>` is expected risks CS8620, which under warnings-as-errors is a
//      build break rather than a note. The helper at the foot of this file pairs xunit's
//      diff-producing `Assert.Equal` with an explicit `string.Equals(..., StringComparison.Ordinal)`
//      instead, so both properties are had at once.
//
//  C-F - NOTHING HARDCODED CARRIED FORWARD
//  ------------------------------------------------------------------------------------------------
//  No credential, key, token, password or connection string appears in this file, and none may be
//  added. Every literal below is either a marker-shaped probe string chosen to be unmistakable in a
//  failing assertion, or one Chinese caption transcribed from the read-only resource table so that a
//  non-ASCII row is a real one rather than an invented one.
//
//  DELIBERATE-BREAK CHECKS - THE FIVE MUTATIONS THIS SUITE MUST CATCH
//  ------------------------------------------------------------------------------------------------
//  A suite that cannot fail is documentation. Each row names the mutation, the test that dies, and
//  why that test and not another:
//
//    1. MUTATION  throw when no provider is installed
//       FAILS AT  both passthrough theories, and TranslatingWithNoProviderInstalledDoesNotThrow
//       BECAUSE   every call is made directly, so the exception escapes into the test result; and
//                 the explicit fact records the absence rather than asserting a presence.
//
//    2. MUTATION  append or wrap an "[untranslated]" marker
//       FAILS AT  the passthrough theories, on the marker-shaped rows in particular
//       BECAUSE   the comparison is ordinal and the marker-shaped inputs give an added marker
//                 nowhere to hide.
//
//    3. MUTATION  assign the provider slot BEFORE validating the argument
//       FAILS AT  ARejectedInstallationLeavesTheIncumbentProviderInPlace
//       BECAUSE   the return code is identical either way, so the assertion is on the NEXT
//                 translation - which routes to a null slot once the order is reversed.
//
//    4. MUTATION  store the provider in a `static` field
//       FAILS AT  TwoFacadeInstancesEachKeepTheirOwnProvider, and
//                 TheFacadeDeclaresNoStaticStateForTheProviderSlot
//       BECAUSE   two facades with one recorder each cross-contaminate through a shared slot, and
//                 the metadata fact names the forbidden shape outright.
//
//    5. MUTATION  hardcode I18N_SRC_CUSTOM in the two-argument overload
//       FAILS AT  TheTwoArgumentOverloadSuppliesTheFrameworkSource, and
//                 TheTwoOverloadsDifferOnlyInTheSourceTheySupply
//       BECAUSE   the recorded `source` argument is the only place the difference is observable -
//                 the returned text is identical under either constant.
//
//  Two of those five are only catchable because of a choice made deliberately: the marker-shaped
//  rows `"???"`, `"[[x]]"` and `"!x!"` exist so that an added marker cannot HIDE INSIDE an input
//  that already looks like one, and every source/category pair uses a NON-ZERO category so a
//  recorded pair cannot be consistent with reading either argument twice (see the hazard note on
//  Section 4).
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  ------------------------------------------------------------------------------------------------
//    * `Assert.Throws` ANYWHERE IN SECTION 1. Its presence would invert the requirement, which is
//      that nothing throws. Section 1's explicit no-throw fact uses `Record.Exception` with
//      `Assert.Null`, which asserts the absence rather than the presence of a fault.
//    * Any assertion that a translating overload returns a code. They return `string?`; the reader
//      who reaches for one has confused them with the installer. Section 3's reflection fact proves
//      the published surface carries no such overload at all.
//    * A mocking library or a fluent assertion library. The dependency set for this refactor is
//      fixed and neither is in it; the hand-written doubles in `FakeI18nProviders.cs` are what this
//      suite consumes, and a single-consumer double would be a nested private class rather than an
//      addition to that shared file.
//    * A second copy of any double. `HandledProvider`, `NotHandledProvider` and `RecordingProvider`
//      are multi-consumer fixtures that already exist; re-declaring one here would fork the
//      contract being doubled.
//    * Any file access, temporary file or environment dependency. Every input is a literal and
//      every outcome is deterministic, which is what lets this suite run in parallel with the rest
//      of the assembly.
// ==================================================================================================

using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for the three published overloads of <see cref="I18n"/>: the silent passthrough, the
/// installer, and the deliberate discarding of the provider's return code.
/// </summary>
/// <remarks>
/// <para>
/// No <c>using PowerFramework.Shared.Localization;</c> directive appears above, because this
/// namespace is a CHILD of the library's namespace and simple-name lookup walks outward to find
/// <see cref="I18n"/>, <see cref="II18nProvider"/> and <see cref="Categories"/> there.
/// <c>PowerFramework.Shared.Kernel</c> IS imported: it is not an enclosing namespace of this one, and
/// <see cref="RetCode"/> and <see cref="Enums"/> live in it. <c>System.Reflection</c> is imported
/// because the implicit-usings set does not include it and three facts below read compiled metadata.
/// </para>
/// <para>
/// Sealed because nothing derives from it, and holding no instance state: xunit constructs a fresh
/// instance per test, and every fixture each test needs is constructed inside that test. That is what
/// makes the whole suite order-independent and safe to run in parallel with its siblings.
/// </para>
/// </remarks>
public sealed class I18nFacadeTests
{
    // ==============================================================================================
    //  PROBE INPUTS
    //  --------------------------------------------------------------------------------------------
    //  Named rather than inlined so that a failing assertion in any section names the same input the
    //  member-data rows do, and so that the marker-shaped trio is visibly a deliberate set rather
    //  than three arbitrary strings someone happened to type.
    // ==============================================================================================

    /// <summary>
    /// A plain ASCII probe that is unmistakably a test input rather than anything from the read-only
    /// translation table, so no assertion below can pass because a real table happened to agree.
    /// </summary>
    private const string PlainAsciiText = "PFW-FACADE-PROBE";

    /// <summary>
    /// A real caption transcribed from the read-only resource table, so the non-ASCII row exercises
    /// text the estate actually carries rather than an invented codepoint.
    /// </summary>
    /// <remarks>
    /// This is the very entry whose English rendering is one of the two preserved mistranslations, so
    /// it is also the string most likely to be touched by a future "helpful" normalisation. Passing it
    /// through a facade with NO provider installed must leave all three characters exactly as they
    /// arrived - no normalisation form change, no re-encoding, no trimming.
    /// </remarks>
    private const string ChineseText = "最小化";

    /// <summary>
    /// A whitespace-only probe. Distinct from <see cref="string.Empty"/> and, like it, a value a
    /// "helpful" facade would be tempted to normalise away.
    /// </summary>
    private const string WhitespaceOnlyText = " ";

    /// <summary>
    /// The first of three MARKER-SHAPED probes: input that already looks like a sentinel.
    /// </summary>
    /// <remarks>
    /// The three exist for one reason. A facade that appended or wrapped an untranslated marker would
    /// be caught by ordinal equality on any row - but only if the marker cannot HIDE inside the input.
    /// A suite whose only inputs were ordinary words could be defeated by a marker that happened to
    /// resemble one of them. These three are shaped like the markers an implementation would actually
    /// reach for: a bare question-mark run, a double-bracket wrapper, and a bang-delimited token.
    /// <para>
    /// This particular value carries a second meaning worth knowing about, which is why it was chosen
    /// over any other punctuation run: elsewhere in the DataWindow service layer a validation message
    /// consisting of a single question mark is treated as "no message" and replaced by a localized
    /// fallback. Nothing of that kind may leak into THIS facade, which has no notion of an empty or
    /// placeholder value at all, so a run of question marks is precisely the input that would expose
    /// such a leak.
    /// </para>
    /// </remarks>
    private const string QuestionMarksText = "???";

    /// <summary>
    /// The second marker-shaped probe: a double-bracket wrapper, the shape a "wrap it to show it was
    /// not translated" implementation produces.
    /// </summary>
    private const string BracketMarkerText = "[[x]]";

    /// <summary>
    /// The third marker-shaped probe: a bang-delimited token, the shape a terse sentinel takes.
    /// </summary>
    private const string BangMarkerText = "!x!";

    /// <summary>
    /// The replacement written by the incumbent provider in the installer section, chosen to be
    /// obvious in a failing assertion and impossible to confuse with a real translation.
    /// </summary>
    private const string FirstReplacement = "<<FIRST-PROVIDER>>";

    /// <summary>
    /// The replacement written by the provider that displaces the first one.
    /// </summary>
    private const string SecondReplacement = "<<SECOND-PROVIDER>>";

    /// <summary>
    /// The replacement used by the handled-returning double in Section 3, distinct from
    /// <see cref="HandledProvider.DefaultReplacement"/> so that a row asserting on it is asserting on
    /// a value THIS suite chose rather than on the fixture's default.
    /// </summary>
    private const string HandledReplacement = "<<HANDLED-REPLACEMENT>>";

    /// <summary>
    /// The text only the FIRST facade sends, in the two-instance isolation test.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SecondFacadeInput"/> on purpose. With a shared input, a
    /// cross-contaminated pair of recorders could still hold a plausible-looking pair of recordings;
    /// with distinct inputs, contamination is visible in the recorded text itself.
    /// </remarks>
    private const string FirstFacadeInput = "only-the-first-facade-sent-this";

    /// <summary>
    /// The text only the SECOND facade sends, in the two-instance isolation test.
    /// </summary>
    private const string SecondFacadeInput = "only-the-second-facade-sent-this";

    /// <summary>
    /// A source value that belongs to neither framework constant, used to prove the three-argument
    /// overload forwards whatever it is handed rather than mapping it onto a known value.
    /// </summary>
    /// <remarks>
    /// The source space the framework defines is exactly two values,
    /// <see cref="Enums.I18N_SRC_PFW"/> and <see cref="Enums.I18N_SRC_CUSTOM"/>
    /// [<c>enums.sru</c>:L115-L116]. A caller-defined value outside that pair is legal precisely
    /// because the facade validates nothing, and this constant is how that is stated.
    /// </remarks>
    private const long CallerDefinedSource = 42L;

    // ==============================================================================================
    //  REFLECTION SCOPES
    //  --------------------------------------------------------------------------------------------
    //  Three of the facts below read compiled metadata rather than behaviour, and each needs a
    //  precisely stated scope. Naming the scopes once keeps every reflection call site short enough to
    //  read at a glance, and - more importantly - guarantees the three facts agree about what "declared
    //  by the type" means. `DeclaredOnly` is present in all three because an inherited `object` member
    //  is not something this suite has anything to say about; `NonPublic` is present in all three
    //  because a logging seam or an ambient slot would most naturally be private, which is exactly what
    //  source review misses and metadata does not.
    // ==============================================================================================

    /// <summary>
    /// Every member the type declares itself, of any accessibility and either storage class.
    /// </summary>
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every STATIC member the type declares itself - the scope in which an ambient provider slot, a
    /// static setter or a global accessor would have to appear.
    /// </summary>
    private const BindingFlags DeclaredStaticMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every INSTANCE member the type declares itself - the scope the ported provider slot lives in.
    /// </summary>
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // ==============================================================================================
    //  MEMBER DATA - table-driven rows per AAP §0.6.7
    //  --------------------------------------------------------------------------------------------
    //  Rows are supplied through `TheoryData<...>` returned from a static method, matching the
    //  convention the sibling suites in this folder already use. They are METHODS rather than
    //  properties so that a row set which needs to be derived rather than listed can be, without the
    //  call sites changing shape.
    // ==============================================================================================

    /// <summary>
    /// Every text shape the silent passthrough must preserve byte for byte.
    /// </summary>
    /// <returns>
    /// Eight rows: a plain ASCII probe, a real Chinese caption, the empty string, a whitespace-only
    /// string, the three marker-shaped probes, and <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The row set is deliberately broader than "a couple of strings". Each shape is one an
    /// implementation might treat specially, and treating ANY of them specially is the behavioural
    /// change C-B forbids: the empty string invites a null-coalesce, whitespace invites a trim, the
    /// non-ASCII row invites a normalisation, the marker-shaped trio invites a "looks like a sentinel
    /// already" short circuit, and <see langword="null"/> invites a substitution.
    /// </para>
    /// <para>
    /// The <see langword="null"/> row is the one the type system makes possible and the oracle
    /// requires: the guard at <c>i18n.srf</c>:L17 and :L21 tests the PROVIDER's validity and never the
    /// text, so a null text is ordinary input on this path. It is also why the element type is
    /// <c>string?</c> rather than <c>string</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<string?> PassthroughTextRows()
    {
        TheoryData<string?> rows = [];

        rows.Add(PlainAsciiText);
        rows.Add(ChineseText);
        rows.Add(string.Empty);
        rows.Add(WhitespaceOnlyText);
        rows.Add(QuestionMarksText);
        rows.Add(BracketMarkerText);
        rows.Add(BangMarkerText);

        // The null row goes in through an EXPLICITLY NULLABLE LOCAL rather than as a bare `null`
        // literal, and the reason is a real toolchain constraint rather than a style preference.
        // `TheoryData<T>.Add` accepts a `TheoryDataRow<T>` and relies on an implicit conversion from
        // T, so a bare `null` binds to the ROW parameter - which is non-nullable - and raises CS8625,
        // an error here because warnings are errors. The typed local makes the argument unambiguously
        // a null VALUE of the row's element type, which is the row this theory needs and the one the
        // oracle's guard makes legal: `i18n.srf`:L17 and :L21 test the PROVIDER's validity and never
        // the text. Handled properly rather than with a null-forgiving operator or a suppression.
        string? absentText = null;
        rows.Add(absentText);

        return rows;
    }

    /// <summary>
    /// The two shipped doubles that sit either side of the contract's return alphabet, each paired
    /// with the code it answers and the text the facade must hand back.
    /// </summary>
    /// <returns>
    /// Two rows - the handled-returning double answering <c>1</c> and rewriting the text, and the
    /// not-handled-returning double answering <c>0</c> and leaving it alone.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Both rows carry the SAME input and DIFFERENT expected output, and the difference is produced
    /// entirely by the provider. That is the shape that makes the discard assertable: the facade
    /// chose neither outcome, it simply returned what it was left with.
    /// </para>
    /// <para>
    /// The <c>expectedProviderCode</c> column is not decoration - the theory consuming these rows
    /// asserts it against the double itself, which is how the code is shown to be part of the
    /// contract even though the facade ignores it. The <c>providerName</c> column is likewise load
    /// bearing: the theory checks it against the double's runtime type name, so a future edit cannot
    /// silently swap one double for another and leave the row set describing the wrong pair.
    /// </para>
    /// </remarks>
    public static TheoryData<string, II18nProvider, long, string?, string?> DiscardedProviderReturnRows()
    {
        TheoryData<string, II18nProvider, long, string?, string?> rows = [];

        // Handled: answers 1 and rewrites the caller's variable, so the facade returns the rewrite.
        rows.Add(
            nameof(HandledProvider),
            new HandledProvider(HandledReplacement),
            1L,
            PlainAsciiText,
            HandledReplacement);

        // Not handled: answers 0 and touches nothing, so the facade returns the input - which is
        // indistinguishable from having installed no provider at all, and is meant to be.
        rows.Add(
            nameof(NotHandledProvider),
            new NotHandledProvider(),
            0L,
            PlainAsciiText,
            PlainAsciiText);

        return rows;
    }

    /// <summary>
    /// Source values a caller may hand to the three-argument overload, each of which must arrive at
    /// the provider unchanged.
    /// </summary>
    /// <returns>
    /// Three rows: both framework-defined sources and one value outside that pair.
    /// </returns>
    /// <remarks>
    /// <see cref="Enums.I18N_SRC_PFW"/> is included on purpose even though the two-argument overload
    /// supplies it automatically. Without that row, an implementation that ignored its own
    /// <c>source</c> parameter and always sent the framework constant would pass every remaining row
    /// of this theory while still being wrong - so the row is here to make the theory's other rows
    /// mean what they appear to mean.
    /// </remarks>
    public static TheoryData<long> CallerSuppliedSourceRows()
    {
        TheoryData<long> rows = [];

        rows.Add(Enums.I18N_SRC_PFW);
        rows.Add(Enums.I18N_SRC_CUSTOM);
        rows.Add(CallerDefinedSource);

        return rows;
    }

    // ==============================================================================================
    //  SECTION 1 - THE SILENT PASSTHROUGH                                       i18n.srf:L17-L18, :L21-L22
    //  --------------------------------------------------------------------------------------------
    //  OBLIGATION A. With NO provider installed the text comes back unchanged and NOTHING ELSE
    //  HAPPENS. The mechanical basis is a single fact about the oracle: `return text` at :L18 and
    //  :L22 sits OUTSIDE the `if IsValid(...)` guard on the line above it, so with no provider the
    //  body of each translating overload is nothing but the return.
    //
    //  C-B. This is INTENDED BEHAVIOUR, asserted here as CORRECT. It is not a gap to be hardened,
    //  and the tests below deliberately assert the ABSENCE of every hardening a reader might expect:
    //  no throw, no log, no marker, no null substitution, no diagnostic.
    //
    //  NOTE ON WHAT IS *NOT* ASSERTED HERE. Neither translating overload returns a code - both
    //  return `string?` - so no assertion in this section reads one. A reader looking for
    //  `Assert.Equal(RetCode..., facade.I18N(category, text))` has confused the translators with the
    //  installer; the installer is Section 2, and Section 3 proves by reflection that the published
    //  surface carries no code-returning translator at all.
    //
    //  AND NOTE THE ABSENCE OF `Assert.Throws` IN THIS ENTIRE SECTION. Every call below is made
    //  DIRECTLY, so an implementation that threw would fail the test naturally, at the call, with the
    //  real exception in the output. Wrapping any of them in `Assert.Throws` would invert the
    //  requirement into its opposite.
    // ==============================================================================================

    /// <summary>
    /// The two-argument overload hands the text straight back, byte for byte, when no provider is
    /// installed. [<c>i18n.srf</c>:L17-L18]
    /// </summary>
    /// <param name="text">One text shape from <see cref="PassthroughTextRows"/>.</param>
    /// <remarks>
    /// <para>
    /// The single most important behaviour of the type, and the one a "helpful" implementation breaks
    /// first - by throwing, by returning <see langword="null"/> so the caller "knows" translation did
    /// not happen, or by wrapping the text in a marker. A great many call sites in the estate feed
    /// this result straight into a message they are assembling, so any of those three would replace a
    /// caption with a fault.
    /// </para>
    /// <para>
    /// The comparison is ORDINAL and therefore byte-identical for a given encoding: no culture-aware
    /// collation, no normalisation form folding, no case insensitivity. That matters most for the
    /// Chinese row, where a culture-aware comparison could report equality across a re-encoding this
    /// suite exists to forbid.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PassthroughTextRows))]
    public void TheTwoArgumentOverloadReturnsTheTextUnchangedWhenNoProviderIsInstalled(string? text)
    {
        // No installer call. This facade has never been handed a provider, which is the state the
        // oracle's guard evaluates to false in.
        I18n facade = new();

        string? throughTheFacade = facade.I18N(Categories.CAT_DWSVC, text);

        AssertTextIsOrdinallyIdentical(text, throughTheFacade, "two-argument passthrough");

        // A second category, chosen to be a DIFFERENT non-zero value, because the passthrough must be
        // independent of the category: with no provider installed the category is never read at all.
        AssertTextIsOrdinallyIdentical(
            text,
            facade.I18N(Categories.CAT_MSGBOX, text),
            "two-argument passthrough, second category");

        // And the category value that collides with a source value [enums.sru:L115, :L118], to show
        // that nothing on this path is switching on either space.
        AssertTextIsOrdinallyIdentical(
            text,
            facade.I18N(Enums.I18N_CAT_WINDOW, text),
            "two-argument passthrough, colliding category");

        AssertReferenceIsUnchangedWhenPresent(text, throughTheFacade);
    }

    /// <summary>
    /// The three-argument overload does the same, for every source and category it is handed.
    /// [<c>i18n.srf</c>:L21-L22]
    /// </summary>
    /// <param name="text">One text shape from <see cref="PassthroughTextRows"/>.</param>
    /// <remarks>
    /// A SEPARATE code path from the overload above and therefore a separate theory rather than an
    /// extra assertion inside it. The two-argument overload delegates inward, so an implementation
    /// could conceivably satisfy one and not the other - most obviously by adding a guard to the
    /// arriving <c>source</c>, which only the three-argument overload lets a caller vary. Every source
    /// exercised below is legal precisely because this facade validates nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PassthroughTextRows))]
    public void TheThreeArgumentOverloadReturnsTheTextUnchangedWhenNoProviderIsInstalled(string? text)
    {
        I18n facade = new();

        string? throughTheFacade = facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, text);

        AssertTextIsOrdinallyIdentical(text, throughTheFacade, "three-argument passthrough");

        // The framework's own source, which is what the short overload supplies internally.
        AssertTextIsOrdinallyIdentical(
            text,
            facade.I18N(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, text),
            "three-argument passthrough, framework source");

        // A caller-defined source outside the two values the framework defines
        // [enums.sru:L115-L116], plus a negative category. Neither is validated, and this is where
        // that is stated.
        AssertTextIsOrdinallyIdentical(
            text,
            facade.I18N(CallerDefinedSource, -1L, text),
            "three-argument passthrough, unrecognised source and category");

        AssertReferenceIsUnchangedWhenPresent(text, throughTheFacade);
    }

    /// <summary>
    /// Translating with no provider installed does not throw - stated explicitly so the intent is
    /// legible rather than merely implied by the theories above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The theories already prove this in the strongest possible way: they call the facade directly,
    /// so an exception fails them at the call site with the real stack trace. This fact exists because
    /// "nothing thrown" is a REQUIREMENT rather than an incidental property, and a requirement that is
    /// only implied by the absence of a wrapper is easy to delete by accident.
    /// </para>
    /// <para>
    /// <c>Record.Exception</c> with <see cref="Assert.Null(object)"/> is used rather than
    /// <c>Assert.Throws</c>, which asserts the presence of a fault and would state the exact opposite
    /// of what the oracle does. The sweep covers both overloads across the awkward inputs together,
    /// because what callers rely on is that NO combination escapes.
    /// </para>
    /// </remarks>
    [Fact]
    public void TranslatingWithNoProviderInstalledDoesNotThrow()
    {
        // Locals rather than inline collection arguments, so no constant array is materialised at a
        // call site. The extremes are included deliberately: the facade neither validates nor ranges
        // either numeric argument, so `long.MinValue` and `long.MaxValue` are legal input and a
        // hypothetical bounds check would surface here rather than in a narrower sweep.
        string?[] texts =
        [
            null,
            string.Empty,
            WhitespaceOnlyText,
            PlainAsciiText,
            ChineseText,
            QuestionMarksText,
            BracketMarkerText,
            BangMarkerText,
        ];

        long[] sources =
        [
            Enums.I18N_SRC_PFW,
            Enums.I18N_SRC_CUSTOM,
            CallerDefinedSource,
            -1L,
            long.MinValue,
            long.MaxValue,
        ];

        long[] categories =
        [
            Enums.I18N_CAT_WINDOW,
            Enums.I18N_CAT_CUSTOM,
            Categories.CAT_MSGBOX,
            Categories.CAT_DWSVC,
            -1L,
            long.MaxValue,
        ];

        Exception? escaped = Record.Exception(() =>
        {
            I18n facade = new();

            foreach (string? text in texts)
            {
                foreach (long category in categories)
                {
                    _ = facade.I18N(category, text);

                    foreach (long source in sources)
                    {
                        _ = facade.I18N(source, category, text);
                    }
                }
            }
        });

        Assert.Null(escaped);
    }

    /// <summary>
    /// The silence is STRUCTURAL: the facade has no logging seam to be silent through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A behavioural assertion that "nothing was logged" is impossible without a logging seam to
    /// observe, and installing one purely to assert its disuse would itself be the new capability C-B
    /// forbids. The ABSENCE OF THE SEAM is therefore the strongest available proof - and it is not a
    /// compromise, because the absence IS the preserved legacy property: <c>i18n.srf</c> is
    /// twenty-five lines containing no logging of any kind, no trace call, no counter and no
    /// diagnostic output, and neither do the three translating and installing bodies it declares.
    /// </para>
    /// <para>
    /// Four independent facts are checked, each closing a different route by which a logger could
    /// arrive. Constructor injection is the idiomatic route; a field is the ambient route; a parameter
    /// on any member is the pass-it-in route; and an assembly reference is the route none of the other
    /// three could hide from, because a logger cannot be used without one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFacadeHasNoLoggingSeamSoTheSilenceIsStructural()
    {
        Type facade = typeof(I18n);

        // (a) Exactly one constructor, and it takes nothing. Nothing can be injected into a type with
        //     no way to accept it.
        ConstructorInfo constructor = Assert.Single(facade.GetConstructors());
        Assert.Empty(constructor.GetParameters());

        // (b) No field of any accessibility or storage class is a logger.
        FieldInfo[] fields = facade.GetFields(DeclaredMembers);

        Assert.DoesNotContain(fields, field => LooksLikeALoggingType(field.FieldType));

        // (c) No member accepts one either, published or not.
        MethodInfo[] methods = facade.GetMethods(DeclaredMembers);

        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter => LooksLikeALoggingType(parameter.ParameterType)));

        // (d) The declaring assembly references no logging library at all, which is the fact none of
        //     the above could be circumvented behind - the library declares zero package references.
        AssemblyName[] referenced = facade.Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(referenced);
        Assert.DoesNotContain(
            referenced,
            name => name.Name is not null
                && name.Name.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  SECTION 2 - THE INSTALLER OVERLOAD                                            i18n.srf:L12-L14
    //  --------------------------------------------------------------------------------------------
    //  OBLIGATION B. The third overload is not a translator at all: it is the installer that decides
    //  which provider the other two dispatch to, and it is the only one of the three that returns a
    //  code. Three lines of oracle, and the ORDER of the first two is the whole subtlety:
    //
    //      L12   if Not IsValid(n) then return RetCode.E_INVALID_OBJECT      <- returns FIRST
    //      L13   n_cst_i18n = n                                             <- assigns SECOND
    //      L14   return RetCode.OK
    //
    //  Because L12 returns BEFORE L13 assigns, a rejected installation cannot disturb an incumbent
    //  provider. An implementation that assigned first and validated afterwards would answer the very
    //  same code while silently uninstalling a working provider, and the only observable difference
    //  is the NEXT translation - which is exactly where the assertion is placed below.
    //
    //  WHAT "INVALID" MEANS IN THE PORT. The oracle gates on the PowerBuilder `IsValid` intrinsic,
    //  which reports whether a reference is live - created and not yet destroyed. .NET has no
    //  destroy-and-dangle state, so the exact managed equivalent of "created and not yet destroyed"
    //  is "not null", and the invalid case is therefore a null provider.
    //
    //  CODES ARE READ BY IDENTIFIER, NEVER AS NUMBERS (AAP §0.4.5.3). `RetCode.OK` and
    //  `RetCode.E_INVALID_OBJECT` [retcode.sru:L39, :L48] appear in serialized payloads, log records
    //  and characterization recordings, so the identifier is the stable reference and the integer
    //  behind it is an implementation detail of the algebra. Writing the number here would also pin
    //  the wrong thing: a suite that asserted `-5` would keep passing if the constant were renamed and
    //  would start failing if the algebra were renumbered, which is precisely backwards.
    // ==============================================================================================

    /// <summary>
    /// An INVALID provider is rejected with <see cref="RetCode.E_INVALID_OBJECT"/> and nothing else
    /// happens. [<c>i18n.srf</c>:L12]
    /// </summary>
    /// <remarks>
    /// <para>
    /// An invalid argument is EXPECTED INPUT here rather than a caller defect: the oracle tests for it
    /// and answers with a code, so the case is part of the published contract. That is also why the
    /// facade's parameter is <c>II18nProvider?</c> and why exercising the case needs no suppression.
    /// </para>
    /// <para>
    /// The null is supplied through an EXPLICITLY NULLABLE LOCAL rather than as a bare <c>null</c>
    /// literal, which is the correct handling of the nullable diagnostic under warnings-as-errors: the
    /// local's declared type states the intent at the point of declaration, no null-forgiving
    /// <c>!</c> operator is needed, and no suppression is applied to this file or any part of it.
    /// </para>
    /// <para>
    /// Rejection is also asserted to be REPEATABLE and non-latching: a second attempt answers the same
    /// code, and translation afterwards is still the untouched passthrough - so a failed install
    /// neither installs a null nor poisons the facade.
    /// </para>
    /// </remarks>
    [Fact]
    public void InstallingAnInvalidProviderAnswersInvalidObject()
    {
        I18n facade = new();

        II18nProvider? invalid = null;

        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(invalid));
        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(invalid));

        // Nothing was installed, so the facade is still in its passthrough state.
        AssertTextIsOrdinallyIdentical(
            PlainAsciiText,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "after a rejected install on a fresh facade");
    }

    /// <summary>
    /// A VALID provider is accepted with <see cref="RetCode.OK"/>, whichever implementation it is.
    /// [<c>i18n.srf</c>:L14]
    /// </summary>
    /// <remarks>
    /// All three shipped doubles are offered, and the same provider is offered twice, because the
    /// oracle's acceptance test is the validity of the reference and nothing else: it does not inspect
    /// the provider's type, ask it anything, or guard against re-installing the one already held.
    /// </remarks>
    [Fact]
    public void InstallingAValidProviderAnswersOk()
    {
        RecordingProvider recorder = new();

        Assert.Equal(RetCode.OK, new I18n().I18N(new HandledProvider()));
        Assert.Equal(RetCode.OK, new I18n().I18N(new NotHandledProvider()));
        Assert.Equal(RetCode.OK, new I18n().I18N(recorder));

        // The same instance, installed twice on one facade: still accepted, and not consulted by the
        // act of installing.
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        Assert.Equal(0, recorder.CallCount);
    }

    /// <summary>
    /// Installation is actually WIRED: the same call that passed text through untouched before
    /// installation returns the provider's replacement afterwards.
    /// </summary>
    /// <remarks>
    /// This is the test that separates "the installer accepted the provider" from "the installer
    /// installed the provider". An implementation that validated its argument, answered
    /// <see cref="RetCode.OK"/> and then discarded it would satisfy every code assertion in this
    /// section and fail only here - which is why the assertion is on the CHANGE across the install
    /// rather than on the return code, and why the before and after values are also asserted to
    /// DIFFER rather than only to match their expectations.
    /// </remarks>
    [Fact]
    public void InstallationChangesSubsequentTranslationResults()
    {
        I18n facade = new();

        string? beforeInstalling = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);
        AssertTextIsOrdinallyIdentical(PlainAsciiText, beforeInstalling, "before installing");

        Assert.Equal(RetCode.OK, facade.I18N(new HandledProvider(HandledReplacement)));

        string? afterInstalling = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);
        AssertTextIsOrdinallyIdentical(HandledReplacement, afterInstalling, "after installing");

        Assert.False(
            string.Equals(beforeInstalling, afterInstalling, StringComparison.Ordinal),
            "Installing a provider must change what the facade answers; it did not.");

        // The three-argument overload routes to the same installed provider, so the wiring is not
        // per-overload.
        AssertTextIsOrdinallyIdentical(
            HandledReplacement,
            facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, PlainAsciiText),
            "after installing, through the three-argument overload");
    }

    /// <summary>
    /// Installing a second provider REPLACES the first outright; it does not chain behind it.
    /// [<c>i18n.srf</c>:L13]
    /// </summary>
    /// <remarks>
    /// The oracle holds a single reference and overwrites it with a plain assignment, so there is no
    /// fallback chain to inherit and none may be introduced. Two recorders with distinguishable
    /// replacements make the replacement observable in the returned text; their CALL COUNTS then rule
    /// out the other shape a chain could take, where both providers are consulted and the first one's
    /// answer merely loses. Text equality alone could not tell those two apart.
    /// </remarks>
    [Fact]
    public void InstallingASecondProviderReplacesTheFirstOutright()
    {
        RecordingProvider first = new(returnCode: 1, replacement: FirstReplacement);
        RecordingProvider second = new(returnCode: 1, replacement: SecondReplacement);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(first));
        Assert.Equal(RetCode.OK, facade.I18N(second));

        AssertTextIsOrdinallyIdentical(
            SecondReplacement,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "after the second install");

        // The displaced provider was never reached - not before the second, and not as a fallback
        // after it.
        Assert.Equal(0, first.CallCount);
        Assert.Empty(first.Calls);

        Assert.Equal(1, second.CallCount);
        I18nTranslateCall onlyCall = Assert.Single(second.Calls);
        AssertTextIsOrdinallyIdentical(PlainAsciiText, onlyCall.Text, "the surviving provider's input");
    }

    /// <summary>
    /// A FAILED installation leaves the already-installed provider in place. [<c>i18n.srf</c>:L12
    /// returning before :L13 assigns]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The load-bearing consequence of the oracle's statement order, and the reason this is its own
    /// fact. An implementation that assigned first and validated afterwards answers the identical code
    /// while silently uninstalling a working provider, so the return code cannot distinguish the two
    /// implementations and the assertion must be on the NEXT translation.
    /// </para>
    /// <para>
    /// Both halves are asserted: that the rejection was reported, AND that translation still routes to
    /// the incumbent. Asserting only the second would pass against an implementation that accepted the
    /// null silently.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARejectedInstallationLeavesTheIncumbentProviderInPlace()
    {
        RecordingProvider incumbent = new(returnCode: 1, replacement: FirstReplacement);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(incumbent));

        AssertTextIsOrdinallyIdentical(
            FirstReplacement,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "before the rejected install");

        II18nProvider? invalid = null;
        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(invalid));

        // Still translating through the incumbent, so the rejection did not displace it - and it is
        // the SAME incumbent, which the growing call count proves rather than merely suggests.
        AssertTextIsOrdinallyIdentical(
            FirstReplacement,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "after the rejected install");

        Assert.Equal(2, incumbent.CallCount);
    }

    /// <summary>
    /// Two facade instances each keep their OWN provider: the slot is injected per instance, never
    /// shared. [<c>n_cst_i18n.sru</c>:L11 resolved per AAP §0.4.5.1]
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ASSERTION THAT PINS THE PORT'S CENTRAL STRUCTURAL DECISION. The legacy held a global
    /// auto-instance whose name shadows its own type name - <c>global n_cst_i18n n_cst_i18n</c> - which
    /// the installer assigned [<c>i18n.srf</c>:L13] and both translators read [:L17, :L21]. AAP
    /// §0.4.5.1 rules that where a legacy global auto-instance shadows its own type name, the TYPE
    /// keeps the descriptive .NET name and the INSTANCE becomes an injected dependency rather than a
    /// global. So the provider must be reachable ONLY through an instance: there is no static setter,
    /// no ambient accessor, and no <c>Current</c>, <c>Default</c> or <c>Instance</c> holder anywhere.
    /// </para>
    /// <para>
    /// Two independent facades are given one recorder EACH and exercised with DISTINCT texts, and each
    /// recorder must hold only its own call. A static mutable slot cross-contaminates them - whichever
    /// installed last would serve both - and this test is what makes that failure loud. The distinct
    /// texts matter: with a shared input, a cross-contaminated pair could still produce a plausible
    /// pair of recordings.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoFacadeInstancesEachKeepTheirOwnProvider()
    {
        RecordingProvider firstRecorder = new(returnCode: 1, replacement: FirstReplacement);
        RecordingProvider secondRecorder = new(returnCode: 1, replacement: SecondReplacement);

        I18n firstFacade = new();
        I18n secondFacade = new();

        Assert.Equal(RetCode.OK, firstFacade.I18N(firstRecorder));
        Assert.Equal(RetCode.OK, secondFacade.I18N(secondRecorder));

        AssertTextIsOrdinallyIdentical(
            FirstReplacement,
            firstFacade.I18N(Categories.CAT_DWSVC, FirstFacadeInput),
            "the first facade's own provider");

        AssertTextIsOrdinallyIdentical(
            SecondReplacement,
            secondFacade.I18N(Categories.CAT_MSGBOX, SecondFacadeInput),
            "the second facade's own provider");

        // Each recorder saw exactly its own call, with its own text and its own category.
        I18nTranslateCall firstCall = Assert.Single(firstRecorder.Calls);
        AssertTextIsOrdinallyIdentical(FirstFacadeInput, firstCall.Text, "the first recorder's only call");
        Assert.Equal(Categories.CAT_DWSVC, firstCall.Category);

        I18nTranslateCall secondCall = Assert.Single(secondRecorder.Calls);
        AssertTextIsOrdinallyIdentical(SecondFacadeInput, secondCall.Text, "the second recorder's only call");
        Assert.Equal(Categories.CAT_MSGBOX, secondCall.Category);

        // A third facade, never handed anything, is unaffected by either installation - the state is
        // per instance rather than per type.
        AssertTextIsOrdinallyIdentical(
            PlainAsciiText,
            new I18n().I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "a facade that was never handed a provider");
    }

    /// <summary>
    /// The facade declares NO static state and no static member, so there is nowhere for an ambient
    /// provider slot to live.
    /// </summary>
    /// <remarks>
    /// The behavioural companion to the two-instance test above, and stronger in one specific way: the
    /// behavioural test would also fail if a static slot were introduced, but it could not say WHY.
    /// This one names the shape that is forbidden. A static field is the ambient slot itself; a static
    /// property or method is the setter or accessor that would reach it. Reading compiled metadata
    /// covers private members too, which source review can miss.
    /// </remarks>
    [Fact]
    public void TheFacadeDeclaresNoStaticStateForTheProviderSlot()
    {
        Type facade = typeof(I18n);

        Assert.Empty(facade.GetFields(DeclaredStaticMembers));
        Assert.Empty(facade.GetProperties(DeclaredStaticMembers));
        Assert.Empty(facade.GetMethods(DeclaredStaticMembers));
        Assert.Empty(facade.GetEvents(DeclaredStaticMembers));

        // The instance state is exactly one field - the ported slot - and it is not static.
        FieldInfo slot = Assert.Single(facade.GetFields(DeclaredInstanceMembers));

        Assert.False(slot.IsStatic);
        Assert.False(slot.IsPublic, "The ported global slot must not be publicly reachable.");
        Assert.Equal(typeof(II18nProvider), slot.FieldType);
    }

    // ==============================================================================================
    //  SECTION 3 - THE DISCARDED PROVIDER RETURN                          C-K RECORD, AT ITS COVERAGE
    //  --------------------------------------------------------------------------------------------
    //  OBLIGATION C, and the decision this suite is required to NAME where it covers it (C-K):
    //
    //    * `ws_objects/pfw.ui.pbl.src/i18n.srf`:L17 and :L21 CALL THE PROVIDER EVENT as a bare
    //      statement - `n_cst_i18n.Event OnTranslate(...)` - and the very next line, :L18 and :L22
    //      respectively, IMMEDIATELY EXECUTES `return text` WITHOUT CAPTURING THE `long`. There is no
    //      variable to hold it, no `choose case` over it, and no branch that consults it.
    //
    //    * THE `long` IS NONETHELESS PART OF `II18nProvider`'S PUBLISHED CONTRACT. The legacy declares
    //      the event as `event type long ontranslate ( long source, long category, ref string text )`
    //      [`n_cst_i18n.sru`:L9], and the alphabet is documented identically in the doc block all three
    //      shipped providers carry - 1 means handled, 0 means not handled. The value is real, every
    //      provider produces it, and the port keeps it on the interface for exactly that reason.
    //
    //    * THIS SUITE THEREFORE ASSERTS THE CODE ON THE *PROVIDER* AND THE TEXT ON THE *FACADE*. That
    //      split is the only one that states both halves truthfully: the contract carries a code, and
    //      the facade does not read it. Asserting the code THROUGH the facade is impossible, and
    //      asserting that the facade surfaces it would be asserting a behaviour the oracle does not
    //      have.
    //
    //  TWO CONSEQUENCES THAT FOLLOW, AND ARE ASSERTED RATHER THAN ASSUMED. A provider answering 0 is
    //  INDISTINGUISHABLE from no provider at all, because both paths reach the same unconditional
    //  return. And a provider that mutates `text` while answering 0 has its mutation honoured, because
    //  the facade never consults the code - the `ref` argument is the channel, and the code is not.
    // ==============================================================================================

    /// <summary>
    /// Whatever code the provider answers, the facade returns the text the provider left behind.
    /// [<c>i18n.srf</c>:L17-L18, :L21-L22]
    /// </summary>
    /// <param name="providerName">The double's own type name, checked against the instance supplied.</param>
    /// <param name="provider">The double under test, offered through the contract's own type.</param>
    /// <param name="expectedProviderCode">The code the double answers - asserted on the DOUBLE.</param>
    /// <param name="input">The text handed to the facade.</param>
    /// <param name="expectedText">The text the facade must hand back - asserted on the FACADE.</param>
    /// <remarks>
    /// <para>
    /// Both rows carry the SAME input and DIFFERENT expected output, and the entire difference is
    /// produced by the provider. That is what makes the discard assertable rather than merely stated:
    /// the facade selected neither outcome; it returned what it was left with.
    /// </para>
    /// <para>
    /// The <paramref name="expectedProviderCode"/> assertion is made against the double invoked
    /// DIRECTLY THROUGH AN <see cref="II18nProvider"/>-TYPED REFERENCE, never through the facade - the
    /// facade has no route by which the code could be observed, which is the whole point. Doing both
    /// in one theory puts the two halves of the C-K record side by side: the same call that produced
    /// the facade's answer also produced a code the facade never saw.
    /// </para>
    /// <para>
    /// <paramref name="providerName"/> is asserted against the instance's runtime type name so a future
    /// edit cannot swap one double for another and leave the row set describing the wrong pair.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DiscardedProviderReturnRows))]
    public void TheFacadeReturnsTheTextTheProviderLeftWhateverCodeItAnswered(
        string providerName,
        II18nProvider provider,
        long expectedProviderCode,
        string? input,
        string? expectedText)
    {
        Assert.Equal(providerName, provider.GetType().Name, StringComparer.Ordinal);

        // THE FACADE HALF. Install the double, translate, and take the answer.
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(provider));

        AssertTextIsOrdinallyIdentical(
            expectedText,
            facade.I18N(Categories.CAT_DWSVC, input),
            $"{providerName} through the two-argument overload");

        AssertTextIsOrdinallyIdentical(
            expectedText,
            facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, input),
            $"{providerName} through the three-argument overload");

        // THE PROVIDER HALF. The same double, reached through the contract rather than the facade,
        // reports a code - and the facade's answer above was identical either way. The `ref` local is
        // the channel the translation travels through, exactly as `text` is at i18n.srf:L17 and :L21.
        string? throughTheContract = input;
        long reported = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref throughTheContract);

        Assert.Equal(expectedProviderCode, reported);
        AssertTextIsOrdinallyIdentical(
            expectedText,
            throughTheContract,
            $"{providerName} through the contract directly");
    }

    /// <summary>
    /// The two doubles report the contract's own codes - <c>1</c> for handled and <c>0</c> for not
    /// handled - when invoked directly through an <see cref="II18nProvider"/>-typed reference.
    /// [<c>n_cst_i18n.sru</c>:L9]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated separately and directly, because this is the fact that shows the code is part of the
    /// CONTRACT even though the facade ignores it. If it were only asserted inside the theory above, a
    /// reader could reasonably conclude the code was an artefact of the fixtures rather than a
    /// published member of the interface.
    /// </para>
    /// <para>
    /// The references are declared as <see cref="II18nProvider"/> rather than as the concrete double
    /// types on purpose: it is the INTERFACE that publishes the <see langword="long"/>, so invoking
    /// through the interface is what proves a caller of the contract can see it. Invoking through
    /// <c>HandledProvider</c> directly would prove only that one class returns a number.
    /// </para>
    /// <para>
    /// The bare numerals <c>1</c> and <c>0</c> appear here as literals rather than as named constants,
    /// matching the oracle, which returns them as bare literals with no enum, no boolean and no result
    /// record. An abstraction over the numeric code is precisely the improvement C-B forbids, and the
    /// numerals are what travel in characterization recordings.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothDoublesReportTheContractsOwnCodesThroughAnII18nProviderReference()
    {
        II18nProvider handled = new HandledProvider(HandledReplacement);

        string? handledText = PlainAsciiText;
        Assert.Equal(1L, handled.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref handledText));
        AssertTextIsOrdinallyIdentical(HandledReplacement, handledText, "the handled double rewrote the text");

        II18nProvider notHandled = new NotHandledProvider();

        string? notHandledText = PlainAsciiText;
        Assert.Equal(0L, notHandled.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref notHandledText));
        AssertTextIsOrdinallyIdentical(PlainAsciiText, notHandledText, "the declining double left the text alone");

        // And the consequence the facade's indifference produces: a provider answering 0 leaves the
        // caller in exactly the same place as having installed nothing at all. Asserted as an equality
        // BETWEEN the two facades, so it fails if either half drifts rather than only if one is wrong.
        I18n withDecliningProvider = new();
        Assert.Equal(RetCode.OK, withDecliningProvider.I18N(new NotHandledProvider()));

        I18n withNoProvider = new();

        AssertTextIsOrdinallyIdentical(
            withNoProvider.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            withDecliningProvider.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "a declining provider versus no provider");
    }

    /// <summary>
    /// A provider that mutates the text while answering NOT HANDLED still has its mutation returned,
    /// because the facade never consults the code.
    /// </summary>
    /// <remarks>
    /// Not a supported provider behaviour, and not presented as one - it is simply what the facade
    /// does, and pinning it is what stops a future author from adding an "only take the text when the
    /// code is 1" guard that the oracle does not have. The recorder is the double that can be put into
    /// this state, by writing a replacement and then being reconfigured to report a code outside the
    /// contract's alphabet entirely.
    /// </remarks>
    [Fact]
    public void AMutationIsHonouredEvenWhenTheProviderDoesNotReportHandled()
    {
        RecordingProvider recorder = new(returnCode: 1, replacement: HandledReplacement);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        AssertTextIsOrdinallyIdentical(
            HandledReplacement,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "reporting handled");

        // Values outside the alphabet are neither validated nor interpreted by the facade. The
        // recorder writes only when its code is exactly 1, so these rows return the input - and the
        // facade reached that answer without ever reading the code that produced it.
        recorder.ReturnCode = -1L;
        AssertTextIsOrdinallyIdentical(
            PlainAsciiText,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "reporting a negative code");

        recorder.ReturnCode = 99L;
        AssertTextIsOrdinallyIdentical(
            PlainAsciiText,
            facade.I18N(Categories.CAT_DWSVC, PlainAsciiText),
            "reporting an out-of-alphabet code");

        Assert.Equal(3, recorder.CallCount);
    }

    /// <summary>
    /// NO published overload surfaces the provider's return code, by any route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DELIBERATE, per C-K, and asserted against compiled metadata so it constrains the SHAPE of the
    /// published surface rather than the behaviour of one call. The published surface is exactly three
    /// overloads of one name [<c>i18n.srf</c>:L7-L9]: the installer returns a code and takes a
    /// provider, and the two translators return text. There is no fourth overload returning a tuple, no
    /// <c>out</c> parameter carrying a code alongside the text, no property latching the last result,
    /// and no event announcing one.
    /// </para>
    /// <para>
    /// Each clause below closes a specific route. Return type closes the obvious one. <c>out</c> and
    /// <c>ref</c> parameters close the side-channel; the only by-reference parameters permitted are the
    /// <see langword="long"/> renderings of the oracle's <c>readonly</c> source and category
    /// [AAP §0.4.5.2 maps <c>readonly</c> to <c>in</c>], so a by-reference parameter of any other type
    /// would be a channel the oracle does not have. The member count closes the "add a
    /// <c>TryTranslate</c> alongside" route. Properties and events close the "latch it for later"
    /// route.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPublishedOverloadSurfacesTheProvidersReturnCode()
    {
        Type facade = typeof(I18n);

        MethodInfo[] published = facade.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        // Exactly three, all spelled the same, matching the oracle's three prototypes.
        Assert.Equal(3, published.Length);
        Assert.All(published, method => Assert.Equal(nameof(I18n.I18N), method.Name, StringComparer.Ordinal));

        // The installer is the ONLY member that returns a code, and it takes the provider.
        MethodInfo installer = Assert.Single(published, method => method.GetParameters().Length == 1);
        Assert.Equal(typeof(long), installer.ReturnType);
        Assert.Equal(typeof(II18nProvider), Assert.Single(installer.GetParameters()).ParameterType);

        // The two translators return TEXT, and neither offers a second channel for a code.
        MethodInfo[] translators = published
            .Where(method => method.GetParameters().Length > 1)
            .ToArray();

        Assert.Equal(2, translators.Length);

        Assert.All(translators, translator =>
        {
            Assert.Equal(typeof(string), translator.ReturnType);

            ParameterInfo[] parameters = translator.GetParameters();

            // No `out` parameter anywhere - nothing is handed back other than the return value.
            Assert.DoesNotContain(parameters, parameter => parameter.IsOut);

            // The only by-reference parameters are the `in long` renderings of `readonly long`.
            Assert.DoesNotContain(
                parameters,
                parameter => parameter.ParameterType.IsByRef
                    && parameter.ParameterType.GetElementType() != typeof(long));

            // The text channel is the last parameter and travels BY VALUE, exactly as `text` does in
            // the oracle's prototypes - it is the one parameter neither prototype marks `readonly`.
            ParameterInfo textParameter = parameters[^1];
            Assert.Equal(typeof(string), textParameter.ParameterType);
            Assert.False(textParameter.ParameterType.IsByRef);
        });

        // Nothing latches a result for a caller to read afterwards.
        Assert.Empty(facade.GetProperties(DeclaredMembers));
        Assert.Empty(facade.GetEvents(DeclaredMembers));
    }

    // ==============================================================================================
    //  SECTION 4 - WHICH SOURCE EACH OVERLOAD SUPPLIES              i18n.srf:L17 versus :L21
    //  --------------------------------------------------------------------------------------------
    //  The one property that distinguishes the two translating overloads from outside. :L17 HARDCODES
    //  `Enums.I18N_SRC_PFW`; :L21 forwards the caller's `source` unchanged. Both overloads can produce
    //  IDENTICAL text, so the returned string cannot tell them apart and only a recording of the
    //  `source` argument can. Getting it wrong would silently break every caller relying on a
    //  caller-defined source reaching the provider - and, in the other direction, would make every
    //  framework lookup decline, since a provider's first act is to filter on the source.
    //
    //  THE HAZARD THAT DICTATES THE CATEGORY USED IN THIS SECTION. Verified in
    //  `ws_objects/pfw.shared.pbl.src/enums.sru`:L115-L123, the source space and the category space
    //  OVERLAP:
    //
    //      Enums.I18N_SRC_PFW    = 0          Enums.I18N_CAT_WINDOW     = 0
    //      Enums.I18N_SRC_CUSTOM = 1          Enums.I18N_CAT_TABCONTROL = 1
    //
    //  A recorded pair of (0, 0) is therefore consistent with an implementation that read the SOURCE
    //  twice, or the CATEGORY twice, or swapped them - so a suite reaching for the two values a
    //  careless fixture picks first could not tell any of those apart. The same trap is set at (1, 1).
    //  Every row in this section consequently uses the NON-ZERO `Categories.CAT_DWSVC`, and the tests
    //  additionally assert that the recorded source and category DIFFER, which is what makes the
    //  disambiguation explicit rather than incidental.
    // ==============================================================================================

    /// <summary>
    /// The two-argument overload supplies the FRAMEWORK's own source. [<c>i18n.srf</c>:L17]
    /// </summary>
    /// <remarks>
    /// The hardcoded source is the whole reason the short overload exists: framework call sites pass a
    /// category and a string and mean "this text is the framework's own", which is precisely the filter
    /// every shipped provider applies first. An implementation that sent
    /// <see cref="Enums.I18N_SRC_CUSTOM"/> here would make every framework lookup decline while
    /// changing no return value that a text assertion could see - so the recorded source is the only
    /// place the mistake is visible.
    /// </remarks>
    [Fact]
    public void TheTwoArgumentOverloadSuppliesTheFrameworkSource()
    {
        RecordingProvider recorder = new();

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        _ = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);

        Assert.Equal(1, recorder.CallCount);
        I18nTranslateCall call = Assert.Single(recorder.Calls);

        Assert.Equal(Enums.I18N_SRC_PFW, call.Source);
        Assert.Equal(Categories.CAT_DWSVC, call.Category);
        AssertTextIsOrdinallyIdentical(PlainAsciiText, call.Text, "the recorded lookup key");

        // Stated explicitly because the two source constants are adjacent values and a transposition
        // would otherwise be invisible.
        Assert.NotEqual(Enums.I18N_SRC_CUSTOM, call.Source);

        // And the disambiguation itself: the recorded pair could not have come from reading one
        // argument twice, because the two recorded values differ.
        Assert.NotEqual(call.Source, call.Category);
    }

    /// <summary>
    /// The three-argument overload supplies whatever source the CALLER passed. [<c>i18n.srf</c>:L21]
    /// </summary>
    /// <param name="source">One source value from <see cref="CallerSuppliedSourceRows"/>.</param>
    /// <remarks>
    /// The row set spans both framework-defined sources and one value outside that pair, because the
    /// facade validates nothing: it neither clamps the source to the known pair nor maps an unknown
    /// value onto a default. Including <see cref="Enums.I18N_SRC_PFW"/> is what makes the other rows
    /// mean what they appear to - without it, an implementation that ignored its own parameter and
    /// always sent the framework constant would pass every remaining row.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CallerSuppliedSourceRows))]
    public void TheThreeArgumentOverloadSuppliesTheCallersSource(long source)
    {
        RecordingProvider recorder = new();

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        _ = facade.I18N(source, Categories.CAT_DWSVC, PlainAsciiText);

        Assert.Equal(1, recorder.CallCount);
        I18nTranslateCall call = Assert.Single(recorder.Calls);

        Assert.Equal(source, call.Source);
        Assert.Equal(Categories.CAT_DWSVC, call.Category);
        AssertTextIsOrdinallyIdentical(PlainAsciiText, call.Text, "the recorded lookup key");

        // The category is a non-zero value distinct from every row's source, so no recorded pair here
        // is consistent with reading one argument twice.
        Assert.NotEqual(call.Source, call.Category);
    }

    /// <summary>
    /// The two overloads are distinguishable ONLY by the recorded source: same provider, same category,
    /// same text in and same text out, different source.
    /// </summary>
    /// <remarks>
    /// The statement of why <c>RecordingProvider</c> has to exist at all, made as a single ordered
    /// recording rather than as two separate facts. Both calls produce the identical answer, so any
    /// assertion on the returned text would pass against an implementation that had collapsed the two
    /// overloads into one - and the collapse would take the caller's source with it.
    /// </remarks>
    [Fact]
    public void TheTwoOverloadsDifferOnlyInTheSourceTheySupply()
    {
        RecordingProvider recorder = new(returnCode: 1, replacement: HandledReplacement);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        string? throughTheShortOverload = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);
        string? throughTheLongOverload =
            facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, PlainAsciiText);

        // Identical answers - which is exactly why the returned text cannot tell them apart.
        AssertTextIsOrdinallyIdentical(HandledReplacement, throughTheShortOverload, "the short overload");
        AssertTextIsOrdinallyIdentical(HandledReplacement, throughTheLongOverload, "the long overload");

        Assert.Equal(2, recorder.CallCount);

        // In order: the framework's source first, the caller's second.
        Assert.Equal(Enums.I18N_SRC_PFW, recorder.Calls[0].Source);
        Assert.Equal(Enums.I18N_SRC_CUSTOM, recorder.Calls[1].Source);

        // Everything else about the two calls is the same, which is what isolates the difference.
        Assert.Equal(Categories.CAT_DWSVC, recorder.Calls[0].Category);
        Assert.Equal(Categories.CAT_DWSVC, recorder.Calls[1].Category);
        AssertTextIsOrdinallyIdentical(recorder.Calls[0].Text, recorder.Calls[1].Text, "the recorded keys");
    }

    /// <summary>
    /// Each translate call dispatches to the provider EXACTLY ONCE - no retry, no double dispatch, and
    /// no second attempt when the provider declines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle invokes the event as a single bare statement, so one call in is one call out. A retry
    /// would be invisible for a pure provider and catastrophic for one with a side effect, and a second
    /// dispatch on a decline is exactly the "helpful fallback" a future author might add - which is why
    /// the count is asserted after a DECLINE specifically, the case where a retry would look reasonable.
    /// </para>
    /// <para>
    /// The first assertion carries a second meaning that is easy to miss. The two-argument overload
    /// delegates INWARD to the three-argument one, so an implementation that dispatched in both bodies
    /// would translate correctly and consult the provider twice. A count of one after a single
    /// two-argument call is what rules that out.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachTranslateCallDispatchesToTheProviderExactlyOnce()
    {
        RecordingProvider recorder = new(returnCode: 0);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        // One call through the DELEGATING overload: one dispatch, not two.
        _ = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);
        Assert.Equal(1, recorder.CallCount);

        // The provider declined, and nothing tried again.
        _ = facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, PlainAsciiText);
        Assert.Equal(2, recorder.CallCount);

        // A handled answer is likewise dispatched once, so the count does not depend on the code.
        recorder.ReturnCode = 1L;
        recorder.Replacement = HandledReplacement;

        _ = facade.I18N(Categories.CAT_DWSVC, PlainAsciiText);
        Assert.Equal(3, recorder.CallCount);

        // A null lookup key does not short-circuit the dispatch either: the oracle's guard tests the
        // PROVIDER's validity and never the text [i18n.srf:L17, :L21].
        _ = facade.I18N(Categories.CAT_DWSVC, null);
        Assert.Equal(4, recorder.CallCount);
        Assert.Null(recorder.Calls[3].Text);
    }

    // ==============================================================================================
    //  ASSERTION HELPERS
    //  --------------------------------------------------------------------------------------------
    //  Private, static, and deliberately thin: each one states a property this suite asserts many
    //  times, so that the property is defined once and every call site reads as the intent rather than
    //  as the mechanics. Neither helper contains a conditional that could make an assertion vanish
    //  silently - the one branch below asserts on BOTH arms.
    // ==============================================================================================

    /// <summary>
    /// Asserts that two texts are ORDINALLY identical - byte for byte, for a given encoding.
    /// </summary>
    /// <param name="expected">The text that must have survived.</param>
    /// <param name="actual">The text that came back.</param>
    /// <param name="what">A short description of the call, so a failure says which one broke.</param>
    /// <remarks>
    /// <para>
    /// Two assertions rather than one, and both are load bearing. <see cref="Assert.Equal(string?,
    /// string?)"/> is xunit's string comparison and is the one that produces a readable diff on
    /// failure, which is what makes a broken row diagnosable. The second names the comparison
    /// explicitly, so the ORDINAL requirement is legible in this source rather than inherited from the
    /// assertion library's default - and would survive that default changing.
    /// </para>
    /// <para>
    /// <c>Assert.Equal(expected, actual, StringComparer.Ordinal)</c> is deliberately NOT used, even
    /// though it looks like the direct expression of the same idea:
    /// <see cref="StringComparer"/> implements <see cref="IEqualityComparer{T}"/> of <c>string?</c>, and
    /// passing it where a comparer of non-nullable <c>string</c> is expected risks CS8620, which under
    /// this repository's warnings-as-errors setting is a build break rather than a note. Ordinality is
    /// therefore obtained through <see cref="StringComparison.Ordinal"/>, which carries no such
    /// nullability edge.
    /// </para>
    /// <para>
    /// Culture-aware comparison would be actively wrong here, not merely unnecessary: it can report
    /// equality across a re-encoding or a normalisation-form change, which is exactly the mutation the
    /// passthrough rows exist to forbid.
    /// </para>
    /// </remarks>
    private static void AssertTextIsOrdinallyIdentical(string? expected, string? actual, string what)
    {
        Assert.Equal(expected, actual);

        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{what}: the text was not ordinally identical to what was supplied.");
    }

    /// <summary>
    /// Strengthens the passthrough by asserting that the very same string INSTANCE came back when one
    /// was supplied.
    /// </summary>
    /// <param name="supplied">The text handed to the facade.</param>
    /// <param name="returned">The text the facade returned.</param>
    /// <remarks>
    /// <para>
    /// An ADDITIONAL assertion, never a replacement for ordinal equality. Reference identity looks like
    /// the stronger signal and is in fact the weaker one: string interning means two independently
    /// produced literals can be the same instance, so identity can hold for a value that was rebuilt
    /// rather than passed through. Equality is what the requirement is about; identity merely rules out
    /// a copy having been made along the way.
    /// </para>
    /// <para>
    /// The <see langword="null"/> arm asserts too, rather than skipping: a supplied
    /// <see langword="null"/> must come back as <see langword="null"/>, which is the substitution a
    /// "be safe" implementation would make.
    /// </para>
    /// </remarks>
    private static void AssertReferenceIsUnchangedWhenPresent(string? supplied, string? returned)
    {
        if (supplied is null)
        {
            Assert.Null(returned);
            return;
        }

        Assert.Same(supplied, returned);
    }

    /// <summary>
    /// Reports whether a type is, contains, or comes from a logging abstraction.
    /// </summary>
    /// <param name="candidate">The field or parameter type being inspected.</param>
    /// <returns>
    /// <see langword="true"/> when the type's own name mentions a logger, when it is declared in a
    /// logging namespace, or when any of its generic arguments is either of those.
    /// </returns>
    /// <remarks>
    /// Three checks rather than one, because a logging seam can arrive in three shapes: the type
    /// itself; a type from a logging namespace whose name does not say so, such as a log-level or
    /// scope type; and a generic wrapper around one, such as a factory or an accessor. By-reference,
    /// array and pointer forms are unwrapped first so a seam cannot hide behind one. Recursion
    /// terminates because generic arguments are strictly smaller than the type that holds them, and a
    /// bare generic parameter has none.
    /// </remarks>
    private static bool LooksLikeALoggingType(Type candidate)
    {
        Type inspected = candidate.HasElementType
            ? candidate.GetElementType() ?? candidate
            : candidate;

        if (inspected.Name.Contains("Logger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? declaringNamespace = inspected.Namespace;
        if (declaringNamespace is not null
            && declaringNamespace.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (Type argument in inspected.GetGenericArguments())
        {
            if (LooksLikeALoggingType(argument))
            {
                return true;
            }
        }

        return false;
    }
}
