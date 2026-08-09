// ==================================================================================================
//  StackTraceProviderTests.cs - CAPTURE, REVERSAL, AND THE TWO-STAGE TRIM
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Diagnostics.StackTraceProvider
//  ORACLES           ws_objects/pfw.common.pbl.src/stacktrace.srf       pfwStackTrace
//                    ws_objects/pfw.common.pbl.src/stacktraceinfo.srf   pfwStackTraceInfo, 4 overloads
//
//  THE THREE BEHAVIOURS THAT HAVE TO BE EXACT
//  ------------------------------------------------------------------------------------------------
//  1. THE REVERSAL. The framework's own capture reports the INNERMOST frame first. The legacy array
//     reports the OUTERMOST first, so the port writes trace index 0 into the LAST slot. Every consumer
//     depends on this: assert.srf appends frames in array order into the payload the consumer prints,
//     so a non-reversed array would print the call stack upside down.
//
//  2. THE TWO-STAGE TRIM, AND WHAT THE SECOND STAGE IS ACTUALLY FOR. The delegating overloads choose an
//     offset, and the worker then subtracts `offset + 1`. MEASURED on this host, the effect is that
//     every overload presents the SAME view from a given call site: the CALLER's own frame innermost,
//     and outward from there, with an offset removing that many more from the innermost end.
//
//         StackTraceInfo()                  -> offset 1     -> caller innermost        (delta  0)
//         StackTraceInfo(prefix)            -> offset 1     -> caller innermost        (delta  0)
//         StackTraceInfo((ushort)n)         -> offset n+1    -> caller innermost, -n   (delta -n)
//         StackTraceInfo((ushort)n, prefix) -> offset n      -> caller innermost, -n   (delta -n)
//
//     So the `+1` the delegating overloads add is NOT redundant and NOT an off-by-one: it exists to hide
//     the DELEGATING OVERLOAD'S OWN FRAME, which the worker's capture would otherwise report. The extra
//     frame and the extra offset cancel exactly, which is why the one- and two-argument forms AGREE for
//     every n rather than differing. That agreement is the property asserted below, and it is what a
//     "simplification" - dropping the +1, or letting the delegator inline - would break, by leaking a
//     frame belonging to the diagnostic helper into the diagnosed stack.
//
//     The one visible artefact of the two stages is at the numeric boundary: the delegating overload's
//     increment is `unchecked`, so an offset of ushort.MaxValue wraps to 0 and the delegator's own frame
//     is then NO LONGER hidden - the rendered trace carries one frame MORE than normal. The two-argument
//     overload performs no increment and returns the empty string for the same value. Both are asserted.
//
//  3. THE BAIL-OUTS RETURN THE EMPTY STRING, NEVER A PARTIAL RESULT AND NEVER A THROW. An over-large
//     offset consumes the whole stack and the answer is "". The catch-all likewise resets the
//     accumulator to "" rather than returning what it had built [stacktraceinfo.srf:L38-L40].
//
//  WHY THESE TESTS DO NOT ASSERT ABSOLUTE FRAME COUNTS
//  ------------------------------------------------------------------------------------------------
//  The captured depth includes the xunit runner's own frames, which vary by runner version and by
//  whether a test is a fact or a theory. So every assertion here is RELATIVE - a difference between two
//  captures taken the same way, an ordering, a shape, or a monotonic relationship - never "the stack is
//  N frames deep". That is what makes the suite stable without weakening what it proves. The
//  [MethodImpl(NoInlining)] on the production methods is what makes even the relative claims sound, and
//  the helpers here carry it for the same reason.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour,
//  including guards the port cannot trigger), C-K (document boundary decisions), AAP 0.4.5.4 (one-based
//  indexing is the refactor's most dangerous mechanical hazard).
// ==================================================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Characterization tests for <see cref="StackTraceProvider"/>.
/// </summary>
public class StackTraceProviderTests
{
    /// <summary>
    /// The separator the provider joins frames with: a bare line feed, matching
    /// <c>stacktraceinfo.srf:L33</c>.
    /// </summary>
    private const string FrameSeparator = "\n";

    /// <summary>
    /// The literal that introduces the line number in a rendered frame.
    /// </summary>
    private const string LineNumberIntroducer = " line:";

    // ==============================================================================================
    //  1. CAPTURE
    // ==============================================================================================

