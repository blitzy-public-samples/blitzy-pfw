// ==================================================================================================
//  LegacyStackFrames.cs - THE DETERMINISTIC FRAME FIXTURES FOR THE ASSERTION PAYLOAD SEAM
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS, AND WHY IT HAS TO EXIST
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Shared.Diagnostics.Assertions.BuildFailure is an INTERNAL seam that takes the
//  captured frames as an argument instead of capturing them itself. Its entire behaviour - which of
//  the two payload shapes it emits, which frame it blames, how it splits that frame into a window, an
//  object, an event and a line number, and which frames it copies into the trace - is a pure function
//  of the frame array and the frame count it is handed. Deterministic frames are therefore not a
//  convenience for the suites that pin that behaviour; they are the PRECONDITION for pinning it at
//  all. A live capture inside a test host carries runner frames that differ by runner version and by
//  whether a test is a fact or a theory, so the parse branches simply are not reachable
//  reproducibly from a real stack.
//
//  This file declares NO test. It is fixture code: every frame string and every frame-list factory
//  the suites in this project feed into that seam lives here and nowhere else, so that one definition
//  of "the two-dot frame", "the caller frame" and "the trimmed stack" is shared rather than
//  re-invented per suite.
//
//  ------------------------------------------------------------------------------------------------
//  LEGACY ORACLES. EVERY FRAME STRING BELOW IS AUTHORED FROM THESE, NEVER READ FROM THEM
//  ------------------------------------------------------------------------------------------------
//      ws_objects/pfw.common.pbl.src/assert.srf            the payload builder and its frame parse
//      ws_objects/pfw.common.pbl.src/stacktrace.srf        the pfwStackTrace prototype
//      ws_objects/pfw.common.pbl.src/stacktraceinfo.srf    the 4 overloads and their offset arithmetic
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw      the behavioural oracle: the window, its
//                                                          controls, its two window functions and the
//                                                          catch that reads the payload back
//      ws_objects/pfw.pbl.src/pfw.sra                      the consumer that unpacks the payload
//
//  ws_objects/** IS READ ONLY (C-C). It is the parity oracle, never a build input and never an edit
//  target. Nothing in this file opens, reads, copies or embeds a path under it, and nothing here
//  touches the file system, the network or the environment in any way. The frame strings were
//  authored by reading those sources and are reproduced as literals, which is why this project's own
//  build declares no content or none items at all.
//
//  ------------------------------------------------------------------------------------------------
//  THE FRAME TEXT FORMAT THESE FIXTURES CONFORM TO, AND WHERE IT IS DEFINED
//  ------------------------------------------------------------------------------------------------
//  StackTraceProvider is the PRODUCER and its DECISION 6 defines the format, because pfw.dll's own
//  frame text cannot be read. The emitted shape is
//
//      <namespace>.<typeChain>.<method> line:<lineNumber>
//
//  and it guarantees four invariants for every frame it emits, INCLUDING the degraded ones: at least
//  one dot exists (I1), a space follows the last dot (I2), a colon follows that space (I3), and the
//  string ends in decimal digits (I4). Those four are exactly what the parser's four positional
//  searches need.
//
//  Every fixture here is a valid instance of that format EXCEPT the two edge fixtures, which exist
//  precisely to drive the arms the invariants make unreachable from a real capture, and each says so
//  on itself. Pinning a format the producer does not emit would be worthless, so the shapes below
//  were checked against DescribeFrame rather than assumed: the legacy window/control/event spelling
//  the oracle uses is ALREADY a conforming instance - scope "w_test_assert.cb_1", member "clicked",
//  tail " line:137" - which is why the oracle's own vocabulary could be kept.
//
//  ------------------------------------------------------------------------------------------------
//  THE ORDERING CONTRACT, AND THE ONE BASED TO ZERO BASED TRANSLATION (AAP 0.4.5.4, 0.8.6 R9)
//  ------------------------------------------------------------------------------------------------
//  AAP 0.4.5.4 and risk R9 name one-based to zero-based translation the single most dangerous
//  mechanical hazard in this refactor, because a silent off-by-one is indistinguishable from a
//  behavioural regression. This file is where a fixture could inject one, so the translation is
//  spelled out rather than left to be re-derived at each call site.
//
//  FRAMES ARE OUTERMOST FIRST. Index 0 is the outermost frame and the INNERMOST frame is last, which
//  is what StackTraceProvider.StackTrace produces and what the legacy array is. For a list of N
//  frames captured from the payload builder:
//
//      ONE BASED (PowerScript)                     ZERO BASED (C#)          what it is
//      ----------------------------------------    ---------------------    -----------------------
//      1                                           0                        the outermost frame
//      ...                                         ...                      outer frames
//      N - 2   <- selected [assert.srf:L38]        N - 3   <- SELECTED      THE CALLER, blamed
//      N - 1                                       N - 2                    the Assert overload
//      N                                           N - 1                    the payload builder
//
//  So `sCallStack[nCount - 2]` [assert.srf:L38] is C# index `frameCount - 3`, and the trimmed stack
//  `for nIndex = 1 to nCount - 2` [assert.srf:L61] is THE FIRST `frameCount - 2` ELEMENTS. Note the
//  asymmetry deliberately: the SELECTION shifts by one when translated and the TRIM does not, because
//  one is an index and the other is a count. That is the whole trap, in one sentence.
//
//  This file therefore contains NO index arithmetic on a frame array at all. Lists are assembled by
//  appending in order, exactly as assert.srf:L62 does, and the trim is expressed as a COUNT
//  (`Take(frameCount - 2)`) rather than as a range of indices. The one place a position is named -
//  the caller - is exposed as ExpectedCallerFrame so that a suite asserts against a named value
//  instead of recomputing an index and getting it wrong in the same direction as the code under test.
//
//  ------------------------------------------------------------------------------------------------
//  THE PARSE ARITHMETIC EVERY EXPECTED VALUE BELOW WAS DERIVED FROM [assert.srf:L39-L59]
//  ------------------------------------------------------------------------------------------------
//      nPos  = LastPos(frame, ".")                             the last dot
//      if nPos > 0 then
//          nPos2 = Pos(frame, ".")                             the first dot
//          if nPos2 < nPos then                                TWO OR MORE DOTS
//              WindowMenu = Left(frame, nPos2 - 1)             text before the FIRST dot
//              Object     = Mid(frame, nPos2 + 1, nPos - nPos2 - 1)   text BETWEEN the two dots
//          else                                                EXACTLY ONE DOT
//              WindowMenu = Left(frame, nPos - 1)
//              Object     = WindowMenu                         [assert.srf:L51] THE SAME VALUE
//          end if
//      end if                                                  no dot: BOTH are left untouched
//      nPos2       = Pos(frame, " ", nPos + 1)                 OUTSIDE the dot guard
//      ObjectEvent = Mid(frame, nPos + 1, nPos2 - nPos - 1)
//      nPos        = Pos(frame, ":", nPos2 + 1)
//      Line        = Long(Mid(frame, nPos + 1))
//
//  Two structural facts about that listing are load bearing and are reproduced in the expectations
//  below rather than smoothed over. The ObjectEvent, Line and trace steps sit OUTSIDE the `nPos > 0`
//  guard, so a frame with no dot still produces a full seven-field payload whose fields 3 and 4 are
//  empty. And the space search is anchored at `nPos + 1`, so when there is no dot it restarts at
//  position 1 and the whole leading token becomes the event.
//
//  ------------------------------------------------------------------------------------------------
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  ------------------------------------------------------------------------------------------------
//  There are NO user specified rules for this project. review_rules returns exactly one statement:
//  that no user rules were provided. Nothing is invented, inferred or back-filled in their place, and
//  the absence is not read as licence to lower the bar - the bar is AAP 0.7.2, the enterprise
//  standard baseline, and AAP 0.7.3, the twelve non-rule constraints. Five bind this file:
//
//      C-B  Replicate legacy behaviour; no improvements. The fixtures encode the frame grammar AS IT
//           IS, including the awkward one-dot shape that makes two payload fields identical, and
//           every fixture whose only purpose is to exercise preserved behaviour a reader would
//           mistake for a bug carries a comment saying so.
//      C-C  ws_objects/** is read only. Authored from, never read from. Stated in full above.
//      C-D  No deferred capability appears here in any form - not as a name, a string, a comment or
//           a frame. The four deferred services receive no code in this refactor, and a fixture
//           naming one would be the thinnest possible violation of that.
//      C-H  The coverage gate is measured on the Diagnostics assembly. This file is FIXTURE code and
//           declares no test attribute of any kind, so xunit discovers nothing here; it exists to
//           make the suites that do carry them able to reach every branch of the seam.
//      C-K  Every decision is documented where it applies: the format conformance check, the
//           index translation, the deliberate reuse of oracle vocabulary, the two edge fixtures the
//           producer cannot emit, and the refusal to fabricate an expectation for an inconsistent
//           array-and-count pair.
//
//  ------------------------------------------------------------------------------------------------
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  ------------------------------------------------------------------------------------------------
//      * Any test attribute, and any reference to Xunit. See C-H. This type must never be discovered
//        as a test class, and the surest way to guarantee that is to import nothing that could.
//      * Any public member. Everything here is internal, matching the sibling capture helper. These
//        fixtures are not part of any published surface and no service consumes them.
//      * Any reflection. The seam is reachable because PowerFramework.Shared.Diagnostics declares
//        InternalsVisibleTo for this assembly and this project pins AssemblyName to match it. If it
//        ever stops being reachable, the fix is that assembly name - never reflection, and never an
//        edit to the sibling project.
//      * Any reference to PowerFramework.Shared.Kernel. A legacy constant value would be taken from
//        RetCode rather than re-spelled, but no frame text contains one: the payload's own numeric
//        field is assembled inside the seam, not here.
//      * Environment.NewLine. The frame separator is a BARE LINE FEED [assert.srf:L63] and is
//        declared as a literal constant below. On Linux, the target operating system,
//        Environment.NewLine is "\n" and would happen to work; on Windows it is "\r\n", which is the
//        payload FIELD delimiter and would split field 7 into extra segments and defeat the
//        consumer's exactly-seven test [pfw.sra:L119]. A fixture that broke that way would look like
//        a production defect.
//      * A SCREAMING_SNAKE constant. The repository root .editorconfig scopes its CA1707 and IDE1006
//        suppressions to the named PRODUCTION files on its BAND 3 roster - the single source of truth
//        for that list - and does not extend them to test files, so under
//        the inherited TreatWarningsAsErrors an underscored identifier here would be a build failure
//        with no way to grant an exception. Identifiers are conventional PascalCase; the legacy
//        vocabulary survives in the VALUES, which is where it is observable.
//      * A shared, cached array. Every factory returns a FRESH array, because arrays are mutable and
//        a suite that sorted or overwrote one in place would otherwise corrupt every later suite in
//        the same run.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// The canonical call-stack frame strings and frame-list factories that this project's suites feed
/// into the internal assertion payload-builder seam.
/// </summary>
/// <remarks>
/// <para>
/// Read this type's file header before adding a member: it records the frame text format the fixtures
/// conform to, the outermost-first ordering contract, the one-based to zero-based translation, and
/// the parse arithmetic at <c>assert.srf:L39-L59</c> from which every expected value documented here
/// was derived.
/// </para>
/// <para>
/// <b>Every expected parse in this type's documentation was hand-traced character position by
/// character position</b> against that arithmetic. Where the legacy's behaviour at a boundary is not
/// recorded anywhere in the repository - the one case is a negative length reaching <c>Mid</c> - the
/// documentation says so and names the ported implementation's documented choice rather than
/// presenting it as verified parity.
/// </para>
/// </remarks>
internal static class LegacyStackFrames
{
    // ==============================================================================================
    //  1. THE PROTOCOL CONSTANTS
    // ==============================================================================================

