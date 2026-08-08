// ==============================================================================================
//  SprintfTests - the parity suite for PowerFramework.Shared.Kernel.Formatting.Sprintf
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  PowerFramework.Shared.Kernel.Formatting.Sprintf, in
//                   shared/PowerFramework.Shared.Kernel/Formatting.cs
//
//  ORACLE           ws_objects/pfw.common.pbl.src/sprintf.srf                 (the declaration)
//                   ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru  (the named case)
//                   ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru (its twin)
//                   plus the 57 live Sprintf call sites across ws_objects/
//
//  THE ONE FACT THAT SHAPES EVERY ROW IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  sprintf.srf:L3 declares
//
//      global type sprintf from function_object native "pfw.dll"
//
//  and the file then ends after 21 forward prototypes at L7-L27 and an `end prototypes` at L28.
//  THERE IS NO POWERSCRIPT BODY. The implementation lives inside the closed pfw.dll and no C++
//  source for that binary is anywhere in this checkout, so it cannot be read - only inferred. The
//  prototypes carry TYPES ONLY: the format is `readonly string` and every value parameter is
//  `any`. They say nothing whatsoever about the grammar, so this suite does not try to derive it
//  from them.
//
//  (An aside on those prototypes, recorded because it explains a shape rather than driving a test:
//  sprintf.srf:L24 declares `any param17` TWICE in one parameter list, and L25, L26 and L27 each
//  keep the duplicate before appending param18, param19 and param20 - so the widest form has 21
//  parameters but only 20 DISTINCT names. The arity ladder is malformed in the export. It is a
//  hand-unrolled variadic workaround for a PowerScript limitation, which is exactly why the port
//  collapses all 21 overloads into one `params` method and has no duplicate name left to
//  reproduce.)
//
//  THE GRAMMAR IS NOT PURELY GUESSWORK: THE LEGACY TREE DOCUMENTS IT
//  --------------------------------------------------------------------------------------------
//  This is the single most useful thing discovered while writing this suite, and it materially
//  raises the evidence available for several rows below. The framework's own authors wrote the
//  grammar down, in Simplified Chinese, as the opening comments of a demo event handler:
//
//      ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L100-L104
//
//          L100  reference syntax: {index[,alignment][:format]}
//          L101  index is the parameter index, STARTING AT 1
//          L102  alignment is the padding width; when the value is shorter than the width it is
//                padded with white space; a POSITIVE alignment RIGHT-aligns and a NEGATIVE
//                alignment LEFT-aligns
//          L103  format is the value's display format - refer to the String() function's format
//                documentation
//          L104  use the backslash '\' to escape the braces '{' and '}'
//
//  Four of those five lines corroborate the as-built port exactly, and they turn several
//  properties from inferred into DOCUMENTED - most importantly the one-based index and the sign
//  convention on alignment. The fifth, L104, does NOT match the port, and that divergence is
//  reported in full below rather than smoothed over.
//
//  WHAT w_test_logger.srw ACTUALLY CONTAINS, stated precisely because this file cites it repeatedly
//  and a loose citation would be worse than none. That grammar note is not a one-off: the same five
//  comment lines are repeated FIVE times in the file, at L100-L104, L163-L167, L191-L195,
//  L219-L223 and L246-L250, once above each demonstration. Below them sit five format literals:
//
//      L107  the format argument of a genuine `Sprintf(` call at L106
//      L171  the format argument of `_logger.Debug(`   at L170
//      L198  the format argument of `_logger.Error(`   at L197
//      L226  the format argument of `_logger.Warning(` at L225
//      L253  the format argument of `_logger.Info(`    at L252
//
//  Only L107 is a `Sprintf(` call site and it is counted as one in the census below; the other four
//  are pfwLogger calls. They are still first-class GRAMMAR evidence rather than a stretch, for two
//  independent reasons: n_logger.sru declares its level methods as
//  `(readonly string fmt, any param1, ... any paramN)` - the identical hand-unrolled variadic shape
//  as sprintf.srf, so the logger consumes this same dialect - and the framework's authors printed
//  the grammar note directly above each one. Where this file cites L171 and its siblings it means
//  "a format literal written by the framework's authors in this dialect", not "a Sprintf call
//  site", and the distinction is preserved at each citation.
//
//  ALSO WORTH RECORDING, because it bounds what could have been used: there is NO `w_test_sprintf`
//  among the 47 `w_test_*.srw` windows, so no window characterizes this function directly.
//  w_test_logger.srw is the closest thing that exists, which is exactly why it is load-bearing
//  here. (One kernel primitive does have a window - w_test_assert.srw - so the absence is specific
//  to this function rather than a blanket property of the test library.) The oracle for this suite
//  is therefore the 57 call sites, the 21 prototypes and that one documented demonstration, and
//  nothing else in the repository can adjudicate a disagreement.
//
//  Rows in this file are therefore labelled with the strength of their evidence, and getting a
//  label wrong in either direction is a real failure rather than a documentation nicety.
//  Labelling an unobserved input "verified" fabricates legacy behaviour nothing can support.
//  Labelling a verified form "inferred" invites a later author to change something that live call
//  sites depend on.
//
//      [VERIFIED @ <locator>]     a form a live legacy call site uses; the locator is the proof.
//      [DOCUMENTED @ <locator>]   a property the legacy's own grammar note states in words.
//      [DERIVED]                  follows necessarily from a documented property, though no call
//                                 site spells this particular literal.
//      [CHARACTERIZES THE PORT]   an input NO call site exercises and no note covers. The legacy
//                                 behaviour is UNOBSERVED, so the row records what the as-built
//                                 port does and says so, rather than pretending otherwise.
//      [REPORTED DISCREPANCY]     the evidence and the port DISAGREE. The row pins what the port
//                                 does today so the disagreement is visible and any future change
//                                 to it is deliberate. See the report below.
//
//  THE COUNTS QUOTED BELOW, AND HOW THEY WERE TAKEN
//  --------------------------------------------------------------------------------------------
//  Every number in this file was measured against the checkout while the suite was written, not
//  copied from a summary, so that a reader can reproduce it. The census scanned every file under
//  ws_objects/ for the token `Sprintf(` and then classified the placeholders on each matching line:
//
//      78  occurrences of `Sprintf(` in total
//      21  of those are the forward-prototype DECLARATIONS in sprintf.srf:L7-L27
//      57  live call sites
//      22  call sites contain a bare {}                        (40 occurrences)
//      34  call sites contain an explicit {N}                  (100 occurrences, indices 1 to 8)
//      48  occurrences of {1} specifically - the most common placeholder in the estate
//       0  call sites use {0}
//       0  call sites use a printf conversion (%s %d %i %f %u %x)
//
//  Two of those figures are larger than the ones quoted in the brief this file was written from,
//  which counted 11 bare-{} sites and 30 indexed sites. The measured figures are used here. The
//  discrepancy does not change a single conclusion - it makes each of them stronger - but the
//  numbers are stated as measured rather than as received, because a count that cannot be
//  reproduced is not evidence.
//
//  WHY THE IMPLEMENTATION CANNOT BE string.Format, AND WHY THIS SUITE IS WHAT PROVES IT
//  --------------------------------------------------------------------------------------------
//  This is the technology-specific decision the suite exists to pin, recorded here because C-K
//  requires every such decision to be documented where it is made.
//
//  The braces LOOK like .NET composite formatting, and that resemblance is the trap. Reaching for
//  string.Format - or for CompositeFormat, or for an interpolated-string handler - breaks the
//  legacy dialect in three independent ways, every one of which is exercised by live legacy call
//  sites rather than by a hypothetical:
//
//      1  string.Format's index is ZERO-BASED; this dialect's index is ONE-BASED, which
//         w_test_logger.srw:L101 states outright. 34 of the 57 live call sites use an explicit
//         index - 100 occurrences in all, spanning indices 1 to 8 - and NOT ONE uses {0}, so a
//         hand-off to string.Format would be off by one at every one of them. Off-by-one output
//         still looks like a correctly formatted string, which makes this the quiet failure rather
//         than the loud one.
//      2  string.Format THROWS FormatException on a bare {}. Here {} is the ordinary
//         "next argument" form, used at 22 call sites in 40 occurrences - including the localization
//         XPath lookup that every translated string in the framework passes through.
//      3  string.Format THROWS when an index has no argument behind it. This dialect repeats a
//         single index freely - {1} FOUR times against ONE argument at
//         n_cst_dwsvc_contextmenu.sru:L1186 and again at L1188 - and renders an absent argument as
//         nothing rather than failing.
//
//  All three were MEASURED against string.Format while this suite was written rather than assumed,
//  and REGION 9 re-measures them on every run: it feeds the suite's own rows through a deliberate
//  string.Format wrapper and asserts the wrapper DISAGREES. A suite that would pass against a
//  string.Format shim would be pinning nothing, so that region is this file's self-check.
//
//  A fourth difference is subtler and is why REGION 6 exists: string.Format resolves {0} to the
//  first argument and this port does not. No call site uses {0} at all, so the legacy's own
//  treatment of it is UNOBSERVED, and REGION 6 is careful to assert only the part the evidence
//  actually establishes.
//
//  REPORTED DISCREPANCIES BETWEEN THE EVIDENCE AND THE AS-BUILT PORT
//  --------------------------------------------------------------------------------------------
//  Formatting.cs enumerates its inferred choices in a register it calls DECISION 13, and each
//  entry names the test that pins it. Writing those tests meant re-running the call-site census,
//  and the census turned up w_test_logger.srw - a file the register's stated premises do not
//  account for. Six findings follow. NONE of them is fixed here: this file's job is to
//  characterize the port and report, not to change behaviour it was not asked to change, and
//  bending the tests to assert un-implemented behaviour would simply redden the shared tree's
//  gate. Each finding is pinned by a named test so that a later characterization run against the
//  real pfw.dll can change the behaviour deliberately and see exactly which assertion moves.
//
//      D-1  BRACE ESCAPING USES THE WRONG MECHANISM.
//           w_test_logger.srw:L104 documents the backslash as the brace escape, and all five format
//           literals in that file use it - L107, L171, L198, L226 and L253 - so `\{1\}` renders a
//           literal `{1}` in the legacy. The port implements DOUBLED braces instead. Observable
//           effect: the port leaves the backslashes in the output where the legacy consumes them.
//           Pinned by: SprintfBraceEscapingIsAnInferredChoice
//
//      D-2  THE STATED PREMISE FOR CHOOSING DOUBLED BRACES IS FALSIFIED.
//           The register justifies doubled braces on the ground that no call site contains a brace
//           outside a placeholder, so nothing could regress. Braces outside placeholders DO occur,
//           in all five w_test_logger.srw format literals - and one of them, L107, is a genuine
//           Sprintf call site, so the premise fails even on the strictest reading of "call site".
//           The conclusion may still be defensible: no literal anywhere uses a DOUBLED brace, so
//           doubling breaks no existing caller. But the reasoning needs restating, and backslash
//           escaping is the mechanism the legacy documents.
//           Pinned by: SprintfBraceEscapingIsAnInferredChoice
//
//      D-3  A MASK APPLIED TO A STRING IS NOT UNOBSERVED.
//           The register calls this an unexercised case. w_test_logger.srw:L107 applies the
//           PowerBuilder string mask `(@@@)-@@@@` to the string "1234567", and L171 applies
//           `\{@@@\}-(@@@@@)` to another string. The port drops a mask on a non-IFormattable
//           value, so it renders "1234567" where the legacy renders "(123)-4567".
//           Pinned by: SprintfMaskOnNonFormattableValueIsAnInferredChoice
//
//      D-4  A MASK CONTAINING AN ESCAPED CLOSING BRACE IS TRUNCATED.
//           The port's mask scanner runs to the first '}' unconditionally, so
//           `{2,20:\{@@@\}-(@@@@@)}` at w_test_logger.srw:L171 ends at the escaped brace: the mask
//           becomes `\{@@@\` and the remaining `-(@@@@@)}` leaks into the output as literal text.
//           This is D-1's consequence one level down and would be fixed by the same change.
//           Pinned by: SprintfMalformedPlaceholderIsAnInferredChoice
//
//      D-5  THE MASK-TOKEN CENSUS IS INCOMPLETE, BUT THE TRANSLATION IS RIGHT ANYWAY.
//           The register says only "YYYY-MM-DD", "YYYY-MM-DD HH:MM:SS" and "#.0#" are corpus
//           verified and treats every other token as best effort. w_test_logger.srw:L171 also uses
//           `MMM-DDD-YY HH:MM:SS`, `H:MM:SS AM/PM`, `###,###,##0.0` and L107 uses `###,###,##0`.
//           Measured, the port translates all four to output that matches the PowerBuilder
//           dialect, so this is a documentation correction rather than a behaviour gap - and it
//           promotes those rows from best effort to verified.
//           Pinned by: SprintfGeneralMaskTokensAreAnInferredChoice
//
//      D-6  POSITIVE ALIGNMENT IS DOCUMENTED AND USED, NOT INFERRED.
//           The register's alignment census lists only negative widths plus `{,-4}`.
//           w_test_logger.srw:L107 and L171 use `{2,20}`, `{1,20}`, `{3,20}`, `{4,20}` and
//           `{5,20}`, and L102 documents the sign convention in words. Right-alignment is
//           therefore verified behaviour. Only the explicit '+' sign remains unobserved.
//           Pinned by: AlignmentPadsToTheDeclaredWidth (the positive-width rows)
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//  --------------------------------------------------------------------------------------------
//  It does not assert printf semantics. Despite the C-inherited name there is no % conversion
//  specifier in this dialect: no %s, %d, %i, %f, %u or %x appears at any call site in the estate,
//  and w_test_progressbar.srw:L208 puts a bare '%' immediately after a placeholder as ordinary
//  literal text. REGION 5 asserts that negative, because "the port is not a printf shim" is a
//  claim that needs evidence rather than an assumption.
//
//  It does not treat ~n, ~r or ~t as grammar. Those are POWERSCRIPT COMPILER escapes, already the
//  actual line-feed, carriage-return and tab characters by the time the legacy runtime enters
//  Sprintf. On the C# side the same call site simply contains "\n", "\r" or "\t". Implementing or
//  asserting tilde handling would invent a grammar rule that never existed.
//
//  It does not assert that anything throws. The as-built port never throws: a malformed
//  placeholder, an index with no argument, and a mask a value rejects all degrade to an inert
//  rendering. That is deliberate and load-bearing rather than lax - many legacy call sites use
//  Sprintf to BUILD an error message, so an exception raised inside it would replace the fault
//  being reported with a formatting fault. Asserting a "helpful" exception would enshrine a
//  behaviour change nobody asked for, which C-B forbids.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. That is a FINDING, not
//  latitude - no rule is invented, inferred or back-filled here, and the bar is the migration
//  plan's enterprise baseline and its non-rule constraint inventory instead.
//
//      C-K  (the PRIMARY constraint on this file) Every technology-specific decision is documented
//           where it is made: the substitution decision above, the six reported discrepancies, and
//           at every row the reason its expectation is what it is.
//      C-B  No behaviour improvements. Every expectation CHARACTERIZES the as-built port. Not one
//           asserts a more standard, more helpful or more correct behaviour, and the six
//           discrepancies are REPORTED rather than corrected here.
//      C-C  The legacy tree is read only and is the oracle. Every locator in this file is a
//           COMMENT, read once by a human while the suite was written. No test opens a path under
//           ws_objects/, reads pfw.i18n.xml or touches pfw.dll at run time - REGION 8 builds its
//           XML inline for exactly that reason. No legacy file was edited to write this suite.
//      C-H  Nullable reference types and warnings-as-errors apply to test projects exactly as they
//           apply to the library. The sibling signature is Sprintf(string?, params object?[]?), so
//           every argument table here is object?[]-shaped. This file contains no null-forgiving !,
//           no #pragma, no analyzer suppression and no unused member. Zero WARNINGS is the bar.
//      0.6.7  Prescribed test shape: table-driven parity matrices expressed as theories with
//           member data. Every region is such a matrix except a handful of single indivisible
//           observations, which are Facts because a one-row table is a table in name only.
//           Determinism is also a 0.6.7 requirement, and REGION 10 pins it: the port claims
//           culture invariance, and a claim nothing checks is a claim that will eventually be
//           false.
//
//  NAMING, WHICH IS A BUILD REQUIREMENT HERE RATHER THAN A PREFERENCE
//  --------------------------------------------------------------------------------------------
//  The repository-root .editorconfig scopes its CA1707 and IDE1006 suppressions BY FILE GLOB to
//  the ten implementation files that genuinely carry preserved legacy SCREAMING_SNAKE identifiers.
//  No test file is covered by any of them, and Directory.Build.props sets TreatWarningsAsErrors.
//  Every identifier declared here is therefore conventionally named - PascalCase types and
//  members, camelCase locals and parameters - and this file declares no SCREAMING_SNAKE identifier
//  of any kind.
//
//  ONE MORE BUILD TRAP, UNIQUE TO THIS FILE: not one format literal below is an INTERPOLATED
//  string. A $"..." literal would have its braces eaten by the C# compiler before Sprintf ever saw
//  them, so the test would silently assert something other than what it appears to. Every format
//  literal here is a plain string, and so is every expected value.
// ==============================================================================================

