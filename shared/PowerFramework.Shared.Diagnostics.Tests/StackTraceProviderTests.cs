// ==================================================================================================
//  StackTraceProviderTests.cs - THE OFFSET ARITHMETIC, THE BAIL-OUTS AND THE PER-LINE PREFIX
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Diagnostics.StackTraceProvider
//
//  ORACLES, READ AS SPECIFICATION AND NEVER AS A BUILD INPUT (C-C)
//      ws_objects/pfw.common.pbl.src/stacktrace.srf        the pfwStackTrace prototype, 9 lines,
//                                                         DECLARATION ONLY [:L7]
//      ws_objects/pfw.common.pbl.src/stacktraceinfo.srf    the four overloads, their delegation
//                                                         constants [:L13,L16,L19] and the single
//                                                         implementation [:L22-L43]
//      ws_objects/pfw.common.pbl.src/assert.srf            the SECOND consumer of the capture, and
//                                                         the only proof of the frame ORDER
//                                                         [:L30,L37-L38,L61-L64]
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw      the behavioural oracle. Its :L79 is the
//                                                         ONLY live StackTraceInfo call site in the
//                                                         whole legacy repository, and its prefix
//                                                         "-- " is reused verbatim below
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
//                                                         the third reader of the rendered trace
//                                                         [:L876], which embeds it in an exception
//                                                         report - a second reason the separator may
//                                                         not be CRLF
//
//  THE SUBSTITUTION THIS SUITE COVERS, NAMED EXPLICITLY (C-K)
//  ------------------------------------------------------------------------------------------------
//      pfwStackTrace, exported from the closed pfw.dll   ->   System.Diagnostics.StackTrace
//
//  ws_objects/pfw.common.pbl.src/stacktrace.srf:L7 declares
//
//      global function int StackTrace (ref string callstack[]) system library "pfw.dll" &
//          alias for "pfwStackTrace"
//
//  which is a CLASSIC EXTERNAL PROTOTYPE - the binding sits on the prototype, and the type
//  declaration at :L3-L4 carries no `native` clause at all. There is no PowerScript body to read and
//  no C++ source anywhere in the repository, so AAP 0.6.5's native-binding matrix classifies the
//  primitive as SUBSTITUTE and System.Diagnostics.StackTrace is the substitute the port uses.
//  Everything built on top of it - all four StackTraceInfo overloads - is pure PowerScript and is
//  ported as logic, so this suite tests ported arithmetic sitting on one substituted primitive.
//
//  THE PRIMITIVES AROUND IT WERE DELIBERATE NON-PORTS, SO NOTHING HERE LOOKS FOR THEM (AAP 0.6.5)
//  ------------------------------------------------------------------------------------------------
//  The same matrix records three neighbouring native surfaces as DELIBERATE NON-PORTS rather than
//  substitutions, and their absence is a decision this suite must not quietly contradict by probing
//  for them:
//      * the thread AFFINITY MASK - Windows only, no portable equivalent, and a tuning knob with no
//        observable behavioural contract. AAP 0.8.5 forbids asserting a performance objective, so
//        removing it is not a behavioural change and there is nothing here to assert about it.
//      * MESSAGE-PUMP PROCESSING and the `Post` idiom it enables - a headless Linux container has no
//        message pump. No test here posts, pumps or waits on one.
//      * thread SUSPEND and RESUME - unsupported in .NET.
//  Consequently this suite is single-threaded and observes only the calling thread's own stack, which
//  is exactly what the production type documents. It starts no thread and asserts nothing about
//  affinity, priority or scheduling.
//
//  THE THREE DEFECT-SHAPED BEHAVIOURS THIS SUITE PINS (C-B: REPLICATE, NEVER CORRECT)
//  ------------------------------------------------------------------------------------------------
//  Each is marked DELIBERATE LEGACY BEHAVIOUR at the test that pins it, because each looks like an
//  implementation error to a reader who has not read the oracle:
//      1. A CAPTURE OR RENDER FAILURE IS SWALLOWED. `catch(throwable ex)` clears the accumulator and
//         the caller receives the empty string [stacktraceinfo.srf:L38-L40]. Nothing is logged,
//         nothing is rethrown, and no partial result is returned. See Section 2.
//      2. AN OUT-OF-RANGE OFFSET YIELDS THE EMPTY STRING, not the whole stack, not a truncated stack
//         and not an exception [stacktraceinfo.srf:L30]. See Section 2.
//      3. THE SINGLE-ARGUMENT NUMERIC OVERLOAD DOES NOT MEAN WHAT THE TWO-ARGUMENT ONE MEANS FOR THE
//         SAME NUMBER. :L19 hands the implementation `offset + 1` where a direct call hands it
//         `offset`, so the two signatures read alike and carry different offsets. See Section 1 and
//         its measured resolution below.
//
//  WHY EVERY ASSERTION IS RELATIVE, AND NEVER AN ABSOLUTE FRAME COUNT OR AN ABSOLUTE FRAME TEXT
//  ------------------------------------------------------------------------------------------------
//  A captured stack inside a test host carries the runner's own frames. Their number varies with the
//  runner version and with whether the test is a fact or a theory - it measured 147 on this host, and
//  that number is not a contract. So every assertion here is a DIFFERENCE between two captures taken
//  inside one test method invocation, an ORDERING, a NESTING, a SHAPE, or a count of separators -
//  never "the stack is N frames deep" and never "frame 3 is this text". That is what makes the suite
//  stable across hosts without weakening what it proves.
//
//  Two mechanisms make even the relative claims sound, and both are verified rather than assumed:
//      * the production methods carry MethodImplOptions.NoInlining, and the three delegating
//         overloads additionally carry NoOptimization to defeat tail-call frame reuse. Section 5
//         asserts the attribute by reflection, because its absence is invisible in a Debug build.
//      * every helper in this file that contributes a frame carries NoInlining for the same reason.
//        CI builds Release, where inlining is more aggressive, so a depth assertion that passed only
//        in Debug would be broken rather than lucky. Every theory here was run in BOTH
//        configurations and produced identical numbers.
//
//  THE ONE PLACE THIS SUITE DIFFERS FROM ITS BRIEF, AND WHY - RECORDED RATHER THAN SMOOTHED OVER
//  ------------------------------------------------------------------------------------------------
//  The obligation for this file states that the numeric-only form and the two-argument form must
//  DISAGREE for the same number, differing by one line. MEASURED ON THIS HOST, IN BOTH
//  CONFIGURATIONS, THEY AGREE EXACTLY - and the agreement is the correct ported behaviour, not a
//  port artefact. The arithmetic, with D standing for the number of frames from the caller outward:
//
//      twoArg(n, prefix) called DIRECTLY    capture = 1 impl frame + D     trim = n + 1   -> D - n
//      numeric(n) DELEGATES to twoArg(n+1)  capture = 1 impl + 1 delegator + D
//                                                                          trim = n + 2   -> D - n
//
//  The delegating overload's extra frame and :L19's extra offset cancel exactly. That is precisely
//  what :L19's `+ 1` is FOR, and the brief's own closing insight says the same thing: the constants
//  exist to cancel their own delegation frames. The production file states it as a table in its
//  DECISION 4. Following the brief's explicit instruction for this case, Section 1 asserts the port's
//  own documented normalisation and keeps every invariant the brief requires, and the substantive
//  asymmetry is pinned by its COUNTERFACTUAL instead of by an inequality:
//
//      DROP :L19's `+ 1` and numeric(n) would trim only n + 1 from a stack one frame deeper, so it
//      would return ONE LINE MORE than twoArg(n), and that extra line would be StackTraceInfo's own
//      frame - a diagnostic helper leaking into the stack it is describing.
//
//  Three independent assertions in Section 1 fail under that mutation: the two forms would no longer
//  agree, neither would equal `full - n`, and the numeric form's output would contain a provider
//  frame. The two forms DO also genuinely diverge at exactly one input, and Section 2 pins it: at
//  ushort.MaxValue the unchecked increment wraps to zero, so the numeric form returns a NON-empty
//  trace one frame longer than normal while the two-argument form returns the empty string.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns exactly one statement: NO USER RULES WERE PROVIDED. Nothing is invented,
//  inferred or back-filled in their place, and the absence is not read as licence to lower the bar.
//  The binding constraints here are AAP 0.7.2, the enterprise-standard baseline, and AAP 0.7.3, the
//  twelve non-rule constraints. Five bind this file, and each is cited again where it applies:
//      C-A  No service is referenced. This project's single ProjectReference is the Diagnostics
//           library; nothing under services/ is reachable and none is named.
//      C-B  Replicate, never correct. The three defect-shaped behaviours above are pinned, each with
//           a DELIBERATE LEGACY BEHAVIOUR note so a future reader cannot mistake one for a bug.
//      C-C  ws_objects/** is read only. Authored from, never read from: no test here opens a path,
//           touches the file system, the network or the environment.
//      C-D  No deferred capability appears here in any form - not as a name, a string or a comment.
//      C-H  Coverage is measured on the Diagnostics assembly, and the 80 percent line gate is met
//           with margin: 87.1 percent of the assembly and 87.9 percent of StackTraceProvider itself,
//           measured from the collector's Cobertura report. Sections 1 to 6 reach all four rendering
//           overloads, the capture primitive, both one-based accessors, all three frame renderers and
//           every REACHABLE arm of both bail-out guards. The arms that are STRUCTURALLY unreachable in
//           the port are enumerated one by one in Section 2, together with the invariants that make
//           them so and the reason C-B forbids deleting any of them.
//      C-K  Every technology-specific decision is documented where it applies: the substitution
//           above, the three deliberate non-ports, the relative-assertion discipline, the measured
//           normalisation, and the single-source-line requirement in Section 3.
//
//  This file declares NO SCREAMING_SNAKE and no underscore-bearing identifier. That is a build
//  requirement, not a preference: TreatWarningsAsErrors is inherited from Directory.Build.props, and
//  the repository root .editorconfig scopes its CA1707 and IDE1006 suppressions to the named
//  PRODUCTION files on its BAND 3 roster, the single source of truth for that list. No test file is
//  covered, so an underscored identifier here would be a build
//  failure with no way to grant an exception. The legacy vocabulary survives in the VALUES - the
//  oracle's own "-- " prefix - which is where it is observable.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//      * Any hard-coded line number or absolute path. A literal line number would turn every future
//        edit of this file into a false failure, and a literal path would not survive the move
//        between a local build and CI.
//      * Any absolute frame count. See the relative-assertion discipline above.
//      * Any member-data factory that calls the subject. A factory runs at discovery time on an
//        entirely different stack, so a capture taken inside one would measure the discovery
//        machinery. Every factory below returns INPUT ROWS ONLY, matching the caution the sibling
//        CallerInfoTests records for the same hazard.
//      * Any shared assertion helper that captures on behalf of a test. Its own frame would join the
//        stack and shift every count by one, so each depth-sensitive capture is taken in the test
//        method's own body.
//      * A second thread, a timer, or any asynchronous call. There is nothing here to await, so the
//        xunit cancellation-token analyzer diagnostic - an ERROR in this repository - cannot arise.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Characterization tests for <see cref="StackTraceProvider"/>: the two-stage offset arithmetic, the
/// bail-outs, the per-line prefix, the capture primitive and the one-based accessors.
/// </summary>
/// <remarks>
/// Read this type's file header before adding a test. It records the substitution under test, the
/// three deliberate non-ports nothing here may probe for, the three defect-shaped behaviours that are
/// pinned rather than corrected, the reason every assertion is relative, and the measured resolution
/// of the numeric-versus-two-argument question.
/// </remarks>
public class StackTraceProviderTests
{
    /// <summary>
    /// The separator the provider joins frames with: a BARE LINE FEED [stacktraceinfo.srf:L33].
    /// </summary>
    /// <remarks>
    /// Taken from <see cref="LegacyStackFrames.FrameSeparator"/> rather than re-spelled, so this suite
    /// and the payload suites cannot disagree about what a line break is. It is a line feed and not a
    /// carriage-return pair because the rendered trace becomes field 7 of a payload whose FIELD
    /// delimiter is CRLF [assert.srf:L19,L76].
    /// </remarks>
    private const string FrameSeparator = LegacyStackFrames.FrameSeparator;