    /// <summary>
    /// The separator the payload builder joins trace frames with: a BARE LINE FEED.
    /// [assert.srf:L63]
    /// </summary>
    /// <remarks>
    /// Exposed so a suite composing an expected field 7 uses the same literal the production code
    /// does. It is a line feed and not a carriage-return pair because it lives INSIDE a payload field
    /// whose fields are delimited by CRLF [assert.srf:L19]; that asymmetry is the only reason the
    /// consumer's split yields exactly two fields or exactly seven [pfw.sra:L115,L119].
    /// </remarks>
    internal const string FrameSeparator = "\n";

    /// <summary>
    /// The largest frame count that still selects the SHALLOW two-field payload shape.
    /// [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// The legacy gate is <c>if nCount &gt; 2 then</c>, so <c>2</c> is the last count that fails it.
    /// Named so that neither this file nor a suite spells the boundary as a bare literal, and reused
    /// below as the trim width, which is the same <c>2</c> for the same reason: the two frames the
    /// gate has no use for are exactly the two the trim removes.
    /// </remarks>
    internal const int MaxShallowFrameCount = 2;

    /// <summary>
    /// The smallest frame count that selects the DEEP seven-field payload shape. [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// The first value that passes the gate, and therefore the interesting boundary for a suite that
    /// wants the deep shape with the least possible content. <see cref="WithDepth(int)"/> accepts it
    /// directly; <see cref="Deep()"/> is deliberately deeper than this, so that a deep list has
    /// recognisable content above the caller as well as below it.
    /// </remarks>
    internal const int MinimumDeepFrameCount = 3;

