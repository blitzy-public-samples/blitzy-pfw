// ==================================================================================================
//  AssertPayloadProtocolTests.cs - THE PARITY SUITE FOR THE SEVEN-FIELD ASSERT PAYLOAD PROTOCOL
//  ------------------------------------------------------------------------------------------------
//  SUBJECT UNDER TEST
//      shared/PowerFramework.Shared.Diagnostics/Assert.cs, and specifically its internal payload
//      builder seam - the port of ws_objects/pfw.common.pbl.src/assert.srf:L16-L87.
//
//  WHY THIS SUITE EXISTS SEPARATELY FROM AssertGuardTests
//      The payload is the ONLY cross-service artifact this shared library produces. Every other
//      behaviour in the project stays inside the process that calls it; this one is a WIRE FORMAT
//      that a different service decodes [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. A delimiter
//      slip, a field-order slip, or an off-by-one in the frame arithmetic is therefore a
//      CROSS-SERVICE BREAK, and not one of the three produces a compiler diagnostic. Nothing but a
//      suite that asserts the bytes can catch them, which is why the field grammar gets a file of
//      its own rather than a handful of rows inside a broader one.
//
//      AssertGuardTests owns the six public guards - which argument values fire and which return
//      silently. This suite owns what the payload LOOKS LIKE once one of them has fired. Between
//      them every line of Assert.cs is reached, which is what the 80 percent line-coverage gate
//      requires of this project (C-H).
//
//  LEGACY PATHS READ AS SPECIFICATION. ALL FIVE ARE READ ONLY (C-C)
//      ws_objects/pfw.common.pbl.src/assert.srf              THE payload grammar. DELIMITER at
//                                                            :L19, field 1 at :L22, field 2 at
//                                                            :L24-L27, the swallowed capture at
//                                                            :L29-L32, the Info copy at :L35, the
//                                                            shape gate at :L37, the frame
//                                                            selection at :L38, the parse at
//                                                            :L39-L59, the trace loop at
//                                                            :L61-L65, fields 3-7 at :L66-L70,
//                                                            the Info suffix at :L71 and the join
//                                                            at :L74-L78.
//      ws_objects/pfw.common.pbl.src/assertionfailed.sru     the seven payload members [:L18-L24].
//      ws_objects/pfw.common.pbl.src/stacktrace.srf          the capture primitive [:L7].
//      ws_objects/pfw.pbl.src/pfw.sra                        THE CONSUMER [:L111-L144]. Every
//                                                            field count and every field
//                                                            expectation below is ultimately a
//                                                            statement about what this decoder
//                                                            will read.
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw        the behavioural oracle. Its
//                                                            info-carrying window function is at
//                                                            :L42 and supplies the oracle info
//                                                            string used as a theory row.
//
//  ================================================================================================
//  GOVERNING CONSTRAINTS
//  ================================================================================================
//  THERE ARE NO USER-SPECIFIED RULES FOR THIS PROJECT. The rules document was retrieved with the
//  rules-review facility and it contains exactly one statement: that no user rules were provided.
//  No rule is therefore invented, inferred or back-filled from convention in their place, and the
//  absence is NOT treated as licence to lower the bar. What binds instead is AAP 0.7.2, the
//  enterprise-standard baseline, and AAP 0.7.3, the twelve non-rule constraints. Six bind this
//  file:
//
//      C-A  No reference to anything under services/**. The decode side of the protocol is
//           exercised through this project's OWN LegacySystemErrorConsumer, which is a test double
//           living in this assembly - never through a service project. The only assertion here
//           that touches it is the delimiter-agreement check in
//           TheFieldDelimiterIsTheCarriageReturnLineFeedPairAndABareLineFeedIsNotASeparator, and
//           that is a same-assembly constant comparison.
//
//      C-B  Replicate legacy behaviour; correct nothing. THREE expectations in this file look
//           wrong and are right, and each carries a comment at the point it is pinned:
//             1. THE PAYLOAD HAS EXACTLY TWO POSSIBLE FIELD COUNTS - two and seven - and nothing
//                in between, because assert.srf:L37 is the only gate and the consumer tests for
//                EXACTLY seven [pfw.sra:L119].
//             2. THE SOURCE-LOCATION SUFFIX REACHES Info AND NEVER FIELD 2, because field 2 is
//                copied by value at :L35 and only Info is appended to at :L71.
//             3. THE TWO INNERMOST FRAMES ARE DISCARDED from the selected frame [:L38] and from
//                the copied stack [:L61], so the report names the CALLER and never the assertion
//                framework.
//
//      C-H  80 percent line coverage per project, measured from the Cobertura report. The theory
//           rows here are chosen to reach BOTH payload shapes, BOTH arms of the window/object
//           split, the no-dot arm of the parse, the copy loop, and every reachable branch of the
//           five one-based string primitives - including their zero and empty-string answers,
//           which an empty caller frame drives all at once.
//
//      C-C  ws_objects/** is specification only. This suite constructs every fixture in code and
//           takes no build-time or run-time dependency on any legacy path. Nothing under
//           ws_objects is opened, parsed or copied at run time.
//
//      C-D  No deferred capability area is named, referenced or stubbed anywhere in this file.
//
//      C-K  Name the technology-specific decisions in the tests that cover them. Three apply:
//
//             * THE FRAME ARRAY COMES FROM System.Diagnostics.StackTrace. The legacy captures with
//               a closed-binary external, `global function int StackTrace (ref string callstack[])
//               system library "pfw.dll" alias for "pfwStackTrace"`
//               [ws_objects/pfw.common.pbl.src/stacktrace.srf:L7]. No C++ source for pfw.dll
//               exists anywhere in the repository, so its frame text cannot be read and cannot be
//               reproduced by inspection. StackTraceProvider therefore SUBSTITUTES the managed
//               System.Diagnostics.StackTrace and DEFINES the frame format
//               `<namespace>.<typeChain>.<method> line:<lineNumber>` to satisfy the four
//               positional searches the legacy parser performs [assert.srf:L39,L41,L55,L58]. Every
//               synthetic frame in LegacyStackFrames conforms to that defined format, and the
//               real-capture fact at the end of this file is what proves the synthetic grammar and
//               the produced grammar are the same grammar.
//
//             * THE CLASS UNDER TEST IS NAMED Assertions, NOT Assert. The legacy function object
//               is named `assert` and declares a subroutine also named `assert`
//               [assert.srf:L3,L8], so the legacy-visible contract reads `Assert.Assert(...)`.
//               C# CANNOT EXPRESS THAT: a class may not contain a member whose name matches the
//               class name unless the member is a constructor, which is compiler error CS0542,
//               and the restriction applies to static members too. The members keep the legacy
//               names Assert and AssertFailed and the containing class is role-named Assertions,
//               matching the Predicates/Bits/Formatting convention the sibling Kernel project
//               established for ported global functions. A second, independent reason the same
//               ruling is required here: this file uses Xunit's own Assert type in every
//               assertion, and a production type named Assert in the enclosing
//               PowerFramework.Shared.Diagnostics namespace would collide with it.
//
//             * ONE EXPECTATION IN THIS FILE IS A PORT DECISION RATHER THAN VERIFIED PARITY, and
//               it is labelled where it is asserted: the empty event name produced when the
//               computed field width is NEGATIVE. PowerBuilder's behaviour for a negative Mid
//               length is documented nowhere in this repository, because the runtime is closed and
//               the tree carries no specification for it. Assert.cs's ported primitive defines it
//               as the empty string and says so on itself; this suite asserts what the port
//               defines and claims nothing about the original.
//
//  NAMING. The repository-root .editorconfig scopes its naming-analyzer suppressions to the named
//  PRODUCTION files on its BAND 3 roster - the single source of truth for that list - and none of
//  them is in this project, so no SCREAMING_SNAKE identifier may be
//  declared here - under the inherited TreatWarningsAsErrors it would be a build failure with no
//  route to an exception. Every identifier below is PascalCase. The legacy magic number and the
//  bare assertion text are legacy STRING LITERALS rather than identifiers, which is fine, and they
//  are held in conventionally named constants so no expectation is a retyped literal.
//
//  ================================================================================================
//  THE ONE-BASED TO ZERO-BASED TRANSLATION, WRITTEN OUT (AAP 0.4.5.4, RISK R9)
//  ================================================================================================
//  AAP 0.4.5.4 names one-based to zero-based translation the single most dangerous mechanical
//  hazard in this refactor, and AAP 0.8.6 carries it as risk R9, because a silent off-by-one is
//  indistinguishable from a behavioural regression. THIS IS WHERE IT BITES IN THIS PROJECT, and
//  the arithmetic is spelled out here once so that no assertion below has to re-derive it:
//
//      PAYLOAD FIELDS.   The legacy writes sMessages[1] .. sMessages[7] and the consumer reads
//                        sMessages[1] .. sMessages[7] [pfw.sra:L117-L124]. Both are ONE-BASED.
//                        This suite reads fields through PayloadFields.Field(oneBasedIndex), which
//                        performs the single subtraction in one place, exactly as the production
//                        code funnels every frame access through StackTraceProvider.FrameAt.
//
//      FRAME SELECTION.  `sCallStack[nCount - 2]` [assert.srf:L38] is a ONE-BASED SUBSCRIPT, so
//                        the equivalent C# index is `frameCount - 3`. THE EXPRESSION SHIFTS BY ONE
//                        WHEN IT IS TRANSLATED.
//
//      FRAME COPY.       `for nIndex = 1 to nCount - 2` [assert.srf:L61] is a COUNT of iterations
//                        starting at one, so the equivalent is THE FIRST `frameCount - 2`
//                        ELEMENTS. THIS EXPRESSION DOES NOT SHIFT.
//
//  The two expressions share the sub-term `nCount - 2` and translate DIFFERENTLY. That asymmetry
//  is the trap, and it is why this suite never computes a frame position of its own: the selected
//  frame is asserted against LegacyStackFrames.ExpectedCallerFrame and the copied stack against
//  LegacyStackFrames.ExpectedTrimmedStack, so an off-by-one in the code under test cannot be
//  matched by an agreeing off-by-one in the expectation.
//
//  ================================================================================================
//  WHY THE INTERNAL SEAM IS USED, AND WHY NOT REFLECTION
//  ================================================================================================
//  Assertions.BuildFailure is INTERNAL and this project is named in the Diagnostics project's
//  InternalsVisibleTo attribute for exactly this purpose. It is called directly, as a compiled
//  reference. Reflection is deliberately not used: a reflective call would not fail to compile
//  when the signature changed, so the one protection this suite has against a silent contract drift
//  would be lost.
//
//  The seam takes the frame COUNT separately from the frame ARRAY, and that separation is the
//  legacy's rather than a convenience: the count is the capture primitive's return value held in
//  its own local, and the swallowed capture failure leaves that local at zero INDEPENDENTLY of the
//  array [assert.srf:L29-L32]. Most rows below pass the array's own length; the rows that pass
//  something else do so on purpose and say so.
// ==================================================================================================

