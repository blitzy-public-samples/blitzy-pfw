// ==================================================================================================
//  LegacySystemErrorConsumer.cs - THE FAR SIDE OF THE ASSERT PAYLOAD BOUNDARY, REPLICATED FOR TESTS
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS        A test-only, faithful replica of the assert-payload CONSUMER.
//  WHAT IT IS NOT           A test suite. It declares no test attribute and xunit discovers nothing
//                           in it. It is also NOT production code and must never be copied into
//                           shared/PowerFramework.Shared.Diagnostics.
//  ORACLE                   ws_objects/pfw.pbl.src/pfw.sra
//                             :L45-L69   _of_splitstring, the field splitter
//                             :L111-L127 the systemerror decode
//                             :L129-L139 the report assembly
//                             :L141-L143 the dialog and HALT CLOSE
//  SUPPORTING ORACLES       ws_objects/pfw.common.pbl.src/assert.srf            the PRODUCER
//                           ws_objects/pfw.common.pbl.src/assertionfailed.sru   the carrier
//
//  WHY A REPLICA EXISTS AT ALL (C-A)
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Shared.Diagnostics produces the seven-field assert payload; it does not consume it.
//  The consumer in the shipping system is the Gateway composition root's
//  Diagnostics/SystemErrorHandler, which is the port of pfw.sra's systemerror event. Two suites in
//  THIS project need the decode side:
//
//      SystemErrorRoundTripTests   to prove the payload the library produces splits back into the
//                                  same field values, which is the cheapest possible guard against a
//                                  delimiter change or an off-by-one in the field layout
//      FailFastTests               to observe that a decoded structural failure ends in TERMINATION
//                                  rather than in a warning
//
//  C-A permits exactly one form of coupling across a service boundary, the published contracts
//  project, so a ProjectReference from this shared library's test project to a services/** project is
//  not available - and the project file says so in as many words. The decode is therefore replicated
//  HERE, deliberately, and the replica's only job is to be a faithful mirror of pfw.sra. It is not a
//  convenient parser: every tolerance a general-purpose parser would add is a tolerance that would
//  hide the very regression these suites exist to catch.
//
//  THE PROTOCOL, AND THE ASYMMETRY THAT MAKES IT WORK
//  ------------------------------------------------------------------------------------------------
//  Fields are joined by CRLF - the two characters "\r\n" and nothing else [assert.srf:L19]. Every
//  separator INSIDE a field is a BARE LINE FEED [assert.srf:L26,L63], and fields 2 and 7 really are
//  multi-line. That asymmetry is the entire reason splitting on CRLF is unambiguous. Splitting on a
//  bare line feed instead would shatter fields 2 and 7 into pieces and turn a two-field payload into
//  three or a seven-field payload into a dozen; Environment.NewLine would do the same on Linux, which
//  is what these containers run. Neither appears in this file, in either role.
//
//  Exactly two payload shapes exist: TWO fields, or SEVEN. Nothing else has any meaning to the
//  consumer [pfw.sra:L116,L119].
//
//  TWO PRESERVED DEFECTS (C-B) - REPRODUCED, NOT CORRECTED
//  ------------------------------------------------------------------------------------------------
//  D1  THE SPLITTER DROPS A TRAILING EMPTY SEGMENT. Only the FINAL segment is subject to a
//      non-empty test [pfw.sra:L64-L66], so a payload whose last field is empty splits into six
//      segments rather than seven and the consumer's exactly-seven branch never fires. Note the
//      precise scope: it is one segment, not all of them - a payload ending in two empty fields
//      loses one and keeps the other. See SplitString for the hand-traced cases.
//
//      Why the shipping payload is safe from D1 anyway: the producer's copy loop always writes at
//      least one frame [assert.srf:L61-L64], so field seven is non-empty BY CONSTRUCTION whenever the
//      deep shape is emitted at all. D1 is latent in the protocol, not live in it - which is exactly
//      why it must be preserved rather than "fixed": nothing observable depends on the fix, and the
//      fix would mask a producer that stopped writing frames.
//
//  D2  THE DECODE TESTS FOR EXACTLY SEVEN FIELDS, NOT SEVEN OR MORE [pfw.sra:L119]. A port that
//      always emitted seven fields would break the consumer in one direction, and a port that always
//      emitted two would break it in the other. Widening this to `>= 7` would make an eight-field
//      payload decode as though it were the deep shape, silently reading the wrong fields.
//
//  WHAT IS DELIBERATELY *NOT* REPLICATED
//  ------------------------------------------------------------------------------------------------
//  The dialog and the process kill. pfw.sra:L141 raises MessageBox(DialogTitle, report, StopSign!)
//  and pfw.sra:L143 then executes HALT CLOSE. This file performs NO output of any kind - no dialog,
//  no console write, no log line - and ends NO process. The report is handed to an INJECTED
//  ILegacyTerminationSink and that is the end of the chain. The reason is blunt: a replica that
//  really terminated would take the whole test host down with it, so the recording sink in this file
//  is what the suites inject and there is no production default anywhere in the abstraction that
//  calls Environment.Exit or Environment.FailFast.
//
//  The termination seam lives HERE, in the test double, and NOT in shared/PowerFramework.Shared
//  .Diagnostics. The production library's fail-fast contract is unchanged by the existence of this
//  file: Assertions.AssertFailed always throws and never returns. Making a production path
//  injectable so that a test can observe it would be weakening the production path to make it
//  testable, and that is not permitted.
//
//  TERMINATION IS UNCONDITIONAL, WHICH IS THE POINT
//  ------------------------------------------------------------------------------------------------
//  The legacy reaches HALT CLOSE for EVERY system error it handles, decoded or not: pfw.sra:L143 sits
//  outside every `if` above it. A payload that failed to decode - wrong object name, one field, six
//  fields - still terminates. Softening that into warn-and-continue would be a behavioural change
//  dressed up as robustness, and the fail-fast posture is called out in the AAP twice (0.1.4 and
//  0.6.7) precisely because it is the tempting thing to soften.
//
//  BINDING CONSTRAINTS AT THIS SITE
//  Naming: the repository root .editorconfig scopes its naming-analyzer suppressions to the named
//  PRODUCTION files on its BAND 3 roster - the single source of truth for that list - and none of
//  them is in this project, so every identifier here is conventional
//  PascalCase and no SCREAMING_SNAKE constant is declared.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// A faithful, test-only replica of the legacy assert-payload consumer at
/// <c>ws_objects/pfw.pbl.src/pfw.sra</c>: it splits a payload, decodes it, assembles the legacy
/// error report and then hands that report to an injected termination seam.
/// </summary>
/// <remarks>
/// <para>
/// The three ported steps are <see cref="SplitString(string, string, ref List{string})"/>
/// [pfw.sra:L45-L69], <see cref="Decode(LegacySystemErrorState)"/> [pfw.sra:L111-L127] and
/// <see cref="BuildReport(LegacyDecodedSystemError)"/> [pfw.sra:L129-L139]. All three are
/// <see langword="static"/> and stateless so that a suite may exercise the split and the decode on
/// their own; <see cref="HandleSystemError(LegacySystemErrorState)"/> is the one entry point that
/// runs the whole chain including the terminating step, and it needs an instance because the
/// termination seam is injected.
/// </para>
/// <para>
/// <b>Thread safety.</b> The three static members hold no state at all. An instance holds only its
/// injected sink, so concurrency safety is the sink's: <see cref="RecordingTerminationSink"/> is not
/// synchronized, which is deliberate - the suites drive one error at a time and a lock would hide an
/// accidental second invocation rather than reveal it.
/// </para>
/// </remarks>
internal sealed class LegacySystemErrorConsumer
{
    /// <summary>
    /// The error object name that gates the entire decode. [pfw.sra:L114]
    /// </summary>
    /// <remarks>
    /// The legacy compares with PowerScript <c>=</c>, which is case-sensitive for strings, so this is
    /// an ordinal comparison and <c>"Assert"</c> does <b>not</b> match. The value is the name of the
    /// legacy global function object that raises the payload [assert.srf:L3].
    /// </remarks>
    internal const string AssertObjectName = "assert";