    // ==============================================================================================
    //  2. THE FRAME SHAPES THE PARSE BRANCHES ON
    // ==============================================================================================

    /// <summary>
    /// TWO DOTS - a declaring scope is present, so the window and the object DIFFER.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shaped after the behavioural oracle rather than an abstract example: <c>w_test_assert</c> is
    /// the oracle window, <c>cb_1</c> is the command button on it, and <c>clicked</c> is the event
    /// that drives the assertion [w_test_assert.srw:L137]. It is simultaneously a conforming instance
    /// of the producer's format - scope <c>w_test_assert.cb_1</c>, member <c>clicked</c>, tail
    /// <c>" line:137"</c> - so it pins a real shape and not a fictional one.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot at position 19, first dot at 14, and 14 &lt; 19 selects the
    /// two-or-more-dots branch [assert.srf:L42]:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>WindowMenu</c> = <c>"w_test_assert"</c>, the text before the FIRST dot
    /// [assert.srf:L44]</description></item>
    /// <item><description><c>Object</c> = <c>"cb_1"</c>, the text BETWEEN the two dots
    /// [assert.srf:L46]</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"clicked"</c>, from the last dot to the next space
    /// at position 27 [assert.srf:L55-L56]</description></item>
    /// <item><description><c>Line</c> = <c>137</c>, the digits after the colon at position 32
    /// [assert.srf:L58-L59]</description></item>
    /// </list>
    /// <para>
    /// <b>This is the shape for which the consumer PRINTS its window line</b>, because
    /// <c>WindowMenu</c> and <c>Object</c> differ and its test is
    /// <c>if Error.WindowMenu &lt;&gt; Error.Object</c> [pfw.sra:L131]. Compare
    /// <see cref="SingleDotFrame"/>, where they do not.
    /// </para>
    /// </remarks>
    internal const string TwoDotFrame = "w_test_assert.cb_1.clicked line:137";

    /// <summary>
    /// MANY DOTS - a fully namespaced managed frame, the shape a real capture actually produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The producer renders a namespace, then the enclosing type chain, then the method, all joined
    /// by dots, so a genuine managed frame carries FOUR dots rather than two. The parse has no
    /// three-or-more-dots branch - it looks only at the first and the last - so the observable
    /// consequence is worth pinning explicitly.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot at position 40, first dot at 15:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>WindowMenu</c> = <c>"PowerFramework"</c> - only the FIRST namespace
    /// segment</description></item>
    /// <item><description><c>Object</c> = <c>"Shared.Kernel.Predicates"</c> - everything between the
    /// first and last dots, dots included</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"IsSucceeded"</c></description></item>
    /// <item><description><c>Line</c> = <c>12</c></description></item>
    /// </list>
    /// <para>
    /// That split is NOT a defect and must not be "corrected": it is the legacy arithmetic applied
    /// faithfully to the new frame format, and the fields it produces are still the window-and-object
    /// pair the consumer prints [pfw.sra:L132,L134]. The text names a real member,
    /// <c>PowerFramework.Shared.Kernel.Predicates.IsSucceeded</c>, whose legacy original is
    /// <c>issucceeded.srf:L12</c> - the guard the long assertion overloads call - so the fixture reads
    /// as a plausible capture rather than as filler.
    /// </para>
    /// </remarks>
    internal const string NamespacedFrame =
        "PowerFramework.Shared.Kernel.Predicates.IsSucceeded line:12";

    /// <summary>
    /// EXACTLY ONE DOT - no declaring scope, so the window and the object are THE SAME VALUE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The non-visual class shape: a legacy <c>n_cst_*</c> object has no window or control above it,
    /// and the managed equivalent is a type declared in the global namespace, which the producer
    /// renders as a bare type chain with no namespace prefix. Both land here.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot and first dot are both position 19, so <c>nPos2 &lt; nPos</c> is
    /// false and the single-dot branch runs [assert.srf:L47-L52]:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>WindowMenu</c> = <c>"n_cst_thread_trans"</c>
    /// [assert.srf:L49]</description></item>
    /// <item><description><c>Object</c> = <c>"n_cst_thread_trans"</c> - <b>the same value</b>
    /// [assert.srf:L51]</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"of_connect"</c></description></item>
    /// <item><description><c>Line</c> = <c>42</c></description></item>
    /// </list>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR (C-B), AND IT IS DELIBERATE RATHER THAN A SLIP.</b> The equality
    /// is assigned outright at <c>assert.srf:L51</c> and it is OBSERVABLE: the consumer prints its
    /// window line only when the two differ [pfw.sra:L131], so it renders an object line alone for
    /// this shape. A port that gave <c>Object</c> some other value - a substring, an empty string, or
    /// anything "better" for the single-dot case - would silently add a window line to every error
    /// report the framework produces from a non-visual object. This fixture is the one that catches
    /// that, which is the only reason it exists.
    /// </para>
    /// </remarks>
    internal const string SingleDotFrame = "n_cst_thread_trans.of_connect line:42";