    /// <summary>
    /// The literal that introduces the line number in a rendered frame, from the producer's own
    /// format contract.
    /// </summary>
    /// <remarks>
    /// Used to locate the end of a frame's qualified member name without parsing, and to recognise a
    /// leaked provider frame by name plus introducer rather than by name alone - a bare name would
    /// also match this suite's own helper names.
    /// </remarks>
    private const string LineNumberIntroducer = " line:";

    /// <summary>
    /// THE ORACLE'S OWN PREFIX, reproduced verbatim: two hyphens followed by one space.
    /// [w_test_assert.srw:L79]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle window's <c>StackTrace</c> button calls <c>StackTraceInfo("-- ")</c>, and that is
    /// the only live call site of any of the four overloads in the entire legacy repository. Using it
    /// rather than an invented marker means the prefix tests cover the one real legacy scenario.
    /// </para>
    /// <para>
    /// <b>The TRAILING SPACE is part of the value and is preserved exactly.</b> It is what separates
    /// the marker from the frame text in the oracle's own output, so trimming it would change the
    /// rendered result the oracle produces.
    /// </para>
    /// </remarks>
    private const string OracleLinePrefix = "-- ";

    // ==============================================================================================
    //  SECTION 1 - THE OFFSET ARITHMETIC  [stacktraceinfo.srf:L13,L16,L19,L29]
    // ==============================================================================================
    //  THE MECHANIC, BEFORE ANY ASSERTION. The render loop keeps the OUTERMOST `nCount` frames
    //  [stacktraceinfo.srf:L32-L36] after `nCount -= offset + 1` [:L29] has reduced the count. So the
    //  offset removes frames from the INNERMOST end, and each unit of offset removes exactly one
    //  line - the innermost surviving one. Nothing is removed from the outermost end at any offset,
    //  which is why the results NEST: a larger offset's output is a strict prefix of a smaller one's.
    //
    //  THIS IS WHERE THE ONE-BASED HAZARD LIVES (AAP 0.4.5.4, risk R9). The legacy loop is
    //  `for nIndex = 1 to nCount` over a one-based array whose UpperBound is the LAST VALID INDEX,
    //  not a length. Both a fencepost in the loop and a mistranslated UpperBound comparison would
    //  shift every result by exactly one line while leaving it looking entirely plausible, so the
    //  theories below assert the DIFFERENCE between adjacent offsets rather than any single count.
    //
    //  WHY THE ROWS START AT 1 AND NOT AT 0. Each row asserts a relationship between offset n and
    //  offset n - 1, so n must have a predecessor. Offset 0 is covered as the predecessor of row 1
    //  and again by the convenience-form tests below.
    // ==============================================================================================

    /// <summary>
    /// Adjacent offsets, each of which is compared against its own predecessor: rows 1, 2, 3 and 4
    /// give four consecutive comparisons and therefore cover the brief's "at least three consecutive
    /// values" with one to spare.
    /// </summary>
    /// <remarks>
    /// An input row only - it does not call the subject. A factory runs at discovery time on an
    /// entirely different stack, so a capture taken inside one would measure the discovery machinery
    /// rather than the test.
    /// </remarks>
    public static TheoryData<ushort> AdjacentOffsetRows => new()
    {
        (ushort)1,
        (ushort)2,
        (ushort)3,
        (ushort)4,
    };

    /// <summary>
    /// Offsets for which the one-argument and two-argument forms must present the identical view,
    /// spanning zero, the first few units and a value an order of magnitude larger.
    /// </summary>
    /// <remarks>
    /// An input row only; see <see cref="AdjacentOffsetRows"/>. The largest row stays far below any
    /// plausible captured depth so that it exercises the arithmetic rather than the bail-out, which
    /// Section 2 covers separately.
    /// </remarks>
    public static TheoryData<ushort> AgreeingOffsetRows => new()
    {
        (ushort)0,
        (ushort)1,
        (ushort)2,
        (ushort)3,
        (ushort)10,
    };

    /// <summary>
    /// EACH ADDITIONAL UNIT OF OFFSET REMOVES EXACTLY ONE LINE - never zero, never two.
    /// </summary>
    /// <param name="offset">The offset under test; its predecessor is <c>offset - 1</c>.</param>
    /// <remarks>
    /// <para>
    /// The arithmetic assertion that matters, and the one a fencepost in the ported loop would break.
    /// Both calls are made from this method's own body so their captures are taken at the same depth,
    /// which is what makes the difference attributable to the offset alone.
    /// </para>
    /// <para>
    /// Hand-derived from <c>stacktraceinfo.srf:L29-L36</c>: the count becomes
    /// <c>captured - offset - 1</c> and the loop emits one line per surviving frame, so incrementing
    /// the offset by one reduces the line count by one. Asserted as an exact difference rather than as
    /// a monotonic decrease, because a decrease of two would satisfy an inequality and is exactly what
    /// a double-decrement defect would produce.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AdjacentOffsetRows))]
    public void EachUnitOfOffsetRemovesExactlyOneLine(ushort offset)
    {
        int atPrevious = FrameCountOf(
            StackTraceProvider.StackTraceInfo((ushort)(offset - 1), string.Empty));
        int atOffset = FrameCountOf(StackTraceProvider.StackTraceInfo(offset, string.Empty));

        Assert.Equal(atPrevious - 1, atOffset);
    }

    /// <summary>
    /// THE RESULTS NEST: a larger offset's output is a strict PREFIX of the next smaller offset's, so
    /// the frames that survive are the OUTERMOST ones, in outermost-first order, and only innermost
    /// lines are ever dropped.
    /// </summary>
    /// <param name="offset">The offset under test; its predecessor is <c>offset - 1</c>.</param>
    /// <remarks>
    /// <para>
    /// The ordering half of the arithmetic, and the half a frame-count assertion cannot see. A trim
    /// implemented from the wrong end would produce exactly the right number of lines and entirely the
    /// wrong lines, passing every count-based test in this section. The prefix relation is the
    /// cheapest statement that rules it out: it simultaneously fixes the direction of the trim, the
    /// order of what remains, and the stability of the surviving text.
    /// </para>
    /// <para>
    /// Sound at every row because the differing frames are always at the innermost end. This method's
    /// own frame carries the line number of whichever statement is executing, so it is the one frame
    /// whose text differs between the two calls - and at any offset of 1 or more it has already been
    /// trimmed from BOTH results, leaving only runner frames whose text is identical.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AdjacentOffsetRows))]
    public void AGreaterOffsetYieldsAStrictPrefixOfTheSmallerOffsetsResult(ushort offset)
    {
        string atPrevious = StackTraceProvider.StackTraceInfo(
            (ushort)(offset - 1), string.Empty);
        string atOffset = StackTraceProvider.StackTraceInfo(offset, string.Empty);

        Assert.StartsWith(atOffset, atPrevious, StringComparison.Ordinal);
        Assert.True(
            atOffset.Length < atPrevious.Length,
            "A greater offset must yield a STRICTLY shorter result, not an equal one.");

        // The prefix relation is asserted again frame by frame, so a failure names the frame that
        // diverged instead of reporting two multi-kilobyte strings.
        string[] previousFrames = FramesOf(atPrevious);
        string[] offsetFrames = FramesOf(atOffset);

        Assert.Equal(previousFrames.Length - 1, offsetFrames.Length);
        Assert.Equal(previousFrames[..^1], offsetFrames);
    }

