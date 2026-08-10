// ==================================================================================================
//  ParseErrorFormatterTests - THE CHARACTERIZATION SUITE FOR THE COLUMN-EXPRESSION ERROR REPORTER
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   services/dataservices-service/PowerFramework.DataServices/Expressions/ParseErrorFormatter.cs
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//              the two `_of_buildparseerror` bodies at :L2402-L2404 and :L2406, declared at :L206
//              and :L207, plus the 28 `MessageBox` call sites they serve.
//            ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L104-L109  (the Han variable
//              names the scenario actually uses)
//            docs/n_cst_dwsvc_columnexp.md:L49                                 (the documented
//              worked example `"$上月读数 + $本月读数"`)
//            ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17     (CAT_MSGBOX and
//              CAT_DWSVC, the categories these 28 messages deliberately do NOT use)
//            ws_objects/pfw.common.pbl.src/sprintf.srf:L7-L27                   (the 21 native
//              prototypes behind Formatting.Sprintf)
//
//  NO USER RULES GOVERN THIS FILE. `review_rules` reports, in full, "No user rules provided." The
//  enterprise baseline of AAP 0.7.2 applies in their place, and the binding constraints are the
//  non-rule ones AAP 0.7.3 enumerates. The four that bear on this file are named at each place they
//  are discharged: C-B (preserve behaviour exactly, defects included), 0.2.1.3 Correction 5 (a
//  dialog becomes a structured result and NOTHING ELSE changes), C-K (cite the locator), and the
//  0.4.5.3 naming constraint (SCREAMING_SNAKE constants are REFERENCED here and DECLARED nowhere
//  here).
//
// ==================================================================================================
//  WHY THIS FILE EXISTS, AND WHAT IT IS THE ONLY OWNER OF
// ==================================================================================================
//
//  `_of_buildparseerror` is eleven lines of PowerScript that decide what a user sees when a column
//  expression will not parse. Nothing else in the estate renders that message, and every one of the
//  eleven caret-bearing failures goes through it - so its output is in every characterization
//  recording of a failed parse, and a single byte of drift invalidates all of them at once.
//
//  Three of its properties are invisible to a compiler and to every analyzer, and each is asserted
//  here rather than trusted:
//
//    1. THE PAD IS SIZED IN BYTES. `Fill("_",LenA(Left(syntax,pos)) - 1)` [:L2403] uses `LenA`, the
//       DBCS BYTE length, not `Len`. Every message in the object is hardcoded Chinese, so a
//       character count silently halves the indent under exactly the input the oracle contains. Both
//       expressions are well-typed and both look plausible. See `DbcsCaretArithmeticTests`.
//
//    2. THE SEPARATOR IS DOUBLED ABOVE AND SINGLE BELOW. `errInfo += "~n~n"` fires only inside the
//       non-empty guard [:L2402], and the expression is followed by exactly one `~n` [:L2403]. A
//       symmetric port reads as tidier and is wrong twice. See `RenderedLayoutTests`.
//
//    3. THE MIDPOINT DIVISION TRUNCATES. `nMacPos + (nPos - nMacPos) / 2` is integer division on
//       two `long`s assigned into a `long`, so an odd span lands the caret BEFORE the true midpoint.
//       A `double` port rounds and moves the caret one column. See `CaretPositionConventionTests`.
//
//  A fourth property is a preserved DEFECT rather than a subtlety: all 28 messages are hardcoded
//  Chinese and none routes through localization, while the equivalent messages elsewhere in the same
//  DataWindow service layer DO localize through `CAT_DWSVC` [se_cst_dw.sru:L357, :L368]. That
//  inconsistency is reproduced, not harmonized, and `NonLocalizationTests` proves it with a
//  localization provider that would translate every one of the 28 if the path were ever taken.
//
// ==================================================================================================
//  HOW THE EXPECTATIONS WERE PRODUCED - GOLDEN-MASTER DISCIPLINE
// ==================================================================================================
//
//  Every expectation in this file was written from `ws_objects/**`, never from the C# under test. An
//  expectation computed by calling the subject asserts only that the subject equals itself, which is
//  the one thing a characterization suite must not do (AAP 0.3.3, Feathers ch. 13).
//
//  Concretely, that discipline shows up in three places:
//
//    * `ParseErrorOracle` restates `Left`, `Fill`, `LenA` and the whole of :L2402-L2404 from the
//      PowerScript. It is an INDEPENDENT SECOND IMPLEMENTATION, and the theories assert that it and
//      the subject agree. Two independent transcriptions agreeing is evidence; one transcription
//      agreeing with itself is not.
//    * Every rendered string is ALSO asserted as a literal, so a shared misreading of the oracle by
//      both transcriptions still fails. The literals were derived by hand from the oracle and then
//      confirmed against the engine's live output.
//    * The caret positions in `CaretPositionConventionTests` were derived by tracing the oracle's
//      scanner - `nMacPos = nPos` at the sigil [:L1287], the terminator arm at :L1403-:L1404, the
//      formula at :L1417 and its ten siblings - and only then compared with what the engine reports.
//
//  POSITIONS ARE INDICES INTO THE TRAILER-EXTENDED EXPRESSION. The parser appends a sentinel before
//  scanning - `constant string TRAILER = ";"` [:L1252] and `sExp = exp + TRAILER` [:L1259] - and
//  every caret-bearing site passes `sExp`, not the caller's expression. So a rendered marker is one
//  character longer than the expression the caller supplied, and a position may point AT the
//  sentinel. The rows below carry the sentinel explicitly wherever they model a real parse.
//
//  AND POSITIONS ARE CHARACTER INDICES WHILE THE PAD IS BYTE-SIZED. `Mid` and `Len` count
//  characters, so the scanner's arithmetic is in characters; `LenA` counts bytes, so the indent is
//  in bytes. That asymmetry is not a defect - it is the whole reason the caret lines up in a
//  fixed-width console where a Han character occupies two columns - and `$上月读数+1` reports
//  position 3 while drawing FOUR underscores. That single row is the sharpest statement of the
//  hazard in the file.
//
// ==================================================================================================
//  CONVENTIONS
// ==================================================================================================
//
//    * Plain xunit `Assert`. There is no mocking library and no fluent-assertion library in the
//      dependency inventory (AAP 0.5.3), and none is wanted: a parity assertion has to be legible
//      beside the PowerScript it transcribes.
//    * Every multi-row assertion is a `[Theory]` over `[MemberData]`, per AAP 0.6.7, and every row
//      carries its oracle locator. The locator is not decorative - it is threaded into a failing
//      assertion's message, so a red test names the line of the read-only source it disagrees with.
//    * Every string comparison is ORDINAL. No trimming, no line-ending normalisation, no culture.
//    * `Environment.NewLine` appears in NO expectation anywhere in this file. PowerScript's `~n` is
//      U+000A alone; on a Windows host `Environment.NewLine` would add a carriage return to every
//      rendered message. The services run on Linux today, where the two happen to agree, which is
//      exactly what would let the mistake ship unnoticed.
//    * Multi-line strings are compared through `Escape`, which renders U+000A as `[LF]`, so a
//      failure prints one legible line instead of a wrapped diff.
//    * No SCREAMING_SNAKE identifier is DECLARED here. `RetCode.E_INVALID_ARGUMENT`,
//      `RetCode.FAILED`, `Enums.I18N_SRC_PFW` and `Categories.CAT_DWSVC` are referenced from the
//      libraries that own them (AAP 0.4.5.3).
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// An independent restatement of the PowerScript this file characterizes, transcribed from
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru</c> and from nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS A SECOND IMPLEMENTATION, NOT A WRAPPER.</b> Nothing here calls
/// <see cref="ParseErrorFormatter"/>. The theories assert that two independent transcriptions of the
/// same eleven lines agree, which is evidence about the transcription; a helper that delegated to the
/// subject would assert only that the subject equals itself.
/// </para>
/// <para>
/// It is deliberately small and deliberately literal. Each member reproduces one PowerBuilder
/// primitive at its cited line, including the boundary behaviour that primitive has and .NET's
/// nearest equivalent does not.
/// </para>
/// </remarks>
internal static class ParseErrorOracle
{
    /// <summary>
    /// The scan sentinel the parser appends before scanning:
    /// <c>constant string TRAILER = ";"</c> [<c>n_cst_dwsvc_columnexp.sru:L1252</c>], applied as
    /// <c>sExp = exp + TRAILER</c> [<c>:L1259</c>].
    /// </summary>
    /// <remarks>
    /// Every caret-bearing site passes <c>sExp</c> - the EXTENDED string - to the formatter, so a row
    /// that models a real parse must carry this character or its position arithmetic is off by one.
    /// </remarks>
    public const string Trailer = ";";

    /// <summary>
    /// The line feed PowerScript's <c>~n</c> escape denotes, U+000A.
    /// </summary>
    /// <remarks>
    /// Declared here so that no expectation in this file has to spell a raw escape, and so that the
    /// contrast with a platform newline is stated once rather than trusted. It is NOT
    /// <c>Environment.NewLine</c> and must never become it.
    /// </remarks>
    public const string LineFeed = "\n";

    // The five Han variable names the legacy scenario actually uses, transcribed rather than
    // translated. A Latin rename would not exercise the same path: a multi-byte identifier is
    // precisely where a character-count-versus-byte-count confusion surfaces.
    // [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L104-L109]

    /// <summary><c>of_AddVarExp("上月读数","4")</c> [<c>w_test_dwsvc_columnexp.srw:L107</c>].</summary>
    public const string LastMonthReading = "上月读数";

    /// <summary><c>of_AddVarExp("本月读数","5")</c> [<c>w_test_dwsvc_columnexp.srw:L108</c>].</summary>
    public const string ThisMonthReading = "本月读数";

    /// <summary><c>of_AddVar("精度",0)</c> [<c>w_test_dwsvc_columnexp.srw:L109</c>].</summary>
    public const string Precision = "精度";

    /// <summary><c>of_AddVarExp("损耗","1")</c> [<c>w_test_dwsvc_columnexp.srw:L104</c>].</summary>
    public const string Loss = "损耗";

    /// <summary><c>of_AddVarExp("倍率","2")</c> [<c>w_test_dwsvc_columnexp.srw:L105</c>].</summary>
    public const string Multiplier = "倍率";

    /// <summary><c>of_AddVarExp("回程","'3'")</c> [<c>w_test_dwsvc_columnexp.srw:L106</c>].</summary>
    public const string ReturnTrip = "回程";

    /// <summary>
    /// PowerBuilder <c>Left(value, count)</c>, with the clamps .NET's substring does not have.
    /// </summary>
    /// <param name="value">The string to take from.</param>
    /// <param name="count">How many CHARACTERS to take.</param>
    /// <returns>The clamped leading slice.</returns>
    /// <remarks>
    /// Both clamps are load-bearing at <c>:L2403</c>. The lower one is what lets a zero or negative
    /// position render at all; the upper one is what lets a position that points at - or past - the
    /// trailer sentinel render instead of faulting. <c>value.Substring(0, count)</c> throws at both
    /// ends.
    /// </remarks>
    public static string Left(string value, long count)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (count <= 0)
        {
            return string.Empty;
        }

        return count >= value.Length ? value : value[..(int)count];
    }

    /// <summary>
    /// PowerBuilder <c>Fill(character, count)</c> for the single character the formatter pads with.
    /// </summary>
    /// <param name="count">How many copies to produce.</param>
    /// <returns>
    /// The empty string for a non-positive <paramref name="count"/>; otherwise that many underscores.
    /// </returns>
    /// <remarks>
    /// THE NON-POSITIVE CASE IS REACHED BY REAL INPUT, not by a hypothetical. <c>:L2403</c> hands
    /// <c>Fill</c> the value <c>LenA(Left(syntax,pos)) - 1</c>, which is <c>-1</c> whenever
    /// <c>pos</c> is zero or less. PowerBuilder answers the empty string; <c>new string('_', -1)</c>
    /// throws.
    /// </remarks>
    public static string Fill(long count)
    {
        return count <= 0 ? string.Empty : new string('_', (int)count);
    }

    /// <summary>
    /// PowerBuilder <c>LenA(value)</c>: the length of a string in BYTES under the host's DBCS
    /// codepage.
    /// </summary>
    /// <param name="value">The string to measure.</param>
    /// <returns>The byte length.</returns>
    /// <remarks>
    /// <para>
    /// The rule, restated from the codepage rather than from the subject: an ASCII code point occupies
    /// ONE byte and every other code point occupies TWO. That is exactly why the legacy sizes a
    /// VISUAL indent with a BYTE count - in the fixed-width console the caret was designed for, a Han
    /// character occupies two columns and two bytes at once.
    /// </para>
    /// <para>
    /// A well-formed surrogate pair counts TWO, not four: <c>LenA</c> measures characters as bytes and
    /// a surrogate pair is one character.
    /// </para>
    /// </remarks>
    public static long ByteLength(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        long bytes = 0;

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];

            if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                bytes += 2;
                continue;
            }

            bytes += current < 0x0080 ? 1 : 2;
        }

        return bytes;
    }

    /// <summary>
    /// The whole of <c>_of_buildparseerror</c> [<c>n_cst_dwsvc_columnexp.sru:L2402-L2404</c>],
    /// restated term by term.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position within <paramref name="syntax"/>.</param>
    /// <param name="errinfo">The message to place above the expression, possibly empty.</param>
    /// <returns>The rendered message.</returns>
    /// <remarks>
    /// <code>
    /// if errInfo &lt;&gt; "" then errInfo += "~n~n"
    /// return errInfo + syntax + "~n" + Fill("_",LenA(Left(syntax,pos)) - 1) + "^ (" + String(pos) + ")"
    /// </code>
    /// The order of operations is the oracle's and is not simplified: <c>Left</c>, then
    /// <c>LenA</c>, then the subtraction ON THE BYTE COUNT, then <c>Fill</c>. Folding the subtraction
    /// into the slice would change the answer for every non-ASCII prefix.
    /// </remarks>
    public static string Render(string syntax, long pos, string errinfo)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(errinfo);

        string message = errinfo.Length == 0 ? errinfo : errinfo + LineFeed + LineFeed;

        return message
            + syntax
            + LineFeed
            + Fill(ByteLength(Left(syntax, pos)) - 1)
            + "^ ("
            + pos.ToString(CultureInfo.InvariantCulture)
            + ")";
    }

    /// <summary>
    /// The two-argument arity [<c>n_cst_dwsvc_columnexp.sru:L2406</c>]:
    /// <c>return _of_BuildParseError(syntax,pos,"")</c>.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position.</param>
    /// <returns>The rendered expression and caret, with no message above them.</returns>
    public static string Render(string syntax, long pos)
    {
        return Render(syntax, pos, string.Empty);
    }

    /// <summary>
    /// The caret position ten of the eleven caret-bearing sites report:
    /// <c>nMacPos + (nPos - nMacPos) / 2</c>.
    /// </summary>
    /// <param name="macroStart">
    /// <c>nMacPos</c> - the position the macro token started at, assigned when the sigil is met
    /// [<c>n_cst_dwsvc_columnexp.sru:L1287</c>].
    /// </param>
    /// <param name="terminator">
    /// <c>nPos</c> - where the scanner stood when the failure was detected, which for a variable
    /// macro is the terminator that ended the token [<c>:L1403</c>].
    /// </param>
    /// <returns>The truncated midpoint of the offending token.</returns>
    /// <remarks>
    /// <para>
    /// <b>INTEGER DIVISION ON TWO <c>long</c>s.</b> PowerScript's <c>/</c> assigned into a <c>long</c>
    /// truncates, so an ODD span lands the caret strictly BEFORE the true midpoint. A <c>double</c>
    /// port would round and move the caret one column, and the column appears in the rendered text at
    /// <c>:L2403</c>, so it appears in every characterization recording.
    /// </para>
    /// <para>
    /// The formula points into the MIDDLE of the offending token rather than at its first character.
    /// That looks like sloppiness and it is observable output, so it is reproduced (constraint C-B).
    /// Sites: [<c>:L1308</c>, <c>:L1383</c>, <c>:L1390</c>, <c>:L1395</c>, <c>:L1399</c>,
    /// <c>:L1417</c>, <c>:L1422</c>, <c>:L1426</c>, <c>:L1431</c>, <c>:L1486</c>].
    /// </para>
    /// </remarks>
    public static long MacroTokenMidpoint(long macroStart, long terminator)
    {
        return macroStart + ((terminator - macroStart) / 2);
    }

    /// <summary>
    /// Renders a possibly multi-line string as one legible line, with U+000A shown as
    /// <c>[LF]</c>.
    /// </summary>
    /// <param name="value">The string to escape, or <see langword="null"/>.</param>
    /// <returns>The escaped rendering, or <c>&lt;null&gt;</c>.</returns>
    /// <remarks>
    /// For failure messages only, never for an expectation. A rendered parse error contains at least
    /// two line feeds, and an assertion message that wraps across four lines is materially harder to
    /// read than one that names its separators.
    /// </remarks>
    public static string Escape(string? value)
    {
        return value is null
            ? "<null>"
            : value.Replace(LineFeed, "[LF]", StringComparison.Ordinal)
                .Replace("\r", "[CR]", StringComparison.Ordinal);
    }

    /// <summary>Counts the line feeds in a rendered message.</summary>
    /// <param name="value">The rendered message.</param>
    /// <returns>How many U+000A characters it contains.</returns>
    /// <remarks>
    /// Written out rather than taken from a query operator so that the count is unambiguously a count
    /// of U+000A and not of "whatever the platform considers a line break".
    /// </remarks>
    public static int CountLineFeeds(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int count = 0;

        foreach (char current in value)
        {
            if (current == '\n')
            {
                count++;
            }
        }

        return count;
    }
}