    /// <summary>
    /// EXACTLY ONE DOT, taken from the oracle: the window FUNCTION that actually calls the assertion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>wf_testassert</c> is the window function at <c>w_test_assert.srw:L39</c> whose body is
    /// <c>Assert(num &gt; 0)</c>, so in the oracle's own run THIS is the frame the payload blames -
    /// the frame at one-based <c>nCount - 2</c> [assert.srf:L38]. A window function has no control
    /// above it, so its frame carries exactly one dot and the window-equals-object branch is what the
    /// oracle exercises BY DEFAULT.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot and first dot are both position 14:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>WindowMenu</c> = <c>"w_test_assert"</c></description></item>
    /// <item><description><c>Object</c> = <c>"w_test_assert"</c> - the same value
    /// [assert.srf:L51]</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"wf_testassert"</c></description></item>
    /// <item><description><c>Line</c> = <c>39</c></description></item>
    /// </list>
    /// <para>
    /// It is kept ALONGSIDE <see cref="SingleDotFrame"/> rather than instead of it because the two
    /// prove different things. That one shows the branch reached from a non-visual class; this one
    /// shows the equality is what the oracle itself produces, so nobody can dismiss it as an artefact
    /// of a contrived example. It is the frame <see cref="OracleChain"/> blames.
    /// </para>
    /// </remarks>
    internal const string WindowFunctionFrame = "w_test_assert.wf_testassert line:39";

    /// <summary>
    /// The DEGRADED frame the producer emits when the runtime cannot describe a frame at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StackTraceProvider</c> substitutes an unknown-scope token and an unknown-member token when
    /// there is no stack frame or no reflected method, and a line number of zero when there is no
    /// portable symbol file. The result still satisfies the format's four invariants, so it parses
    /// rather than failing - and because it carries exactly one dot, it parses down the
    /// window-equals-object branch.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot and first dot are both position 10:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>WindowMenu</c> = <c>"&lt;unknown&gt;"</c></description></item>
    /// <item><description><c>Object</c> = <c>"&lt;unknown&gt;"</c> - the same value
    /// [assert.srf:L51]</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"?"</c> - one character, from the length
    /// <c>12 - 10 - 1</c></description></item>
    /// <item><description><c>Line</c> = <c>0</c></description></item>
    /// </list>
    /// <para>
    /// This is a REAL production shape, not a synthetic edge: a release build with no symbol file
    /// degrades every frame's line number to zero, and a frame the runtime cannot reflect degrades
    /// both tokens. Pinning it is what proves the parse never throws on the failure path - the one
    /// path that must not fail, since it is the path a failing assertion is already on.
    /// </para>
    /// </remarks>
    internal const string DegradedFrame = "<unknown>.? line:0";

    /// <summary>
    /// EDGE: no dot at all, so the window and the object are never assigned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE PRODUCER CANNOT EMIT THIS SHAPE.</b> Invariant I1 of its format contract guarantees at
    /// least one dot in every frame, including all four degraded cases. The legacy native primitive's
    /// output cannot be inspected either, because <c>pfw.dll</c> is closed and no source for it exists
    /// anywhere in the repository. This fixture therefore pins no producer behaviour; it exists for
    /// exactly one reason, which is to drive the FALSE arm of <c>if nPos &gt; 0</c> [assert.srf:L40] -
    /// an arm the legacy author wrote, which is reachable through the seam, and which is therefore
    /// part of the ported behaviour whether or not any producer reaches it.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. <c>LastPos</c> returns 0, so the entire dot block at
    /// <c>assert.srf:L40-L53</c> is skipped and NEITHER <c>WindowMenu</c> NOR <c>Object</c> is
    /// assigned - they keep whatever the failure record initialises them to, which is why they are
    /// still EQUAL and why the consumer still suppresses its window line [pfw.sra:L131]. The steps at
    /// <c>assert.srf:L54-L59</c> then run positionally regardless:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the space search restarts at position 1, because it is anchored at
    /// <c>nPos + 1</c> and <c>nPos</c> is 0, and finds position 13</description></item>
    /// <item><description><c>ObjectEvent</c> = <c>"dotlessframe"</c> - the WHOLE leading token, from
    /// the length <c>13 - 0 - 1</c></description></item>
    /// <item><description><c>Line</c> = <c>7</c>, from the colon at position 18</description></item>
    /// </list>
    /// <para>
    /// A consuming suite asserts only what the ported implementation defines for the two unassigned
    /// fields. This documentation deliberately does not name a value for them: it states that they are
    /// not written, which is the fact <c>assert.srf</c> establishes, and leaves the initial value to
    /// the failure record that owns it.
    /// </para>
    /// </remarks>
    internal const string DotlessFrame = "dotlessframe line:7";