using System.Globalization;
using System.Xml.Linq;
using System.Xml.XPath;

using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Formatting.Sprintf(string, object[])"/>, the managed reconstruction
/// of the legacy <c>sprintf</c> global function declared at
/// <c>ws_objects/pfw.common.pbl.src/sprintf.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// The type under test resolves by simple name without a using directive: this file's namespace,
/// <c>PowerFramework.Shared.Kernel.Tests</c>, is nested inside <c>PowerFramework.Shared.Kernel</c>,
/// so simple-name lookup walks up into the enclosing namespace and finds <c>Formatting</c> there.
/// The redundant directive is deliberately omitted rather than added and then suppressed.
/// </para>
/// <para>
/// Every test here is synchronous and in process. Nothing awaits, so the ambient test cancellation
/// token has no call to be threaded through. Nothing touches the file system, a socket or a clock,
/// and the one test that reads the current culture restores it, so the suite is deterministic and
/// reproducible - the single hard prerequisite of the characterization model this refactor is
/// measured by.
/// </para>
/// </remarks>
public sealed class SprintfTests
{
    // ==========================================================================================
    // SHARED FIXTURE VALUES
    // ==========================================================================================
    //
    // One timestamp is reused across the mask regions, and its exact value is chosen rather than
    // convenient. MONTH 05 AND MINUTE 07 DIFFER, and second 09 differs from both. That is the whole
    // point: in a PowerBuilder display mask `MM` means MONTH in the date part and MINUTES in the
    // time part, disambiguated by position, whereas .NET reads `MM` as month everywhere. A
    // timestamp whose month and minute happened to coincide would pass while that bug was present.
    // The hour is 14 so that a 12-hour mask has something to convert (14 -> 2 PM), and 2024 is a
    // leap year so nothing about the date is a degenerate case.

    /// <summary>
    /// The timestamp every date and time mask row is rendered from: 15 May 2024, 14:07:09.
    /// </summary>
    /// <remarks>
    /// Month 05, minute 07 and second 09 are three DIFFERENT values on purpose, so that a mask
    /// translation which confuses month with minute cannot pass by coincidence.
    /// </remarks>
    private static readonly DateTime SampleTimestamp = new(2024, 5, 15, 14, 7, 9);

    /// <summary>
    /// A second, unrelated timestamp, used where a row needs two distinguishable ones.
    /// </summary>
    private static readonly DateTime OtherTimestamp = new(2024, 6, 1, 9, 30, 0);

    // ==========================================================================================
    // REGION 1 - THE SEQUENTIAL PLACEHOLDER {}, WHICH IS THE FORM string.Format CANNOT PARSE
    // ==========================================================================================
    //
    // A bare {} takes the NEXT argument from a cursor that starts at the first argument and
    // advances by one each time such a placeholder is resolved. This form is why a string.Format
    // hand-off is impossible rather than merely wrong: string.Format raises FormatException on {}
    // outright, so all 22 call sites using it would fail at run time instead of formatting.
    //
    // The first two rows are the realistic case named by this file's requirements, and they are
    // realistic in a way that matters: the format literal is an XPath expression, so the single
    // quotes and the @ [ ] ( ) / characters around the placeholders must all survive as literal
    // text. If any of them were treated as syntax the resulting expression would be malformed and
    // every localization lookup in the framework would silently return nothing. REGION 8 closes
    // that loop by evaluating the substituted expression for real.
    //
    // Every row in this table is VERIFIED against a live call site. None characterizes an
    // unobserved input.

    /// <summary>
    /// Rows for the index-omitted <c>{}</c> form: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> SequentialPlaceholderCases => new()
    {
        // [VERIFIED @ ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L51]
        // Cross-checked against the oracle character for character, single quotes included:
        //     sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",sCat,text))
        //              .GetValueString( )
        // sCat is one of the five category names the choose case at L38-L46 yields - here "dwsvc",
        // the category DataServices' validation path uses - and text is the Simplified Chinese
        // source string being translated.
        {
            "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",
            ["dwsvc", "修改数据被拒绝"],
            "string(pfw/dwsvc[@lang='en']/tr[@text='修改数据被拒绝']/@to)"
        },

        // [VERIFIED @ ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L52]
        // The Traditional Chinese twin, identical but for the language predicate. That the language
        // is baked into the FORMAT rather than passed as an argument is why the providers are three
        // separate literals instead of one parameterised one.
        {
            "string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)",
            ["msgbox", "确定"],
            "string(pfw/msgbox[@lang='cht']/tr[@text='确定']/@to)"
        },

        // [VERIFIED @ ws_objects/pfw.ui.objects.pbl.src/n_cst_base_theme.sru:L501]
        //     return Sprintf("rgb({},{},{})",r,g,b)
        // Deliberately boring: it is the shortest row that a string.Format implementation fails.
        { "rgb({},{},{})", [12, 34, 56], "rgb(12,34,56)" },

        // [VERIFIED @ ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L239]
        // and at n_cst_dwsvc_contextmenu.sru:L795, L863, L916, L1009, L1018, L1027, L1033, L1039,
        // L1045 and L1052 - eleven sites in all, every one of the shape
        //     Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)
        // These two rows are the SAME template either side of translation: the Simplified Chinese
        // source, and the English rendering that pfw.i18n.xml:L52 maps it to
        // (<tr text="第{}行" to="Line {}"/>). They are the proof that localization and Sprintf are
        // coupled - the TRANSLATED TEXT ITSELF carries a placeholder, so a lookup's output is fed
        // straight back in as a format string. REGION 8 exercises that chain end to end.
        { "第{}行", [7], "第7行" },
        { "Line {}", [7], "Line 7" },

        // [VERIFIED @ ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L643]
        // A diagnostic built from a non-ASCII format. It sits inside a commented-out block, which
        // makes it inert as BEHAVIOUR but perfectly good as GRAMMAR evidence: the framework's own
        // authors wrote it against the real function.
        {
            "参数类型不匹配: 期望 {} 实际 {}",
            ["long", "string"],
            "参数类型不匹配: 期望 long 实际 string"
        },

        // [VERIFIED @ ws_objects/pfwx.tests.pbl.src/wx_test_mqttclient.srw:L638]
        //     mle_1.text += Sprintf("Close [code: {}, info: {}]~r~n",code,info)
        // The trailing ~r~n is a PowerScript COMPILER escape for a carriage-return line-feed pair.
        // It is not grammar and gets no special treatment; on the C# side it is spelled "\r\n".
        // Note also the literal square brackets, which a placeholder scanner must ignore.
        {
            "Close [code: {}, info: {}]\r\n",
            [1006, "abnormal closure"],
            "Close [code: 1006, info: abnormal closure]\r\n"
        },

        // [VERIFIED @ ws_objects/pfwx.tests.pbl.src/wx_test_mqttclient.srw:L643]
        { "Error: {}, {}\r\n", [1006, "abnormal closure"], "Error: 1006, abnormal closure\r\n" },

        // [VERIFIED @ ws_objects/pfwx.tests.pbl.src/wx_test_httpclient.srw:L369]
        // Three sequential placeholders with a literal '/' immediately after the third, which is
        // the abutment case: text touching a placeholder must not be absorbed into it.
        {
            "total: {}, received: {}, speed: {}/s",
            ["1.2 MB", "640 KB", "128 KB"],
            "total: 1.2 MB, received: 640 KB, speed: 128 KB/s"
        },
    };

    /// <summary>
    /// Verifies that an index-omitted <c>{}</c> takes the next argument in sequence, and that every
    /// non-placeholder character - including the XPath punctuation the localization providers
    /// depend on - is copied through verbatim.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [MemberData(nameof(SequentialPlaceholderCases))]
    public void SequentialPlaceholderTakesTheNextArgument(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }

    // ==========================================================================================
    // REGION 2 - THE ONE-BASED INDEX, AND WHY A REPEATED INDEX IS THE ROW THAT MATTERS MOST
    // ==========================================================================================
    //
    // {1} is the FIRST argument. That is not an inference: w_test_logger.srw:L101 states in words
    // that the index starts at 1, and 34 of the 57 live call sites use an explicit index while not
    // one uses {0}. A string.Format hand-off would be off by one at every single one of them, and
    // off-by-one output still looks like a correctly formatted string - which is exactly why this
    // region exists and why REGION 9 re-checks it against the real string.Format on every run.
    //
    // Two properties beyond the numbering are pinned here because both are easy to implement
    // wrongly in ways that compile and look plausible:
    //
    //     RESOLUTION IS BY INDEX, NOT BY ENCOUNTER ORDER. The out-of-order row proves it.
    //     AN EXPLICIT INDEX IS A LOOKUP, NOT A CONSUMPTION. It may be repeated freely, and
    //     repeating it neither exhausts the arguments nor advances anything. The four-repetition
    //     rows are lifted verbatim from live DataWindow Find expressions: an implementation that
    //     consumed one argument per PLACEHOLDER instead of per INDEX would render the second and
    //     subsequent {1} as nothing and emit a syntactically broken expression, and the resulting
    //     Find would fail silently at run time rather than at build time.

    /// <summary>
    /// Rows for the explicit <c>{N}</c> form: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> OneBasedIndexCases => new()
    {
        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_camera_capture.srw:L341]
        //     ddlb_resolutions.AddItem(Sprintf("{1} x {2}",LoWord(nDims[nIndex]),HiWord(...)))
        // The minimal proof that {1} is the FIRST argument: a zero-based reading would render
        // "1080 x " and then fail for want of a third value.
        { "{1} x {2}", [1920, 1080], "1920 x 1080" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_camera_capture.srw:L65]
        { "{1} {2}", [3, "Integrated Camera"], "3 Integrated Camera" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_blink.srw:L430]
        { "width:{1} height:{2}", [1280, 720], "width:1280 height:720" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_websocket.srw:L266]
        // The literal ends with ~r~n in the oracle; see the note in REGION 1.
        {
            "Connection Closed! Code:{1} Info:{2}\r\n",
            [1000, "bye"],
            "Connection Closed! Code:1000 Info:bye\r\n"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L587]
        // Three indexed placeholders in ascending order, which is the ordinary shape.
        {
            "Connection Closed! Code:{1} Info:{2} {3}\r\n",
            [1000, "bye", "Reconnecting"],
            "Connection Closed! Code:1000 Info:bye Reconnecting\r\n"
        },

        // [VERIFIED @ ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L1188]
        // and identically at L1354. THE MOST IMPORTANT ROW IN THIS FILE after the XPath pair:
        //     sFind = Sprintf("({1} <> {1}[1]) or (if(IsNull({1}),1,0) <> if(IsNull({1}[1]),1,0))",
        //                     sColName)
        // ONE argument, {1} used FOUR times. Note also the abutting "[1]" array subscripts, which
        // are literal text that must not be mistaken for part of a placeholder.
        {
            "({1} <> {1}[1]) or (if(IsNull({1}),1,0) <> if(IsNull({1}[1]),1,0))",
            ["salary"],
            "(salary <> salary[1]) or (if(IsNull(salary),1,0) <> if(IsNull(salary[1]),1,0))"
        },