// Exactly four namespaces, and every one of them is used by simple name in the body below:
//   System                             ArgumentOutOfRangeException, in the frame-list key resolver
//   System.Globalization               CultureInfo and NumberStyles, for the invariant-culture parses
//                                      and renderings that field 1 and field 6 are pinned against
//   System.Runtime.CompilerServices    MethodImplOptions, for the frame-preservation attributes on the
//                                      real-capture helpers
//   Xunit                              the test framework
// Nothing else is imported. In particular no legacy path is referenced, no service project is
// referenced (C-A), and no reflection namespace appears - the internal payload-builder seam is reached
// as a compiled reference through InternalsVisibleTo, never reflectively.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Parity suite for the seven-field assertion payload protocol built by
/// <c>Assertions.BuildFailure</c>, ported from <c>ws_objects/pfw.common.pbl.src/assert.srf</c>.
/// </summary>
/// <remarks>
/// Table-driven throughout: every expectation that varies across inputs is a theory fed from member
/// data, so a new frame shape or a new frame depth is a row rather than a method. The facts are
/// reserved for the singular claims - the shape boundary, the delimiter discipline, the
/// location-suffix divergence and the one real-capture sanity check.
/// </remarks>
public class AssertPayloadProtocolTests
{
    // ==============================================================================================
    //  THE PROTOCOL LITERALS, HELD ONCE
    // ==============================================================================================

    /// <summary>
    /// The FIELD delimiter: the carriage-return/line-feed PAIR. [assert.srf:L19]
    /// </summary>
    /// <remarks>
    /// Declared here rather than imported so that this suite states the delimiter INDEPENDENTLY of
    /// the code under test - an expectation that read its own delimiter from the producer could not
    /// detect the producer changing it. The producer and the consumer are held to this same value by
    /// <see cref="TheFieldDelimiterIsTheCarriageReturnLineFeedPairAndABareLineFeedIsNotASeparator"/>.
    /// </remarks>
    private const string FieldDelimiter = "\r\n";

    /// <summary>
    /// The separator used INSIDE a field: a bare line feed. [assert.srf:L26,L63,L71]
    /// </summary>
    private const string IntraFieldSeparator = "\n";

    /// <summary>
    /// Field 1 of every payload, in every shape, as text. [assert.srf:L22]
    /// </summary>
    private const string ErrorNumberField = "-10000";

    /// <summary>
    /// The number field 1 parses back to, which the consumer assigns to its error number.
    /// [pfw.sra:L117]
    /// </summary>
    private const long ExpectedErrorNumber = -10000L;

    /// <summary>
    /// Field 2 before any info is appended. [assert.srf:L24]
    /// </summary>
    private const string BareAssertionText = "Assertion failed";

    /// <summary>
    /// The oracle's own info string, from the window function that calls the info-carrying guard.
    /// [w_test_assert.srw:L42]
    /// </summary>
    private const string OracleInfo = "Invalid Number!";

    /// <summary>
    /// The opening of the source-location suffix that reaches <c>Info</c> and never field 2.
    /// [assert.srf:L71]
    /// </summary>
    private const string LocationSuffixOpening = "\nat ";

    /// <summary>
    /// The field count of the SHALLOW payload shape. [assert.srf:L37, pfw.sra:L116]
    /// </summary>
    private const int ShallowFieldCount = 2;

    /// <summary>
    /// The field count of the DEEP payload shape, which the consumer tests for EXACTLY.
    /// [assert.srf:L66-L70, pfw.sra:L119]
    /// </summary>
    private const int DeepFieldCount = 7;

    // ==============================================================================================
    //  SECTION A - THE TWO PAYLOAD SHAPES, AND ONLY TWO
    // ==============================================================================================
    //  The legacy gate is a single comparison, `if nCount > 2 then` [assert.srf:L37], and the field
    //  join takes its element count from `UpperBound(sMessages)` [assert.srf:L74], which is 2 or 7
    //  and nothing else.
    //
    //  PRESERVED LEGACY BEHAVIOUR, DELIBERATELY PINNED (C-B). The consumer requires AT LEAST two
    //  fields [pfw.sra:L116] and then tests for EXACTLY seven [pfw.sra:L119] - never for at least
    //  seven. So a port that always emitted seven fields would break the shallow direction by
    //  reporting a window, an object, an event and a line number that the capture never produced,
    //  and a port that always emitted two would break the deep direction by silently discarding all
    //  five of them. Both failures are invisible to a compiler. These are the rows that catch them.
    // ==============================================================================================

    /// <summary>
    /// Every frame count that selects the shallow shape: zero, one and two. [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// Zero is included because it is the count a FAILED CAPTURE leaves behind - the legacy wraps
    /// the capture in a <c>catch</c> with an empty body and never reads the caught variable
    /// [assert.srf:L29-L32] - so it is a production shape rather than a synthetic edge.
    /// </remarks>
    public static TheoryData<int> ShallowFrameCounts =>
        new(0, 1, LegacyStackFrames.MaxShallowFrameCount);

    /// <summary>
    /// Frame counts at and above four, all of which select the deep shape.
    /// </summary>
    /// <remarks>
    /// The boundary count of three is deliberately NOT here: it is asserted on its own, next to the
    /// count of two, by <see cref="TheShapeGateIsStrictlyGreaterThanTwo"/>, so that the boundary
    /// cannot be lost inside a list.
    /// </remarks>
    public static TheoryData<int> DeepFrameCounts => new(4, 5, 6, 9, 12);

    /// <summary>
    /// Every frame count from zero to twelve, for the theory that pins the ABSENCE of a third shape.
    /// </summary>
    public static TheoryData<int> AllFrameCounts
    {
        get
        {
            TheoryData<int> data = [];
            for (int frameCount = 0; frameCount <= 12; frameCount++)
            {
                data.Add(frameCount);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ShallowFrameCounts))]
    public void AShallowStackProducesExactlyTwoFields(int frameCount)
    {
        string[] callStack = LegacyStackFrames.Shallow(frameCount);

        PayloadFields fields = BuildPayload(callStack, frameCount, string.Empty);

        // PRESERVED LEGACY BEHAVIOUR (C-B). Exactly two, never more. There is no window, no object,
        // no event, no line number and no trace in this shape, and the consumer is written to expect
        // precisely that [pfw.sra:L116,L119].
        Assert.Equal(ShallowFieldCount, fields.Count);
        Assert.Equal(ErrorNumberField, fields.Number);
        Assert.Equal(BareAssertionText, fields.Text);
    }

    [Theory]
    [MemberData(nameof(DeepFrameCounts))]
    public void ADeepStackProducesExactlySevenFields(int frameCount)
    {
        string[] callStack = LegacyStackFrames.WithDepth(frameCount);

        PayloadFields fields = BuildPayload(callStack, frameCount, string.Empty);

        // PRESERVED LEGACY BEHAVIOUR (C-B). EXACTLY seven, because the consumer's branch is
        // `if nCount = 7` and not `if nCount >= 7` [pfw.sra:L119]. An eighth field would fail that
        // equality and cost the consumer the window, the object, the event, the line number and the
        // whole trace in one go - silently, since it would still have decoded fields 1 and 2.
        Assert.Equal(DeepFieldCount, fields.Count);
    }

    [Fact]
    public void TheShapeGateIsStrictlyGreaterThanTwo()
    {
        // assert.srf:L37 - `if nCount > 2 then`. STRICTLY greater. Two frames is the last count that
        // fails it and THREE FRAMES ALREADY PRODUCE THE FULL PAYLOAD.
        string[] atTheGate = LegacyStackFrames.Shallow(LegacyStackFrames.MaxShallowFrameCount);
        string[] justPastIt = LegacyStackFrames.WithDepth(LegacyStackFrames.MinimumDeepFrameCount);

        // A port written with `>= 2` fails the first of these; a port written with `> 3` fails the
        // second. Only the legacy's own `> 2` satisfies both.
        Assert.Equal(ShallowFieldCount, BuildPayload(atTheGate, string.Empty).Count);
        Assert.Equal(DeepFieldCount, BuildPayload(justPastIt, string.Empty).Count);
    }