    /// <summary>
    /// EDGE: no space after the method name, so the event window has a NEGATIVE length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE PRODUCER CANNOT EMIT THIS SHAPE EITHER.</b> Invariant I2 guarantees a space after the
    /// last dot, supplied by the leading space of the line-number introducer. This fixture is
    /// <see cref="TwoDotFrame"/> with that one space removed and NOTHING else changed, so a suite can
    /// compare the two directly and see that exactly one field moves.
    /// </para>
    /// <para>
    /// EXPECTED PARSE. Last dot 19, first dot 14, so the window and object split is IDENTICAL to
    /// <see cref="TwoDotFrame"/> - <c>"w_test_assert"</c> and <c>"cb_1"</c>. Then:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the space search finds nothing and yields 0, so the event length computes
    /// as <c>0 - 19 - 1</c>, which is <b>negative</b></description></item>
    /// <item><description>the colon search is anchored at that zero plus one, so it restarts at
    /// position 1 and still finds the colon at 27</description></item>
    /// <item><description><c>Line</c> = <c>137</c> - <b>the line number still parses</b>, which is
    /// the point: losing the event does not lose the location</description></item>
    /// </list>
    /// <para>
    /// <b>THE NEGATIVE LENGTH IS THE ONE EXPECTATION IN THIS FILE THAT IS A DOCUMENTED DECISION
    /// RATHER THAN VERIFIED PARITY (C-K).</b> PowerBuilder's exact behaviour for a negative length is
    /// recorded nowhere in this repository, because the runtime is closed and there is no
    /// specification in the tree to consult. The ported string primitives define it as the empty
    /// string and say so on themselves. A consuming suite therefore asserts what the port defines,
    /// and this fixture claims nothing more than the arithmetic above.
    /// </para>
    /// </remarks>
    internal const string NoSpaceAfterMethodFrame = "w_test_assert.cb_1.clicked:137";

    // ==============================================================================================
    //  3. THE TWO INNERMOST FRAMES, WHICH MUST BE TRIMMED FROM EVERYTHING
    // ==============================================================================================

    /// <summary>
    /// The SECOND innermost frame: the assertion guard that called the payload builder. It must
    /// appear in NEITHER the selected frame NOR the copied stack.
    /// </summary>
    /// <remarks>
    /// One-based position <c>nCount - 1</c>, C# index <c>frameCount - 2</c>. Its legacy original is
    /// the <c>assert(readonly boolean)</c> overload at <c>assert.srf:L89</c>, whose whole body is a
    /// guard and a delegation, and the line number in the text is that locator so the frame is
    /// traceable to the source it stands for. The selection at <c>assert.srf:L38</c> skips it and the
    /// trim at <c>assert.srf:L61</c> excludes it, which is exactly what makes the report name the
    /// USER's method instead of the assertion framework's.
    /// </remarks>
    internal const string AssertGuardFrame =
        "PowerFramework.Shared.Diagnostics.Assertions.Assert line:89";

    /// <summary>
    /// The INNERMOST frame: the payload builder itself. It must appear in NEITHER the selected frame
    /// NOR the copied stack.
    /// </summary>
    /// <remarks>
    /// One-based position <c>nCount</c>, C# index <c>frameCount - 1</c>. Its legacy original is
    /// <c>assertfailed</c> at <c>assert.srf:L16</c>, and it is the innermost frame for a structural
    /// reason worth knowing: the legacy capture primitive is a native export and therefore not a
    /// PowerScript frame [assert.srf:L30], and the managed provider reproduces that by excluding
    /// exactly its own frame - so in both systems the innermost frame is the routine that asked for
    /// the capture.
    /// </remarks>
    internal const string PayloadBuilderFrame =
        "PowerFramework.Shared.Diagnostics.Assertions.AssertFailed line:16";

    // ==============================================================================================
    //  4. THE OUTER FRAMES, AND THE CALLER THE DEEP LIST IS BUILT AROUND
    // ==============================================================================================

    /// <summary>
    /// An outer frame, above the caller: the oracle's second command button event.
    /// </summary>
    /// <remarks>
    /// <c>cb_3</c>'s <c>clicked</c> event wraps its call in a try/catch and reads the payload back
    /// [w_test_assert.srw:L97-L100], and the call it guards is on <c>:L98</c>, which is the line
    /// number in the text. Outer frames are never PARSED - the payload reads only the selected frame
    /// [assert.srf:L38] and copies the rest verbatim [assert.srf:L61-L64] - so what matters about
    /// this fixture is that it is recognisable and distinct, not that it is positioned realistically.
    /// </remarks>
    internal const string OutermostFrame = "w_test_assert.cb_3.clicked line:98";

    /// <summary>
    /// A second outer frame, between <see cref="OutermostFrame"/> and the caller: the oracle's second
    /// window function.
    /// </summary>
    /// <remarks>
    /// <c>wf_testassert2</c> at <c>w_test_assert.srw:L42</c> is the overload-with-info counterpart of
    /// <see cref="WindowFunctionFrame"/>, its body being
    /// <c>Assert(num &gt; 0,"Invalid Number!")</c>. Two outer frames rather than one, so that a deep
    /// list proves the trim keeps MORE than just the caller and a suite can assert the copied stack's
    /// ORDER rather than only its length.
    /// </remarks>
    internal const string SecondOutermostFrame = "w_test_assert.wf_testassert2 line:42";

    /// <summary>
    /// The frame <see cref="Deep()"/> is built around - the one the payload builder will select and
    /// blame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exposed as a named member for the reason AAP 0.4.5.4 exists: a suite that recomputed the
    /// position would be re-deriving <c>nCount - 2</c> as a zero-based index, and an off-by-one there
    /// would agree with an off-by-one in the code under test and pass. Assert against this value, not
    /// against an index.
    /// </para>
    /// <para>
    /// It is <see cref="TwoDotFrame"/>, so the DEFAULT deep list drives the two-or-more-dots branch
    /// and produces a window and an object that DIFFER - the shape whose window line the consumer
    /// prints [pfw.sra:L131]. Every other branch is driven by passing that frame to
    /// <see cref="Deep(string)"/> instead.
    /// </para>
    /// </remarks>
    internal const string ExpectedCallerFrame = TwoDotFrame;