    /// <summary>
    /// The FIELD delimiter: CRLF, and CRLF only. [pfw.sra:L115, assert.srf:L19]
    /// </summary>
    /// <remarks>
    /// Never <see cref="Environment.NewLine"/>, which is a bare line feed on Linux and would split
    /// nothing at all, and never a bare line feed, which is the separator used INSIDE fields 2 and 7
    /// and would shatter them.
    /// </remarks>
    internal const string FieldDelimiter = "\r\n";

    /// <summary>
    /// The title of the legacy error dialog, preserved verbatim from <c>pfw.sra:L141</c>.
    /// </summary>
    /// <remarks>
    /// Exposed as text so a suite can assert the string survived the migration (AAP 0.8.2). Nothing
    /// in this file displays it: the legacy call is
    /// <c>MessageBox("系统错误", sErrInfo, StopSign!)</c>, and only its report argument crosses into
    /// the termination seam. The <c>StopSign!</c> icon has no managed counterpart here and is
    /// recorded in this comment rather than modelled as data, because a headless service raises no
    /// dialog to put an icon on.
    /// </remarks>
    internal const string DialogTitle = "系统错误";

    /// <summary>
    /// The exit code handed to the termination seam in place of <c>HALT CLOSE</c>. [pfw.sra:L143]
    /// </summary>
    /// <remarks>
    /// <b>A modelling decision, not a legacy-observable value.</b> PowerScript's <c>HALT CLOSE</c>
    /// publishes no exit code, so there is nothing to preserve; a fixed non-zero code is used because
    /// abnormal termination is what the statement means, and it is a named constant purely so a suite
    /// can assert the same value the consumer passes.
    /// </remarks>
    internal const int HaltCloseExitCode = 1;

    /// <summary>
    /// The injected stand-in for <c>MessageBox</c> followed by <c>HALT CLOSE</c>. [pfw.sra:L141-L143]
    /// </summary>
    private readonly ILegacyTerminationSink _termination;

    /// <summary>
    /// Creates a consumer that reports its terminating step to <paramref name="termination"/>.
    /// </summary>
    /// <param name="termination">
    /// The termination seam. Pass a <see cref="RecordingTerminationSink"/> from a suite; there is no
    /// default and no parameterless constructor, precisely so that no code path can reach a real
    /// process termination by accident.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="termination"/> is <see langword="null"/>.
    /// </exception>
    internal LegacySystemErrorConsumer(ILegacyTerminationSink termination)
    {
        ArgumentNullException.ThrowIfNull(termination);

        _termination = termination;
    }

