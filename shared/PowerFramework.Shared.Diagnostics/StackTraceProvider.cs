// ==============================================================================================
//  StackTraceProvider - the call stack capture primitive and the four StackTraceInfo overloads
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.common.pbl.src/stacktrace.srf     (9 lines, DECLARATION ONLY)
//                 ws_objects/pfw.common.pbl.src/stacktraceinfo.srf (44 lines, pure PowerScript)
//
//  ALSO READ AS SPECIFICATION, AND EQUALLY READ ONLY:
//      ws_objects/pfw.common.pbl.src/assert.srf        the second consumer of the capture, and the
//                                                     only evidence of both the frame ORDER and
//                                                     the frame TEXT format
//                                                     [assert.srf:L30,L38,L39-L59,L61-L65].
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw  the behavioural oracle, and the single live
//                                                     call site of StackTraceInfo in the whole
//                                                     repository: StackTraceInfo("-- ") [:L79].
//                                                     REFERENCE only per AAP 0.2.2.3 - never
//                                                     ported and never edited.
//      ws_objects/pfw.pbl.src/pfw.sra                 the ultimate consumer. It splits the
//                                                     assertion payload on CRLF and takes the deep
//                                                     branch only when the split yields EXACTLY
//                                                     seven fields [pfw.sra:L115,L119], which is
//                                                     what forces the separator chosen below.
//
//  ORACLE STATUS  Both ported files are READ ONLY: they are the behavioural oracle for parity
//                 testing, never an edit target (AAP 0.7.3 C-C). Every guard, every arithmetic
//                 step and every literal below carries the line it was taken from.
//
//  THE NATIVE SURFACE HERE IS THE SECOND OF TWO MECHANISMS, AND IT IS THE EASY ONE TO MISS
//  --------------------------------------------------------------------------------------------
//  AAP 0.6.5 establishes that this estate binds native code in TWO different ways. This file is an
//  instance of the second, so the record is stated rather than left to be inferred:
//
//      1. PBNI CLASS BINDING      `global type <name> from function_object native "pfw.dll"`,
//                                 with the binding on the TYPE
//      2. CLASSIC EXTERNAL        `... system library "pfw.dll" alias for "<export>"`, with the
//         PROTOTYPE               binding on the PROTOTYPE and no native clause on the type
//
//  stacktrace.srf is mechanism 2. Its type declaration is a bare
//  `global type stacktrace from function_object` [stacktrace.srf:L3-L4] and the entire binding
//  lives on the prototype [stacktrace.srf:L7]:
//
//      global function int StackTrace (ref string callstack[]) system library "pfw.dll" &
//          alias for "pfwStackTrace"
//
//  This is worth recording because an early analysis that searched only for the conventional
//  external-function keyword returned zero results and would have missed the mechanism entirely.
//  There is no PowerScript body to read and no C++ source anywhere in the repository, so the
//  prototype and the two in-repo call sites are the whole of the evidence.
//
//  THE SUBSTITUTION, AND EXACTLY WHAT DIFFERS ACROSS IT (C-K)
//  --------------------------------------------------------------------------------------------
//  AAP 0.6.5's native-binding matrix classifies this primitive as SUBSTITUTE, so writing a managed
//  equivalent is the sanctioned outcome and reverse engineering pfw.dll is explicitly not expected:
//
//      pfwStackTrace, exported from the closed pfw.dll   ->   System.Diagnostics.StackTrace
//
//  PRESERVED across the substitution: the shape of the primitive (an integer count returned
//  alongside an array of frame strings), the frame ORDER (outermost first), the fact that the
//  primitive cannot observe its own frame, and every guard and arithmetic step of the four
//  StackTraceInfo overloads built on top of it.
//
//  DIFFERENT across the substitution, and unavoidably so:
//      * The frames are CLR frames, not PowerScript frames, so the outermost entries name the
//        runtime's host and thread start rather than a PowerBuilder window event. The ORDER and the
//        arithmetic are unchanged; only the identity of the outermost frames differs.
//      * The frame TEXT is defined by this file, because pfw.dll's format cannot be read. It is
//        defined to satisfy assert.srf's parser exactly; see the format contract below.
//      * A frame may report line number 0. See the file-info degradation note below.
//
//  FRAME ORDER IS THE SINGLE MOST DANGEROUS TRANSLATION IN THIS FOLDER (AAP 0.8.6 R9)
//  --------------------------------------------------------------------------------------------
//  The legacy array is one-based with index 1 = the OUTERMOST frame and index count = the
//  INNERMOST frame. System.Diagnostics.StackTrace runs the OTHER WAY: its index 0 is the INNERMOST
//  frame. The capture below therefore REVERSES the runtime's order, and a maintainer who "tidies"
//  that reversal away produces a report that still has the right number of frames, still parses,
//  and names the wrong routine - which is precisely the failure mode AAP 0.8.6 R9 warns about for
//  one-based to zero-based translation.
//
//  The order is not assumed. It is proved by the second consumer, twice over:
//      * assert.srf:L38 selects the frame to blame as sCallStack[nCount - 2]. Inside assertfailed
//        the two innermost PowerScript frames are assertfailed itself and the assert overload that
//        called it - the native primitive is not a PowerScript frame and cannot see itself - so
//        subtracting 2 from the innermost index lands on the USER's frame, which is the only frame
//        an assertion report could usefully name.
//      * assert.srf:L61 builds the trace as `for nIndex = 1 to nCount - 2`, that is everything from
//        the outermost down to and including the user's frame, excluding those same two framework
//        frames. Under the reverse reading it would discard the application's outermost frames and
//        keep the framework's own, which is nonsense.
//  The sibling AssertionFailure.cs states the same contract on its StackTrace member ("an ordered
//  list of frames, outermost first"), so the two files agree by construction rather than by luck.
//
//  ONE-BASED INDEXING DISCIPLINE (AAP 0.4.5.4)
//  --------------------------------------------------------------------------------------------
//  Every index expression ported from these two files is one-based, and PowerBuilder's UpperBound
//  returns the LAST VALID INDEX rather than a length. Both are routed through the two centralized
//  accessors below, UpperBound and FrameAt, so no ported expression ever touches a zero-based
//  index directly and the guard at stacktraceinfo.srf:L30 ports character for character. They are
//  internal rather than private for a reason: Assert.cs ports one-based arithmetic over the same
//  array [assert.srf:L38,L61-L64] and must reuse these rather than duplicate them.
//
//  THE OFFSET WIDTH IS ushort, WHICH IS NOT A TYPO (AAP 0.4.5.2)
//  --------------------------------------------------------------------------------------------
//  PowerBuilder and C# spell different widths with the same names, and the sibling Kernel Bits.cs
//  settles the mapping for the whole shared layer at its own :L42-L43:
//
//      PowerBuilder ulong / unsignedlong    32-bit unsigned   ->   C# uint     NOT C# ulong
//      PowerBuilder uint  / unsignedinteger 16-bit unsigned   ->   C# ushort   NOT C# uint
//
//  stacktraceinfo.srf:L9 and :L10 declare `readonly uint offset`, so the managed parameter is
//  ushort. Matching Bits.cs is not cosmetic consistency: a shared layer that mapped the same
//  legacy spelling to two different widths would be a defect in its own right, and the width also
//  fixes what `offset + 1` does at the top of the range - see the arithmetic note on that overload.
//  The consequence at a call site is that an int variable does not implicitly convert, so
//  StackTraceInfo(someInt) raises CS1503 and the caller must say (ushort)someInt. That error is
//  the width discipline working and must never be routed around by adding an int or long overload;
//  C-B forbids the convenience overload and it would open a silent truncation door.
//
//  THE LEGACY `readonly` MODIFIER IS DROPPED RATHER THAN MAPPED TO `in`, ON PURPOSE
//  --------------------------------------------------------------------------------------------
//  All four prototypes mark their parameters `readonly` [stacktraceinfo.srf:L8-L10], and AAP
//  0.4.5.2's type table maps that modifier to `in`. It is deliberately not applied here, for the
//  same reason the sibling Kernel Bits.cs records for its own signatures: `readonly` on a 16-bit
//  scalar or on a string reference carries no observable contract, because the callee cannot reach
//  the caller's copy either way, so the modifier is unobservable and dropping it changes nothing a
//  test could detect. By value is chosen because both operands are no larger than the reference
//  that would point at them, and that is stated as the reason for the choice rather than as a
//  performance claim - AAP 0.8.5 forbids asserting one. The two files agree, which matters more
//  than either choice in isolation: a shared layer that spelled the same legacy modifier two ways
//  would invite a reader to look for a distinction that is not there.
//
//  THE JIT MUST NOT REMOVE A FRAME, AND THIS HAS NO LEGACY ANALOGUE
//  --------------------------------------------------------------------------------------------
//  This is the one hazard the PowerScript original cannot have, because PowerBuilder does not
//  inline PowerScript functions or reuse a caller's frame. In .NET both are possible, and either
//  one silently changes what the ported arithmetic trims:
//
//      * INLINING. If the capture were inlined into the implementation, or the implementation into
//        a delegating overload, or a delegating overload into the user's method, the collapsed
//        frame would vanish from the trace. The trim of two frames would then consume the USER's
//        frame instead of the framework's, and the reported trace would end one frame too early.
//        Every member here that participates in the frame structure therefore carries
//        MethodImplOptions.NoInlining.
//      * TAIL CALLS. The three delegating overloads have a body consisting solely of a call in
//        tail position, which is the one shape for which a JIT may reuse the caller's frame rather
//        than pushing a new one. Those three additionally carry MethodImplOptions.NoOptimization,
//        which disables that transformation for the method. This is frame preservation and is NOT
//        a performance statement in either direction; AAP 0.8.5 forbids asserting a performance
//        objective, and none is asserted here.
//
//  Both mitigations are verified by test rather than by reasoning: the ordering test drives a
//  deliberately nested call chain, and the self-exclusion test asserts that no returned frame names
//  this class. If a future runtime removes a frame anyway, those two tests fail loudly, which is
//  the whole reason they are written the way they are.
//
//  THE FRAME TEXT FORMAT IS DEFINED HERE, AND Assert.cs PARSES IT (C-K)
//  --------------------------------------------------------------------------------------------
//  pfw.dll's frame format cannot be read, so this file DEFINES the format. It is not free to
//  choose: assert.srf:L39-L59 parses each frame with four positional searches, and the format
//  exists to satisfy all four. See the format contract on DescribeFrame for the emitted shape, the
//  four invariants it guarantees, and the reasons the file path is deliberately absent from it.
//  Assert.cs is the parser and was authored after this file; it must be written against that
//  contract, and the two must never be changed independently.
//
//  THE SEPARATOR IS A BARE LINE FEED, AND THAT IS LOAD BEARING
//  --------------------------------------------------------------------------------------------
//  stacktraceinfo.srf:L33 joins frames with "~n", PowerScript for a bare line feed. It is NOT
//  CRLF and NOT the platform newline, and the difference is not cosmetic: Assert.cs embeds this
//  string as field 7 of a payload whose field delimiter IS CRLF [assert.srf:L19,L76], and
//  pfw.sra:L119 takes the deep branch only when splitting that payload on CRLF yields exactly
//  seven fields. A CRLF here would split field 7 into extra fields and silently push the consumer
//  down its shallow branch, losing the window, object, event and line number as well as the trace.
//  Environment.NewLine is therefore forbidden in this file, and a test asserts the returned string
//  contains no carriage return at all.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project: the rules document was retrieved and it
//  contains exactly one statement, that no user rules were provided. No rule is therefore invented
//  or inferred here, and the absence is not read as licence to lower the bar. The binding
//  constraints in their place are AAP 0.7.2, the enterprise standard baseline, and AAP 0.7.3, the
//  twelve non-rule constraints. Four of the twelve bind this file:
//
//      C-A  Pure behaviour, no I/O. Nothing here writes a file, opens a socket, logs, reads
//           configuration or touches the console. There is no static mutable state, so every
//           member is safe to call from any thread. The project carries zero package references
//           and this file imports nothing outside the Base Class Library.
//      C-B  No new features and no behaviour improvements, documented defects replicated. The two
//           stage arithmetic is preserved rather than collapsed, both bail-out conditions are
//           reproduced including the one that is unreachable in the port, the prefix is applied to
//           every line rather than only the first, and the exception swallow returns an empty
//           string rather than throwing, logging or reporting. Nothing validates an argument and
//           nothing throws: see the no-throw note on the class.
//      C-C  The legacy tree is read only and is the only specification. All five legacy paths above
//           were read and none was modified; every member cites its authorising locator.
//      C-K  Every technology-specific and boundary-specific decision is documented at its point of
//           reproduction. The substitution, the order reversal, the self-frame skip, the width
//           ruling, the JIT frame hazard, the frame text format and the separator each have their
//           own block, above or on the member that implements them.
//
//  This file declares NO SCREAMING_SNAKE and no underscore bearing identifier. That is a build
//  requirement rather than a preference: TreatWarningsAsErrors is inherited from
//  Directory.Build.props, and the repository root .editorconfig scopes its naming analyzer
//  suppressions to the individual files that genuinely carry preserved legacy constant
//  identifiers. This file is deliberately not one of them, because it declares no legacy constant
//  at all, so an identifier here that needed a suppression would be a build error with no way to
//  grant it.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON (C-B). Recorded so the omissions read as
//  decisions rather than oversights, and so nobody "completes" this file by adding one:
//
//      * A fifth StackTraceInfo overload. stacktraceinfo.srf declares exactly four prototypes
//        [:L7-L10] and they ARE the contract; the repository has one live call site, so no other
//        caller constrains the shape. No overload is added and none is removed.
//      * An overload taking int, long or a nullable offset. See the width ruling above.
//      * Any throw. A large offset returns an empty string, because that is what
//        stacktraceinfo.srf:L30 does. An ArgumentOutOfRangeException here would be a behaviour
//        change dressed as robustness.
//      * A StringBuilder. The accumulation below mirrors stacktraceinfo.srf:L33-L35 statement for
//        statement so it can be audited against the source line by line. No performance objective
//        is published anywhere in this repository, so none may be claimed as justification for
//        restructuring it (AAP 0.8.5).
//      * Frame filtering of any kind. Frames with no reflected method, runtime frames and test
//        host frames are all kept, because dropping one would change the count and therefore what
//        the ported `nCount - offset - 1` and assert.srf's `nCount - 2` actually select.
//      * A thread, thread-affinity or message-pump API. AAP 0.6.5 records those primitives as
//        deliberate non-ports, and nothing here needs one.
//      * A logging or telemetry hook. Forbidden by C-A, and the legacy has none: the swallow at
//        stacktraceinfo.srf:L38-L40 is an empty handler.
//
//  DEPENDENCIES. Base Class Library only, and there is deliberately NO `using System.Diagnostics;`
//  anywhere in this file. Omitting it is what makes the name collision between
//  System.Diagnostics.StackTrace and this project's own PowerFramework.Shared.Diagnostics
//  namespace structurally impossible rather than merely avoided, so both that type and StackFrame
//  are spelled in full at every use. For the same reason no TYPE in this project may be named
//  StackTrace; the AssertionFailure.StackTrace member already owns that spelling, and the name
//  StackTraceProvider exists precisely to keep out of its way. PowerFramework.Shared.Kernel is
//  also deliberately not imported: this project references it for the return-code predicates that
//  the assertion overloads in Assert.cs guard on, and nothing in THIS file needs a symbol from it.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==============================================================================================

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PowerFramework.Shared.Diagnostics;