    // ==============================================================================================
    //  5. THE FACTORIES
    // ==============================================================================================
    //  Every factory returns string[], which is exactly what the payload-builder seam accepts, and
    //  the seam takes the COUNT as a separate argument. That separation is the legacy's, not a
    //  convenience: the count is the capture primitive's return value held in its own local, and a
    //  swallowed capture failure leaves that local at zero INDEPENDENTLY of the array
    //  [assert.srf:L30-L32]. So a suite normally passes the array's own length, and a suite that
    //  wants the deliberately inconsistent pair passes something else on purpose.
    //
    //  No factory wraps the frames in a type of its own. A wrapper the seam cannot consume would be
    //  worse than useless - it would force every call site to unwrap, and the first unwrap written
    //  with an index would reintroduce the hazard this file exists to keep out.
    //
    //  Every returned array is FRESHLY ALLOCATED. Arrays are mutable and the fixtures are shared
    //  across suites in one process, so a cached array would let a suite that sorted or overwrote one
    //  in place corrupt every suite that ran after it.
    // ==============================================================================================

    /// <summary>
    /// Builds the default DEEP frame list - five frames, outermost first - whose selected frame is
    /// <see cref="ExpectedCallerFrame"/>.
    /// </summary>
    /// <returns>
    /// A fresh array of five frames: two outer frames, the caller, then the two framework frames.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Five frames, so the count comfortably passes the shallow gate
    /// (<see cref="MaxShallowFrameCount"/>) and the trimmed stack is three frames long rather than
    /// one - which is what lets a suite assert the copied stack's ORDER and not merely its length.
    /// </para>
    /// <para>
    /// The layout, and the arithmetic that makes the caller land where it does:
    /// </para>
    /// <list type="table">
    /// <listheader><term>index</term><description>frame</description></listheader>
    /// <item><term>0</term><description><see cref="OutermostFrame"/></description></item>
    /// <item><term>1</term><description><see cref="SecondOutermostFrame"/></description></item>
    /// <item><term>2</term><description><see cref="ExpectedCallerFrame"/> - SELECTED, because
    /// one-based <c>5 - 2</c> is C# index <c>5 - 3</c></description></item>
    /// <item><term>3</term><description><see cref="AssertGuardFrame"/> - trimmed</description></item>
    /// <item><term>4</term><description><see cref="PayloadBuilderFrame"/> - trimmed</description></item>
    /// </list>
    /// <para>
    /// <b>The ORDER of the two outer frames is a structural fixture, not a reconstruction of one real
    /// dispatch order</b>, and saying so is more useful than implying otherwise: each frame's TEXT is
    /// drawn from a real locator in the oracle window, but the payload never parses an outer frame, so
    /// their realism buys nothing while their identity buys everything. <see cref="OracleChain"/> is
    /// the factory whose order IS real, and it exists for exactly that reason.
    /// </para>
    /// </remarks>
    internal static string[] Deep()
    {
        return Deep(ExpectedCallerFrame);
    }

    /// <summary>
    /// Builds a DEEP frame list whose selected frame is <paramref name="callerFrame"/>, so that any
    /// parse branch can be driven without a suite computing a position.
    /// </summary>
    /// <param name="callerFrame">
    /// The frame to place in the caller slot. Any of this type's frame constants, or any text a suite
    /// wants parsed. The empty string is ALLOWED and is a meaningful case - it drives the no-dot,
    /// no-space and no-colon arms all at once - so it is not rejected.
    /// </param>
    /// <returns>
    /// A fresh array of five frames: two outer frames, <paramref name="callerFrame"/>, then the two
    /// framework frames.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callerFrame"/> is <see langword="null"/>. Rejected because a null frame is a
    /// fixture authoring mistake rather than a case the seam is specified for: the producer never
    /// returns a null element, so a suite passing one would be pinning nothing.
    /// </exception>
    /// <remarks>
    /// The surrounding four frames are identical to <see cref="Deep()"/>'s, which is what makes a
    /// branch comparison sound: only the caller slot changes, so any difference in the resulting
    /// payload is attributable to the frame under test and to nothing else.
    /// </remarks>
    internal static string[] Deep(string callerFrame)
    {
        ArgumentNullException.ThrowIfNull(callerFrame);

        // Assembled by listing outermost first, so the ordering contract is visible in the source
        // rather than encoded in index expressions. See the ordering contract in this file's header.
        return
        [
            OutermostFrame,         // index 0 - outermost, copied into the trace, never parsed
            SecondOutermostFrame,   // index 1 - copied into the trace, never parsed
            callerFrame,            // index 2 - SELECTED and blamed: one-based 5 - 2, C# 5 - 3
            AssertGuardFrame,       // index 3 - trimmed [assert.srf:L38,L61]
            PayloadBuilderFrame,    // index 4 - trimmed [assert.srf:L38,L61]
        ];
    }

    /// <summary>
    /// Builds the ORACLE's own four-frame chain, in its real dispatch order, as pressing the oracle
    /// window's first button produces it.
    /// </summary>
    /// <returns>A fresh array of four frames, outermost first.</returns>
    /// <remarks>
    /// <para>
    /// The one factory here whose ORDER is a fact rather than a fixture convention. Reading
    /// <c>w_test_assert.srw</c> outward from the assertion: <c>cb_1</c>'s <c>clicked</c> event calls
    /// the window function [<c>:L137</c>], the window function calls the guard [<c>:L39</c>], the
    /// guard calls the payload builder [<c>assert.srf:L91</c>], and the payload builder captures
    /// [<c>assert.srf:L30</c>]. Hence, outermost first:
    /// </para>
    /// <list type="table">
    /// <listheader><term>index</term><description>frame</description></listheader>
    /// <item><term>0</term><description><see cref="TwoDotFrame"/> - the button's
    /// event</description></item>
    /// <item><term>1</term><description><see cref="WindowFunctionFrame"/> - SELECTED, because
    /// one-based <c>4 - 2</c> is C# index <c>4 - 3</c></description></item>
    /// <item><term>2</term><description><see cref="AssertGuardFrame"/></description></item>
    /// <item><term>3</term><description><see cref="PayloadBuilderFrame"/></description></item>
    /// </list>
    /// <para>
    /// <b>Its selected frame is therefore the SINGLE-DOT one</b>, so the oracle's own default
    /// behaviour is the window-equals-object shape whose window line the consumer SUPPRESSES
    /// [pfw.sra:L131] - and the two-dot frame it also contains is merely copied into the trace. That
    /// is a genuinely counter-intuitive fact about the legacy, and it is the reason this factory is
    /// worth having next to <see cref="Deep()"/> rather than folded into it.
    /// </para>
    /// <para>
    /// Four frames is also the shortest deep list this file offers with fully recognisable content, so
    /// it doubles as the near-boundary case: one frame above the caller and two below it.
    /// </para>
    /// </remarks>
    internal static string[] OracleChain()
    {
        return [TwoDotFrame, WindowFunctionFrame, AssertGuardFrame, PayloadBuilderFrame];
    }

