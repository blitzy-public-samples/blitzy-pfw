// ==================================================================================================
//  SystemErrorRoundTripTests.cs - THE ONE SUITE THAT EXERCISES BOTH HALVES OF THE ASSERT PROTOCOL
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE PROVES, AND WHY NOTHING ELSE PROVES IT
//  ------------------------------------------------------------------------------------------------
//  The assertion payload is a wire format with two halves that no type system checks:
//
//      THE PRODUCER  PowerFramework.Shared.Diagnostics.Assertions, ported from
//                    ws_objects/pfw.common.pbl.src/assert.srf. It JOINS its fields with a
//                    CARRIAGE RETURN AND LINE FEED PAIR [assert.srf:L19,L74-L78] while embedding
//                    BARE LINE FEEDS inside field 2 [assert.srf:L26] and field 7
//                    [assert.srf:L63].
//
//      THE CONSUMER  the legacy application object's systemerror event, ws_objects/pfw.pbl.src/
//                    pfw.sra:L111-L144. It SPLITS on the pair [pfw.sra:L115] and then COUNTS
//                    [pfw.sra:L116,L119].
//
//  Those two facts are compatible only by careful construction. A single carriage-return pair used
//  for an internal separator would shatter one field into several and defeat the consumer's
//  exactly-seven test, silently costing it the window, the object, the event, the line number and
//  the entire call stack at once. A splitter that appended one more segment, or one fewer, would do
//  the same from the other side. Neither half can detect the mistake alone, and no other suite in
//  this project runs both halves together - the payload suite drives the producer and stops at the
//  payload, and the fail-fast suite drives the consumer end to end from payloads it is handed. THIS
//  suite is therefore the only guard the repository has against a delimiter change or an off-by-one
//  in the field layout, which is exactly the rationale its folder requirement states.
//
//  Every test here follows one shape: PRODUCE a payload with the producer, DECODE it with the
//  consumer, and assert the values came back as they went in.
//
//  ================================================================================================
//  WHY THE PRODUCTION GATEWAY HANDLER IS NOT REFERENCED (C-A). DO NOT "IMPROVE" THIS
//  ================================================================================================
//  The SHIPPING consumer of this payload is
//  services/gateway-service/PowerFramework.Gateway/Diagnostics/SystemErrorHandler.cs, and this
//  project deliberately DOES NOT reference it. AAP 0.7.3 constraint C-A permits exactly one form of
//  coupling across a service boundary - the published contracts - and a test project reaching into
//  a service's internals to reuse a class is precisely the coupling that rules out. The test project
//  file says the same thing in its own words and holds exactly one project reference for that
//  reason.
//
//  The decode side here is therefore LegacySystemErrorConsumer, this project's own replica of the
//  legacy application object (C-K). It is a replica of pfw.sra, NOT a copy of the Gateway handler
//  and NOT a wrapper over it: its authority is the read-only legacy source, which AAP 0.1.4
//  establishes is the only statement of intended behaviour that exists. A future reader who
//  "simplifies" this file by referencing PowerFramework.Gateway would break C-A, would make this
//  project depend on a service, and would replace an independent check of the protocol with a
//  tautology - the handler would then be validated against itself.
//
//  ================================================================================================
//  THE TWO DEFECT-SHAPED BEHAVIOURS PINNED HERE (C-B). NEITHER MAY BE FIXED
//  ================================================================================================
//  D1  THE SPLITTER DROPS A TRAILING EMPTY FIELD. Its loop appends every segment that PRECEDES a
//      delimiter, empty ones included [pfw.sra:L58-L63], but the segment that FOLLOWS the last
//      delimiter is appended only when it is non-empty [pfw.sra:L64-L66]. A payload with seven
//      fields written and an empty seventh therefore arrives as SIX segments. Section 4 pins the
//      hazard, proves the producer is safe from it, and proves the safety rests on two facts rather
//      than one.
//
//  D2  THE FULL BRANCH FIRES AT EXACTLY SEVEN FIELDS, NOT AT SEVEN OR MORE. The legacy test is an
//      equality [pfw.sra:L119], so six fields and EIGHT fields both decode as the shallow shape and
//      both silently lose all five frame-derived values. Section 4 pins it with hand-built payloads
//      of six, seven and eight fields, and Section 2 shows the eight-field case is reachable from
//      the real producer.
//
//  Neither is corrected, because in both cases the observable contract IS the field count the
//  consumer sees, and changing it would change the report the framework produces.
//
//  ================================================================================================
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  ================================================================================================
//  There are NO user specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. Nothing is invented, inferred
//  or back filled from convention in their place, and the absence is not read as licence to lower
//  the bar. The binding constraints are AAP 0.7.2, the enterprise standard baseline, and AAP 0.7.3,
//  the twelve non rule constraints. Five bind this file:
//
//      C-A  No coupling across a service boundary. See the block above.
//      C-B  Documented defects replicated rather than corrected. D1 and D2 above, each annotated as
//           DELIBERATE LEGACY where it is asserted.
//      C-C  ws_objects/** is read only and is the specification. Four legacy paths were read as the
//           behavioural source for this file and none was modified, moved or reformatted:
//           pfw.pbl.src/pfw.sra, common.pbl.src/assert.srf, common.pbl.src/assertionfailed.sru and
//           tests.pbl.src/w_test_assert.srw. Nothing here reads a path under ws_objects at build
//           time or at run time; every fixture is constructed in code.
//      C-H  80 percent line coverage per project, measured from the Cobertura report. Warnings are
//           errors here by inheritance and there is no NoWarn, so this file is warning clean.
//      C-K  Every boundary specific decision is documented where it applies, including the one
//           locator correction below and the honest limit on the producer-safety guarantee in
//           Section 4.
//
//  NAMING. The repository root .editorconfig scopes its naming-analyzer suppressions to named
//  PRODUCTION files and none of them is in this project, so every identifier here is conventional
//  PascalCase and no SCREAMING_SNAKE constant is declared - not even for the legacy constants whose
//  spelling is preserved elsewhere.
//
//  LEGACY TEXT IS VERBATIM (AAP 0.8.2). The seven Chinese report labels and the dialog title are
//  spelled out in this file INDEPENDENTLY of the consumer, and that independence is the point: an
//  assertion that reused the consumer's own literal would pass no matter what it said. They were
//  taken from a byte dump of pfw.sra:L129-L141, not retyped from memory, and they are neither
//  translated nor reworded in any expectation.
//
//  ONE LOCATOR CORRECTION, RECORDED HONESTLY (C-K). The object-name gate is at pfw.sra:L114, not
//  L113. Line 113 of that file is blank. Every locator in this file was checked against the file
//  itself rather than carried forward, which is how the discrepancy surfaced.
//
//  NO INDEX ARITHMETIC IS WRITTEN ANYWHERE HERE (AAP 0.4.5.4, risk R9). The AAP names one-based to
//  zero-based translation the single most dangerous mechanical hazard in this refactor, because an
//  off-by-one in a test agrees with an off-by-one in the code and both pass. So positions are named
//  by VALUE through LegacyStackFrames - its ExpectedCallerFrame, DepthFrame, ExpectedTrimmedStack
//  and ExpectedStackTraceInfo members exist for exactly this purpose - the trimmed stack is
//  measured as a COUNT rather than as a range, and an empty payload field is detected as two
//  ADJACENT delimiters rather than by subscripting a field list.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//      * It does not re-declare a frame string. Every frame comes from LegacyStackFrames, so this
//        suite and the trimming suite cannot disagree about what a frame looks like.
//      * It does not re-implement a splitter, a joiner over payload fields it invented, or any part
//        of the parse. It calls the producer and the consumer and asserts.
//      * It does not drive the terminating step. LegacySystemErrorConsumer.HandleSystemError needs
//        an injected termination seam and belongs to the fail-fast suite; the three members used
//        here - both static split overloads, Decode and BuildReport - are static and stateless
//        precisely so this suite can run them on their own.
//      * It asserts no performance objective of any kind. AAP 0.8.5 forbids one, because the
//        repository publishes no latency, throughput or availability commitment anywhere.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Round-trip characterization tests for the assertion payload: produced by
/// <see cref="Assertions"/> [assert.srf] and decoded by <see cref="LegacySystemErrorConsumer"/>
/// [pfw.sra:L111-L144].
/// </summary>
/// <remarks>
/// Table-driven throughout, as AAP 0.6.7 prescribes: each parity matrix is a theory fed from member
/// data, so a case's expected outcome is readable as a row rather than buried in a body.
/// </remarks>
public class SystemErrorRoundTripTests
{
    // ==============================================================================================
    //  PROTOCOL CONSTANTS. EVERY ONE CARRIES ITS LEGACY LOCATOR
    // ==============================================================================================

    /// <summary>
    /// Payload field 1 as it crosses the boundary: TEXT, not a number [assert.srf:L22].
    /// </summary>
    private const string MagicNumberText = "-10000";

    /// <summary>
    /// Payload field 1 after the consumer parses it back [pfw.sra:L117].
    /// </summary>
    private const long MagicNumber = -10000L;

    /// <summary>
    /// Payload field 2's fixed opening text, before any info continuation [assert.srf:L24].
    /// </summary>
    private const string BareAssertionText = "Assertion failed";

    /// <summary>
    /// The separator that joins field 2's info continuation: a BARE LINE FEED [assert.srf:L26].
    /// </summary>
    /// <remarks>
    /// Named separately from <see cref="LegacyStackFrames.FrameSeparator"/> even though both are a
    /// line feed, because they are two different legacy decisions at two different locators - this
    /// one inside field 2, that one inside field 7 - and a future change to either must be visible
    /// as a change to one of them rather than to a shared symbol.
    /// </remarks>
    private const string InfoSeparator = "\n";

    /// <summary>
    /// The opening of the source-location suffix that the deep shape appends to
    /// <see cref="AssertionFailure.Info"/> and to <b>nothing else</b> [assert.srf:L71].
    /// </summary>
    private const string LocationSuffixOpening = "\nat ";