    [Theory]
    [MemberData(nameof(AllFrameCounts))]
    public void NoFrameCountProducesAnyShapeOtherThanTwoOrSevenFields(int frameCount)
    {
        string[] callStack = frameCount <= LegacyStackFrames.MaxShallowFrameCount
            ? LegacyStackFrames.Shallow(frameCount)
            : LegacyStackFrames.WithDepth(frameCount);

        PayloadFields fields = BuildPayload(callStack, frameCount, OracleInfo);

        // PRESERVED LEGACY BEHAVIOUR (C-B). `UpperBound(sMessages)` [assert.srf:L74] can only be 2
        // or 7, because fields 3 to 7 are written together inside one guard [assert.srf:L66-L70] and
        // are never written individually. A partially populated payload is not a shape the protocol
        // has, and introducing one - by emitting field 3 when a dot was found but not field 6, say -
        // would fail the consumer's equality test without failing anything a compiler checks.
        int expected = frameCount > LegacyStackFrames.MaxShallowFrameCount
            ? DeepFieldCount
            : ShallowFieldCount;

        Assert.Equal(expected, fields.Count);
    }

    [Fact]
    public void TheFieldDelimiterIsTheCarriageReturnLineFeedPairAndABareLineFeedIsNotASeparator()
    {
        // THE SINGLE CHEAPEST GUARD IN THIS FILE against the most likely regression in it. The
        // payload uses TWO different newline forms for two different jobs: CRLF between FIELDS
        // [assert.srf:L19] and a BARE LINE FEED inside a field - the info continuation
        // [assert.srf:L26] and the trace join [assert.srf:L63]. Collapse the two and the consumer's
        // split [pfw.sra:L115] shreds fields 2 and 7 into extra segments, the exactly-seven test
        // [pfw.sra:L119] fails, and the window, object, event, line number and trace all vanish at
        // once. Nothing about that failure is visible to a compiler.
        //
        // The payload below is constructed so that BOTH multi-line fields are populated: the info
        // makes field 2 two lines, and a five-frame stack makes field 7 three lines.
        string[] callStack = LegacyStackFrames.Deep();
        AssertionFailure failure = Assertions.BuildFailure(callStack, callStack.Length, OracleInfo);
        string payload = failure.Message;

        PayloadFields fields = PayloadFields.SplitAsTheConsumerDoes(payload);

        // Both fields really do carry bare line feeds, so the guard below is guarding something.
        Assert.Contains(IntraFieldSeparator, fields.Text);
        Assert.Contains(IntraFieldSeparator, fields.StackTraceInfo);

        // And splitting on the CRLF PAIR still yields exactly seven segments.
        Assert.Equal(DeepFieldCount, fields.Count);

        // Whereas splitting on a bare line feed yields strictly MORE, which is the whole point: the
        // line feed is not a field separator, so a port that used one would report a field count no
        // consumer branch matches.
        string[] asIfLineFeedWereTheDelimiter = payload.Split(IntraFieldSeparator);
        Assert.True(
            asIfLineFeedWereTheDelimiter.Length > DeepFieldCount,
            "A bare line feed must not be the field separator, so splitting on one must over-split.");

        // No field other than 2 and 7 may contain a line feed at all: fields 1 and 3 to 6 are single
        // tokens by construction [assert.srf:L22,L66-L69].
        Assert.DoesNotContain(IntraFieldSeparator, fields.Number);
        Assert.DoesNotContain(IntraFieldSeparator, fields.WindowMenu);
        Assert.DoesNotContain(IntraFieldSeparator, fields.Object);
        Assert.DoesNotContain(IntraFieldSeparator, fields.ObjectEvent);
        Assert.DoesNotContain(IntraFieldSeparator, fields.LineText);

        // PRODUCER AND CONSUMER MUST AGREE ON THE DELIMITER, and the decode side of the protocol
        // lives in this assembly's own test double rather than in any service project (C-A). If one
        // side is ever respelled, this equality is what fails first.
        Assert.Equal(FieldDelimiter, LegacySystemErrorConsumer.FieldDelimiter);

        // AND THE TWO INTRA-FIELD SEPARATORS MUST AGREE WITH EACH OTHER. The legacy uses one line feed
        // for two distinct jobs - the info continuation [assert.srf:L26] and the trace join
        // [assert.srf:L63] - and the shared fixtures name only the second. This equality is what stops
        // the two drifting apart into a file that would then need two different split literals.
        Assert.Equal(IntraFieldSeparator, LegacyStackFrames.FrameSeparator);

        // Neither intra-field separator may BE the field delimiter, which is the whole asymmetry.
        Assert.NotEqual(FieldDelimiter, IntraFieldSeparator);
    }

    // ==============================================================================================
    //  SECTION B - FIELD 1 AND FIELD 2
    // ==============================================================================================

    /// <summary>
    /// One shallow and one deep frame list, so a claim about fields 1 and 2 can be shown to hold in
    /// BOTH payload shapes rather than in whichever one a single row happened to pick.
    /// </summary>
    /// <remarks>
    /// The key is resolved by <see cref="ResolveCallStack"/>. Keys rather than arrays because a
    /// theory row reads far better in a test-run report as a name than as a rendered array, and
    /// because the resolver is the one place a factory choice can be corrected.
    /// </remarks>
    public static TheoryData<string> BothPayloadShapes => new("Shallow", "Deep");

    [Theory]
    [MemberData(nameof(BothPayloadShapes))]
    public void FieldOneIsTheLiteralMagicNumberStringInEveryShape(string shapeKey)
    {
        string[] callStack = ResolveCallStack(shapeKey);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // assert.srf:L22 - `sMessages[1] = "-10000"`. A STRING, assigned before the shape gate is
        // even reached, so it is identical in both shapes.
        Assert.Equal(ErrorNumberField, fields.Number);
    }

    [Fact]
    public void FieldOneParsesBackToTheNegativeErrorNumberTheConsumerAssigns()
    {
        string[] callStack = LegacyStackFrames.Deep();

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // BOTH REPRESENTATIONS MATTER, WHICH IS WHY THEY ARE ASSERTED SEPARATELY. The producer emits
        // field 1 as TEXT [assert.srf:L22] and the consumer converts it to a NUMBER on the way in:
        // `Error.Number = Long(sMessages[1])` [pfw.sra:L117]. The string equality above pins the
        // wire form; this parse pins the value the consumer will actually hold. A port that emitted
        // the same number formatted differently - with a plus sign, with padding, or with a locale's
        // own negative sign - would satisfy one of these two assertions and not the other.
        long asTheConsumerReadsIt = long.Parse(
            fields.Number,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);

        Assert.Equal(ExpectedErrorNumber, asTheConsumerReadsIt);
        Assert.True(asTheConsumerReadsIt < 0, "The assertion error number is negative.");
    }

    [Theory]
    [MemberData(nameof(BothPayloadShapes))]
    public void FieldTwoWithNoInfoIsExactlyTheBareAssertionText(string shapeKey)
    {
        string[] callStack = ResolveCallStack(shapeKey);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // assert.srf:L24 - `sMessages[2] = "Assertion failed"`, with the append at :L25-L27 skipped
        // because the info is empty. Exact equality, so no continuation and no trailing separator
        // can hide in it.
        Assert.Equal(BareAssertionText, fields.Text);
    }

    /// <summary>
    /// The info strings whose appended form is pinned, paired with the shape they are pinned in.
    /// </summary>
    /// <remarks>
    /// The oracle's own string [w_test_assert.srw:L42] is the first row, because it is the only info
    /// value the legacy repository actually produces. The rest widen it: a value containing a bare
    /// line feed, which is legal and must survive verbatim; one containing a colon and a dot, the
    /// two characters the frame parser searches for, to show that field 2 is never parsed; and one
    /// containing leading and trailing spaces, to show that nothing trims it.
    /// </remarks>
    public static TheoryData<string, string> InfoAppendMatrix
    {
        get
        {
            TheoryData<string, string> data = [];
            foreach (string shapeKey in new string[] { "Shallow", "Deep" })
            {
                data.Add(shapeKey, OracleInfo);
                data.Add(shapeKey, "first line" + IntraFieldSeparator + "second line");
                data.Add(shapeKey, "n_cst_thread_trans.of_connect line:42");
                data.Add(shapeKey, "   padded   ");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InfoAppendMatrix))]
    public void FieldTwoWithInfoIsTheBareTextThenOneLineFeedThenTheInfoVerbatim(
        string shapeKey,
        string info)
    {
        string[] callStack = ResolveCallStack(shapeKey);

        PayloadFields fields = BuildPayload(callStack, info);

        // assert.srf:L25-L27 - `if info <> "" then sMessages[2] += "~n" + info end if`. ONE bare line
        // feed, then the info UNCHANGED: not trimmed, not normalised, not escaped, and not converted
        // to the field delimiter.
        Assert.Equal(BareAssertionText + IntraFieldSeparator + info, fields.Text);
    }

    [Theory]
    [MemberData(nameof(BothPayloadShapes))]
    public void AnEmptyInfoAppendsNothingAndLeavesNoTrailingLineFeed(string shapeKey)
    {
        string[] callStack = ResolveCallStack(shapeKey);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // The legacy test is against THE EMPTY STRING [assert.srf:L25], so an empty info takes the
        // false arm and the separator is never written. A port that appended unconditionally would
        // leave a trailing line feed on field 2 in every payload the info-less guards produce -
        // which is most of them.
        Assert.Equal(BareAssertionText, fields.Text);
        Assert.DoesNotContain(IntraFieldSeparator, fields.Text);
    }

    /// <summary>
    /// The info values used to show that field 2 is byte-identical in both shapes.
    /// </summary>
    public static TheoryData<string> InfoValuesForTheDivergence => new(string.Empty, OracleInfo);