    /// <summary>
    /// Builds a SHALLOW frame list, whose count never exceeds <see cref="MaxShallowFrameCount"/> and
    /// which therefore drives the two-field payload shape.
    /// </summary>
    /// <param name="frameCount">
    /// The number of frames to build: <c>0</c>, <c>1</c> or <c>2</c>. Nothing else is a shallow count.
    /// </param>
    /// <returns>
    /// A fresh array of exactly <paramref name="frameCount"/> frames, outermost first.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="frameCount"/> is negative or greater than
    /// <see cref="MaxShallowFrameCount"/>. Both are rejected rather than clamped, because a fixture
    /// that silently returned a DEEP list from a member named <c>Shallow</c> would make a suite assert
    /// the wrong payload shape and still pass its own name.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECTS D2 AND D3 (C-B) ARE WHY THIS MEMBER EXISTS.</b> The legacy gate is
    /// <c>if nCount &gt; 2 then</c> [assert.srf:L37] and it is the ONLY gate between the two payload
    /// shapes: a count of two or less yields a payload with no window, no object, no event, no line
    /// number and no trace at all, silently. Zero is the count a FAILED CAPTURE leaves behind, because
    /// the legacy wraps the capture in a catch with an empty body and never reads the exception
    /// [assert.srf:L29-L32] - so the zero variant is not a theoretical edge, it is the shape the
    /// framework emits when its own diagnostics break.
    /// </para>
    /// <para>
    /// The content is chosen to match what a genuinely shallow capture would contain rather than to be
    /// arbitrary. The provider excludes only its own frame, so a capture taken from the payload builder
    /// with nothing above it sees the framework frames and nothing else:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>0</c> - empty, the swallowed-capture-failure shape</description></item>
    /// <item><description><c>1</c> - <see cref="PayloadBuilderFrame"/> alone</description></item>
    /// <item><description><c>2</c> - <see cref="AssertGuardFrame"/> then
    /// <see cref="PayloadBuilderFrame"/>, the boundary count, still shallow</description></item>
    /// </list>
    /// </remarks>
    internal static string[] Shallow(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, MaxShallowFrameCount);