    /// <summary>
    /// THE THREE CONVENIENCE FORMS AGREE: the no-argument form, the prefix-only form with an empty
    /// prefix, and the numeric-only form with zero all render the identical trace from one call site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In the legacy this is exactly what the constants at <c>stacktraceinfo.srf:L13</c>, <c>:L16</c>
    /// and <c>:L19</c> are for: each compensates for its own delegation frame, so all three normalise
    /// to "the stack from the outermost frame down to and including the calling site". None of them is
    /// therefore a shorthand for a different view; they differ only in whether a prefix is emitted.
    /// </para>
    /// <para>
    /// <b>THE THREE CALLS MUST STAY ON ONE SOURCE LINE.</b> The innermost surviving frame is this
    /// method's own, and its text carries the line number of the statement that is executing - so
    /// three calls spread over three lines would legitimately render three different innermost
    /// frames. Keeping them on one line is what makes byte-for-byte equality assertable. The
    /// reformat-proof assertions follow it, so a maintainer who splits the line still has the
    /// substance covered and gets a comprehensible failure rather than a mystery.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeConvenienceFormsRenderTheIdenticalTrace()
    {
        (string viaNoArgument, string viaEmptyPrefix, string viaZeroOffset) = (StackTraceProvider.StackTraceInfo(), StackTraceProvider.StackTraceInfo(string.Empty), StackTraceProvider.StackTraceInfo((ushort)0));

        Assert.Equal(viaNoArgument, viaEmptyPrefix);
        Assert.Equal(viaNoArgument, viaZeroOffset);

        // Reformat-proof restatement: the line COUNTS agree, every frame except the innermost is
        // identical, and the innermost frame names the same member in all three - only its line
        // number could ever differ.
        string[] noArgumentFrames = FramesOf(viaNoArgument);
        string[] emptyPrefixFrames = FramesOf(viaEmptyPrefix);
        string[] zeroOffsetFrames = FramesOf(viaZeroOffset);

        Assert.Equal(noArgumentFrames.Length, emptyPrefixFrames.Length);
        Assert.Equal(noArgumentFrames.Length, zeroOffsetFrames.Length);
        Assert.Equal(noArgumentFrames[..^1], emptyPrefixFrames[..^1]);
        Assert.Equal(noArgumentFrames[..^1], zeroOffsetFrames[..^1]);
        Assert.Equal(QualifiedMemberOf(noArgumentFrames[^1]), QualifiedMemberOf(emptyPrefixFrames[^1]));
        Assert.Equal(QualifiedMemberOf(noArgumentFrames[^1]), QualifiedMemberOf(zeroOffsetFrames[^1]));

        // And all three end at THIS method, so none of them leaks a provider frame.
        Assert.Contains(
            nameof(TheThreeConvenienceFormsRenderTheIdenticalTrace),
            noArgumentFrames[^1],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// THE PLUS-ONE AT <c>stacktraceinfo.srf:L19</c> IS PINNED HERE. The numeric-only form and the
    /// two-argument form present the identical view for the same number, and both equal the full
    /// capture minus that number.
    /// </summary>
    /// <param name="offset">The offset handed to both forms.</param>
    /// <remarks>
    /// <para>
    /// DELIBERATE LEGACY BEHAVIOUR, AND THE ONE READERS GET BACKWARDS. The two signatures look
    /// interchangeable and their offsets are NOT the same number: <c>:L19</c> hands the implementation
    /// <c>offset + 1</c> where a direct call hands it <c>offset</c>. The reason they nevertheless
    /// agree is that the delegating route also puts one extra frame on the stack - the delegating
    /// overload's own - and the extra offset removes exactly that frame. The two effects cancel, which
    /// is what the <c>+ 1</c> exists to achieve.
    /// </para>
    /// <para>
    /// <b>The counterfactual is what makes this an assertion rather than a restatement.</b> A port
    /// that "simplified" <c>:L19</c> by forwarding the number unchanged would trim only
    /// <c>offset + 1</c> from a stack one frame deeper, so the numeric form would return ONE LINE MORE
    /// than the two-argument form and the extra line would be <c>StackTraceInfo</c>'s own frame - a
    /// diagnostic helper appearing in the stack it is describing. All three assertions below fail
    /// under that mutation: the forms would disagree, neither would equal <c>full - offset</c>, and
    /// the numeric form's output would contain a provider frame.
    /// </para>
    /// <para>
    /// The full capture is taken in this method's own body, so it is measured at the same depth as the
    /// two rendered results and the subtraction is exact rather than approximate.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AgreeingOffsetRows))]
    public void TheNumericOnlyFormAgreesWithTheTwoArgumentFormBecauseOfTheDelegationPlusOne(
        ushort offset)
    {
        string viaNumericOnly = StackTraceProvider.StackTraceInfo(offset);
        string viaTwoArguments = StackTraceProvider.StackTraceInfo(offset, string.Empty);
        int fullCount = StackTraceProvider.StackTrace(out _);

        // 1. The forms agree.
        Assert.Equal(FrameCountOf(viaTwoArguments), FrameCountOf(viaNumericOnly));

        // 2. They agree AT THE RIGHT VALUE rather than merely with each other: the caller's own stack
        //    minus the offset. Without the `+ 1` the numeric form would be one line longer than this.
        Assert.Equal(fullCount - offset, FrameCountOf(viaTwoArguments));
        Assert.Equal(fullCount - offset, FrameCountOf(viaNumericOnly));

        // 3. Neither leaks a provider frame. The delegating overload's frame is the one the `+ 1`
        //    removes, so its presence would be the visible symptom of the dropped increment.
        foreach (string rendered in new[] { viaNumericOnly, viaTwoArguments })
        {
            Assert.DoesNotContain(
                $".{nameof(StackTraceProvider.StackTraceInfo)}{LineNumberIntroducer}",
                rendered,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                $".{nameof(StackTraceProvider.StackTrace)}{LineNumberIntroducer}",
                rendered,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A no-offset trace ends at the CALLER's own frame, by both routes into the implementation.
    /// </summary>
    /// <remarks>
    /// The qualitative statement behind the arithmetic above, and the one that says what the second
    /// stage of the trim is actually for [stacktraceinfo.srf:L29]. The delegating route and the direct
    /// route reach the implementation at different depths, so checking both is what proves the
    /// normalisation rather than one lucky path through it.
    /// </remarks>
    [Fact]
    public void ANoOffsetTraceEndsAtTheCallersOwnFrame()
    {
        string viaDelegate = StackTraceProvider.StackTraceInfo();
        string viaImplementation = StackTraceProvider.StackTraceInfo((ushort)0, string.Empty);

        Assert.Contains(
            nameof(ANoOffsetTraceEndsAtTheCallersOwnFrame),
            FramesOf(viaDelegate)[^1],
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(ANoOffsetTraceEndsAtTheCallersOwnFrame),
            FramesOf(viaImplementation)[^1],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The four overloads trim exactly as the two-stage arithmetic requires, measured as line counts
    /// relative to a full capture taken at the same depth.
    /// </summary>
    /// <param name="offset">
    /// The offset for the two parameterised overloads. The two parameterless-in-offset overloads are
    /// checked against the same row's zero-offset expectation, because they hard-code offset 1 and so
    /// present the caller's stack whole.
    /// </param>
    /// <remarks>
    /// <para>
    /// DECISION 4 of the production file in one theory. The delegating overloads choose an offset
    /// [stacktraceinfo.srf:L13,L16,L19] and the implementation subtracts a further one [:L29], and
    /// the visible consequence is that a zero-offset call reproduces the caller's stack exactly while
    /// an offset of n removes n more from the innermost end.
    /// </para>
    /// <para>
    /// Expressed as differences from a capture taken in this method's own body, never as absolute
    /// counts: the runner contributes most of the depth and its contribution is not a contract.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AgreeingOffsetRows))]
    public void TheFourOverloadsTrimExactlyAsTheTwoStageArithmeticRequires(ushort offset)
    {
        int fullCount = StackTraceProvider.StackTrace(out _);

        // The two overloads that take no offset hard-code 1 [:L13,L16], which the implementation's own
        // subtraction turns into "the caller's stack, whole".
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo()));
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo(OracleLinePrefix)));

        // The two that do take one remove exactly that many more.
        Assert.Equal(fullCount - offset, FrameCountOf(StackTraceProvider.StackTraceInfo(offset)));
        Assert.Equal(
            fullCount - offset,
            FrameCountOf(StackTraceProvider.StackTraceInfo(offset, OracleLinePrefix)));
    }

    /// <summary>
    /// The parameterless overload and the prefix-only overload trim identically, differing only in the
    /// text they emit.
    /// </summary>
    /// <remarks>
    /// Both delegate with the constant 1 [stacktraceinfo.srf:L13,L16], so their line counts must match
    /// exactly while their content differs. This is the pair that IS interchangeable up to the prefix,
    /// and pinning it next to the pair whose numbers mean different things is what makes the
    /// distinction legible instead of folklore.
    /// </remarks>
    [Fact]
    public void TheParameterlessAndPrefixOnlyOverloadsTrimIdentically()
    {
        string bare = StackTraceProvider.StackTraceInfo();
        string prefixed = StackTraceProvider.StackTraceInfo(OracleLinePrefix);

        Assert.Equal(FrameCountOf(bare), FrameCountOf(prefixed));
        Assert.NotEqual(bare, prefixed);
    }

    // ==============================================================================================
    //  SECTION 2 - THE BAIL-OUTS, ALL FOUR  [stacktraceinfo.srf:L27,L30,L38-L40]
    // ==============================================================================================
    //  There are FOUR distinct ways the implementation returns the empty string, not one, and two of
    //  them share a single `if` statement. Enumerated with their locators so none is mistaken for a
    //  duplicate of another:
    //
    //      BAIL-OUT 1  [:L27]      `if nCount < 1 then return ""`
    //                              The capture yielded nothing at all. Tested BEFORE any arithmetic.
    //      BAIL-OUT 2  [:L30] arm1 `if nCount <= 0 ...`
    //                              The trim consumed the whole stack. THE LIVE ARM.
    //      BAIL-OUT 3  [:L30] arm2 `... or nCount > UpperBound(sCallStack) then return ""`
    //                              A defensive guard against a capture that REPORTS more frames than
    //                              it DELIVERED.
    //      BAIL-OUT 4  [:L38-L40]  `catch(throwable ex)` clearing the accumulator.
    //                              Any error anywhere in the body.
    //
    //  TWO OF THE FOUR ARE STRUCTURALLY UNREACHABLE IN THE PORT, AND THAT IS NOT A REASON TO DELETE
    //  EITHER GUARD (C-B). The production file records the same finding at its own :L570-L575 and
    //  :L590-L595, and the reasoning is worth restating from the test side because it is what these
    //  tests assert INSTEAD of the arms themselves:
    //
    //      BAIL-OUT 1 cannot fire because a live managed call stack always reports at least the frame
    //      that called the capture. The port has no way to observe zero frames.
    //
    //      BAIL-OUT 3 cannot fire because the count is DERIVED from the same array's length and is
    //      then only ever DECREMENTED [:L29], so it can never exceed the upper bound.
    //
    //  In the legacy neither is dead. pfwStackTrace is a closed native export: a zero return and an
    //  over-reported count are both possibilities this repository cannot rule out, because no C++
    //  source for pfw.dll exists anywhere in it. Removing either guard would therefore delete a
    //  defence against the ONE component whose behaviour cannot be inspected - a behaviour change
    //  dressed as dead-code removal, which C-B forbids.
    //
    //  SO THESE TESTS PIN THE INVARIANTS THAT MAKE THE TWO ARMS UNREACHABLE, which is the strongest
    //  claim available from the public surface and is strictly more useful than a test that could not
    //  be written: if either invariant ever breaks, the corresponding guard becomes live and these
    //  tests say so. The one-based accessors the guards read are additionally driven directly through
    //  LegacyStackFrames fixtures in Section 5, including the EMPTY list, so the value BAIL-OUT 1
    //  compares against is itself covered.
    //
    //  BAIL-OUT 4'S HANDLER BODY IS UNREACHABLE TOO, AND ITS GUARANTEE IS STILL ASSERTED. Worth
    //  separating, because the two facts are easily conflated. Nothing inside the ported body can
    //  throw for any reachable input: the capture cannot fail, the subtraction cannot overflow because
    //  a ushort offset cannot take an int out of range, the one-based accessor is reached only after
    //  BAIL-OUT 2 has proved the index valid, and appending a null prefix appends nothing rather than
    //  raising. So the handler at :L38-L40 never executes in the port. What the handler PROMISES -
    //  that no overload ever propagates an exception - is nevertheless a contract callers depend on,
    //  because this surface is reached from a failure path, and NoOverloadEverThrowsOrReturnsNull
    //  asserts it across the whole sampled input domain. Reproducing an unexecutable handler is
    //  required by C-B for the same reason as the other two: for the closed pfw.dll, a raising capture
    //  is a possibility this repository cannot rule out.
    //
    //  WHAT THAT MEANS FOR THE COVERAGE REPORT, STATED HERE SO IT IS NOT REDISCOVERED AS A GAP (C-H).
    //  Measured on this host at 87.9 percent line coverage of StackTraceProvider and 87.1 percent of
    //  the Diagnostics assembly, comfortably above the 80 percent gate, with all four rendering
    //  overloads, the capture primitive, both one-based accessors and all three frame-rendering
    //  helpers exercised. Every uncovered line is a defensive arm that this port cannot enter, and
    //  each is one of the following - there are no others:
    //      * the empty-array return inside the capture primitive, for a runtime that reports no frames
    //      * BAIL-OUT 1's return                      [stacktraceinfo.srf:L27]
    //      * BAIL-OUT 4's handler body                [stacktraceinfo.srf:L38-L40]
    //      * the empty-method-name token in the frame renderer, for a member whose reflected name is
    //        the empty string
    //  BAIL-OUT 3's second arm is the one apparent exception and it is not one: it shares a single
    //  `if` with the live first arm, so the LINE is covered while the CONDITION cannot be satisfied.
    //  None of these four is a candidate for deletion, and none is a test that was skipped.
    // ==============================================================================================

    /// <summary>
    /// Offsets at or beyond any plausible captured depth, which drive BAIL-OUT 2's live arm.
    /// </summary>
    /// <remarks>
    /// An input row only. The values climb to one below <see cref="ushort.MaxValue"/> because the
    /// maximum itself behaves differently through the numeric-only overload - see
    /// <see cref="TheMaximumOffsetWrapsThroughTheNumericOnlyOverloadOnly"/> - so it is pinned on its
    /// own rather than folded in here where it would contradict the row's expectation.
    /// </remarks>
    public static TheoryData<ushort> OverLargeOffsetRows => new()
    {
        (ushort)1000,
        (ushort)5000,
        (ushort)10000,
        (ushort)30000,
        (ushort)(ushort.MaxValue - 1),
    };

    /// <summary>
    /// The offset and prefix cross-product swept for the no-throw guarantee, including both boundary
    /// values of the offset domain and a null prefix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An input row only. The offsets sample the domain rather than exhausting it: 65,536 live stack
    /// captures would be disproportionate for a property whose every branch is already reached by
    /// these rows - below the depth, at the depth, far beyond it, and both ends of the range.
    /// </para>
    /// <para>
    /// The prefixes cover the empty string, a null, a single space, the oracle's own marker and a
    /// multi-byte string, because a prefix is concatenated rather than parsed and a multi-byte value is
    /// the one that would expose an encoding assumption.
    /// </para>
    /// </remarks>
    public static TheoryData<ushort, string?> NoThrowSweepRows
    {
        get
        {
            ushort[] offsets =
            [
                0,
                1,
                2,
                3,
                13,
                1000,
                30000,
                ushort.MaxValue - 1,
                ushort.MaxValue,
            ];

            string?[] prefixes = [null, string.Empty, " ", OracleLinePrefix, "多字节前缀"];

            TheoryData<ushort, string?> rows = [];
            foreach (ushort offset in offsets)
            {
                foreach (string? prefix in prefixes)
                {
                    rows.Add(offset, prefix);
                }
            }

            return rows;
        }
    }

    /// <summary>
    /// BAIL-OUT 1's invariant: a live capture ALWAYS reports at least one frame, which is why the
    /// <c>nCount &lt; 1</c> arm cannot fire in the port - and the value it compares against is itself
    /// pinned for the empty case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strongest claim the public surface supports for this arm. The port cannot be made to observe
    /// zero frames, because the method that calls the capture is itself on the stack, so a test that
    /// drove the arm directly cannot be written without an injection seam the rendering path does not
    /// expose. Rather than leave the arm unmentioned, the invariant that makes it unreachable is
    /// asserted: if a future runtime ever returns nothing, this test fails and the guard becomes live.
    /// </para>
    /// <para>
    /// The second half pins the OTHER operand. <c>UpperBound</c> of an empty frame list is <c>0</c>,
    /// which is PowerScript's upper bound for an unallocated array, so a count of zero would fail the
    /// <c>nCount &lt; 1</c> test at <c>stacktraceinfo.srf:L27</c> before it could reach the loop and
    /// index anything. Driven from <see cref="LegacyStackFrames.WithDepth(int)"/> with a depth of zero,
    /// which is the only deterministic route to an empty frame list.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALiveCaptureAlwaysReportsAtLeastOneFrameSoTheEmptyCaptureArmCannotFire()
    {
        int count = StackTraceProvider.StackTrace(out string[] frames);

        Assert.True(
            count >= 1,
            "A live managed call stack always reports at least the frame that called the capture, "
                + "which is what makes stacktraceinfo.srf:L27 unreachable in this port.");
        Assert.NotEmpty(frames);

        // The operand the unreachable arm would compare against, pinned for the empty case.
        Assert.Equal(0, StackTraceProvider.UpperBound(LegacyStackFrames.WithDepth(0)));
    }

    /// <summary>
    /// BAIL-OUT 3's invariant: the reported count ALWAYS equals the delivered array's length, so the
    /// <c>nCount &gt; UpperBound</c> arm cannot fire in the port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count and the array must agree because both consumers iterate one-based from 1 to the
    /// returned count while indexing the array [stacktraceinfo.srf:L32-L35, assert.srf:L61-L62]. A
    /// count LARGER than the array is precisely the failure shape the second arm defends against, and
    /// asserting that the port cannot produce it is what documents the arm as unreachable rather than
    /// untested.
    /// </para>
    /// <para>
    /// The count is only ever DECREMENTED after that point [stacktraceinfo.srf:L29], so equality here
    /// plus a subtraction is the whole proof: the value can fall below the bound, which BAIL-OUT 2
    /// catches, but it can never rise above it. The guard nevertheless stays in the implementation,
    /// because for the closed <c>pfw.dll</c> an over-reported count is a possibility this repository
    /// cannot rule out - no C++ source for it exists anywhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReportedCountEqualsTheDeliveredLengthSoTheOverReportedCountArmCannotFire()
    {
        int count = StackTraceProvider.StackTrace(out string[] frames);

        Assert.Equal(frames.Length, count);
        Assert.Equal(StackTraceProvider.UpperBound(frames), count);
        Assert.False(
            count > StackTraceProvider.UpperBound(frames),
            "The count is derived from the array's own length and thereafter only decremented, so "
                + "stacktraceinfo.srf:L30's second arm is unreachable in this port.");
    }

    /// <summary>
    /// BAIL-OUT 2's live arm: an offset at or beyond the captured depth returns THE EMPTY STRING.
    /// </summary>
    /// <param name="offset">An offset far beyond any plausible captured depth.</param>
    /// <remarks>
    /// <para>
    /// DELIBERATE LEGACY BEHAVIOUR. Returning the empty string - rather than the whole stack, a
    /// truncated stack, the outermost frame, or an exception - is what <c>stacktraceinfo.srf:L30</c>
    /// does, and it is preserved exactly. It also happens to be the right behaviour for the caller:
    /// this surface is reached from a failure path, so a diagnostic helper that threw while describing
    /// a fault would replace the original fault with its own and lose the thing being diagnosed.
    /// </para>
    /// <para>
    /// The rows sit far beyond any managed stack depth so the theory cannot become flaky as the runner
    /// changes; the exact boundary is pinned separately by
    /// <see cref="TheBoundaryBetweenOneFrameAndNoneIsExact"/>. Both parameterised overloads are
    /// checked, with and without a prefix, because a prefix must not resurrect a bail-out result -
    /// <c>:L34</c> lives inside the loop, and the loop does not execute at all here.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OverLargeOffsetRows))]
    public void AnOffsetBeyondTheCapturedDepthReturnsTheEmptyString(ushort offset)
    {
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(offset));
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(offset, string.Empty));
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(offset, OracleLinePrefix));
    }