// ==================================================================================================
//  PHASE 1 - THE EXACT RENDERED LAYOUT, BOTH ARITIES
// ==================================================================================================

/// <summary>
/// Pins the composition of <c>_of_buildparseerror</c> [<c>n_cst_dwsvc_columnexp.sru:L2402-L2404</c>]
/// and the delegation of its two-argument arity [<c>:L2406</c>], byte for byte.
/// </summary>
/// <remarks>
/// The asymmetry is the point of this class. TWO line feeds follow a non-empty message and NONE
/// follows an empty one, while exactly ONE follows the expression. A port that made the separator
/// symmetric would read as tidier and would be wrong in two places at once, and neither error is
/// visible to a compiler.
/// </remarks>
public sealed class ParseErrorRenderedLayoutTests
{
    /// <summary>
    /// The rendered layout, one row per shape the oracle produces, with the expected string derived
    /// by hand from <c>:L2402-L2404</c>.
    /// </summary>
    /// <remarks>
    /// Every <c>syntax</c> here carries the trailer sentinel <c>";"</c> [<c>:L1252</c>, <c>:L1259</c>]
    /// because every legacy caret-bearing call site passes <c>sExp</c>, the extended string.
    /// </remarks>
    public static TheoryData<string, long, string, string, string> LayoutMatrix() => new()
    {
        // ---- The empty message: NOTHING is prepended, so the expression starts on the FIRST line.
        //      This is exactly what the two-argument arity produces [:L2406].
        {
            "a+$x;",
            4L,
            "",
            "a+$x;\n___^ (4)",
            ":L2403 - Left(\"a+$x;\",4)=\"a+$x\", LenA=4, pad=3"
        },

        // ---- The same expression WITH a message: two line feeds appear between them and the caret
        //      line is untouched. Message transcribed from the undefined-variable arm.
        {
            "a+$x;",
            4L,
            "解析变量宏失败!\n变量[x],未定义",
            "解析变量宏失败!\n变量[x],未定义\n\na+$x;\n___^ (4)",
            ":L1422 message over :L2403 layout"
        },

        // ---- A real undefined-variable failure, position and pad as the scanner reports them.
        {
            "$nosuch + 1;",
            5L,
            "解析变量宏失败!\n变量[nosuch],未定义",
            "解析变量宏失败!\n变量[nosuch],未定义\n\n$nosuch + 1;\n____^ (5)",
            ":L1422 - nMacPos=1, nPos=9, midpoint 5, Left=\"$nosu\", LenA=5, pad=4"
        },

        // ---- The invalid-name arm, whose position is 1 and whose pad is therefore EMPTY. The caret
        //      sits in the first column with no rule drawn to it at all.
        {
            "$+ 1;",
            1L,
            "解析变量宏失败!\n无效的名称",
            "解析变量宏失败!\n无效的名称\n\n$+ 1;\n^ (1)",
            ":L1486 - nMacPos=1, nPos=2, midpoint 1, LenA(\"$\")=1, pad=0"
        },

        // ---- The unclosed-quote arm, the ONE site whose position is the opening quote rather than a
        //      token midpoint [:L1505 passes nQuotePos].
        {
            "$$v + 'abcdefgh;",
            7L,
            "解析变量宏失败!\n引号未关闭",
            "解析变量宏失败!\n引号未关闭\n\n$$v + 'abcdefgh;\n______^ (7)",
            ":L1505 - nQuotePos=7, Left=\"$$v + '\", LenA=7, pad=6"
        },

        // ---- A context function macro, refused before its arguments are even examined. Its message
        //      is the ONLY caret-bearing one with no substitution argument and no bracketed name.
        {
            "@$$Fn(n2);",
            3L,
            "解析函数宏失败!\n不支持引用上下文的函数宏",
            "解析函数宏失败!\n不支持引用上下文的函数宏\n\n@$$Fn(n2);\n__^ (3)",
            ":L1308 - nMacPos=1, nPos=6 after two sigil skips, midpoint 3, LenA(\"@$$\")=3, pad=2"
        },

        // ---- HAN IN BOTH THE MESSAGE AND THE EXPRESSION, which is the realistic case: the oracle's
        //      own scenario names its variables in Chinese. Position 3 draws FOUR underscores.
        {
            "$上月读数+1;",
            3L,
            "解析变量宏失败!\n变量[上月读数],未定义",
            "解析变量宏失败!\n变量[上月读数],未定义\n\n$上月读数+1;\n____^ (3)",
            ":L1422 with w_test_dwsvc_columnexp.srw:L107 - LenA(\"$上月\")=1+2+2=5, pad=4"
        },

        // ---- The documented worked example, extended with the trailer. A caret inside the SECOND
        //      Han name is where a character-count port drifts furthest.
        {
            "$上月读数 + $本月读数;",
            11L,
            "解析变量宏失败!\n变量[本月读数],未定义",
            "解析变量宏失败!\n变量[本月读数],未定义\n\n$上月读数 + $本月读数;\n________________^ (11)",
            "docs/n_cst_dwsvc_columnexp.md:L49 over :L2403 - LenA of the 11-character prefix is 17, pad=16"
        },
    };

    /// <summary>
    /// The three-argument arity composes message, separator, expression, pad, caret and position in
    /// exactly the oracle's order.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position.</param>
    /// <param name="errinfo">The message, empty for the two-argument shape.</param>
    /// <param name="expected">The rendering derived by hand from the oracle.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    [Theory]
    [MemberData(nameof(LayoutMatrix))]
    public void ThreeArgumentArity_RendersTheOracleComposition(
        string syntax,
        long pos,
        string errinfo,
        string expected,
        string locator)
    {
        string actual = ParseErrorFormatter.BuildParseError(syntax, pos, errinfo);

        Assert.Equal(expected, actual);

        // THE LOCATOR IS LOAD-BEARING, NOT DECORATIVE. Threading it into a real invariant's message is
        // what makes a red row name the line of the read-only oracle it disagrees with, and :L2403 ends
        // every rendering with the parenthesised position without exception.
        // The single-character overload is used deliberately: it is inherently ordinal, so there is no
        // comparison mode to get wrong here.
        Assert.True(
            actual.EndsWith(')'),
            locator + " must end with the parenthesised position, but rendered: "
                + ParseErrorOracle.Escape(actual));

        // The independent restatement agrees. Two transcriptions of :L2402-L2404 concurring is
        // evidence about the transcription; one concurring with itself would not be.
        Assert.Equal(ParseErrorOracle.Render(syntax, pos, errinfo), actual);
    }

    /// <summary>
    /// A non-empty message is followed by exactly TWO line feeds and the expression by exactly ONE.
    /// </summary>
    /// <remarks>
    /// Counted rather than pattern-matched, because the message itself contains a line feed - the
    /// legacy writes <c>"解析变量宏失败!~n变量[...],未定义"</c> - so a naive "contains two feeds" check
    /// would pass on a port that emitted one separator and relied on the message's own.
    /// </remarks>
    [Fact]
    public void NonEmptyMessage_IsFollowedByExactlyTwoLineFeeds_AndTheExpressionByExactlyOne()
    {
        // One line feed INSIDE the message [:L1422], so the totals below are unambiguous.
        const string message = "解析变量宏失败!\n变量[x],未定义";
        const string syntax = "a+$x;";

        string rendered = ParseErrorFormatter.BuildParseError(syntax, 4L, message);

        // [:L2402] `errInfo += "~n~n"` - the message, then TWO feeds, then the expression.
        Assert.StartsWith(
            message + ParseErrorOracle.LineFeed + ParseErrorOracle.LineFeed + syntax,
            rendered,
            StringComparison.Ordinal);

        // Not three. A doubled append would be invisible to a StartsWith check on two.
        Assert.DoesNotContain(
            message + ParseErrorOracle.LineFeed + ParseErrorOracle.LineFeed + ParseErrorOracle.LineFeed,
            rendered,
            StringComparison.Ordinal);

        // [:L2403] `+ syntax + "~n" +` - the expression, then exactly ONE feed, then the caret line.
        Assert.EndsWith(syntax + ParseErrorOracle.LineFeed + "___^ (4)", rendered, StringComparison.Ordinal);

        // 1 inside the message + 2 separating + 1 after the expression = 4, and no more.
        Assert.Equal(4, ParseErrorOracle.CountLineFeeds(rendered));
    }

    /// <summary>
    /// An EMPTY message prepends nothing at all, so the expression starts on the first line.
    /// </summary>
    /// <remarks>
    /// This is the guarded half of <c>:L2402</c>: <c>if errInfo &lt;&gt; "" then</c>. It is also what
    /// the two-argument arity relies on, so a port that appended the separator unconditionally would
    /// put a blank line at the top of every rendered marker in every characterization recording.
    /// </remarks>
    [Fact]
    public void EmptyMessage_PrependsNothing_SoTheExpressionStartsOnTheFirstLine()
    {
        string rendered = ParseErrorFormatter.BuildParseError("a+$x;", 4L, string.Empty);

        Assert.Equal("a+$x;\n___^ (4)", rendered);
        Assert.StartsWith("a+$x;", rendered, StringComparison.Ordinal);

        // Exactly one feed in the whole rendering: the one at :L2403 after the expression.
        Assert.Equal(1, ParseErrorOracle.CountLineFeeds(rendered));
    }

    /// <summary>
    /// The syntax and position pairs the delegation is checked over.
    /// </summary>
    /// <remarks>
    /// Deliberately includes a Han expression and a zero position, because the delegation has to hold
    /// at the boundaries too - those are precisely the inputs an optional-parameter collapse would be
    /// most likely to get subtly different.
    /// </remarks>
    public static TheoryData<string, long, string> DelegationMatrix() => new()
    {
        { "a+$x;", 4L, ":L2406 over the header's worked example" },
        { "$nosuch + 1;", 5L, ":L2406 over the undefined-variable arm at :L1422" },
        { "$上月读数+1;", 3L, ":L2406 over a Han expression, w_test_dwsvc_columnexp.srw:L107" },
        { "$+ 1;", 1L, ":L2406 at the zero-pad boundary, :L1486" },
        { "a+$x;", 0L, ":L2406 at the negative-fill boundary" },
        { "", 1L, ":L2406 over an empty expression" },
    };

    /// <summary>
    /// The two-argument arity is the three-argument arity with an empty message - and that identity is
    /// the guarantee that the published contract's two renderings cannot disagree.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    /// <remarks>
    /// <c>:L2406</c> is <c>return _of_BuildParseError(syntax,pos,"")</c> and nothing else. The two
    /// arities are not collapsed into one optional parameter in the port because they feed two
    /// different fields of <c>dataservices.v1.ExpressionError</c> - the marker and the text - and this
    /// identity is what stops those two fields describing different expressions, pads or positions.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DelegationMatrix))]
    public void TwoArgumentArity_IsTheThreeArgumentArityWithAnEmptyMessage(
        string syntax,
        long pos,
        string locator)
    {
        string shortForm = ParseErrorFormatter.BuildParseError(syntax, pos);
        string longForm = ParseErrorFormatter.BuildParseError(syntax, pos, string.Empty);

        Assert.Equal(longForm, shortForm);

        Assert.True(
            string.Equals(shortForm, ParseErrorOracle.Render(syntax, pos), StringComparison.Ordinal),
            locator + " expected " + ParseErrorOracle.Escape(ParseErrorOracle.Render(syntax, pos))
                + " but rendered " + ParseErrorOracle.Escape(shortForm));
    }

    /// <summary>
    /// The caret line is underscores, then the caret, then ONE space, then the position in
    /// parentheses - and the space inside <c>"^ ("</c> is the only one in it.
    /// </summary>
    /// <remarks>
    /// The literal at <c>:L2403</c> is <c>"^ ("</c>. There is no space between the pad and the caret,
    /// none between the parenthesis and the digits, and none before the closing parenthesis. An extra
    /// space anywhere would be invisible in a rendered dialog and fatal to a byte-exact comparison.
    /// </remarks>
    [Fact]
    public void CaretLine_IsPadThenCaretThenOneSpaceThenParenthesisedPosition()
    {
        string rendered = ParseErrorFormatter.BuildParseError("$nosuch + 1;", 5L);

        // The caret line is everything after the LAST feed; the expression above it contains spaces of
        // its own, so the whole rendering cannot be counted directly.
        int lastFeed = rendered.LastIndexOf('\n');
        Assert.True(lastFeed >= 0, "The rendering must contain the :L2403 line feed.");

        string caretLine = rendered[(lastFeed + 1)..];

        Assert.Equal("____^ (5)", caretLine);

        // Exactly one space in the caret line, and it sits immediately after the caret.
        Assert.Equal(1, caretLine.Split(' ').Length - 1);

        int caret = caretLine.IndexOf(ParseErrorFormatter.CaretCharacter);
        Assert.Equal(caret + 1, caretLine.IndexOf(' '));
        Assert.Equal("^ (", caretLine.Substring(caret, 3));

        // Everything before the caret is the pad character and nothing else.
        Assert.Equal(new string(ParseErrorFormatter.PaddingCharacter, caret), caretLine[..caret]);

        // And the pad character is an UNDERSCORE, not a space - it draws a visible rule, so a renderer
        // that treated it as whitespace would be normalising observable output.
        Assert.Equal('_', ParseErrorFormatter.PaddingCharacter);
        Assert.Equal('^', ParseErrorFormatter.CaretCharacter);
    }