    // ----------------------------------------------------------------------------------------------
    // STEP 1 - THE SPLITTER [pfw.sra:L45-L69]
    // ----------------------------------------------------------------------------------------------
    // The legacy signature is
    //
    //      public function integer _of_splitstring(string src, readonly string delimiter,
    //                                             ref string dstarray[])
    //
    // so it returns the segment COUNT and produces the segments through a REF ARRAY. Both halves are
    // reproduced: the count is the return value and the segments arrive through a `ref` parameter.
    // The `ref` is not decoration. pfw.sra:L55 assigns a whole empty array over the caller's array,
    // which is a REPLACEMENT rather than a mutation, and the empty-delimiter path at :L53 returns
    // BEFORE that assignment - so the caller's array survives untouched in exactly one case, and only
    // a `ref` parameter can express that (AAP 0.4.5.2 maps a PowerScript `ref` parameter to a C#
    // `ref` parameter for this reason).
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Splits <paramref name="source"/> on <paramref name="delimiter"/>, reproducing
    /// <c>_of_splitstring</c> character for character, <b>including its trailing-empty defect</b>.
    /// [pfw.sra:L45-L69]
    /// </summary>
    /// <param name="source">The text to split. The payload, when called from the decode.</param>
    /// <param name="delimiter">
    /// The delimiter. <see cref="FieldDelimiter"/> when called from the decode. The empty string is a
    /// live input and short-circuits the whole method.
    /// </param>
    /// <param name="destination">
    /// Receives the ordered segments. <b>Replaced, not appended to</b>, except on the
    /// empty-delimiter path where it is left exactly as the caller passed it [pfw.sra:L53 returns
    /// before :L55 clears].
    /// </param>
    /// <returns>
    /// The segment count, which is PowerScript's <c>UpperBound</c> of the produced array
    /// [pfw.sra:L68]. Under one-based access the last valid index and the count are the same number,
    /// which is why no adjustment appears here - AAP 0.4.5.4 and risk R9 name one-based translation
    /// the single most dangerous mechanical hazard in this refactor, so the coincidence is stated
    /// rather than left for a reader to "correct".
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="delimiter"/> is <see langword="null"/>. Guarding
    /// a state PowerScript cannot present - its strings are never null on this path - so the guard
    /// can never mask a legacy behaviour; it exists so a null from a hand-written fixture fails
    /// loudly instead of surfacing as a NullReferenceException from inside the loop.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT D1 (C-B).</b> The loop at pfw.sra:L58-L63 appends EVERY segment that
    /// precedes a delimiter, empty ones included. The segment that FOLLOWS the last delimiter is
    /// appended only when it is non-empty [pfw.sra:L64-L66]. So a trailing empty field is silently
    /// dropped - and precisely one is, because only that final segment is tested. The behaviour is
    /// reproduced, not corrected; correcting it would change the field count the consumer sees, which
    /// is the observable contract.
    /// </para>
    /// <para>
    /// Hand-traced against pfw.sra:L48-L69, with CRLF as the delimiter:
    /// </para>
    /// <list type="bullet">
    /// <item><description>two non-empty fields, <c>"a\r\nb"</c> - 2 segments</description></item>
    /// <item><description>seven non-empty fields - 7 segments</description></item>
    /// <item><description>
    /// seven fields whose last is empty, <c>"1\r\n2\r\n3\r\n4\r\n5\r\n6\r\n"</c> - <b>6</b> segments,
    /// which is D1, and the exactly-seven branch of the decode then never fires
    /// </description></item>
    /// <item><description>
    /// an empty field in the MIDDLE, <c>"a\r\n\r\nc"</c> - 3 segments with the empty one present, so
    /// the drop really is confined to the tail
    /// </description></item>
    /// <item><description>
    /// TWO trailing empties, <c>"a\r\n\r\n"</c> - 2 segments, <c>["a", ""]</c>: one empty is dropped
    /// and one survives, because the loop appended the first before the test could reach it
    /// </description></item>
    /// <item><description>no delimiter present, <c>"abc"</c> - 1 segment</description></item>
    /// <item><description>an empty source with a valid delimiter - 0 segments</description></item>
    /// <item><description>
    /// an empty delimiter - 0 segments, returned immediately, with
    /// <paramref name="destination"/> untouched
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static int SplitString(string source, string delimiter, ref List<string> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(delimiter);

        // pfw.sra:L52 - `nLenDelimiter = Len(delimiter)`
        int delimiterLength = delimiter.Length;

        // pfw.sra:L53 - `if nLenDelimiter = 0 then return 0`
        //
        // BEFORE the clear below, which is why the caller's array is left alone on this path. The
        // order is load bearing and is not tidied.
        if (delimiterLength == 0)
        {
            return 0;
        }

        // pfw.sra:L55 - `dstArray = emptyArray`
        //
        // A whole-array ASSIGNMENT, so a fresh list rather than Clear() on the caller's one. The
        // difference is observable to a caller that kept its own reference, and the legacy's
        // behaviour is the replacement.
        destination = [];

        // pfw.sra:L48 - `string str` and the running `src`. The legacy reassigns its own `src`
        // parameter as it consumes the string; a C# parameter could be reassigned too, but a local
        // makes it obvious that the caller's string is not being mutated.
        string remainder = source;

        // pfw.sra:L57 - `nPos = Pos(src,delimiter)`
        int position = Pos(remainder, delimiter);

        // pfw.sra:L58-L63 - the consume loop.
        while (position > 0)
        {
            // pfw.sra:L59-L60 - `str = Left(src,nPos - 1)` then the append idiom
            // `dstArray[UpperBound(dstArray) + 1] = str`, which becomes an ordinary Add and therefore
            // carries no index arithmetic at all. An EMPTY str is appended here without any test,
            // which is what keeps a mid-payload empty field in the results.
            destination.Add(Left(remainder, position - 1));

            // pfw.sra:L61 - `src = Mid(src,nPos + nLenDelimiter)`
            remainder = Mid(remainder, position + delimiterLength);

            // pfw.sra:L62 - `nPos = Pos(src,delimiter)`
            position = Pos(remainder, delimiter);
        }

        // pfw.sra:L64-L66 - `if src <> "" then dstArray[UpperBound(dstArray) + 1] = src end if`
        //
        // PRESERVED DEFECT D1 (C-B). THIS TEST IS THE DEFECT. Appending unconditionally would be the
        // obvious "fix" and would change the segment count for a payload with an empty last field,
        // which is the one thing the consumer's exactly-seven test reads. Do not remove the test, and
        // do not widen it to also drop empties from the middle - only the final segment is tested.
        if (remainder != string.Empty)
        {
            destination.Add(remainder);
        }

        // pfw.sra:L68 - `return UpperBound(dstArray)`
        return UpperBound(destination);
    }

    /// <summary>
    /// Splits <paramref name="source"/> on <paramref name="delimiter"/> and returns the segments,
    /// for callers that want the segments rather than the count.
    /// </summary>
    /// <param name="source">The text to split.</param>
    /// <param name="delimiter">The delimiter. The empty string yields no segments.</param>
    /// <returns>
    /// The ordered segments. Empty when <paramref name="delimiter"/> is the empty string, and empty
    /// when <paramref name="source"/> is.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="delimiter"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// A thin convenience over
    /// <see cref="SplitString(string, string, ref List{string})"/> and <b>not</b> a second
    /// implementation: it delegates, so the two can never diverge and D1 applies to both. The count
    /// the ported form returns is this list's <see cref="List{T}.Count"/>, so nothing is lost by
    /// taking the segments instead.
    /// </remarks>
    internal static IReadOnlyList<string> SplitString(string source, string delimiter)
    {
        List<string> segments = [];
        SplitString(source, delimiter, ref segments);
        return segments;
    }