    /// <summary>
    /// <see cref="StackTraceProvider.StackTrace"/> returns the frame count, and it always equals the
    /// length of the array it hands back.
    /// </summary>
    /// <remarks>
    /// The count and the array must agree because <c>assert.srf</c> iterates one-based from 1 to the
    /// returned count while indexing the array [assert.srf:L61-L62]. A count larger than the array is
    /// the exact shape of the failure the second bail-out arm defends against, so the agreement is
    /// asserted directly rather than assumed.
    /// </remarks>
    [Fact]
    public void TheReturnedCountAlwaysEqualsTheArrayLength()
    {
        int count = StackTraceProvider.StackTrace(out string[] frames);

        Assert.Equal(frames.Length, count);
        Assert.True(count > 0, "A live managed call stack always reports at least the calling frame.");
    }

    /// <summary>
    /// The out-array is never null, even though the count may in principle be zero.
    /// </summary>
    /// <remarks>
    /// <c>assert.srf:L30</c> ignores the count inside its catch and indexes the array anyway, so a null
    /// array would be a null dereference on the failure path - the one path that must not fail. The
    /// production code returns <c>[]</c> rather than null for exactly that reason.
    /// </remarks>
    [Fact]
    public void TheOutArrayIsNeverNull()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        Assert.NotNull(frames);
    }

    /// <summary>
    /// THE REVERSAL: the caller's own frame is the LAST element, and the outermost frame is the first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted from a known-depth chain built by three nested helpers, so the expected ordering is
    /// unambiguous without depending on the runner's depth. Reading from the END of the array, the
    /// frames must be the innermost helper, then its caller, then its caller - which is the reverse of
    /// what the framework's own capture reports.
    /// </para>
    /// <para>
    /// This is the single assertion that would catch a "simplification" to <c>trace.GetFrames()</c>
    /// without the reversal, and the consequence of missing it is a call stack printed upside down in
    /// every assertion payload.
    /// </para>
    /// </remarks>
    [Fact]
    public void FramesAreOrderedOutermostFirstSoTheCallerIsLast()
    {
        string[] frames = CaptureThroughOuter();

        Assert.True(frames.Length >= 4, "The three helpers plus this test must all be present.");

        // Reading from the innermost end backwards: the capture helper, then inner, then middle, then
        // outer. The provider skips its own frame, so the innermost frame belongs to the helper that
        // called it.
        Assert.Contains(nameof(CaptureHere), frames[^1], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughInner), frames[^2], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughMiddle), frames[^3], StringComparison.Ordinal);
        Assert.Contains(nameof(CaptureThroughOuter), frames[^4], StringComparison.Ordinal);
    }

    /// <summary>
    /// The provider skips its OWN frame, so <c>StackTrace</c> never appears in the frames it returns.
    /// </summary>
    /// <remarks>
    /// The capture is constructed with a skip count of one for this reason. Without it every payload
    /// would carry a leading frame naming the diagnostic helper rather than the code under diagnosis,
    /// and the trim arithmetic below - which is calibrated against the legacy's frame set - would be off
    /// by one everywhere.
    /// </remarks>
    [Fact]
    public void TheProviderSkipsItsOwnFrame()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        Assert.DoesNotContain(
            frames,
            frame => frame.Contains(
                $".{nameof(StackTraceProvider.StackTrace)}{LineNumberIntroducer}",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A deeper call site yields strictly more frames than a shallower one, by exactly the number of
    /// intervening calls.
    /// </summary>
    /// <remarks>
    /// The relative form of "the capture is real". An implementation that returned a constant, a
    /// truncated set, or a cached array would pass the shape assertions above and fail this one. The
    /// difference is asserted as an exact number rather than an inequality because the three helpers are
    /// non-inlined, so the count is deterministic.
    /// </remarks>
    [Fact]
    public void DepthIsReflectedExactlyInTheFrameCount()
    {
        string[] shallow = CaptureHere();
        string[] deeper = CaptureThroughOuter();

        Assert.Equal(shallow.Length + 3, deeper.Length);
    }

    /// <summary>
    /// Every frame is rendered as <c>&lt;scope&gt;.&lt;member&gt; line:&lt;number&gt;</c>, with a
    /// non-empty scope and member and a parsable integer line number.
    /// </summary>
    /// <remarks>
    /// The format is what the consumer prints, so it is pinned as a shape rather than as exact text -
    /// the line numbers are real and would change with every edit to this file. The line number is
    /// asserted to be a parsable non-negative integer rather than a specific value because a release
    /// build without a symbol file legitimately degrades it to zero, and the provider is documented to
    /// degrade rather than omit.
    /// </remarks>
    [Fact]
    public void EveryFrameIsRenderedInTheDocumentedShape()
    {
        StackTraceProvider.StackTrace(out string[] frames);

        Assert.NotEmpty(frames);

        foreach (string frame in frames)
        {
            int introducer = frame.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);
            Assert.True(introducer > 0, $"Frame '{frame}' carries no line-number introducer.");

            string qualifiedMember = frame[..introducer];
            string lineNumber = frame[(introducer + LineNumberIntroducer.Length)..];

            Assert.Contains(".", qualifiedMember, StringComparison.Ordinal);
            Assert.True(
                int.TryParse(lineNumber, out int parsed),
                $"Frame '{frame}' does not end in an integer line number.");
            Assert.True(parsed >= 0, $"Frame '{frame}' has a negative line number.");

            // Neither half may be empty: the provider substitutes placeholders rather than emitting
            // nothing when reflection cannot answer.
            int lastDot = qualifiedMember.LastIndexOf('.');
            Assert.NotEqual(0, lastDot);
            Assert.NotEqual(qualifiedMember.Length - 1, lastDot);
        }
    }

    /// <summary>
    /// A frame declared on a NESTED type carries the full enclosing chain plus the namespace.
    /// </summary>
    /// <remarks>
    /// The scope is built by walking <c>DeclaringType</c> outward, so a nested type renders as
    /// <c>Namespace.Outer.Inner</c> rather than as the bare name the framework's <c>Type.Name</c>
    /// gives. Asserted because the legacy scope is a fully qualified object name, and a bare name would
    /// make two same-named nested helpers in different outer types indistinguishable in a payload.
    /// </remarks>
    [Fact]
    public void ANestedTypeFrameCarriesItsEnclosingChainAndNamespace()
    {
        string[] frames = NestedCaptureHelper.Capture();

        string innermost = frames[^1];

        Assert.Contains(
            $"{typeof(StackTraceProviderTests).Namespace}."
                + $"{nameof(StackTraceProviderTests)}.{nameof(NestedCaptureHelper)}."
                + $"{nameof(NestedCaptureHelper.Capture)}",
            innermost,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A frame whose declaring type has NO namespace renders as the bare type chain, with no leading
    /// dot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement of the nested-type test, and the only branch of scope rendering a namespaced call
    /// site cannot reach. A naive implementation that concatenated the namespace unconditionally would
    /// emit <c>.GlobalScopeCaptureHelper.Capture</c> - a leading dot - which breaks the frame format's
    /// one-dot-separates-scope-from-member invariant and would make the scope portion parse as empty.
    /// </para>
    /// <para>
    /// The helper lives in <c>GlobalScopeCaptureHelper.cs</c> because a file-scoped namespace declaration
    /// must precede every type in its file, so a namespace-less type cannot share this one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANamespacelessTypeFrameRendersAsTheBareTypeChain()
    {
        string[] frames = GlobalScopeCaptureHelper.Capture();

        string innermost = frames[^1];

        Assert.StartsWith(
            $"{nameof(GlobalScopeCaptureHelper)}.{nameof(GlobalScopeCaptureHelper.Capture)}"
                + LineNumberIntroducer,
            innermost,
            StringComparison.Ordinal);

        Assert.False(
            innermost.StartsWith('.'),
            $"Frame '{innermost}' begins with a dot, so an absent namespace was concatenated anyway.");
    }

    /// <summary>
    /// A constructor frame renders with its leading dot stripped, so it reads <c>ctor</c> rather than
    /// <c>.ctor</c>.
    /// </summary>
    /// <remarks>
    /// The framework names constructors <c>.ctor</c> and static constructors <c>.cctor</c>. Left alone
    /// they would render as <c>Namespace.Type..ctor</c> - a double dot - which is neither the legacy
    /// spelling nor parsable by anything splitting on the last dot. The provider strips the leading dot
    /// for that reason, and this is the only test that reaches the branch.
    /// </remarks>
    [Fact]
    public void AConstructorFrameHasItsLeadingDotStripped()
    {
        ConstructorCaptureHelper helper = new();

        string innermost = helper.Frames[^1];

        Assert.Contains($".ctor{LineNumberIntroducer}", innermost, StringComparison.Ordinal);
        Assert.DoesNotContain($"..ctor{LineNumberIntroducer}", innermost, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  2. THE TWO-STAGE TRIM
    // ==============================================================================================

    /// <summary>
    /// The four overloads trim exactly as the stage arithmetic requires, measured as frame counts
    /// relative to a full capture taken from the same depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point of DECISION 4 in one test. Each rendered result is split on the frame separator
    /// and its frame count compared against the full capture taken from the same method, and the
    /// DIFFERENCE is what the arithmetic predicts. Using differences rather than absolutes is what makes
    /// this independent of the runner's depth, which is ~147 frames here and is not a stable number.
    /// </para>
    /// <para>
    /// The measured result is that a no-offset call reproduces the full capture EXACTLY - delta zero -
    /// because the second stage removes precisely the frames belonging to the provider itself. An offset
    /// of n then removes n more. That is the contract: "the stack as my caller sees it, minus n".
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourOverloadsTrimExactlyAsTheStageArithmeticRequires()
    {
        int fullCount = StackTraceProvider.StackTrace(out _);

        // No offset: the provider's own frames are removed and nothing else.
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo()));
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo(">> ")));
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)0)));
        Assert.Equal(fullCount, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)0, string.Empty)));

        // An offset of n removes exactly n more, on both overloads that take one.
        Assert.Equal(fullCount - 1, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)1)));
        Assert.Equal(fullCount - 2, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)2)));
        Assert.Equal(fullCount - 3, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)3)));

        Assert.Equal(fullCount - 1, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)1, string.Empty)));
        Assert.Equal(fullCount - 2, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)2, string.Empty)));
        Assert.Equal(fullCount - 3, FrameCountOf(StackTraceProvider.StackTraceInfo((ushort)3, string.Empty)));
    }

    /// <summary>
    /// A no-offset rendered trace has the CALLER's own frame as its innermost entry - no frame belonging
    /// to the provider leaks into it.
    /// </summary>
    /// <remarks>
    /// The qualitative statement behind the delta-zero arithmetic above, and the one that explains why
    /// the second stage exists at all. Both the parameterless overload and the two-argument one with a
    /// zero offset are checked, because they reach the worker by different routes - one through a
    /// delegating frame and one directly - and both must still end at the caller.
    /// </remarks>
    [Fact]
    public void ANoOffsetTraceEndsAtTheCallersOwnFrame()
    {
        string viaDelegate = StackTraceProvider.StackTraceInfo();
        string viaWorker = StackTraceProvider.StackTraceInfo((ushort)0, string.Empty);

        string innermostViaDelegate = viaDelegate.Split(FrameSeparator)[^1];
        string innermostViaWorker = viaWorker.Split(FrameSeparator)[^1];

        Assert.Contains(
            nameof(ANoOffsetTraceEndsAtTheCallersOwnFrame),
            innermostViaDelegate,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(ANoOffsetTraceEndsAtTheCallersOwnFrame),
            innermostViaWorker,
            StringComparison.Ordinal);

        // And no frame of the provider appears anywhere in either.
        foreach (string rendered in new[] { viaDelegate, viaWorker })
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
    /// The one-argument ushort overload and the two-argument one AGREE for every offset: the delegating
    /// overload's extra frame is cancelled exactly by the extra offset it adds.
    /// </summary>
    /// <param name="offset">The offset passed to both overloads.</param>
    /// <remarks>
    /// <para>
    /// This is the property that explains the second stage, and it is the one a maintainer is most
    /// likely to break. The two overloads reach the worker by DIFFERENT routes - one through its own
    /// delegating frame, one directly - so their captures differ in depth by one. The delegating route
    /// compensates by passing <c>offset + 1</c>, and the two effects cancel, leaving both overloads
    /// presenting the identical view.
    /// </para>
    /// <para>
    /// Dropping the <c>+ 1</c> as a redundant-looking adjustment, or allowing the delegating overload to
    /// be inlined, would break the cancellation in opposite directions - the first leaking a
    /// <c>StackTraceInfo</c> frame into the diagnosed stack, the second removing a real caller frame.
    /// Neither would be visible without this assertion.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)10)]
    public void TheOneAndTwoArgumentOverloadsAgreeForEveryOffset(ushort offset)
    {
        int viaOneArgument = FrameCountOf(StackTraceProvider.StackTraceInfo(offset));
        int viaTwoArguments = FrameCountOf(StackTraceProvider.StackTraceInfo(offset, string.Empty));

        Assert.Equal(viaTwoArguments, viaOneArgument);

        // Both are also the full capture minus the offset, so the agreement is at the right value
        // rather than merely mutual.
        int fullCount = StackTraceProvider.StackTrace(out _);
        Assert.Equal(fullCount - offset, viaTwoArguments);
    }

    /// <summary>
    /// The parameterless overload and the prefix-taking one trim identically, differing only in the
    /// prefix they emit.
    /// </summary>
    /// <remarks>
    /// Both delegate with offset 1, so their frame counts must match exactly while their text differs.
    /// This is the pair that IS interchangeable up to the prefix, and pinning it alongside the pair that
    /// is not makes the distinction legible.
    /// </remarks>
    [Fact]
    public void TheParameterlessAndPrefixOverloadsTrimIdentically()
    {
        string bare = StackTraceProvider.StackTraceInfo();
        string prefixed = StackTraceProvider.StackTraceInfo("[P]");

        Assert.Equal(FrameCountOf(bare), FrameCountOf(prefixed));
        Assert.NotEqual(bare, prefixed);
    }

    /// <summary>
    /// The prefix is emitted before EVERY frame, not once at the start.
    /// </summary>
    /// <remarks>
    /// <c>stacktraceinfo.srf:L34</c> tests the prefix inside the loop, so it repeats. The count of
    /// occurrences is therefore the frame count, and that is asserted rather than merely
    /// <c>StartsWith</c> - which a once-at-the-start implementation would also satisfy.
    /// </remarks>
    [Fact]
    public void ThePrefixIsEmittedBeforeEveryFrame()
    {
        const string Prefix = "<<P>>";

        string rendered = StackTraceProvider.StackTraceInfo(Prefix);

        int frameCount = FrameCountOf(rendered);
        int prefixCount = rendered.Split(Prefix).Length - 1;

        Assert.Equal(frameCount, prefixCount);
        Assert.StartsWith(Prefix, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty prefix emits nothing at all: no marker, no separator change, no leading whitespace.
    /// </summary>
    /// <remarks>
    /// The legacy test is <c>prefix &lt;&gt; ""</c>, so the empty string takes the no-op path. Pinned
    /// because an implementation that concatenated unconditionally would produce identical output here
    /// and would differ for a null prefix, which the next test covers.
    /// </remarks>
    [Fact]
    public void AnEmptyPrefixEmitsNothing()
    {
        string withEmpty = StackTraceProvider.StackTraceInfo((ushort)0, string.Empty);

        Assert.False(
            withEmpty.StartsWith(FrameSeparator, StringComparison.Ordinal),
            "An empty prefix must not introduce leading structure.");

        // Frames are still separated normally.
        Assert.Equal(FrameCountOf(withEmpty), withEmpty.Split(FrameSeparator).Length);
    }

    /// <summary>
    /// A NULL prefix behaves exactly as an empty one: it appends nothing and does not throw.
    /// </summary>
    /// <remarks>
    /// The legacy comparison against the empty string yields true for null and concatenating null
    /// appends nothing, which is also what PowerScript does when the test yields null. So the ported
    /// behaviour is "indistinguishable from empty", and it is asserted by comparing frame counts rather
    /// than whole strings - the two calls capture at the same depth, so the counts must agree.
    /// </remarks>
    [Fact]
    public void ANullPrefixBehavesExactlyAsAnEmptyOne()
    {
        string viaNull = StackTraceProvider.StackTraceInfo((ushort)0, null!);
        string viaEmpty = StackTraceProvider.StackTraceInfo((ushort)0, string.Empty);

        Assert.Equal(FrameCountOf(viaEmpty), FrameCountOf(viaNull));

        // A null prefix appends nothing, so no frame acquires a leading marker. Asserted as structure
        // rather than by searching for the text "null": this test method's own name contains that word
        // and appears in every captured frame.
        foreach (string frame in viaNull.Split(FrameSeparator))
        {
            Assert.False(
                frame.StartsWith(' '),
                $"Frame '{frame}' acquired a leading marker from a null prefix.");
            Assert.Contains(LineNumberIntroducer, frame, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Frames are joined with a BARE LINE FEED and there is no separator after the last one.
    /// </summary>
    /// <remarks>
    /// The separator must not be CRLF, because CRLF is the assert payload's FIELD delimiter - the
    /// rendered trace becomes one field of that payload, so using CRLF here would split one field into
    /// many and break the consumer's exactly-seven test. The no-trailing-separator half comes from the
    /// <c>nIndex &gt; 1</c> guard [stacktraceinfo.srf:L33].
    /// </remarks>
    [Fact]
    public void FramesAreJoinedWithABareLineFeedAndNoTrailingSeparator()
    {
        string rendered = StackTraceProvider.StackTraceInfo();

        Assert.DoesNotContain("\r", rendered, StringComparison.Ordinal);
        Assert.False(rendered.EndsWith(FrameSeparator, StringComparison.Ordinal));
        Assert.False(rendered.StartsWith(FrameSeparator, StringComparison.Ordinal));

        Assert.Equal(FrameCountOf(rendered), rendered.Split(FrameSeparator).Length);
    }

    /// <summary>
    /// The rendered trace keeps the OUTERMOST-first order the capture produced.
    /// </summary>
    /// <remarks>
    /// The trim removes frames from the INNERMOST end, so the outermost frames are the ones that
    /// survive - and they survive in order. Asserted because a trim implemented from the wrong end
    /// would produce the same frame COUNT as the correct one and the wrong frames entirely, passing
    /// every arithmetic test above.
    /// </remarks>
    [Fact]
    public void TheRenderedTraceKeepsOutermostFirstOrderAndTrimsFromTheInnermostEnd()
    {
        string rendered = RenderThroughOuter();
        string[] renderedFrames = rendered.Split(FrameSeparator);

        StackTraceProvider.StackTrace(out string[] captured);

        // The outermost frame is first in both.
        Assert.Equal(captured[0], renderedFrames[0]);

        // The helpers nearest the outside survive; the innermost ones were trimmed. RenderThroughOuter
        // sits above the trimmed region, so it must still be present.
        Assert.Contains(
            renderedFrames,
            frame => frame.Contains(nameof(RenderThroughOuter), StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  3. THE BAIL-OUTS
    // ==============================================================================================

    /// <summary>
    /// An offset large enough to consume the whole stack returns the EMPTY STRING rather than throwing
    /// or returning a partial result.
    /// </summary>
    /// <param name="offset">An offset at or beyond the captured depth.</param>
    /// <remarks>
    /// This is bail-out 2's first arm, and it is the live one. The values are chosen to be far beyond
    /// any plausible managed stack depth so the test cannot become flaky as the runner changes. Empty
    /// rather than a throw matters because the caller is a diagnostic path: a diagnostic helper that
    /// threw while describing a failure would replace the original fault with its own.
    /// </remarks>
    [Theory]
    [InlineData((ushort)1000)]
    [InlineData((ushort)10000)]
    [InlineData((ushort)65534)]
    public void AnOverLargeOffsetReturnsTheEmptyString(ushort offset)
    {
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(offset));
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(offset, "prefix"));
    }

    /// <summary>
    /// The maximum <see cref="ushort"/> offset WRAPS through the unchecked increment in the
    /// one-argument overload, so it behaves as offset zero rather than as an over-large offset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StackTraceInfo(ushort)</c> delegates with <c>unchecked((ushort)(offset + 1))</c>, so 65535
    /// becomes 0 and the call trims 1 rather than returning empty. The two-argument overload with the
    /// same value performs no increment and therefore DOES return empty.
    /// </para>
    /// <para>
    /// Pinned because the wrap is a consequence of the deliberate <c>unchecked</c> rather than an
    /// accident, and because the two overloads diverging at the boundary is exactly the kind of edge a
    /// reader would otherwise assume was untested. In the legacy the type is a 16-bit unsigned integer
    /// with the same wrap-around, so this is preserved behaviour, not a port artefact.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMaximumOffsetWrapsInTheOneArgumentOverloadButNotTheTwoArgumentOne()
    {
        string viaOneArgument = StackTraceProvider.StackTraceInfo(ushort.MaxValue);
        string viaTwoArguments = StackTraceProvider.StackTraceInfo(ushort.MaxValue, string.Empty);

        Assert.NotEqual(string.Empty, viaOneArgument);
        Assert.Equal(string.Empty, viaTwoArguments);

        // Wrapped to zero, so the worker subtracts only 1 - which is no longer enough to hide the
        // DELEGATING overload's own frame. The rendered trace therefore carries one frame MORE than a
        // normal call, and that extra frame is StackTraceInfo itself.
        int fullCount = StackTraceProvider.StackTrace(out _);
        Assert.Equal(fullCount + 1, FrameCountOf(viaOneArgument));

        Assert.Contains(
            $".{nameof(StackTraceProvider.StackTraceInfo)}{LineNumberIntroducer}",
            viaOneArgument,
            StringComparison.Ordinal);

        // A normal call leaks no such frame, so the leak is the wrap and not the provider.
        Assert.DoesNotContain(
            $".{nameof(StackTraceProvider.StackTraceInfo)}{LineNumberIntroducer}",
            StackTraceProvider.StackTraceInfo(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The largest offset that still yields output produces exactly one frame, and one more than that
    /// produces none - the boundary is exact, with no off-by-one.
    /// </summary>
    /// <remarks>
    /// The arithmetic boundary asserted directly rather than inferred from the theory rows. It is the
    /// clearest statement that <c>nCount &lt;= 0</c> is the guard - not <c>&lt; 0</c>, which would
    /// return an empty-but-present frame, and not <c>&lt;= 1</c>, which would drop the last real one.
    /// </remarks>
    [Fact]
    public void TheBoundaryBetweenOneFrameAndNoneIsExact()
    {
        int fullCount = StackTraceProvider.StackTrace(out _);

        // A no-offset call yields exactly fullCount frames, so an offset of fullCount - 1 leaves one and
        // an offset of fullCount leaves none.
        ushort lastYielding = (ushort)(fullCount - 1);
        ushort firstEmpty = (ushort)fullCount;

        string oneFrame = StackTraceProvider.StackTraceInfo(lastYielding, string.Empty);
        Assert.Equal(1, FrameCountOf(oneFrame));
        Assert.DoesNotContain(FrameSeparator, oneFrame, StringComparison.Ordinal);

        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(firstEmpty, string.Empty));

        // The one-argument overload reaches the same boundary at the same value, which is the
        // cancellation holding right up to the edge.
        Assert.Equal(1, FrameCountOf(StackTraceProvider.StackTraceInfo(lastYielding)));
        Assert.Equal(string.Empty, StackTraceProvider.StackTraceInfo(firstEmpty));
    }

    /// <summary>
    /// No overload throws, for any offset across the whole <see cref="ushort"/> domain sampled, with any
    /// prefix.
    /// </summary>
    /// <remarks>
    /// The provider is reached from a failure path, so throwing is the one thing it must never do -
    /// including from the catch-all, which resets the accumulator instead of rethrowing. The sweep
    /// samples the domain rather than exhausting it because 65536 captures of a live stack is
    /// disproportionate for a property whose branches are all reached by the rows here.
    /// </remarks>
    [Fact]
    public void NoOverloadThrowsForAnyOffsetOrPrefix()
    {
        ushort[] offsets =
            [0, 1, 2, 3, 5, 8, 13, 100, 1000, 30000, ushort.MaxValue - 1, ushort.MaxValue];
        string?[] prefixes = [null, string.Empty, " ", ">> ", "多字节前缀"];

        foreach (ushort offset in offsets)
        {
            Assert.NotNull(StackTraceProvider.StackTraceInfo(offset));

            foreach (string? prefix in prefixes)
            {
                Assert.NotNull(StackTraceProvider.StackTraceInfo(offset, prefix!));
            }
        }

        Assert.NotNull(StackTraceProvider.StackTraceInfo());

        foreach (string? prefix in prefixes)
        {
            Assert.NotNull(StackTraceProvider.StackTraceInfo(prefix!));
        }
    }

    // ==============================================================================================
    //  4. THE ONE-BASED FRIEND SEAMS  (AAP 0.4.5.4)
    // ==============================================================================================

    /// <summary>
    /// <c>UpperBound</c> returns PowerBuilder's LAST VALID INDEX for a one-based array, which for a
    /// zero-based backing array of length n is n.
    /// </summary>
    /// <param name="length">The array length to probe.</param>
    /// <remarks>
    /// The whole reason this seam exists. PowerScript's upper-bound function returns the last valid
    /// index, and its arrays are one-based - so for n elements it answers n, whereas a C# reader
    /// expects <c>Length - 1</c> to be the last index. Centralizing the convention in one internal
    /// method is what AAP 0.4.5.4 asks for, and testing it directly is what makes the convention
    /// auditable instead of re-derived at each call site.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    public void UpperBoundReturnsTheLastValidOneBasedIndex(int length)
    {
        string[] frames = Enumerable.Range(0, length).Select(index => $"frame{index}").ToArray();

        Assert.Equal(length, StackTraceProvider.UpperBound(frames));
    }

    /// <summary>
    /// <c>FrameAt</c> is one-based: index 1 is the first element and index <c>UpperBound</c> is the
    /// last.
    /// </summary>
    /// <remarks>
    /// The complement of the bound seam, and the pair is what makes a ported one-based loop
    /// <c>for (i = 1; i &lt;= UpperBound(a); i++)</c> correct without any adjustment at the call site.
    /// Both ends are asserted, because an implementation that subtracted at the wrong end would still
    /// satisfy a single-element probe.
    /// </remarks>
    [Fact]
    public void FrameAtIsOneBasedAtBothEnds()
    {
        string[] frames = ["first", "second", "third"];

        Assert.Equal("first", StackTraceProvider.FrameAt(frames, 1));
        Assert.Equal("second", StackTraceProvider.FrameAt(frames, 2));
        Assert.Equal("third", StackTraceProvider.FrameAt(frames, 3));
        Assert.Equal("third", StackTraceProvider.FrameAt(frames, StackTraceProvider.UpperBound(frames)));
    }

    /// <summary>
    /// A one-based loop bounded by <c>UpperBound</c> visits every element exactly once, in order.
    /// </summary>
    /// <remarks>
    /// The seams asserted as the idiom they exist to support, rather than as two isolated functions.
    /// This is the loop shape every ported PowerScript iteration takes, so proving it is exhaustive and
    /// ordered is proving the translation of all of them.
    /// </remarks>
    [Fact]
    public void AOneBasedLoopBoundedByUpperBoundVisitsEveryElementInOrder()
    {
        string[] frames = ["a", "b", "c", "d"];

        System.Collections.Generic.List<string> visited = [];

        for (int index = 1; index <= StackTraceProvider.UpperBound(frames); index++)
        {
            visited.Add(StackTraceProvider.FrameAt(frames, index));
        }

        Assert.Equal(frames, visited);
    }

    /// <summary>
    /// A zero index or an index past the bound is out of range - the seam does not silently clamp.
    /// </summary>
    /// <remarks>
    /// Index 0 is the trap: it is the valid first index in C# and an INVALID index in PowerScript, so a
    /// seam that accepted it would let a zero-based loop compile and run while reading one element early
    /// and missing the last. Throwing is correct here because a bad index is a translation error in the
    /// caller, not a runtime condition to be tolerated - unlike everything on the rendering path above.
    /// </remarks>
    [Fact]
    public void AZeroOrPastTheEndIndexIsOutOfRange()
    {
        string[] frames = ["only"];

        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, 0));
        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, 2));
        Assert.Throws<IndexOutOfRangeException>(() => StackTraceProvider.FrameAt(frames, -1));
    }

    // ==============================================================================================
    //  5. SHAPE
    // ==============================================================================================

    /// <summary>
    /// The provider is a static class exposing exactly the capture method and the four rendering
    /// overloads.
    /// </summary>
    /// <remarks>
    /// The overload count is the machine-checkable form of "four overloads with distinct trim
    /// arithmetic": a fifth would need its own stage arithmetic and its own row in the table above, and
    /// a missing one would mean a legacy call site has no port. The type being static mirrors the
    /// legacy global functions, which have no instance to hold.
    /// </remarks>
    [Fact]
    public void TheProviderIsStaticWithExactlyFiveMethods()
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
    /// Every method that participates in the trim arithmetic is marked
    /// <see cref="MethodImplOptions.NoInlining"/>, so the frame counts cannot change under
    /// optimization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a correctness requirement rather than a performance note. The trim is expressed in
    /// FRAMES, so if the runtime inlined <c>StackTraceInfo</c> into its caller the frame it was
    /// calibrated to remove would not exist and the result would silently include one frame too many -
    /// in a release build only, which is the worst possible place for the difference to appear.
    /// </para>
    /// <para>
    /// Asserted by reflection because the symptom is invisible in a debug build, so no behavioural test
    /// can catch its absence in the configuration developers usually run.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameSensitiveMethodForbidsInlining()
    {
        MethodInfo[] frameSensitive = typeof(StackTraceProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is nameof(StackTraceProvider.StackTrace)
                or nameof(StackTraceProvider.StackTraceInfo))
            .ToArray();

        Assert.NotEmpty(frameSensitive);

        foreach (MethodInfo method in frameSensitive)
        {
            Assert.True(
                method.MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining),
                $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}) "
                    + "participates in frame-count arithmetic and must forbid inlining, or its trim "
                    + "silently changes in a release build.");
        }
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Counts the frames in a rendered trace, treating the empty string as zero frames.
    /// </summary>
    /// <param name="rendered">A rendered trace.</param>
    /// <returns>The number of frames it carries.</returns>
    /// <remarks>
    /// Needed because <c>"".Split("\n")</c> yields one empty element rather than none, so a naive split
    /// would report the bail-out result as one frame and make every arithmetic assertion off by one at
    /// the boundary.
    /// </remarks>
    private static int FrameCountOf(string rendered)
    {
        return rendered.Length == 0 ? 0 : rendered.Split(FrameSeparator).Length;
    }

    /// <summary>Captures at this depth.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureHere()
    {
        StackTraceProvider.StackTrace(out string[] frames);
        return frames;
    }

    /// <summary>Adds one frame above <see cref="CaptureHere"/>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughInner() => CaptureHere();

    /// <summary>Adds a second frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughMiddle() => CaptureThroughInner();

    /// <summary>Adds a third frame, giving a known four-deep chain below the test method.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] CaptureThroughOuter() => CaptureThroughMiddle();

    /// <summary>Renders a trace from three frames above the test method.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RenderThroughOuter() => RenderThroughMiddle();

    /// <summary>Intermediate frame for <see cref="RenderThroughOuter"/>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RenderThroughMiddle() => RenderThroughInner();

    /// <summary>Innermost render helper.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string RenderThroughInner() => StackTraceProvider.StackTraceInfo();

    /// <summary>
    /// A nested type, so the enclosing-chain branch of scope rendering is reachable.
    /// </summary>
    private static class NestedCaptureHelper
    {
        /// <summary>Captures from inside a nested type.</summary>
        /// <returns>The captured frames.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string[] Capture()
        {
            StackTraceProvider.StackTrace(out string[] frames);
            return frames;
        }
    }

    /// <summary>
    /// Captures from inside a constructor, so the leading-dot-stripping branch is reachable.
    /// </summary>
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

        /// <summary>Gets the frames captured during construction.</summary>
        internal string[] Frames { get; }
    }
}