    [Theory]
    [MemberData(nameof(InfoValuesForTheDivergence))]
    public void FieldTwoNeverCarriesTheSourceLocationSuffixEvenOnADeepStack(string info)
    {
        string[] deepStack = LegacyStackFrames.Deep();
        string[] shallowStack = LegacyStackFrames.Shallow(LegacyStackFrames.MaxShallowFrameCount);

        AssertionFailure deepFailure =
            Assertions.BuildFailure(deepStack, deepStack.Length, info);
        PayloadFields deepFields = PayloadFields.SplitAsTheConsumerDoes(deepFailure.Message);
        PayloadFields shallowFields = BuildPayload(shallowStack, info);

        // ------------------------------------------------------------------------------------------
        // PRESERVED LEGACY BEHAVIOUR, AND THE EXACT DEFECT-SHAPED SUBTLETY A PORT GETS WRONG (C-B).
        // ------------------------------------------------------------------------------------------
        // The mechanism is two lines far apart. assert.srf:L35 copies field 2 into the failure's
        // Info member BY VALUE - `ex.#Info = sMessages[2]` - and assert.srf:L71 appends the source
        // location to Info ALONE. Field 2 is never re-read after :L35, so it does not receive the
        // suffix and the two strings DIVERGE in the deep shape.
        //
        // A port that kept ONE shared string and appended to it - which compiles, reads tidier, and
        // still produces seven fields - would make the consumer report the location twice: once in
        // its message line, from field 2 [pfw.sra:L118,L130], and again wherever Info is displayed.
        // THAT IS THE FAILURE THIS ROW EXISTS TO CATCH, and no compiler and no field-count assertion
        // can see it.
        //
        // The claim is therefore expressed as an EQUALITY BETWEEN SHAPES: deep field 2 must be the
        // same string a shallow stack would have produced, because the shallow shape never reaches
        // :L71 at all.
        Assert.Equal(shallowFields.Text, deepFields.Text);
        Assert.DoesNotContain(LocationSuffixOpening, deepFields.Text);

        // AND THE OTHER HALF OF THE DIVERGENCE, WITHOUT WHICH THE ASSERTION ABOVE IS HOLLOW: a port
        // that never appended the suffix ANYWHERE would satisfy it too. Info must carry the suffix,
        // composed exactly as assert.srf:L71 composes it, from fields 4, 5 and 6.
        string expectedSuffix = LocationSuffixOpening + deepFields.Object + "::"
            + deepFields.ObjectEvent + "(" + deepFields.LineText + ")";

        Assert.Equal(deepFields.Text + expectedSuffix, deepFailure.Info);

        // AssertionFailureTests owns the positive assertions about the Info member itself - that it
        // round-trips, that it is independent of Message, and that the carrier type does not decorate
        // either. This row owns only the DIVERGENCE between Info and field 2, which is a property of
        // the PRODUCER and therefore belongs with the payload grammar.
    }

    // ==============================================================================================
    //  SECTION C - THE FRAME PARSE, FIELDS 3 TO 6
    // ==============================================================================================
    //  Hand-traced from assert.srf:L38-L59 and cross-checked character position by character position
    //  against every fixture's own documented expectation in LegacyStackFrames. The parse is four
    //  positional searches and four substrings:
    //
    //      nPos  = LastPos(frame, ".")            [:L39]
    //      nPos2 = Pos(frame, ".")                [:L41]   two or more dots when nPos2 < nPos
    //      field 3 = Left(frame, nPos2 - 1)       [:L44]   text BEFORE the first dot
    //      field 4 = Mid(frame, nPos2 + 1,
    //                    nPos - nPos2 - 1)        [:L46]   text BETWEEN the first and last dots
    //      nPos2 = Pos(frame, " ", nPos + 1)      [:L55]   the first space AFTER the last dot
    //      field 5 = Mid(frame, nPos + 1,
    //                    nPos2 - nPos - 1)        [:L56]   the method name
    //      nPos  = Pos(frame, ":", nPos2 + 1)     [:L58]
    //      field 6 = String(Long(Mid(frame,
    //                    nPos + 1)))              [:L59,L69]  the digits after the colon
    //
    //  The event and line steps sit OUTSIDE the dot guard, which is why a frame with no dot still
    //  produces a seven-field payload with an event and a line number and two empty fields.
    // ==============================================================================================

    /// <summary>
    /// The TWO-OR-MORE-DOT frames, whose window and object DIFFER. [assert.srf:L42-L46]
    /// </summary>
    /// <remarks>
    /// Columns: frame, expected field 3, expected field 4, expected field 5, expected field 6. Two
    /// rows, and they are not redundant: the first is the legacy control shape with exactly two dots,
    /// and the second is the FOUR-dot shape a real managed capture produces, which proves the parse
    /// takes only the FIRST and LAST dots and treats everything between them - dots included - as
    /// the object.
    /// </remarks>
    public static TheoryData<string, string, string, string, string> TwoDotParseMatrix =>
        new()
        {
            {
                LegacyStackFrames.TwoDotFrame,
                "w_test_assert", "cb_1", "clicked", "137"
            },
            {
                LegacyStackFrames.NamespacedFrame,
                "PowerFramework", "Shared.Kernel.Predicates", "IsSucceeded", "12"
            },
        };

    [Theory]
    [MemberData(nameof(TwoDotParseMatrix))]
    public void ATwoDotFrameSplitsIntoTheWindowTheObjectTheEventAndTheLine(
        string callerFrame,
        string expectedWindowMenu,
        string expectedObject,
        string expectedObjectEvent,
        string expectedLineText)
    {
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        Assert.Equal(DeepFieldCount, fields.Count);
        Assert.Equal(expectedWindowMenu, fields.WindowMenu);
        Assert.Equal(expectedObject, fields.Object);
        Assert.Equal(expectedObjectEvent, fields.ObjectEvent);
        Assert.Equal(expectedLineText, fields.LineText);

        // AND THE CONSEQUENCE AT THE CONSUMER: because the two differ, the consumer's own test
        // `if Error.WindowMenu <> Error.Object` [pfw.sra:L131] is TRUE, so it PRINTS its window line.
        // Compare the one-dot theory below, where the identical test suppresses that line.
        Assert.NotEqual(fields.WindowMenu, fields.Object);
    }

    /// <summary>
    /// The EXACTLY-ONE-DOT frames, whose window and object are the SAME value. [assert.srf:L47-L52]
    /// </summary>
    /// <remarks>
    /// Columns: frame, expected shared field 3 and 4 value, expected field 5, expected field 6. Four
    /// rows covering the four ways a real single-dot frame arises - a non-visual class, the oracle's
    /// own window function, the producer's fully degraded frame, and a generated positional frame -
    /// so the equality cannot be dismissed as an artefact of one contrived example.
    /// </remarks>
    public static TheoryData<string, string, string, string> OneDotParseMatrix =>
        new()
        {
            {
                LegacyStackFrames.SingleDotFrame,
                "n_cst_thread_trans", "of_connect", "42"
            },
            {
                LegacyStackFrames.WindowFunctionFrame,
                "w_test_assert", "wf_testassert", "39"
            },
            {
                LegacyStackFrames.DegradedFrame,
                "<unknown>", "?", "0"
            },
            {
                LegacyStackFrames.DepthFrame(7),
                "pfw", "frame7", "7"
            },
        };

    [Theory]
    [MemberData(nameof(OneDotParseMatrix))]
    public void AOneDotFrameMakesFieldThreeAndFieldFourTheSameValue(
        string callerFrame,
        string expectedSharedScope,
        string expectedObjectEvent,
        string expectedLineText)
    {
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // ------------------------------------------------------------------------------------------
        // PRESERVED LEGACY BEHAVIOUR, DELIBERATELY PINNED (C-B), AND IT IS OBSERVABLE.
        // ------------------------------------------------------------------------------------------
        // assert.srf:L51 assigns the object FROM the window outright - `ex.#Object = ex.#WindowMenu`.
        // The equality is asserted here as an EQUALITY BETWEEN THE TWO FIELDS and not merely as two
        // individual values, because the equality is the load-bearing fact: the consumer prints its
        // window line only when the two DIFFER [pfw.sra:L131], so it is this equality that SUPPRESSES
        // that line. A port that "helpfully" gave field 4 a distinct value for the single-dot case -
        // a substring, an empty string, or anything tidier - would add a spurious window line to
        // every error report the framework produces from a non-visual object, and every individual
        // field assertion would still pass.
        Assert.Equal(fields.WindowMenu, fields.Object);

        Assert.Equal(DeepFieldCount, fields.Count);
        Assert.Equal(expectedSharedScope, fields.WindowMenu);
        Assert.Equal(expectedSharedScope, fields.Object);
        Assert.Equal(expectedObjectEvent, fields.ObjectEvent);
        Assert.Equal(expectedLineText, fields.LineText);
    }

    /// <summary>
    /// Event-name and line-number extraction across every dot count the parse can see.
    /// [assert.srf:L55-L59]
    /// </summary>
    /// <remarks>
    /// Columns: frame, expected field 5, expected field 6. This matrix exists SEPARATELY from the two
    /// dot-count matrices above because the event and line steps sit OUTSIDE the dot guard
    /// [assert.srf:L54-L59] and therefore have to be shown to work independently of it - the two-dot
    /// rows, the one-dot rows and the no-dot row all appear here together.
    /// <para>
    /// The line numbers deliberately span ONE, TWO, THREE and FIVE digits - 0, 7, 12, 39, 42, 137 and
    /// 12345 - because a port that read a single character after the colon, or that assumed a fixed
    /// width, would pass a one-digit row and fail the rest. The five-digit row also crosses the
    /// thousands boundary, which is what makes the group-separator claim in
    /// <see cref="FieldSixIsThePlainDecimalRenderingWithNoSeparatorAndNoPadding"/> testable at all.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> EventAndLineMatrix =>
        new()
        {
            { LegacyStackFrames.TwoDotFrame, "clicked", "137" },
            { LegacyStackFrames.NamespacedFrame, "IsSucceeded", "12" },
            { LegacyStackFrames.SingleDotFrame, "of_connect", "42" },
            { LegacyStackFrames.WindowFunctionFrame, "wf_testassert", "39" },
            { LegacyStackFrames.DegradedFrame, "?", "0" },
            { LegacyStackFrames.DotlessFrame, "dotlessframe", "7" },
            { LegacyStackFrames.DepthFrame(12345), "frame12345", "12345" },
        };