/// <summary>
/// The call stack capture primitive and the four rendering overloads ported from the legacy
/// <c>pfw.common</c> library's <c>stacktrace</c> and <c>stacktraceinfo</c> function objects.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StackTrace(out string[])"/> substitutes for <c>pfwStackTrace</c>, a classic external
/// prototype bound to the closed <c>pfw.dll</c> whose body cannot be read anywhere in the
/// repository [stacktrace.srf:L7]. The four <see cref="StackTraceInfo()"/> overloads render a
/// captured stack as one string and are a line-for-line port of pure PowerScript
/// [stacktraceinfo.srf:L13-L43].
/// </para>
/// <para>
/// <b>Frames are ordered OUTERMOST FIRST</b>, matching the legacy one-based array in which index 1
/// is the outermost frame and index <c>count</c> is the innermost. That is the opposite of
/// <see cref="System.Diagnostics.StackTrace.GetFrames"/>, so the capture reverses the runtime's
/// order. See the file header for the proof taken from <c>assert.srf</c> and for why removing the
/// reversal produces a wrong report that still passes a frame-count assertion.
/// </para>
/// <para>
/// <b>Nothing here throws and nothing validates an argument.</b> Every member returns a value for
/// every input: an offset larger than the captured depth yields <see cref="string.Empty"/>
/// [stacktraceinfo.srf:L30], and any exception raised while capturing or rendering is swallowed and
/// also yields <see cref="string.Empty"/> [stacktraceinfo.srf:L38-L40]. Both are the ported legacy
/// behaviour rather than defensive coding, and neither may be replaced with an exception.
/// </para>
/// <para>
/// Every member is a pure function of its arguments and the current call stack. There is no static
/// mutable state, so all members are safe to call concurrently from any thread; each observes only
/// the stack of the thread that calls it.
/// </para>
/// </remarks>
public static class StackTraceProvider
{
    // ------------------------------------------------------------------------------------------
    // DECISION 1 - THE SEPARATOR IS A BARE LINE FEED
    // ------------------------------------------------------------------------------------------
    // Ports "~n" [stacktraceinfo.srf:L33]. Declared as a named constant so that a maintainer
    // reaching for Environment.NewLine has to delete a comment that explains why they must not:
    // Assert.cs carries this string as field 7 of a CRLF-delimited payload and pfw.sra:L119 takes
    // its deep branch only on exactly seven fields, so a CRLF here silently costs the consumer the
    // window, object, event, line number and trace all at once.
    // ------------------------------------------------------------------------------------------
    private const string FrameSeparator = "\n";