    /// <summary>
    /// The separator is a single line feed and never a carriage-return pair, on any host.
    /// </summary>
    /// <remarks>
    /// PowerScript's <c>~n</c> is U+000A alone. The published separator is asserted as the literal
    /// line feed rather than compared against a platform newline, because the services run on Linux
    /// today - where the two coincide - and that coincidence is exactly what would let a platform
    /// newline ship unnoticed and then add a byte to every recorded message on a Windows host.
    /// </remarks>
    [Fact]
    public void Separator_IsASingleLineFeedAndNeverACarriageReturnPair()
    {
        Assert.Equal("\n", ParseErrorFormatter.LineSeparator);
        Assert.Equal(1, ParseErrorFormatter.LineSeparator.Length);
        Assert.Equal('\u000A', ParseErrorFormatter.LineSeparator[0]);

        string rendered = ParseErrorFormatter.BuildParseError(
            "a+$x;",
            4L,
            "解析变量宏失败!\n变量[x],未定义");

        Assert.DoesNotContain("\r", rendered, StringComparison.Ordinal);
        Assert.Equal(4, ParseErrorOracle.CountLineFeeds(rendered));
    }

    /// <summary>
    /// <see langword="null"/> is treated as absent at both parameters, and never propagated.
    /// </summary>
    /// <remarks>
    /// PowerScript would evaluate <c>null &lt;&gt; ""</c> to null, skip the guard and poison the whole
    /// concatenation - but that path is unreachable from the oracle, because all eleven caret-bearing
    /// sites pass a non-empty literal and <c>:L2406</c> passes <c>""</c>. Reproducing it would hand
    /// every caller a null to check for a state the legacy cannot reach, which is a branch the legacy
    /// does not have.
    /// </remarks>
    [Fact]
    public void NullSyntaxAndNullMessage_AreTreatedAsAbsentAndNeverPropagated()
    {
        Assert.Equal(
            ParseErrorFormatter.BuildParseError("a+$x;", 4L, string.Empty),
            ParseErrorFormatter.BuildParseError("a+$x;", 4L, null));

        Assert.Equal("\n^ (3)", ParseErrorFormatter.BuildParseError(null, 3L, null));
        Assert.Equal("\n^ (3)", ParseErrorFormatter.BuildParseError(null, 3L));
    }