    /// <summary>
    /// The separator between the object and the event inside that suffix [assert.srf:L71].
    /// </summary>
    private const string LocationSuffixSeparator = "::";

    /// <summary>
    /// The oracle's own info string [w_test_assert.srw:L42], used verbatim so the matrices exercise
    /// a real legacy scenario rather than an invented one.
    /// </summary>
    private const string OracleInfo = "Invalid Number!";

    /// <summary>
    /// The field count of the deep payload shape [assert.srf:L66-L70, pfw.sra:L119].
    /// </summary>
    private const int DeepFieldCount = 7;

    /// <summary>
    /// The field count of the shallow payload shape [assert.srf:L22-L24, pfw.sra:L116].
    /// </summary>
    private const int ShallowFieldCount = 2;

    // ==============================================================================================
    //  THE SEVEN REPORT LABELS AND THE DIALOG TITLE, VERBATIM FROM pfw.sra (AAP 0.8.2)
    // ==============================================================================================
    //  Spelled here INDEPENDENTLY of the consumer on purpose: reusing its literals would make the
    //  label assertions tautological. Taken from a byte dump of pfw.sra:L129-L141.
    //
    //  Note the deliberate irregularity, which is legacy formatting and is not harmonised: every
    //  label is "<label>: " with an ASCII colon AND a trailing space, EXCEPT the call-stack label,
    //  which has a colon and then a line feed so the trace starts on its own line.
    // ==============================================================================================

    /// <summary>The report's first line, in full [pfw.sra:L129].</summary>
    private const string TypeLine = "类型: SYSTEM";

    /// <summary>The message label [pfw.sra:L130].</summary>
    private const string MessageLabel = "\n信息: ";

    /// <summary>The window label, emitted only conditionally [pfw.sra:L132].</summary>
    private const string WindowLabel = "\n窗口: ";

    /// <summary>The object label [pfw.sra:L134].</summary>
    private const string ObjectLabel = "\n对象: ";

    /// <summary>The function label [pfw.sra:L135].</summary>
    private const string FunctionLabel = "\n函数: ";

    /// <summary>The line-number label [pfw.sra:L136].</summary>
    private const string LineLabel = "\n行号: ";

    /// <summary>The call-stack label, emitted only conditionally [pfw.sra:L138].</summary>
    private const string CallStackLabel = "\n调用栈:\n";

    /// <summary>The error dialog's title [pfw.sra:L141].</summary>
    private const string LegacyDialogTitle = "系统错误";

    // ==============================================================================================
    //  HAND-BUILT PAYLOAD CONTENT, FOR THE CASES THE PRODUCER CANNOT REACH
    // ==============================================================================================
    //  Section 4 has to feed the consumer payloads of six and eight fields, and the producer emits
    //  only two or seven [assert.srf:L37]. Those payloads are therefore composed field by field from
    //  the values below rather than produced, and every value is deliberately unlike any fixture
    //  frame so that a decoded value can only have come from the field it was written to.
    // ==============================================================================================

    /// <summary>Hand-built field 3, the window/menu.</summary>
    private const string HandBuiltWindowMenu = "handbuilt_windowmenu";

    /// <summary>Hand-built field 4, the object.</summary>
    private const string HandBuiltObject = "handbuilt_object";

    /// <summary>Hand-built field 5, the event name.</summary>
    private const string HandBuiltObjectEvent = "handbuilt_objectevent";

    /// <summary>Hand-built field 6 as it crosses the boundary: TEXT [pfw.sra:L123 parses it].</summary>
    private const string HandBuiltLineText = "4242";

    /// <summary>Hand-built field 6 after the consumer parses it back.</summary>
    private const long HandBuiltLine = 4242L;

    /// <summary>
    /// Hand-built field 7: two frames joined by the intra-field separator the producer uses
    /// [assert.srf:L63], so the hand-built payload is shaped like a produced one.
    /// </summary>
    private const string HandBuiltStackTrace =
        "handbuilt_frame_one" + LegacyStackFrames.FrameSeparator + "handbuilt_frame_two";

    /// <summary>
    /// A hand-built EIGHTH field. There is no such field in the protocol; it exists to build the
    /// eight-field payload that preserved defect D2 turns into a silent loss of five values.
    /// </summary>
    private const string HandBuiltEighthField = "handbuilt_eighth_field";

    /// <summary>
    /// The incoming <c>Error.Number</c> used by the gate theories, deliberately not
    /// <see cref="MagicNumber"/> so a pass-through is distinguishable from a decode.
    /// </summary>
    private const long IncomingNumber = 42L;

    /// <summary>The incoming <c>Error.WindowMenu</c> used by the gate theories.</summary>
    private const string IncomingWindowMenu = "w_incoming";

    /// <summary>The incoming <c>Error.ObjectEvent</c> used by the gate theories.</summary>
    private const string IncomingObjectEvent = "of_incoming";

    /// <summary>The incoming <c>Error.Line</c> used by the gate theories.</summary>
    private const long IncomingLine = 7L;

    /// <summary>
    /// The eight payload fields the hand-built payloads are cut from, in payload order.
    /// </summary>
    /// <remarks>
    /// A static readonly field rather than an inline array at each call site, so no call site
    /// allocates a constant array as an argument, and so the eight values are declared exactly once.
    /// </remarks>
    private static readonly string[] HandBuiltFieldSequence =
    [
        MagicNumberText,        // field 1 - parsed back to a number  [pfw.sra:L117]
        BareAssertionText,      // field 2 - the error text           [pfw.sra:L118]
        HandBuiltWindowMenu,    // field 3 - the window/menu          [pfw.sra:L120]
        HandBuiltObject,        // field 4 - the object               [pfw.sra:L121]
        HandBuiltObjectEvent,   // field 5 - the event name           [pfw.sra:L122]
        HandBuiltLineText,      // field 6 - parsed back to a number  [pfw.sra:L123]
        HandBuiltStackTrace,    // field 7 - the call stack           [pfw.sra:L124]
        HandBuiltEighthField,   // field 8 - NOT part of the protocol
    ];

    // ==============================================================================================
    //  HELPERS. FIVE, AND NOT ONE OF THEM REIMPLEMENTS ANY PART OF THE PROTOCOL
    // ==============================================================================================

    /// <summary>
    /// Produces a payload the way the framework does, taking the frame count from the frame list's
    /// own length.
    /// </summary>
    /// <param name="callStack">The frames, outermost first, from <see cref="LegacyStackFrames"/>.</param>
    /// <param name="info">The info string, empty for the info-less arity [assert.srf:L25].</param>
    /// <returns>The populated failure, whose <see cref="Exception.Message"/> is the payload.</returns>
    /// <remarks>
    /// The count and the array are separate arguments in the seam because the legacy keeps them
    /// separate - the count is the capture primitive's return value in its own local
    /// [assert.srf:L30] - so passing the length here is the ORDINARY pairing and any other pairing is
    /// stated explicitly at its call site. Section 4 has the one deliberate exception.
    /// </remarks>
    private static AssertionFailure Produce(string[] callStack, string info)
    {
        return Assertions.BuildFailure(callStack, callStack.Length, info);
    }

    /// <summary>
    /// Sends a payload through the consumer exactly as the legacy <c>systemerror</c> event would
    /// receive one raised by the assert function object [pfw.sra:L114-L127].
    /// </summary>
    /// <param name="payload">The payload, normally <see cref="Exception.Message"/> of a produced failure.</param>
    /// <returns>The decoded fields plus the two branch flags.</returns>
    private static LegacyDecodedSystemError Decode(string payload)
    {
        return LegacySystemErrorConsumer.Decode(LegacySystemErrorState.FromAssertPayload(payload));
    }

    /// <summary>
    /// Splits a payload into its fields with the consumer's own splitter.
    /// </summary>
    /// <param name="payload">The payload to split.</param>
    /// <returns>The ordered fields, subject to preserved defect D1 [pfw.sra:L64-L66].</returns>
    /// <remarks>
    /// The consumer's splitter, never a local one. A second implementation here could agree with a
    /// broken producer and disagree with the shipping consumer, which is the one outcome this suite
    /// exists to make impossible.
    /// </remarks>
    private static IReadOnlyList<string> SplitFields(string payload)
    {
        return LegacySystemErrorConsumer.SplitString(
            payload, LegacySystemErrorConsumer.FieldDelimiter);
    }

    /// <summary>
    /// Returns what payload field 2 must contain for a given info string [assert.srf:L24-L27].
    /// </summary>
    /// <param name="info">The info string.</param>
    /// <returns>
    /// The bare text when <paramref name="info"/> is empty, otherwise the bare text, one BARE LINE
    /// FEED, and the info verbatim.
    /// </returns>
    /// <remarks>
    /// The test is against the EMPTY STRING, exactly as the legacy writes it [assert.srf:L25] - not a
    /// null test, not a whitespace test and not a length-or-null convenience. A whitespace-only info
    /// is therefore appended like any other, and the matrix has a row proving it.
    /// </remarks>
    private static string ExpectedFieldTwo(string info)
    {
        return info == string.Empty ? BareAssertionText : BareAssertionText + InfoSeparator + info;
    }

    /// <summary>
    /// Composes a payload of exactly <paramref name="fieldCount"/> hand-built fields.
    /// </summary>
    /// <param name="fieldCount">
    /// How many fields to write, from <c>0</c> to the length of
    /// <see cref="HandBuiltFieldSequence"/>.
    /// </param>
    /// <returns>The fields joined by the consumer's own field delimiter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fieldCount"/> is negative or exceeds the available field count.
    /// </exception>
    /// <remarks>
    /// Joined with <see cref="LegacySystemErrorConsumer.FieldDelimiter"/> rather than a local
    /// literal, so a delimiter change cannot leave this helper agreeing with a stale value. Joining
    /// is not splitting: nothing here reimplements the splitter, whose defect D1 is precisely what
    /// several of these payloads are built to expose.
    /// </remarks>
    private static string HandBuiltPayload(int fieldCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fieldCount, HandBuiltFieldSequence.Length);