        // [VERIFIED @ ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L1186]
        // and identically at L1352. The same four-repetition shape, this time with single quotes
        // in the surrounding expression - more literal punctuation to preserve.
        {
            "if(IsNull({1}),'',{1}) <> if(IsNull({1}[1]),'',{1}[1])",
            ["name"],
            "if(IsNull(name),'',name) <> if(IsNull(name[1]),'',name[1])"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_sciter.srw:L78]
        // The highest index anywhere in the estate is 8, and this is the site that reaches it.
        {
            "str:{1} num:{2} fnum:{3} dnum:{4}\nstrary:{5}\nnumary:{6}\nfnumary:{7}\ndnumary:{8}",
            ["s", 1, 2.5, 3.25, "a,b", "1,2", "1.5,2.5", "3.5,4.5"],
            "str:s num:1 fnum:2.5 dnum:3.25\nstrary:a,b\nnumary:1,2\nfnumary:1.5,2.5\ndnumary:3.5,4.5"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L1041]
        //     FileWrite(hFile,Sprintf('<{1}>',#AppName))
        // A placeholder wrapped in angle brackets, written with PowerScript single-quote string
        // delimiters. The delimiter choice is a PowerScript detail with no bearing on the grammar.
        { "<{1}>", ["PowerFrameworkDemo"], "<PowerFrameworkDemo>" },

        // [VERIFIED @ ws_objects/pfw.pack.pbl.src/w_packager.srw:L1607]
        //     FileWrite(hFile,Sprintf('copy entry "{1}" {2} {3} "{4}"',...))
        // Four indexed placeholders, two of them inside embedded double quotes.
        {
            "copy entry \"{1}\" {2} {3} \"{4}\"",
            ["pfw.pbl", "retcode", "function", "pfw.min.pbl"],
            "copy entry \"pfw.pbl\" retcode function \"pfw.min.pbl\""
        },

        // [VERIFIED @ ws_objects/pfw.pack.pbl.src/w_packager.srw:L1614]
        // Two adjacent-ish placeholders separated only by a semicolon.
        {
            "set liblist \"{1};{2}\"",
            ["pfw.pbd", "pfw.min.pbl"],
            "set liblist \"pfw.pbd;pfw.min.pbl\""
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_static_map.srw:L60]
        // A URL query string: the '&' and '=' separators are literal text, and '?' is too.
        {
            "http://api.map.baidu.com/staticimage?center={1}&zoom={2}&width={3}&height={4}",
            ["beijing", 11, 400, 300],
            "http://api.map.baidu.com/staticimage?center=beijing&zoom=11&width=400&height=300"
        },

        // [DERIVED from ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L101]
        // No call site spells this particular literal, but the property is not in doubt: L101 says
        // the number IS the parameter index, so resolution is positional and the order in which
        // placeholders appear in the format is irrelevant. This row is what distinguishes "reads
        // the index" from "counts the placeholders and happens to agree when they ascend" - and
        // every corpus site ascends, so without this row that bug would be invisible.
        { "{2} {1}", ["first", "second"], "second first" },