    /// <summary>
    /// <c>String(pos)</c> [<c>:L2403</c>] renders invariant digits with no group separator and no
    /// culture-dependent sign.
    /// </summary>
    /// <remarks>
    /// A host whose locale groups thousands would otherwise render <c>^ (1,234,567)</c> and change a
    /// byte of every recording taken on it.
    /// </remarks>
    [Fact]
    public void Position_IsRenderedWithInvariantDigitsAndNoGroupSeparator()
    {
        string rendered = ParseErrorFormatter.BuildParseError("a+$x;", 1234567L);

        Assert.EndsWith("^ (1234567)", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(",", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", rendered[..rendered.LastIndexOf('^')], StringComparison.Ordinal);
    }
}


// ==================================================================================================
//  PHASE 2 - THE DBCS CARET ARITHMETIC, THE DECISIVE TEST IN THIS FILE
// ==================================================================================================

/// <summary>
/// Pins <c>Fill("_",LenA(Left(syntax,pos)) - 1)</c> [<c>n_cst_dwsvc_columnexp.sru:L2403</c>] as a
/// BYTE-width computation, and fails if it is ever replaced by a character count or by a different
/// encoding's byte count.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS IS THE MOST IMPORTANT CLASS IN THE FILE.</b> <c>LenA</c> is the DBCS byte length and
/// <c>Len</c> is the character length. Every message in the legacy object is hardcoded Chinese and the
/// scenario's own variables are named in Chinese
/// [<c>w_test_dwsvc_columnexp.srw:L104-L109</c>], so the distinction is not academic - it is the
/// common case. A character-count port draws HALF the indent under a Han prefix and a UTF-8 port draws
/// one and a half times too much. Both compile, both produce a plausible-looking string, and no
/// compiler diagnostic and no analyzer covers either.
/// </para>
/// <para>
/// <b>THE SHARED KERNEL HAS NO <c>LenA</c>.</b> <c>PowerFramework.Shared.Kernel.Text</c> publishes
/// <c>Iif</c>, <c>ReplaceAll</c> and <c>ClassNameEx</c>, and <c>Formatting</c> publishes
/// <c>FormatRetCode</c> and <c>Sprintf</c>; neither offers a byte-length primitive. So the formatter
/// publishing <see cref="ParseErrorFormatter.LenA"/> itself is correct, and the assertions below pin
/// the rendered pad to THAT member rather than to an ad-hoc
/// <c>System.Text.Encoding</c> call - which is what would silently change the answer.
/// </para>
/// </remarks>
public sealed class ParseErrorDbcsCaretArithmeticTests
{
    /// <summary>
    /// The byte-width matrix: pure ASCII prefixes, pure Han prefixes and - the realistic case - mixed
    /// prefixes, where a character-count port drifts by a VARIABLE amount.
    /// </summary>
    /// <remarks>
    /// The <c>expectedPad</c> column is stated separately from the rendered literal on purpose. A
    /// failure in the count alone says "the arithmetic moved"; a failure in the literal alone says
    /// "something else in the layout moved". Two columns distinguish those two diagnoses.
    /// </remarks>
    public static TheoryData<string, long, int, string, string> ByteWidthMatrix() => new()
    {
        // ---- PURE ASCII. Byte count and character count agree here, which is exactly why an
        //      ASCII-only test suite would never notice the difference.
        {
            "n1 + n2;",
            5L,
            4,
            "n1 + n2;\n____^ (5)",
            ":L2403 pure ASCII - LenA(\"n1 + \")=5, pad=4, and a char count would agree"
        },
        {
            "$nosuch + 1;",
            9L,
            8,
            "$nosuch + 1;\n________^ (9)",
            ":L1422 pure ASCII - LenA(\"$nosuch +\")=9, pad=8"
        },

        // ---- PURE HAN PREFIX. Three Han characters measure SIX bytes, so the pad is FIVE - twice
        //      what a UTF-16 character count gives, which is two.
        {
            "上月读数 + 1;",
            3L,
            5,
            "上月读数 + 1;\n_____^ (3)",
            "w_test_dwsvc_columnexp.srw:L107 over :L2403 - LenA(\"上月读\")=6, pad=5, char count gives 2"
        },
        {
            "$精度+1;",
            2L,
            2,
            "$精度+1;\n__^ (2)",
            "w_test_dwsvc_columnexp.srw:L109 over :L2403 - LenA(\"$精\")=3, pad=2, char count gives 1"
        },

        // ---- MIXED, WHICH IS THE REALISTIC CASE. The drift of a character-count port is not a
        //      constant factor here: it depends on how many Han characters happen to precede the caret.
        {
            "$上月读数+1;",
            3L,
            4,
            "$上月读数+1;\n____^ (3)",
            "w_test_dwsvc_columnexp.srw:L107 over :L2403 - LenA(\"$上月\")=5, pad=4, char count gives 2"
        },
        {
            "$损耗*$倍率;",
            4L,
            5,
            "$损耗*$倍率;\n_____^ (4)",
            "w_test_dwsvc_columnexp.srw:L104-L105 over :L2403 - LenA(\"$损耗*\")=6, pad=5"
        },
        {
            "$回程 + '3';",
            5L,
            6,
            "$回程 + '3';\n______^ (5)",
            "w_test_dwsvc_columnexp.srw:L106 over :L2403 - LenA(\"$回程 +\")=7, pad=6"
        },
        {
            "$本月读数*$倍率+$损耗;",
            6L,
            9,
            "$本月读数*$倍率+$损耗;\n_________^ (6)",
            "w_test_dwsvc_columnexp.srw:L113 over :L2403 - LenA(\"$本月读数*\")=10, pad=9"
        },

        // ---- THE DOCUMENTED WORKED EXAMPLE, at two different carets. The first lands on the ASCII
        //      operator between the two Han names, the second inside the second name.
        {
            "$上月读数 + $本月读数;",
            7L,
            10,
            "$上月读数 + $本月读数;\n__________^ (7)",
            "docs/n_cst_dwsvc_columnexp.md:L49 over :L2403 - LenA(\"$上月读数 +\")=11, pad=10"
        },
        {
            "$上月读数 + $本月读数;",
            11L,
            16,
            "$上月读数 + $本月读数;\n________________^ (11)",
            "docs/n_cst_dwsvc_columnexp.md:L49 over :L2403 - LenA of the 11-char prefix is 17, pad=16"
        },
    };

    /// <summary>
    /// The pad is sized from the byte length of the prefix, and the rendered string matches the
    /// oracle's byte for byte.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position.</param>
    /// <param name="expectedPad">How many pad characters the oracle's arithmetic produces.</param>
    /// <param name="expected">The rendering derived by hand from the oracle.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    [Theory]
    [MemberData(nameof(ByteWidthMatrix))]
    public void Pad_IsSizedFromTheByteLengthOfThePrefix(
        string syntax,
        long pos,
        int expectedPad,
        string expected,
        string locator)
    {
        string actual = ParseErrorFormatter.BuildParseError(syntax, pos);

        Assert.Equal(expected, actual);

        string caretLine = actual[(actual.LastIndexOf('\n') + 1)..];
        int caret = caretLine.IndexOf(ParseErrorFormatter.CaretCharacter);

        Assert.True(
            caret == expectedPad,
            locator + " expected " + expectedPad.ToString(CultureInfo.InvariantCulture)
                + " pad character(s) but rendered " + caret.ToString(CultureInfo.InvariantCulture)
                + ": " + ParseErrorOracle.Escape(actual));

        // THE PAD COMES FROM THE FORMATTER'S OWN PUBLISHED LenA, not from an ad-hoc encoding call.
        // `LenA` is public precisely so this can be asserted rather than assumed: if the
        // implementation ever sized the pad from something else, this line separates and fails.
        string prefix = ParseErrorOracle.Left(syntax, pos);
        Assert.Equal(ParseErrorFormatter.LenA(prefix) - 1, caret);

        // And the independent restatement of :L2403 agrees on the whole rendering.
        Assert.Equal(ParseErrorOracle.Render(syntax, pos), actual);
    }

    /// <summary>
    /// THE EXPLICIT NEGATIVE ASSERTION: what the two tempting substitutes would have produced, spelled
    /// out so that a regression names its own cause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caret in the wrong column is a silent parity break. It changes no type, raises no diagnostic
    /// and still renders a message that looks entirely reasonable - it only fails against a recording,
    /// and only if a recording covers a non-ASCII expression. So the two wrong answers are computed
    /// here alongside the right one and asserted to be different.
    /// </para>
    /// <para>
    /// <c>LenA</c> [<c>:L2403</c>] is the DBCS byte length. The character count is
    /// <c>Len</c>, which the oracle does NOT use at this line. UTF-8 is not the legacy host's ANSI
    /// codepage at all - it encodes a Han character in three bytes rather than two, so it over-indents
    /// by one column per Chinese character, which reads exactly like an off-by-one and is not one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Pad_IsNeitherACharacterCountNorAUtf8ByteCount_ExplicitNegativeAssertion()
    {
        const string syntax = "$上月读数+1;";
        const long pos = 3L;
        const string prefix = "$上月";

        string actual = ParseErrorFormatter.BuildParseError(syntax, pos);

        // The right answer: LenA("$上月") = 1 + 2 + 2 = 5, so the pad is 4.
        Assert.Equal("$上月读数+1;\n____^ (3)", actual);
        Assert.Equal(5L, ParseErrorFormatter.LenA(prefix));

        // WRONG ANSWER 1 - a UTF-16 character count. Len("$上月") = 3, so the pad would be 2 and the
        // caret would sit two columns to the left of where the legacy draws it.
        string naiveCharacterCount =
            syntax + "\n" + new string('_', prefix.Length - 1) + "^ (3)";

        Assert.Equal("$上月读数+1;\n__^ (3)", naiveCharacterCount);
        Assert.NotEqual(naiveCharacterCount, actual);
        Assert.Equal(2, prefix.Length - 1);

        // WRONG ANSWER 2 - Encoding.UTF8.GetByteCount. Three bytes per Han character gives 7, so the
        // pad would be 6 and the caret would sit two columns to the RIGHT.
        int utf8Pad = Encoding.UTF8.GetByteCount(prefix) - 1;
        string naiveUtf8Count = syntax + "\n" + new string('_', utf8Pad) + "^ (3)";

        Assert.Equal(6, utf8Pad);
        Assert.Equal("$上月读数+1;\n______^ (3)", naiveUtf8Count);
        Assert.NotEqual(naiveUtf8Count, actual);

        // All three differ from one another, so no pair of them could be confused for the other.
        Assert.NotEqual(naiveCharacterCount, naiveUtf8Count);
    }

    /// <summary>
    /// Every row of the byte-width matrix, restated as the three-way comparison: the byte count is the
    /// answer, and both substitutes differ from it wherever the prefix is not pure ASCII.
    /// </summary>
    /// <param name="syntax">The trailer-extended expression.</param>
    /// <param name="pos">The one-based caret position.</param>
    /// <param name="expectedPad">The oracle's pad count.</param>
    /// <param name="expected">The oracle's rendering, unused by this assertion beyond its presence.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    /// <remarks>
    /// The ASCII rows are included deliberately even though all three measures agree on them: an
    /// assertion that the three agree on ASCII and diverge on Han is a stronger statement than one
    /// that only ever looks at Han.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ByteWidthMatrix))]
    public void Pad_AgreesWithACharacterCountOnlyWhereThePrefixIsPureAscii(
        string syntax,
        long pos,
        int expectedPad,
        string expected,
        string locator)
    {
        string prefix = ParseErrorOracle.Left(syntax, pos);

        bool pureAscii = true;
        foreach (char current in prefix)
        {
            if (current >= 0x0080)
            {
                pureAscii = false;
                break;
            }
        }

        int bytePad = (int)ParseErrorFormatter.LenA(prefix) - 1;
        int characterPad = prefix.Length - 1;
        int utf8Pad = Encoding.UTF8.GetByteCount(prefix) - 1;

        Assert.Equal(expectedPad, bytePad);
        Assert.Equal(expected, ParseErrorFormatter.BuildParseError(syntax, pos));

        if (pureAscii)
        {
            Assert.Equal(bytePad, characterPad);
            Assert.Equal(bytePad, utf8Pad);
        }
        else
        {
            Assert.True(
                bytePad > characterPad && bytePad < utf8Pad,
                locator + " expected the DBCS byte pad to sit strictly between the character pad and "
                    + "the UTF-8 pad, but got byte=" + bytePad.ToString(CultureInfo.InvariantCulture)
                    + " char=" + characterPad.ToString(CultureInfo.InvariantCulture)
                    + " utf8=" + utf8Pad.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// <see cref="ParseErrorFormatter.LenA"/> itself: one byte per ASCII code point, two per anything
    /// else.
    /// </summary>
    /// <remarks>
    /// The <c>"国A"</c> row is a cross-check rather than a duplicate. The DataWindow expression
    /// evaluator's own <c>LenA</c> function is pinned at the same value in
    /// <c>DataWindowExpressionEvaluatorTests</c>, so the two independent <c>LenA</c> ports in this
    /// service agree - which is what stops a rendered caret and an evaluated <c>LenA(...)</c>
    /// expression from disagreeing about the same string.
    /// </remarks>
    public static TheoryData<string, long, string> LenAMatrix() => new()
    {
        { "", 0L, "the empty string measures nothing" },
        { "a", 1L, "one ASCII code point is one byte" },
        { "abc", 3L, "pure ASCII measures its character count" },
        { "n1 + n2", 7L, "the ASCII example at :L2403, spaces and operator included" },
        { "\u007F", 1L, "U+007F is the LAST single-byte code point" },
        { "\u0080", 2L, "U+0080 is the FIRST double-byte code point - the boundary of the rule" },
        { "上", 2L, "one Han character is two bytes" },
        { "上月读数", 8L, "w_test_dwsvc_columnexp.srw:L107 - four Han characters, eight bytes" },
        { "精度", 4L, "w_test_dwsvc_columnexp.srw:L109" },
        { "$上月", 5L, "the mixed prefix behind the decisive row: 1 + 2 + 2" },
        { "$上月读数+1;", 12L, "the whole trailer-extended expression: 1+2+2+2+2+1+1+1" },
        { "国A", 3L, "cross-check against the evaluator's own LenA: 2 + 1" },
        { "\U0001F600", 2L, "a surrogate PAIR is ONE character and measures two, not four" },
        { "\u20AC", 2L, "the euro sign - a documented over-count the oracle's corpus cannot adjudicate" },
    };

    /// <summary>The published byte-length primitive answers the DBCS rule.</summary>
    /// <param name="value">The string to measure.</param>
    /// <param name="expected">The byte length the rule produces.</param>
    /// <param name="note">Why the row is here.</param>
    [Theory]
    [MemberData(nameof(LenAMatrix))]
    public void LenA_CountsOneByteForAsciiAndTwoForEverythingElse(
        string value,
        long expected,
        string note)
    {
        long actual = ParseErrorFormatter.LenA(value);

        Assert.Equal(expected, actual);

        Assert.True(
            actual == ParseErrorOracle.ByteLength(value),
            note + " - the independent restatement of the DBCS rule disagrees: "
                + ParseErrorOracle.ByteLength(value).ToString(CultureInfo.InvariantCulture)
                + " versus " + actual.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <see langword="null"/> measures zero and never throws, and an UNPAIRED surrogate measures two
    /// under the same non-ASCII rule.
    /// </summary>
    /// <remarks>
    /// Both are held out of the matrix rather than folded into it: a null cannot be a
    /// <c>TheoryData</c> row without weakening the row type, and a lone surrogate is malformed text
    /// that has no business appearing in a generated test-case name.
    /// </remarks>
    [Fact]
    public void LenA_MeasuresNullAsZeroAndAnUnpairedSurrogateAsTwo()
    {
        Assert.Equal(0L, ParseErrorFormatter.LenA(null));
        Assert.Equal(0L, ParseErrorFormatter.LenA(string.Empty));

        // A high surrogate with nothing after it, and a low surrogate with nothing before it. Neither
        // is special-cased: the non-ASCII rule covers both, which is a branch the oracle never asks
        // for and therefore a branch that is not added.
        string loneHigh = new('\uD83D', 1);
        string loneLow = new('\uDE00', 1);

        Assert.Equal(2L, ParseErrorFormatter.LenA(loneHigh));
        Assert.Equal(2L, ParseErrorFormatter.LenA(loneLow));

        // Reversed order is two separate lone surrogates, so four, not one pair.
        Assert.Equal(4L, ParseErrorFormatter.LenA(loneLow + loneHigh));

        // And the well-formed pair in the right order is one character, so two.
        Assert.Equal(2L, ParseErrorFormatter.LenA(loneHigh + loneLow));
    }
}


// ==================================================================================================
//  PHASE 3 - BOUNDARY POSITIONS
// ==================================================================================================

/// <summary>
/// Pins what the formatter does at and beyond the edges of the expression, where <c>Left</c> and
/// <c>Fill</c> [<c>n_cst_dwsvc_columnexp.sru:L2403</c>] behave unlike their nearest .NET equivalents.
/// </summary>
/// <remarks>
/// <para>
/// <b>THESE CASES ARE PINNED, NOT AVOIDED.</b> <c>Fill("_", LenA(Left(syntax,pos)) - 1)</c> receives
/// <c>-1</c> whenever <c>pos</c> is zero or less, because <c>Left</c> answers the empty string there
/// and <c>0 - 1</c> is <c>-1</c>. PowerBuilder's <c>Fill</c> answers the empty string for a
/// non-positive count; <c>new string('_', -1)</c> throws, and <c>value.Substring(0, count)</c> throws
/// for a negative count AND for a count past the end. Every one of those .NET exceptions would fire
/// while the engine is in the middle of REPORTING a fault, replacing a legacy diagnostic with a
/// managed stack trace.
/// </para>
/// <para>
/// Zero is not a hypothetical position either: it is what an uninitialised <c>nQuotePos</c> would
/// carry, and the reason the published contract's caret position is nullable rather than
/// zero-defaulted is precisely that ZERO IS A MEANINGFUL POSITION here.
/// </para>
/// </remarks>
public sealed class ParseErrorBoundaryPositionTests
{
    /// <summary>
    /// The boundary matrix: the first column, the zero and negative positions, the trailer sentinel,
    /// positions past the end, and the Han first character.
    /// </summary>
    public static TheoryData<string, long, string, string> BoundaryMatrix() => new()
    {
        // ---- pos = 1 over an ASCII first character: LenA("a") = 1, so 1 - 1 = ZERO pad characters
        //      and the caret sits in the very first column.
        {
            "a+$x;",
            1L,
            "a+$x;\n^ (1)",
            ":L2403 - LenA(Left(\"a+$x;\",1))=1, 1-1=0, so Fill produces nothing at all"
        },

        // ---- pos = 0: Left yields the empty string, LenA yields 0, and Fill is handed -1. It must
        //      answer the empty string rather than raise.
        {
            "a+$x;",
            0L,
            "a+$x;\n^ (0)",
            ":L2403 - Left(...,0)=\"\", LenA=0, Fill(-1) is the EMPTY string in PowerBuilder"
        },

        // ---- Negative positions clamp identically, and the position itself still renders with its
        //      sign because String(pos) is applied to the value as given.
        {
            "a+$x;",
            -1L,
            "a+$x;\n^ (-1)",
            ":L2403 - a negative position clamps at Left and renders its own sign"
        },
        {
            "a+$x;",
            -1234L,
            "a+$x;\n^ (-1234)",
            ":L2403 - a far negative position behaves no differently"
        },

        // ---- pos AT the trailer sentinel. The sentinel is a real character of `sExp` [:L1259], so a
        //      position pointing at it is in range and the whole expression is measured.
        {
            "a+$x;",
            5L,
            "a+$x;\n____^ (5)",
            ":L1259 - pos points AT the \";\" sentinel, LenA of the whole string is 5, pad=4"
        },

        // ---- pos PAST the end. Left clamps to the whole string, so the caret lands at the end and
        //      the rendered position still reports what the caller asked for.
        {
            "a+$x;",
            6L,
            "a+$x;\n____^ (6)",
            ":L2403 - one past the end, Left clamps, pad stays 4"
        },
        {
            "a+$x;",
            999L,
            "a+$x;\n____^ (999)",
            ":L2403 - far past the end, still clamped, still no exception"
        },

        // ---- pos = 1 where the FIRST CHARACTER IS HAN: the byte length is 2, so the pad is ONE. This
        //      row proves the arithmetic is byte-based even at the boundary where a character count
        //      would give zero.
        {
            "上月读数;",
            1L,
            "上月读数;\n_^ (1)",
            "w_test_dwsvc_columnexp.srw:L107 over :L2403 - LenA(\"上\")=2, pad=1, char count gives 0"
        },
        {
            "上",
            1L,
            "上\n_^ (1)",
            ":L2403 - a single Han character, nothing else: still one pad character"
        },
        {
            "上月读数;",
            5L,
            "上月读数;\n________^ (5)",
            ":L2403 - the whole Han expression including the sentinel: LenA=9, pad=8"
        },
        {
            "上月读数;",
            99L,
            "上月读数;\n________^ (99)",
            ":L2403 - past the end of a Han expression clamps to the same pad"
        },

        // ---- An EMPTY expression. Left answers empty for any position, so the pad is always empty and
        //      the rendering is a bare line feed followed by the caret line.
        {
            "",
            1L,
            "\n^ (1)",
            ":L2403 - an empty expression renders the separator and the caret line only"
        },
        {
            "",
            0L,
            "\n^ (0)",
            ":L2403 - empty expression, zero position: both degenerate cases at once"
        },
        {
            "$",
            1L,
            "$\n^ (1)",
            ":L1486 shape - a bare sigil, the shortest expression the scanner can fail on"
        },
    };

    /// <summary>Every boundary position renders, and none of them throws.</summary>
    /// <param name="syntax">The expression.</param>
    /// <param name="pos">The one-based caret position, possibly out of range.</param>
    /// <param name="expected">The rendering derived by hand from the oracle.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    [Theory]
    [MemberData(nameof(BoundaryMatrix))]
    public void BoundaryPosition_RendersTheOracleResultAndNeverThrows(
        string syntax,
        long pos,
        string expected,
        string locator)
    {
        string actual = ParseErrorFormatter.BuildParseError(syntax, pos);

        Assert.Equal(expected, actual);
        Assert.Equal(ParseErrorOracle.Render(syntax, pos), actual);

        // Exactly one line feed: the separator at :L2403. A boundary case must not gain or lose one.
        Assert.True(
            ParseErrorOracle.CountLineFeeds(actual) == 1,
            locator + " expected exactly one line feed but rendered: "
                + ParseErrorOracle.Escape(actual));
    }

    /// <summary>
    /// A non-positive position hands <c>Fill</c> a negative count, and that is pinned as producing the
    /// empty string rather than as producing an exception.
    /// </summary>
    /// <remarks>
    /// Stated as its own assertion because the distinction is behavioural, not incidental: throwing
    /// here would be a behaviour change dressed up as robustness, and it would fire on the reporting
    /// path, turning a legacy diagnostic into a managed fault.
    /// </remarks>
    [Fact]
    public void NonPositivePosition_ProducesNoPadAndRaisesNothing()
    {
        foreach (long pos in (long[])[0L, -1L, -2L, -100L])
        {
            Exception? raised = Record.Exception(() => ParseErrorFormatter.BuildParseError("a+$x;", pos));
            Assert.Null(raised);

            string rendered = ParseErrorFormatter.BuildParseError("a+$x;", pos);
            string caretLine = rendered[(rendered.LastIndexOf('\n') + 1)..];

            // Zero pad characters: the caret is the first character of its line.
            Assert.Equal(0, caretLine.IndexOf(ParseErrorFormatter.CaretCharacter));
            Assert.StartsWith("^ (", caretLine, StringComparison.Ordinal);
        }

        // And the byte arithmetic that produced it: LenA("") - 1 is -1, which Fill answers empty.
        Assert.Equal(0L, ParseErrorFormatter.LenA(ParseErrorOracle.Left("a+$x;", 0L)));
        Assert.Equal(string.Empty, ParseErrorOracle.Fill(ParseErrorFormatter.LenA(string.Empty) - 1));
    }

    /// <summary>
    /// The extreme positions clamp rather than wrap, which is what the <c>long</c> comparison inside
    /// the ported <c>Left</c> buys.
    /// </summary>
    /// <remarks>
    /// A narrowing conversion applied BEFORE the comparison would turn a large positive position into a
    /// negative one and a clamp into an exception - the classic shape of this defect. Both extremes are
    /// unreachable from the scanner, and both are pinned anyway, because an error reporter that faults
    /// on its own input is worse than one that reports an odd position.
    /// </remarks>
    [Fact]
    public void ExtremePositions_ClampInsteadOfWrapping()
    {
        Assert.Equal(
            "a+$x;\n____^ (9223372036854775807)",
            ParseErrorFormatter.BuildParseError("a+$x;", long.MaxValue));

        Assert.Equal(
            "a+$x;\n^ (-9223372036854775808)",
            ParseErrorFormatter.BuildParseError("a+$x;", long.MinValue));

        // The same two extremes over a Han expression, where the clamped pad is byte-sized.
        Assert.Equal(
            "上月读数;\n________^ (9223372036854775807)",
            ParseErrorFormatter.BuildParseError("上月读数;", long.MaxValue));

        Assert.Equal(
            "上月读数;\n^ (-9223372036854775808)",
            ParseErrorFormatter.BuildParseError("上月读数;", long.MinValue));
    }

    /// <summary>
    /// The first column, side by side: an ASCII first character draws NO pad and a Han first character
    /// draws exactly ONE.
    /// </summary>
    /// <remarks>
    /// The sharpest single statement of the byte-width rule at its boundary. A character-count port
    /// produces zero for BOTH, so this pair is the smallest input on which the two implementations can
    /// be told apart.
    /// </remarks>
    [Fact]
    public void FirstColumn_DrawsNoPadForAsciiAndExactlyOneForHan()
    {
        Assert.Equal("a+$x;\n^ (1)", ParseErrorFormatter.BuildParseError("a+$x;", 1L));
        Assert.Equal("上月读数;\n_^ (1)", ParseErrorFormatter.BuildParseError("上月读数;", 1L));

        Assert.Equal(1L, ParseErrorFormatter.LenA("a"));
        Assert.Equal(2L, ParseErrorFormatter.LenA("上"));

        // What a character count would have produced for both: zero pad, and therefore two renderings
        // that are indistinguishable in the one place they must differ.
        Assert.Equal(1, "a".Length);
        Assert.Equal(1, "上".Length);
    }
}


// ==================================================================================================
//  PHASE 4 - THE TWO CARET-POSITION CONVENTIONS
// ==================================================================================================

/// <summary>
/// Pins both formulas the legacy uses to arrive at a caret position: the truncated token midpoint that
/// ten of the eleven caret-bearing sites report, and the opening-quote position that the eleventh
/// reports.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE FORMATTER TAKES A POSITION AND DOES NOT COMPUTE ONE</b>, deliberately: the legacy computes it
/// at the CALL SITE from three parser locals - <c>nMacPos</c>, <c>nPos</c> and <c>nQuotePos</c>
/// [<c>n_cst_dwsvc_columnexp.sru:L1241</c>] - that only the scanner owns, and recomputing either
/// formula inside the renderer would require the renderer to hold parser state. So the conventions are
/// pinned at three levels here, none of which is a substitute for the others:
/// </para>
/// <list type="number">
/// <item>
/// AT THE CATALOGUE, that each of the eleven sites names the convention the oracle uses at its line.
/// </item>
/// <item>
/// AS ARITHMETIC, that <c>nMacPos + (nPos - nMacPos) / 2</c> TRUNCATES rather than rounds - restated
/// from the PowerScript, with the inputs traced out of the scanner.
/// </item>
/// <item>
/// END TO END THROUGH THE ENGINE, that a real malformed expression produces exactly the position the
/// traced formula predicts, and that the rendered marker is that position over the
/// TRAILER-EXTENDED expression.
/// </item>
/// </list>
/// </remarks>
public sealed class ParseErrorCaretPositionConventionTests
{
    /// <summary>
    /// Builds an initialised, enabled engine over the column-expression fixture.
    /// </summary>
    /// <returns>The engine, ready to bind an expression.</returns>
    /// <remarks>
    /// One variable is defined - <c>v</c> - because a DYNAMIC reference such as <c>$$v</c> is not
    /// resolved at bind time and a row that relies on the scan continuing past the reference needs the
    /// reference itself to be well formed. Every other row in this class fails on a name that is
    /// deliberately NOT defined.
    /// </remarks>
    private static ColumnExpressionEngine NewEngine()
    {
        ExpressionSession session = new(
            "parse-error-formatter-tests",
            lifetime: null,
            columnExpression: null,
            timeProvider: null,
            traceSink: null);

        ColumnExpressionEngine engine = new(
            options: null,
            evaluator: null,
            macroInvoker: null,
            session: session,
            syntaxMutator: null,
            traceSink: null);

        engine.OnInit(FakeDataWindowFixtures.CreateColumnExpressionFixture());
        Assert.Equal(RetCode.OK, engine.SetEnabled(true));
        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        return engine;
    }

    /// <summary>
    /// Which convention each of the eleven caret-bearing sites reports, and the oracle line each one
    /// occupies.
    /// </summary>
    /// <remarks>
    /// The split is ten to one and it is not a preference. A midpoint needs two positions to sit
    /// between; the unclosed-quote failure is detected AFTER the scan loop has run off the end
    /// [<c>:L1503</c>], so there is no closing delimiter to take a midpoint against and the only useful
    /// position is where the run that was never closed began.
    /// </remarks>
    public static TheoryData<ExpressionErrorSite, CaretPositionConvention, int> ConventionMatrix() => new()
    {
        {
            ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
            CaretPositionConvention.MacroTokenMidpoint,
            1308
        },
        {
            ExpressionErrorSite.FunctionMacroInvalidArgumentList,
            CaretPositionConvention.MacroTokenMidpoint,
            1383
        },
        {
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
            CaretPositionConvention.MacroTokenMidpoint,
            1390
        },
        {
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName,
            CaretPositionConvention.MacroTokenMidpoint,
            1395
        },
        {
            ExpressionErrorSite.FunctionMacroUndefined,
            CaretPositionConvention.MacroTokenMidpoint,
            1399
        },
        {
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            CaretPositionConvention.MacroTokenMidpoint,
            1417
        },
        {
            ExpressionErrorSite.VariableMacroUndefined,
            CaretPositionConvention.MacroTokenMidpoint,
            1422
        },
        {
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
            CaretPositionConvention.MacroTokenMidpoint,
            1426
        },
        {
            ExpressionErrorSite.VariableMacroValueInvalid,
            CaretPositionConvention.MacroTokenMidpoint,
            1431
        },
        {
            ExpressionErrorSite.VariableMacroInvalidName,
            CaretPositionConvention.MacroTokenMidpoint,
            1486
        },

        // THE ONE THAT IS NOT A MIDPOINT [:L1505 passes nQuotePos].
        {
            ExpressionErrorSite.VariableMacroQuoteNotClosed,
            CaretPositionConvention.OpeningQuote,
            1505
        },
    };

    /// <summary>Each caret-bearing site names the convention its oracle line uses.</summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="convention">The convention the oracle uses at that line.</param>
    /// <param name="legacyLine">The line the site occupies in the read-only oracle.</param>
    [Theory]
    [MemberData(nameof(ConventionMatrix))]
    public void EachCaretBearingSite_NamesItsOwnConvention(
        ExpressionErrorSite site,
        CaretPositionConvention convention,
        int legacyLine)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);

        Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, descriptor.Family);
        Assert.Equal(convention, descriptor.CaretConvention);
        Assert.Equal(ExpressionErrorCategory.Parse, descriptor.Category);

        // The site identifier's VALUE is the oracle line, which is what makes the locator
        // machine-readable rather than a comment that can drift from the code beside it.
        Assert.Equal(legacyLine, descriptor.LegacyLine);
        Assert.Equal(legacyLine, (int)site);
    }

    /// <summary>
    /// Ten sites use the midpoint, exactly one uses the opening quote, and every plain-message site
    /// names no convention at all.
    /// </summary>
    [Fact]
    public void ConventionCensus_IsTenMidpointsOneOpeningQuoteAndSeventeenWithout()
    {
        // Named explicitly rather than inferred, because the type is part of the claim: the catalogue is
        // an IMMUTABLE snapshot of a read-only source, so a census taken over it cannot be invalidated
        // by anything this test or any other does.
        ImmutableArray<ExpressionErrorSiteDescriptor> sites = ExpressionErrorCatalog.All;

        int midpoints = 0;
        int openingQuotes = 0;
        int unspecified = 0;

        foreach (ExpressionErrorSiteDescriptor descriptor in sites)
        {
            switch (descriptor.CaretConvention)
            {
                case CaretPositionConvention.MacroTokenMidpoint:
                    midpoints++;
                    Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, descriptor.Family);
                    break;

                case CaretPositionConvention.OpeningQuote:
                    openingQuotes++;
                    Assert.Equal(
                        ExpressionErrorSite.VariableMacroQuoteNotClosed,
                        descriptor.Site);
                    break;

                default:
                    unspecified++;
                    Assert.Equal(ExpressionErrorFamily.PlainMessage, descriptor.Family);
                    break;
            }
        }

        Assert.Equal(10, midpoints);
        Assert.Equal(1, openingQuotes);
        Assert.Equal(17, unspecified);
        Assert.Equal(28, sites.Length);
    }

    /// <summary>
    /// The midpoint arithmetic, with <c>nMacPos</c> and <c>nPos</c> traced out of the scanner and the
    /// expected result computed by hand.
    /// </summary>
    /// <remarks>
    /// Odd spans are the point of the matrix: they are the only inputs on which truncation and rounding
    /// disagree, and therefore the only inputs that can catch a <c>double</c> port.
    /// </remarks>
    public static TheoryData<long, long, long, string> MidpointMatrix() => new()
    {
        // span 0 - the sigil and the terminator adjacent, no token at all between them.
        { 1L, 1L, 1L, ":L1486 shape - span 0, the midpoint is the start" },

        // ODD span 1 - `$+` : the sigil then straight to the terminator.
        { 1L, 2L, 1L, ":L1486 - span 1 is ODD, 1/2 truncates to 0, so the caret stays at 1" },

        // ODD span 3 - `$ab+`.
        { 1L, 4L, 2L, ":L1422 - span 3 is ODD, 3/2 truncates to 1" },

        // even span 4 - `$abc+`.
        { 1L, 5L, 3L, ":L1422 - span 4 is even, 4/2 is exactly 2" },

        // ODD span 5 - `$abcd+` : THE DECISIVE ROW. Truncation gives 3, any rounding gives 4.
        { 1L, 6L, 3L, ":L1422 - span 5 is ODD, 5/2 truncates to 2; a rounding port would answer 4" },

        // ODD span 7 with a non-one start, so the start is not confused for part of the division.
        { 3L, 10L, 6L, ":L1417 shape - span 7 is ODD from a start of 3, 7/2 truncates to 3" },

        // even span 8 - `$nosuch +`.
        { 1L, 9L, 5L, ":L1422 - span 8 is even, the observed position for \"$nosuch + 1\"" },

        // A long token, so the truncation is not an artefact of small numbers.
        { 1L, 32L, 16L, ":L1422 - span 31 is ODD, 31/2 truncates to 15" },
    };

    /// <summary>
    /// <c>nMacPos + (nPos - nMacPos) / 2</c> is integer division and TRUNCATES TOWARD ZERO; it never
    /// rounds.
    /// </summary>
    /// <param name="macroStart">The position the macro token started at.</param>
    /// <param name="terminator">The position the scanner stood at when the failure was detected.</param>
    /// <param name="expected">The truncated midpoint, computed by hand from the oracle.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    /// <remarks>
    /// The caret column is rendered into the message at <c>:L2403</c>, so it is in every
    /// characterization recording of a failed parse. A <c>double</c> port would move the caret one
    /// column on every odd-length token - a change that no type system objects to and that no dialog
    /// reader would ever notice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MidpointMatrix))]
    public void MacroTokenMidpoint_TruncatesTowardZeroAndNeverRounds(
        long macroStart,
        long terminator,
        long expected,
        string locator)
    {
        long actual = ParseErrorOracle.MacroTokenMidpoint(macroStart, terminator);

        Assert.Equal(expected, actual);

        long span = terminator - macroStart;
        double exact = macroStart + (span / 2.0);

        if (span % 2 == 0)
        {
            // An even span divides exactly, so truncation and rounding cannot be told apart here.
            Assert.Equal(expected, (long)exact);
        }
        else
        {
            // TRUNCATION, stated as the unambiguous property: the integer result is STRICTLY BELOW the
            // true midpoint. That single inequality excludes every rounding mode at once, which
            // comparing against one particular mode would not.
            Assert.True(
                actual < exact,
                locator + " expected the truncated midpoint to sit strictly below the exact midpoint "
                    + exact.ToString(CultureInfo.InvariantCulture) + " but got "
                    + actual.ToString(CultureInfo.InvariantCulture));

            Assert.Equal(expected, (long)Math.Floor(exact));

            // And it differs from the away-from-zero rounding a floating-point port would produce, by
            // exactly the one column that would invalidate a recording.
            long rounded = (long)Math.Round(exact, MidpointRounding.AwayFromZero);
            Assert.NotEqual(expected, rounded);
            Assert.Equal(expected + 1, rounded);
        }
    }

    /// <summary>
    /// The end-to-end rows: a malformed expression, the scanner locals traced out of the oracle, and
    /// the position the engine must report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>nMacPos</c> is 1 in every row because the sigil is the first character, and
    /// <c>nMacPos = nPos</c> is assigned there [<c>:L1287</c>]. <c>nPos</c> is the position of the
    /// terminator that ends the token - the operator arm at <c>:L1403</c> - counted in CHARACTERS,
    /// because <c>Mid</c> and <c>Len</c> count characters.
    /// </para>
    /// <para>
    /// The last row is the one that matters most: its expression is four Han characters long, so its
    /// position is a CHARACTER index of 3 while its pad is a BYTE width of 4. A port that used one
    /// measure for both would have to be wrong about one of them.
    /// </para>
    /// </remarks>
    public static TheoryData<string, ExpressionErrorSite, long, long, long, string>
        EngineReportedPositionMatrix() => new()
    {
        {
            "$nosuch + 1",
            ExpressionErrorSite.VariableMacroUndefined,
            1L,
            9L,
            5L,
            ":L1422 - the terminator is the \"+\" at character 9, span 8"
        },
        {
            "$abcd+1",
            ExpressionErrorSite.VariableMacroUndefined,
            1L,
            6L,
            3L,
            ":L1422 - the terminator is the \"+\" at character 6, ODD span 5, truncated to 3"
        },
        {
            "$abc+1",
            ExpressionErrorSite.VariableMacroUndefined,
            1L,
            5L,
            3L,
            ":L1422 - the terminator is at character 5, even span 4"
        },
        {
            "$ab+1",
            ExpressionErrorSite.VariableMacroUndefined,
            1L,
            4L,
            2L,
            ":L1422 - the terminator is at character 4, ODD span 3, truncated to 2"
        },
        {
            "$+ 1",
            ExpressionErrorSite.VariableMacroInvalidName,
            1L,
            2L,
            1L,
            ":L1486 - the sigil is followed immediately by the terminator, ODD span 1"
        },
        {
            "$上月读数+1",
            ExpressionErrorSite.VariableMacroUndefined,
            1L,
            6L,
            3L,
            "w_test_dwsvc_columnexp.srw:L107 over :L1422 - CHARACTER index 3, BYTE pad 4"
        },
    };

    /// <summary>
    /// A real malformed expression reports exactly the position the traced oracle formula predicts, and
    /// renders it over the trailer-extended expression.
    /// </summary>
    /// <param name="expression">The expression handed to the engine, WITHOUT the trailer.</param>
    /// <param name="site">The site the oracle raises for it.</param>
    /// <param name="macroStart">The <c>nMacPos</c> the scanner holds.</param>
    /// <param name="terminator">The <c>nPos</c> the scanner holds when it fails.</param>
    /// <param name="expectedPosition">The truncated midpoint of those two.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    [Theory]
    [MemberData(nameof(EngineReportedPositionMatrix))]
    public void EngineReportedPosition_IsTheTracedMidpointOverTheTrailerExtendedExpression(
        string expression,
        ExpressionErrorSite site,
        long macroStart,
        long terminator,
        long expectedPosition,
        string locator)
    {
        // The traced formula first, so the expectation is established from the oracle BEFORE the
        // subject is consulted.
        Assert.Equal(expectedPosition, ParseErrorOracle.MacroTokenMidpoint(macroStart, terminator));

        ColumnExpressionEngine engine = NewEngine();

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", expression));

        ExpressionParseError? error = engine.LastError;
        Assert.NotNull(error);

        Assert.Equal(site, error.Site);
        Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, error.Family);
        Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
        Assert.Equal(expectedPosition, error.CaretPosition);

        // THE POSITION INDEXES THE TRAILER-EXTENDED EXPRESSION [:L1252, :L1259], which is one character
        // longer than what the caller supplied. A consumer that rendered the caret against its own
        // expression would be off by the sentinel.
        string extended = expression + ParseErrorOracle.Trailer;
        Assert.Equal(extended, error.Expression);

        // And the marker is the two-argument rendering of exactly that pair [:L2406].
        Assert.Equal(ParseErrorOracle.Render(extended, expectedPosition), error.RenderedMarker);

        Assert.True(
            string.Equals(
                error.RenderedMarker,
                ParseErrorFormatter.BuildParseError(extended, expectedPosition),
                StringComparison.Ordinal),
            locator + " expected the marker "
                + ParseErrorOracle.Escape(ParseErrorFormatter.BuildParseError(extended, expectedPosition))
                + " but the engine reported " + ParseErrorOracle.Escape(error.RenderedMarker));
    }

    /// <summary>
    /// The unclosed quote reports <c>nQuotePos</c> - the position of the OPENING QUOTE - and not a
    /// token midpoint.
    /// </summary>
    /// <remarks>
    /// The two conventions genuinely disagree on this input, which is why the enumeration has two
    /// members rather than one formula. The <c>$$v</c> token here starts at character 1 and is
    /// terminated at character 5, whose truncated midpoint is 3; the quote opens at character 7. A port
    /// that applied the midpoint everywhere would place the caret four columns early and still render a
    /// perfectly plausible message.
    /// </remarks>
    [Fact]
    public void UnclosedQuote_ReportsTheOpeningQuoteAndNotTheTokenMidpoint()
    {
        ColumnExpressionEngine engine = NewEngine();

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "$$v + 'abcdefgh"));

        ExpressionParseError? error = engine.LastError;
        Assert.NotNull(error);

        Assert.Equal(ExpressionErrorSite.VariableMacroQuoteNotClosed, error.Site);
        Assert.Equal(CaretPositionConvention.OpeningQuote, error.CaretConvention);

        // nQuotePos: the apostrophe is character 7 of "$$v + 'abcdefgh;".
        Assert.Equal(7L, error.CaretPosition);
        Assert.Equal("$$v + 'abcdefgh;", error.Expression);
        Assert.Equal("$$v + 'abcdefgh;\n______^ (7)", error.RenderedMarker);

        // The midpoint the OTHER convention would have produced for the same expression's macro token,
        // stated so the divergence is explicit rather than implied.
        Assert.Equal(3L, ParseErrorOracle.MacroTokenMidpoint(1L, 5L));
        Assert.NotEqual(3L, error.CaretPosition);
    }
}