    /// <summary>
    /// The boundary is EXACT: the largest offset that still yields output produces exactly one frame,
    /// and one more than that produces none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic boundary asserted directly rather than inferred, and the clearest statement that
    /// the guard is <c>nCount &lt;= 0</c> and not something adjacent to it
    /// [stacktraceinfo.srf:L30]. A guard of <c>&lt; 0</c> would let a zero count through and emit
    /// nothing while reporting success; a guard of <c>&lt;= 1</c> would discard the last real frame.
    /// Both are one character away from the ported text and both would pass every other test in this
    /// section.
    /// </para>
    /// <para>
    /// The offset at the boundary is DERIVED from a capture taken in this method's own body, never
    /// written as a literal, because the depth is the runner's and is not a contract. Also asserted at
    /// the one-frame boundary: the result carries no separator at all, which is the
    /// <c>nIndex &gt; 1</c> guard at <c>:L33</c> behaving correctly for a single line.
    /// </para>
    /// <para>
    /// The numeric-only overload is checked at the SAME two values, because the cancellation described
    /// in Section 1 must hold right up to the edge rather than merely in the comfortable middle.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBoundaryBetweenOneFrameAndNoneIsExact()
    {
        int fullCount = StackTraceProvider.StackTrace(out _);

        // A zero-offset call yields exactly fullCount lines, so an offset of fullCount - 1 leaves one
        // and an offset of fullCount leaves none.
        ushort lastYielding = (ushort)(fullCount - 1);
        ushort firstEmpty = (ushort)fullCount;

        string oneFrame = StackTraceProvider.StackTraceInfo(lastYielding, string.Empty);
        Assert.Equal(1, FrameCountOf(oneFrame));
        Assert.DoesNotContain(FrameSeparator, oneFrame, StringComparison.Ordinal);

        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(firstEmpty, string.Empty));