    // ----------------------------------------------------------------------------------------------
    // STEP 2 - THE DECODE [pfw.sra:L111-L127]
    // ----------------------------------------------------------------------------------------------
    // The legacy decode MUTATES the global Error object in place: it overwrites Error.Number and
    // Error.Text in the shallow branch and four more members in the deep one, and it puts field seven
    // into a local. A record in and a record out expresses the same transformation without a global,
    // and it is the shape the suites need anyway - the input carries the gate value, and the output
    // carries both post-decode field values and which branches fired.
    //
    // WHY THE INPUT IS A RECORD RATHER THAN JUST THE PAYLOAD TEXT. The gate at :L114 reads
    // Error.Object, and the fields the decode does NOT write are the ones the incoming error already
    // carried - so a payload that fails the gate, or one with only two fields, still has to report
    // the pre-existing WindowMenu, Object, ObjectEvent and Line. Taking only the text would make
    // those unrepresentable and the pass-through case untestable.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Decodes an incoming system error, reproducing the <c>systemerror</c> event's field extraction.
    /// [pfw.sra:L111-L127]
    /// </summary>
    /// <param name="error">The incoming error state, as the runtime would have populated it.</param>
    /// <returns>
    /// The post-decode field values, the segment count, and a flag for each of the two branches.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE GATE.</b> Everything is conditional on
    /// <see cref="LegacySystemErrorState.Object"/> being exactly <see cref="AssertObjectName"/>
    /// [pfw.sra:L114]. An error raised by anything else PASSES THROUGH UNDECODED: every field comes
    /// back as it arrived, the segment count is zero because the legacy never even calls the splitter,
    /// and both branch flags are <see langword="false"/>.
    /// </para>
    /// <para>
    /// <b>PRESERVED DEFECT D2 (C-B).</b> The deep branch tests for EXACTLY seven fields, not seven or
    /// more [pfw.sra:L119]. Five or six fields decode as the shallow shape and eight fields do too, so
    /// a producer that grew an eighth field would silently stop populating window, object, event, line
    /// and stack trace rather than fail. Widening this to <c>&gt;=</c> would read the wrong fields
    /// from an eight-field payload and would break the consumer in the opposite direction.
    /// </para>
    /// <para>
    /// <b>THE SPLIT SOURCE IS THE INCOMING TEXT.</b> The legacy splits <c>Error.Text</c> and then
    /// overwrites <c>Error.Text</c> from field 2 [pfw.sra:L115,L118]. Reading the incoming value first
    /// is therefore mandatory; deriving the split source from the already-overwritten field would
    /// decode field 2 as though it were the whole payload.
    /// </para>
    /// <para>
    /// <b>NUMERIC FIELDS NEVER FAIL.</b> Fields 1 and 6 go through PowerScript's <c>Long</c>
    /// [pfw.sra:L117,L123], which answers <c>0</c> for text that is not a number and never raises. The
    /// zero is a legacy answer, not a convenience fallback added here - see <see cref="Long(string)"/>.
    /// </para>
    /// </remarks>
    internal static LegacyDecodedSystemError Decode(LegacySystemErrorState error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // pfw.sra:L111-L112 - `long nCount` and `string sErrInfo,sStackTrace,sMessages[]`.
        // PowerScript numeric and string locals default to zero and the empty string, and both
        // defaults are load bearing: they are what a gated-out or shallow payload reports.
        int segmentCount = 0;
        string stackTrace = string.Empty;

        // The Error members the decode may overwrite, seeded with what arrived. Anything the decode
        // does not reach keeps the incoming value, which is precisely how the legacy behaves - it
        // assigns INTO a live object rather than building a new one.
        long number = error.Number;
        string text = error.Text;
        string windowMenu = error.WindowMenu;
        string errorObject = error.Object;
        string objectEvent = error.ObjectEvent;
        long line = error.Line;

        bool numberAndTextDecoded = false;
        bool sevenFieldBranchTaken = false;

        // pfw.sra:L114 - `if Error.Object = "assert" then`
        //
        // C# `==` on string is an ordinal, case-sensitive comparison, which is what PowerScript's `=`
        // does for strings. No culture-aware or case-insensitive comparison here: "Assert" must not
        // match, because the legacy would not match it either.
        if (error.Object == AssertObjectName)
        {
            // pfw.sra:L115 - `nCount = _of_SplitString(Error.Text,"~r~n",ref sMessages)`
            //
            // `~r~n` is CRLF. The INCOMING text is the split source; see the remark above.
            List<string> messages = [];
            segmentCount = SplitString(error.Text, FieldDelimiter, ref messages);

            // pfw.sra:L116 - `if nCount >= 2 then`
            if (segmentCount >= 2)
            {
                // pfw.sra:L117-L118 - `Error.Number = Long(sMessages[1])` / `Error.Text =
                // sMessages[2]`. Field 1 crosses the boundary as the TEXT "-10000"
                // [assert.srf:L22] and is parsed back to a number here.
                number = Long(FieldAt(messages, 1));
                text = FieldAt(messages, 2);
                numberAndTextDecoded = true;

                // pfw.sra:L119 - `if nCount = 7 then`. EXACTLY seven. Preserved defect D2.
                if (segmentCount == 7)
                {
                    // pfw.sra:L120-L124 - fields 3 to 7, in the legacy's own order.
                    windowMenu = FieldAt(messages, 3);
                    errorObject = FieldAt(messages, 4);
                    objectEvent = FieldAt(messages, 5);
                    line = Long(FieldAt(messages, 6));
                    stackTrace = FieldAt(messages, 7);
                    sevenFieldBranchTaken = true;
                }
            }
        }

        return new LegacyDecodedSystemError(
            number,
            text,
            windowMenu,
            errorObject,
            objectEvent,
            line,
            stackTrace,
            segmentCount,
            numberAndTextDecoded,
            sevenFieldBranchTaken);
    }