    // ------------------------------------------------------------------------------------------
    // DECISION 2 - THE CAPTURE SKIPS EXACTLY ITS OWN FRAME
    // ------------------------------------------------------------------------------------------
    // pfwStackTrace is a native export and therefore not a PowerScript frame, so the innermost
    // frame of the legacy array is the PowerScript routine that CALLED it - the implementation at
    // stacktraceinfo.srf:L26, or assertfailed at assert.srf:L30. The managed substitute is an
    // ordinary method and does appear on the stack, so it excludes itself to reproduce that.
    //
    // The value is 1, not 2 and not 0, because System.Diagnostics.StackTrace(int, bool) begins at
    // the frame of the method that constructs it and then skips the stated number of ADDITIONAL
    // frames: 0 would start at the capture method itself, 1 starts at its caller. There is no
    // private helper between the constructor and the caller, which is what keeps the value at 1;
    // adding one would silently make this number wrong. It is verified by the self-exclusion test
    // rather than by counting, exactly as the file header requires.
    // ------------------------------------------------------------------------------------------
    private const int SelfFrameSkipCount = 1;

    // ------------------------------------------------------------------------------------------
    // DECISION 3 - THE THREE FRAME TEXT TOKENS
    // ------------------------------------------------------------------------------------------
    // These three literals, together with the dot that joins scope to type to method, are the
    // whole of the frame text format. They are named rather than inlined so that the format
    // contract on DescribeFrame has something to point at, because Assert.cs parses exactly this
    // shape and the two files must not drift apart. Do not add a file path token: see the format
    // contract for why a dot in the tail would break the parser.
    // ------------------------------------------------------------------------------------------
    private const string LineNumberIntroducer = " line:";
    private const string UnknownScopeName = "<unknown>";
    private const string UnknownMemberName = "?";