// ==================================================================================================
//  PHASE 5A - THE STRUCTURED RESULT THAT REPLACES THE DIALOG
// ==================================================================================================

/// <summary>
/// Pins AAP 0.2.1.3 Correction 5: a legacy <c>MessageBox</c> becomes a structured result and NOTHING
/// ELSE CHANGES. Everything the dialog put on screen is still carried, and carried APART rather than
/// fused into one string.
/// </summary>
/// <remarks>
/// A dialog delivered six things at once - a title, a message, an icon, and for a parse failure the
/// expression, a caret and a position. The migration is allowed to change the delivery channel and
/// nothing else, so each of the six is asserted individually here. The test that a caller "can
/// reconstruct everything the dialog used to display" is only meaningful if every one of the six is
/// checked; a single assertion on the rendered text would pass while losing the icon, the code and the
/// argument list.
/// </remarks>
public sealed class ParseErrorStructuredResultTests
{
    /// <summary>
    /// Builds the substitution arguments for a site, using ASCII tokens that cannot occur inside any of
    /// the 28 Chinese templates.
    /// </summary>
    /// <param name="count">How many arguments the site's template requires.</param>
    /// <returns>Exactly <paramref name="count"/> distinct tokens.</returns>
    /// <remarks>
    /// ASCII on purpose. An argument that could also appear in the template would make
    /// "the template was not pre-substituted" untestable, because a match would be ambiguous.
    /// </remarks>
    private static string?[] Arguments(int count)
    {
        string?[] arguments = new string?[count];

        for (int index = 0; index < count; index++)
        {
            arguments[index] = "arg-" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        return arguments;
    }

    /// <summary>
    /// Builds the structured error for a site through whichever factory its family requires.
    /// </summary>
    /// <param name="descriptor">The site's descriptor.</param>
    /// <returns>The structured error.</returns>
    /// <remarks>
    /// The family decides the factory, which is the whole point of the family existing: a caret-bearing
    /// site reported through the plain factory - or the reverse - is a caller mistake and is rejected
    /// rather than rendered.
    /// </remarks>
    private static ExpressionParseError Build(ExpressionErrorSiteDescriptor descriptor)
    {
        string?[] arguments = Arguments(descriptor.ArgumentCount);

        return descriptor.Family == ExpressionErrorFamily.CaretBearingParseError
            ? ParseErrorFormatter.CreateParseError(descriptor.Site, "$abcd+1;", 3L, arguments)
            : ParseErrorFormatter.CreatePlainError(descriptor.Site, arguments);
    }

    /// <summary>All 28 sites with their oracle line, for the invariants that hold across every one.</summary>
    public static TheoryData<ExpressionErrorSite, int> AllSites()
    {
        TheoryData<ExpressionErrorSite, int> data = [];

        foreach (ExpressionErrorSiteDescriptor descriptor in ExpressionErrorCatalog.All)
        {
            data.Add(descriptor.Site, descriptor.LegacyLine);
        }

        return data;
    }

    /// <summary>
    /// A caret-bearing error carries the expression, the position, the message, the severity and the
    /// category - so a caller can reconstruct the whole dialog.
    /// </summary>
    /// <remarks>
    /// The row chosen is the Han one deliberately: it is simultaneously the byte-width case, the
    /// undefined-variable arm at <c>:L1422</c>, and the shape the oracle's own scenario produces
    /// [<c>w_test_dwsvc_columnexp.srw:L107</c>].
    /// </remarks>
    [Fact]
    public void CaretBearingError_CarriesEverythingTheDialogShowed()
    {
        const string extended = "$上月读数+1;";

        ExpressionParseError error = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroUndefined,
            extended,
            3L,
            ParseErrorOracle.LastMonthReading);

        // 1 - THE TITLE. A separate MessageBox argument in the legacy, so a separate field here.
        Assert.Equal("错误", error.Title);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);