    // ----------------------------------------------------------------------------------------------
    // STEP 3 - THE REPORT [pfw.sra:L129-L139]
    // ----------------------------------------------------------------------------------------------
    // Seven lines, six of them unconditional, assembled with PowerScript `+` and `+=` and separated
    // by `~n`, which is a BARE LINE FEED - never CRLF, and never Environment.NewLine. The labels are
    // Chinese and are reproduced VERBATIM (AAP 0.8.2): they are legacy text, not messages to be
    // translated, reordered or reformatted, and they were taken from a byte dump of pfw.sra:L129-L138
    // rather than retyped. Note the deliberate irregularity: every label is "<label>: " with an ASCII
    // colon AND a trailing space, except the call-stack label, which is "调用栈:" with no space and a
    // line feed after it so the trace starts on its own line. That irregularity is legacy formatting
    // and is not harmonised.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Assembles the legacy error report from a decoded error, including its conditional window line.
    /// [pfw.sra:L129-L139]
    /// </summary>
    /// <param name="decoded">The decoded error, as produced by
    /// <see cref="Decode(LegacySystemErrorState)"/>.</param>
    /// <returns>
    /// The report text, line-feed separated. This is the exact string the legacy would have passed to
    /// <c>MessageBox</c> as its message argument [pfw.sra:L141].
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="decoded"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE WINDOW LINE IS SUPPRESSED WHEN WINDOW AND OBJECT ARE EQUAL</b> [pfw.sra:L131], and that
    /// suppression is COUPLED to the producer. When a captured frame carries exactly one dot, the
    /// producer assigns the object from the window menu - <c>ex.#Object = ex.#WindowMenu</c>
    /// [assert.srf:L49,L51] - so the two payload fields are identical and this line vanishes. When the
    /// frame carries two or more dots the producer splits them into different values
    /// [assert.srf:L44,L46] and the line appears. A port that stopped emitting equal values, or one
    /// that dropped this test, would change the report for every one-dot frame.
    /// </para>
    /// <para>
    /// <b>THE CALL-STACK LINE IS SUPPRESSED WHEN THE TRACE IS EMPTY</b> [pfw.sra:L137], which is the
    /// normal shallow-shape outcome: field 7 never arrived, so the local is still the empty string.
    /// </para>
    /// <para>
    /// The line number is rendered with invariant culture so that no hosting locale can inject a group
    /// separator or a non-ASCII negative sign into a report that a characterization recording is
    /// compared against byte for byte. PowerScript's <c>String(long)</c> [pfw.sra:L136] is itself
    /// culture-free for integers, so this is fidelity rather than an addition.
    /// </para>
    /// </remarks>
    internal static string BuildReport(LegacyDecodedSystemError decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        // pfw.sra:L129-L130 - `sErrInfo = "类型: SYSTEM" + "~n信息: " + Error.Text`
        //
        // "SYSTEM" is a literal in the legacy, not a rendered enumeration: this handler is the SYSTEM
        // error handler and the line always reads the same.
        string report = "类型: SYSTEM" + "\n信息: " + decoded.Text;

        // pfw.sra:L131-L133 - `if Error.WindowMenu <> Error.Object then ... end if`
        if (decoded.WindowMenu != decoded.Object)
        {
            report += "\n窗口: " + decoded.WindowMenu;
        }

        // pfw.sra:L134 - `sErrInfo += "~n对象: " + Error.Object`
        report += "\n对象: " + decoded.Object;

        // pfw.sra:L135-L136 - `sErrInfo += "~n函数: " + Error.ObjectEvent + "~n行号: " +
        // String(Error.Line)`. One statement in the legacy, one statement here, so the two lines can
        // never be separated by a future edit.
        report += "\n函数: " + decoded.ObjectEvent +
                  "\n行号: " + decoded.Line.ToString(CultureInfo.InvariantCulture);

        // pfw.sra:L137-L139 - `if sStackTrace <> "" then sErrInfo += "~n调用栈:~n" + sStackTrace`
        //
        // No trailing space after the colon, and a line feed instead. See the section note above.
        if (decoded.StackTrace != string.Empty)
        {
            report += "\n调用栈:\n" + decoded.StackTrace;
        }

        return report;
    }

    // ----------------------------------------------------------------------------------------------
    // STEP 4 - THE WHOLE CHAIN, INCLUDING THE TERMINATING STEP [pfw.sra:L111-L143]
    // ----------------------------------------------------------------------------------------------
    // One entry point so that FailFastTests can drive the handler end to end, while
    // SystemErrorRoundTripTests calls the split and the decode on their own. The terminating step is
    // the reason this member is an instance member: the seam is injected, and there is no default.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the legacy <c>systemerror</c> handler end to end: split, decode, build the report, then
    /// terminate. [pfw.sra:L111-L143]
    /// </summary>
    /// <param name="error">The incoming error state.</param>
    /// <returns>The decoded error and the assembled report, for inspection by the caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>TERMINATION IS UNCONDITIONAL.</b> pfw.sra:L143's <c>HALT CLOSE</c> sits outside every
    /// conditional above it, so the legacy terminates for EVERY system error it handles - a payload
    /// that failed the gate, one with a single field, one with six. The sink is therefore invoked
    /// before this method can return, on every path, and there is no branch in which it is skipped.
    /// Turning any of those cases into warn-and-continue would be a behavioural change dressed up as
    /// robustness (AAP 0.1.4, 0.6.7).
    /// </para>
    /// <para>
    /// <b>WHY THIS MEMBER RETURNS AT ALL, WHEN THE LEGACY CANNOT.</b> Nothing follows
    /// <c>HALT CLOSE</c> in the legacy. Here the sink is a recording double that returns normally, and
    /// the return value exists only so a suite can inspect what was produced. The
    /// <see langword="return"/> is placed AFTER the sink call so no observable step is reordered: a
    /// sink that threw would prevent the return, which is the closest a managed method can get to not
    /// coming back.
    /// </para>
    /// <para>
    /// <b>NOTHING IS DISPLAYED.</b> pfw.sra:L141's <c>MessageBox</c> becomes the sink call and nothing
    /// else: no dialog, no console write, no log line. <see cref="DialogTitle"/> is available to a
    /// suite that wants to assert the legacy title survived, and is not written anywhere by this
    /// method.
    /// </para>
    /// </remarks>
    internal LegacySystemErrorOutcome HandleSystemError(LegacySystemErrorState error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // pfw.sra:L114-L127
        LegacyDecodedSystemError decoded = Decode(error);

        // pfw.sra:L129-L139
        string report = BuildReport(decoded);

        // pfw.sra:L141-L143 - `MessageBox("系统错误",sErrInfo,StopSign!)` then `HALT CLOSE`, collapsed
        // into the one observable act a headless service can perform. UNCONDITIONAL: no `if` guards
        // this call, deliberately.
        _termination.Terminate(report, HaltCloseExitCode);

        return new LegacySystemErrorOutcome(decoded, report);
    }