    [Theory]
    [MemberData(nameof(EventAndLineMatrix))]
    public void TheEventNameAndTheLineNumberAreExtractedIndependentlyOfTheDotCount(
        string callerFrame,
        string expectedObjectEvent,
        string expectedLineText)
    {
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // assert.srf:L56 - the method name, from just after the last dot up to the first space after
        // it. For the no-dot row the search restarts at position 1, which is why the whole leading
        // token becomes the event.
        Assert.Equal(expectedObjectEvent, fields.ObjectEvent);

        // assert.srf:L58-L59 - the digits after the colon that follows that space.
        Assert.Equal(expectedLineText, fields.LineText);
    }

    /// <summary>
    /// Line numbers whose rendering is pinned, chosen to straddle the thousands boundary.
    /// </summary>
    /// <remarks>
    /// One is the smallest value the positional frame factory accepts; 999 and 1000 straddle the point
    /// at which a culture-sensitive rendering would begin inserting a group separator; and the two
    /// larger values put several separators' worth of digits behind that boundary. The rendering of
    /// ZERO is pinned separately, by the degraded-frame row of <see cref="EventAndLineMatrix"/>, since
    /// this factory cannot produce a position of zero.
    /// </remarks>
    public static TheoryData<int> LineNumbersToRender => new(1, 7, 137, 999, 1000, 12345, 1234567);