        // 2 - THE MESSAGE, exactly as the dialog rendered it: the text, a blank line, the expression and
        //     the caret [:L2402-L2404].
        Assert.Equal("解析变量宏失败!\n变量[上月读数],未定义\n\n$上月读数+1;\n____^ (3)", error.Text);

        // 3 - THE SEVERITY. `StopSign!` at :L1422, and at all 28 sites.
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);

        // 4 - THE CATEGORY, so a consumer can tell a parse failure from a cache failure.
        Assert.Equal(ExpressionErrorCategory.Parse, error.Category);
        Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, error.Family);

        // 5 - THE EXPRESSION AND THE POSITION, which travel together because the position indexes the
        //     expression and a consumer that separated them could not draw its own marker.
        Assert.Equal(extended, error.Expression);
        Assert.Equal(3L, error.CaretPosition);
        Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
        Assert.Equal("$上月读数+1;\n____^ (3)", error.RenderedMarker);

        // 6 - THE RETURN CODE the enclosing legacy function answered immediately afterwards, referenced
        //     by identifier rather than as the literal -3 (AAP 0.4.5.3).
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);

        // And the locator, machine-readable rather than a comment.
        Assert.Equal(1422, error.LegacyLine);
        Assert.Equal(1422, (int)error.Site);

        // The substitution arguments survive UNSUBSTITUTED beside the rendered text.
        Assert.Equal("解析变量宏失败!\n变量[{1}],未定义", error.FormatTemplate);
        Assert.Equal(ParseErrorOracle.LastMonthReading, Assert.Single(error.FormatArguments));

        // Nothing was caught here, so nothing is reported as caught.
        Assert.Null(error.CaughtExceptionText);
    }

    /// <summary>
    /// A plain-message error carries NO caret members, and they are absent rather than defaulted.
    /// </summary>
    /// <remarks>
    /// Absent rather than zero because ZERO IS A MEANINGFUL POSITION at the caret-bearing sites - it is
    /// the boundary that renders with no pad at all. A plain site that reported position zero would be
    /// indistinguishable from a caret site that legitimately reported it.
    /// </remarks>
    [Fact]
    public void PlainError_CarriesNoCaretMembersAndTheyAreAbsentRatherThanDefaulted()
    {
        ExpressionParseError error = ParseErrorFormatter.CreatePlainError(
            ExpressionErrorSite.DuplicateVariableDefinition,
            ParseErrorOracle.Precision);

        Assert.Equal("变量[精度],重复定义", error.Text);
        Assert.Equal(ExpressionErrorFamily.PlainMessage, error.Family);
        Assert.Equal(ExpressionErrorCategory.DuplicateDefinition, error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);

        // :L1607 answers FAILED, not E_INVALID_ARGUMENT: the argument is well formed and the STATE
        // rejects it. The distinction is preserved rather than smoothed over.
        Assert.Equal(RetCode.FAILED, error.ReturnCode);
        Assert.NotEqual(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);
        Assert.Equal(1607, error.LegacyLine);

        Assert.Null(error.Expression);
        Assert.Null(error.CaretPosition);
        Assert.Null(error.RenderedMarker);
        Assert.Equal(CaretPositionConvention.Unspecified, error.CaretConvention);
    }

    /// <summary>
    /// Every one of the 28 sites reports the stop sign, and every one carries the oracle's title.
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="legacyLine">The line the site occupies in the read-only oracle.</param>
    /// <remarks>
    /// Asserted 28 times rather than once in a comment. "All 28 use StopSign!" is a claim about a
    /// read-only source, and a claim about 28 lines should be checkable 28 times.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllSites))]
    public void EverySite_ReportsTheStopSignSeverityAndTheOracleTitle(
        ExpressionErrorSite site,
        int legacyLine)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);
        ExpressionParseError error = Build(descriptor);

        Assert.Equal(ExpressionErrorSeverity.StopSign, descriptor.Severity);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);

        Assert.True(
            error.LegacyLine == legacyLine,
            ExpressionErrorCatalog.OracleSourcePath + ":L"
                + legacyLine.ToString(CultureInfo.InvariantCulture)
                + " reported legacy line " + error.LegacyLine.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The severity and category values mirror the published contract's enumerations number for number,
    /// so the gRPC projection is a cast and no lookup table can rot.
    /// </summary>
    /// <remarks>
    /// The expected numbers are the protocol definition's own literals -
    /// <c>shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:L527-L533</c> for the severity and
    /// <c>:L3618-L3646</c> for the category. They are asserted as literals rather than against the
    /// generated types because the unit under test deliberately does NOT reference the generated
    /// contract classes: its folder's error foundation depends on the shared kernel and the base class
    /// library only, and that is what keeps it buildable without the protocol toolchain.
    /// </remarks>
    [Fact]
    public void SeverityAndCategoryValues_MirrorThePublishedContractNumberForNumber()
    {
        // dataservices.v1.Severity [dataservices.v1.proto:L527-L533]
        Assert.Equal(0, (int)ExpressionErrorSeverity.Unspecified);
        Assert.Equal(1, (int)ExpressionErrorSeverity.None);
        Assert.Equal(2, (int)ExpressionErrorSeverity.Information);
        Assert.Equal(3, (int)ExpressionErrorSeverity.Question);
        Assert.Equal(4, (int)ExpressionErrorSeverity.Exclamation);
        Assert.Equal(5, (int)ExpressionErrorSeverity.StopSign);

        // dataservices.v1.ExpressionError.Category [dataservices.v1.proto:L3618-L3646]
        Assert.Equal(0, (int)ExpressionErrorCategory.Unspecified);
        Assert.Equal(1, (int)ExpressionErrorCategory.CacheCreation);
        Assert.Equal(2, (int)ExpressionErrorCategory.CacheError);
        Assert.Equal(3, (int)ExpressionErrorCategory.CacheUpdate);
        Assert.Equal(4, (int)ExpressionErrorCategory.Expression);
        Assert.Equal(5, (int)ExpressionErrorCategory.Parse);
        Assert.Equal(6, (int)ExpressionErrorCategory.Preprocess);
        Assert.Equal(7, (int)ExpressionErrorCategory.UndefinedName);
        Assert.Equal(8, (int)ExpressionErrorCategory.InvalidValue);
        Assert.Equal(9, (int)ExpressionErrorCategory.MacroUnsupported);
        Assert.Equal(10, (int)ExpressionErrorCategory.DuplicateDefinition);
        Assert.Equal(11, (int)ExpressionErrorCategory.ForeignReferenceBlocked);
    }

    /// <summary>
    /// The rendered text and the rendered marker cannot disagree, because the two-argument arity
    /// delegates to the three-argument one [<c>:L2406</c>].
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="legacyLine">The line the site occupies in the read-only oracle.</param>
    /// <remarks>
    /// The published contract carries both renderings - the marker for byte-exact characterization
    /// comparison and the text as the dialog showed it - so a port in which they described different
    /// expressions, pads or positions would produce two mutually contradictory records of one failure.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllSites))]
    public void TextAndRenderedMarker_CannotDisagree(ExpressionErrorSite site, int legacyLine)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);
        ExpressionParseError error = Build(descriptor);

        if (descriptor.Family == ExpressionErrorFamily.PlainMessage)
        {
            Assert.Null(error.RenderedMarker);
            return;
        }

        string? marker = error.RenderedMarker;
        Assert.NotNull(marker);

        // The text is the message, two line feeds, then exactly the marker.
        Assert.EndsWith(marker, error.Text, StringComparison.Ordinal);

        string message = error.Text[..^marker.Length];

        Assert.True(
            message.EndsWith("\n\n", StringComparison.Ordinal),
            ExpressionErrorCatalog.OracleSourcePath + ":L"
                + legacyLine.ToString(CultureInfo.InvariantCulture)
                + " expected two line feeds between the message and the marker, but the text was "
                + ParseErrorOracle.Escape(error.Text));

        Assert.Equal("$abcd+1;", error.Expression);
        Assert.Equal(3L, error.CaretPosition);
        Assert.Equal("$abcd+1;\n__^ (3)", error.RenderedMarker);
    }

    /// <summary>
    /// The caught runtime exception's text is derived at the ONE site that captures one, and is null
    /// everywhere else.
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="legacyLine">The line the site occupies in the read-only oracle.</param>
    /// <remarks>
    /// <c>:L739</c> embeds <c>ex.text</c> as its third substitution argument, and the field is DERIVED
    /// from the argument list at the index the descriptor records rather than stored a second time -
    /// which is what makes it incapable of disagreeing with the rendered message.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllSites))]
    public void CaughtExceptionText_IsDerivedAtTheOneSiteThatCapturesOne(
        ExpressionErrorSite site,
        int legacyLine)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);
        ExpressionParseError error = Build(descriptor);

        if (site == ExpressionErrorSite.ExpressionCacheError)
        {
            Assert.Equal(739, legacyLine);
            Assert.Equal(3, descriptor.CaughtExceptionArgumentIndex);

            // One-based index 3 over the three arguments this site takes.
            Assert.Equal("arg-3", error.CaughtExceptionText);
            Assert.Equal(error.FormatArguments[2], error.CaughtExceptionText);
        }
        else
        {
            Assert.Null(descriptor.CaughtExceptionArgumentIndex);
            Assert.Null(error.CaughtExceptionText);
        }
    }

    /// <summary>
    /// The substitution arguments survive UNSUBSTITUTED alongside the rendered text, so a consumer can
    /// re-render the message - in another format, or with a value redacted - without parsing the
    /// rendered text back apart.
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="legacyLine">The line the site occupies in the read-only oracle.</param>
    [Theory]
    [MemberData(nameof(AllSites))]
    public void SprintfArguments_SurviveUnsubstitutedBesideTheRenderedText(
        ExpressionErrorSite site,
        int legacyLine)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);
        ExpressionParseError error = Build(descriptor);

        Assert.Equal(descriptor.ArgumentCount, error.FormatArguments.Length);

        // THE TEMPLATE IS NOT PRE-SUBSTITUTED. Every one-based placeholder the site declares is still
        // present, and no argument value has leaked into it.
        Assert.Equal(descriptor.FormatTemplate, error.FormatTemplate);

        for (int index = 1; index <= descriptor.ArgumentCount; index++)
        {
            string placeholder = "{" + index.ToString(CultureInfo.InvariantCulture) + "}";
            string argument = "arg-" + index.ToString(CultureInfo.InvariantCulture);

            Assert.Contains(placeholder, error.FormatTemplate, StringComparison.Ordinal);
            Assert.DoesNotContain(argument, error.FormatTemplate, StringComparison.Ordinal);

            Assert.Equal(argument, error.FormatArguments[index - 1]);

            // And the value DID reach the rendered text, so the two are complementary rather than
            // redundant: the template says what to render and the arguments say what with.
            Assert.True(
                error.Text.Contains(argument, StringComparison.Ordinal),
                ExpressionErrorCatalog.OracleSourcePath + ":L"
                    + legacyLine.ToString(CultureInfo.InvariantCulture) + " lost argument "
                    + argument + " from its rendered text: " + ParseErrorOracle.Escape(error.Text));
        }

        // A site with no arguments declares none and renders its template unchanged as its message.
        if (descriptor.ArgumentCount == 0)
        {
            Assert.Empty(error.FormatArguments);
            Assert.DoesNotContain("{1}", error.FormatTemplate, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Reporting a site through the wrong factory is rejected rather than rendered, because the caret
    /// and plain families are fixed by the read-only oracle.
    /// </summary>
    /// <remarks>
    /// One of the two programming-error conditions the unit under test admits. It is unreachable from
    /// data - each site's family is fixed at its line - so the 28-site matrices above turn the mistake
    /// into a build-time or test-time failure rather than a runtime one. Nothing data-shaped throws:
    /// a null string, an out-of-range position and an unrepresentable character all degrade instead.
    /// </remarks>
    [Fact]
    public void ReportingASiteThroughTheWrongFactory_IsRejectedRatherThanRendered()
    {
        // A caret-bearing site cannot be reported as a plain message.
        Assert.Throws<ArgumentException>(
            () => ParseErrorFormatter.CreatePlainError(
                ExpressionErrorSite.VariableMacroUndefined,
                "nosuch"));

        // Nor a plain-message site as a caret-bearing one.
        Assert.Throws<ArgumentException>(
            () => ParseErrorFormatter.CreateParseError(
                ExpressionErrorSite.DuplicateVariableDefinition,
                "$abcd+1;",
                3L,
                "nosuch"));

        // Nor may a site's fixed argument count be varied: the legacy builds each message by
        // concatenation, and a concatenation cannot have a variable arity.
        Assert.Throws<ArgumentException>(
            () => ParseErrorFormatter.CreateParseError(
                ExpressionErrorSite.VariableMacroUndefined,
                "$abcd+1;",
                3L,
                "nosuch",
                "extra"));

        // And a site that is not one of the 28 is not a site at all.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParseErrorFormatter.CreatePlainError(ExpressionErrorSite.Unspecified));
    }
}


// ==================================================================================================
//  PHASE 5B - THE PRESERVED NON-LOCALIZATION DEFECT
// ==================================================================================================

/// <summary>
/// Proves that none of the 28 column-expression messages routes through localization, and that the
/// inconsistency with the messages that DO localize elsewhere in the same service is reproduced rather
/// than harmonized.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE FACT BEING PINNED.</b> All 28 <c>MessageBox</c> calls in
/// <c>n_cst_dwsvc_columnexp.sru</c> carry hardcoded Chinese and NONE of them calls <c>I18N</c>. The
/// equivalent messages elsewhere in the same DataWindow service layer DO localize, through
/// <c>I18N(ne_cst_i18n.CAT_DWSVC, ...)</c> - the validation-error path at
/// <c>se_cst_dw.sru:L357</c> and <c>:L368</c>, and the row-select rejection at
/// <c>n_cst_dwsvc_rowselect.sru:L239</c>. Which file localizes and which does not is an accident of
/// where the code was written, not a pattern.
/// </para>
/// <para>
/// <b>WHY IT IS NOT REPAIRED.</b> Routing these 28 through the localization layer would change
/// observable output text, which is precisely the silent correction constraint C-B forbids. It would
/// also be the easiest possible mistake to make: the DataServices project genuinely DOES reference
/// <c>PowerFramework.Shared.Localization</c> - this very test file compiles against
/// <c>Categories</c> and <c>I18n</c> - so adding the import to the formatter would compile on the
/// first attempt and quietly repair a defect the plan requires be kept.
/// </para>
/// <para>
/// <b>HOW THE PROOF IS CONSTRUCTED.</b> A localization provider is taught a marker translation for
/// every one of the 28 templates AND for every rendered text, and installed on a live facade. Three
/// things are then asserted, in increasing strength: the emitted text is unchanged; the error reports
/// itself as unlocalized and carries no category; and THE PROVIDER WAS NEVER CONSULTED AT ALL. The
/// third is the one that matters - "translation returned the same text" and "no translation was
/// attempted" are different claims, and only the second is the legacy's behaviour. A final assertion
/// translates through the same facade to prove the double is live, so none of the preceding assertions
/// can be vacuous.
/// </para>
/// </remarks>
public sealed class ParseErrorNonLocalizationTests
{
    /// <summary>
    /// A translation that could not occur naturally, so a passing assertion cannot be mistaken for
    /// "the lookup happened and happened to return the same text".
    /// </summary>
    private const string Marker = "[TRANSLATED-BY-THE-TEST-DOUBLE]";

    /// <summary>Builds the substitution arguments for a site.</summary>
    /// <param name="count">How many the site's template requires.</param>
    /// <returns>Exactly <paramref name="count"/> Han tokens.</returns>
    /// <remarks>
    /// Han tokens here, unlike the ASCII ones the payload tests use, because this class is about
    /// TRANSLATION: an argument that is already Chinese is the realistic input, and it is what makes
    /// "the rendered text is entirely Chinese and entirely untranslated" a meaningful assertion.
    /// </remarks>
    private static string?[] Arguments(int count)
    {
        string?[] arguments = new string?[count];

        for (int index = 0; index < count; index++)
        {
            arguments[index] = ParseErrorOracle.ThisMonthReading;
        }

        return arguments;
    }

    /// <summary>Builds the structured error for a site through whichever factory its family requires.</summary>
    /// <param name="descriptor">The site's descriptor.</param>
    /// <returns>The structured error.</returns>
    private static ExpressionParseError Build(ExpressionErrorSiteDescriptor descriptor)
    {
        string?[] arguments = Arguments(descriptor.ArgumentCount);

        return descriptor.Family == ExpressionErrorFamily.CaretBearingParseError
            ? ParseErrorFormatter.CreateParseError(descriptor.Site, "$本月读数+1;", 3L, arguments)
            : ParseErrorFormatter.CreatePlainError(descriptor.Site, arguments);
    }

    /// <summary>
    /// With a provider installed that would translate every template, every title and every rendered
    /// text, not one character changes and the provider is never asked.
    /// </summary>
    [Fact]
    public void ColumnExpressionMessages_DoNotRouteThroughLocalization_IsPreservedDefect()
    {
        ScriptedI18nProvider provider = new();
        I18n facade = new();

        // The facade is genuinely live: installation answers OK [i18n.srf:L14].
        Assert.Equal(RetCode.OK, facade.I18N(provider));

        // Teach the double every key the error path could conceivably look up: the 28 templates, the
        // dialog title, and - built first, then taught - every fully rendered text.
        foreach (ExpressionErrorSiteDescriptor descriptor in ExpressionErrorCatalog.All)
        {
            _ = provider.Teach(descriptor.FormatTemplate, Marker);
            _ = provider.Teach(Build(descriptor).Text, Marker);
        }

        _ = provider.Teach(ExpressionErrorCatalog.LegacyTitle, Marker);
        _ = provider.Teach(ParseErrorOracle.ThisMonthReading, Marker);

        // The provider has been consulted zero times so far - teaching does not consult it - so the
        // count below is entirely attributable to the error path.
        Assert.Empty(provider.Requests);

        foreach (ExpressionErrorSiteDescriptor descriptor in ExpressionErrorCatalog.All)
        {
            ExpressionParseError error = Build(descriptor);

            // 1 - THE TEXT IS UNCHANGED. Not one of the 28 templates, and not one rendered form, came
            //     back as the marker.
            Assert.DoesNotContain(Marker, error.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, error.Title, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, error.FormatTemplate, StringComparison.Ordinal);
            Assert.Equal(descriptor.FormatTemplate, error.FormatTemplate);
            Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);

            // 2 - THE ERROR REPORTS ITSELF UNLOCALIZED AND CARRIES NO CATEGORY. The flag exists so a
            //     consumer can tell translatable text from untranslatable text WITHOUT any message
            //     changing.
            Assert.False(error.Localized);
            Assert.Equal(0L, error.LocalizationCategory);

            // And specifically NOT the category the genuinely localized call sites use
            // [se_cst_dw.sru:L357, n_cst_dwsvc_rowselect.sru:L239], nor its sibling
            // [ne_cst_i18n.sru:L16-L17].
            Assert.NotEqual(Categories.CAT_DWSVC, error.LocalizationCategory);
            Assert.NotEqual(Categories.CAT_MSGBOX, error.LocalizationCategory);
        }

        // 3 - THE STRONGEST FORM: the provider was never consulted, once, for anything. A port that
        //     localized and then found no entry would leave a request behind here even though the text
        //     came back unchanged.
        Assert.Empty(provider.Requests);

        // AND THE DOUBLE IS LIVE, so nothing above is vacuous. The same facade, the same category both
        // genuinely localized call sites pass, the same key - and here it DOES translate.
        string? translated = facade.I18N(
            Categories.CAT_DWSVC,
            ExpressionErrorCatalog.All[0].FormatTemplate);

        Assert.Equal(Marker, translated);

        I18nRequest request = Assert.Single(provider.Requests);
        Assert.Equal(Enums.I18N_SRC_PFW, request.Source);
        Assert.Equal(Categories.CAT_DWSVC, request.Category);
        Assert.Equal(ExpressionErrorCatalog.All[0].FormatTemplate, request.Text);
    }

    /// <summary>
    /// The emitted text is the oracle's hardcoded Chinese, not an English rendering of it.
    /// </summary>
    /// <remarks>
    /// The complement of the assertion above. "Not translated" and "still Chinese" are two different
    /// claims, and a port that had quietly replaced the literals with English would satisfy the first
    /// while failing the intent of constraint C-B entirely.
    /// </remarks>
    [Fact]
    public void EmittedText_IsTheOraclesHardcodedChineseAndNotAnEnglishRendering()
    {
        ExpressionParseError parse = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroUndefined,
            "$本月读数+1;",
            3L,
            ParseErrorOracle.ThisMonthReading);

        // [:L1422] - the leading phrase, the bracketed name and the trailing phrase, all Chinese.
        Assert.StartsWith("解析变量宏失败!", parse.Text, StringComparison.Ordinal);
        Assert.Contains("变量[本月读数],未定义", parse.Text, StringComparison.Ordinal);

        // The title too [:L1422's first MessageBox argument].
        Assert.Equal("错误", parse.Title);

        // And a plain site, so the claim is not limited to the scanner's messages [:L1607].
        ExpressionParseError plain = ParseErrorFormatter.CreatePlainError(
            ExpressionErrorSite.DuplicateVariableDefinition,
            ParseErrorOracle.Precision);

        Assert.Equal("变量[精度],重复定义", plain.Text);
    }
}