    // ----------------------------------------------------------------------------------------------
    // ONE-BASED POWERSCRIPT PRIMITIVES (AAP 0.4.5.4, risk R9)
    // ----------------------------------------------------------------------------------------------
    // Pos, Left, Mid and Long are ONE BASED in PowerScript, and UpperBound answers a LAST INDEX rather
    // than a count. Every ported expression above is written in terms of the equivalents below so that
    // pfw.sra:L52-L68 and :L117,L123 port CHARACTER FOR CHARACTER. Hand-converting
    // `Left(src, nPos - 1)` or `Mid(src, nPos + nLenDelimiter)` into zero-based substring arithmetic at
    // the call site is exactly how a silent off-by-one enters, and the AAP names one-based translation
    // the single most dangerous mechanical hazard in this refactor - a wrong index here would produce a
    // plausible field list that quietly disagrees with the producer.
    //
    // They are PRIVATE because they are implementation detail of this replica. They deliberately do NOT
    // reuse StackTraceProvider's internal UpperBound and FrameAt: those two are documented as accessors
    // for a CAPTURED FRAME ARRAY, and the values indexed here are payload fields in a list, so
    // borrowing them would silently widen their documented contract. Their semantics are nevertheless
    // matched exactly, so the two ports of the same PowerScript primitive cannot disagree.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the ONE-BASED position of the first occurrence of <paramref name="target"/> in
    /// <paramref name="source"/>, or <c>0</c> when it is absent. Reproduces PowerScript
    /// <c>Pos(string, target)</c>. [pfw.sra:L57,L62]
    /// </summary>
    /// <param name="source">The string to search.</param>
    /// <param name="target">The string to search for; never empty at any call site here, because the
    /// empty-delimiter path returns before the first search.</param>
    /// <returns>A one-based position, or <c>0</c> when there is no match.</returns>
    /// <remarks>
    /// Ordinal and case-sensitive, matching PowerScript. <see cref="string.IndexOf(string,
    /// StringComparison)"/> answers <c>-1</c> for no match, and adding one converts both the found
    /// case to one-based and the absent case to the <c>0</c> the ported loop condition tests against.
    /// </remarks>
    private static int Pos(string source, string target)
    {
        return source.IndexOf(target, StringComparison.Ordinal) + 1;
    }

    /// <summary>
    /// Returns the first <paramref name="count"/> characters of <paramref name="source"/>. Reproduces
    /// PowerScript <c>Left(string, n)</c>. [pfw.sra:L59]
    /// </summary>
    /// <param name="source">The string to take from.</param>
    /// <param name="count">How many characters to take.</param>
    /// <returns>
    /// The empty string when <paramref name="count"/> is zero or less; the whole of
    /// <paramref name="source"/> when <paramref name="count"/> reaches or exceeds its length;
    /// otherwise the first <paramref name="count"/> characters.
    /// </returns>
    /// <remarks>
    /// A count of zero is reachable and normal, not an edge case to guard away: a delimiter at position
    /// one gives <c>position - 1 == 0</c>, which is what produces the empty segment for a payload whose
    /// first field is empty, and for the second and later empties of a run.
    /// </remarks>
    private static string Left(string source, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        if (count >= source.Length)
        {
            return source;
        }

        return source[..count];
    }

    /// <summary>
    /// Returns the remainder of <paramref name="source"/> from the ONE-BASED position
    /// <paramref name="start"/>. Reproduces PowerScript <c>Mid(string, start)</c>. [pfw.sra:L61]
    /// </summary>
    /// <param name="source">The string to take from.</param>
    /// <param name="start">A one-based position to take from.</param>
    /// <returns>
    /// The empty string when <paramref name="start"/> is below <c>1</c> or past the end of
    /// <paramref name="source"/>; otherwise everything from that position onwards.
    /// </returns>
    /// <remarks>
    /// The past-the-end answer is reachable and is what ends the consume loop: a payload ending in a
    /// delimiter leaves <c>position + delimiterLength</c> one past the end, so the remainder is the
    /// empty string - and that empty remainder is exactly what the ported trailing test at
    /// pfw.sra:L64 then declines to append, which is preserved defect D1.
    /// </remarks>
    private static string Mid(string source, int start)
    {
        if (start < 1 || start > source.Length)
        {
            return string.Empty;
        }

        return source[(start - 1)..];
    }

    /// <summary>
    /// Returns the LAST VALID one-based index of <paramref name="segments"/>, reproducing PowerScript
    /// <c>UpperBound</c>. [pfw.sra:L68]
    /// </summary>
    /// <param name="segments">The produced segment list.</param>
    /// <returns>
    /// The last valid one-based index, which is the list's count; <c>0</c> for an empty list, matching
    /// PowerScript's upper bound for an unallocated array.
    /// </returns>
    /// <remarks>
    /// The value is a LAST INDEX, not a count-past-the-end. Under one-based access it happens to share
    /// the same number as the managed count, and that coincidence is why the ported return needs no
    /// adjustment - stating it here is what stops a future reader from "correcting" it by one.
    /// </remarks>
    private static int UpperBound(List<string> segments)
    {
        return segments.Count;
    }

    /// <summary>
    /// Returns the payload field at a ONE-BASED index, reproducing PowerScript array access.
    /// [pfw.sra:L117-L124]
    /// </summary>
    /// <param name="fields">The segment list the splitter produced.</param>
    /// <param name="oneBasedIndex">
    /// A one-based index, so <c>1</c> is the first field and <see cref="UpperBound(List{string})"/> is
    /// the last.
    /// </param>
    /// <returns>The field text at that one-based position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is outside <c>1..UpperBound(fields)</c>. No bounds check is
    /// added on top of the list's own: PowerScript raises its own error on an out-of-range subscript,
    /// so propagating is the faithful behaviour, and returning a substitute value would be the
    /// behaviour change C-B forbids. Both ported call sites are guarded by the count tests at
    /// pfw.sra:L116 and :L119, so neither can reach this.
    /// </exception>
    /// <remarks>
    /// This is the ONLY place a payload field is indexed, which keeps the one-based-to-zero-based
    /// conversion in a single auditable expression rather than repeated seven times at the call sites.
    /// </remarks>
    private static string FieldAt(List<string> fields, int oneBasedIndex)
    {
        return fields[oneBasedIndex - 1];
    }

    /// <summary>
    /// Converts <paramref name="text"/> to a 64-bit integer, answering <c>0</c> when it is not a
    /// number. Reproduces PowerScript <c>Long(string)</c>. [pfw.sra:L117,L123]
    /// </summary>
    /// <param name="text">The field text to convert.</param>
    /// <returns>
    /// The parsed value, or <c>0</c> when <paramref name="text"/> is empty or not a valid integer.
    /// </returns>
    /// <remarks>
    /// <b>NEVER THROWS</b>, and the <c>0</c> is a LEGACY ANSWER rather than a tolerance added here:
    /// PowerScript's conversion functions answer zero for unconvertible text, so a malformed field 1 or
    /// field 6 silently becomes zero in the report exactly as it would have in the legacy. Parsing is
    /// invariant and accepts a leading sign and surrounding white space, matching both PowerScript's
    /// tolerant conversion and the producer's own port of the same primitive, so producer and consumer
    /// cannot disagree about a value that round-trips.
    /// </remarks>
    private static long Long(string text)
    {
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0L;
    }
}