    [Theory]
    [MemberData(nameof(LineNumbersToRender))]
    public void FieldSixIsThePlainDecimalRenderingWithNoSeparatorAndNoPadding(int lineNumber)
    {
        // The positional frame factory renders the same number as both the position and the line
        // number, so it is the one fixture that lets a line number be CHOSEN rather than merely
        // observed - and because it is a factory, no frame text is re-declared here.
        string callerFrame = LegacyStackFrames.DepthFrame(lineNumber);
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        AssertionFailure failure = Assertions.BuildFailure(callStack, callStack.Length, string.Empty);
        PayloadFields fields = PayloadFields.SplitAsTheConsumerDoes(failure.Message);

        // assert.srf:L69 - `sMessages[6] = String(ex.#Line)`. The ROUND NUMBER and nothing else: the
        // consumer converts it straight back with `Error.Line = Long(sMessages[6])` [pfw.sra:L123], and
        // a group separator, a padded width or an explicit sign would each either defeat that
        // conversion or change its result.
        Assert.Equal(lineNumber.ToString(CultureInfo.InvariantCulture), fields.LineText);

        // The field and the carrier's own member must agree, because the legacy reads the field back
        // FROM the member [assert.srf:L69] rather than from a parse local.
        Assert.Equal(failure.Line.ToString(CultureInfo.InvariantCulture), fields.LineText);
        Assert.Equal((long)lineNumber, failure.Line);

        Assert.DoesNotContain(",", fields.LineText);
        Assert.DoesNotContain(".", fields.LineText);
        Assert.DoesNotContain(" ", fields.LineText);
        Assert.DoesNotContain("+", fields.LineText);

        // No padding: a rendered positive number never begins with a zero.
        Assert.NotEqual('0', fields.LineText[0]);

        // And it round-trips through exactly the conversion the consumer performs.
        Assert.Equal(
            (long)lineNumber,
            long.Parse(fields.LineText, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ADotlessFrameLeavesFieldsThreeAndFourUnwrittenAndStillYieldsTheFullPayload()
    {
        string[] callStack = LegacyStackFrames.Deep(LegacyStackFrames.DotlessFrame);

        // assert.srf:L39-L40 - LastPos finds nothing, so `if nPos > 0` takes its FALSE arm and the
        // whole dot block at :L40-L53 is skipped. Neither field 3 nor field 4 is ever assigned. The
        // call must not throw: this is the path a failing assertion is already on, so an exception
        // escaping here would replace a reported failure with an unrelated one.
        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // Still SEVEN fields, because the shape gate keys on the frame COUNT and not on the parse
        // succeeding [assert.srf:L37].
        Assert.Equal(DeepFieldCount, fields.Count);

        // ONLY WHAT THE PORT DEFINES IS ASSERTED, AND NO VALUE IS INVENTED. The legacy establishes
        // that these two fields are NOT WRITTEN; what an unwritten field holds is the initial value
        // of the carrier that owns it, so that carrier is asked rather than a literal being guessed.
        AssertionFailure untouched = new();
        Assert.Equal(untouched.WindowMenu, fields.WindowMenu);
        Assert.Equal(untouched.Object, fields.Object);

        // The two fields are therefore CONSISTENT WITH EACH OTHER, which is itself observable: the
        // consumer still suppresses its window line [pfw.sra:L131].
        Assert.Equal(fields.WindowMenu, fields.Object);

        // The steps outside the dot guard still ran [assert.srf:L54-L59].
        Assert.Equal("dotlessframe", fields.ObjectEvent);
        Assert.Equal("7", fields.LineText);
    }

    [Fact]
    public void AFrameWithNoSpaceAfterTheMethodLosesOnlyTheEventAndKeepsTheLocation()
    {
        // The fixture is the two-dot control frame with ONE SPACE REMOVED and nothing else changed,
        // so the comparison below shows that exactly one field moves.
        string[] withoutSpace =
            LegacyStackFrames.Deep(LegacyStackFrames.NoSpaceAfterMethodFrame);
        string[] withSpace = LegacyStackFrames.Deep(LegacyStackFrames.TwoDotFrame);

        PayloadFields degraded = BuildPayload(withoutSpace, string.Empty);
        PayloadFields control = BuildPayload(withSpace, string.Empty);

        Assert.Equal(DeepFieldCount, degraded.Count);

        // The dot split is untouched, because it happens before the space search [assert.srf:L41-L46].
        Assert.Equal(control.WindowMenu, degraded.WindowMenu);
        Assert.Equal(control.Object, degraded.Object);

        // And the LOCATION SURVIVES: the colon search is anchored at the space position plus one
        // [assert.srf:L58], and that position is zero, so the search restarts at 1 and still finds the
        // colon. Losing the event does not lose the line number.
        Assert.Equal(control.LineText, degraded.LineText);

        // ------------------------------------------------------------------------------------------
        // THE ONE EXPECTATION IN THIS FILE THAT IS A PORT DECISION RATHER THAN VERIFIED PARITY (C-K).
        // ------------------------------------------------------------------------------------------
        // The space search yields 0, so the event width computes as `0 - nPos - 1`, which is NEGATIVE
        // [assert.srf:L56]. PowerBuilder's behaviour for a negative Mid length is recorded NOWHERE in
        // this repository - the runtime is closed and the tree carries no specification for it - so no
        // parity claim is made. Assert.cs's ported primitive DEFINES a non-positive length as the
        // empty string and documents that as a decision; this is that decision asserted, and nothing
        // more. It is deliberately not asserted as legacy behaviour.
        Assert.Equal(string.Empty, degraded.ObjectEvent);
        Assert.NotEqual(control.ObjectEvent, degraded.ObjectEvent);
    }

    /// <summary>
    /// Frames whose FIRST character is the dot, so the scope width computes as ZERO.
    /// [assert.srf:L44,L49]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Columns: frame, expected field 3, expected field 4, expected field 5, expected field 6. Each
    /// frame is an existing fixture with ONE DOT PREPENDED and nothing else changed, so no frame text
    /// is invented here - the deep factory documents that any text a suite wants parsed is a legal
    /// caller frame, and composing from a named fixture keeps the row traceable to the shape it varies.
    /// </para>
    /// <para>
    /// WHY THIS ROW EXISTS. Both scope assignments are <c>Left(frame, position - 1)</c>
    /// [assert.srf:L44,L49], and a dot at position 1 makes that width ZERO. It is the ONLY input that
    /// drives the non-positive-width arm of the ported <c>Left</c> primitive, and without it that arm
    /// is unexercised - which the coverage report shows and which C-H does not permit. It is a live
    /// arm of the parse rather than a defensive one, so the gap was a MISSING ROW and not a
    /// documented-unreachable line.
    /// </para>
    /// <para>
    /// The two rows reach the arm through the two DIFFERENT call sites - the leading dot with more dots
    /// after it takes the two-or-more branch [assert.srf:L44], and the leading dot as the only dot
    /// takes the one-dot branch [assert.srf:L49] - so neither call site can regress unnoticed.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string, string> LeadingDotParseMatrix =>
        new()
        {
            {
                "." + LegacyStackFrames.SingleDotFrame,
                string.Empty, "n_cst_thread_trans", "of_connect", "42"
            },
            {
                "." + LegacyStackFrames.DotlessFrame,
                string.Empty, string.Empty, "dotlessframe", "7"
            },
        };

    [Theory]
    [MemberData(nameof(LeadingDotParseMatrix))]
    public void ALeadingDotMakesTheScopeWidthZeroAndYieldsAnEmptyWindowMenu(
        string callerFrame,
        string expectedWindowMenu,
        string expectedObject,
        string expectedObjectEvent,
        string expectedLineText)
    {
        string[] callStack = LegacyStackFrames.Deep(callerFrame);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // Still SEVEN fields, and still no throw: a zero-width scope is a parse OUTCOME, not a fault.
        Assert.Equal(DeepFieldCount, fields.Count);
        Assert.Equal(expectedWindowMenu, fields.WindowMenu);
        Assert.Equal(expectedObject, fields.Object);
        Assert.Equal(expectedObjectEvent, fields.ObjectEvent);
        Assert.Equal(expectedLineText, fields.LineText);

        // The one-dot branch still assigns field 4 FROM field 3 [assert.srf:L51], so an empty scope
        // stays an empty PAIR and the consumer still suppresses its window line [pfw.sra:L131]. The
        // two-or-more-dot branch computes them independently, so there they legitimately differ - and
        // the consumer then prints a window line whose window is empty, which is preserved legacy
        // behaviour rather than something to guard against (C-B).
        Assert.Equal(expectedWindowMenu == expectedObject, fields.WindowMenu == fields.Object);
    }

    [Fact]
    public void AnEmptyCallerFrameDrivesEveryDegenerateSearchAtOnceWithoutThrowing()
    {
        // The empty string is an explicitly ALLOWED caller frame for the deep factory, and it is the
        // single input that reaches the no-dot arm, the no-space arm and the no-colon arm together -
        // which is what makes it worth a row of its own rather than a footnote on the two above.
        string[] callStack = LegacyStackFrames.Deep(string.Empty);

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        Assert.Equal(DeepFieldCount, fields.Count);

        AssertionFailure untouched = new();
        Assert.Equal(untouched.WindowMenu, fields.WindowMenu);
        Assert.Equal(untouched.Object, fields.Object);
        Assert.Equal(string.Empty, fields.ObjectEvent);

        // assert.srf:L59 - `Long(Mid(sCalling, nPos + 1))` over text that is not a number. The ported
        // primitive answers zero and never throws, which matters precisely because this arm is
        // reachable from a frame that carries no line number at all.
        Assert.Equal("0", fields.LineText);
    }

    // ==============================================================================================
    //  SECTION D - FRAME TRIMMING AND THE ONE-BASED HAZARD
    // ==============================================================================================
    //  Two expressions, sharing a sub-term, translating differently. The header of this file writes
    //  the arithmetic out in full; the short form is:
    //
    //      SELECTION   sCallStack[nCount - 2]        [assert.srf:L38]  one-based SUBSCRIPT
    //                                                                  -> C# index frameCount - 3
    //      COPY        for nIndex = 1 to nCount - 2  [assert.srf:L61]  one-based COUNT
    //                                                                  -> the first frameCount - 2
    //
    //  PRESERVED LEGACY BEHAVIOUR, DELIBERATELY PINNED (C-B). Both expressions DISCARD THE TWO
    //  INNERMOST FRAMES, and that is the entire reason an assertion report names the code that failed
    //  rather than the assertion framework that reported it. Reversing the direction, or shifting
    //  either expression by one, produces a report of exactly the right LENGTH that blames exactly the
    //  wrong FRAME - and a frame-count assertion on its own would still pass. That is why every row
    //  below asserts against a NAMED fixture and never against a recomputed position: an off-by-one in
    //  the code under test cannot be cancelled out by an agreeing off-by-one in the expectation.
    // ==============================================================================================

    /// <summary>
    /// The frame lists that carry the two named framework frames in their innermost slots.
    /// </summary>
    /// <remarks>
    /// Resolved by <see cref="ResolveCallStack"/>. Three rows: the default deep list, the same list
    /// with a one-dot caller so the trim is shown to be independent of the parse branch, and the
    /// oracle's own four-frame chain, which is the shortest realistic list and therefore the row
    /// closest to the shape gate.
    /// </remarks>
    public static TheoryData<string> FrameListsCarryingTheFrameworkFrames =>
        new("Deep", "DeepOneDot", "OracleChain");

    /// <summary>
    /// Deep depths for the theories that prove the trim arithmetic as a FUNCTION of depth.
    /// </summary>
    /// <remarks>
    /// Five values, starting at the boundary depth of three, so the copied length is shown to track
    /// the depth rather than to have been fitted to one convenient case. Three is the minimum the
    /// prompt-level requirement asks for; five is cheap and removes any doubt.
    /// </remarks>
    public static TheoryData<int> DeepDepths =>
        new(LegacyStackFrames.MinimumDeepFrameCount, 4, 5, 8, 13);

    [Fact]
    public void TheSelectedFrameIsTheCallersFrameAndNeitherFrameworkFrameIsParsed()
    {
        string[] callStack = LegacyStackFrames.Deep();

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // The expected values are the parse of LegacyStackFrames.ExpectedCallerFrame - obtained by
        // parsing THAT FRAME AND NOTHING ELSE, so the claim "the selected frame is the caller's" is
        // asserted directly rather than inferred from four coincidentally matching field values.
        PayloadFields callerAlone = ParseSelectedFrame(LegacyStackFrames.ExpectedCallerFrame);

        Assert.Equal(callerAlone.WindowMenu, fields.WindowMenu);
        Assert.Equal(callerAlone.Object, fields.Object);
        Assert.Equal(callerAlone.ObjectEvent, fields.ObjectEvent);
        Assert.Equal(callerAlone.LineText, fields.LineText);

        // AND NEITHER FRAMEWORK FRAME REACHED FIELDS 3 TO 6. Their own parses are computed the same
        // way and must differ from what the payload carries; the event names are the sharpest
        // discriminator, since both framework frames share a scope with each other.
        PayloadFields guardAlone = ParseSelectedFrame(LegacyStackFrames.AssertGuardFrame);
        PayloadFields builderAlone = ParseSelectedFrame(LegacyStackFrames.PayloadBuilderFrame);

        Assert.NotEqual(guardAlone.ObjectEvent, fields.ObjectEvent);
        Assert.NotEqual(builderAlone.ObjectEvent, fields.ObjectEvent);
        Assert.NotEqual(guardAlone.Object, fields.Object);
        Assert.NotEqual(builderAlone.Object, fields.Object);
    }

    [Fact]
    public void TheOracleChainBlamesItsWindowFunctionWhichIsTheOneDotShape()
    {
        // Worth its own fact because the result is genuinely counter-intuitive. The oracle's chain
        // contains the two-dot button-event frame AND the one-dot window-function frame, and the
        // selection at assert.srf:L38 picks the WINDOW FUNCTION - so the oracle's own default
        // behaviour is the window-equals-object shape whose window line the consumer SUPPRESSES
        // [pfw.sra:L131]. The two-dot frame it also carries is merely copied into the trace.
        string[] callStack = LegacyStackFrames.OracleChain();

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        PayloadFields selected = ParseSelectedFrame(LegacyStackFrames.WindowFunctionFrame);

        Assert.Equal(selected.WindowMenu, fields.WindowMenu);
        Assert.Equal(selected.ObjectEvent, fields.ObjectEvent);
        Assert.Equal(selected.LineText, fields.LineText);
        Assert.Equal(fields.WindowMenu, fields.Object);
    }

    [Theory]
    [MemberData(nameof(DeepDepths))]
    public void TheCopiedStackHoldsExactlyTwoFewerFramesThanTheCountAtEveryDepth(int depth)
    {
        string[] callStack = LegacyStackFrames.WithDepth(depth);

        AssertionFailure failure = Assertions.BuildFailure(callStack, depth, string.Empty);
        PayloadFields fields = PayloadFields.SplitAsTheConsumerDoes(failure.Message);

        string[] copied = fields.StackTraceInfo.Split(IntraFieldSeparator);

        // assert.srf:L61 - `for nIndex = 1 to nCount - 2`. A COUNT, so it does NOT shift on
        // translation: the first depth - 2 frames, and no others.
        Assert.Equal(depth - LegacyStackFrames.MaxShallowFrameCount, copied.Length);

        // Asserted against the fixture's single definition of "trimmed" rather than against a locally
        // recomputed slice, so the two cannot agree on a shared mistake.
        Assert.Equal(LegacyStackFrames.ExpectedStackTraceInfo(callStack), fields.StackTraceInfo);
        Assert.Equal(LegacyStackFrames.ExpectedTrimmedStack(callStack), copied);

        // THE TWO INNERMOST FRAMES OF THIS LIST ARE GONE (C-B). At this depth they are the frames
        // numbered depth and depth - 1, and each generated frame carries its own position as its text,
        // so they can be named by VALUE - never by index.
        Assert.DoesNotContain(LegacyStackFrames.DepthFrame(depth), copied);
        Assert.DoesNotContain(LegacyStackFrames.DepthFrame(depth - 1), copied);

        // AND THE SELECTED FRAME IS THE NEXT ONE OUT, which is also the LAST frame the copy keeps.
        // assert.srf:L38 selects one-based depth - 2 and :L61 copies one-based 1 through depth - 2, so
        // the selected frame is by construction the final element of the copied stack. A port that
        // shifted the selection by one would fail this equality while still producing a copied stack
        // of exactly the right length.
        string expectedSelected =
            LegacyStackFrames.DepthFrame(depth - LegacyStackFrames.MaxShallowFrameCount);
        Assert.Equal(expectedSelected, copied[^1]);

        PayloadFields selected = ParseSelectedFrame(expectedSelected);
        Assert.Equal(selected.ObjectEvent, fields.ObjectEvent);
        Assert.Equal(selected.LineText, fields.LineText);
    }

    [Theory]
    [MemberData(nameof(FrameListsCarryingTheFrameworkFrames))]
    public void NeitherInnermostFrameworkFrameSurvivesIntoTheCopiedStack(string callStackKey)
    {
        string[] callStack = ResolveCallStack(callStackKey);

        AssertionFailure failure =
            Assertions.BuildFailure(callStack, callStack.Length, string.Empty);
        PayloadFields fields = PayloadFields.SplitAsTheConsumerDoes(failure.Message);

        string[] copied = fields.StackTraceInfo.Split(IntraFieldSeparator);

        // PRESERVED LEGACY BEHAVIOUR (C-B). The two innermost frames are excluded from the copied
        // stack [assert.srf:L61] just as they are excluded from the selected frame [assert.srf:L38].
        // A report that listed the assertion framework's own two frames would name them in every
        // failure the framework ever emits, which is exactly the noise the legacy arithmetic removes.
        Assert.DoesNotContain(LegacyStackFrames.AssertGuardFrame, copied);
        Assert.DoesNotContain(LegacyStackFrames.PayloadBuilderFrame, copied);
        Assert.DoesNotContain(LegacyStackFrames.AssertGuardFrame, failure.StackTrace);
        Assert.DoesNotContain(LegacyStackFrames.PayloadBuilderFrame, failure.StackTrace);

        Assert.Equal(LegacyStackFrames.ExpectedTrimmedStack(callStack), copied);

        // THE FIELD AND THE FRAME LIST ARE ONE SEQUENCE, because the legacy builds both in a single
        // pass [assert.srf:L62,L64]. Splitting the loop in two is how they would silently drift.
        Assert.Equal(copied, failure.StackTrace);
    }

    [Fact]
    public void TheCopiedStackPreservesTheOutermostFirstOrderAndIsJoinedByABareLineFeed()
    {
        string[] callStack = LegacyStackFrames.Deep();

        PayloadFields fields = BuildPayload(callStack, string.Empty);

        // assert.srf:L63-L64 - the separator is a BARE LINE FEED inserted BETWEEN entries only: never
        // leading, never trailing, and never the CRLF field delimiter. A CRLF here would split field 7
        // at the consumer and collapse the seven-field shape to something no branch matches.
        string expected = LegacyStackFrames.OutermostFrame
            + LegacyStackFrames.FrameSeparator + LegacyStackFrames.SecondOutermostFrame
            + LegacyStackFrames.FrameSeparator + LegacyStackFrames.ExpectedCallerFrame;

        Assert.Equal(expected, fields.StackTraceInfo);
        Assert.DoesNotContain(FieldDelimiter, fields.StackTraceInfo);
        Assert.DoesNotContain("\r", fields.StackTraceInfo);

        string[] copied = fields.StackTraceInfo.Split(IntraFieldSeparator);

        // OUTERMOST FIRST, which is the ordering the producer documents and the ordering the whole
        // trim arithmetic depends on. Reversing it would make the selection blame the outermost frame
        // instead of the caller while leaving every length assertion satisfied.
        Assert.Equal(LegacyStackFrames.OutermostFrame, copied[0]);
        Assert.Equal(LegacyStackFrames.ExpectedCallerFrame, copied[^1]);

        // No leading or trailing separator, expressed as the absence of an empty first or last entry.
        Assert.NotEqual(string.Empty, copied[0]);
        Assert.NotEqual(string.Empty, copied[^1]);
    }

    // ==============================================================================================
    //  SECTION E - ONE REAL-CAPTURE SANITY CHECK
    // ==============================================================================================
    //  Every row above drives the payload builder from HAND-WRITTEN frame strings, which is what makes
    //  the branches reachable and the expectations exact. The cost of that is a standing question:
    //  are those synthetic strings representative of what the producer really emits?
    //
    //  This is the fact that answers it. It goes through the PUBLIC guard, lets the real capture run,
    //  and asserts the payload it produces has the same shape and the same populated fields the
    //  synthetic rows claim. If it passes alongside them, the fixture grammar in LegacyStackFrames is
    //  trustworthy. IF IT FAILS, THE FIXTURES ARE WRONG AND MUST BE FIXED - this assertion is never to
    //  be loosened to accommodate them, because loosening it is exactly how a synthetic suite drifts
    //  away from the code it is supposed to be characterising.
    // ==============================================================================================

    [Fact]
    public void ARealAssertionFailureProducesTheSevenFieldPayloadWithItsCallerNamed()
    {
        // Nested local helpers, so the captured stack is genuinely deep and the frame the report
        // blames is a frame this test owns rather than the runner's. NoInlining on both: PowerBuilder
        // never inlines a PowerScript function and never reuses a caller's frame, whereas a JIT may do
        // either - and either would silently change which frame the ported `nCount - 2` arithmetic
        // lands on. This hazard has no legacy analogue, which is why the production members carry the
        // same attribute and why it is repeated here.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void FailFromInnermostHelper() => Assertions.Assert(false, OracleInfo);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CallTheInnermostHelper() => FailFromInnermostHelper();

        AssertionFailure failure =
            Assert.Throws<AssertionFailure>(CallTheInnermostHelper);

        PayloadFields fields = PayloadFields.SplitAsTheConsumerDoes(failure.Message);

        // EXACTLY SEVEN, from a real capture and not from a fixture.
        Assert.Equal(DeepFieldCount, fields.Count);

        Assert.Equal(ErrorNumberField, fields.Number);
        Assert.Equal(BareAssertionText + IntraFieldSeparator + OracleInfo, fields.Text);

        // Fields 3 to 5 are populated, which is the claim that matters: the real producer emits frames
        // the ported parser can actually split.
        Assert.NotEmpty(fields.WindowMenu);
        Assert.NotEmpty(fields.Object);
        Assert.NotEmpty(fields.ObjectEvent);
        Assert.NotEmpty(fields.StackTraceInfo);

        // Field 6 is a number the consumer's conversion accepts. NOT asserted to be non-zero: a build
        // without a portable symbol file degrades every line number to zero, and that is a real
        // production shape rather than a fault.
        Assert.True(
            long.TryParse(
                fields.LineText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long capturedLine),
            "Field 6 must be a plain decimal the consumer's numeric conversion can read.");
        Assert.Equal(failure.Line, capturedLine);

        // THE REPORT NAMES THE CALLER (C-B). The innermost helper is the direct caller of the guard, so
        // it is the frame at one-based nCount - 2 and its name must appear in field 5. A compiler
        // decorates a local function's metadata name, so this is a containment check rather than an
        // equality - the decoration is an implementation detail, but the identifier inside it is not.
        Assert.Contains(nameof(FailFromInnermostHelper), fields.ObjectEvent);

        // AND NEITHER FRAMEWORK FRAME SURVIVED. The names are composed from the type and the members
        // themselves, so a rename cannot leave a stale literal behind here. The line-number introducer
        // is included in each prefix because it is what separates the guard's frame from the payload
        // builder's - one name is a prefix of the other.
        string assertionsScope = typeof(Assertions).FullName!;
        string guardFramePrefix =
            assertionsScope + "." + nameof(Assertions.Assert) + " line:";
        string builderFramePrefix =
            assertionsScope + "." + nameof(Assertions.AssertFailed) + " line:";

        Assert.DoesNotContain(guardFramePrefix, fields.StackTraceInfo);
        Assert.DoesNotContain(builderFramePrefix, fields.StackTraceInfo);

        // The copied stack still ends at the selected frame, exactly as the synthetic rows assert, so
        // the two arithmetic expressions of assert.srf:L38 and :L61 agree on live frames too.
        string[] copied = fields.StackTraceInfo.Split(IntraFieldSeparator);
        Assert.Equal(copied, failure.StackTrace);
        Assert.Contains(fields.ObjectEvent, copied[^1]);
        Assert.EndsWith(" line:" + fields.LineText, copied[^1]);

        // And the location suffix divergence holds on a live capture as well as on a fixture.
        Assert.DoesNotContain(LocationSuffixOpening, fields.Text);
        Assert.Contains(LocationSuffixOpening, failure.Info);
    }

    // ==============================================================================================
    //  SECTION F - THE TWO FINDINGS FROM DISCOVERY THAT NEED THEIR OWN ROWS
    // ==============================================================================================

    [Fact]
    public void ASwallowedCaptureFailureLeavesTheCountAtZeroAndSilentlySelectsTheShallowShape()
    {
        // PRESERVED LEGACY BEHAVIOUR, DELIBERATELY PINNED (C-B). The legacy wraps its capture in
        // `catch(throwable ex1)` with an EMPTY BODY and never reads the caught variable
        // [assert.srf:L29-L32]. A capture failure therefore leaves the count local at zero and the
        // shape gate at :L37 quietly takes its shallow branch: no window, no object, no event, no line
        // number, no trace, and no diagnostic of any kind. THIS IS NOT AN ERROR TO SURFACE. Logging
        // it, rethrowing it, or substituting a synthetic frame would each be an improvement the
        // migration is not permitted to make - and the consumer already handles the two-field shape
        // [pfw.sra:L116].
        string[] noFramesAtAll = LegacyStackFrames.Shallow(0);

        PayloadFields fromEmptyCapture = BuildPayload(noFramesAtAll, 0, OracleInfo);

        Assert.Equal(ShallowFieldCount, fromEmptyCapture.Count);
        Assert.Equal(ErrorNumberField, fromEmptyCapture.Number);
        Assert.Equal(BareAssertionText + IntraFieldSeparator + OracleInfo, fromEmptyCapture.Text);

        // AND IT IS THE COUNT THAT GATES THE SHAPE, NOT THE ARRAY. The legacy holds the two values in
        // separate locals [assert.srf:L17,L30], so a capture that failed AFTER partially filling the
        // array still reports zero and still produces two fields. Passing a deliberately inconsistent
        // pair is the only way to show that the port kept them separate rather than deriving one from
        // the other - which would have made this shape unreachable.
        string[] framesTheCountDisowns = LegacyStackFrames.Deep();

        PayloadFields fromDisownedFrames = BuildPayload(framesTheCountDisowns, 0, OracleInfo);

        Assert.Equal(ShallowFieldCount, fromDisownedFrames.Count);
        Assert.Equal(fromEmptyCapture.Text, fromDisownedFrames.Text);
    }

    [Theory]
    [MemberData(nameof(DeepDepths))]
    public void FieldSevenIsNeverEmptyWheneverTheDeepBranchFires(int depth)
    {
        string[] callStack = LegacyStackFrames.WithDepth(depth);

        PayloadFields fields = BuildPayload(callStack, depth, string.Empty);

        // LOAD BEARING, NOT COSMETIC. The copy loop runs at least once whenever the gate opens,
        // because the gate needs three frames and the loop bound is count - 2 [assert.srf:L37,L61], so
        // field 7 always carries at least one frame. That matters because the consumer's splitter
        // DISCARDS A TRAILING EMPTY SEGMENT: an empty field 7 would arrive as SIX segments, the
        // exactly-seven branch would not run [pfw.sra:L119], and the window, object, event and line
        // number would all be dropped even though the producer had emitted them.
        //
        // The consumer-side half of that claim - that a payload whose seventh field is empty really
        // does decode as six - is owned by SystemErrorRoundTripTests, and the carrier-level statement
        // of the same defect is owned by AssertionFailureTests. What belongs HERE is the producer-side
        // guarantee that the deep branch never emits that payload in the first place.
        Assert.NotEmpty(fields.StackTraceInfo);
        Assert.Equal(DeepFieldCount, fields.Count);
        Assert.NotEmpty(fields.StackTraceInfo.Split(IntraFieldSeparator)[0]);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds a payload through the internal seam, passing the frame array's own length as the count.
    /// </summary>
    /// <param name="callStack">The frames, outermost first.</param>
    /// <param name="info">The additional failure text, possibly empty.</param>
    /// <returns>The produced payload, split the way the consumer splits it.</returns>
    /// <remarks>
    /// The ordinary case. Every factory in <c>LegacyStackFrames</c> returns an array whose length IS
    /// the count a real capture would have reported, so this overload is what most rows want; the rows
    /// that deliberately disagree the two values call the three-argument overload and say why.
    /// </remarks>
    private static PayloadFields BuildPayload(string[] callStack, string info)
    {
        return BuildPayload(callStack, callStack.Length, info);
    }

    /// <summary>
    /// Builds a payload through the internal seam with an explicitly supplied frame count.
    /// </summary>
    /// <param name="callStack">The frames, outermost first.</param>
    /// <param name="frameCount">The count the capture is treated as having reported.</param>
    /// <param name="info">The additional failure text, possibly empty.</param>
    /// <returns>The produced payload, split the way the consumer splits it.</returns>
    /// <remarks>
    /// <c>Assertions.BuildFailure</c> is <see langword="internal"/> and this assembly is named in the
    /// Diagnostics project's <c>InternalsVisibleTo</c> attribute for exactly this purpose, so the call
    /// below is a compiled reference. Reflection is deliberately avoided: a reflective call would not
    /// fail to compile when the seam's signature changed, which would cost this suite its only
    /// protection against a silent contract drift.
    /// </remarks>
    private static PayloadFields BuildPayload(string[] callStack, int frameCount, string info)
    {
        AssertionFailure failure = Assertions.BuildFailure(callStack, frameCount, info);

        return PayloadFields.SplitAsTheConsumerDoes(failure.Message);
    }

    /// <summary>
    /// Returns the payload that results when <paramref name="frame"/> is the SELECTED frame, so that
    /// a claim about which frame was blamed can be asserted against that frame's own parse.
    /// </summary>
    /// <param name="frame">The frame text to place in the caller slot.</param>
    /// <returns>The produced payload, split the way the consumer splits it.</returns>
    /// <remarks>
    /// Exists so that no row has to restate a frame's expected parse in order to say "this frame is the
    /// one that was blamed". The surrounding frames are the deep factory's, which are identical across
    /// every call, so any difference between two results is attributable to the caller slot alone.
    /// </remarks>
    private static PayloadFields ParseSelectedFrame(string frame)
    {
        return BuildPayload(LegacyStackFrames.Deep(frame), string.Empty);
    }

    /// <summary>
    /// Resolves a theory row's frame-list key to a freshly built frame array.
    /// </summary>
    /// <param name="callStackKey">
    /// <c>"Shallow"</c>, <c>"Deep"</c>, <c>"DeepOneDot"</c> or <c>"OracleChain"</c>.
    /// </param>
    /// <returns>A fresh frame array, outermost first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="callStackKey"/> is not one of the four keys. Refused rather than defaulted,
    /// because a row that silently fell back to some other list would assert the wrong payload shape
    /// and still report its own name as passing.
    /// </exception>
    /// <remarks>
    /// Keys rather than arrays in the theory data, for two reasons. A key renders as a NAME in a
    /// test-run report where a rendered five-element array does not, and every factory allocates a
    /// FRESH array per call, so resolving here rather than in a member-data property guarantees that
    /// two rows can never share a mutable fixture.
    /// </remarks>
    private static string[] ResolveCallStack(string callStackKey)
    {
        return callStackKey switch
        {
            "Shallow" => LegacyStackFrames.Shallow(LegacyStackFrames.MaxShallowFrameCount),
            "Deep" => LegacyStackFrames.Deep(),
            "DeepOneDot" => LegacyStackFrames.Deep(LegacyStackFrames.SingleDotFrame),
            "OracleChain" => LegacyStackFrames.OracleChain(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(callStackKey),
                callStackKey,
                "Unknown frame-list key."),
        };
    }

    /// <summary>
    /// The produced payload, split on the field delimiter and addressed by the ONE-BASED field numbers
    /// the legacy producer writes and the legacy consumer reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE POINT OF THIS TYPE IS THE ONE-BASED DISCIPLINE (AAP 0.4.5.4, risk R9). The producer writes
    /// <c>sMessages[1]</c> through <c>sMessages[7]</c> [assert.srf:L22-L70] and the consumer reads
    /// <c>sMessages[1]</c> through <c>sMessages[7]</c> [pfw.sra:L117-L124]; both are one-based. Routing
    /// every read through <see cref="Field(int)"/> puts the single subtraction in ONE place - exactly
    /// as the production code funnels every frame-array access through
    /// <c>StackTraceProvider.FrameAt</c> - so a suite of this size carries one index expression rather
    /// than several dozen opportunities for an off-by-one.
    /// </para>
    /// <para>
    /// The named accessors are spelled as the CONSUMER's own assignments spell them
    /// [pfw.sra:L117-L124], so a reader can match an assertion to the line of the decoder it protects
    /// without counting fields. <see cref="LineText"/> is the one deliberate departure: field 6 is a
    /// STRING on the wire [assert.srf:L69] and the name says so, because the numeric value it parses
    /// back to lives on the failure record rather than in the payload.
    /// </para>
    /// <para>
    /// It exposes no mutator and no re-split. A payload is decoded once and read many times, which is
    /// what the consumer does too.
    /// </para>
    /// </remarks>
    private sealed class PayloadFields
    {
        private readonly string[] _segments;

        private PayloadFields(string[] segments)
        {
            _segments = segments;
        }

        /// <summary>The number of fields the payload decodes into. Two or seven, never anything else.</summary>
        internal int Count => _segments.Length;

        /// <summary>Field 1 - the error number, as text. [assert.srf:L22, pfw.sra:L117]</summary>
        internal string Number => Field(1);

        /// <summary>Field 2 - the assertion text and its optional continuation. [assert.srf:L24-L27, pfw.sra:L118]</summary>
        internal string Text => Field(2);

        /// <summary>Field 3 - the window or menu. [assert.srf:L66, pfw.sra:L120]</summary>
        internal string WindowMenu => Field(3);

        /// <summary>Field 4 - the object. [assert.srf:L67, pfw.sra:L121]</summary>
        internal string Object => Field(4);

        /// <summary>Field 5 - the object's event or method. [assert.srf:L68, pfw.sra:L122]</summary>
        internal string ObjectEvent => Field(5);

        /// <summary>Field 6 - the line number, as text. [assert.srf:L69, pfw.sra:L123]</summary>
        internal string LineText => Field(6);

        /// <summary>Field 7 - the copied call stack, line-feed joined. [assert.srf:L70, pfw.sra:L124]</summary>
        internal string StackTraceInfo => Field(7);

        /// <summary>
        /// Splits a payload exactly as the legacy consumer's first act splits it. [pfw.sra:L115]
        /// </summary>
        /// <param name="payload">The assembled payload. [assert.srf:L74-L80]</param>
        /// <returns>The decoded fields.</returns>
        /// <remarks>
        /// Splits on the CRLF PAIR and on nothing else, and applies no options: no entries are removed
        /// and nothing is trimmed, because the consumer does neither and an expectation that tidied the
        /// payload would hide the very regressions this suite exists to catch.
        /// </remarks>
        internal static PayloadFields SplitAsTheConsumerDoes(string payload)
        {
            return new PayloadFields(payload.Split(FieldDelimiter));
        }

        /// <summary>
        /// Returns the field at <paramref name="oneBasedFieldNumber"/>, counting from 1 as both the
        /// producer and the consumer count.
        /// </summary>
        /// <param name="oneBasedFieldNumber">The one-based field number, 1 through <see cref="Count"/>.</param>
        /// <returns>The field's text, verbatim.</returns>
        /// <remarks>
        /// THE ONLY INDEX ARITHMETIC IN THIS FILE. An out-of-range field number is left to raise the
        /// array's own exception rather than being folded into a substitute value: a row that asked for
        /// field 7 of a two-field payload has made a mistake about the shape, and that mistake must
        /// fail loudly rather than compare successfully against an empty string.
        /// </remarks>
        internal string Field(int oneBasedFieldNumber)
        {
            return _segments[oneBasedFieldNumber - 1];
        }
    }
}