// ==================================================================================================
//  PHASE 5C - THE LEGACY MESSAGE TEXT, TRANSCRIBED RATHER THAN TRANSLATED
// ==================================================================================================

/// <summary>
/// Pins the message templates of the sites named in this file's brief, transcribed from
/// <c>n_cst_dwsvc_columnexp.sru</c> code point by code point.
/// </summary>
/// <remarks>
/// <para>
/// The templates carry the legacy text with each runtime value replaced by a ONE-BASED
/// <c>Formatting.Sprintf</c> placeholder and each <c>~n</c> escape replaced by the line feed it
/// denotes. Nothing else about them is changed: not the bracketed-name interpolation style, not the
/// ASCII punctuation, and certainly not the wording.
/// </para>
/// <para>
/// The interpolation style is observable output, which is why it is preserved down to the punctuation.
/// The legacy writes <c>"变量[" + name + "],未定义"</c> - an ASCII open bracket, the name, an ASCII
/// close bracket, an ASCII comma - and every one of those characters lands in a characterization
/// recording. The placeholder syntax is the reconstruction of the native <c>Sprintf</c>
/// [<c>sprintf.srf:L7-L27</c>], whose 21 prototypes are all that survives of it.
/// </para>
/// <para>
/// One wording quirk is transcribed rather than corrected: <c>:L1417</c> says
/// <c>"解析函数宏失败!"</c> - "parsing the FUNCTION macro failed" - although the construct that failed
/// is a VARIABLE. That is the oracle's own text and it is reproduced (constraint C-B).
/// </para>
/// </remarks>
public sealed class ParseErrorLegacyMessageTextTests
{
    /// <summary>
    /// The message templates: all eleven caret-bearing sites, plus a representative sample of the
    /// plain-message ones.
    /// </summary>
    /// <remarks>
    /// Four pairs of the 28 sites have BYTE-IDENTICAL templates and are still distinct sites -
    /// [<c>:L1390</c>] and [<c>:L1395</c>], [<c>:L1607</c>] and [<c>:L2122</c>], [<c>:L2192</c>] and
    /// [<c>:L2242</c>], and [<c>:L2282</c>] and [<c>:L2306</c>] - which is why the descriptor records
    /// each site's PowerScript argument source. The first of those pairs appears in full below.
    /// </remarks>
    public static TheoryData<ExpressionErrorSite, string, int, string> LegacyMessageMatrix() => new()
    {
        // ---- THE ELEVEN CARET-BEARING SITES, in oracle order.
        {
            ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
            "解析函数宏失败!\n不支持引用上下文的函数宏",
            0,
            ":L1308 - a function macro written with the context flag, refused outright"
        },
        {
            ExpressionErrorSite.FunctionMacroInvalidArgumentList,
            "解析函数宏失败!\n无效的参数列表",
            0,
            ":L1383 - an unterminated quote, unbalanced brackets, or a dangling argument"
        },
        {
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
            "解析函数宏失败!\n函数[{1}],参数数量不正确",
            1,
            ":L1390 - names the macro by fnData.fullName, which INCLUDES the sigils"
        },
        {
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName,
            "解析函数宏失败!\n函数[{1}],参数数量不正确",
            1,
            ":L1395 - byte-identical template to :L1390; names it by the bare fnData.name"
        },
        {
            ExpressionErrorSite.FunctionMacroUndefined,
            "解析函数宏失败!\n函数[{1}],未定义",
            1,
            ":L1399 - the case else arm; the engine defines exactly two built-in macros"
        },
        {
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            "解析函数宏失败!\n上下文变量[{1}],不支持静态展开",
            1,
            ":L1417 - says FUNCTION macro although a VARIABLE failed; transcribed, not corrected"
        },
        {
            ExpressionErrorSite.VariableMacroUndefined,
            "解析变量宏失败!\n变量[{1}],未定义",
            1,
            ":L1422 - _of_FindVarIndex answered zero"
        },
        {
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
            "解析变量宏失败!\n外部变量[{1}],不支持静态展开",
            1,
            ":L1426 - a foreign variable's value lives behind a live pointer, so it cannot bind"
        },
        {
            ExpressionErrorSite.VariableMacroValueInvalid,
            "解析变量宏失败!\n变量[{1}],值无效",
            1,
            ":L1431 - THE ONE parse arm answering FAILED rather than E_INVALID_ARGUMENT"
        },
        {
            ExpressionErrorSite.VariableMacroInvalidName,
            "解析变量宏失败!\n无效的名称",
            0,
            ":L1486 - a sigil with nothing after it"
        },
        {
            ExpressionErrorSite.VariableMacroQuoteNotClosed,
            "解析变量宏失败!\n引号未关闭",
            0,
            ":L1505 - raised after the scan loop ends; the one nQuotePos site"
        },

        // ---- A REPRESENTATIVE SAMPLE OF THE PLAIN-MESSAGE SITES, showing the other three template
        //      shapes the object uses: the bracketed-name prefix, the multi-line Expression report, and
        //      the preprocessor's three-line form.
        {
            ExpressionErrorSite.CreateExpressionCacheFailed,
            "[{1}]创建表达式缓存失败:\nExpression:{2}\nError:{3}",
            3,
            ":L713 - the lazy cache-creation path; the ASCII colon after the Chinese is the oracle's"
        },
        {
            ExpressionErrorSite.ExpressionCacheError,
            "[{1}]表达式缓存错误:\nExpression:{2}\nError:{3}",
            3,
            ":L739 - the one site embedding a caught exception's text, as its third argument"
        },
        {
            ExpressionErrorSite.ExpressionError,
            "[{1}]表达式错误:\nExpression:{2}",
            2,
            ":L762 - the evaluator answered one of its two error sentinels"
        },
        {
            ExpressionErrorSite.DuplicateVariableDefinition,
            "变量[{1}],重复定义",
            1,
            ":L1607 - the shortest message in the object"
        },
        {
            ExpressionErrorSite.ForeignVariableUndefined,
            "外部变量[{1}],未定义",
            1,
            ":L2133 - the one site answering E_VAR_NOT_FOUND"
        },
        {
            ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed,
            "预处理表达式发生错误!\n[{1}]表达式错误:\nExpression:{2}\nSub Expression:{3}",
            3,
            ":L2232 - note the ASCII SPACE inside the literal \"Sub Expression:\""
        },
    };