// ==================================================================================================
//  THE DATA SHAPES
//  ------------------------------------------------------------------------------------------------
//  Three immutable records. The legacy has no equivalents - it mutates the global Error object and one
//  local - so these exist to make that transformation expressible without a global and observable to a
//  suite. Their member names follow the legacy Error members one for one, including `Object`, which is
//  what pfw.sra reads and what AssertionFailure already spells the same way; keeping the spelling is
//  what lets a reader compare the record against pfw.sra:L117-L124 line by line.
// ==================================================================================================

/// <summary>
/// The incoming system-error state, as the PowerBuilder runtime would have populated the global
/// <c>Error</c> object before <c>systemerror</c> ran. [pfw.sra:L111-L127]
/// </summary>
/// <param name="Number">
/// <c>Error.Number</c>. Overwritten from field 1 when the payload decodes [pfw.sra:L117].
/// </param>
/// <param name="Text">
/// <c>Error.Text</c>. <b>This is the payload</b> when the object name is
/// <see cref="LegacySystemErrorConsumer.AssertObjectName"/>: the decode splits this value and then
/// overwrites it from field 2 [pfw.sra:L115,L118].
/// </param>
/// <param name="WindowMenu">
/// <c>Error.WindowMenu</c>. Overwritten from field 3 only in the deep shape [pfw.sra:L120].
/// </param>
/// <param name="Object">
/// <c>Error.Object</c>. <b>This is the gate</b> [pfw.sra:L114] and it is also overwritten from field 4
/// in the deep shape [pfw.sra:L121], so on a deep payload the value that opened the gate is replaced
/// by the object the frame blames.
/// </param>
/// <param name="ObjectEvent">
/// <c>Error.ObjectEvent</c>. Overwritten from field 5 only in the deep shape [pfw.sra:L122].
/// </param>
/// <param name="Line">
/// <c>Error.Line</c>. Overwritten from field 6 only in the deep shape [pfw.sra:L123].
/// </param>
/// <remarks>
/// Non-nullable reference members are the contract; there are no runtime null guards on the members
/// themselves because the nullable context makes a null a compile-time error for any caller in this
/// assembly, and every caller is in this assembly.
/// </remarks>
internal sealed record LegacySystemErrorState(
    long Number,
    string Text,
    string WindowMenu,
    string Object,
    string ObjectEvent,
    long Line)
{
    /// <summary>
    /// Builds the incoming state for an assertion payload: the object name set to the gate value and
    /// every other member left at its neutral default.
    /// </summary>
    /// <param name="payload">
    /// The CRLF-joined payload, normally <c>AssertionFailure.Message</c> as the producer set it.
    /// </param>
    /// <returns>An incoming state that will pass the decode's gate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A test-authoring convenience, not a legacy behaviour: the runtime, not this factory, is what
    /// populates the object name in the real system. It exists because the round-trip suite's whole
    /// question is "does the payload the producer emitted decode back into the same fields", and that
    /// question needs no other incoming state.
    /// </para>
    /// <para>
    /// <b>One consequence worth knowing before asserting on a report built from this.</b> A SHALLOW
    /// payload leaves the object name at <see cref="LegacySystemErrorConsumer.AssertObjectName"/> and
    /// the window menu at the empty string, and those two differ - so
    /// <see cref="LegacySystemErrorConsumer.BuildReport(LegacyDecodedSystemError)"/> emits its window
    /// line with an EMPTY value [pfw.sra:L131-L132]. That is the legacy's own behaviour for a shallow
    /// payload, not an artefact of this factory. A suite that wants the runtime's pre-populated members
    /// instead should use the primary constructor.
    /// </para>
    /// </remarks>
    internal static LegacySystemErrorState FromAssertPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new LegacySystemErrorState(
            0L,
            payload,
            string.Empty,
            LegacySystemErrorConsumer.AssertObjectName,
            string.Empty,
            0L);
    }
}

/// <summary>
/// The outcome of the decode: the seven decoded values plus enough state for a suite to tell which of
/// the two branches fired. [pfw.sra:L111-L127]
/// </summary>
/// <param name="Number">
/// <c>Error.Number</c> after the decode. Field 1 parsed with PowerScript <c>Long</c> semantics when
/// <paramref name="NumberAndTextDecoded"/> is <see langword="true"/>, otherwise the incoming value
/// [pfw.sra:L117].
/// </param>
/// <param name="Text">
/// <c>Error.Text</c> after the decode: field 2, or the incoming text - which is still the whole
/// undecoded payload when the gate or the count test failed [pfw.sra:L118].
/// </param>
/// <param name="WindowMenu">
/// <c>Error.WindowMenu</c> after the decode: field 3, or the incoming value [pfw.sra:L120].
/// </param>
/// <param name="Object">
/// <c>Error.Object</c> after the decode: field 4, or the incoming value - which is still
/// <see cref="LegacySystemErrorConsumer.AssertObjectName"/> for a shallow assert payload
/// [pfw.sra:L121].
/// </param>
/// <param name="ObjectEvent">
/// <c>Error.ObjectEvent</c> after the decode: field 5, or the incoming value [pfw.sra:L122].
/// </param>
/// <param name="Line">
/// <c>Error.Line</c> after the decode: field 6 parsed with PowerScript <c>Long</c> semantics, or the
/// incoming value [pfw.sra:L123].
/// </param>
/// <param name="StackTrace">
/// The legacy's <c>sStackTrace</c> local: field 7, or the empty string. It has no <c>Error</c> member
/// to live on, which is why the legacy keeps it in a local [pfw.sra:L112,L124], and the empty string is
/// what suppresses the report's call-stack line [pfw.sra:L137].
/// </param>
/// <param name="SegmentCount">
/// The splitter's answer [pfw.sra:L115]. <c>0</c> when the gate rejected the error, because the legacy
/// never calls the splitter in that case and its <c>nCount</c> local keeps its default. This is the
/// value both branch tests read, so it is the one to assert against when checking that six, seven and
/// eight fields are treated differently.
/// </param>
/// <param name="NumberAndTextDecoded">
/// Whether the two-or-more branch fired [pfw.sra:L116].
/// </param>
/// <param name="SevenFieldBranchTaken">
/// Whether the EXACTLY-seven branch fired [pfw.sra:L119]. <see langword="false"/> for six fields and
/// <see langword="false"/> for eight; see preserved defect D2 on
/// <see cref="LegacySystemErrorConsumer.Decode(LegacySystemErrorState)"/>.
/// </param>
internal sealed record LegacyDecodedSystemError(
    long Number,
    string Text,
    string WindowMenu,
    string Object,
    string ObjectEvent,
    long Line,
    string StackTrace,
    int SegmentCount,
    bool NumberAndTextDecoded,
    bool SevenFieldBranchTaken);