        return string.Join(
            LegacySystemErrorConsumer.FieldDelimiter, HandBuiltFieldSequence.Take(fieldCount));
    }

    /// <summary>
    /// Returns the fixture frame list appropriate to a frame count: the shallow fixture at or below
    /// the shallow boundary, the depth fixture above it.
    /// </summary>
    /// <param name="frameCount">The number of frames wanted.</param>
    /// <returns>A fresh frame list of exactly that length, outermost first.</returns>
    /// <remarks>
    /// Both fixtures can produce a list of any small length, but each documents a different intent -
    /// <see cref="LegacyStackFrames.Shallow(int)"/> carries the framework frames a genuinely shallow
    /// capture would contain, <see cref="LegacyStackFrames.WithDepth(int)"/> carries individually
    /// numbered frames - so the boundary is honoured rather than ignored. The boundary itself is
    /// <see cref="LegacyStackFrames.MaxShallowFrameCount"/> and is never spelled as a literal
    /// [assert.srf:L37].
    /// </remarks>
    private static string[] FramesFor(int frameCount)
    {
        return frameCount <= LegacyStackFrames.MaxShallowFrameCount
            ? LegacyStackFrames.Shallow(frameCount)
            : LegacyStackFrames.WithDepth(frameCount);
    }

    // ==============================================================================================
    //  1. THE SEVEN-FIELD ROUND TRIP - FIELD BY FIELD
    // ==============================================================================================

    /// <summary>
    /// The deep parity matrix: the frame the producer will blame, and the four values its parse must
    /// yield [assert.srf:L38-L59].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row places its frame in the SAME slot of the SAME five-frame list through
    /// <see cref="LegacyStackFrames.Deep(string)"/>, so the four surrounding frames are identical
    /// across rows and any difference in the decoded payload is attributable to the frame under test
    /// and to nothing else.
    /// </para>
    /// <para>
    /// The frame TEXT is referenced through <see cref="LegacyStackFrames"/> and never retyped, so a
    /// change to a fixture moves the matrix with it instead of leaving a stale literal that still
    /// passes. The four EXPECTED values are spelled out here rather than derived, because a matrix
    /// that computed them would be re-deriving the parse it is meant to check.
    /// </para>
    /// <para>
    /// <b>WHY BOTH DOT SHAPES ARE PRESENT, AND WHY THAT IS NOT REDUNDANCY.</b> The two-dot and
    /// namespaced rows are the only ones where the window and the object DIFFER, so they are the only
    /// rows that can detect fields 3 and 4 being swapped anywhere in the protocol. On every one-dot
    /// row the producer assigns them the same value outright [assert.srf:L51] and a swap is invisible.
    /// Dropping either dot shape would leave a real defect undetectable.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string, long> DeepCallerFrameMatrix
    {
        get
        {
            TheoryData<string, string, string, string, long> matrix = [];

            // TWO DOTS - the oracle's button event. Window and object DIFFER [assert.srf:L44,L46],
            // which is what makes the window line appear in the report [pfw.sra:L131].
            matrix.Add(LegacyStackFrames.TwoDotFrame, "w_test_assert", "cb_1", "clicked", 137L);

            // MANY DOTS - a real managed frame. The parse looks only at the FIRST and the LAST dot,
            // so the object keeps its interior dots. That is the legacy arithmetic applied faithfully
            // to the new frame format, not a defect, and it must not be "corrected".
            matrix.Add(
                LegacyStackFrames.NamespacedFrame,
                "PowerFramework",
                "Shared.Kernel.Predicates",
                "IsSucceeded",
                12L);

            // EXACTLY ONE DOT - a non-visual class. PRESERVED LEGACY BEHAVIOUR (C-B): the object is
            // assigned FROM the window menu [assert.srf:L51], so the two fields carry the SAME string
            // across the boundary. That equality is load bearing at the far end - it is what makes the
            // consumer suppress its window line [pfw.sra:L131] - and Section 5 asserts the
            // consequence. It is not a copy-paste slip and must not be "improved".
            matrix.Add(
                LegacyStackFrames.SingleDotFrame,
                "n_cst_thread_trans",
                "n_cst_thread_trans",
                "of_connect",
                42L);

            // EXACTLY ONE DOT, from the oracle itself: the window function whose body is the
            // assertion [w_test_assert.srw:L39]. Kept alongside the row above because this one proves
            // the equality is what the ORACLE produces by default, so it cannot be dismissed as an
            // artefact of a contrived example.
            matrix.Add(
                LegacyStackFrames.WindowFunctionFrame,
                "w_test_assert",
                "w_test_assert",
                "wf_testassert",
                39L);

            // DEGRADED - the shape a release build with no symbol file produces. One dot, so window
            // equals object again, and a line number of zero. This is the path a failing assertion is
            // already on, so it is the one that must not throw.
            matrix.Add(LegacyStackFrames.DegradedFrame, "<unknown>", "<unknown>", "?", 0L);

            // NO DOT AT ALL - fields 3 and 4 are never assigned and cross the boundary EMPTY, while
            // fields 5 and 6 are still assigned because those steps sit OUTSIDE the dot guard
            // [assert.srf:L40 vs L54-L59]. Seven fields are still written, so Section 4 reuses this
            // shape as its realistic empty-middle-field case.
            matrix.Add(LegacyStackFrames.DotlessFrame, "", "", "dotlessframe", 7L);

            // NO SPACE AFTER THE METHOD - field 5 crosses the boundary EMPTY because the event window
            // computes to a negative length, and yet the LINE STILL PARSES [assert.srf:L55-L59].
            // Losing the event does not lose the location, and that asymmetry is worth pinning.
            matrix.Add(LegacyStackFrames.NoSpaceAfterMethodFrame, "w_test_assert", "cb_1", "", 137L);

            return matrix;
        }
    }

    /// <summary>
    /// The frame shapes alone, for the theories that need a shape but no expected parse.
    /// </summary>
    /// <remarks>
    /// The same seven shapes as <see cref="DeepCallerFrameMatrix"/>. A separate member rather than a
    /// reuse of that one, because a theory must consume every parameter it declares and these
    /// theories have no use for the four expected values.
    /// </remarks>
    public static TheoryData<string> DeepCallerFrameShapes =>
        new(
            LegacyStackFrames.TwoDotFrame,
            LegacyStackFrames.NamespacedFrame,
            LegacyStackFrames.SingleDotFrame,
            LegacyStackFrames.WindowFunctionFrame,
            LegacyStackFrames.DegradedFrame,
            LegacyStackFrames.DotlessFrame,
            LegacyStackFrames.NoSpaceAfterMethodFrame);

    /// <summary>
    /// Frame counts that all select the DEEP shape, from the boundary upwards.
    /// </summary>
    /// <remarks>
    /// <see cref="LegacyStackFrames.MinimumDeepFrameCount"/> is the first count that passes the
    /// producer's shape gate [assert.srf:L37] and is included as the boundary; the rest give the
    /// "two different depths" the requirement asks for and then some, so that the trimmed-stack
    /// arithmetic is checked at more than one width.
    /// </remarks>
    public static TheoryData<int> DeepFrameCounts =>
        new(LegacyStackFrames.MinimumDeepFrameCount, 4, 5, 6, 9);

    /// <summary>
    /// Every field of a deep payload survives the round trip with the value it went in with.
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <param name="expectedWindowMenu">Payload field 3 [assert.srf:L66].</param>
    /// <param name="expectedObject">Payload field 4 [assert.srf:L67].</param>
    /// <param name="expectedObjectEvent">Payload field 5 [assert.srf:L68].</param>
    /// <param name="expectedLine">Payload field 6, after the consumer parses it back [pfw.sra:L123].</param>
    /// <remarks>
    /// <para>
    /// Each of the five frame-derived values is asserted TWICE and the two assertions catch different
    /// faults, which is why neither is redundant:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// against the INDEPENDENT expectation from the matrix, which catches the producer's parse
    /// changing;
    /// </description></item>
    /// <item><description>
    /// against the PRODUCER'S OWN member, which catches the field ORDER changing on either side. Swap
    /// fields 3 and 4 anywhere in the protocol and the decoded window menu stops equalling the
    /// produced one, on exactly the two rows where they differ.
    /// </description></item>
    /// </list>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepCallerFrameMatrix))]
    public void EveryFieldOfADeepPayloadSurvivesTheRoundTripWithTheValueItWentInWith(
        string callerFrame,
        string expectedWindowMenu,
        string expectedObject,
        string expectedObjectEvent,
        long expectedLine)
    {
        string[] frames = LegacyStackFrames.Deep(callerFrame);
        AssertionFailure failure = Produce(frames, string.Empty);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        // The splitter saw exactly seven fields and the full branch fired [pfw.sra:L116,L119].
        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.NumberAndTextDecoded);
        Assert.True(decoded.SevenFieldBranchTaken);

        // Field 1: written as text [assert.srf:L22], parsed back to a number [pfw.sra:L117].
        Assert.Equal(MagicNumber, decoded.Number);

        // Field 2: the bare text, because this theory supplies no info. NOT the location suffix -
        // that reaches Info alone [assert.srf:L71], and Section 2 pins the divergence.
        Assert.Equal(BareAssertionText, decoded.Text);

        // Fields 3 to 6: the frame parse, checked against the matrix and against the producer.
        Assert.Equal(expectedWindowMenu, decoded.WindowMenu);
        Assert.Equal(failure.WindowMenu, decoded.WindowMenu);

        Assert.Equal(expectedObject, decoded.Object);
        Assert.Equal(failure.Object, decoded.Object);

        Assert.Equal(expectedObjectEvent, decoded.ObjectEvent);
        Assert.Equal(failure.ObjectEvent, decoded.ObjectEvent);

        Assert.Equal(expectedLine, decoded.Line);
        Assert.Equal(failure.Line, decoded.Line);

        // Field 7: the trimmed frames joined by a BARE LINE FEED [assert.srf:L61-L64]. The expected
        // value comes from the fixture's single definition of the trim, so this suite and the
        // trimming suite cannot disagree about what "trimmed" means.
        Assert.Equal(LegacyStackFrames.ExpectedStackTraceInfo(frames), decoded.StackTrace);
        Assert.Equal(failure.StackTraceInfo, decoded.StackTrace);
    }

    /// <summary>
    /// The round-tripped stack text keeps exactly two fewer lines than the frame count, at every
    /// depth.
    /// </summary>
    /// <param name="frameCount">The number of frames the producer is given.</param>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ASSERTION THAT CATCHES A DELIMITER MISTAKE.</b> Field 7 contains BARE LINE FEEDS
    /// [assert.srf:L63] and the fields are delimited by a CARRIAGE RETURN AND LINE FEED PAIR
    /// [assert.srf:L19]. Confuse the two in either direction and the line count moves: a pair used
    /// inside field 7 makes the consumer see more than seven fields and lose the whole trace, and a
    /// bare feed used as the delimiter makes it see one field and decode nothing at all. Counting the
    /// lines after the round trip detects both.
    /// </para>
    /// <para>
    /// The count is compared against <c>frameCount</c> minus
    /// <see cref="LegacyStackFrames.MaxShallowFrameCount"/> rather than against a literal two, because
    /// the two frames the trim removes are exactly the two the shape gate has no use for
    /// [assert.srf:L37,L61] - one number, one meaning.
    /// </para>
    /// <para>
    /// The last line is then asserted BY VALUE against
    /// <see cref="LegacyStackFrames.DepthFrame(int)"/>, not by index: the innermost surviving frame is
    /// the one the producer also blames, so naming it by value ties the trim and the selection
    /// together without this test re-deriving either position (AAP 0.4.5.4).
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepFrameCounts))]
    public void TheRoundTrippedStackTextKeepsExactlyTwoFewerLinesThanTheFrameCount(int frameCount)
    {
        string[] frames = LegacyStackFrames.WithDepth(frameCount);
        AssertionFailure failure = Produce(frames, string.Empty);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.SevenFieldBranchTaken);

        string[] lines = decoded.StackTrace.Split(
            LegacyStackFrames.FrameSeparator, StringSplitOptions.None);

        Assert.Equal(frameCount - LegacyStackFrames.MaxShallowFrameCount, lines.Length);
        Assert.Equal(LegacyStackFrames.ExpectedTrimmedStack(frames), lines);

        // The innermost surviving line is the frame the payload blames [assert.srf:L38], named by
        // value. Its position is deliberately not recomputed here.
        Assert.Equal(
            LegacyStackFrames.DepthFrame(frameCount - LegacyStackFrames.MaxShallowFrameCount),
            lines[^1]);

        // Neither framework frame survived the trim [assert.srf:L61].
        Assert.DoesNotContain(LegacyStackFrames.AssertGuardFrame, lines);
        Assert.DoesNotContain(LegacyStackFrames.PayloadBuilderFrame, lines);
    }

    /// <summary>
    /// The oracle's own call chain round-trips to the frame the oracle itself blames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="LegacyStackFrames.OracleChain"/> is the one fixture whose ORDER is a fact rather
    /// than a convention: it is what pressing the oracle window's first button produces
    /// [w_test_assert.srw:L137, L39, assert.srf:L91, L30]. Running the round trip over it is the
    /// closest this suite can get to the behavioural oracle without the PowerBuilder runtime, which
    /// is unavailable here - the legacy binaries are closed and AAP 0.6.7 records the oracle as the
    /// legacy tree itself.
    /// </para>
    /// <para>
    /// Its blamed frame is the WINDOW FUNCTION, so the chain's own default outcome is the
    /// window-equals-object shape whose window line the consumer suppresses - even though the chain
    /// also CONTAINS a two-dot frame, which is merely copied into the trace. That is a genuinely
    /// counter-intuitive property of the legacy and it is asserted rather than described.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOracleCallChainRoundTripsToTheFrameTheOracleItselfBlames()
    {
        string[] frames = LegacyStackFrames.OracleChain();
        AssertionFailure failure = Produce(frames, OracleInfo);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.SevenFieldBranchTaken);

        Assert.Equal(MagicNumber, decoded.Number);
        Assert.Equal(ExpectedFieldTwo(OracleInfo), decoded.Text);

        // The blamed frame is w_test_assert.wf_testassert [w_test_assert.srw:L39], which carries one
        // dot - so window and object arrive as the same string [assert.srf:L51].
        Assert.Equal("w_test_assert", decoded.WindowMenu);
        Assert.Equal(decoded.WindowMenu, decoded.Object);
        Assert.Equal("wf_testassert", decoded.ObjectEvent);
        Assert.Equal(39L, decoded.Line);

        // The two-dot frame is present in the TRACE, having been copied rather than parsed.
        Assert.Equal(LegacyStackFrames.ExpectedStackTraceInfo(frames), decoded.StackTrace);
        Assert.Contains(LegacyStackFrames.TwoDotFrame, decoded.StackTrace, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  2. FIELD 2 EXACTLY - INCLUDING ITS EMBEDDED LINE FEED, EXCLUDING THE LOCATION SUFFIX
    // ==============================================================================================

    /// <summary>
    /// Info strings whose round trip must leave payload field 2 byte-identical.
    /// </summary>
    /// <remarks>
    /// Four rows, each chosen for a distinct reason: the empty string is the info-less arity
    /// [assert.srf:L91]; the oracle's own string is the real scenario [w_test_assert.srw:L42];
    /// whitespace-only proves the legacy's test really is against the empty string and not against
    /// whitespace [assert.srf:L25]; and the multi-line row is the important one - a BARE LINE FEED
    /// inside field 2 must not be mistaken for a field boundary by the consumer's splitter.
    /// </remarks>
    public static TheoryData<string> InfoStrings =>
        new(
            string.Empty,
            OracleInfo,
            "  ",
            "first" + InfoSeparator + "second");

    /// <summary>
    /// The decoded text is payload field 2 exactly, and never carries the source-location suffix.
    /// </summary>
    /// <param name="info">The info string handed to the producer.</param>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT D4 OBSERVED ACROSS THE BOUNDARY (C-B).</b> The producer copies field 2 into
    /// <see cref="AssertionFailure.Info"/> [assert.srf:L35] and only afterwards appends the location
    /// suffix to <c>Info</c> ALONE [assert.srf:L71]. Field 2 is never re-read, so in the deep shape the
    /// two DIVERGE: the oracle displays <c>Info</c>, with the suffix [w_test_assert.srw:L100], while
    /// the consumer assigns its error text from field 2, without it [pfw.sra:L118]. This theory pins
    /// both sides of that divergence in one place, and the divergence is deliberate legacy behaviour -
    /// building field 2 from <c>Info</c> would make the consumer report the location twice and is the
    /// single easiest way to get the producer wrong.
    /// </para>
    /// <para>
    /// The suffix is reconstructed from the producer's own members rather than from a literal, so the
    /// assertion states the RELATIONSHIP between <c>Info</c> and field 2 rather than restating the
    /// suffix's format, which belongs to the payload suite.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(InfoStrings))]
    public void TheDecodedTextIsPayloadFieldTwoExactlyAndNeverCarriesTheLocationSuffix(string info)
    {
        string[] frames = LegacyStackFrames.Deep();
        AssertionFailure failure = Produce(frames, info);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        // A bare line feed inside field 2 is NOT a field boundary: the count is still seven.
        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.SevenFieldBranchTaken);

        Assert.Equal(ExpectedFieldTwo(info), decoded.Text);

        // The suffix never reaches the decoded text, and neither does the field delimiter.
        Assert.DoesNotContain(LocationSuffixOpening, decoded.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            LegacySystemErrorConsumer.FieldDelimiter, decoded.Text, StringComparison.Ordinal);

        // D4: Info is strictly the decoded text plus the suffix, so the two are NOT interchangeable.
        string expectedInfo = decoded.Text +
                              LocationSuffixOpening +
                              failure.Object +
                              LocationSuffixSeparator +
                              failure.ObjectEvent +
                              "(" +
                              failure.Line.ToString(CultureInfo.InvariantCulture) +
                              ")";

        Assert.Equal(expectedInfo, failure.Info);
        Assert.NotEqual(failure.Info, decoded.Text);
    }

    /// <summary>
    /// An info string that carries the FIELD DELIMITER collapses the deep payload at the consumer,
    /// silently costing it all five frame-derived values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DELIBERATE LEGACY BEHAVIOUR (C-B). THIS IS NOT A BUG TO FIX HERE.</b> The producer validates
    /// nothing about info - its only test is against the empty string [assert.srf:L25] - so a caller
    /// who puts a carriage-return pair in it splits field 2 in two, and the consumer then sees EIGHT
    /// fields. Because the consumer's test is an EQUALITY rather than a minimum [pfw.sra:L119], the
    /// full branch does not fire and the window, object, event, line number and trace are all lost
    /// while the decode still reports success for the number and the text.
    /// </para>
    /// <para>
    /// It is asserted here for two reasons. First, it is legacy behaviour and C-B requires it be
    /// reproduced rather than corrected - neither by rejecting such an info string, nor by escaping
    /// it, nor by widening the consumer's test. Second, it makes the eight-field case of Section 4
    /// REACHABLE FROM THE REAL PRODUCER rather than only from a hand-built payload, which is what
    /// makes preserved defect D2 a live property of the shipping system instead of a curiosity.
    /// </para>
    /// <para>
    /// The decoded text is the part of field 2 BEFORE the delimiter, which is the mechanical proof
    /// that the field was split rather than merely miscounted.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnInfoStringCarryingTheFieldDelimiterCollapsesTheDeepPayloadAtTheConsumer()
    {
        const string Head = "before";
        const string Tail = "after";

        string[] frames = LegacyStackFrames.Deep();
        AssertionFailure failure =
            Produce(frames, Head + LegacySystemErrorConsumer.FieldDelimiter + Tail);

        LegacySystemErrorState incoming =
            LegacySystemErrorState.FromAssertPayload(failure.Message);
        LegacyDecodedSystemError decoded = LegacySystemErrorConsumer.Decode(incoming);

        // EIGHT fields, one more than the protocol has, so the equality test at pfw.sra:L119 fails.
        Assert.Equal(DeepFieldCount + 1, decoded.SegmentCount);
        Assert.True(decoded.NumberAndTextDecoded);
        Assert.False(decoded.SevenFieldBranchTaken);

        // The number and the text still decode, and the text is field 2 as the SPLIT left it.
        Assert.Equal(MagicNumber, decoded.Number);
        Assert.Equal(BareAssertionText + InfoSeparator + Head, decoded.Text);
        Assert.DoesNotContain(Tail, decoded.Text, StringComparison.Ordinal);

        // All five frame-derived values are lost: they pass through as the incoming state's, even
        // though the producer computed them correctly and put them on the wire.
        Assert.Equal(incoming.WindowMenu, decoded.WindowMenu);
        Assert.Equal(incoming.Object, decoded.Object);
        Assert.Equal(incoming.ObjectEvent, decoded.ObjectEvent);
        Assert.Equal(incoming.Line, decoded.Line);
        Assert.Equal(string.Empty, decoded.StackTrace);

        // The proof that the loss is the consumer's counting and not the producer's parse.
        Assert.NotEqual(string.Empty, failure.WindowMenu);
        Assert.NotEqual(string.Empty, failure.StackTraceInfo);
    }

    // ==============================================================================================
    //  3. THE TWO-FIELD ROUND TRIP - THE CORRECT PAYLOAD FOR A SHALLOW STACK
    // ==============================================================================================

    /// <summary>
    /// Every shallow frame count crossed with the info-less and info-carrying arities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three counts are the only ones the producer's shape gate treats as shallow
    /// [assert.srf:L37], and each is a real production case rather than a synthetic edge: <c>0</c> is
    /// what a SWALLOWED CAPTURE FAILURE leaves behind, because the legacy catches the capture's
    /// exception with an empty body and never reads it [assert.srf:L29-L32], and <c>1</c> and
    /// <c>2</c> are what a capture taken with nothing above the framework's own frames contains.
    /// </para>
    /// <para>
    /// Both arities are present because the shallow shape's field 2 must be complete, and a matrix
    /// that only ever passed the empty string could not show that.
    /// </para>
    /// </remarks>
    public static TheoryData<int, string> ShallowFrameCountAndInfoMatrix
    {
        get
        {
            TheoryData<int, string> matrix = [];

            for (int frameCount = 0;
                 frameCount <= LegacyStackFrames.MaxShallowFrameCount;
                 frameCount++)
            {
                matrix.Add(frameCount, string.Empty);
                matrix.Add(frameCount, OracleInfo);
            }

            return matrix;
        }
    }

    /// <summary>
    /// A shallow payload round-trips the number and the text, and leaves the five frame-derived
    /// values exactly as the incoming error carried them.
    /// </summary>
    /// <param name="frameCount">The shallow frame count [assert.srf:L37].</param>
    /// <param name="info">The info string.</param>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT D2's OTHER HALF (C-B).</b> The two-field payload is emitted SILENTLY: there
    /// is no marker, no warning and no distinguishing field to tell the consumer that five values are
    /// missing rather than empty. The consumer is written to expect exactly that - it requires two or
    /// more fields [pfw.sra:L116] and only then tests for seven - and the shape is reproduced rather
    /// than corrected.
    /// </para>
    /// <para>
    /// The five untouched values are asserted against the INCOMING STATE first, which is the precise
    /// claim - the decode never wrote them, so they are whatever arrived [pfw.sra:L120-L124 are inside
    /// the branch that did not run] - and then against their concrete values, so that a change to the
    /// incoming defaults cannot make the pass-through assertion vacuously true. Note the object name
    /// survives as the GATE VALUE, which is legacy behaviour with a visible consequence in Section 5.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShallowFrameCountAndInfoMatrix))]
    public void AShallowPayloadRoundTripsTheNumberAndTheTextAndNothingElse(int frameCount, string info)
    {
        string[] frames = LegacyStackFrames.Shallow(frameCount);
        AssertionFailure failure = Produce(frames, info);

        LegacySystemErrorState incoming =
            LegacySystemErrorState.FromAssertPayload(failure.Message);
        LegacyDecodedSystemError decoded = LegacySystemErrorConsumer.Decode(incoming);

        // TWO fields, so the two-or-more branch fires and the exactly-seven branch does not.
        Assert.Equal(ShallowFieldCount, decoded.SegmentCount);
        Assert.True(decoded.NumberAndTextDecoded);
        Assert.False(decoded.SevenFieldBranchTaken);

        // What DOES survive.
        Assert.Equal(MagicNumber, decoded.Number);
        Assert.Equal(ExpectedFieldTwo(info), decoded.Text);

        // What is never written, stated as pass-through.
        Assert.Equal(incoming.WindowMenu, decoded.WindowMenu);
        Assert.Equal(incoming.Object, decoded.Object);
        Assert.Equal(incoming.ObjectEvent, decoded.ObjectEvent);
        Assert.Equal(incoming.Line, decoded.Line);

        // The same claim against concrete values, so the pass-through above cannot be vacuous.
        Assert.Equal(string.Empty, decoded.WindowMenu);
        Assert.Equal(LegacySystemErrorConsumer.AssertObjectName, decoded.Object);
        Assert.Equal(string.Empty, decoded.ObjectEvent);
        Assert.Equal(0L, decoded.Line);

        // Field 7 never arrived, so the consumer's stack-trace local keeps its default
        // [pfw.sra:L112] - which is what suppresses the report's call-stack line [pfw.sra:L137].
        Assert.Equal(string.Empty, decoded.StackTrace);

        // Nothing was LOST in transit: the producer had nothing to send, because its shape gate never
        // reached the parse or the copy loop [assert.srf:L37].
        Assert.Equal(string.Empty, failure.WindowMenu);
        Assert.Equal(string.Empty, failure.Object);
        Assert.Equal(string.Empty, failure.ObjectEvent);
        Assert.Equal(0L, failure.Line);
        Assert.Equal(string.Empty, failure.StackTraceInfo);
        Assert.Empty(failure.StackTrace);
    }

    /// <summary>
    /// The shallow payload is the CORRECT payload for a shallow stack, not a degraded one: its text
    /// is byte-identical to the deep shape's for the same info.
    /// </summary>
    /// <param name="info">The info string, applied identically to both shapes.</param>
    /// <remarks>
    /// <para>
    /// A pointed comparison rather than a restatement. Fields 1 and 2 are assembled BEFORE the shape
    /// gate is ever consulted [assert.srf:L22-L27 precede :L37], so the shape cannot influence them,
    /// and the consumer decodes both from the same two-or-more branch [pfw.sra:L117-L118]. A port that
    /// truncated, marked or otherwise "degraded" the shallow text - by appending a note about the
    /// missing frames, say - would break this equality, and it would do so in the exact scenario a
    /// diagnostic failure produces, which is when a correct message matters most.
    /// </para>
    /// <para>
    /// Only the TEXT is compared. The numbers are equal too and are asserted, but the five
    /// frame-derived values are legitimately different between the shapes, and that difference is the
    /// subject of the theory above.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(InfoStrings))]
    public void TheShallowTextIsByteIdenticalToTheDeepTextForTheSameInfo(string info)
    {
        LegacyDecodedSystemError shallow = Decode(
            Produce(LegacyStackFrames.Shallow(LegacyStackFrames.MaxShallowFrameCount), info).Message);

        LegacyDecodedSystemError deep =
            Decode(Produce(LegacyStackFrames.Deep(), info).Message);

        Assert.Equal(deep.Text, shallow.Text);
        Assert.Equal(deep.Number, shallow.Number);
        Assert.Equal(ExpectedFieldTwo(info), shallow.Text);

        // The two shapes really were different, so the equality above is not comparing a shape with
        // itself.
        Assert.False(shallow.SevenFieldBranchTaken);
        Assert.True(deep.SevenFieldBranchTaken);
        Assert.NotEqual(deep.SegmentCount, shallow.SegmentCount);
    }

    // ==============================================================================================
    //  4. THE EXACTLY-SEVEN GATE, AND THE TRAILING-EMPTY-FIELD HAZARD
    // ==============================================================================================

    /// <summary>
    /// Field counts either side of the gate, and whether the full branch fires for each.
    /// </summary>
    /// <remarks>
    /// One below, at, and one above. These payloads are HAND BUILT rather than produced, because the
    /// producer emits only two fields or seven [assert.srf:L37] and six and eight are therefore
    /// unreachable through it - which is exactly why they have to be fed to the consumer directly.
    /// </remarks>
    public static TheoryData<int, bool> FieldCountGateMatrix
    {
        get
        {
            TheoryData<int, bool> matrix = [];

            // SIX - one short. The full branch does not fire and five values are lost.
            matrix.Add(DeepFieldCount - 1, false);

            // SEVEN - the only count that fires it [pfw.sra:L119].
            matrix.Add(DeepFieldCount, true);

            // EIGHT - one too many, and it fails for the SAME reason six does, because the test is an
            // equality rather than a minimum. Section 2 shows this count is reachable from the real
            // producer.
            matrix.Add(DeepFieldCount + 1, false);

            return matrix;
        }
    }

    /// <summary>
    /// The frame shapes whose parse leaves an EMPTY field in the middle of the payload.
    /// </summary>
    /// <remarks>
    /// The dotless frame leaves fields 3 and 4 empty, because window and object are assigned only
    /// inside the dot guard [assert.srf:L40]; the no-space frame leaves field 5 empty, because the
    /// event window computes to a negative length [assert.srf:L55-L56]. Both still write seven fields.
    /// </remarks>
    public static TheoryData<string> EmptyMiddleFieldFrameShapes =>
        new(LegacyStackFrames.DotlessFrame, LegacyStackFrames.NoSpaceAfterMethodFrame);

    /// <summary>
    /// The decode fires its full branch at EXACTLY seven fields, and at no other count.
    /// </summary>
    /// <param name="fieldCount">How many fields the hand-built payload carries.</param>
    /// <param name="expectedBranchTaken">Whether the exactly-seven branch should fire.</param>
    /// <remarks>
    /// <para>
    /// <b>DELIBERATE LEGACY BEHAVIOUR (C-B). THE TEST IS AN EQUALITY, NOT A MINIMUM</b>
    /// [pfw.sra:L119]. The consequence is asymmetric and worth stating plainly: a producer that ever
    /// grew an EIGHTH field would not fail, it would silently stop populating the window, the object,
    /// the event, the line number and the whole call stack, while still reporting a decoded number and
    /// a decoded text. Widening the test to seven-or-more would not fix that - it would read fields 3
    /// to 7 out of an eight-field payload whose layout it no longer matches - so the equality is
    /// preserved exactly as written.
    /// </para>
    /// <para>
    /// The branch's effect is asserted in both directions: when it fires, all five values come from
    /// the payload; when it does not, all five pass through from the incoming state. Asserting only
    /// the flag would let a decode that set the flag correctly and the values wrongly pass.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FieldCountGateMatrix))]
    public void TheDecodeFiresItsFullBranchOnlyAtExactlySevenFields(
        int fieldCount, bool expectedBranchTaken)
    {
        string payload = HandBuiltPayload(fieldCount);

        LegacySystemErrorState incoming = LegacySystemErrorState.FromAssertPayload(payload);
        LegacyDecodedSystemError decoded = LegacySystemErrorConsumer.Decode(incoming);

        // The splitter counted what was written: no field was dropped, because none of these payloads
        // ends in an empty field. That is the hazard the next two tests are about.
        Assert.Equal(fieldCount, decoded.SegmentCount);

        // Every count here is at least two, so the number and the text always decode [pfw.sra:L116].
        Assert.True(decoded.NumberAndTextDecoded);
        Assert.Equal(MagicNumber, decoded.Number);
        Assert.Equal(BareAssertionText, decoded.Text);

        Assert.Equal(expectedBranchTaken, decoded.SevenFieldBranchTaken);

        if (expectedBranchTaken)
        {
            Assert.Equal(HandBuiltWindowMenu, decoded.WindowMenu);
            Assert.Equal(HandBuiltObject, decoded.Object);
            Assert.Equal(HandBuiltObjectEvent, decoded.ObjectEvent);
            Assert.Equal(HandBuiltLine, decoded.Line);
            Assert.Equal(HandBuiltStackTrace, decoded.StackTrace);
        }
        else
        {
            Assert.Equal(incoming.WindowMenu, decoded.WindowMenu);
            Assert.Equal(incoming.Object, decoded.Object);
            Assert.Equal(incoming.ObjectEvent, decoded.ObjectEvent);
            Assert.Equal(incoming.Line, decoded.Line);
            Assert.Equal(string.Empty, decoded.StackTrace);

            // The values really were on the wire - the payload carries them - so the loss is the
            // count test's doing and nothing else's.
            Assert.Contains(HandBuiltWindowMenu, payload, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A payload with seven fields written but an EMPTY seventh arrives as six segments, so the full
    /// branch never fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT D1 (C-B). THE SPLITTER DROPS A TRAILING EMPTY FIELD.</b> Its loop appends
    /// every segment that PRECEDES a delimiter, empty ones included [pfw.sra:L58-L63], but the segment
    /// that FOLLOWS the last delimiter is appended only when it is non-empty [pfw.sra:L64-L66]. So a
    /// payload ending in a delimiter loses exactly one field - the empty one at the tail - and a
    /// seven-field payload becomes a six-segment list that the exactly-seven test then rejects.
    /// </para>
    /// <para>
    /// The payload is built as SIX fields plus a trailing delimiter, which is precisely what "seven
    /// fields written, the seventh empty" means, and it needs no index into a field list to express.
    /// The behaviour is reproduced rather than corrected: the field count the consumer sees IS the
    /// observable contract, so appending the empty tail segment would change what the framework
    /// reports for every payload that ends in a delimiter.
    /// </para>
    /// </remarks>
    [Fact]
    public void APayloadWhoseSeventhFieldIsEmptyArrivesAsSixSegments()
    {
        string payload = HandBuiltPayload(DeepFieldCount - 1) +
                         LegacySystemErrorConsumer.FieldDelimiter;

        // The delimiter really is there, so the payload does carry a seventh field position.
        Assert.EndsWith(
            LegacySystemErrorConsumer.FieldDelimiter, payload, StringComparison.Ordinal);

        Assert.Equal(DeepFieldCount - 1, SplitFields(payload).Count);

        LegacyDecodedSystemError decoded = Decode(payload);

        Assert.Equal(DeepFieldCount - 1, decoded.SegmentCount);
        Assert.False(decoded.SevenFieldBranchTaken);
    }

    /// <summary>
    /// The real producer never emits a payload whose seventh field is empty, so the deep shape always
    /// survives the splitter intact.
    /// </summary>
    /// <param name="callerFrame">The frame the producer will blame.</param>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ONE ASSERTION THAT TIES THE PRODUCER'S COPY LOOP TO THE CONSUMER'S SPLITTER.</b>
    /// When the deep branch is taken the copy loop runs at least once - its bound is
    /// <c>1</c> to <c>nCount - 2</c> and the branch requires <c>nCount &gt; 2</c>
    /// [assert.srf:L37,L61-L64] - so field 7 receives at least one frame, and every frame the capture
    /// provider renders is non-empty because its format guarantees a scope, a dot, a member and a line
    /// introducer. Field 7 is therefore non-empty by construction, the payload never ends in a
    /// delimiter, and defect D1 cannot reach it.
    /// </para>
    /// <para>
    /// <b>AND THE OTHER HALF, STATED HONESTLY (C-K): THE HAZARD IS REAL AND THE SAFETY RESTS ON TWO
    /// FACTS, NOT ONE.</b> The copy loop guarantees a frame is WRITTEN; it does not guarantee that
    /// frame is non-empty. The next test shows what happens when that second fact is removed. So any
    /// future change that could leave field 7 empty - a provider that emitted an empty frame text, a
    /// trim that could remove every frame, a filter over the copied frames - would silently take the
    /// consumer from seven segments to six and cost it all five frame-derived values, with no error
    /// anywhere. Do not "fix" the splitter to compensate; keep field 7 non-empty.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeepCallerFrameShapes))]
    public void TheProducerNeverEmitsAPayloadWhoseSeventhFieldIsEmpty(string callerFrame)
    {
        string[] frames = LegacyStackFrames.Deep(callerFrame);
        AssertionFailure failure = Produce(frames, string.Empty);

        // The copy loop wrote at least one frame [assert.srf:L61-L64] and what it wrote is non-empty.
        Assert.NotEmpty(failure.StackTrace);
        Assert.NotEqual(string.Empty, failure.StackTraceInfo);

        // So the payload cannot end in a delimiter, and the splitter has no trailing empty to drop.
        Assert.DoesNotContain(
            LegacySystemErrorConsumer.FieldDelimiter,
            failure.Message[^LegacySystemErrorConsumer.FieldDelimiter.Length..],
            StringComparison.Ordinal);

        Assert.Equal(DeepFieldCount, SplitFields(failure.Message).Count);
        Assert.True(Decode(failure.Message).SevenFieldBranchTaken);
    }

    /// <summary>
    /// An empty seventh field collapses a PRODUCED deep payload to six segments, which is why the
    /// non-emptiness of that field is load bearing rather than incidental.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement of the test above, and the reason its guarantee is stated as resting on two
    /// facts. Handed a frame list whose only trimmed frame is the EMPTY STRING, the producer takes its
    /// deep branch, writes all seven fields, and writes an empty field 7 - so the consumer's splitter
    /// drops it, sees six segments, and the exactly-seven branch never fires. Seven fields written,
    /// five values lost, no error raised anywhere.
    /// </para>
    /// <para>
    /// <b>THE REAL CAPTURE PROVIDER CANNOT PRODUCE THIS INPUT (C-K)</b>, and this test does not claim
    /// otherwise. Its frame format guarantees a scope, a dot, a member and a line introducer in every
    /// frame, including all of its degraded cases, so an empty frame text is unreachable through the
    /// public assertion surface. The case is constructed through the internal payload-builder seam,
    /// which accepts any frame text - the same reason the fixtures can offer a dotless frame the
    /// provider also cannot emit. What it pins is the COUPLING, which is a property of the protocol
    /// and not of the provider: it says exactly which change would break the consumer silently.
    /// </para>
    /// <para>
    /// The two framework frames come from the fixtures rather than being retyped, and the frame count
    /// passed to the seam is the array's own length, so nothing about this case is inconsistent except
    /// the one thing under test.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptySeventhFieldCollapsesAProducedDeepPayloadToSixSegments()
    {
        // Three frames, so the deep branch is taken [assert.srf:L37]; the only trimmed frame is the
        // outermost, and it is empty.
        string[] callStack =
        [
            string.Empty,                           // the blamed and copied frame - EMPTY
            LegacyStackFrames.AssertGuardFrame,     // trimmed [assert.srf:L61]
            LegacyStackFrames.PayloadBuilderFrame,  // trimmed [assert.srf:L61]
        ];

        AssertionFailure failure = Produce(callStack, string.Empty);

        // The deep branch really was taken: one frame was copied, and it is the empty one.
        Assert.Single(failure.StackTrace);
        Assert.Equal(string.Empty, failure.StackTraceInfo);
        Assert.EndsWith(
            LegacySystemErrorConsumer.FieldDelimiter, failure.Message, StringComparison.Ordinal);

        // Seven fields written, SIX segments received - preserved defect D1 reached from the producer.
        Assert.Equal(DeepFieldCount - 1, SplitFields(failure.Message).Count);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        Assert.Equal(DeepFieldCount - 1, decoded.SegmentCount);
        Assert.False(decoded.SevenFieldBranchTaken);
        Assert.True(decoded.NumberAndTextDecoded);
    }

    /// <summary>
    /// An empty field in the MIDDLE of the payload survives, so the deep shape still decodes.
    /// </summary>
    /// <param name="callerFrame">A frame whose parse leaves an interior field empty.</param>
    /// <remarks>
    /// <para>
    /// The precise scope of defect D1: the splitter appends intermediate empty segments
    /// UNCONDITIONALLY [pfw.sra:L58-L63] and tests only the segment after the LAST delimiter
    /// [pfw.sra:L64-L66], so the drop is confined to the tail. A payload whose window/menu field is
    /// empty therefore still arrives as seven segments and still decodes in full.
    /// </para>
    /// <para>
    /// Both rows are REAL producer output rather than hand-built payloads, which is the point of using
    /// them: the empty interior field is what the legacy parse itself produces for a frame with no dot
    /// or no space, so this is the shape the shipping system actually has to survive.
    /// </para>
    /// <para>
    /// The empty field is detected as TWO ADJACENT DELIMITERS rather than by subscripting a field list,
    /// so this test writes no index (AAP 0.4.5.4).
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EmptyMiddleFieldFrameShapes))]
    public void AnEmptyFieldInTheMiddleOfThePayloadSurvivesTheRoundTrip(string callerFrame)
    {
        string[] frames = LegacyStackFrames.Deep(callerFrame);
        AssertionFailure failure = Produce(frames, string.Empty);

        string adjacentDelimiters = LegacySystemErrorConsumer.FieldDelimiter +
                                   LegacySystemErrorConsumer.FieldDelimiter;

        // Two delimiters with nothing between them IS an empty field, and there is one.
        Assert.Contains(adjacentDelimiters, failure.Message, StringComparison.Ordinal);

        // It is not at the tail, so D1 has nothing to drop.
        Assert.DoesNotContain(
            LegacySystemErrorConsumer.FieldDelimiter,
            failure.Message[^LegacySystemErrorConsumer.FieldDelimiter.Length..],
            StringComparison.Ordinal);

        LegacyDecodedSystemError decoded = Decode(failure.Message);

        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.SevenFieldBranchTaken);
        Assert.Equal(LegacyStackFrames.ExpectedStackTraceInfo(frames), decoded.StackTrace);
    }

    /// <summary>
    /// An empty middle field does not shift the fields after it: everything still lands where the
    /// consumer expects.
    /// </summary>
    /// <remarks>
    /// The hand-built control for the theory above, and it makes a stronger claim than "the count is
    /// still seven": with field 3 empty, field 4 must still decode to the OBJECT and field 6 must still
    /// decode to the line number. A splitter that skipped empty segments instead of appending them
    /// would keep a plausible-looking payload and silently shift the object into the window, the event
    /// into the object, and the line number into the event - a failure a count assertion alone would
    /// not catch.
    /// </remarks>
    [Fact]
    public void AnEmptyMiddleFieldDoesNotShiftTheFieldsAfterIt()
    {
        // Appended in payload order, so no index arithmetic appears (AAP 0.4.5.4).
        List<string> fields =
        [
            MagicNumberText,        // field 1
            BareAssertionText,      // field 2
            string.Empty,           // field 3 - EMPTY, the window/menu
            HandBuiltObject,        // field 4
            HandBuiltObjectEvent,   // field 5
            HandBuiltLineText,      // field 6
            HandBuiltStackTrace,    // field 7 - NON-EMPTY, which is what keeps the count at seven
        ];

        string payload = string.Join(LegacySystemErrorConsumer.FieldDelimiter, fields);

        Assert.Equal(DeepFieldCount, SplitFields(payload).Count);

        LegacyDecodedSystemError decoded = Decode(payload);

        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.SevenFieldBranchTaken);

        Assert.Equal(string.Empty, decoded.WindowMenu);
        Assert.Equal(HandBuiltObject, decoded.Object);
        Assert.Equal(HandBuiltObjectEvent, decoded.ObjectEvent);
        Assert.Equal(HandBuiltLine, decoded.Line);
        Assert.Equal(HandBuiltStackTrace, decoded.StackTrace);
    }

    // ==============================================================================================
    //  5. THE REPORT-SUPPRESSION COUPLING - WHERE THE PRODUCER'S PARSE CHANGES THE REPORT
    // ==============================================================================================

    /// <summary>
    /// Frame counts and whether the assembled report should carry a call-stack section.
    /// </summary>
    /// <remarks>
    /// The three shallow counts must not carry one, because field 7 never arrived and the consumer's
    /// local is still the empty string [pfw.sra:L112,L137]. The deep counts must, because the copy loop
    /// always wrote at least one frame [assert.srf:L61-L64]. The boundary sits between the second and
    /// third row, which is why <see cref="LegacyStackFrames.MaxShallowFrameCount"/> and
    /// <see cref="LegacyStackFrames.MinimumDeepFrameCount"/> are both present as themselves rather than
    /// as literals.
    /// </remarks>
    public static TheoryData<int, bool> CallStackSectionMatrix
    {
        get
        {
            TheoryData<int, bool> matrix = [];

            matrix.Add(0, false);
            matrix.Add(LegacyStackFrames.MaxShallowFrameCount - 1, false);
            matrix.Add(LegacyStackFrames.MaxShallowFrameCount, false);
            matrix.Add(LegacyStackFrames.MinimumDeepFrameCount, true);
            matrix.Add(LegacyStackFrames.MinimumDeepFrameCount + 2, true);

            return matrix;
        }
    }

    /// <summary>
    /// The report OMITS its window line for a one-dot frame, because the producer made the window and
    /// the object the same string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE COUPLING THIS SUITE EXISTS TO CATCH.</b> Two independent legacy decisions meet here: the
    /// producer assigns the object FROM the window menu when the blamed frame carries exactly one dot
    /// [assert.srf:L49,L51], and the consumer emits its window line only when the two DIFFER
    /// [pfw.sra:L131]. Neither half means anything alone. A port that gave the object some other value
    /// for the single-dot case - a substring, an empty string, or anything "better" - would add a window
    /// line to every error report the framework produces from a non-visual object, and no test of
    /// either half in isolation would notice.
    /// </para>
    /// <para>
    /// The precondition is asserted before the conclusion, so a failure says which half moved: if the
    /// equality assertion fails the producer changed, and if only the omission fails the consumer did.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReportOmitsTheWindowLineWhenTheDecodedWindowAndObjectAreEqual()
    {
        LegacyDecodedSystemError decoded = Decode(
            Produce(LegacyStackFrames.Deep(LegacyStackFrames.SingleDotFrame), string.Empty).Message);

        // The producer's half [assert.srf:L51], observed after the round trip.
        Assert.Equal(decoded.WindowMenu, decoded.Object);

        string report = LegacySystemErrorConsumer.BuildReport(decoded);

        // The consumer's half [pfw.sra:L131].
        Assert.DoesNotContain(WindowLabel, report, StringComparison.Ordinal);

        // The object line is still there, so the omission is the window line's and not the whole block's.
        Assert.Contains(ObjectLabel + decoded.Object, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report INCLUDES its window line for a two-dot frame, because the producer split the frame
    /// into two different values.
    /// </summary>
    /// <remarks>
    /// The other side of the coupling above. The two-or-more-dots branch takes the window from before
    /// the FIRST dot and the object from between the first and the last [assert.srf:L44,L46], so the two
    /// differ and the consumer's test admits the line. Without this test the suppression above could be
    /// satisfied by a report that never emitted a window line at all.
    /// </remarks>
    [Fact]
    public void TheReportIncludesTheWindowLineWhenTheDecodedWindowAndObjectDiffer()
    {
        LegacyDecodedSystemError decoded =
            Decode(Produce(LegacyStackFrames.Deep(), string.Empty).Message);

        Assert.NotEqual(decoded.Object, decoded.WindowMenu);

        string report = LegacySystemErrorConsumer.BuildReport(decoded);

        Assert.Contains(WindowLabel + decoded.WindowMenu, report, StringComparison.Ordinal);
        Assert.Contains(ObjectLabel + decoded.Object, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report carries its call-stack section only when the round-tripped stack text is non-empty.
    /// </summary>
    /// <param name="frameCount">The frame count handed to the producer.</param>
    /// <param name="expectedCallStackSection">Whether the report should carry the section.</param>
    /// <remarks>
    /// The suppression is a plain emptiness test [pfw.sra:L137], and its practical consequence is that
    /// the SHALLOW shape yields a report with no call-stack section at all - which is the report the
    /// framework produces when its own stack capture failed [assert.srf:L29-L32]. That is legacy
    /// behaviour: no placeholder line, no "unavailable" note, nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CallStackSectionMatrix))]
    public void TheReportCarriesTheCallStackSectionOnlyWhenTheStackTextIsNonEmpty(
        int frameCount, bool expectedCallStackSection)
    {
        string[] frames = FramesFor(frameCount);

        LegacyDecodedSystemError decoded = Decode(Produce(frames, string.Empty).Message);
        string report = LegacySystemErrorConsumer.BuildReport(decoded);

        Assert.Equal(expectedCallStackSection, decoded.StackTrace != string.Empty);

        if (expectedCallStackSection)
        {
            Assert.Contains(CallStackLabel + decoded.StackTrace, report, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(CallStackLabel, report, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The whole report for a deep payload is the legacy labels, verbatim, in the legacy order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exact whole-string comparison rather than a set of containment checks, so the ORDER of the
    /// seven lines is pinned as well as their text. The labels are this file's own constants, taken
    /// from a byte dump of pfw.sra:L129-L138 and spelled independently of the consumer - AAP 0.8.2
    /// requires legacy text be preserved verbatim, and an expectation that reused the consumer's own
    /// literals would pass no matter what they said.
    /// </para>
    /// <para>
    /// The line number is rendered with the invariant culture, matching the consumer, so no hosting
    /// locale can inject a group separator into a report that a characterization recording is compared
    /// against byte for byte.
    /// </para>
    /// <para>
    /// The report is also asserted to contain no field delimiter anywhere: its own separator is a BARE
    /// LINE FEED [pfw.sra:L129-L138], and field 7 arrived carrying bare line feeds too, so a carriage
    /// return appearing here would mean the delimiter had leaked into the rendering.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeepReportIsTheLegacyLabelsVerbatimInTheLegacyOrder()
    {
        string[] frames = LegacyStackFrames.Deep();

        LegacyDecodedSystemError decoded = Decode(Produce(frames, OracleInfo).Message);
        string report = LegacySystemErrorConsumer.BuildReport(decoded);

        string expected = TypeLine +
                          MessageLabel + decoded.Text +
                          WindowLabel + decoded.WindowMenu +
                          ObjectLabel + decoded.Object +
                          FunctionLabel + decoded.ObjectEvent +
                          LineLabel + decoded.Line.ToString(CultureInfo.InvariantCulture) +
                          CallStackLabel + decoded.StackTrace;

        Assert.Equal(expected, report);

        Assert.DoesNotContain(
            LegacySystemErrorConsumer.FieldDelimiter, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole report for a shallow payload is the same labels minus the call-stack section - and it
    /// DOES carry a window line, with an empty value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DELIBERATE LEGACY BEHAVIOUR (C-B), AND IT LOOKS LIKE A BUG.</b> The shallow decode writes
    /// neither the window menu nor the object, so both keep what arrived [pfw.sra:L120-L121 are inside
    /// the branch that did not run]: the window menu is empty and the object is still the GATE VALUE
    /// that opened the decode. Those two differ, so the consumer's suppression test admits the line and
    /// the report shows an empty window followed by an object named after the assert function object
    /// itself [assert.srf:L3]. That is what the legacy renders for a shallow payload and it is asserted
    /// rather than tidied away.
    /// </para>
    /// <para>
    /// Which makes this the case a naive port would get wrong in the opposite direction from Section 5's
    /// first test: suppress the window line here and the report loses a line the legacy emits; emit it
    /// for the one-dot deep shape and the report gains one the legacy suppresses. Only both tests
    /// together fix the behaviour.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShallowReportCarriesAnEmptyWindowLineAndNoCallStackSection()
    {
        string[] frames = LegacyStackFrames.Shallow(0);

        LegacyDecodedSystemError decoded = Decode(Produce(frames, string.Empty).Message);
        string report = LegacySystemErrorConsumer.BuildReport(decoded);

        string expected = TypeLine +
                          MessageLabel + BareAssertionText +
                          WindowLabel + string.Empty +
                          ObjectLabel + LegacySystemErrorConsumer.AssertObjectName +
                          FunctionLabel + string.Empty +
                          LineLabel + "0";

        Assert.Equal(expected, report);

        Assert.Contains(WindowLabel, report, StringComparison.Ordinal);
        Assert.DoesNotContain(CallStackLabel, report, StringComparison.Ordinal);
        Assert.DoesNotContain(
            LegacySystemErrorConsumer.FieldDelimiter, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The legacy dialog title survived the migration verbatim.
    /// </summary>
    /// <remarks>
    /// The title the legacy passes to its message box [pfw.sra:L141]. It is asserted here because AAP
    /// 0.8.2 requires legacy text be preserved verbatim and because this is the only check that the
    /// Chinese literal was not mangled by an encoding change or a re-typing - the comparison is against
    /// this file's own independently spelled constant. Nothing displays it: a headless service raises no
    /// dialog, and only the report crosses into the consumer's termination seam.
    /// </remarks>
    [Fact]
    public void TheLegacyDialogTitleSurvivedTheMigrationVerbatim()
    {
        Assert.Equal(LegacyDialogTitle, LegacySystemErrorConsumer.DialogTitle);
    }

    // ==============================================================================================
    //  6. THE OBJECT-NAME GATE
    // ==============================================================================================

    /// <summary>
    /// Object names that must NOT open the decode.
    /// </summary>
    /// <remarks>
    /// Six rows. One is an ordinary unrelated error type, three probe the comparison's exactness - two
    /// case variants and a trailing space - one is the empty string, and one is a name that merely
    /// starts with the marker. PowerScript's <c>=</c> on strings is ordinal and case-sensitive and
    /// performs no trimming [pfw.sra:L114], so every row must pass through undecoded.
    /// </remarks>
    public static TheoryData<string> NonMarkerObjectNames =>
        new("runtimeerror", "Assert", "ASSERT", "", "assertion", "assert ");

    /// <summary>
    /// An error whose object name is not the assert marker is left entirely undecoded.
    /// </summary>
    /// <param name="objectName">The incoming <c>Error.Object</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>THE GATE IS PART OF THE CONTRACT, NOT AN OPTIMISATION</b> [pfw.sra:L114]. An unrelated system
    /// error must not be reinterpreted as an assert payload: its error text is a human message, not a
    /// delimited field list, and decoding it would overwrite a real error number with whatever leading
    /// characters happened to parse as digits and would replace the runtime's own window, object, event
    /// and line number with garbage. So the splitter is never even called - which is why the segment
    /// count reports zero rather than one, the count local keeping its PowerScript default
    /// [pfw.sra:L111].
    /// </para>
    /// <para>
    /// The payload used is one that WOULD decode in full, so the only reason nothing decodes is the
    /// gate. Every incoming member is a distinctive non-default value, so a pass-through is
    /// distinguishable from a decode that happened to write the same thing.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NonMarkerObjectNames))]
    public void AnErrorWhoseObjectNameIsNotTheAssertMarkerIsLeftEntirelyUndecoded(string objectName)
    {
        string payload = HandBuiltPayload(DeepFieldCount);

        // Guard the premise: this name really is not the marker, so the row tests what it claims to.
        Assert.NotEqual(LegacySystemErrorConsumer.AssertObjectName, objectName);

        LegacySystemErrorState incoming = new(
            IncomingNumber,
            payload,
            IncomingWindowMenu,
            objectName,
            IncomingObjectEvent,
            IncomingLine);

        LegacyDecodedSystemError decoded = LegacySystemErrorConsumer.Decode(incoming);

        // The splitter was never called, so neither branch fired.
        Assert.Equal(0, decoded.SegmentCount);
        Assert.False(decoded.NumberAndTextDecoded);
        Assert.False(decoded.SevenFieldBranchTaken);

        // Every field arrives as it left, and the text is still the WHOLE undecoded payload.
        Assert.Equal(IncomingNumber, decoded.Number);
        Assert.Equal(payload, decoded.Text);
        Assert.Equal(IncomingWindowMenu, decoded.WindowMenu);
        Assert.Equal(objectName, decoded.Object);
        Assert.Equal(IncomingObjectEvent, decoded.ObjectEvent);
        Assert.Equal(IncomingLine, decoded.Line);
        Assert.Equal(string.Empty, decoded.StackTrace);
    }

    /// <summary>
    /// The very same payload and the very same incoming state decode in full once the object name is
    /// exactly the assert marker.
    /// </summary>
    /// <remarks>
    /// The positive control for the gate theory, and it is what makes that theory meaningful: without
    /// it, a decode that never decoded anything at all would satisfy every row. The incoming members
    /// are the same distinctive non-default values, so this test also shows that a deep payload
    /// OVERWRITES all four of them - including <c>Error.Object</c> itself, so the value that opened the
    /// gate is replaced by the object the blamed frame names [pfw.sra:L121].
    /// </remarks>
    [Fact]
    public void TheSamePayloadDecodesInFullWhenTheObjectNameIsExactlyTheAssertMarker()
    {
        string payload = HandBuiltPayload(DeepFieldCount);

        LegacySystemErrorState incoming = new(
            IncomingNumber,
            payload,
            IncomingWindowMenu,
            LegacySystemErrorConsumer.AssertObjectName,
            IncomingObjectEvent,
            IncomingLine);

        LegacyDecodedSystemError decoded = LegacySystemErrorConsumer.Decode(incoming);

        Assert.Equal(DeepFieldCount, decoded.SegmentCount);
        Assert.True(decoded.NumberAndTextDecoded);
        Assert.True(decoded.SevenFieldBranchTaken);

        Assert.Equal(MagicNumber, decoded.Number);
        Assert.Equal(BareAssertionText, decoded.Text);
        Assert.Equal(HandBuiltWindowMenu, decoded.WindowMenu);
        Assert.Equal(HandBuiltObject, decoded.Object);
        Assert.Equal(HandBuiltObjectEvent, decoded.ObjectEvent);
        Assert.Equal(HandBuiltLine, decoded.Line);
        Assert.Equal(HandBuiltStackTrace, decoded.StackTrace);

        // Every incoming value was replaced, including the gate value itself.
        Assert.NotEqual(incoming.Number, decoded.Number);
        Assert.NotEqual(incoming.Text, decoded.Text);
        Assert.NotEqual(incoming.WindowMenu, decoded.WindowMenu);
        Assert.NotEqual(incoming.Object, decoded.Object);
        Assert.NotEqual(incoming.ObjectEvent, decoded.ObjectEvent);
        Assert.NotEqual(incoming.Line, decoded.Line);
    }
}