    /// <summary>Each site carries its oracle template byte for byte, with the arity the oracle fixes.</summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="expectedTemplate">The template transcribed from the oracle.</param>
    /// <param name="expectedArgumentCount">How many values the oracle concatenates into it.</param>
    /// <param name="locator">The oracle line the row transcribes.</param>
    [Theory]
    [MemberData(nameof(LegacyMessageMatrix))]
    public void EachSite_CarriesItsOracleTemplateByteForByte(
        ExpressionErrorSite site,
        string expectedTemplate,
        int expectedArgumentCount,
        string locator)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);

        Assert.Equal(expectedTemplate, descriptor.FormatTemplate);

        Assert.True(
            descriptor.ArgumentCount == expectedArgumentCount,
            locator + " expected "
                + expectedArgumentCount.ToString(CultureInfo.InvariantCulture)
                + " substitution argument(s) but the descriptor declares "
                + descriptor.ArgumentCount.ToString(CultureInfo.InvariantCulture));

        // The argument sources are recorded per site, one per placeholder, which is what distinguishes
        // the four pairs of sites whose templates are byte-identical.
        Assert.Equal(expectedArgumentCount, descriptor.LegacyArgumentSources.Length);

        // Every `~n` in the oracle is a LINE FEED here and never a carriage-return pair.
        Assert.DoesNotContain("\r", descriptor.FormatTemplate, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two argument-count templates are byte-identical and are distinguished ONLY by the
    /// PowerScript expression that supplies their argument.
    /// </summary>
    /// <remarks>
    /// <c>:L1390</c> names the macro by <c>fnData.fullName</c> - which includes the macro sigils - and
    /// <c>:L1395</c> by the bare <c>fnData.name</c>. The rendered messages differ, the templates do
    /// not, and collapsing the two sites would lose the locator that says which branch of the scanner
    /// reported.
    /// </remarks>
    [Fact]
    public void TheTwoArgumentCountTemplates_AreIdenticalAndDifferOnlyInTheirArgumentSource()
    {
        ExpressionErrorSiteDescriptor byFullName = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName);
        ExpressionErrorSiteDescriptor byName = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName);

        Assert.Equal(byFullName.FormatTemplate, byName.FormatTemplate);
        Assert.Equal(1390, byFullName.LegacyLine);
        Assert.Equal(1395, byName.LegacyLine);

        Assert.Equal("fnData.fullName", Assert.Single(byFullName.LegacyArgumentSources));
        Assert.Equal("fnData.name", Assert.Single(byName.LegacyArgumentSources));

        // The same holds for the duplicate-definition pair, whose two entry points differ while their
        // text does not [:L1607 for a local variable, :L2122 for a foreign one].
        ExpressionErrorSiteDescriptor local = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.DuplicateVariableDefinition);
        ExpressionErrorSiteDescriptor foreign = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.DuplicateForeignVariableDefinition);

        Assert.Equal(local.FormatTemplate, foreign.FormatTemplate);
        Assert.NotEqual(local.LegacyLine, foreign.LegacyLine);
    }

    /// <summary>
    /// Every punctuation mark in all 28 templates is ASCII, and no fullwidth form appears anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trap worth a test of its own. Chinese text is routinely written with fullwidth punctuation -
    /// U+FF01 for the exclamation mark, U+FF0C for the comma, U+FF1A for the colon, U+FF3B and U+FF3D
    /// for the brackets - and an editor or a well-meaning reviewer could substitute any of them without
    /// changing how the message LOOKS. Every one of the 28 messages uses the ASCII forms, verified code
    /// point by code point against the oracle, and each fullwidth character is three UTF-8 bytes where
    /// its ASCII counterpart is one.
    /// </para>
    /// <para>
    /// The <c>Sprintf</c> dialect's three special characters are checked at the same time: no template
    /// contains a backslash, and no template contains a brace beyond its own placeholders. That is what
    /// makes the templates escape-free, which was verified across all 28 rather than assumed.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryTemplatePunctuationMark_IsAsciiAndNeverFullwidth()
    {
        foreach (ExpressionErrorSiteDescriptor descriptor in ExpressionErrorCatalog.All)
        {
            string template = descriptor.FormatTemplate;
            string where = ExpressionErrorCatalog.OracleSourcePath + ":L"
                + descriptor.LegacyLine.ToString(CultureInfo.InvariantCulture);

            foreach (char fullwidth in (char[])
            [
                '\uFF01', // ！ fullwidth exclamation mark
                '\uFF0C', // ， fullwidth comma
                '\uFF1A', // ： fullwidth colon
                '\uFF3B', // ［ fullwidth left square bracket
                '\uFF3D', // ］ fullwidth right square bracket
                '\uFF08', // （ fullwidth left parenthesis
                '\uFF09', // ） fullwidth right parenthesis
            ])
            {
                Assert.False(
                    template.Contains(fullwidth),
                    where + " contains the fullwidth character U+"
                        + ((int)fullwidth).ToString("X4", CultureInfo.InvariantCulture)
                        + ", but the oracle uses the ASCII form: " + ParseErrorOracle.Escape(template));
            }

            // No escape is needed and none is present: the dialect treats only the backslash and the two
            // braces specially, and the braces that DO appear are this template's own placeholders.
            Assert.DoesNotContain("\\", template, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", template, StringComparison.Ordinal);

            string unexpected = "{"
                + (descriptor.ArgumentCount + 1).ToString(CultureInfo.InvariantCulture) + "}";
            Assert.DoesNotContain(unexpected, template, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The ASCII punctuation is asserted POSITIVELY as well, at the exact code points the oracle uses.
    /// </summary>
    /// <remarks>
    /// The negative sweep above proves no fullwidth form crept in; this proves the ASCII forms are
    /// actually there, which a template that had lost its punctuation altogether would fail.
    /// </remarks>
    [Fact]
    public void TheAsciiPunctuationIsPresentAtTheOraclesOwnCodePoints()
    {
        ExpressionErrorSiteDescriptor undefined = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.VariableMacroUndefined);

        const string expected = "解析变量宏失败!\n变量[{1}],未定义";
        Assert.Equal(expected, undefined.FormatTemplate);

        // The four ASCII marks in that one template, by code point.
        Assert.Equal('\u0021', expected[7]);   // '!'  after 解析变量宏失败
        Assert.Equal('\u000A', expected[8]);   // the ~n escape, a bare line feed
        Assert.Equal('\u005B', expected[11]);  // '['  opening the bracketed name
        Assert.Equal('\u005D', expected[15]);  // ']'  closing it
        Assert.Equal('\u002C', expected[16]);  // ','  before the trailing phrase

        // And the dialog title, which every one of the 28 sites passes as its first MessageBox argument.
        Assert.Equal("错误", ExpressionErrorCatalog.LegacyTitle);
        Assert.Equal('\u9519', ExpressionErrorCatalog.LegacyTitle[0]);
        Assert.Equal('\u8BEF', ExpressionErrorCatalog.LegacyTitle[1]);
        Assert.Equal(2, ExpressionErrorCatalog.LegacyTitle.Length);
        Assert.Equal(4L, ParseErrorFormatter.LenA(ExpressionErrorCatalog.LegacyTitle));
    }

    /// <summary>
    /// The catalogue names the oracle it transcribes, in full, so a recording or a log record can cite
    /// it without a reader having to know which of the 39 legacy libraries it lives in.
    /// </summary>
    /// <remarks>
    /// A full path rather than a file name, because there are two distinct files named
    /// <c>pfw.sra</c> in this repository and a bare name genuinely is ambiguous here.
    /// </remarks>
    [Fact]
    public void TheCatalogueNamesItsOracleByFullPath()
    {
        Assert.Equal(
            "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru",
            ExpressionErrorCatalog.OracleSourcePath);

        // Every site's identifier value is its line in that file, and the 28 are all distinct.
        HashSet<int> lines = [];

        foreach (ExpressionErrorSiteDescriptor descriptor in ExpressionErrorCatalog.All)
        {
            Assert.True(
                lines.Add(descriptor.LegacyLine),
                ExpressionErrorCatalog.OracleSourcePath + ":L"
                    + descriptor.LegacyLine.ToString(CultureInfo.InvariantCulture)
                    + " is claimed by more than one site, but two dialogs cannot share a line.");

            Assert.NotEqual(ExpressionErrorSite.Unspecified, descriptor.Site);
            Assert.InRange(descriptor.LegacyLine, 1, 2435);
        }

        Assert.Equal(28, lines.Count);
    }
}


// ==================================================================================================
//  PHASE 5D - VALUE EQUALITY, WITHOUT WHICH A CHARACTERIZATION COMPARISON CANNOT WORK
// ==================================================================================================

/// <summary>
/// Pins value equality on the two records the error reporter publishes, and on the one member of each
/// that a compiler-generated implementation would get wrong.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS NOT A TEST OF LANGUAGE FEATURES.</b> Both records carry an
/// <see cref="ImmutableArray{T}"/>, and <see cref="ImmutableArray{T}"/> implements equality as
/// REFERENCE equality of its underlying array - so a generated record <c>Equals</c> reports two
/// instances built from identical inputs as DIFFERENT. Both types therefore hand-write
/// <c>Equals</c> and <c>GetHashCode</c> to compare that member element by element.
/// </para>
/// <para>
/// It matters in exactly the place these types exist to serve. A characterization suite compares a
/// recorded error against a reproduced one; with reference equality it would fail for a reason that has
/// nothing to do with the behaviour under comparison, and the failure would look like a parity break.
/// A record also ADVERTISES value semantics, so one that quietly lacks them is a trap rather than a
/// nuance.
/// </para>
/// </remarks>
public sealed class ParseErrorValueEqualityTests
{
    /// <summary>Builds the same caret-bearing error every time, from a fresh argument array.</summary>
    /// <returns>An error for the undefined-variable arm at <c>:L1422</c>.</returns>
    /// <remarks>
    /// A FRESH array on every call, deliberately. Two calls therefore produce two errors whose
    /// <c>FormatArguments</c> wrap DIFFERENT underlying arrays with identical contents - which is
    /// precisely the pair that reference equality would separate.
    /// </remarks>
    private static ExpressionParseError NewCaretError()
    {
        return ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroUndefined,
            "$上月读数+1;",
            3L,
            ParseErrorOracle.LastMonthReading);
    }

    /// <summary>
    /// Two errors built from identical inputs are equal and hash alike, despite wrapping two distinct
    /// argument arrays.
    /// </summary>
    [Fact]
    public void TwoErrorsFromIdenticalInputs_AreEqualAndHashAlike()
    {
        ExpressionParseError first = NewCaretError();
        ExpressionParseError second = NewCaretError();

        // Distinct instances, so this is not a reference comparison in disguise.
        Assert.NotSame(first, second);

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        // The member that makes it interesting: identical contents, and the arrays behind them are not
        // required to be the same array.
        Assert.Equal(first.FormatArguments.Length, second.FormatArguments.Length);
        Assert.Equal(
            ParseErrorOracle.LastMonthReading,
            Assert.Single(second.FormatArguments));
    }

    /// <summary>
    /// Changing any single stored member breaks equality, and the two DERIVED members are deliberately
    /// excluded from it.
    /// </summary>
    /// <remarks>
    /// <c>LegacyLine</c> is derived from the site and <c>CaughtExceptionText</c> from the argument list,
    /// so comparing either would compare the same fact twice. The <c>with</c> expressions below also
    /// exercise the generated copy constructor, which is the only route by which an instance carrying a
    /// self-contradictory combination could be built at all.
    /// </remarks>
    [Fact]
    public void ChangingAnyStoredMember_BreaksEquality()
    {
        ExpressionParseError original = NewCaretError();

        Assert.NotEqual(original, original with { Text = "解析变量宏失败!\n变量[x],未定义" });
        Assert.NotEqual(original, original with { Title = "错" });
        Assert.NotEqual(original, original with { Severity = ExpressionErrorSeverity.Exclamation });
        Assert.NotEqual(original, original with { Category = ExpressionErrorCategory.Preprocess });
        Assert.NotEqual(original, original with { Localized = true });
        Assert.NotEqual(original, original with { LocalizationCategory = Categories.CAT_DWSVC });
        Assert.NotEqual(original, original with { ReturnCode = RetCode.FAILED });
        Assert.NotEqual(original, original with { ReturnCode = null });
        Assert.NotEqual(original, original with { Expression = "$本月读数+1;" });
        Assert.NotEqual(original, original with { CaretPosition = 4L });
        Assert.NotEqual(original, original with { CaretPosition = null });
        Assert.NotEqual(
            original,
            original with { CaretConvention = CaretPositionConvention.OpeningQuote });
        Assert.NotEqual(original, original with { RenderedMarker = null });
        Assert.NotEqual(original, original with { FormatTemplate = "变量[{1}],重复定义" });
        Assert.NotEqual(original, original with { FormatArguments = [ParseErrorOracle.Precision] });

        // The argument list is compared ELEMENT BY ELEMENT, so a different length differs too.
        Assert.NotEqual(original, original with { FormatArguments = [] });

        // A different site changes both the identity and the derived locator.
        ExpressionParseError elsewhere = original with
        {
            Site = ExpressionErrorSite.VariableMacroValueInvalid,
        };

        Assert.NotEqual(original, elsewhere);
        Assert.Equal(1422, original.LegacyLine);
        Assert.Equal(1431, elsewhere.LegacyLine);

        // And nothing is equal to null or to an unrelated object.
        Assert.False(original.Equals(null));
        Assert.NotNull(original.ToString());
    }

    /// <summary>
    /// The two sites whose templates are BYTE-IDENTICAL are still unequal descriptors, because their
    /// argument sources differ.
    /// </summary>
    /// <remarks>
    /// [<c>:L1390</c>] and [<c>:L1395</c>] render different messages from the same template because one
    /// names the macro by its full name and the other by its bare name. Descriptor equality has to see
    /// that, which is exactly what the element-by-element comparison of the argument-source list buys.
    /// </remarks>
    [Fact]
    public void DescriptorsWithByteIdenticalTemplates_AreStillUnequal()
    {
        ExpressionErrorSiteDescriptor byFullName = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName);
        ExpressionErrorSiteDescriptor byName = ExpressionErrorCatalog.Get(
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName);

        Assert.Equal(byFullName.FormatTemplate, byName.FormatTemplate);
        Assert.NotEqual(byFullName, byName);

        // A descriptor equals itself by value through a fresh copy, argument-source list included -
        // the same ImmutableArray trap, on the other record.
        ExpressionErrorSiteDescriptor copy = byFullName with { };

        Assert.NotSame(byFullName, copy);
        Assert.Equal(byFullName, copy);
        Assert.Equal(byFullName.GetHashCode(), copy.GetHashCode());

        // And the derived locator is excluded from equality because it is a restatement of the site.
        Assert.Equal((int)byFullName.Site, byFullName.LegacyLine);
        Assert.False(byFullName.Equals(null));
        Assert.NotNull(byFullName.ToString());

        // Changing any recorded member of a descriptor breaks equality, argument sources included.
        Assert.NotEqual(byFullName, byFullName with { ArgumentCount = 2 });
        Assert.NotEqual(byFullName, byFullName with { LegacyArgumentSources = ["fnData.name"] });
        Assert.NotEqual(byFullName, byFullName with { CaughtExceptionArgumentIndex = 1 });
        Assert.NotEqual(
            byFullName,
            byFullName with { CaretConvention = CaretPositionConvention.OpeningQuote });
    }

    /// <summary>
    /// Every one of the 28 catalogue entries is equal to a fresh copy of itself and unequal to every
    /// other entry.
    /// </summary>
    /// <remarks>
    /// A single sweep rather than 28 pairs, and it is the assertion that would have caught the
    /// reference-equality defect on any of them rather than only on the two that share a template.
    /// </remarks>
    [Fact]
    public void EveryCatalogueEntry_EqualsAFreshCopyAndNoOtherEntry()
    {
        ImmutableArray<ExpressionErrorSiteDescriptor> sites = ExpressionErrorCatalog.All;

        for (int outer = 0; outer < sites.Length; outer++)
        {
            ExpressionErrorSiteDescriptor descriptor = sites[outer];
            ExpressionErrorSiteDescriptor copy = descriptor with { };

            Assert.Equal(descriptor, copy);
            Assert.Equal(descriptor.GetHashCode(), copy.GetHashCode());

            for (int inner = 0; inner < sites.Length; inner++)
            {
                if (inner == outer)
                {
                    continue;
                }

                Assert.NotEqual(descriptor, sites[inner]);
            }
        }

        Assert.Equal(28, sites.Length);
    }
}