/// <summary>
/// What one end-to-end run of the handler produced. [pfw.sra:L111-L143]
/// </summary>
/// <param name="Decoded">The decoded error.</param>
/// <param name="Report">
/// The assembled report - the exact string handed to the termination seam, and the exact string the
/// legacy would have passed to <c>MessageBox</c> [pfw.sra:L141].
/// </param>
/// <remarks>
/// There is deliberately no "terminated" flag. Termination is UNCONDITIONAL, so a flag could only ever
/// read <see langword="true"/> and would invite a reader to believe there is a path on which it reads
/// otherwise. The termination is observed at the sink instead, which also proves it happened exactly
/// once.
/// </remarks>
internal sealed record LegacySystemErrorOutcome(LegacyDecodedSystemError Decoded, string Report);

// ==================================================================================================
//  THE TERMINATION SEAM
//  ------------------------------------------------------------------------------------------------
//  pfw.sra:L141-L143 shows a dialog and then executes HALT CLOSE. Neither act is available to a
//  headless service, and reproducing the second one literally inside a test assembly would kill the
//  test host and take the entire run down with it - so the terminating step is expressed as a
//  one-method abstraction that the suites satisfy with the recording implementation below.
//
//  THERE IS NO PRODUCTION DEFAULT. No implementation in this file, and none anywhere in this test
//  assembly, calls Environment.Exit, Environment.FailFast, Process.Kill or any other way of ending a
//  process. The abstraction is the boundary at which the replica stops.
//
//  AND IT LIVES HERE, NOT IN THE PRODUCTION LIBRARY. Making the real fail-fast path injectable so a
//  test could observe it would be weakening production code to make it testable. The Diagnostics
//  library's own contract is untouched: its AssertFailed always throws and never returns.
// ==================================================================================================

/// <summary>
/// The stand-in for <c>MessageBox</c> followed by <c>HALT CLOSE</c>. [pfw.sra:L141-L143]
/// </summary>
/// <remarks>
/// One method, because the legacy's terminating step is one act. An implementation is free to record,
/// to throw, or to do nothing at all, but <b>no implementation may end the process</b>: this interface
/// is only ever satisfied inside a test assembly, where doing so would destroy the run.
/// </remarks>
internal interface ILegacyTerminationSink
{
    /// <summary>
    /// Invoked once per handled system error, after the report has been assembled and immediately
    /// before the handler returns.
    /// </summary>
    /// <param name="report">
    /// The assembled report [pfw.sra:L129-L139], which is what the legacy displayed.
    /// </param>
    /// <param name="exitCode">
    /// The exit code standing in for <c>HALT CLOSE</c>, always
    /// <see cref="LegacySystemErrorConsumer.HaltCloseExitCode"/> from the handler. Modelled rather than
    /// preserved: PowerScript publishes no exit code for <c>HALT CLOSE</c>.
    /// </param>
    void Terminate(string report, int exitCode);
}

/// <summary>
/// An <see cref="ILegacyTerminationSink"/> that records every invocation instead of terminating
/// anything. This is what the suites inject.
/// </summary>
/// <remarks>
/// <para>
/// It records rather than asserts, so a suite can check both that the terminating step happened and
/// that it happened EXACTLY ONCE - a replica that terminated twice for one error, or not at all, is a
/// fidelity failure that a void stand-in could not detect.
/// </para>
/// <para>
/// <b>Not synchronized</b>, deliberately: the suites drive one error at a time, and a lock would hide
/// an accidental concurrent second invocation rather than reveal it.
/// </para>
/// <para>
/// The interface member is implemented EXPLICITLY, so this file declares no <c>public</c> member
/// anywhere. Nothing about the recorder needs to be reachable except through the seam the consumer
/// holds, and every observation below is <c>internal</c> like the type itself.
/// </para>
/// </remarks>
internal sealed class RecordingTerminationSink : ILegacyTerminationSink
{
    /// <summary>The reports handed over, in invocation order.</summary>
    private readonly List<string> _reports = [];

    /// <summary>The exit codes handed over, in invocation order and index-aligned with the reports.
    /// </summary>
    private readonly List<int> _exitCodes = [];

    /// <summary>
    /// How many times the terminating step ran. One per handled system error, decoded or not.
    /// </summary>
    /// <remarks>
    /// Derived from the recorded reports rather than kept as a separate counter, so the count and the
    /// records cannot drift apart.
    /// </remarks>
    internal int InvocationCount => _reports.Count;

    /// <summary>Every report handed over, in invocation order.</summary>
    internal IReadOnlyList<string> Reports => _reports;

    /// <summary>Every exit code handed over, in invocation order.</summary>
    internal IReadOnlyList<int> ExitCodes => _exitCodes;

    /// <summary>
    /// The most recent report, or <see langword="null"/> when the terminating step has not run.
    /// </summary>
    /// <remarks>
    /// The null is meaningful and is not a stand-in for the empty string: an assembled report is never
    /// empty, because six of its seven lines are unconditional, so <see langword="null"/> can only mean
    /// "never invoked".
    /// </remarks>
    internal string? LastReport => _reports.Count == 0 ? null : _reports[^1];

    /// <summary>
    /// The most recent exit code, or <see langword="null"/> when the terminating step has not run.
    /// </summary>
    internal int? LastExitCode => _exitCodes.Count == 0 ? null : _exitCodes[^1];

    /// <summary>
    /// Forgets every recorded invocation, so one instance can serve several cases in a suite.
    /// </summary>
    internal void Reset()
    {
        _reports.Clear();
        _exitCodes.Clear();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Records and returns. It performs NO output of any kind - no dialog, no console write, no log
    /// line - and it does not end the process. Both lists are appended in one uninterrupted pair of
    /// statements so their indices stay aligned.
    /// </remarks>
    void ILegacyTerminationSink.Terminate(string report, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(report);

        _reports.Add(report);
        _exitCodes.Add(exitCode);
    }
}