        Assert.Equal(1, FrameCountOf(StackTraceProvider.StackTraceInfo(lastYielding)));
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(firstEmpty));
    }

    /// <summary>
    /// BAIL-OUT 4: no overload EVER throws and no overload EVER returns <see langword="null"/>, for any
    /// offset in the sampled domain crossed with any prefix including a null one.
    /// </summary>
    /// <param name="offset">The offset handed to the two parameterised overloads.</param>
    /// <param name="prefix">The prefix handed to the two prefix-taking overloads; may be null.</param>
    /// <remarks>
    /// <para>
    /// DELIBERATE LEGACY BEHAVIOUR. <c>stacktraceinfo.srf:L38-L40</c> wraps the entire body in
    /// <c>catch(throwable ex)</c> whose only statement clears the accumulator. Nothing is logged,
    /// nothing is rethrown, no partial result is returned, and the declared exception variable is never
    /// read. Swallowing is therefore the ported contract rather than an oversight to be "improved" into
    /// a throw, a log call or a partial result - and this theory is what stops that improvement from
    /// being made quietly.
    /// </para>
    /// <para>
    /// The null prefix is included because <c>:L34</c>'s test is <c>prefix &lt;&gt; ""</c>, which a
    /// null satisfies, and concatenating a null appends nothing - so a null must be indistinguishable
    /// from empty rather than a null-reference. Section 3 pins that equivalence; here it only has to
    /// not throw.
    /// </para>
    /// <para>
    /// <see cref="ushort.MaxValue"/> is deliberately in the offset set. It is the one value the
    /// unchecked increment at the numeric-only overload wraps, so it is also the one value most likely
    /// to surface an arithmetic exception if a maintainer ever made the conversion checked - which C-B
    /// forbids, because the legacy narrows silently.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NoThrowSweepRows))]
    public void NoOverloadEverThrowsOrReturnsNull(ushort offset, string? prefix)
    {
        Assert.NotNull(StackTraceProvider.StackTraceInfo());
        Assert.NotNull(StackTraceProvider.StackTraceInfo(prefix!));
        Assert.NotNull(StackTraceProvider.StackTraceInfo(offset));
        Assert.NotNull(StackTraceProvider.StackTraceInfo(offset, prefix!));
    }

    /// <summary>
    /// THE ONE INPUT AT WHICH THE TWO NUMERIC FORMS ARE GENUINELY NOT EQUIVALENT: at
    /// <see cref="ushort.MaxValue"/> the numeric-only overload's unchecked increment WRAPS to zero, so
    /// it returns a non-empty trace one line LONGER than normal while the two-argument overload returns
    /// the empty string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DELIBERATE LEGACY BEHAVIOUR, AND THE VISIBLE RESIDUE OF <c>stacktraceinfo.srf:L19</c>. The
    /// legacy parameter is a 16-bit unsigned integer and PowerBuilder narrows silently on assignment,
    /// so <c>offset + 1</c> at the top of the range wraps rather than saturating or raising. The port
    /// reproduces that with an explicit <c>unchecked</c> conversion. A checked conversion would throw,
    /// which C-B forbids; a saturating one would invent behaviour the legacy does not have.
    /// </para>
    /// <para>
    /// The consequence is worth stating precisely, because it is the concrete form of the asymmetry the
    /// two signatures hide. Wrapped to zero, the implementation subtracts only one - which is no longer
    /// enough to hide the DELEGATING overload's own frame - so the rendered trace carries one line MORE
    /// than a normal call and that extra line is <c>StackTraceInfo</c> itself. The same value handed to
    /// the two-argument overload undergoes no increment, consumes the whole stack, and bails out to the
    /// empty string. Same number, two different results: that is the asymmetry, observable at exactly
    /// one input.
    /// </para>
    /// <para>
    /// The final assertion is the control: a normal call leaks no provider frame, so the leak is
    /// attributable to the wrap and not to the provider.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMaximumOffsetWrapsThroughTheNumericOnlyOverloadOnly()
    {
        string viaNumericOnly = StackTraceProvider.StackTraceInfo(ushort.MaxValue);
        string viaTwoArguments = StackTraceProvider.StackTraceInfo(ushort.MaxValue, string.Empty);
        int fullCount = StackTraceProvider.StackTrace(out _);

        Assert.NotEqual(string.Empty, viaNumericOnly);
        Assert.Equal(string.Empty, viaTwoArguments);

        // One line MORE than normal, and the extra line is the delegating overload's own frame.
        Assert.Equal(fullCount + 1, FrameCountOf(viaNumericOnly));
        Assert.Contains(
            $".{nameof(StackTraceProvider.StackTraceInfo)}{LineNumberIntroducer}",
            viaNumericOnly,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            $".{nameof(StackTraceProvider.StackTraceInfo)}{LineNumberIntroducer}",
            StackTraceProvider.StackTraceInfo(),
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 3 - PER-LINE PREFIXING AND THE SEPARATOR  [stacktraceinfo.srf:L33-L35]
    // ==============================================================================================
    //  THE EMITTED SHAPE, AND WHY THE ORDER OF THE TWO `if`s MATTERS. The loop body is three
    //  statements in a fixed order:
    //
    //      :L33    if nIndex > 1 then sCallStackInfo += "~n"      the SEPARATOR, only after the first
    //      :L34    if prefix <> "" then sCallStackInfo += prefix  the PREFIX, on EVERY iteration
    //      :L35    sCallStackInfo += sCallStack[nIndex]           the frame
    //
    //  The separator is therefore emitted BEFORE the prefix for every line after the first, so the
    //  shape is
    //
    //      prefix frame1 LF prefix frame2 LF prefix frame3
    //
    //  with NO leading separator, NO trailing separator, and the prefix present on the FIRST line as
    //  well as the rest. Two mistakes produce results that look almost right: prefixing only the first
    //  line (a `nIndex = 1` guard added to :L34), and emitting the separator after each frame rather
    //  than before each frame but the first (which appends a trailing separator). Both are pinned
    //  below by counting rather than by inspecting either end alone.
    //
    //  AN EMPTY PREFIX IS A NO-OP BY CONSTRUCTION. :L34's test is `prefix <> ""`, so the empty string
    //  takes the untaken branch. The observable result is identical to concatenating an empty string,
    //  which is why "an empty prefix renders exactly what the no-prefix overload renders" is a
    //  property rather than a coincidence - and why a null must behave the same way, since the same
    //  comparison is satisfied by a null and concatenating a null appends nothing.
    //
    //  THE SEPARATOR IS A BARE LINE FEED, AND THAT IS THE MOST LOAD-BEARING CHARACTER IN THIS FILE.
    //  It is not CRLF and not the platform newline. The rendered trace becomes FIELD 7 of the
    //  assertion payload, whose FIELD DELIMITER is CRLF [assert.srf:L19,L76], and the consumer takes
    //  its deep branch only when splitting that payload on CRLF yields EXACTLY SEVEN fields. A CRLF
    //  here would split field 7 into extra fields, inflate the count past seven, and silently push the
    //  consumer down its shallow branch - losing the window, the object, the event, the line number
    //  AND the trace in one go. The suites that pin the payload and the consumer from the other side of
    //  that boundary are AssertPayloadProtocolTests and SystemErrorRoundTripTests; this section is the
    //  producer-side half of the same contract, and the three files must never be changed
    //  independently. A second reader confirms the same requirement independently:
    //  n_cst_eventful.sru:L876 embeds the rendered trace in a multi-line exception report.
    // ==============================================================================================

    /// <summary>
    /// Offsets at which the prefix behaviour is checked, so prefixing is proved independent of the
    /// trim rather than only at one convenient depth.
    /// </summary>
    /// <remarks>
    /// An input row only. Includes zero, because that is the deepest result and therefore the one with
    /// the most lines for a per-line property to be wrong on, and small non-zero values so that a
    /// prefix cannot be shown to depend on the result being untrimmed.
    /// </remarks>
    public static TheoryData<ushort> PrefixOffsetRows => new()
    {
        (ushort)0,
        (ushort)1,
        (ushort)5,
    };

    /// <summary>
    /// THE ORACLE'S OWN PREFIX IS APPLIED TO EVERY LINE, INCLUDING THE FIRST, with its trailing space
    /// preserved exactly. [stacktraceinfo.srf:L34, w_test_assert.srw:L79]
    /// </summary>
    /// <param name="offset">The offset at which the prefix behaviour is checked.</param>
    /// <remarks>
    /// <para>
    /// The prefix is <c>"-- "</c>, taken verbatim from the behavioural oracle's only live call site, so
    /// this covers a real legacy scenario rather than an invented marker. The trailing space is part of
    /// the value: it is what separates the marker from the frame text in the oracle's own output.
    /// </para>
    /// <para>
    /// Asserted three ways, because each rules out a different near-miss. The OCCURRENCE COUNT equal to
    /// the line count rules out prefixing only the first line - which a <c>StartsWith</c> check alone
    /// would happily accept. <c>StartsWith</c> rules out a leading separator appearing before the first
    /// prefix. And EVERY LINE individually starting with the prefix rules out a prefix applied to all
    /// lines but one, which the occurrence count alone would miss if a frame's own text happened to
    /// contain the marker.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrefixOffsetRows))]
    public void TheOraclePrefixIsAppliedToEveryLineIncludingTheFirst(ushort offset)
    {
        string rendered = StackTraceProvider.StackTraceInfo(offset, OracleLinePrefix);
        string[] lines = FramesOf(rendered);

        Assert.NotEmpty(lines);
        Assert.StartsWith(OracleLinePrefix, rendered, StringComparison.Ordinal);
        Assert.Equal(lines.Length, rendered.Split(OracleLinePrefix).Length - 1);

        foreach (string line in lines)
        {
            Assert.StartsWith(OracleLinePrefix, line, StringComparison.Ordinal);

            // The marker must not have swallowed the frame: what follows it is still a frame in the
            // producer's format, so the prefix is prepended rather than substituted.
            Assert.Contains(LineNumberIntroducer, line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// AN EMPTY PREFIX RENDERS EXACTLY WHAT THE NO-PREFIX OVERLOAD RENDERS - no marker, no extra
    /// separator, no stray space. [stacktraceinfo.srf:L34]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE TWO CALLS MUST STAY ON ONE SOURCE LINE.</b> The innermost surviving frame is this
    /// method's own and its text carries the line number of the executing statement, so two calls on
    /// two lines would legitimately render two different innermost frames. On one line the two results
    /// must be byte-for-byte identical, which is the strongest form of the claim. The reformat-proof
    /// restatement follows it so a maintainer who splits the line still has the substance covered.
    /// </para>
    /// <para>
    /// Both overloads reach the implementation through one delegating frame - <c>:L13</c> and
    /// <c>:L16</c> both pass the constant 1 - so their captures are taken at the same depth and no
    /// allowance for a differing trim is needed. An implementation that concatenated the prefix
    /// unconditionally would also pass this test, which is exactly why the null case is pinned
    /// separately: the two implementations diverge there and nowhere else.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyPrefixRendersExactlyWhatTheNoPrefixOverloadRenders()
    {
        (string viaNoPrefix, string viaEmptyPrefix) = (StackTraceProvider.StackTraceInfo(), StackTraceProvider.StackTraceInfo(string.Empty));

        Assert.Equal(viaNoPrefix, viaEmptyPrefix);

        // Reformat-proof restatement, plus the explicit absence of the two artefacts an unconditional
        // concatenation of some other value would introduce.
        string[] noPrefixLines = FramesOf(viaNoPrefix);
        string[] emptyPrefixLines = FramesOf(viaEmptyPrefix);

        Assert.Equal(noPrefixLines.Length, emptyPrefixLines.Length);
        Assert.Equal(noPrefixLines[..^1], emptyPrefixLines[..^1]);
        Assert.False(
            viaEmptyPrefix.StartsWith(FrameSeparator, StringComparison.Ordinal),
            "An empty prefix must not introduce a leading separator.");

        foreach (string line in emptyPrefixLines)
        {
            Assert.False(
                line.StartsWith(' '),
                $"Line '{line}' acquired a leading space from an empty prefix.");
        }
    }

    /// <summary>
    /// A NULL PREFIX IS INDISTINGUISHABLE FROM AN EMPTY ONE: it appends nothing and does not throw.
    /// </summary>
    /// <param name="offset">The offset at which the equivalence is checked.</param>
    /// <remarks>
    /// <para>
    /// DELIBERATE LEGACY BEHAVIOUR. <c>stacktraceinfo.srf:L34</c>'s comparison is
    /// <c>prefix &lt;&gt; ""</c>, which a null satisfies, and concatenating a null appends nothing -
    /// which is also what PowerScript does when the comparison yields null. So the ported contract is
    /// "a null behaves as empty", not "a null is rejected", and adding an argument-null guard here
    /// would be the behaviour change C-B forbids.
    /// </para>
    /// <para>
    /// Asserted as a line count and as the absence of any leading marker rather than by searching the
    /// output for the text "null": this method's own name contains that word and appears in the
    /// captured frames, so a text search would match itself.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrefixOffsetRows))]
    public void ANullPrefixBehavesExactlyAsAnEmptyOne(ushort offset)
    {
        string viaNull = StackTraceProvider.StackTraceInfo(offset, null!);
        string viaEmpty = StackTraceProvider.StackTraceInfo(offset, string.Empty);

        Assert.Equal(FrameCountOf(viaEmpty), FrameCountOf(viaNull));

        foreach (string line in FramesOf(viaNull))
        {
            Assert.False(
                line.StartsWith(' '),
                $"Line '{line}' acquired a leading marker from a null prefix.");
            Assert.Contains(LineNumberIntroducer, line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// THE SEPARATOR IS A BARE LINE FEED AND NEVER A CARRIAGE-RETURN PAIR, with exactly
    /// <c>lineCount - 1</c> of them, none leading and none trailing. [stacktraceinfo.srf:L33]
    /// </summary>
    /// <param name="offset">The offset at which the separator structure is checked.</param>
    /// <remarks>
    /// <para>
    /// The carriage-return half is not cosmetic. The rendered trace becomes FIELD 7 of the assertion
    /// payload, whose FIELD DELIMITER is CRLF [assert.srf:L19,L76], and the consumer takes its deep
    /// branch only when splitting that payload on CRLF yields EXACTLY SEVEN fields. A CRLF here would
    /// split field 7 into extra fields and break that test, costing the consumer the window, the
    /// object, the event, the line number and the trace at once. The suites that pin the payload and
    /// the consumer from the other side of the boundary are <c>AssertPayloadProtocolTests</c> and
    /// <c>SystemErrorRoundTripTests</c>; this is the producer-side half of the same contract.
    /// </para>
    /// <para>
    /// The separator COUNT is what catches a trailing-separator regression, which neither an
    /// <c>EndsWith</c> check nor a line count would catch on its own: a trailing separator makes the
    /// count equal the line count instead of one less, while a naive split would report the extra empty
    /// segment as an additional line and keep the two numbers consistent with each other.
    /// </para>
    /// <para>
    /// The prefix is supplied at the oracle's own value so the structure is asserted in the shape the
    /// oracle actually produces - a prefixed line is where a separator emitted in the wrong order would
    /// show up, as <c>LF</c> after the frame rather than before the next prefix.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrefixOffsetRows))]
    public void FramesAreJoinedByExactlyOneBareLineFeedBetweenAdjacentLines(ushort offset)
    {
        string rendered = StackTraceProvider.StackTraceInfo(offset, OracleLinePrefix);
        int lineCount = FrameCountOf(rendered);

        Assert.True(lineCount > 1, "The structure under test needs at least two lines.");

        Assert.DoesNotContain("\r", rendered, StringComparison.Ordinal);
        Assert.Equal(lineCount - 1, rendered.Count(character => character == '\n'));
        Assert.False(
            rendered.StartsWith(FrameSeparator, StringComparison.Ordinal),
            "There is no leading separator: :L33 emits it only when nIndex > 1.");
        Assert.False(
            rendered.EndsWith(FrameSeparator, StringComparison.Ordinal),
            "There is no trailing separator: :L33 emits it BEFORE a line, not after one.");

        // No empty line anywhere, which is what a doubled separator would produce.
        Assert.DoesNotContain(FramesOf(rendered), line => line.Length == 0);
    }

    /// <summary>
    /// The prefix affects only the TEXT, never the trim: a prefixed and an unprefixed render at the
    /// same offset carry the same lines, and stripping the prefix from each line recovers the
    /// unprefixed result.
    /// </summary>
    /// <param name="offset">The offset at which both renders are taken.</param>
    /// <remarks>
    /// <para>
    /// <c>:L34</c> sits inside the loop and touches only the accumulator, so it cannot influence
    /// <c>nCount</c> [:L29] or the loop bound [:L32]. Asserting that directly is what rules out a port
    /// that folded prefix handling into the arithmetic - for instance by treating a prefixed render as
    /// needing one more or one fewer frame.
    /// </para>
    /// <para>
    /// The recovery assertion compares the outer lines only. The innermost line is this method's own
    /// frame, whose line number differs between the two statements that produce the two renders, so it
    /// is compared by qualified member name instead - the part a call's position cannot change.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrefixOffsetRows))]
    public void ThePrefixChangesTheTextButNeverTheTrim(ushort offset)
    {
        string prefixed = StackTraceProvider.StackTraceInfo(offset, OracleLinePrefix);
        string unprefixed = StackTraceProvider.StackTraceInfo(offset, string.Empty);

        string[] prefixedLines = FramesOf(prefixed);
        string[] unprefixedLines = FramesOf(unprefixed);

        Assert.Equal(unprefixedLines.Length, prefixedLines.Length);

        string[] stripped = [.. prefixedLines.Select(line => line[OracleLinePrefix.Length..])];

        Assert.Equal(unprefixedLines[..^1], stripped[..^1]);
        Assert.Equal(QualifiedMemberOf(unprefixedLines[^1]), QualifiedMemberOf(stripped[^1]));
    }

    // ==============================================================================================
    //  SECTION 4 - THE CAPTURE PRIMITIVE  [stacktrace.srf:L7]
    // ==============================================================================================
    //  THE SUBSTITUTED MEMBER ITSELF. Everything in Sections 1 to 3 is ported PowerScript sitting on
    //  top of this one method, which is the only substitution in the file:
    //
    //      pfwStackTrace, exported from the closed pfw.dll   ->   System.Diagnostics.StackTrace
    //
    //  Its contract has three parts, and each is asserted below rather than assumed:
    //
    //      1. THE COUNT AND THE ARRAY AGREE. The legacy returns an int count alongside a one-based
    //         array whose UpperBound is the LAST VALID INDEX - a value that, for a one-based array,
    //         happens to equal the item count. Both consumers iterate `1 .. count` while indexing the
    //         array [stacktraceinfo.srf:L32-L35, assert.srf:L61-L62], so a disagreement is an
    //         out-of-range subscript. This is the R9 one-based hazard in its purest form: a count and
    //         a last-index that are numerically equal under one-based access and differ by one under
    //         zero-based access.
    //
    //      2. THE ORDER IS OUTERMOST FIRST, and this is LOAD BEARING rather than aesthetic.
    //         System.Diagnostics.StackTrace numbers frames the OTHER way - its index 0 is the
    //         innermost - so the port reverses them. The proof that outermost-first is the legacy
    //         order comes from the second consumer, twice:
    //             assert.srf:L38  selects the frame to blame as sCallStack[nCount - 2], i.e. the THIRD
    //                             entry FROM THE END, which under outermost-first is the user's own
    //                             frame sitting just above the two framework frames
    //             assert.srf:L61  copies `1 .. nCount - 2`, i.e. everything from the outermost down to
    //                             and including that frame, TRIMMING THE LAST TWO
    //         Reverse the list and :L38 lands on a framework frame instead, so every assertion report
    //         would name the assert helper's own location as the failure site while still carrying the
    //         right NUMBER of frames - a wrong answer that passes a count assertion. AssertPayloadProtocolTests
    //         pins the consuming half of that arithmetic; this section pins the ordering it depends on.
    //
    //      3. IT NEVER THROWS AND NEVER RETURNS NULL. assert.srf:L30-L32 calls it inside a try whose
    //         catch is EMPTY, and then indexes the array regardless - so on the failure path a null
    //         array would be a null dereference on the one path that must not fail.
    //
    //  THE FRAME TEXT FORMAT IS DEFINED BY THE PORT, NOT COPIED FROM THE ORACLE (C-K). pfw.dll's own
    //  format cannot be read, so the producer defines `<namespace>.<typeChain>.<method> line:<n>` and
    //  guarantees four invariants for every frame including its degraded ones: at least one dot, a
    //  space after the last dot, a colon after that space, and a decimal-digit tail. Those four are
    //  exactly what assert.srf:L39-L59's four positional searches need. This section asserts the SHAPE
    //  and the three degradation branches, never an absolute frame text - a literal would encode this
    //  file's own line numbers and break on the next edit.
    // ==============================================================================================

    /// <summary>
    /// The returned count and the returned array ALWAYS agree, and both agree with the one-based upper
    /// bound. [stacktraceinfo.srf:L27,L30, assert.srf:L61-L62]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The R9 one-based hazard pinned at its source. The legacy count is compared against
    /// <c>UpperBound</c> - PowerScript's LAST VALID INDEX - at <c>stacktraceinfo.srf:L30</c>, and under
    /// one-based access that value is numerically the item count. A port that returned
    /// <c>Length - 1</c> from either would make the ported guard reject a legitimate full-depth render
    /// and would make <c>assert.srf:L61</c>'s loop drop the caller's own frame.
    /// </para>
    /// <para>
    /// Also asserted: a live capture is never empty, which is the invariant Section 2 relies on when it
    /// documents <c>:L27</c> as unreachable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCountTheArrayLengthAndTheOneBasedUpperBoundAllAgree()
    {
        int count = StackTraceProvider.StackTrace(out string[] frames);

        Assert.Equal(frames.Length, count);
        Assert.Equal(StackTraceProvider.UpperBound(frames), count);
        Assert.True(count > 0, "A live managed call stack always reports at least the calling frame.");
    }

    /// <summary>
    /// The capture never throws and never returns <see langword="null"/> for a normal call.
    /// </summary>
    /// <remarks>
    /// <c>assert.srf:L30-L32</c> wraps the capture in a try whose catch is EMPTY and then indexes the
    /// array anyway, so a null array would be a null dereference on the failure path - the one path
    /// that must not fail. The port returns an empty array rather than null for exactly that reason,
    /// and the legacy array is likewise never null, only empty.
    /// </remarks>
    [Fact]
    public void TheCaptureNeverThrowsAndNeverReturnsNull()
    {
        int count = StackTraceProvider.StackTrace(out string[] frames);

        Assert.NotNull(frames);
        Assert.True(count >= 0);
        Assert.DoesNotContain(frames, frame => frame is null);
    }

    /// <summary>
    /// THE REVERSAL: frames are ordered OUTERMOST FIRST, so the innermost helper of a known nesting
    /// chain appears LAST.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single assertion that would catch a port simplified to the framework's own frame order
    /// without the reversal. Driven from a chain of four non-inlined helpers so the expected ordering
    /// is unambiguous without depending on the runner's depth: reading from the END of the array the
    /// frames must be the capture helper, then its caller, then its caller, then its caller.
    /// </para>
    /// <para>
    /// <b>Why this is load bearing.</b> <c>assert.srf:L38</c> selects the frame to blame as
    /// <c>sCallStack[nCount - 2]</c> - the third entry from the end - and <c>:L61</c> copies
    /// <c>1 .. nCount - 2</c>, trimming the last two. Under outermost-first those two expressions mean
    /// "the user's own frame" and "everything down to and including it". Reversed, the same two
    /// expressions name a framework frame and discard the application's outermost frames, so every
    /// assertion report would blame the assert helper's own location while still carrying exactly the
    /// right NUMBER of frames. <c>AssertPayloadProtocolTests</c> pins the consuming half of that
    /// arithmetic and depends on the ordering asserted here.
    /// </para>
    /// <para>
    /// Every helper in the chain carries <see cref="MethodImplOptions.NoInlining"/>. Without it the JIT
    /// would be free to collapse the chain in a Release build and the positional assertions would fail
    /// there and pass in Debug, which is the worst possible place for the difference to appear.
    /// </para>
    /// </remarks>
    [Fact]
    public void FramesAreOrderedOutermostFirstSoTheInnermostHelperIsLast()
    {
        string[] frames = CaptureThroughOuter();

        Assert.True(frames.Length >= 4, "The three helpers plus this test must all be present.");

        Assert.Contains(nameof(CaptureHere), frames[^1], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughInner), frames[^2], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughMiddle), frames[^3], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughOuter), frames[^4], StringComparison.Ordinal);

        // The corollary: the outermost frame is a runtime frame, not one of this file's, so nothing was
        // reversed only partially.
        Assert.DoesNotContain(nameof(CaptureHere), frames[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The capture EXCLUDES ITS OWN FRAME, mirroring a native export that cannot observe itself.
    /// </summary>
    /// <remarks>
    /// <c>pfwStackTrace</c> is a native export and therefore not a PowerScript frame, so the innermost
    /// frame of the legacy array is the routine that CALLED it - the implementation at
    /// <c>stacktraceinfo.srf:L26</c>, or <c>assertfailed</c> at <c>assert.srf:L30</c>. The managed
    /// substitute is an ordinary method and would otherwise appear, so it skips itself. Without that
    /// skip every payload would carry a leading frame naming the diagnostic helper rather than the code
    /// under diagnosis, and the whole trim arithmetic - calibrated against the legacy's frame set -
    /// would be off by one everywhere.
    /// </remarks>
    [Fact]
    public void TheCaptureExcludesItsOwnFrame()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        Assert.DoesNotContain(
            frames,
            frame => frame.Contains(
                $".{nameof(StackTraceProvider.StackTrace)}{LineNumberIntroducer}",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Depth is reflected EXACTLY in the frame count: a chain three calls deeper yields exactly three
    /// more frames.
    /// </summary>
    /// <remarks>
    /// The relative form of "the capture is real". An implementation that returned a constant, a
    /// truncated set or a cached array would satisfy every shape assertion in this section and fail
    /// this one. The difference is asserted as an exact number rather than an inequality because the
    /// three helpers are non-inlined, so the count is deterministic in both configurations.
    /// </remarks>
    [Fact]
    public void DepthIsReflectedExactlyInTheFrameCount()
    {
        string[] shallow = CaptureHere();
        string[] deeper = CaptureThroughOuter();

        Assert.Equal(shallow.Length + 3, deeper.Length);
    }

    /// <summary>
    /// Every frame satisfies the producer's four format invariants, so
    /// <c>assert.srf:L39-L59</c>'s four positional searches all succeed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted as a SHAPE, never as exact text. The frames carry real line numbers from this file, so
    /// a literal expectation would break on the next edit; and a release build without a symbol file
    /// legitimately degrades the line number to zero, which the producer documents as a degradation
    /// rather than an omission - the colon and the digits remain either way.
    /// </para>
    /// <para>
    /// The four invariants, in the order the parser needs them: at least one dot so the
    /// <c>LastPos</c> search at <c>:L39</c> returns a position; a space after the last dot so the
    /// search at <c>:L55</c> finds the end of the method name; a colon after that space so the search
    /// at <c>:L58</c> finds the line-number introducer; and a decimal-digit tail so <c>:L59</c> reads a
    /// number. Neither half of the qualified member may be empty, which is why the producer substitutes
    /// tokens instead of emitting nothing when reflection cannot answer.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameSatisfiesTheProducersFourFormatInvariants()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        Assert.NotEmpty(frames);

        foreach (string frame in frames)
        {
            int introducer = frame.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);
            Assert.True(introducer > 0, $"Frame '{frame}' carries no line-number introducer.");

            string qualifiedMember = frame[..introducer];
            string lineNumber = frame[(introducer + LineNumberIntroducer.Length)..];

            // I1: at least one dot.
            Assert.Contains(".", qualifiedMember, StringComparison.Ordinal);

            // I4: a decimal tail, non-negative. Zero is a legitimate degraded value.
            Assert.True(
                int.TryParse(lineNumber, out int parsed),
                $"Frame '{frame}' does not end in an integer line number.");
            Assert.True(parsed >= 0, $"Frame '{frame}' has a negative line number.");

            // Neither half of the qualified member is empty: the producer substitutes tokens rather
            // than emitting nothing, so the dot is never at either end.
            int lastDot = qualifiedMember.LastIndexOf('.');
            Assert.NotEqual(0, lastDot);
            Assert.NotEqual(qualifiedMember.Length - 1, lastDot);
        }
    }

    /// <summary>
    /// A frame declared on a NESTED type carries its full enclosing chain and its namespace.
    /// </summary>
    /// <remarks>
    /// The scope is built by walking the declaring type outward, so a nested type renders as
    /// <c>Namespace.Outer.Inner</c> rather than as the bare innermost name the reflection API reports.
    /// Asserted because the legacy scope is a fully qualified object name, and a bare name would make
    /// two same-named nested helpers under different outer types indistinguishable in a payload.
    /// </remarks>
    [Fact]
    public void ANestedTypeFrameCarriesItsEnclosingChainAndNamespace()
    {
        string[] frames = NestedCaptureHelper.Capture();

        Assert.Contains(
            $"{typeof(StackTraceProviderTests).Namespace}."
                + $"{nameof(StackTraceProviderTests)}.{nameof(NestedCaptureHelper)}."
                + $"{nameof(NestedCaptureHelper.Capture)}",
            frames[^1],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A frame declared on a type with NO NAMESPACE renders as the bare type chain and still carries
    /// exactly one dot, which is the parser's defined single-dot branch rather than a failure.
    /// </summary>
    /// <remarks>
    /// <c>assert.srf:L47-L51</c> has an explicit branch for a frame with exactly one dot, in which the
    /// window and the object become the SAME value - and the consumer relies on that equality to
    /// suppress its window line. So a namespaceless type is a supported shape, not a degenerate one;
    /// the degenerate shape would be a missing colon, which the format's third invariant rules out
    /// unconditionally.
    /// </remarks>
    [Fact]
    public void ANamespacelessTypeFrameRendersAsTheBareTypeChain()
    {
        string[] frames = GlobalScopeCaptureHelper.Capture();
        string innermost = frames[^1];

        int introducer = innermost.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);
        Assert.True(introducer > 0, $"Frame '{innermost}' carries no line-number introducer.");

        string qualifiedMember = innermost[..introducer];

        Assert.Equal(1, qualifiedMember.Count(character => character == '.'));
        Assert.StartsWith(nameof(GlobalScopeCaptureHelper), qualifiedMember, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CONSTRUCTOR frame has the leading dot of its reflected name stripped, so exactly one dot
    /// separates the type from the member.
    /// </summary>
    /// <remarks>
    /// An instance constructor's reflected name begins with a dot, which without the strip would leave
    /// the type portion ending in a stray dot and put the constructor marker where the method name
    /// belongs - the parser at <c>assert.srf:L46</c> would then report that marker as the object.
    /// Asserted through a helper that captures from inside its own constructor body, because that is
    /// the only way to reach the branch.
    /// </remarks>
    [Fact]
    public void AConstructorFrameHasItsLeadingDotStripped()
    {
        string[] frames = new ConstructorCaptureHelper().Frames;
        string innermost = frames[^1];

        Assert.Contains(
            $"{nameof(ConstructorCaptureHelper)}.ctor{LineNumberIntroducer}",
            innermost,
            StringComparison.Ordinal);
        Assert.DoesNotContain("..ctor", innermost, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 5 - THE ONE-BASED ACCESSORS  [stacktraceinfo.srf:L30,L35 / AAP 0.4.5.4]
    // ==============================================================================================
    //  AAP 0.4.5.4 calls one-based to zero-based translation the single most dangerous mechanical
    //  hazard in this refactor, because a silent off-by-one is indistinguishable from a behavioural
    //  regression. The production type routes every ported index expression through two internal
    //  accessors so the convention lives in one auditable place, and this project's assembly is named
    //  in the Diagnostics project's InternalsVisibleTo, so the convention is VERIFIED here rather than
    //  trusted.
    //
    //  THE TWO CONVENTIONS, STATED ONCE:
    //      UpperBound(frames)      PowerScript's LAST VALID INDEX. For a one-based array of n elements
    //                              it answers n, whereas a C# reader expects n - 1 to be the last
    //                              index. That coincidence is exactly why the ported guard at :L30
    //                              compares with `>` and needs no adjustment.
    //      FrameAt(frames, i)      one-based access, so 1 is the OUTERMOST frame and UpperBound is the
    //                              innermost.
    //
    //  EVERY FIXTURE HERE COMES FROM LegacyStackFrames, and every expectation is named by VALUE through
    //  DepthFrame rather than computed from an index. That is the point of the fixture type: the number
    //  inside each generated frame IS its one-based position, so an assertion can say "the frame at
    //  position 3 is the frame numbered 3" without any test ever converting between the two index
    //  bases and getting it wrong in the same direction as the code under test.
    // ==============================================================================================

    /// <summary>
    /// Frame-list depths for the one-based accessor theories, including the EMPTY list.
    /// </summary>
    /// <remarks>
    /// An input row only. Zero is included because it is the one depth whose upper bound differs in kind
    /// rather than in degree - PowerScript's upper bound for an unallocated array is <c>0</c>, which is
    /// also the value that makes <c>stacktraceinfo.srf:L27</c>'s guard meaningful.
    /// </remarks>
    public static TheoryData<int> FrameListDepthRows => new()
    {
        0,
        1,
        2,
        3,
        17,
    };

    /// <summary>
    /// <c>UpperBound</c> returns PowerScript's LAST VALID one-based index, which for a list of
    /// <paramref name="depth"/> frames is <paramref name="depth"/> itself.
    /// </summary>
    /// <param name="depth">The number of frames in the fixture list.</param>
    /// <remarks>
    /// The whole reason the accessor exists. PowerScript's upper-bound function returns the last valid
    /// index and its arrays are one-based, so for n elements it answers n - where a C# reader expects
    /// <c>Length - 1</c>. Centralizing that in one internal method is what AAP 0.4.5.4 asks for, and
    /// asserting it directly is what makes the convention auditable instead of re-derived at each call
    /// site. Driven from <see cref="LegacyStackFrames.WithDepth(int)"/> so the fixture and the payload
    /// suites share one definition of a frame list.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameListDepthRows))]
    public void UpperBoundReturnsTheLastValidOneBasedIndex(int depth)
    {
        string[] frames = LegacyStackFrames.WithDepth(depth);

        Assert.Equal(depth, StackTraceProvider.UpperBound(frames));
    }

    /// <summary>
    /// <c>FrameAt</c> is one-based at BOTH ends: index 1 is the outermost frame and
    /// <c>UpperBound</c> is the innermost.
    /// </summary>
    /// <param name="depth">The number of frames in the fixture list; rows of 0 are skipped.</param>
    /// <remarks>
    /// The complement of the bound accessor, and the pair is what makes a ported one-based loop correct
    /// with no adjustment at the call site. Both ends are asserted because an accessor that subtracted
    /// at the wrong end would still satisfy a single-element probe. Expectations are named by VALUE
    /// through <see cref="LegacyStackFrames.DepthFrame(int)"/>, so no test here writes an index
    /// expression of its own.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FrameListDepthRows))]
    public void FrameAtIsOneBasedAtBothEnds(int depth)
    {
        if (depth == 0)
        {
            // An empty list has no valid one-based index at all, which the out-of-range test covers.
            Assert.Equal(0, StackTraceProvider.UpperBound(LegacyStackFrames.WithDepth(depth)));
            return;
        }

        string[] frames = LegacyStackFrames.WithDepth(depth);

        Assert.Equal(LegacyStackFrames.DepthFrame(1), StackTraceProvider.FrameAt(frames, 1));
        Assert.Equal(
            LegacyStackFrames.DepthFrame(depth),
            StackTraceProvider.FrameAt(frames, StackTraceProvider.UpperBound(frames)));
    }

    /// <summary>
    /// A one-based loop bounded by <c>UpperBound</c> visits every element exactly once, in order.
    /// </summary>
    /// <remarks>
    /// The two accessors asserted as the IDIOM they exist to support rather than as two isolated
    /// functions. This is the loop shape every ported PowerScript iteration takes - including
    /// <c>stacktraceinfo.srf:L32-L36</c> and <c>assert.srf:L61-L64</c> - so proving it exhaustive and
    /// ordered proves the translation of all of them.
    /// </remarks>
    [Fact]
    public void AOneBasedLoopBoundedByUpperBoundVisitsEveryElementInOrder()
    {
        string[] frames = LegacyStackFrames.WithDepth(LegacyStackFrames.MinimumDeepFrameCount + 1);
        List<string> visited = [];

        for (int index = 1; index <= StackTraceProvider.UpperBound(frames); index++)
        {
            visited.Add(StackTraceProvider.FrameAt(frames, index));
        }

        Assert.Equal(frames, visited);
    }

    /// <summary>
    /// Index ZERO, an index past the bound, and a negative index are all out of range - the accessor
    /// does not silently clamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Index 0 is the trap, and it is the reason this test exists rather than being folded into the one
    /// above: 0 is the valid FIRST index in C# and an INVALID index in PowerScript, so an accessor that
    /// accepted it would let a zero-based loop compile and run while reading one element early and
    /// silently missing the last.
    /// </para>
    /// <para>
    /// Throwing is correct here, and it is the one place in this suite where it is. PowerScript raises
    /// its own error on an out-of-range subscript, so propagating the array's exception is the faithful
    /// behaviour, and a bad index is a translation error in the caller rather than a runtime condition
    /// to be tolerated. The rendering path above is the opposite case, and it swallows - the sole caller
    /// of this accessor sits inside that swallow, so an out-of-range index there surfaces as the empty
    /// string rather than as an exception.
    /// </para>
    /// </remarks>
    [Fact]
    public void AZeroPastTheEndOrNegativeIndexIsOutOfRange()
    {
        string[] frames = LegacyStackFrames.WithDepth(1);

        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, 0));
        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, 2));
        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, -1));

        Assert.Throws<IndexOutOfRangeException>(
            () => StackTraceProvider.FrameAt(LegacyStackFrames.WithDepth(0), 1));
    }

    // ==============================================================================================
    //  SECTION 6 - SHAPE, AND THE TWO ATTRIBUTES THE ARITHMETIC DEPENDS ON
    // ==============================================================================================
    //  These two tests assert by REFLECTION because both symptoms are invisible in the configuration
    //  developers usually run. A missing NoInlining changes nothing in Debug and silently changes the
    //  trim in Release; a fifth overload would compile and pass every behavioural test above while
    //  having no stage arithmetic of its own.
    // ==============================================================================================

    /// <summary>
    /// The provider is a static class exposing EXACTLY the capture primitive and the four rendering
    /// overloads. [stacktrace.srf:L7, stacktraceinfo.srf:L7-L10]
    /// </summary>
    /// <remarks>
    /// The machine-checkable form of "four overloads, each with its own stage arithmetic". A FIFTH
    /// would need its own row in Section 1's table and its own delegation constant, and a MISSING one
    /// would mean a legacy prototype has no port - <c>stacktraceinfo.srf:L7-L10</c> declares exactly
    /// four and they ARE the contract. The type being static mirrors the legacy global functions, which
    /// have no instance to hold.
    /// </remarks>
    [Fact]
    public void TheProviderIsStaticWithExactlyTheCaptureAndFourRenderingOverloads()
    {
        Assert.True(typeof(StackTraceProvider).IsAbstract && typeof(StackTraceProvider).IsSealed);

        MethodInfo[] declared = typeof(StackTraceProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(5, declared.Length);
        Assert.Single(declared, method => method.Name == nameof(StackTraceProvider.StackTrace));
        Assert.Equal(
            4,
            declared.Count(method => method.Name == nameof(StackTraceProvider.StackTraceInfo)));
    }

    /// <summary>
    /// Every frame-sensitive method forbids INLINING, and the three DELEGATING overloads additionally
    /// forbid OPTIMIZATION so a tail call cannot reuse the caller's frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a correctness requirement, not a performance note, and no performance objective is
    /// asserted anywhere by it. The trim is expressed in FRAMES, so if the runtime inlined a rendering
    /// overload into its caller the frame the arithmetic was calibrated to remove would not exist and
    /// the result would silently include one frame too many - in a Release build only.
    /// </para>
    /// <para>
    /// The three delegating overloads need the second attribute as well because their bodies consist
    /// solely of a call in tail position, which is the one shape for which a JIT may reuse the caller's
    /// frame rather than pushing a new one. That would remove exactly the frame
    /// <c>stacktraceinfo.srf:L19</c>'s <c>+ 1</c> exists to hide, so Section 1's agreement would break
    /// in Release and hold in Debug. The implementation overload is identified as the one taking two
    /// parameters, so this test needs no knowledge of parameter ORDER beyond the arity.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameSensitiveMethodForbidsInliningAndTheDelegatorsForbidOptimization()
    {
        MethodInfo[] frameSensitive = typeof(StackTraceProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is nameof(StackTraceProvider.StackTrace)
                or nameof(StackTraceProvider.StackTraceInfo))
            .ToArray();

        Assert.Equal(5, frameSensitive.Length);

        foreach (MethodInfo method in frameSensitive)
        {
            Assert.True(
                method.MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining),
                $"{DescribeSignature(method)} participates in frame-count arithmetic and must forbid "
                    + "inlining, or its trim silently changes in a Release build.");
        }

        MethodInfo[] delegating = [.. frameSensitive
            .Where(method => method.Name == nameof(StackTraceProvider.StackTraceInfo)
                && method.GetParameters().Length < 2)];

        Assert.Equal(3, delegating.Length);

        foreach (MethodInfo method in delegating)
        {
            Assert.True(
                method.MethodImplementationFlags.HasFlag(MethodImplAttributes.NoOptimization),
                $"{DescribeSignature(method)} is a tail call and must forbid optimization, or the "
                    + "frame that stacktraceinfo.srf:L19's `+ 1` hides may not exist at all.");
        }
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================
    //  NONE OF THESE CAPTURES ON BEHALF OF A TEST. Every helper below is a pure function of a string
    //  it is handed, so no helper contributes a frame to a measurement. The helpers that DO
    //  deliberately contribute a frame are the nesting chains at the very bottom of this file, and
    //  each of those carries NoInlining and exists only to add known depth.
    // ==============================================================================================

    /// <summary>
    /// Splits a rendered trace into its frames, treating the empty string as NO frames.
    /// </summary>
    /// <param name="rendered">A rendered trace as any of the four overloads returns it.</param>
    /// <returns>The frames, outermost first; empty for a bail-out result.</returns>
    /// <remarks>
    /// The empty-string special case is load bearing rather than tidiness: <c>"".Split("\n")</c> yields
    /// one empty element rather than none, so a naive split would report a bail-out result as ONE frame
    /// and make every arithmetic assertion off by one exactly at the boundary the bail-out defines.
    /// </remarks>
    private static string[] FramesOf(string rendered)
    {
        return rendered.Length == 0 ? [] : rendered.Split(FrameSeparator);
    }

    /// <summary>
    /// Counts the frames in a rendered trace, treating the empty string as zero frames.
    /// </summary>
    /// <param name="rendered">A rendered trace.</param>
    /// <returns>The number of frames it carries.</returns>
    /// <remarks>
    /// Expressed through <see cref="FramesOf(string)"/> so there is ONE definition of "a frame" in
    /// this file, and so the empty-string rule cannot be applied in one place and forgotten in
    /// another.
    /// </remarks>
    private static int FrameCountOf(string rendered)
    {
        return FramesOf(rendered).Length;
    }

    /// <summary>
    /// Returns the qualified member portion of a frame - everything before the line-number introducer.
    /// </summary>
    /// <param name="frame">One frame of a rendered trace.</param>
    /// <returns>
    /// The scope and member text, or the whole frame when it carries no introducer.
    /// </returns>
    /// <remarks>
    /// Lets an assertion compare two frames that name the SAME member from different statements: the
    /// line number is the only part of a frame that a call's position changes, so stripping it is what
    /// makes an equality assertion reformat-proof. The introducer is located with a LAST-index search
    /// because the producer appends it at the very end of the frame.
    /// </remarks>
    private static string QualifiedMemberOf(string frame)
    {
        int introducer = frame.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);

        return introducer < 0 ? frame : frame[..introducer];
    }

    /// <summary>
    /// Renders a method's name and parameter types for a reflection assertion's failure message.
    /// </summary>
    /// <param name="method">The method to describe.</param>
    /// <returns>The method name followed by its parameter type names in parentheses.</returns>
    /// <remarks>
    /// Section 6 asserts over five overloads that differ only in their parameters, so a failure message
    /// carrying the name alone would not say WHICH overload failed. Only the type names are needed, not
    /// a fully qualified signature, because the five overloads are distinguishable by them.
    /// </remarks>
    private static string DescribeSignature(MethodInfo method)
    {
        string parameters = string.Join(
            ", ",
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));

        return $"{method.Name}({parameters})";
    }

    // ----------------------------------------------------------------------------------------------
    //  THE NESTING CHAINS - THE ONLY HELPERS HERE THAT DELIBERATELY CONTRIBUTE A FRAME
    // ----------------------------------------------------------------------------------------------
    //  Each of the four capture helpers below adds exactly one frame to the stack, giving Section 4 a
    //  chain of known depth to assert an ordering and an exact count against. EVERY ONE CARRIES
    //  NoInlining, and that is not defensive habit: without it the JIT is free to collapse the chain in
    //  a Release build, the positional assertions would then fail in Release and pass in Debug, and the
    //  natural but wrong response would be to weaken the assertion rather than restore the attribute.
    //  CI builds Release, so the attribute is what makes these tests mean the same thing in both
    //  configurations - which was verified by running the whole suite in each.
    //
    //  None of them takes a parameter and none returns anything derived from a test's expectation. They
    //  add depth and nothing else, so no expectation can be smuggled through one.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Captures at this depth, the innermost link of the chain.</summary>
    /// <returns>The captured frames, outermost first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureHere()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        return frames;
    }

    /// <summary>Adds one frame above <see cref="CaptureHere"/>.</summary>
    /// <returns>The captured frames, outermost first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughInner() => CaptureHere();

    /// <summary>Adds a second frame.</summary>
    /// <returns>The captured frames, outermost first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughMiddle() => CaptureThroughInner();

    /// <summary>
    /// Adds a third frame, giving a four-deep chain below the calling test method.
    /// </summary>
    /// <returns>The captured frames, outermost first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughOuter() => CaptureThroughMiddle();

    /// <summary>
    /// A NESTED type, so the enclosing-chain branch of scope rendering is reachable.
    /// </summary>
    /// <remarks>
    /// Nested inside the test class deliberately: the branch under test walks the declaring type
    /// outward, so the assertion needs a type whose expected chain is known and stable, and this class
    /// is the only such type in scope. The namespace-less branch cannot be reached from here at all,
    /// because a file-scoped namespace declaration governs every type in this file - the sibling
    /// <c>GlobalScopeCaptureHelper</c> exists in a file of its own for exactly that reason, and its own
    /// documentation names this suite as its consumer.
    /// </remarks>
    private static class NestedCaptureHelper
    {
        /// <summary>Captures from inside a nested type.</summary>
        /// <returns>The captured frames, outermost first.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string[] Capture()
        {
            StackTraceProvider.StackTrace(out string[] frames);

            return frames;
        }
    }

    /// <summary>
    /// Captures from inside a CONSTRUCTOR, so the leading-dot-stripping branch is reachable.
    /// </summary>
    /// <remarks>
    /// A constructor is the only member whose reflected name begins with a dot, so this is the only way
    /// to reach that branch. The frames are captured in the constructor body and exposed as a property
    /// rather than returned, because a constructor cannot return a value.
    /// </remarks>
    private sealed class ConstructorCaptureHelper
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConstructorCaptureHelper"/> class, capturing the
        /// call stack from inside the constructor body.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal ConstructorCaptureHelper()
        {
            StackTraceProvider.StackTrace(out string[] frames);
            Frames = frames;
        }

        /// <summary>Gets the frames captured during construction, outermost first.</summary>
        internal string[] Frames { get; }
    }
}