    /// <summary>
    /// Captures the current call stack, returning the frame count and, through
    /// <paramref name="callStack"/>, the frames themselves rendered as text.
    /// </summary>
    /// <param name="callStack">
    /// Receives one string per captured frame. <b>Index 0 is the OUTERMOST frame and the INNERMOST
    /// frame is last.</b> Never <see langword="null"/>; an empty array when nothing could be
    /// captured.
    /// </param>
    /// <returns>
    /// The number of frames placed in <paramref name="callStack"/>, which both callers test before
    /// they index the array [stacktraceinfo.srf:L27, assert.srf:L37].
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/stacktrace.srf:L7</c>, prototype
    /// <c>global function int StackTrace (ref string callstack[]) system library "pfw.dll" alias
    /// for "pfwStackTrace"</c>. That is a CLASSIC EXTERNAL PROTOTYPE, the second of the two native
    /// binding mechanisms in this estate rather than a PBNI class binding: the type declaration at
    /// :L3-L4 carries no <c>native</c> clause and the whole binding sits on the prototype. The body
    /// lives in the closed <c>pfw.dll</c>, for which no C++ source exists anywhere in the
    /// repository, so AAP 0.6.5 classifies the primitive as SUBSTITUTE and
    /// <see cref="System.Diagnostics.StackTrace"/> is the substitute used here.
    /// </para>
    /// <para>
    /// The legacy <c>ref</c> becomes <c>out</c>: the caller's array is an empty local
    /// [stacktraceinfo.srf:L23, assert.srf:L17] and the primitive replaces it wholesale, so nothing
    /// reads the incoming value and <c>out</c> states that contract precisely.
    /// </para>
    /// <para>
    /// <b>ORDERING CONTRACT: index 0 of <paramref name="callStack"/> is the outermost frame; the
    /// innermost frame is last.</b> <see cref="System.Diagnostics.StackTrace"/> numbers frames the
    /// other way round, so this method reverses them. Removing the reversal produces a
    /// plausible-looking report of exactly the right length that names the wrong routine and
    /// discards the application's outermost frames, and a frame-count assertion would still pass -
    /// the failure mode AAP 0.8.6 R9 names. The order is proved by <c>assert.srf</c>, which selects
    /// the frame to blame as <c>sCallStack[nCount - 2]</c> [assert.srf:L38] and copies frames
    /// <c>1</c> through <c>nCount - 2</c> into the payload [assert.srf:L61]; under this contract
    /// "skip the two innermost frames" is simply dropping the last two entries.
    /// </para>
    /// <para>
    /// This method's own frame is excluded, mirroring a native export that cannot observe itself, so
    /// the innermost frame returned is the method that called it. See <c>DECISION 2</c> above for
    /// why the skip count is exactly 1. No other frame is filtered - not frames without a reflected
    /// method, not runtime frames and not test host frames - because dropping one would change the
    /// count and therefore change which frame the ported arithmetic selects.
    /// </para>
    /// <para>
    /// <b>CONTRACT FOR CALLERS.</b> The count returned here is a count of frames INCLUDING the
    /// caller's own, exactly as the legacy array is, so a caller that means to exclude itself and
    /// its own public entry point trims two frames from the innermost end. A caller must also carry
    /// <see cref="MethodImplOptions.NoInlining"/> on every method between itself and the frame it
    /// intends to name, or the JIT may collapse one and the trim will consume a frame too many.
    /// That applies to <c>Assert.cs</c>: its <c>AssertFailed</c> and its seven public overloads all
    /// need the attribute for <c>assert.srf:L38</c>'s selection to land on the user's frame.
    /// </para>
    /// <para>
    /// Each frame is rendered by <c>DescribeFrame</c>; read the format contract there before
    /// parsing the text, and never change it without changing the parser in <c>Assert.cs</c> in the
    /// same commit.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StackTrace(out string[] callStack)
    {
        // fNeedFileInfo must be true: without it StackFrame.GetFileLineNumber always returns 0 and
        // the line number in every frame would be a constant rather than a degraded value.
        System.Diagnostics.StackTrace trace =
            new System.Diagnostics.StackTrace(SelfFrameSkipCount, fNeedFileInfo: true);

        int frameCount = trace.FrameCount;
        if (frameCount < 1)
        {
            // Return an empty array rather than null so that a caller which ignores the count - as
            // assert.srf:L30 does inside its catch - still holds an indexable array. The legacy
            // array is likewise never null, only empty.
            callStack = [];
            return 0;
        }

        string[] frames = new string[frameCount];

        // THE REVERSAL. trace index 0 is the INNERMOST frame, so it is written to the LAST slot,
        // and the outermost frame lands at slot 0. FrameCount and GetFrame are used in preference
        // to GetFrames so that no nullability of the returned array has to be reasoned about; the
        // per-frame null that GetFrame can return is handled inside DescribeFrame.
        for (int traceIndex = 0; traceIndex < frameCount; traceIndex++)
        {
            frames[frameCount - 1 - traceIndex] = DescribeFrame(trace.GetFrame(traceIndex));
        }

        callStack = frames;
        return frames.Length;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 4 - THE TWO STAGE TRIM ARITHMETIC IS PRESERVED, NOT COLLAPSED
    // ------------------------------------------------------------------------------------------
    // The four overloads below trim frames from the INNERMOST end in two stages, and the stages
    // must stay separate. The delegating overloads choose an offset [stacktraceinfo.srf:L13,L16,
    // L19] and the implementation then subtracts `offset + 1` [stacktraceinfo.srf:L29], so:
    //
    //      StackTraceInfo()               offset 1          trims 2
    //      StackTraceInfo(prefix)         offset 1          trims 2
    //      StackTraceInfo(0)              offset 0 + 1 = 1  trims 2   <- identical to the above
    //      StackTraceInfo(n)              offset n + 1      trims n + 2
    //      StackTraceInfo(0, prefix)      offset 0          trims 1
    //      StackTraceInfo(n, prefix)      offset n          trims n + 1
    //
    // Two frames are trimmed by the parameterless and prefix-only forms because two provider frames
    // are on the stack at that point - the delegating overload and the implementation - so every
    // form's natural zero lands on the CALLER's frame as the innermost retained entry. That is the
    // whole point of the `+ 1` at :L19, and it is why folding the two subtractions into one would
    // make StackTraceInfo(0) disagree with StackTraceInfo() from the same call site: exactly the
    // off-by-one AAP 0.4.5.4 and 0.8.6 R9 warn about. An overload-equivalence test pins it.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders the current call stack as one string, one frame per line, outermost frame first.
    /// </summary>
    /// <returns>
    /// The frames joined by a single line feed, or <see cref="string.Empty"/> if the stack could not
    /// be captured or is shallower than the frames this overload trims.
    /// </returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/stacktraceinfo.srf:L13</c>, whose entire body is
    /// <c>return StackTraceInfo(1,"")</c>. <b>Effective trim: 2 frames</b> from the innermost end,
    /// which are this overload's own frame and the implementation's, so the innermost frame in the
    /// result is the method that called this. Identical output to
    /// <see cref="StackTraceInfo(ushort)"/> called with <c>0</c> from the same call site; see
    /// <c>DECISION 4</c> above.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static string StackTraceInfo()
    {
        return StackTraceInfo(1, string.Empty);
    }

    /// <summary>
    /// Renders the current call stack as one string with <paramref name="prefix"/> prepended to
    /// every line, outermost frame first.
    /// </summary>
    /// <param name="prefix">
    /// Text placed in front of EVERY frame, not only the first. An empty string prepends nothing.
    /// </param>
    /// <returns>
    /// The prefixed frames joined by a single line feed, or <see cref="string.Empty"/> if the stack
    /// could not be captured or is shallower than the frames this overload trims.
    /// </returns>
    /// <remarks>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/stacktraceinfo.srf:L16</c>, whose entire body is
    /// <c>return StackTraceInfo(1,prefix)</c>. <b>Effective trim: 2 frames</b>, as for
    /// <see cref="StackTraceInfo()"/>. This is the overload the behavioural oracle exercises, with
    /// prefix <c>"-- "</c> [w_test_assert.srw:L79], and it is the only live call site of any of
    /// these four in the whole legacy repository.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static string StackTraceInfo(string prefix)
    {
        return StackTraceInfo(1, prefix);
    }

    /// <summary>
    /// Renders the current call stack as one string, outermost frame first, discarding
    /// <paramref name="offset"/> further frames from the innermost end.
    /// </summary>
    /// <param name="offset">
    /// Additional frames to discard beyond the two this overload already accounts for. PowerBuilder
    /// <c>uint</c> is 16-bit unsigned, hence <see cref="ushort"/>; see the width ruling in the file
    /// header.
    /// </param>
    /// <returns>
    /// The frames joined by a single line feed, or <see cref="string.Empty"/> if the stack could not
    /// be captured or is shallower than the frames this overload trims.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/stacktraceinfo.srf:L19</c>, whose entire body is
    /// <c>return StackTraceInfo(offset + 1,"")</c>. <b>Effective trim: <c>offset + 2</c> frames</b>,
    /// because the implementation subtracts a further <c>1</c>. So <c>StackTraceInfo(0)</c> trims 2
    /// and is identical to <see cref="StackTraceInfo()"/>; see <c>DECISION 4</c> above.
    /// </para>
    /// <para>
    /// The <c>+ 1</c> is evaluated in <see cref="int"/> and narrowed back to <see cref="ushort"/> in
    /// an <c>unchecked</c> context, which reproduces PowerBuilder's silent narrowing on assignment
    /// to a 16-bit parameter. The only value it changes is <see cref="ushort.MaxValue"/>, which
    /// wraps to <c>0</c> and therefore trims 1 rather than 65,537; every other offset large enough
    /// to matter falls out through the guard at <c>stacktraceinfo.srf:L30</c> and returns
    /// <see cref="string.Empty"/>. A checked conversion would throw, which C-B forbids, and a
    /// saturating one would invent behaviour the legacy does not have.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static string StackTraceInfo(ushort offset)
    {
        return StackTraceInfo(unchecked((ushort)(offset + 1)), string.Empty);
    }

    /// <summary>
    /// Renders the current call stack as one string with <paramref name="prefix"/> prepended to
    /// every line, outermost frame first, discarding <paramref name="offset"/> frames beyond this
    /// method's own from the innermost end. This is the single implementation the other three
    /// overloads delegate to.
    /// </summary>
    /// <param name="offset">
    /// Additional frames to discard beyond this method's own. PowerBuilder <c>uint</c> is 16-bit
    /// unsigned, hence <see cref="ushort"/>; see the width ruling in the file header.
    /// </param>
    /// <param name="prefix">
    /// Text placed in front of EVERY frame, not only the first. An empty string prepends nothing.
    /// </param>
    /// <returns>
    /// The prefixed frames joined by a single line feed, with no leading and no trailing separator;
    /// or <see cref="string.Empty"/> when the stack could not be captured, when the trim consumes
    /// the whole stack, or when any exception is raised while capturing or rendering.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>ws_objects/pfw.common.pbl.src/stacktraceinfo.srf:L22-L43</c>. The body below
    /// follows that source statement for statement, and each statement carries its line number, so
    /// the two can be read side by side. <b>Effective trim: <c>offset + 1</c> frames</b>
    /// [stacktraceinfo.srf:L29], so a direct call with <c>offset</c> of <c>0</c> discards only this
    /// method's own frame and the innermost frame in the result is the caller's.
    /// </para>
    /// <para>
    /// The separator is a bare line feed and never CRLF or <see cref="Environment.NewLine"/>
    /// [stacktraceinfo.srf:L33]; the file header records why that is load bearing rather than
    /// cosmetic. The separator is emitted BEFORE each frame after the first
    /// [stacktraceinfo.srf:L33], which is what produces a value with no leading and no trailing
    /// separator, and the prefix is emitted for every frame [stacktraceinfo.srf:L34], not only the
    /// first.
    /// </para>
    /// <para>
    /// The local variable names below are the legacy names from <c>stacktraceinfo.srf:L22-L23</c>,
    /// kept deliberately so the port can be audited against the source without a mapping table.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string StackTraceInfo(ushort offset, string prefix)
    {
        // [stacktraceinfo.srf:L22-L23] locals. A PowerScript string local starts as the empty
        // string, and that initial value is observable: it is what an exception path returns.
        string sCallStackInfo = string.Empty;

        try
        {
            // [stacktraceinfo.srf:L26]
            int nCount = StackTrace(out string[] sCallStack);

            // [stacktraceinfo.srf:L27] BAIL-OUT 1, on the count alone and before any arithmetic. It
            // is the defence against a capture that yielded nothing at all, and like the second arm
            // of BAIL-OUT 2 it is unreachable in this port, because a live managed call stack always
            // reports at least the frame that called the capture. Reproduced verbatim regardless:
            // C-B forbids dropping a guard because the ported implementation cannot trigger it, and
            // for pfw.dll a zero return is a real possibility this file cannot rule out.
            if (nCount < 1)
            {
                return string.Empty;
            }

            // [stacktraceinfo.srf:L29] The second stage of the trim. See DECISION 4; do not fold
            // this into the offset the delegating overloads pass.
            nCount -= offset + 1;

            // [stacktraceinfo.srf:L30] BAIL-OUT 2, and it is TWO conditions rather than one.
            //
            // The first arm is live: it fires whenever the trim consumed the whole stack, which is
            // what makes a large offset return the empty string instead of throwing.
            //
            // The second arm compares against UpperBound, PowerBuilder's LAST VALID INDEX, and it
            // is unreachable in this port because nCount is derived from the same array's length
            // and then only ever decremented. It is reproduced verbatim anyway: C-B forbids
            // dropping a guard on the grounds that the ported implementation cannot trigger it, and
            // in the legacy it is the defence against a native primitive that reports more frames
            // than it filled - something this file cannot rule out for pfw.dll, only for itself.
            if (nCount <= 0 || nCount > UpperBound(sCallStack))
            {
                return string.Empty;
            }

            // [stacktraceinfo.srf:L32-L36] One-based iteration over the OUTERMOST nCount frames,
            // which is what discards the innermost `offset + 1` of them. FrameAt is the centralized
            // one-based accessor; nothing here indexes the array directly.
            for (int nIndex = 1; nIndex <= nCount; nIndex++)
            {
                // [stacktraceinfo.srf:L33]
                if (nIndex > 1)
                {
                    sCallStackInfo += FrameSeparator;
                }

                // [stacktraceinfo.srf:L34] The legacy test is `prefix <> ""`. A null prefix takes
                // the same path to the same result: the comparison is true, and concatenating null
                // appends nothing, which is also what PowerScript does when the test yields null.
                if (prefix != string.Empty)
                {
                    sCallStackInfo += prefix;
                }

                // [stacktraceinfo.srf:L35]
                sCallStackInfo += FrameAt(sCallStack, nIndex);
            }
        }
        catch (Exception)
        {
            // [stacktraceinfo.srf:L38-L40] `catch(throwable ex)` with a body that only clears the
            // accumulator. The legacy swallows the exception without logging or rethrowing, and
            // returning the empty string is therefore deliberate ported behaviour and NOT defensive
            // coding to be "improved" into a throw, a log call or a partial result.
            //
            // The exception variable is deliberately absent. The legacy declares `ex` and never
            // reads it; a declared-and-unused C# variable raises CS0168, which the inherited
            // TreatWarningsAsErrors turns into a build failure.
            sCallStackInfo = string.Empty;
        }

        // [stacktraceinfo.srf:L42]
        return sCallStackInfo;
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 5 - ONE CENTRALIZED PAIR OF ONE BASED ACCESSORS FOR THE WHOLE PROJECT
    // ------------------------------------------------------------------------------------------
    // AAP 0.4.5.4 calls one-based to zero-based translation the single most dangerous mechanical
    // hazard in this refactor, because a silent off-by-one is indistinguishable from a behavioural
    // regression. The two members below are the only place in this project where a captured frame
    // array is indexed or measured, so every ported one-based expression goes through them and no
    // ported expression ever mixes one-based arithmetic with a raw zero-based index.
    //
    // They are INTERNAL rather than private on purpose. Assert.cs ports one-based arithmetic over
    // the same array - `sCallStack[nCount - 2]` [assert.srf:L38] and `for nIndex = 1 to nCount - 2`
    // [assert.srf:L61-L64] - and must reuse these rather than duplicate them, which is what keeps
    // the convention in one auditable place. The project's InternalsVisibleTo also makes them
    // directly testable, so the discipline is verified rather than trusted.
    //
    // The legacy append idiom `arr[UpperBound(arr) + 1] = value` [assert.srf:L62] has no accessor
    // here by design: it becomes an ordinary list Add, so it needs no index arithmetic at all.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the LAST VALID one-based index of <paramref name="frames"/>, reproducing PowerScript
    /// <c>UpperBound</c>.
    /// </summary>
    /// <param name="frames">A captured frame array as returned by
    /// <see cref="StackTrace(out string[])"/>.</param>
    /// <returns>
    /// The last valid one-based index, which is the array's length; <c>0</c> for an empty array,
    /// matching PowerScript's upper bound for an unallocated array.
    /// </returns>
    /// <remarks>
    /// Reproduces the <c>UpperBound(sCallStack)</c> of <c>stacktraceinfo.srf:L30</c>. The value is a
    /// LAST INDEX and not a count-past-the-end, which is exactly why the ported guard compares
    /// against it with <c>&gt;</c> rather than <c>&gt;=</c>. Under one-based access it happens to
    /// share the same numeric value as the managed length, and that coincidence is the reason the
    /// ported comparison needs no adjustment - stating it here is what stops a future reader from
    /// "correcting" the operator.
    /// </remarks>
    internal static int UpperBound(string[] frames)
    {
        return frames.Length;
    }

    /// <summary>
    /// Returns the frame at a ONE-BASED index, reproducing PowerScript array access.
    /// </summary>
    /// <param name="frames">A captured frame array as returned by
    /// <see cref="StackTrace(out string[])"/>.</param>
    /// <param name="oneBasedIndex">
    /// A one-based index, so <c>1</c> is the OUTERMOST frame and
    /// <see cref="UpperBound(string[])"/> is the innermost.
    /// </param>
    /// <returns>The frame text at that one-based position.</returns>
    /// <exception cref="IndexOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is outside <c>1..UpperBound(frames)</c>. Callers guard
    /// first, exactly as the legacy does [stacktraceinfo.srf:L27,L30 and assert.srf:L37], and the
    /// one caller in this file runs inside the ported swallow, so an out-of-range index surfaces as
    /// the empty string rather than as an exception.
    /// </exception>
    /// <remarks>
    /// Reproduces <c>sCallStack[nIndex]</c> [stacktraceinfo.srf:L35]. No bounds check is added here:
    /// PowerScript raises its own error on an out-of-range subscript, so the array's own exception
    /// is the faithful behaviour, and adding a check that returned a substitute value would be the
    /// behaviour change C-B forbids.
    /// </remarks>
    internal static string FrameAt(string[] frames, int oneBasedIndex)
    {
        return frames[oneBasedIndex - 1];
    }

    // ------------------------------------------------------------------------------------------
    // DECISION 6 - THE FRAME TEXT FORMAT CONTRACT
    // ------------------------------------------------------------------------------------------
    // pfw.dll's frame format cannot be read, so this file DEFINES it. The format is not a free
    // choice: assert.srf:L39-L59 parses every frame with four positional searches, and it is
    // Assert.cs that will reproduce that parser, so producer and parser are two files that must
    // agree. The emitted shape is
    //
    //      <namespace>.<typeChain>.<method> line:<lineNumber>
    //
    // and it satisfies all four of the parser's searches:
    //
    //      assert.srf:L39  LastPos(frame, ".")     -> the dot before the method name
    //      assert.srf:L41  Pos(frame, ".")         -> the first dot. When it precedes the last dot
    //                                                 the text before it is WindowMenu and the text
    //                                                 between the two is Object [:L44,L46]; when
    //                                                 there is exactly one dot, WindowMenu and
    //                                                 Object become the SAME value [:L49,L51], and
    //                                                 the consumer relies on that equality to
    //                                                 suppress its window line [pfw.sra:L131]
    //      assert.srf:L55  Pos(frame, " ", last+1) -> the space that ends the method name, supplied
    //                                                 by the leading space of " line:"
    //      assert.srf:L58  Pos(frame, ":", space+1)-> the colon that introduces the line number,
    //                                                 supplied by the trailing colon of " line:",
    //                                                 with Long(Mid(...)) reading the digits to the
    //                                                 end of the string [:L59]
    //
    // FOUR INVARIANTS THIS FORMAT GUARANTEES FOR EVERY FRAME, INCLUDING DEGRADED ONES:
    //      I1  at least one dot exists, so the LastPos search never returns 0
    //      I2  a space follows the last dot
    //      I3  a colon follows that space
    //      I4  the string ends in decimal digits
    //
    // WHY THE FILE PATH IS ABSENT, AND MUST STAY ABSENT. A file name carries a dot - ".cs" - which
    // would become the LAST dot in the string and move the parser's method-name window onto the
    // extension. The tail is therefore restricted to " line:" plus digits, and nothing with a dot
    // may ever be appended after the method name.
    //
    // WHY Type.Name IS WALKED RATHER THAN Type.FullName USED. For a CONSTRUCTED GENERIC type
    // FullName embeds assembly-qualified argument names containing dots, commas, spaces and equals
    // signs, any one of which breaks I1 to I4. Type.Name is safe for every case - it is "Foo`1" for
    // a generic type and a bare name for a nested one - so the nested chain is assembled from Name
    // by walking DeclaringType outward and the namespace is taken from Namespace, which already
    // reports the outermost type's namespace for a nested type.
    //
    // GRACEFUL DEGRADATION, ALL FOUR CASES, NONE OF WHICH THROWS OR BREAKS THE PARSE:
    //      * No StackFrame at all, or no reflected method (System.Diagnostics.StackFrame.GetFrame
    //        may return null and GetMethod may return null for a frame the runtime cannot describe):
    //        the scope becomes the unknown-scope token and the method the unknown-member token,
    //        which still yields I1 through I4.
    //      * No namespace, for a type in the global namespace or a compiler-generated one: the
    //        frame carries exactly ONE dot and the parser takes its defined single-dot branch, in
    //        which WindowMenu and Object are equal [assert.srf:L49-L51]. That is a supported branch
    //        of the parser, not the degenerate path - the degenerate path is a MISSING COLON, which
    //        I3 rules out unconditionally.
    //      * No line number, which is the normal case for a Release build with no portable PDB:
    //        GetFileLineNumber returns 0 and the frame ends ":0". The colon is still present, so I3
    //        and I4 hold and the parser reads a line number of zero rather than failing. No PDB is
    //        demanded and no build setting is changed to obtain one.
    //      * A constructor. MethodBase.Name is ".ctor" or ".cctor", whose leading dot would make
    //        the type portion end in a stray dot and put "ctor" where the method name belongs. The
    //        leading dot is stripped so a constructor emits as "<scope>.<Type>.ctor", keeping
    //        exactly one dot between the type and the method.
    //
    // ROBUSTNESS NOTE WORTH KNOWING. An exotic compiler-generated identifier containing a space or
    // a dot can only degrade the Object and ObjectEvent split; the LINE NUMBER still parses,
    // because the parser searches for the colon strictly AFTER the space it has already found. No
    // sanitisation of identifiers is performed, because a substituted character would misreport the
    // routine while looking correct, which is worse than a split that is visibly odd.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders one captured frame as the single line of text that <c>Assert.cs</c> parses.
    /// </summary>
    /// <param name="frame">
    /// The frame to render, or <see langword="null"/> when the runtime could not supply one.
    /// </param>
    /// <returns>
    /// <c>&lt;namespace&gt;.&lt;typeChain&gt;.&lt;method&gt; line:&lt;lineNumber&gt;</c>. Never
    /// <see langword="null"/> and never empty.
    /// </returns>
    /// <remarks>
    /// See <c>DECISION 6</c> above for the format contract, the four invariants every emitted frame
    /// satisfies, the four degradation cases, and the reasons the file path is absent and
    /// <c>Type.Name</c> is walked rather than <see cref="Type.FullName"/> used. This format
    /// has no legacy equivalent to copy - the native primitive's output cannot be read - so it is
    /// defined here and <c>Assert.cs</c> must be authored against it.
    /// </remarks>
    private static string DescribeFrame(System.Diagnostics.StackFrame? frame)
    {
        MethodBase? method = frame?.GetMethod();

        // Degradation cases 1 and 4: no frame or no reflected method, and a constructor's leading
        // dot stripped. Both keep invariant I1, at least one dot, intact.
        string scope = method?.DeclaringType is Type declaringType
            ? DescribeDeclaringType(declaringType)
            : UnknownScopeName;
        string member = DescribeMemberName(method);

        // Degradation case 3: GetFileLineNumber returns 0 without a portable PDB, so the tail
        // degrades to ":0" and keeps invariants I3 and I4 rather than losing the colon.
        int lineNumber = frame is null ? 0 : frame.GetFileLineNumber();

        return scope + "." + member + LineNumberIntroducer +
            lineNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Renders a declaring type as the dot-separated scope portion of a frame: its namespace when it
    /// has one, then its enclosing types outermost first, then its own name.
    /// </summary>
    /// <param name="declaringType">The type that declares the frame's method.</param>
    /// <returns>
    /// The scope text, which contains no space and no colon so that it cannot disturb the frame
    /// format's invariants.
    /// </returns>
    /// <remarks>
    /// Built from <see cref="MemberInfo.Name"/> and <see cref="Type.Namespace"/> only, never from
    /// <see cref="Type.FullName"/>; <c>DECISION 6</c> records why. A type with no namespace yields a
    /// frame carrying exactly one dot, which is the parser's defined single-dot branch
    /// [assert.srf:L49-L51] rather than a failure.
    /// </remarks>
    private static string DescribeDeclaringType(Type declaringType)
    {
        // Nested types first: Name reports only the innermost segment, so walk DeclaringType
        // outward and prepend, which puts the outermost enclosing type first.
        string chain = declaringType.Name;
        for (Type? enclosing = declaringType.DeclaringType;
            enclosing is not null;
            enclosing = enclosing.DeclaringType)
        {
            chain = enclosing.Name + "." + chain;
        }

        // Namespace already reports the OUTERMOST type's namespace for a nested type, so it is read
        // from the declaring type rather than from the walked chain.
        string? typeNamespace = declaringType.Namespace;
        if (string.IsNullOrEmpty(typeNamespace))
        {
            return chain;
        }

        return typeNamespace + "." + chain;
    }

    /// <summary>
    /// Renders a frame's method name as the member portion of a frame, stripping the leading dot
    /// that <see cref="MemberInfo.Name"/> carries for a constructor.
    /// </summary>
    /// <param name="method">The frame's method, or <see langword="null"/> when unavailable.</param>
    /// <returns>
    /// The member text, which never begins with a dot, so exactly one dot separates the scope from
    /// the member and the frame format's invariants hold.
    /// </returns>
    /// <remarks>
    /// Degradation case 4 of <c>DECISION 6</c>: an instance constructor reports <c>.ctor</c> and a
    /// static one <c>.cctor</c>, so without the strip the type portion would end in a stray dot and
    /// the parser would report <c>ctor</c> as the object rather than the event.
    /// </remarks>
    private static string DescribeMemberName(MethodBase? method)
    {
        string name = method?.Name ?? UnknownMemberName;
        if (name.Length == 0)
        {
            return UnknownMemberName;
        }

        if (name[0] == '.')
        {
            // ".ctor" -> "ctor", ".cctor" -> "cctor". A name that is nothing but a dot would leave
            // the member portion empty and break invariant I2, so it degrades to the token instead.
            return name.Length > 1 ? name.Substring(1) : UnknownMemberName;
        }

        return name;
    }
}