        return frameCount switch
        {
            0 => [],
            1 => [PayloadBuilderFrame],

            // The only remaining value is MaxShallowFrameCount itself - the boundary count that still
            // fails the gate - because both guards above have already run.
            _ => [AssertGuardFrame, PayloadBuilderFrame],
        };
    }

    /// <summary>
    /// Builds a frame list of a requested length whose entries are individually distinguishable, for
    /// the theories where only the NUMBER of frames and their ORDER matter.
    /// </summary>
    /// <param name="depth">
    /// The number of frames to build. Zero is valid and yields an empty array.
    /// </param>
    /// <returns>A fresh array of exactly <paramref name="depth"/> frames, outermost first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="depth"/> is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Intended for the offset arithmetic of the four <c>StackTraceInfo</c> overloads, whose worker
    /// subtracts <c>offset + 1</c> from the count and then renders frames <c>1</c> through the
    /// remainder [stacktraceinfo.srf:L29-L36]. Those theories care how many lines come back and in
    /// what order, never what a frame says.
    /// </para>
    /// <para>
    /// <b>THE NUMBER IN EACH FRAME IS BOTH ITS ONE-BASED POSITION AND ITS LINE NUMBER</b>, which is a
    /// deliberate property rather than a coincidence: it lets an assertion name a position by VALUE
    /// instead of by index, so no test has to convert between the legacy's one-based positions and
    /// C#'s zero-based ones. See <see cref="DepthFrame(int)"/> for the text.
    /// </para>
    /// <para>
    /// Each generated frame conforms to the producer's format and carries exactly one dot, so if one
    /// of them is ever the selected frame it parses down the window-equals-object branch
    /// [assert.srf:L49-L51]. For a list of <paramref name="depth"/> frames passed with a matching
    /// count, the selected frame is the one numbered <c>depth - 2</c>.
    /// </para>
    /// <para>
    /// Built by APPENDING in ascending order, exactly as <c>assert.srf:L62</c> does, so that the frame
    /// numbered <c>i</c> lands at one-based position <c>i</c> with no index expression written
    /// anywhere.
    /// </para>
    /// </remarks>
    internal static string[] WithDepth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        List<string> frames = new(depth);
        for (int oneBasedIndex = 1; oneBasedIndex <= depth; oneBasedIndex++)
        {
            frames.Add(DepthFrame(oneBasedIndex));
        }

        return [.. frames];
    }

    /// <summary>
    /// Returns the text of the frame <see cref="WithDepth(int)"/> places at one-based position
    /// <paramref name="oneBasedIndex"/>.
    /// </summary>
    /// <param name="oneBasedIndex">
    /// The ONE-BASED position, counting from the outermost frame, exactly as <c>assert.srf</c> counts.
    /// </param>
    /// <returns>
    /// <c>pfw.frame&lt;n&gt; line:&lt;n&gt;</c>, where both occurrences of <c>n</c> are
    /// <paramref name="oneBasedIndex"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is less than <c>1</c>. Zero is rejected on purpose: there is
    /// no frame at one-based position zero, and accepting it would be the first step towards a suite
    /// treating these positions as zero-based.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Exists so a suite can name an expected frame by POSITION without indexing an array. Its parse,
    /// should it ever be the selected frame: last dot and first dot are both position 4, so
    /// <c>WindowMenu</c> and <c>Object</c> are both <c>"pfw"</c> [assert.srf:L49-L51],
    /// <c>ObjectEvent</c> is <c>"frame"</c> followed by the number, and <c>Line</c> is the number.
    /// </para>
    /// <para>
    /// The number is formatted with the invariant culture so no hosting locale can inject a group
    /// separator into a value the parse reads back numerically.
    /// </para>
    /// </remarks>
    internal static string DepthFrame(int oneBasedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(oneBasedIndex, 1);

        string position = oneBasedIndex.ToString(CultureInfo.InvariantCulture);
        return "pfw.frame" + position + " line:" + position;
    }

    /// <summary>
    /// Returns the frames the payload builder will copy into its trace - the legacy's
    /// <c>1</c> through <c>nCount - 2</c> - taking the count from the array's own length.
    /// </summary>
    /// <param name="callStack">The frame list, outermost first.</param>
    /// <returns>
    /// A fresh array holding the outer frames down to and including the caller, and NEITHER framework
    /// frame. Empty when the list is shallow.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callStack"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The convenience form for the ordinary case, where a suite passes the seam a count equal to the
    /// array's length - which every factory in this type produces. Use
    /// <see cref="ExpectedTrimmedStack(string[], int)"/> when the count is deliberately something
    /// else.
    /// </remarks>
    internal static string[] ExpectedTrimmedStack(string[] callStack)
    {
        ArgumentNullException.ThrowIfNull(callStack);

        return ExpectedTrimmedStack(callStack, callStack.Length);
    }

    /// <summary>
    /// Returns the frames the payload builder will copy into its trace for a given frame count - the
    /// legacy's <c>1</c> through <c>nCount - 2</c> [assert.srf:L61].
    /// </summary>
    /// <param name="callStack">The frame list, outermost first.</param>
    /// <param name="frameCount">The count the seam will be given.</param>
    /// <returns>
    /// A fresh array holding the outer frames down to and including the caller, and NEITHER framework
    /// frame. Empty when <paramref name="frameCount"/> does not exceed
    /// <see cref="MaxShallowFrameCount"/>, because the legacy loop then does not execute at all - and
    /// empty also because the shallow shape never reaches the loop.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callStack"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="frameCount"/> is negative, or greater than <paramref name="callStack"/>'s
    /// length.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE FILE'S SINGLE DEFINITION OF "TRIMMED", SO THE TRIMMING AND ROUND-TRIP SUITES
    /// CANNOT DISAGREE ABOUT IT.</b> The trim is expressed as a COUNT and never as a range of
    /// indices: the legacy loop runs one-based <c>1</c> to <c>nCount - 2</c>, which is precisely THE
    /// FIRST <c>frameCount - 2</c> ELEMENTS. Unlike the frame SELECTION, this expression does not
    /// shift by one when it is translated, and that asymmetry is the trap the header describes.
    /// </para>
    /// <para>
    /// <b>A COUNT LARGER THAN THE ARRAY IS REFUSED RATHER THAN CLAMPED (C-K).</b> That pair is a real
    /// test case - it is how a suite pins that the seam propagates the array's own out-of-range
    /// exception, which the ported accessor documents as the faithful behaviour - but it has no
    /// expected TRIMMED STACK, because the legacy would raise on the subscript rather than return a
    /// short list. Returning a silently clamped array here would hand that suite a plausible
    /// expectation for an operation that is specified to throw.
    /// </para>
    /// </remarks>
    internal static string[] ExpectedTrimmedStack(string[] callStack, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(callStack);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, callStack.Length);

        if (frameCount <= MaxShallowFrameCount)
        {
            // assert.srf:L37 never reaches the loop at all in the shallow shape, and even if it did,
            // `for nIndex = 1 to nCount - 2` with a bound below 1 would not execute. Both roads lead
            // here, so the empty result is returned once rather than reasoned about twice.
            return [];
        }

        // A COUNT, not an index: the first (frameCount - 2) elements are one-based 1 .. frameCount - 2.
        return [.. callStack.Take(frameCount - MaxShallowFrameCount)];
    }

    /// <summary>
    /// Returns the trace text the payload builder will produce for a frame list - the trimmed frames
    /// joined by <see cref="FrameSeparator"/>. [assert.srf:L63-L64]
    /// </summary>
    /// <param name="callStack">The frame list, outermost first.</param>
    /// <returns>
    /// The joined trace, or the empty string when the trimmed stack is empty. Never
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callStack"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Field 7 of the payload, and therefore the value the consumer prints as its call stack
    /// [pfw.sra:L124,L138]. It is composed from <see cref="ExpectedTrimmedStack(string[])"/> so that
    /// there is one definition of the trim and one definition of the separator, rather than a suite
    /// re-joining the frames and choosing a separator of its own.
    /// </para>
    /// <para>
    /// The separator sits BETWEEN entries only - never leading, never trailing - which is what the
    /// legacy's <c>if nIndex &gt; 1</c> guard produces [assert.srf:L63] and what a plain join
    /// reproduces exactly.
    /// </para>
    /// <para>
    /// Takes the count from the array's length, as <see cref="ExpectedTrimmedStack(string[])"/> does.
    /// A suite working with a deliberately different count joins
    /// <see cref="ExpectedTrimmedStack(string[], int)"/> with <see cref="FrameSeparator"/> itself,
    /// which keeps the unusual case visible at its call site instead of hiding it behind an overload.
    /// </para>
    /// </remarks>
    internal static string ExpectedStackTraceInfo(string[] callStack)
    {
        ArgumentNullException.ThrowIfNull(callStack);

        return string.Join(FrameSeparator, ExpectedTrimmedStack(callStack));
    }
}