        // [DERIVED from ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L1183]
        // The oracle site builds its format by concatenation, so the exact literal is assembled at
        // run time and cannot be quoted here; the repetition property it relies on is the same one
        // the two four-repetition rows above verify. This reduced row keeps the essential shape -
        // two uses of one index with literal text between them.
        { "String({1}) <> Describe(\"Evaluate('{1}',2)\")", ["age"], "String(age) <> Describe(\"Evaluate('age',2)\")" },
    };

    /// <summary>
    /// Verifies that an explicit index is ONE-BASED, resolves positionally rather than by encounter
    /// order, and may be repeated without consuming anything.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [MemberData(nameof(OneBasedIndexCases))]
    public void ExplicitIndexIsOneBasedAndRepeatable(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins that the legacy ceiling of 21 values is a limitation of the
    /// hand-unrolled PowerScript declaration rather than a contract, so the managed surface accepts
    /// more without behavioural regression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sprintf.srf:L7-L27</c> declares the same function 21 times, once per argument count,
    /// because PowerScript cannot forward an arbitrary-length argument list. The port collapses
    /// them into one <c>params</c> method, which removes the ceiling as a side effect.
    /// </para>
    /// <para>
    /// This row CHARACTERIZES the port and cannot be otherwise: a call with 25 values could not
    /// have been written against the legacy at all, so there is no legacy behaviour for it to
    /// diverge from. It is asserted rather than left implicit so that anyone tempted to reintroduce
    /// a fixed arity ceiling for fidelity's sake sees that the removal was deliberate.
    /// </para>
    /// </remarks>
    [Fact]
    public void ArgumentCountBeyondTheLegacyCeilingIsAccepted()
    {
        object?[] twentyFiveValues = [.. Enumerable.Range(1, 25).Select(value => (object?)value)];

        Assert.Equal(25, twentyFiveValues.Length);

        // {25} is past the widest legacy prototype; {1} and the sequential cursor still behave.
        Assert.Equal("25|1|1", Formatting.Sprintf("{25}|{1}|{}", twentyFiveValues));
    }

    // ==========================================================================================
    // REGION 3 - ALIGNMENT
    // ==========================================================================================
    //
    // w_test_logger.srw:L102 documents the whole convention in one sentence: alignment is a padding
    // width, a value shorter than the width is padded with white space, a POSITIVE width
    // right-aligns and a NEGATIVE width left-aligns. Both signs are used at live call sites, so
    // both are verified rather than inferred - which corrects the incomplete census recorded as
    // D-6 in the header.
    //
    // Two further properties are pinned here:
    //
    //     PADDING IS APPLIED AFTER THE MASK, to the already-rendered text. It is the only order
    //     that can produce the fixed-width columns the corpus builds, and the filescanner rows
    //     below are the columns in question.
    //     THE INDEX AND THE ALIGNMENT ARE INDEPENDENTLY OPTIONAL. {,-4} - alignment present, index
    //     omitted - is a live form, and an index-omitted placeholder with an alignment still
    //     participates in the sequential cursor like any other.
    //
    // The expected values are written as adjacent literals, one per column, with the arithmetic in
    // a trailing comment. That is deliberate: a single 100-character literal full of runs of spaces
    // cannot be reviewed, whereas "value" + "               " with "5 + 15 = 20" beside it can. The
    // C# compiler folds adjacent string literals into one constant, so these are still literals.

    /// <summary>
    /// Rows for the alignment field: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> AlignmentCases => new()
    {
        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_filescanner.srw:L165]
        //     mle_1.text = Sprintf("{1,-20}~t{2,-4}~t{3,-10}~t{4,-20}~t{5,-20}~r~n",
        //                          "名称","类型","大小","创建日期","最后修改日期")
        // Five left-aligned columns of CJK header text, and the row that makes the
        // characters-versus-display-columns question observable: every one of these headers is
        // double-width when rendered, so a display-column implementation would pad differently.
        // The port counts CHARACTERS, as .NET does. See
        // SprintfAlignmentIsMeasuredInCharactersIsAnInferredChoice for that decision on its own.
        {
            "{1,-20}\t{2,-4}\t{3,-10}\t{4,-20}\t{5,-20}\r\n",
            ["名称", "类型", "大小", "创建日期", "最后修改日期"],
            "名称                  " + "\t"     // 2 chars + 18 spaces  = 20
                + "类型  " + "\t"               // 2 chars + 2 spaces   = 4
                + "大小        " + "\t"         // 2 chars + 8 spaces   = 10
                + "创建日期                " + "\t"   // 4 chars + 16 spaces = 20
                + "最后修改日期              "         // 6 chars + 14 spaces = 20
                + "\r\n"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_filescanner.srw:L167]
        //     mle_1.text += Sprintf("{1,-20}~t{2,-4}~t{3,-10}~t{4:YYYY-MM-DD HH:MM:SS}~t"
        //                           + "{5:YYYY-MM-DD HH:MM:SS}~r~n", name, iif(...), ..., ...)
        // The data row beneath that header, and note the asymmetry the oracle actually has:
        // columns 1 to 3 carry an alignment, columns 4 and 5 carry a MASK AND NO ALIGNMENT. So the
        // two timestamps are unpadded, and the "FILE" column is exactly its declared width and
        // therefore unpadded too - the boundary case where padding is zero.
        {
            "{1,-20}\t{2,-4}\t{3,-10}\t{4:YYYY-MM-DD HH:MM:SS}\t{5:YYYY-MM-DD HH:MM:SS}\r\n",
            ["readme.txt", "FILE", "12KB", SampleTimestamp, OtherTimestamp],
            "readme.txt          " + "\t"       // 10 chars + 10 spaces = 20
                + "FILE" + "\t"                 // 4 chars, width 4     - no padding at all
                + "12KB      " + "\t"           // 4 chars + 6 spaces   = 10
                + "2024-05-15 14:07:09" + "\t"  // masked, unaligned    - no padding
                + "2024-06-01 09:30:00"         // masked, unaligned    - no padding
                + "\r\n"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L315]
        //     mle_trace.text += Sprintf("{,-4} {}({})>{}: {}, Expr: {}",
        //                               mle_trace.LineCount() + 1, dwo...., ...)
        // The {,-4} form: an alignment with the index OMITTED. It proves the two fields are
        // independently optional, and it proves an index-omitted placeholder with an alignment
        // still draws from the sequential cursor - here taking the FIRST value, after which the
        // five bare {} take values two through six in order.
        {
            "{,-4} {}({})>{}: {}, Expr: {}",
            [1, "dw_1", 3, "salary", "8000.25", "sum(salary for page)"],
            "1   " + " "                        // 1 char + 3 spaces = 4, then the literal space
                + "dw_1(3)>salary: 8000.25, Expr: sum(salary for page)"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L171] and at L198, L226, L253 -
        // pfwLogger format literals in this same dialect, plus the Sprintf literal at L107; see the
        // header note on that file. A POSITIVE width, which right-aligns. This is the form the header
        // records as D-6: it is written in shipped demo code and documented in words at L102, so
        // right-alignment is verified behaviour rather than an inference.
        { "[{1,20}]", ["abc"], "[" + "                 abc" + "]" },   // 17 spaces + 3 chars = 20

        // [DERIVED from w_test_logger.srw:L102] A short left-aligned value, reduced to the smallest
        // form that shows the padding side unambiguously.
        { "[{1,-6}]", ["ab"], "[" + "ab    " + "]" },                 // 2 chars + 4 spaces = 6

        // [DERIVED from w_test_logger.srw:L102] L102 says what happens when the value is SHORTER
        // than the width and says nothing about longer, so no-truncation is the reading consistent
        // with the documented half: the value is emitted in full and the column simply runs wide.
        // Truncating would destroy data, which is the one outcome the note cannot have intended.
        { "[{1,-3}]", ["abcdef"], "[abcdef]" },
        { "[{1,3}]", ["abcdef"], "[abcdef]" },

        // [DERIVED from w_test_logger.srw:L102] A value EXACTLY as wide as the field: padding is
        // zero and the boundary is inclusive on both sides of the sign.
        { "[{1,-6}]", ["abcdef"], "[abcdef]" },
        { "[{1,6}]", ["abcdef"], "[abcdef]" },

        // [CHARACTERIZES THE PORT] An explicit '+' sign on the width. No call site anywhere uses
        // one and L102 speaks only of a positive value, so the legacy's handling is UNOBSERVED. The
        // port accepts it for symmetry with '-' and treats it as no sign at all.
        { "[{1,+6}]", ["ab"], "[" + "    ab" + "]" },                 // 4 spaces + 2 chars = 6

        // [CHARACTERIZES THE PORT] Index omitted AND a positive width. The corpus has the
        // index-omitted form only with a negative width ({,-4}), so this particular combination is
        // UNOBSERVED; it is asserted because the two fields being independent means it is reachable.
        { "[{,4}]", ["ab"], "[" + "  ab" + "]" },                     // 2 spaces + 2 chars = 4

        // [CHARACTERIZES THE PORT] Alignment applied to a value that renders as nothing. The
        // padding still happens, so the column keeps its width - which is what a fixed-width report
        // needs, and which follows from padding being applied after rendering.
        { "[{1,-4}]", [null], "[" + "    " + "]" },                   // 0 chars + 4 spaces = 4
    };

    /// <summary>
    /// Verifies that the alignment field pads to its declared width with spaces, right-aligning for
    /// a positive width and left-aligning for a negative one, and never truncating.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [MemberData(nameof(AlignmentCases))]
    public void AlignmentPadsToTheDeclaredWidth(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }


    // ==========================================================================================
    // REGION 4 - THE FORMAT MASK, WHICH IS POWERBUILDER'S AND NOT .NET'S
    // ==========================================================================================
    //
    // w_test_logger.srw:L103 says the mask is the value's display format and points the reader at
    // the PowerBuilder String() function's format documentation. That single line is what makes
    // this region necessary: the mask arriving in a legacy format string is a POWERBUILDER mask,
    // and forwarding it unchanged to .NET is wrong in a way that produces plausible output.
    //
    // THE TRAP, STATED PLAINLY. In a PowerBuilder display mask `MM` means MONTH in the date part
    // and MINUTES in the time part, disambiguated by POSITION - m or mm is a minute when it follows
    // an hour designator. .NET's custom date and time format strings read `MM` as month EVERYWHERE
    // and `mm` as minute everywhere, and they are case-SENSITIVE where PowerBuilder masks are not.
    // So "YYYY-MM-DD HH:MM:SS" passed straight to .NET puts the MONTH in the minutes slot and
    // yields a timestamp that is wrong but entirely believable. No compiler catches it, and neither
    // does a test whose month and minute happen to be equal - which is why SampleTimestamp has
    // month 05, minute 07 and second 09, three different values, and why
    // MonthAndMinuteAreDisambiguatedByPosition asserts the distinction on its own.
    //
    // A numeric mask needs no translation at all, and that is a fact about the two dialects rather
    // than a convenience: PowerBuilder's numeric alphabet of '#' for an optional digit and '0' for
    // a required one is the same alphabet .NET custom numeric formats use, and the grouping comma
    // means the same thing in both. w_test_sqlite.srw:L356 exercises both halves of the dispatch in
    // one statement - a number through {5:#.0#} and a date through {6:YYYY-MM-DD}.

    /// <summary>
    /// Rows for the mask field: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> FormatMaskCases => new()
    {
        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_filescanner.srw:L167]
        // The full date-and-time mask, and the row the trap is measured on: month 05 must land in
        // the month slot and minute 07 in the minute slot.
        {
            "{1:YYYY-MM-DD HH:MM:SS}",
            [SampleTimestamp],
            "2024-05-15 14:07:09"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L356]
        // The date-only mask. Both of these two masks are named in Formatting.cs as the only
        // date-and-time masks the corpus verifies, and both are asserted here.
        { "{1:YYYY-MM-DD}", [SampleTimestamp], "2024-05-15" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L356]
        //     sRecordStr += Sprintf("~n{1}~t{2}~t{3}~t{4}~t{5:#.0#}~t{6:YYYY-MM-DD}",
        //                           rs.GetColumnNumber("id"), rs.GetColumnString("name"),
        //                           rs.GetColumnNumber("age"), ..., ..., ...)
        // The whole statement, and it is worth having as one row rather than six: the six columns
        // are the six columns of dw_sqlite.srd, the estate's only updatable DataWindow and the
        // golden-master fixture for the retrieval/validation/update triple - id, name, age,
        // address, salary and birth date. One statement exercises the maskless path, the numeric
        // mask path and the date mask path together, which is exactly how the dispatch can be seen
        // to be type-directed rather than mask-directed.
        {
            "\n{1}\t{2}\t{3}\t{4}\t{5:#.0#}\t{6:YYYY-MM-DD}",
            [1, "张三", 30, "深圳", 8000.25m, new DateTime(1994, 3, 2)],
            "\n1\t张三\t30\t深圳\t8000.25\t1994-03-02"
        },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L356] The numeric mask on its
        // own, over three values that each exercise a different part of the "#.0#" alphabet:
        //     '#' is an OPTIONAL digit and '0' is a REQUIRED one, in both dialects.
        { "{1:#.0#}", [8000.25m], "8000.25" },   // both optional decimals present
        { "{1:#.0#}", [5000m], "5000.0" },       // the required decimal is supplied as a zero
        { "{1:#.0#}", [42], "42.0" },            // an integral value still gets the required digit

        // [CHARACTERIZES THE PORT] A value below one, where the LEADING ZERO IS ELIDED because the
        // integral part of the mask is a single OPTIONAL '#'. So 0.5 renders as ".5", not "0.5".
        // That follows from the shared '#'-means-optional convention and would be identical in
        // PowerBuilder, but no call site passes a sub-unit value through this mask, so the row is
        // labelled as characterizing rather than verified. It is here because ".5" looks like a
        // defect at a glance and someone will otherwise "fix" it.
        { "{1:#.0#}", [0.5m], ".5" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L107]
        //     {2,20:###,###,##0}   over the value 123457
        // A grouped numeric mask. The grouping comma sits INSIDE the mask, which incidentally
        // proves the mask field runs to the closing brace and is not comma-delimited - a
        // comma-splitting parser would read this as an alignment.
        { "{1:###,###,##0}", [123457], "123,457" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L171] {3,20:###,###,##0.0}
        { "{1:###,###,##0.0}", [1234567.8], "1,234,567.8" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L171] {4,20:MMM-DDD-YY HH:MM:SS}
        // Recorded in the header as D-5: Formatting.cs treats every token beyond three as best
        // effort, but this mask is a live call site. Measured, the port's translation agrees with
        // the PowerBuilder dialect token for token - MMM is the abbreviated MONTH name, DDD the
        // abbreviated DAY-OF-WEEK name, YY the two-digit year - and the MM after HH is still
        // minutes. 15 May 2024 was a Wednesday.
        { "{1:MMM-DDD-YY HH:MM:SS}", [SampleTimestamp], "May-Wed-24 14:07:09" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_logger.srw:L171] {5,20:H:MM:SS AM/PM}
        // The other half of D-5, and the only place in the estate where a MERIDIEM designator
        // appears. Its presence anywhere in the mask selects a 12-hour clock for the hour token,
        // which is PowerBuilder's rule, so 14:07:09 becomes 2:07:09 PM. A single H means no leading
        // zero. If the clock selection were driven by the hour token instead of by the whole mask,
        // this row would render "14:07:09 PM".
        { "{1:H:MM:SS AM/PM}", [SampleTimestamp], "2:07:09 PM" },

        // [CHARACTERIZES THE PORT] Mask matching is CASE-INSENSITIVE. PowerBuilder masks are
        // case-insensitive and .NET's are not, so the port folds case before translating; these two
        // rows assert that an all-lowercase and a deliberately ragged mask both mean what the
        // canonical upper-case spelling means. No call site varies the case, so the rows characterize
        // rather than verify - but a case-sensitive port would be a latent trap for the first author
        // who typed a mask in lower case.
        { "{1:yyyy-mm-dd hh:mm:ss}", [SampleTimestamp], "2024-05-15 14:07:09" },
        { "{1:YyYy-Mm-Dd}", [SampleTimestamp], "2024-05-15" },

        // [CHARACTERIZES THE PORT] An EMPTY mask - the {1:} form - is treated as no mask at all
        // rather than as an empty format specifier. No call site writes it.
        { "{1:}", ["value"], "value" },

        // [CHARACTERIZES THE PORT] The mask dispatch keys on the VALUE's type, not on the mask's
        // shape, and it recognises DateTimeOffset alongside DateTime. The legacy type mapping
        // produces DateTime for a PowerScript datetime, so a DateTimeOffset is not something a
        // legacy call site could have passed.
        {
            "{1:YYYY-MM-DD HH:MM:SS}",
            [new DateTimeOffset(2024, 5, 15, 14, 7, 9, TimeSpan.Zero)],
            "2024-05-15 14:07:09"
        },

        // [CHARACTERIZES THE PORT] A DateOnly with a date-only mask. DateOnly is the legacy type
        // mapping's target for a PowerScript date, so the pairing is meaningful even though the
        // corpus does not spell it.
        { "{1:YYYY-MM-DD}", [new DateOnly(2024, 5, 15)], "2024-05-15" },
    };

    /// <summary>
    /// Verifies that a mask is interpreted in the PowerBuilder dialect for a date or time value and
    /// passed through unchanged for a numeric one.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [MemberData(nameof(FormatMaskCases))]
    public void FormatMaskIsInterpretedInThePowerBuilderDialect(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }

    /// <summary>
    /// [VERIFIED @ <c>w_test_filescanner.srw:L167</c>] Pins the single most dangerous silent failure
    /// in the mask translation: <c>MM</c> means MONTH in the date part of a PowerBuilder mask and
    /// MINUTES in the time part, and the two are told apart by POSITION.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a Fact rather than a row in <see cref="FormatMaskCases"/> because the observation is
    /// a RELATIONSHIP between two slots rather than a single expected string, and stating it as such
    /// is what makes the test self-explaining to whoever it eventually fails for.
    /// </para>
    /// <para>
    /// The three assertions are ordered from the general to the specific. The first is the whole
    /// rendering. The second and third pull the month and the minute out of it INDEPENDENTLY and
    /// check each against the value it must come from - because that is the assertion a naive
    /// pass-through to .NET actually breaks, and an equality check against one long string reports
    /// it as an opaque diff instead of as the swap it is.
    /// </para>
    /// </remarks>
    [Fact]
    public void MonthAndMinuteAreDisambiguatedByPosition()
    {
        // Month 05, minute 07: two different numbers, so a swap cannot hide.
        Assert.Equal(5, SampleTimestamp.Month);
        Assert.Equal(7, SampleTimestamp.Minute);

        string rendered = Formatting.Sprintf("{1:YYYY-MM-DD HH:MM:SS}", SampleTimestamp);

        Assert.Equal("2024-05-15 14:07:09", rendered);

        // The MM before the day is the MONTH, taken from the date part.
        Assert.Equal("05", rendered.Substring(5, 2));

        // The MM after the hour is the MINUTE, taken from the time part. Forwarding the mask
        // unchanged to .NET would put "05" here too, and the result - "2024-05-15 14:05:09" - is a
        // perfectly plausible timestamp that nothing but this assertion would question.
        Assert.Equal("07", rendered.Substring(14, 2));

        // ...and the same mask must not be read as month everywhere, which is the failure stated as
        // an inequality so the test reports the confusion by name rather than as a string diff.
        Assert.NotEqual("2024-05-15 14:05:09", rendered);
    }


    // ==========================================================================================
    // REGION 5 - NEGATIVE: A printf CONVERSION SPECIFIER IS NOT A PLACEHOLDER
    // ==========================================================================================
    //
    // The function is named after C's printf and shares nothing with it but the name. The estate
    // was searched for every printf conversion - %s, %d, %i, %f, %u, %x - and there are ZERO
    // occurrences in any Sprintf format string. There is no conversion-specifier sub-language to
    // implement, and asserting the negative is what proves the port is not a printf shim.
    //
    // The negative is not purely a search result either. w_test_progressbar.srw:L208 and L346 both
    // call
    //     Sprintf("正在干点什么({1}%)", hpb.Position / (hpb.MaxPosition - hpb.MinPosition) * 100)
    // where a bare '%' sits IMMEDIATELY AFTER a placeholder as ordinary literal text. That is
    // positive evidence that '%' carries no meaning here: an implementation that treated '%' as the
    // start of a conversion would consume the ')' after it and mangle a progress caption the
    // framework actually displays.
    //
    // Two properties are asserted per row, and the second is the one that catches a half-migration:
    // the % sequences survive as literal text, AND the arguments are consumed only by brace
    // placeholders. A port that recognised %s would silently spend an argument on it.

    /// <summary>
    /// Rows for the printf negative: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> PrintfStyleConversionCases => new()
    {
        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_progressbar.srw:L208] and at L346.
        // The live proof that a bare '%' is literal: the caption renders as a percentage.
        { "正在干点什么({1}%)", [42], "正在干点什么(42%)" },

        // [CHARACTERIZES THE PORT] The classic printf pair. Both two-character sequences pass
        // through untouched and BOTH arguments go unconsumed, because the format contains no brace
        // placeholder at all. Zero call sites in the estate write these, so the legacy's own
        // handling is UNOBSERVED - but a port that recognised them would break the verified row
        // above, so the two are consistent.
        { "%s and %d", ["ignored", 7], "%s and %d" },

        // [CHARACTERIZES THE PORT] The wider conversion set, including the doubled %% that C uses
        // to escape a literal percent. Nothing here is special-cased: %% stays %%, it does not
        // collapse to a single %.
        { "%i %f %u %x %% done", [], "%i %f %u %x %% done" },

        // [CHARACTERIZES THE PORT] The mixed case, which is the interesting one. The brace
        // placeholder resolves and the printf conversion does not, in the SAME format string, so a
        // reader can see the two grammars are not both live.
        { "%d items, {} shown", [5], "%d items, 5 shown" },

        // [CHARACTERIZES THE PORT] POSIX positional printf syntax, which is the closest thing in the
        // printf family to this dialect's indexed placeholder and therefore the likeliest thing for
        // someone to "add support for". It is inert.
        { "%1$s and %2$s", ["a", "b"], "%1$s and %2$s" },

        // [CHARACTERIZES THE PORT] A percent sign abutting a brace placeholder on BOTH sides, which
        // is the shape that would break first if '%' ever started a token.
        { "%{1}%", ["x"], "%x%" },
    };

    /// <summary>
    /// Verifies that printf-style conversion specifiers are inert literal text, and that arguments
    /// are consumed only by brace placeholders.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The expected rendering.</param>
    /// <remarks>
    /// The second assertion is the load-bearing one for the mixed rows: it re-renders the format
    /// with the arguments REVERSED and requires the result to be unchanged whenever the format has
    /// no brace placeholder. If a <c>%</c> sequence were consuming an argument, reversing the
    /// arguments would change the output.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrintfStyleConversionCases))]
    public void PrintfStyleConversionsAreNotPlaceholders(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));

        // Arguments are consumed only by brace placeholders. Where the format has none, the
        // arguments cannot influence the result at all - so permuting them must change nothing.
        if (!format.Contains('{', StringComparison.Ordinal))
        {
            object?[] reversed = [.. arguments.Reverse()];

            Assert.Equal(expected, Formatting.Sprintf(format, reversed));
            Assert.Equal(expected, Formatting.Sprintf(format));
        }
    }

    // ==========================================================================================
    // REGION 6 - NEGATIVE: {0} IS NOT A LEGACY PLACEHOLDER
    // ==========================================================================================
    //
    // BE PRECISE ABOUT WHAT IS AND IS NOT KNOWN HERE, because this is the region where it would be
    // easiest to fabricate a legacy expectation and hardest to notice afterwards.
    //
    // WHAT IS ESTABLISHED. The index is one-based - w_test_logger.srw:L101 says so in words - so
    // zero is not a valid index in this dialect. And {0} appears NOWHERE in the estate: all 100
    // explicit-index occurrences across the 34 call sites that carry one use an index of 1 to 8.
    //
    // WHAT IS NOT ESTABLISHED. Because no call site writes {0}, the legacy's exact treatment of it
    // is UNOBSERVED. It might render nothing, or pass through verbatim, or return a null string, or
    // fault. Nothing in the repository adjudicates it, and no amount of reasoning about a closed
    // binary will.
    //
    // SO THIS REGION ASSERTS TWO DIFFERENT THINGS AND KEEPS THEM APART. The first is the property
    // the evidence genuinely establishes: {0} DOES NOT RESOLVE TO THE FIRST ARGUMENT - which is
    // precisely what string.Format would do with it, and therefore precisely the wrong behaviour to
    // inherit. That assertion is strong and permanent. The second is the port's specific choice -
    // it renders as the empty string, taking the same path as any other index with no argument
    // behind it - and it is labelled as characterizing so that a later characterization run against
    // the real pfw.dll can change it without anyone mistaking it for verified legacy behaviour.

    /// <summary>
    /// Rows for the zero-index negative: format, arguments, and the port's rendering.
    /// </summary>
    /// <remarks>
    /// Every row here CHARACTERIZES THE PORT. The expected values are what the as-built
    /// implementation produces for an input the legacy corpus never exercises.
    /// </remarks>
    public static TheoryData<string, object?[], string> ZeroIndexCases => new()
    {
        // {0} alone renders as nothing. In the port, a one-based 0 maps to the zero-based index -1,
        // which is out of range, and an out-of-range index renders as the empty string.
        { "{0}", ["first"], "" },

        // The discriminating row: {0} and {1} side by side against ONE argument. {1} resolves and
        // {0} does not, so the difference between the two is visible in a single result.
        // string.Format would render "[first][" and then throw for want of a second argument.
        { "[{0}][{1}]", ["first"], "[][first]" },

        // A zero index with an alignment still pads, because padding is applied to the rendered
        // text and the rendered text is empty. The column keeps its width.
        { "[{0,-4}]", ["first"], "[    ]" },

        // A zero index with a mask renders as nothing too: there is no value for the mask to apply
        // to, and the absent value short-circuits before the mask is consulted.
        { "[{0:YYYY-MM-DD}]", [SampleTimestamp], "[]" },
    };

    /// <summary>
    /// [CHARACTERIZES THE PORT] Records what the as-built port renders for a zero index, an input
    /// the legacy corpus never exercises.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The rendering the as-built port produces.</param>
    /// <remarks>
    /// The stronger, evidence-backed claim - that a zero index does not become the first argument -
    /// is asserted separately by <see cref="ZeroIndexIsNotALegacyPlaceholder"/>. The two are kept
    /// apart on purpose: this table may legitimately be rewritten by a characterization run against
    /// the real <c>pfw.dll</c>, whereas that claim may not.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ZeroIndexCases))]
    public void ZeroIndexRendersAsTheAsBuiltPortDefines(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }

    /// <summary>
    /// Verifies the property the evidence actually establishes: <c>{0}</c> is not a legacy
    /// placeholder and does not resolve to the first argument, which is exactly what a zero-based
    /// implementation such as <c>string.Format</c> would do with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Fact rather than a table row, and deliberately narrow: every format it uses has <c>{0}</c>
    /// as its ONLY placeholder, so nothing else in the format can supply the first argument and the
    /// assertion cannot be satisfied or defeated by an unrelated substitution. That mistake is easy
    /// to make - a format containing both <c>{0}</c> and <c>{1}</c> renders the first argument
    /// through the <c>{1}</c>, and a "does not contain" check over the whole result would then fail
    /// for a reason that has nothing to do with the zero index.
    /// </para>
    /// <para>
    /// This assertion is expected to survive any future correction to the port's treatment of an
    /// unobserved input. Whatever <c>{0}</c> comes to render as, it must not become argument one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ZeroIndexIsNotALegacyPlaceholder()
    {
        // {0} as the only placeholder, over an argument distinctive enough that its appearance
        // anywhere in the result could only have come from a zero-based resolution.
        Assert.DoesNotContain(
            "sentinel",
            Formatting.Sprintf("{0}", "sentinel"),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "sentinel",
            Formatting.Sprintf("<{0}>", "sentinel"),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "sentinel",
            Formatting.Sprintf("{0,-10}", "sentinel"),
            StringComparison.Ordinal);

        // ...and the contrast that gives the claim its meaning: the ONE-based {1} does resolve, so
        // the format is well formed and the argument is reachable. Without this line the three
        // assertions above would also pass against an implementation that resolved nothing at all.
        Assert.Equal("sentinel", Formatting.Sprintf("{1}", "sentinel"));
    }


    // ==========================================================================================
    // REGION 7 - LITERAL TEXT AND THE ORDINARY EDGES
    // ==========================================================================================
    //
    // Everything that is not a placeholder is copied through verbatim, and the corpus leans on that
    // harder than it looks: n_cst_dwsvc_contextmenu.sru:L1186 builds a DataWindow Find expression in
    // which "{1}[1]" means the resolved column name followed by a LITERAL "[1]" array subscript, so
    // text abutting a placeholder must survive untouched.
    //
    // The mismatch rows are the ones worth reading twice. A format with more placeholders than
    // arguments, and one with more arguments than placeholders, are both reachable from real code -
    // a caller passing a value that turns out to be optional, or a message template edited without
    // its call site - and neither throws. Every row in this region CHARACTERIZES THE PORT unless
    // marked otherwise, because the corpus never gets these wrong and so never demonstrates what
    // happens when they are.

    /// <summary>
    /// Rows for literal text and the ordinary edge inputs: format, arguments, expected rendering.
    /// </summary>
    public static TheoryData<string, object?[], string> LiteralAndEdgeCases => new()
    {
        // [CHARACTERIZES THE PORT] No placeholder at all: the format is returned unchanged and the
        // arguments are simply unused.
        { "plain text with no placeholder", ["unused"], "plain text with no placeholder" },
        { "plain text with no placeholder", [], "plain text with no placeholder" },

        // [VERIFIED @ ws_objects/pfw.tests.pbl.src/w_test_qrcode.srw:L145]
        //     st_info.Text = Sprintf("Version:{1} Ecc:",_qrcode.GetVer(true))
        // A real call site whose format ENDS in literal text after its only placeholder, including a
        // trailing colon with nothing behind it. Not a mistake to tidy - it is what the oracle says.
        { "Version:{1} Ecc:", [7], "Version:7 Ecc:" },

        // [CHARACTERIZES THE PORT] More placeholders than arguments. The first resolves; the rest
        // render as nothing. Nothing throws, which is the property that matters, because callers use
        // this function to build error text.
        { "{}-{}-{}", ["a"], "a--" },
        { "{1}-{2}-{3}", ["a"], "a--" },

        // [CHARACTERIZES THE PORT] More arguments than placeholders: the surplus is ignored.
        { "{}", ["a", "b", "c"], "a" },
        { "{2}", ["a", "b", "c"], "b" },

        // [CHARACTERIZES THE PORT] Adjacent placeholders with no separator between them. No corpus
        // literal writes two placeholders back to back, so this is unobserved - but it is the direct
        // consequence of the scanner resuming immediately after a closing brace, and it is the shape
        // a caller reaches for when concatenating.
        { "{}{}", ["dw_1", ".salary"], "dw_1.salary" },
        { "{1}{1}", ["ab"], "abab" },

        // [CHARACTERIZES THE PORT] A lone CLOSING brace with no opening brace. It is literal text,
        // which is a deliberate divergence from string.Format - that API raises FormatException on
        // an unmatched closing brace, and a legacy format string carrying incidental braces would
        // then fail rather than render.
        { "a } b", ["v"], "a } b" },
        { "}", [], "}" },

        // [CHARACTERIZES THE PORT] A CSS rule body, which is the everyday shape of a format string
        // full of braces that are not placeholders. It survives intact.
        { "div { color: red; }", ["v"], "div { color: red; }" },

        // [CHARACTERIZES THE PORT] Text abutting a placeholder on both sides, reduced from the
        // DataWindow Find expressions in REGION 2 to the smallest form that shows the property.
        { "prefix{1}suffix", ["mid"], "prefixmidsuffix" },
        { "{1}[1]", ["salary"], "salary[1]" },
    };

    /// <summary>
    /// Verifies that literal text is copied through verbatim and that an argument-count mismatch in
    /// either direction renders inertly rather than throwing.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The rendering the as-built port produces.</param>
    [Theory]
    [MemberData(nameof(LiteralAndEdgeCases))]
    public void LiteralTextAndEdgeInputsRenderAsTheAsBuiltPortDefines(
        string format,
        object?[] arguments,
        string expected)
    {
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));
    }

    // ==========================================================================================
    // REGION 8 - THE REALISTIC CASE, ROUND-TRIPPED THROUGH A REAL XPATH EVALUATOR
    // ==========================================================================================
    //
    // REGION 1 asserts that the localization format renders to the expected STRING. That is
    // necessary and it is not sufficient, because the expected string is only useful if it is a
    // WELL-FORMED XPATH EXPRESSION that selects the intended node. A stray or absorbed character
    // would produce a string that looks plausible in a diff and selects nothing at run time - and
    // the symptom would surface far from here, as a localization that silently returns untranslated
    // text, because the localization fallback is a SILENT PASSTHROUGH by design.
    //
    // So this region closes the loop: it feeds Sprintf's own output into an XPath evaluator and
    // checks that the translation comes back. The migration plan has the Localization library
    // reading pfw.i18n.xml through XDocument plus XPathSelectElements, and these tests use exactly
    // that pair, so a defect here would be caught at the same layer the real code uses.
    //
    // THE XML IS BUILT INLINE, NEVER READ FROM DISK. pfw.i18n.xml is part of the read-only legacy
    // tree; opening it at run time would couple this unit test to a file the refactor must not
    // depend on, and would make the suite fail in any working directory. The inline document is
    // shaped exactly like the real one - pfw/<category lang="..."></category>/tr[@text][@to] - and
    // the entries are copied from pfw.i18n.xml:L51-L53 by hand, as data rather than as a file
    // reference.

    /// <summary>
    /// A minimal XML document shaped exactly like <c>pfw.i18n.xml</c>, built inline so that no test
    /// opens the read-only legacy resource at run time.
    /// </summary>
    /// <remarks>
    /// The two English entries are transcribed from <c>pfw.i18n.xml:L51-L53</c>. The second one,
    /// <c>第{}行</c> mapping to <c>Line {}</c>, is the entry whose translated value CARRIES A
    /// PLACEHOLDER - the reason the localization chain and Sprintf are coupled at all.
    /// </remarks>
    private const string LocalizationDocument =
        "<pfw>"
            + "<dwsvc lang='en'>"
                + "<tr text='修改数据被拒绝' to='Modified data is rejected'/>"
                + "<tr text='第{}行' to='Line {}'/>"
            + "</dwsvc>"
            + "<dwsvc lang='cht'>"
                + "<tr text='修改数据被拒绝' to='修改資料被拒絕'/>"
            + "</dwsvc>"
        + "</pfw>";

    /// <summary>
    /// Verifies that the expression <see cref="Formatting.Sprintf(string, object[])"/> builds for the
    /// English localization provider is a well-formed XPath expression that selects the intended
    /// translation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format is the one at <c>n_cst_i18n_en.sru:L51</c> verbatim. The legacy evaluates it
    /// through the deferred <c>n_xmldoc</c> parser; the migration plan substitutes
    /// <see cref="XDocument"/> with XPath, so that substitution is what is exercised here.
    /// </para>
    /// <para>
    /// The legacy expression is wrapped in the XPath <c>string()</c> function, which returns a STRING
    /// rather than a node set - so it is evaluated with <c>XPathEvaluate</c>. The node-set form is
    /// checked separately below with <c>XPathSelectElements</c>, because that is the API the plan
    /// names for the real provider and its parse rules are the stricter of the two.
    /// </para>
    /// </remarks>
    [Fact]
    public void LocalizationExpressionEvaluatesAsWellFormedXPath()
    {
        XDocument document = XDocument.Parse(LocalizationDocument);

        string expression = Formatting.Sprintf(
            "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",
            "dwsvc",
            "修改数据被拒绝");

        // The rendered expression, asserted here too so that a failure downstream can be told apart
        // from a failure in the substitution itself.
        Assert.Equal(
            "string(pfw/dwsvc[@lang='en']/tr[@text='修改数据被拒绝']/@to)",
            expression);

        // ...and the payoff: the expression PARSES and SELECTS. If Sprintf had absorbed a bracket, a
        // quote or a slash, this line would throw or return the empty string.
        object evaluated = document.XPathEvaluate(expression);

        Assert.Equal("Modified data is rejected", Assert.IsType<string>(evaluated));

        // The node-set form of the same lookup, through the API the plan names for the real provider.
        string nodeSetExpression = Formatting.Sprintf(
            "pfw/{}[@lang='en']/tr[@text='{}']",
            "dwsvc",
            "修改数据被拒绝");

        XElement selected = Assert.Single(document.XPathSelectElements(nodeSetExpression));

        Assert.Equal("Modified data is rejected", selected.Attribute("to")?.Value);
    }

    /// <summary>
    /// Verifies the same round trip for the Traditional Chinese provider at
    /// <c>n_cst_i18n_cht.sru:L52</c>, whose format differs only in the language predicate.
    /// </summary>
    /// <remarks>
    /// Kept as a separate test rather than folded into a theory because its value is in showing that
    /// the LANGUAGE IS PART OF THE FORMAT rather than an argument - which is why the framework has
    /// three provider objects with three literals instead of one parameterised lookup, and why a
    /// change to the language cannot be made by passing a different value.
    /// </remarks>
    [Fact]
    public void TraditionalChineseLocalizationExpressionSelectsItsOwnLanguage()
    {
        XDocument document = XDocument.Parse(LocalizationDocument);

        string expression = Formatting.Sprintf(
            "string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)",
            "dwsvc",
            "修改数据被拒绝");

        Assert.Equal(
            "string(pfw/dwsvc[@lang='cht']/tr[@text='修改数据被拒绝']/@to)",
            expression);

        // The Traditional Chinese entry, NOT the English one that shares the same source text.
        Assert.Equal("修改資料被拒絕", Assert.IsType<string>(document.XPathEvaluate(expression)));
    }

    /// <summary>
    /// Verifies the complete two-step chain that makes this function part of the localization path:
    /// a lookup whose TRANSLATED VALUE carries a placeholder, fed straight back into
    /// <see cref="Formatting.Sprintf(string, object[])"/> as a format string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape of eleven live call sites -
    /// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)</c> at
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c> and at <c>n_cst_dwsvc_contextmenu.sru:L795, L863, L916,
    /// L1009, L1018, L1027, L1033, L1039, L1045</c> and <c>L1052</c> - and it is the reason the
    /// localization library appears in the shared layer beneath DataServices rather than beside it.
    /// </para>
    /// <para>
    /// The chain uses Sprintf TWICE with two different roles, and getting either wrong breaks the
    /// other. First to build the XPath that finds the translation, then with the translation itself
    /// as the format. A port that mangled placeholders would fail at step one by selecting nothing,
    /// or at step two by leaving <c>Line {}</c> unsubstituted in a user-visible message.
    /// </para>
    /// </remarks>
    [Fact]
    public void TranslatedTemplateIsFedBackIntoSprintfAsAFormatString()
    {
        XDocument document = XDocument.Parse(LocalizationDocument);

        // STEP 1 - the lookup. The source text being translated is itself a format string, so the
        // placeholder has to survive being embedded in an XPath predicate untouched.
        const string sourceTemplate = "第{}行";

        string lookup = Formatting.Sprintf(
            "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",
            "dwsvc",
            sourceTemplate);

        Assert.Equal(
            "string(pfw/dwsvc[@lang='en']/tr[@text='第{}行']/@to)",
            lookup);

        string translatedTemplate = Assert.IsType<string>(document.XPathEvaluate(lookup));

        Assert.Equal("Line {}", translatedTemplate);

        // STEP 2 - the translated template is now the FORMAT. This is the value a legacy call site
        // hands straight to Sprintf.
        Assert.Equal("Line 42", Formatting.Sprintf(translatedTemplate, 42));

        // ...and the untranslated source renders too, which is what the localization layer's SILENT
        // PASSTHROUGH fallback relies on: with no provider installed the source text comes back
        // unchanged and must still format correctly.
        Assert.Equal("第42行", Formatting.Sprintf(sourceTemplate, 42));
    }

    // ==========================================================================================
    // REGION 9 - THE ADVERSARIAL SELF-CHECK
    // ==========================================================================================
    //
    // This region exists to answer one question about this file: WOULD THE SUITE ABOVE ACTUALLY FAIL
    // against a thin wrapper over string.Format? If any of its rows would pass either way, then that
    // row is not pinning the substitution decision and the whole exercise is decorative.
    //
    // So the check is performed rather than asserted in prose. Each row below is a format taken from
    // the regions above, and each is run through BOTH the port and a deliberate string.Format
    // wrapper, with the test requiring that the two DISAGREE. Three independent kinds of
    // disagreement are represented, because a single mechanism could be fixed by accident:
    //
    //     the bare {} rows        - string.Format raises FormatException
    //     the one-based rows      - string.Format is zero-based, so it either shifts by one or runs
    //                               out of arguments and raises
    //     the repeated-index row  - string.Format raises for want of the arguments the repetition
    //                               appears to demand
    //
    // A NOTE ON WHY THE FORMAT ARRIVES AS A PARAMETER. The wrapper takes its format from the theory
    // row rather than from a literal, and that is a build requirement as much as a design one: with a
    // constant format string the CA2241 analyzer evaluates the call at compile time and reports the
    // deliberate mismatch, which under TreatWarningsAsErrors would be a build error. A non-constant
    // format also models the real hazard more honestly - the mistake this suite guards against is
    // forwarding a legacy format string that arrived from elsewhere.

    /// <summary>
    /// Rows for the adversarial check: a format and arguments taken from the regions above, together
    /// with the rendering the port must produce.
    /// </summary>
    public static TheoryData<string, object?[], string> StringFormatDisagreementCases => new()
    {
        // Bare {} - string.Format raises FormatException on the placeholder itself.
        { "rgb({},{},{})", [12, 34, 56], "rgb(12,34,56)" },
        {
            "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",
            ["dwsvc", "修改数据被拒绝"],
            "string(pfw/dwsvc[@lang='en']/tr[@text='修改数据被拒绝']/@to)"
        },
        { "Line {}", [7], "Line 7" },

        // One-based indices - string.Format reads {1} as the SECOND argument, so it shifts by one
        // and then runs out.
        { "{1} x {2}", [1920, 1080], "1920 x 1080" },
        { "{1} {2}", [3, "Integrated Camera"], "3 Integrated Camera" },

        // A repeated index against a single argument - string.Format raises for want of arguments it
        // believes the format demands.
        {
            "({1} <> {1}[1]) or (if(IsNull({1}),1,0) <> if(IsNull({1}[1]),1,0))",
            ["salary"],
            "(salary <> salary[1]) or (if(IsNull(salary),1,0) <> if(IsNull(salary[1]),1,0))"
        },

        // Out-of-order indices, where a zero-based reading silently produces a DIFFERENT string
        // rather than raising - the quiet failure mode, and the one a smoke test would miss.
        { "{2} {1}", ["first", "second"], "second first" },
    };

    /// <summary>
    /// Verifies that the expectations in this suite genuinely discriminate: every row disagrees with
    /// what <see cref="string.Format(IFormatProvider, string, object?[])"/> produces for the same
    /// input, whether by raising or by rendering something else.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The rendering the port must produce.</param>
    [Theory]
    [MemberData(nameof(StringFormatDisagreementCases))]
    public void SuiteExpectationsDisagreeWithAStringFormatImplementation(
        string format,
        object?[] arguments,
        string expected)
    {
        // The port renders the legacy dialect correctly.
        Assert.Equal(expected, Formatting.Sprintf(format, arguments));

        // ...and string.Format does not. Either it raises, or it renders something else; both count
        // as disagreement, and the sentinel keeps the two cases in one assertion without hiding
        // which one occurred - a raise yields the sentinel, which can never equal a rendering.
        const string raised = "<string.Format raised>";

        string viaStringFormat = RenderWithStringFormat(format, arguments, raised);

        Assert.NotEqual(expected, viaStringFormat);
    }

    /// <summary>
    /// Renders <paramref name="format"/> through <see cref="string.Format(IFormatProvider, string,
    /// object?[])"/>, returning <paramref name="onFailure"/> when the call raises.
    /// </summary>
    /// <param name="format">The format string, deliberately NOT a constant - see the region note.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="onFailure">The sentinel to return when the call raises.</param>
    /// <returns>The .NET composite-formatting rendering, or the sentinel.</returns>
    /// <remarks>
    /// <para>
    /// This helper exists ONLY to be disagreed with. It is the wrong implementation, written on
    /// purpose, so that the claim "the port cannot be string.Format" is measured on every run rather
    /// than asserted in a comment that will eventually stop being true.
    /// </para>
    /// <para>
    /// The invariant culture is passed explicitly so that the comparison cannot come out differently
    /// on a host with a different ambient culture. A culture-dependent difference would be a
    /// disagreement for the wrong reason, and this test would then pass while proving nothing.
    /// </para>
    /// </remarks>
    private static string RenderWithStringFormat(string format, object?[] arguments, string onFailure)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, arguments);
        }
        catch (FormatException)
        {
            // The bare {} rows and the repeated-index row land here. That is the point.
            return onFailure;
        }
    }

    // ==========================================================================================
    // REGION 10 - DETERMINISM: THE RENDERING IS CULTURE-INVARIANT
    // ==========================================================================================
    //
    // Formatting.cs claims culture invariance and states plainly why it matters: the refactor's
    // parity evidence is paired legacy and target characterization recordings, and the Golden-Master
    // technique's one hard prerequisite is repeatability. A number, a date or a negative sign that
    // rendered differently on a host with a different ambient culture would make two recordings
    // diverge for a reason that has nothing to do with the port - and the divergence would look
    // exactly like a behavioural regression.
    //
    // A claim that nothing checks is a claim that will eventually be false, so this region checks it.
    // The values are chosen to be maximally sensitive: under de-DE the decimal separator is a comma
    // and the group separator a full stop, so a leaked culture turns "8000.25" into "8000,25" and
    // "1,234,567.8" into "1.234.567,8"; the date separator and month-name spelling differ too. Every
    // one of those differences would be caught by the rows below.

    /// <summary>
    /// Rows for the culture-invariance check: format, arguments, expected culture-independent
    /// rendering.
    /// </summary>
    /// <remarks>
    /// Every row here is a format from an earlier region whose output would CHANGE if the ambient
    /// culture leaked into the rendering.
    /// </remarks>
    public static TheoryData<string, object?[], string> CultureSensitiveCases => new()
    {
        // A decimal through a masked and an unmasked path: the decimal separator is the tell.
        { "{1:#.0#}", [8000.25m], "8000.25" },
        { "{1}", [8000.25m], "8000.25" },
        { "{1}", [1234.5], "1234.5" },

        // A grouped numeric mask, where BOTH separators differ under de-DE.
        { "{1:###,###,##0.0}", [1234567.8], "1,234,567.8" },

        // A negative value, because some cultures use a character other than the ASCII hyphen-minus
        // for the negative sign - and a stored recording could not match if that leaked.
        { "{1}", [-1234], "-1234" },
        { "{1:#.0#}", [-8000.25m], "-8000.25" },

        // Dates through a mask and without one. The masked path also proves the separator escaping
        // holds: an unescaped '/' or ':' in a .NET custom format is a CULTURE separator placeholder,
        // so a mask that reached .NET unescaped could still take a culture-dependent path even with
        // an invariant provider supplied.
        { "{1:YYYY-MM-DD HH:MM:SS}", [SampleTimestamp], "2024-05-15 14:07:09" },
        { "{1:YYYY/MM/DD}", [SampleTimestamp], "2024/05/15" },
        { "{1}", [SampleTimestamp], "05/15/2024 14:07:09" },

        // A month NAME and a day NAME, which are spelled differently in almost every culture. Under
        // de-DE these would be "Mai" and "Mi".
        { "{1:MMM-DDD-YY}", [SampleTimestamp], "May-Wed-24" },

        // A meridiem designator, which many cultures render differently or not at all.
        { "{1:H:MM:SS AM/PM}", [SampleTimestamp], "2:07:09 PM" },
    };

    /// <summary>
    /// Verifies that rendering does not consult the ambient culture, by producing each row under a
    /// deliberately hostile <see cref="CultureInfo.CurrentCulture"/> and requiring the invariant
    /// result.
    /// </summary>
    /// <param name="format">The PowerFramework-dialect format string.</param>
    /// <param name="arguments">The values to substitute.</param>
    /// <param name="expected">The culture-independent rendering.</param>
    /// <remarks>
    /// <para>
    /// German is chosen because it inverts BOTH numeric separators relative to the invariant culture,
    /// which is the most sensitive single choice available, and it also changes the date separator and
    /// every month and day name.
    /// </para>
    /// <para>
    /// <see cref="CultureInfo.CurrentCulture"/> is thread-local, and it is assigned only for the
    /// duration of this test body and restored in a <c>finally</c>, so a test running concurrently on
    /// another thread cannot observe it. <see cref="CultureInfo.DefaultThreadCurrentCulture"/> is
    /// deliberately NOT touched: that one is process-wide and would leak into every other test in the
    /// assembly.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CultureSensitiveCases))]
    public void RenderingIsCultureInvariant(string format, object?[] arguments, string expected)
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            // A guard on the fixture itself: if the hostile culture were not actually in effect the
            // test would pass vacuously, which is the failure mode of every test of this shape.
            Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

            Assert.Equal(expected, Formatting.Sprintf(format, arguments));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }


    // ==========================================================================================
    // REGION 11 - THE INFERRED-CHOICE REGISTER: THE TWELVE TESTS Formatting.cs NAMES BY NAME
    // ==========================================================================================
    //
    // Formatting.cs carries a block it calls DECISION 13 - "the complete register of inferred
    // choices" - listing twelve behaviours the 57-call-site corpus does not settle. Each entry ends
    // with a line of the form "Pinned by: <TestName>". Those twelve names are a CONTRACT between the
    // implementation and this file: if a named test does not exist, the implementation's own
    // documentation is false, and the next reader has no way to find the assertion that holds a
    // choice in place. All twelve are implemented below, under exactly the names the register uses,
    // in the register's own order.
    //
    // (A thirteenth entry, FormatRetCodeNullIsAnInferredChoice, belongs to Formatting.FormatRetCode
    // and therefore to FormatRetCodeTests.cs, not here.)
    //
    // Every test in this region CHARACTERIZES THE PORT by construction - that is what makes it a
    // register entry. Where the census performed while writing this suite showed a register premise
    // to be FALSIFIED, the test says so and pins current behaviour anyway; those are the reported
    // discrepancies D-1 through D-5 in the file header.

    /// <summary>
    /// [REPORTED DISCREPANCY D-1 and D-2] Pins register CHOICE 1: how a literal brace is escaped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The register states that brace escaping is undetermined "because NO call site in the
    /// repository contains a brace that is not part of a placeholder", and on that basis chose the
    /// doubled brace, mirroring .NET. THAT PREMISE IS FALSE. Five call sites contain braces outside
    /// placeholders - <c>w_test_logger.srw:L107, L171, L198, L226, L253</c> - and
    /// <c>w_test_logger.srw:L104</c> documents the mechanism in words: the BACKSLASH escapes
    /// <c>{</c> and <c>}</c>. The legacy renders <c>\{1\}</c> as <c>{1}</c>.
    /// </para>
    /// <para>
    /// The port does not implement backslash escaping, so it renders <c>\{1\}</c> with the
    /// backslashes intact. This test pins BOTH halves of the situation - what the port does with a
    /// backslash escape, and what it does with a doubled brace - so that implementing the documented
    /// mechanism later breaks exactly one named test, deliberately, rather than surprising someone.
    /// Nothing is corrected here: this suite characterizes and reports, and a test asserting
    /// un-implemented behaviour would simply redden the shared tree's gate without fixing anything.
    /// </para>
    /// <para>
    /// Worth noting for whoever picks this up: the conclusion may survive the correction to its
    /// premise, because no call site uses a DOUBLED brace, so supporting doubling regresses no
    /// caller. What is missing is support for the documented backslash form, and all five affected
    /// call sites are in <c>pfw.tests.pbl.src</c>, a characterization-fixture library that no
    /// in-scope service depends on.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfBraceEscapingIsAnInferredChoice()
    {
        // The port's chosen mechanism: a doubled brace collapses to one literal brace.
        Assert.Equal("{value}", Formatting.Sprintf("{{{1}}}", "value"));
        Assert.Equal("{}", Formatting.Sprintf("{{}}"));
        Assert.Equal("{", Formatting.Sprintf("{{"));

        // D-1: the DOCUMENTED mechanism is the backslash, and the port does not implement it. The
        // backslashes survive into the output, where w_test_logger.srw:L104 says the legacy consumes
        // them and emits a bare "{1}". This assertion states the port's behaviour, not the
        // legacy's - the inequality below is what records the divergence.
        Assert.Equal("\\{1\\}", Formatting.Sprintf("\\{1\\}", "value"));
        Assert.NotEqual("{1}", Formatting.Sprintf("\\{1\\}", "value"));

        // ...and the reason the backslash form does not accidentally substitute: "{1\}" is not a
        // well-formed placeholder, so the opening brace is emitted verbatim and scanning resumes
        // after it. The argument is therefore never consumed, which is why "value" is absent above.
        Assert.DoesNotContain("value", Formatting.Sprintf("\\{1\\}", "value"), StringComparison.Ordinal);
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 2: an index with no corresponding argument,
    /// including the explicit <c>{0}</c> that appears nowhere in the corpus.
    /// </summary>
    /// <remarks>
    /// The port renders the empty string, applies any alignment anyway, and does not throw. The
    /// no-throw half is the part that matters and it is not laziness: many legacy call sites build
    /// an error message with this function, so an exception raised here would replace the fault
    /// being reported with a formatting fault.
    /// </remarks>
    [Fact]
    public void SprintfOutOfRangeIndexIsAnInferredChoice()
    {
        // An index past the end of the arguments.
        Assert.Equal("", Formatting.Sprintf("{2}", "only"));
        Assert.Equal("[]", Formatting.Sprintf("[{9}]", "only"));

        // The zero index takes the same path, because one-based 0 maps to the zero-based index -1.
        Assert.Equal("", Formatting.Sprintf("{0}", "only"));

        // Alignment is still applied, so a fixed-width column keeps its width when a value is
        // missing. Padding is applied to the rendered text, and the rendered text is empty.
        Assert.Equal("[    ]", Formatting.Sprintf("[{9,-4}]", "only"));
        Assert.Equal("[    ]", Formatting.Sprintf("[{9,4}]", "only"));

        // No arguments at all is the degenerate case of the same rule.
        Assert.Equal("", Formatting.Sprintf("{1}"));
        Assert.Equal("", Formatting.Sprintf("{}"));

        // A number that is in range for the parser's guard but far past any real argument list still
        // renders inertly rather than overflowing or throwing.
        Assert.Equal("[]", Formatting.Sprintf("[{1000000}]", "only"));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 3: how a null argument renders.
    /// </summary>
    /// <remarks>
    /// The port renders the empty string. This is a genuine choice rather than an obvious one:
    /// PowerScript has null for value types and its string concatenation PROPAGATES null, so a
    /// legacy caller passing a null could plausibly have received a null string back for the whole
    /// result. Reproducing that would mean widening the return type to <c>string?</c> and pushing a
    /// null check onto every call site, which is a worse outcome than the thing being reproduced.
    /// </remarks>
    [Fact]
    public void SprintfNullArgumentIsAnInferredChoice()
    {
        Assert.Equal("[]", Formatting.Sprintf("[{1}]", new object?[] { null }));
        Assert.Equal("[]", Formatting.Sprintf("[{}]", new object?[] { null }));

        // A null among non-null values renders as nothing without disturbing its neighbours, and
        // without shifting the sequential cursor.
        Assert.Equal("a--c", Formatting.Sprintf("{}-{}-{}", "a", null, "c"));

        // A null with an alignment still pads, so a column keeps its width.
        Assert.Equal("[    ]", Formatting.Sprintf("[{1,-4}]", new object?[] { null }));

        // A null with a mask renders as nothing: the absent value short-circuits before the mask is
        // consulted, so the mask cannot fail on it.
        Assert.Equal("[]", Formatting.Sprintf("[{1:YYYY-MM-DD}]", new object?[] { null }));

        // The whole result is a non-null empty string, never a null string. This is the assertion
        // that pins the choice described above.
        Assert.NotNull(Formatting.Sprintf("{1}", new object?[] { null }));

        // A NON-null value whose ToString() RETURNS null, which is a different thing from a null
        // argument and reaches a different defensive path in the port. It is legal in C# - the
        // override is annotated as returning string? - so a caller can produce it without meaning to,
        // and PowerScript's null-propagating string concatenation makes it the closest analogue of
        // the legacy's own null behaviour. The port coalesces it to the empty string on BOTH the
        // maskless and the masked path, so neither can return a null string to a caller.
        NullRenderingValue rendersAsNull = new();

        Assert.Equal("[]", Formatting.Sprintf("[{1}]", rendersAsNull));
        Assert.Equal("[]", Formatting.Sprintf("[{1:YYYY}]", rendersAsNull));
        Assert.Equal("[    ]", Formatting.Sprintf("[{1,-4}]", rendersAsNull));
        Assert.NotNull(Formatting.Sprintf("{1}", rendersAsNull));
    }

    /// <summary>
    /// A value whose <see cref="object.ToString"/> returns <see langword="null"/>, used to reach the
    /// port's null-coalescing fallback on both the masked and the maskless rendering path.
    /// </summary>
    /// <remarks>
    /// This exists because the fallback is otherwise unreachable from a test and would sit
    /// permanently uncovered - and an uncovered defensive branch is one nobody can be sure still
    /// works. The override is legal rather than contrived: <see cref="object.ToString"/> is annotated
    /// as returning <c>string?</c>, so any type may do this, and a caller can produce one without
    /// intending to. It also happens to be the nearest managed analogue of PowerScript's
    /// null-propagating string concatenation.
    /// </remarks>
    private sealed class NullRenderingValue
    {
        /// <summary>Returns <see langword="null"/>, which is the entire purpose of this type.</summary>
        /// <returns>Always <see langword="null"/>.</returns>
        public override string? ToString() => null;
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 4: a null or empty format string, and a null
    /// arguments array.
    /// </summary>
    /// <remarks>
    /// Both yield the empty string rather than throwing. The null-array case is not hypothetical: in
    /// C# a <c>params</c> parameter accepts a null array directly, so <c>Sprintf("{1}", null)</c>
    /// binds the null literal to the ARRAY rather than passing one null value, and the method has to
    /// normalise it before use. Writing the cast explicitly here is what makes the test say which of
    /// the two readings it means.
    /// </remarks>
    [Fact]
    public void SprintfNullFormatIsAnInferredChoice()
    {
        Assert.Equal(string.Empty, Formatting.Sprintf(null, "unused"));
        Assert.Equal(string.Empty, Formatting.Sprintf(null));
        Assert.Equal(string.Empty, Formatting.Sprintf(string.Empty, "unused"));
        Assert.Equal(string.Empty, Formatting.Sprintf(""));

        // The null ARRAY, distinguished from a single null VALUE by the cast. Both render inertly,
        // but they travel different code paths and only one of them is about the format's arguments
        // being absent altogether.
        Assert.Equal("[]", Formatting.Sprintf("[{1}]", (object?[]?)null));
        Assert.Equal("[]", Formatting.Sprintf("[{1}]", new object?[] { null }));

        // A null format wins over everything else in the format position, including a well-formed
        // argument list.
        Assert.Equal(string.Empty, Formatting.Sprintf(null, "a", "b", "c"));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 5: mixing the index-omitted <c>{}</c> with the
    /// explicit <c>{N}</c> in one format string, and whether an explicit index advances the
    /// sequential cursor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No corpus literal mixes the two forms, so the combination is unobserved. The port honours
    /// both and treats an explicit index as a LOOKUP that does not advance the cursor; only an
    /// index-omitted placeholder advances it.
    /// </para>
    /// <para>
    /// That is the reading consistent with the VERIFIED half of the behaviour. An explicit index is
    /// demonstrably repeatable - <c>{1}</c> appears four times against one argument at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1186</c> and again at <c>L1188</c> - so if a lookup also
    /// consumed, those four repetitions would advance the cursor four times for no reason, and any
    /// bare <c>{}</c> later in such a format would land on the wrong value. The alternative reading
    /// has nothing to recommend it.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfMixedIndexAndSequentialIsAnInferredChoice()
    {
        // {1} looks up the first value WITHOUT advancing, so the following {} also takes the first.
        // {2} looks up the second without advancing, so the next {} takes the second.
        Assert.Equal("A A B B", Formatting.Sprintf("{1} {} {2} {}", "A", "B"));

        // The cursor is driven purely by the index-omitted placeholders, however many explicit
        // indices are interleaved: three {} take the first three values in order.
        Assert.Equal("C-A-B-C", Formatting.Sprintf("{3}-{}-{}-{}", "A", "B", "C"));

        // Repeating an explicit index consumes nothing at all, so a trailing {} still starts at the
        // first value. This is the assertion that would fail if a lookup advanced the cursor.
        Assert.Equal("AAAA|A", Formatting.Sprintf("{1}{1}{1}{1}|{}", "A", "B"));

        // ...and with the two forms the other way round: the leading {} advances past the first
        // value, so the following {} takes the second, while the explicit {1} in between changes
        // nothing.
        Assert.Equal("A A B", Formatting.Sprintf("{} {1} {}", "A", "B"));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT, and REPORTED DISCREPANCY D-4] Pins register CHOICE 6: a malformed
    /// placeholder is copied verbatim and scanning resumes after the brace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing throws, and a format string carrying incidental braces survives intact. "Malformed"
    /// covers a non-digit where an index belongs, a comma with no width, an unterminated brace,
    /// embedded whitespace, and a digit run past the parser's guard.
    /// </para>
    /// <para>
    /// The final assertion records D-4. Because the mask field runs to the first <c>}</c>
    /// unconditionally, the live mask <c>{2,20:\{@@@\}-(@@@@@)}</c> at
    /// <c>w_test_logger.srw:L171</c> ends at the ESCAPED closing brace: the mask becomes
    /// <c>\{@@@\</c> and the remaining <c>-(@@@@@)}</c> leaks into the output as literal text. That
    /// is a consequence of D-1 one level down and would be fixed by the same change, so it is pinned
    /// here rather than in a test of its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfMalformedPlaceholderIsAnInferredChoice()
    {
        // A non-digit where the index belongs.
        Assert.Equal("{a}", Formatting.Sprintf("{a}", "v"));
        Assert.Equal("{name}", Formatting.Sprintf("{name}", "v"));

        // A comma with no width behind it. Once the comma is present the width is mandatory. The
        // third row is the same rule at the very END of the string, where the scanner runs out of
        // input rather than finding the wrong character - a different code path to the same outcome.
        Assert.Equal("{1,}", Formatting.Sprintf("{1,}", "v"));
        Assert.Equal("{1,-}", Formatting.Sprintf("{1,-}", "v"));
        Assert.Equal("{1,", Formatting.Sprintf("{1,", "v"));

        // Embedded white space, which this grammar does not tolerate anywhere inside a placeholder.
        Assert.Equal("{ 1}", Formatting.Sprintf("{ 1}", "v"));
        Assert.Equal("{1 }", Formatting.Sprintf("{1 }", "v"));

        // An unterminated placeholder, with and without a mask.
        Assert.Equal("{1", Formatting.Sprintf("{1", "v"));
        Assert.Equal("{1:YYYY", Formatting.Sprintf("{1:YYYY", "v"));
        Assert.Equal("abc{", Formatting.Sprintf("abc{", "v"));

        // A digit run past the parser's guard is MALFORMED rather than merely out of range, so it
        // renders verbatim - whereas the largest in-guard value renders as an absent argument. The
        // two rows either side of that boundary are asserted together because the difference is
        // otherwise invisible.
        Assert.Equal("{1000001}", Formatting.Sprintf("{1000001}", "v"));
        Assert.Equal("[]", Formatting.Sprintf("[{1000000}]", "v"));

        // D-4: the live brace-escaped mask from w_test_logger.srw:L171, where the mask is truncated
        // at the escaped closing brace and the remainder becomes literal text. The value is a
        // string, so the truncated mask is then dropped as well - see
        // SprintfMaskOnNonFormattableValueIsAnInferredChoice.
        Assert.Equal(
            "            12345678-(@@@@@)}",
            Formatting.Sprintf("{2,20:\\{@@@\\}-(@@@@@)}", "abc", "12345678"));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 7: how a boolean argument renders.
    /// </summary>
    /// <remarks>
    /// <see cref="bool"/> does not implement <see cref="IFormattable"/>, so it takes the plain
    /// <c>ToString</c> path and renders as <c>True</c> and <c>False</c> - capitalised, which is
    /// .NET's spelling rather than PowerScript's. No corpus call site passes a boolean through a
    /// literal format, so the legacy's spelling is unobserved. The capitalisation is asserted
    /// explicitly because it is exactly the kind of detail a later author would "tidy" to lower case
    /// and thereby change output that a characterization recording may already contain.
    /// </remarks>
    [Fact]
    public void SprintfBooleanRenderingIsAnInferredChoice()
    {
        Assert.Equal("True", Formatting.Sprintf("{1}", true));
        Assert.Equal("False", Formatting.Sprintf("{1}", false));
        Assert.Equal("True/False", Formatting.Sprintf("{1}/{2}", true, false));

        // A mask cannot be honoured by a boolean, so it is dropped rather than applied - the same
        // rule as for any other non-formattable value.
        Assert.Equal("True", Formatting.Sprintf("{1:YYYY-MM-DD}", true));

        // Alignment still applies, because padding happens after rendering.
        Assert.Equal("[True  ]", Formatting.Sprintf("[{1,-6}]", true));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 8: a date or time argument with NO mask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy default would be locale dependent, which the refactor's determinism requirement
    /// forbids outright: a value that rendered differently on a host with a different ambient culture
    /// would make two characterization recordings diverge for a reason that has nothing to do with
    /// the port. The port therefore uses the INVARIANT general format rather than inventing a mask.
    /// </para>
    /// <para>
    /// The consequences are asserted rather than described, because two of them are surprising. The
    /// invariant general format for a date and time is month-first with a four-digit year, which is
    /// nobody's preferred spelling and is deterministic, which is the point. And a
    /// <see cref="TimeOnly"/> renders as SHORT time, so it LOSES THE SECONDS - the last assertion
    /// pins that, since a reader comparing against a legacy recording that shows seconds needs to
    /// know it is this choice and not a truncation bug.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfMasklessDateRenderingIsAnInferredChoice()
    {
        Assert.Equal("05/15/2024 14:07:09", Formatting.Sprintf("{1}", SampleTimestamp));
        Assert.Equal("05/15/2024", Formatting.Sprintf("{1}", new DateOnly(2024, 5, 15)));

        // A TimeOnly renders as SHORT time: the seconds are absent, by choice rather than by defect.
        Assert.Equal("14:07", Formatting.Sprintf("{1}", new TimeOnly(14, 7, 9)));

        // The rendering is culture-invariant, which is the property the whole choice exists to
        // protect. REGION 10 asserts it under a hostile ambient culture.
        Assert.Equal(
            SampleTimestamp.ToString(CultureInfo.InvariantCulture),
            Formatting.Sprintf("{1}", SampleTimestamp));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 9: whether an alignment width counts CHARACTERS
    /// or display columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two differ for exactly the text the corpus pads:
    /// <c>w_test_filescanner.srw:L165</c> aligns five CJK column headers to widths of 20, 4 and 10,
    /// and every character in them is double-width when rendered in a monospaced font. Counting
    /// characters pads a two-character header with 18 spaces; counting display columns would pad it
    /// with 16. The port counts CHARACTERS, as .NET does.
    /// </para>
    /// <para>
    /// This is asserted as a comparison between a CJK value and an ASCII value of the same character
    /// length, which is the only formulation that distinguishes the two rules: an equality against a
    /// single literal would pass under either if the literal were derived from the same rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfAlignmentIsMeasuredInCharactersIsAnInferredChoice()
    {
        // Two CJK characters and two ASCII characters are padded IDENTICALLY, because both are two
        // CHARACTERS long. Under a display-column rule the CJK value would receive two fewer spaces.
        string cjk = Formatting.Sprintf("[{1,-6}]", "中文");
        string ascii = Formatting.Sprintf("[{1,-6}]", "ab");

        Assert.Equal("[中文    ]", cjk);
        Assert.Equal("[ab    ]", ascii);
        Assert.Equal(ascii.Length, cjk.Length);

        // The same rule on the right-aligned side.
        Assert.Equal("[    中文]", Formatting.Sprintf("[{1,6}]", "中文"));

        // ...and the corpus header that makes the question matter: a two-character CJK header padded
        // to width 20 receives 18 spaces.
        Assert.Equal(
            "名称                  ",   // 2 characters + 18 spaces = 20
            Formatting.Sprintf("{1,-20}", "名称"));
    }

    /// <summary>
    /// [REPORTED DISCREPANCY D-3] Pins register CHOICE 10: a mask applied to a value that cannot
    /// honour one, such as a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The port drops the mask and renders the value unmasked, because <see cref="string"/> does not
    /// implement <see cref="IFormattable"/> and there is nothing to hand the mask to.
    /// </para>
    /// <para>
    /// THE REGISTER CALLS THIS UNEXERCISED AND IT IS NOT. <c>w_test_logger.srw:L107</c> applies the
    /// PowerBuilder string mask <c>(@@@)-@@@@</c> to the string <c>"1234567"</c>, and
    /// <c>L171, L198, L226</c> and <c>L253</c> apply <c>\{@@@\}-(@@@@@)</c> to another string.
    /// <c>w_test_logger.srw:L103</c> points at the <c>String()</c> function's masks, whose <c>@</c>
    /// is a character placeholder - so the legacy renders <c>(123)-4567</c> where the port renders
    /// <c>1234567</c>.
    /// </para>
    /// <para>
    /// Pinned, not fixed, for the reasons given in the file header: implementing the <c>@</c> string
    /// mask is a behaviour change this file is not the place to make, and asserting it before it
    /// exists would redden the gate without moving the work forward. All five affected call sites
    /// are in <c>pfw.tests.pbl.src</c>, which no in-scope service depends on.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfMaskOnNonFormattableValueIsAnInferredChoice()
    {
        // A date mask on a string: dropped, value rendered as-is.
        Assert.Equal("not-a-date", Formatting.Sprintf("{1:YYYY-MM-DD}", "not-a-date"));

        // A numeric mask on a string: also dropped, rather than applied by string surgery.
        Assert.Equal("12345", Formatting.Sprintf("{1:###,###}", "12345"));

        // D-3: the live PowerBuilder string mask from w_test_logger.srw:L107. The port renders the
        // digits unmasked; the legacy would render "(123)-4567". The inequality is what records the
        // divergence, so a future implementation of the @ mask breaks a named assertion on purpose.
        Assert.Equal("1234567", Formatting.Sprintf("{1:(@@@)-@@@@}", "1234567"));
        Assert.NotEqual("(123)-4567", Formatting.Sprintf("{1:(@@@)-@@@@}", "1234567"));

        // The alignment beside a dropped mask is still honoured, which is how the affected corpus
        // columns keep their width even while the mask is ignored.
        Assert.Equal(
            "             1234567",   // 13 spaces + 7 characters = 20
            Formatting.Sprintf("{1,20:(@@@)-@@@@}", "1234567"));
    }

    /// <summary>
    /// [CHARACTERIZES THE PORT] Pins register CHOICE 11: a mask the value REJECTS, such as a date
    /// mask naming an hour applied to a <see cref="DateOnly"/>.
    /// </summary>
    /// <remarks>
    /// The port falls back to the unmasked invariant rendering and never propagates the exception.
    /// That is deliberate and is the same reasoning as everywhere else in this file: callers build
    /// error text with this method, so a formatting fault must not displace the fault being
    /// reported. The assertions therefore check both halves - that a value comes back, and that
    /// nothing is thrown.
    /// </remarks>
    [Fact]
    public void SprintfRejectedMaskIsAnInferredChoice()
    {
        // A DateOnly cannot honour an hour, a minute or a second, so the whole mask is rejected and
        // the invariant unmasked rendering is used instead.
        Assert.Equal(
            "05/15/2024",
            Formatting.Sprintf("{1:YYYY-MM-DD HH:MM:SS}", new DateOnly(2024, 5, 15)));

        // A TimeOnly cannot honour a year, a month or a day, which is the mirror image.
        Assert.Equal("14:07", Formatting.Sprintf("{1:YYYY-MM-DD}", new TimeOnly(14, 7, 9)));

        // A TimeSpan is deliberately NOT treated as a date or time value by the mask dispatch - the
        // legacy type mapping never produces one, and its custom format alphabet differs - so the
        // mask reaches it untranslated, it rejects it, and the fallback renders it plainly.
        Assert.Equal("03:00:00", Formatting.Sprintf("{1:YYYY}", TimeSpan.FromHours(3)));

        // Nothing throws on any of those paths. Asserted separately because the fallback VALUE and
        // the absence of an exception are two different guarantees, and only one of them is visible
        // in an equality check.
        Assert.Null(
            Record.Exception(() => Formatting.Sprintf(
                "{1:YYYY-MM-DD HH:MM:SS}|{2:YYYY}|{3:ZZZZ}",
                new DateOnly(2024, 5, 15),
                TimeSpan.FromHours(3),
                42)));
    }

    /// <summary>
    /// [REPORTED DISCREPANCY D-5] Pins register CHOICE 12: every mask token beyond the three the
    /// register treats as corpus verified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The register names <c>YYYY-MM-DD</c>, <c>YYYY-MM-DD HH:MM:SS</c> and the numeric <c>#.0#</c>
    /// as the verified set and calls every other token a documented best effort.
    /// <c>w_test_logger.srw</c> shows the corpus is wider than that: <c>L171</c> uses
    /// <c>MMM-DDD-YY HH:MM:SS</c>, <c>H:MM:SS AM/PM</c> and <c>###,###,##0.0</c>, and <c>L107</c>
    /// uses <c>###,###,##0</c>. Measured, the port translates all four to output that matches the
    /// PowerBuilder dialect, so D-5 is a DOCUMENTATION correction rather than a behaviour gap - the
    /// register understates its own evidence.
    /// </para>
    /// <para>
    /// The genuinely unverified tokens are asserted too, and labelled as such inline, so the line
    /// between the two is drawn in the test rather than left to the reader.
    /// </para>
    /// </remarks>
    [Fact]
    public void SprintfGeneralMaskTokensAreAnInferredChoice()
    {
        // D-5, first half: month NAME and day-of-week NAME tokens, live at w_test_logger.srw:L171.
        // A three-character month run is a name in both dialects and has no minute reading at all,
        // which is why the MM after HH later in the same mask still means minutes.
        Assert.Equal("May-Wed-24 14:07:09", Formatting.Sprintf("{1:MMM-DDD-YY HH:MM:SS}", SampleTimestamp));

        // D-5, second half: the meridiem designator, also live at L171. Its presence ANYWHERE in the
        // mask selects a 12-hour clock, which is PowerBuilder's rule, so 14:07:09 becomes 2:07:09 PM.
        Assert.Equal("2:07:09 PM", Formatting.Sprintf("{1:H:MM:SS AM/PM}", SampleTimestamp));

        // ...and the same mask without the designator stays on a 24-hour clock, which is the
        // assertion that shows the selection is driven by the designator and not by the hour token.
        Assert.Equal("14:07:09", Formatting.Sprintf("{1:H:MM:SS}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] The SHORT meridiem designator, a/p, which PowerBuilder documents
        // alongside am/pm and which no call site in the estate uses. It renders a single letter, and
        // it selects the 12-hour clock exactly as the long form does - the assertion that matters is
        // that the hour is 2 rather than 14, because a designator the mask scanner failed to
        // recognise would leave the clock at 24 hours AND emit the solidus as a literal.
        Assert.Equal("2:07:09 P", Formatting.Sprintf("{1:H:MM:SS A/P}", SampleTimestamp));
        Assert.Equal("9:07:09 A", Formatting.Sprintf("{1:H:MM:SS A/P}", new DateTime(2024, 5, 15, 9, 7, 9)));

        // [CHARACTERIZES THE PORT] Both designator orders are accepted, in both lengths, because
        // PowerBuilder documents both - and both map onto the same output, since .NET has a single
        // designator specifier per length rather than one per order. So p/a does NOT invert anything.
        Assert.Equal("2:07:09 P", Formatting.Sprintf("{1:H:MM:SS P/A}", SampleTimestamp));
        Assert.Equal("2:07:09 PM", Formatting.Sprintf("{1:H:MM:SS PM/AM}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] ...and the designator is matched case-insensitively too, like
        // every other token.
        Assert.Equal("2:07:09 P", Formatting.Sprintf("{1:h:mm:ss a/p}", SampleTimestamp));
        Assert.Equal("2:07:09 PM", Formatting.Sprintf("{1:h:mm:ss am/pm}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] Genuinely unverified tokens follow. A four-character day run is a
        // full day name; a four-character month run is a full month name.
        Assert.Equal("Wednesday", Formatting.Sprintf("{1:DDDD}", SampleTimestamp));
        Assert.Equal("May", Formatting.Sprintf("{1:MMMM}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] Fractional seconds. PowerBuilder uses up to six f for
        // microseconds and .NET accepts up to seven, so a six-run is representable either way.
        Assert.Equal("000000", Formatting.Sprintf("{1:FFFFFF}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] A single-token mask, which is the one shape .NET would otherwise
        // misread: a one-character custom format string is interpreted as a STANDARD specifier, so
        // the port forces the custom reading. "D" therefore means day-of-month here, as it does in
        // PowerBuilder, and not .NET's long-date pattern.
        Assert.Equal("15", Formatting.Sprintf("{1:D}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] A mask token that means nothing in either dialect is emitted as
        // literal text rather than dropped or rejected.
        Assert.Equal("QQ 2024", Formatting.Sprintf("{1:QQ YYYY}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] The separators are escaped individually before the mask reaches
        // .NET, which is a determinism measure rather than a stylistic one: an unescaped '/' is the
        // culture's DATE separator placeholder and an unescaped ':' is the culture's TIME separator
        // placeholder, so an unescaped mask could still take a culture-dependent path even with an
        // invariant provider supplied. These two rows are what prove the separators survive as
        // themselves.
        Assert.Equal("2024/05/15", Formatting.Sprintf("{1:YYYY/MM/DD}", SampleTimestamp));
        Assert.Equal("2024.05.15", Formatting.Sprintf("{1:YYYY.MM.DD}", SampleTimestamp));

        // [CHARACTERIZES THE PORT] RUN-LENGTH CLAMPING, which is where the two dialects' maximum
        // token widths differ and is therefore worth pinning rather than leaving to chance. Each
        // token run is capped at the widest form .NET accepts, so an over-long run degrades to the
        // widest legal one instead of being emitted verbatim and rejected.
        //
        // The year cap is the observable one and it is genuinely surprising: five y in .NET means
        // five DIGITS, so a six-or-more-y run renders 2024 as "02024" with a leading zero. Asserting
        // it is the only way a reader can tell that from a defect.
        Assert.Equal("02024", Formatting.Sprintf("{1:YYYYYY}", SampleTimestamp));     // 6 y -> 5
        Assert.Equal("02024", Formatting.Sprintf("{1:YYYYYYYY}", SampleTimestamp));   // 8 y -> 5
        Assert.Equal("2024", Formatting.Sprintf("{1:YYY}", SampleTimestamp));         // 3 y, uncapped

        // The second, day, hour and month caps, and one run SHORT of each cap so both sides of the
        // comparison are exercised. A single S is a second with no leading zero; two or more is a
        // padded second. Note the day cap yields a full day NAME and the month cap a full month
        // name, so an over-long run of either is still meaningful rather than mangled.
        Assert.Equal("14:07:9", Formatting.Sprintf("{1:HH:MM:S}", SampleTimestamp));      // 1 s
        Assert.Equal("14:07:09", Formatting.Sprintf("{1:HH:MM:SSS}", SampleTimestamp));   // 3 s -> 2
        Assert.Equal("14", Formatting.Sprintf("{1:HHH}", SampleTimestamp));               // 3 h -> 2
        Assert.Equal("Wednesday", Formatting.Sprintf("{1:DDDDD}", SampleTimestamp));      // 5 d -> 4
        Assert.Equal("May", Formatting.Sprintf("{1:MMMMM}", SampleTimestamp));            // 5 M -> 4

        // Fractional seconds: PowerBuilder uses up to six for microseconds and .NET accepts up to
        // seven, so an eight-run clamps to seven. SampleTimestamp has no sub-second component, which
        // is why every digit is a zero - that is the value, not a truncation.
        Assert.Equal("09.0000000", Formatting.Sprintf("{1:SS.FFFFFFFF}", SampleTimestamp));
    }

}
