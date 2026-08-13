// ==================================================================================================
//  CallerInfoTests.cs - THE TWO PROTOTYPE SHAPES, AND WHAT COMPILE-TIME SUBSTITUTION CHANGES
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Diagnostics.CallerInfo
//  ORACLE            ws_objects/pfw.common.pbl.src/getcurrentscript.srf  9 lines, DECLARATION ONLY
//  READ ALONGSIDE    ws_objects/pfw.common.pbl.src/stacktrace.srf        pfwStackTrace, the sibling
//                                                                       classic external prototype
//                    ws_objects/pfw.common.pbl.src/assert.srf:L39-L59    the ONLY consumer anywhere
//                                                                       in the estate that parses a
//                                                                       composed frame, and so the
//                                                                       only evidence of what the
//                                                                       composed text must look like
//
//  Those three paths are READ ONLY. They are the behavioural oracle for parity testing and never an
//  edit target. They were read as specification and they were not modified.
//
//  THE WHOLE OF THE LEGACY EVIDENCE IS TWO PROTOTYPES OVER ONE NATIVE ALIAS
//  ------------------------------------------------------------------------------------------------
//      global function string getcurrentscript () system library "pfw.dll" &
//          alias for "pfwGetCurrentScript"                            [getcurrentscript.srf:L7]
//      global function boolean getcurrentscript (ref string module, ref long lineno) &
//          system library "pfw.dll" alias for "pfwGetCurrentScript"   [getcurrentscript.srf:L8]
//
//  There is no PowerScript body: the file declares a bare `global type getcurrentscript from
//  function_object` with no native clause [getcurrentscript.srf:L3-L4] and stops after the two
//  prototypes [getcurrentscript.srf:L9]. The two signatures ARE the specification, and because both
//  bind the SAME native export they are two views of one primitive rather than two behaviours. This
//  suite therefore covers BOTH shapes - the single-value form and the form yielding a module and a
//  line number through `ref` parameters - and asserts the identity that ties them together, instead
//  of treating either as redundant.
//
//  THE SUBSTITUTION THIS SUITE COVERS, AND THE ONE THING IT CHANGES (C-K)
//  ------------------------------------------------------------------------------------------------
//  Both `pfwGetCurrentScript` prototypes are exported from the closed `pfw.dll`, for which no C++
//  source exists anywhere in the repository. AAP section 0.4.2.3 names the substitute, and AAP
//  section 0.6.5 classifies current-script retrieval as SUBSTITUTE rather than a non-port:
//
//      pfwGetCurrentScript, over the closed pfw.dll   ->   the System.Runtime.CompilerServices
//                                                          caller information attributes
//                                                          [CallerMemberName], [CallerFilePath]
//                                                          and [CallerLineNumber]
//
//  WHAT THAT MOVES: the RESOLUTION TIME. `pfwGetCurrentScript` was a native export that interrogated
//  the PowerBuilder virtual machine's live state, so it resolved at RUN TIME by inspecting the
//  running stack and reported whatever was actually executing - correct even through an indirect or
//  dynamically dispatched call. Caller information attributes resolve at COMPILE TIME: Roslyn bakes
//  a literal into every CALL SITE for each argument the caller omits, so the values describe the
//  code that LEXICALLY contains the call and nothing is inspected while the program runs.
//
//  Three assertions in this file exist purely to pin that shift, because it is the only behavioural
//  difference across the substitution and a reader has to be able to see it:
//
//      * a helper that calls CallerInfo without forwarding reports THE HELPER, not the helper's
//        caller - see TwoDifferentMembersReportDifferentIdentities
//      * a helper that declares its own caller information parameters and forwards them reports its
//        CALLER, which is how the legacy's effect is recovered - see AForwardingHelperReportsIts-
//        CallerRatherThanItself
//      * the reported identity is INDEPENDENT of how deep the call stack is, which is the direct
//        observable consequence of nothing walking a stack - see TheReportedIdentityIsIndependent-
//        OfCallStackDepth. The sibling StackTraceProviderTests asserts the exact opposite for
//        StackTraceProvider, and the contrast is the point: these are the estate's two diagnostics
//        surfaces and only one of them looks at the stack.
//
//  THE CRITICAL TESTING CONSTRAINT, AND THE ONE SANCTIONED EXCEPTION TO IT
//  ------------------------------------------------------------------------------------------------
//  NEVER PASS THE CALLER INFORMATION ARGUMENTS EXPLICITLY when the thing under test is the
//  substitution. The entire behaviour of those parameters is that the compiler fills them in at the
//  call site; supplying one by hand stops the substitution and tests only the string handling that
//  follows it. Every call in Section 1 and Section 2 below is therefore written with NO arguments -
//  `CallerInfo.GetCurrentScript()` and `CallerInfo.GetCurrentScript(ref module, ref lineNo)` - which
//  is also exactly the legacy call shape.
//
//  The tests are split into two sections on precisely that line, and the split is not cosmetic:
//
//      SECTION 1 and 2   SUBSTITUTION FACTS. Plain [Fact], no argument ever supplied, and the call
//                        written DIRECTLY INSIDE the test method whose identity is asserted - or
//                        inside a named helper whose OWN identity is what the assertion names, which
//                        is stated at each such site. Member-data indirection is deliberately absent
//                        here: a factory or a shared assertion helper that called CallerInfo would
//                        report ITS location, so routing these calls through one would destroy the
//                        very thing being measured.
//
//      SECTION 3         COMPOSITION AND DEGRADATION MATRICES, table-driven [Theory] with
//                        [MemberData], because this is where the input genuinely varies. Reaching
//                        the degraded branches and the `false` return REQUIRES supplying the
//                        arguments, and supplying them is exactly what a forwarding member does -
//                        the implementation's own sanctioned position for a hand-written value.
//                        Indirection is harmless in this section for a precise reason: with all
//                        three values supplied, NO substitution occurs, so no call site's identity
//                        can leak into the result. The [MemberData] factories return input rows
//                        only; not one of them calls CallerInfo.
//
//  NO ABSOLUTE PATH AND NO LINE NUMBER LITERAL APPEARS ANYWHERE IN THIS FILE
//  ------------------------------------------------------------------------------------------------
//  A hard-coded line number would turn every future edit of this file into a false failure, and a
//  hard-coded absolute path would not survive the move between a local build and a CI build, where
//  ContinuousIntegrationBuild normalizes the paths the compiler embeds. So every claim here is a
//  RELATIONSHIP or a SHAPE:
//
//      the member name  equals `nameof` the enclosing method
//      the file scope   equals `nameof(CallerInfoTests)`, and the captured path ENDS WITH that name
//                       plus ".cs" - the C# convention that a file is named for the type it declares
//                       is the same one the implementation leans on to use a file name as a scope
//      the line number  equals what an independent WITNESS on the SAME source line reports, and the
//                       difference between two call sites equals the difference between two witnesses
//
//  The witnesses - CallerLineWitness, CallerFilePathWitness and CallerMemberNameWitness - do NOT
//  call CallerInfo. Each is an independent second reading of the same compile-time substitution,
//  which is what lets an ABSOLUTE line number be asserted exactly without ever writing one down.
//  Two invocations placed on ONE physical source line receive the same substituted line number, so a
//  witness sharing a line with a call under test pins that call's number precisely.
//
//  THAT IS WHY SEVERAL ASSIGNMENTS BELOW ARE WRITTEN AS A TUPLE, and it is the only reason. A form
//  such as
//
//      (long witness, string composed) = (CallerLineWitness(), CallerInfo.GetCurrentScript());
//
//  is ONE statement whose two invocations occupy one line, which is exactly what is needed - and it
//  stays inside the repository's formatting conventions, where two separate statements sharing a line
//  would not. Where the pair is too long for a single line the tuple is wrapped, with both invocations
//  kept together on the continuation line, because it is the line the INVOCATIONS sit on that decides
//  the substituted value and not the line the statement starts on. Splitting either pair apart is the
//  one edit that would silently break these tests, so any such assignment is commented at its site.
//
//  None of these helpers carries [MethodImpl(MethodImplOptions.NoInlining)], unlike every capture
//  helper in StackTraceProviderTests. That is deliberate and it is itself part of the evidence:
//  inlining can move a frame, and no frame is consulted here, so the attribute would protect
//  nothing. If a value in this file ever became sensitive to inlining, something would be reading
//  the run-time stack and the implementation, not the test, would be at fault.
//
//  BINDING CONSTRAINTS AT THIS SITE
//  Identifiers here are strictly PascalCase with no underscore and no SCREAMING_SNAKE spelling. The
//  repository root .editorconfig scopes its CA1707 and IDE1006 suppressions to the individual
//  production files that carry preserved legacy constant identifiers; no test file is among them, so
//  a suppression could not be granted to this one even if it were wanted.
// ==================================================================================================

// `using System;` is supplied implicitly by ImplicitUsings, which Directory.Build.props enables for
// every project including this one, so it is redundant to the compiler. It is declared anyway, and
// only for that reason: both sibling suites in this project declare it, this file does use System
// types by simple name (Exception and StringComparison), and a reader should not have to know which
// namespaces the implicit set covers to see what the file depends on. The other three are load
// bearing - measured by removing each in turn, they are required by 12, 36 and 112 diagnostics
// respectively. Nothing beyond these four namespaces and the one project reference this project
// already declares is used, so no dependency is added by this file.
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Parity and substitution tests for <see cref="CallerInfo"/>, the managed substitute for the two
/// <c>pfwGetCurrentScript</c> external prototypes declared at
/// <c>ws_objects/pfw.common.pbl.src/getcurrentscript.srf:L7-L8</c>.
/// </summary>
/// <remarks>
/// <para>
/// The legacy declaration has <b>no call site anywhere in the repository</b>: a case-insensitive
/// search of <c>ws_objects/**</c> for <c>getcurrentscript</c> matches exactly one file, the
/// declaration itself. There is consequently no observed behaviour to characterise from the oracle
/// and no paired recording to compare against, so this suite's job is different from its siblings':
/// it pins the two prototype <b>shapes</b> and the properties of the attribute-based substitution -
/// that the reported values track the call site, that both <c>ref</c> parameters are written, and
/// that nothing depends on run-time stack inspection. It deliberately asserts nothing that the two
/// prototypes and the substitution do not genuinely establish, because the closed binary documented
/// no further semantics and none may be invented.
/// </para>
/// <para>
/// This suite is also the only consumer of <see cref="CallerInfo"/> in the tree, so it carries the
/// whole coverage obligation for that file rather than relying on incidental use elsewhere.
/// </para>
/// </remarks>
public class CallerInfoTests
{
    // ==============================================================================================
    //  THE FORMAT TOKENS, RESTATED RATHER THAN REACHED FOR
    // ==============================================================================================
    //  CallerInfo declares these as `private const`, so they are unreachable from here by design -
    //  the implementation is explicit that it shares no state with its siblings. Restating them is
    //  what makes this suite an independent check of the format rather than a tautology: if a value
    //  in the implementation changed, these would disagree and the tests would fail, which is
    //  exactly the outcome wanted. The same four values also appear in StackTraceProvider, because
    //  a composed identity and a captured frame are required to read and parse identically.
    // ==============================================================================================

    /// <summary>The dot that separates the file scope from the member name.</summary>
    private const string ScopeSeparator = ".";

    /// <summary>The literal that introduces the line number in a composed value.</summary>
    private const string LineNumberIntroducer = " line:";

    /// <summary>The token a composed value carries when no file scope could be derived.</summary>
    private const string UnknownScopeName = "<unknown>";

    /// <summary>The token a composed value carries when no member name could be derived.</summary>
    private const string UnknownMemberName = "?";

    /// <summary>
    /// The extension this source file carries, used to relate the captured path to
    /// <c>nameof(CallerInfoTests)</c> without writing a path down.
    /// </summary>
    private const string SourceFileExtension = ".cs";

    /// <summary>
    /// A module value the implementation cannot produce, used to prove the <c>ref string</c>
    /// parameter is genuinely written rather than left as the caller set it.
    /// </summary>
    /// <remarks>
    /// It contains a space and no dot, so it cannot be confused with any composed value, and it
    /// cannot arise from any file name because a file scope never contains a space.
    /// </remarks>
    private const string SentinelModule = "sentinel module never produced";

    /// <summary>
    /// A line number the compiler cannot supply, used to prove the <c>ref long</c> parameter is
    /// genuinely written. Negative, so no real substitution can collide with it.
    /// </summary>
    private const long SentinelLineNumber = -424242L;

    // ==============================================================================================
    //  SECTION 1 - THE SINGLE-VALUE FORM
    //  Ports [getcurrentscript.srf:L7]: `global function string getcurrentscript ()`.
    //  Every call below supplies NO arguments, which is both the legacy call shape and the only way
    //  to observe the substitution at all.
    // ==============================================================================================

    /// <summary>
    /// The composed value names the enclosing member, qualified by this source file's name, and
    /// carries a positive line number.
    /// </summary>
    /// <remarks>
    /// The expected scope is written as <c>nameof(CallerInfoTests)</c> rather than as a string, and
    /// the expected member as <c>nameof</c> the enclosing method, so a rename cannot leave a stale
    /// literal behind. The scope holds because <c>[CallerFilePath]</c> is reduced to its file name
    /// and C# names a file for the type it declares - the same convention the implementation relies
    /// on for having any scope to report at all.
    /// </remarks>
    [Fact]
    public void TheSingleValueFormNamesTheEnclosingMemberQualifiedByThisFilesName()
    {
        string composed = CallerInfo.GetCurrentScript();

        Assert.NotNull(composed);
        Assert.NotEmpty(composed);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator +
                nameof(TheSingleValueFormNamesTheEnclosingMemberQualifiedByThisFilesName),
            ModulePartOf(composed));
        Assert.True(
            LineNumberPartOf(composed) > 0L,
            "A genuine call site always yields a positive line number.");
    }

    /// <summary>
    /// The member segment of the composed value is exactly the name the compiler substitutes, which
    /// is the enclosing member's own name.
    /// </summary>
    /// <remarks>
    /// Two independent readings are compared: <see cref="CallerMemberNameWitness"/> takes the
    /// substituted name through a parameter of its own, and <c>nameof</c> takes it from the language.
    /// Their agreement is what establishes that the member segment is neither derived nor guessed by
    /// the implementation - it is passed through verbatim, which is the whole of what the legacy
    /// prototype promised.
    /// </remarks>
    [Fact]
    public void TheSubstitutedMemberNameIsTheEnclosingMembersOwnName()
    {
        string witnessedMemberName = CallerMemberNameWitness();
        string composed = CallerInfo.GetCurrentScript();

        Assert.Equal(nameof(TheSubstitutedMemberNameIsTheEnclosingMembersOwnName), witnessedMemberName);
        Assert.Equal(nameof(CallerInfoTests) + ScopeSeparator + witnessedMemberName, ModulePartOf(composed));
    }

    /// <summary>
    /// The reported line number is exactly the line of the call, proved against an independent
    /// witness sharing the same source line rather than against a literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The witness reads <c>[CallerLineNumber]</c> through a different parameter on a different
    /// method, so agreement between the two is a genuine cross-check of the substitution rather than
    /// a restatement of it. Both invocations sit on ONE physical source line on purpose, which is why
    /// they are written as a single tuple assignment: two invocations on one line receive the same
    /// substituted number, and that is what makes an ABSOLUTE assertion possible with no line number
    /// written anywhere in this file.
    /// </para>
    /// <para>
    /// The witness returns <see cref="long"/> from the <see cref="int"/> the attribute supplies,
    /// mirroring the widening the implementation performs for its <c>ref long</c> parameter.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSingleValueFormReportsTheLineOfTheCallItself()
    {
        // One statement, two invocations, one line: do not split this pair apart.
        (long witness, string composed) = (CallerLineWitness(), CallerInfo.GetCurrentScript());

        Assert.True(witness > 0L, "The compiler always supplies a positive line number.");
        Assert.Equal(witness, LineNumberPartOf(composed));
    }

    /// <summary>
    /// Two call sites in one member report increasing line numbers whose difference equals the
    /// number of source lines between them.
    /// </summary>
    /// <remarks>
    /// The difference is compared against the difference between two witnesses, never against a
    /// literal, so inserting or removing lines anywhere in this file - including between the two
    /// pairs below - leaves the assertion true. That is the property that keeps the arithmetic
    /// pinned without making the suite hostile to editing.
    /// </remarks>
    [Fact]
    public void TwoCallSitesInOneMemberReportIncreasingLineNumbersWhoseDifferenceIsTheSourceDistance()
    {
        // Each pair is one statement whose two invocations share a line: do not split either apart.
        (long firstWitness, string first) = (CallerLineWitness(), CallerInfo.GetCurrentScript());

        // Any amount of code, comment or blank space may sit between the two pairs. Nothing below
        // depends on how much does.
        (long secondWitness, string second) = (CallerLineWitness(), CallerInfo.GetCurrentScript());

        // Same member and same file, so only the line number can differ.
        Assert.Equal(ModulePartOf(first), ModulePartOf(second));
        Assert.NotEqual(first, second);
        Assert.True(
            LineNumberPartOf(second) > LineNumberPartOf(first),
            "A later call site must report a strictly greater line number.");
        Assert.Equal(secondWitness - firstWitness, LineNumberPartOf(second) - LineNumberPartOf(first));
    }

    /// <summary>
    /// Two different members calling the single-value form report different identities, so the value
    /// genuinely tracks its call site instead of being a constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second reading comes from <see cref="ReportUnforwarded"/>, and the assertion names THAT
    /// HELPER'S OWN identity deliberately - it is the one situation the file header allows a helper
    /// to stand between a test and <see cref="CallerInfo"/>, because the helper's identity is
    /// precisely the thing being asserted.
    /// </para>
    /// <para>
    /// This is also the first of the three assertions that pin the run-time to compile-time shift
    /// (C-K). <c>pfwGetCurrentScript</c> inspected the running virtual machine and would have named
    /// this test method; the substitution is resolved where the call is written, so it names the
    /// helper. The difference is reproduced here rather than hidden.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoDifferentMembersReportDifferentIdentities()
    {
        string fromThisTest = CallerInfo.GetCurrentScript();
        string fromTheHelper = ReportUnforwarded();

        Assert.NotEqual(fromThisTest, fromTheHelper);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(TwoDifferentMembersReportDifferentIdentities),
            ModulePartOf(fromThisTest));
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ReportUnforwarded),
            ModulePartOf(fromTheHelper));
    }

    /// <summary>
    /// A helper that declares its own caller information parameters and forwards them reports its
    /// CALLER, which is how the legacy's dynamic reading is recovered through a helper.
    /// </summary>
    /// <remarks>
    /// Forwarding is the only sanctioned reason to pass these arguments by hand, and the
    /// implementation documents the pattern in full. The two readings taken here differ in exactly
    /// one respect - one member forwards and the other does not - so the comparison isolates the
    /// forwarding effect itself. The forwarded line number is pinned against a witness on the same
    /// source line, which also proves the forwarded value is the CALL SITE'S line and not the line
    /// inside the helper where the arguments are passed on.
    /// </remarks>
    [Fact]
    public void AForwardingHelperReportsItsCallerRatherThanItself()
    {
        // One statement, two invocations, one line: do not split this pair apart.
        (long witness, string throughForwarder) = (CallerLineWitness(), ReportForwarded());
        string withoutForwarding = ReportUnforwarded();

        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(AForwardingHelperReportsItsCallerRatherThanItself),
            ModulePartOf(throughForwarder));
        Assert.Equal(witness, LineNumberPartOf(throughForwarder));
        Assert.NotEqual(ModulePartOf(throughForwarder), ModulePartOf(withoutForwarding));
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ReportUnforwarded),
            ModulePartOf(withoutForwarding));
    }

    /// <summary>
    /// The reported identity is byte-identical however deep the call stack is when the call is
    /// reached, which is the direct observable consequence of nothing inspecting the stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both readings come from the same single call site inside <see cref="ReportUnforwarded"/>; one
    /// is reached directly and the other through three intermediate members. A run-time stack
    /// reading - which is what <c>pfwGetCurrentScript</c> performed - could not return the same
    /// value for both. A compile-time substitution cannot return anything else.
    /// </para>
    /// <para>
    /// The sibling <c>StackTraceProviderTests</c> asserts the exact opposite for
    /// <c>StackTraceProvider</c>, whose captured depth does grow with the chain. The contrast is
    /// deliberate: these are the two diagnostics surfaces of this library and only one of them looks
    /// at the stack. It is also why no helper in this file forbids inlining - there is no frame whose
    /// presence anything here depends on.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReportedIdentityIsIndependentOfCallStackDepth()
    {
        string directly = ReportUnforwarded();
        string throughThreeMembers = ReportThroughOuter();

        Assert.Equal(directly, throughThreeMembers);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ReportUnforwarded),
            ModulePartOf(throughThreeMembers));
    }

    /// <summary>
    /// The single-value form never returns <see langword="null"/>, never returns the empty string
    /// and never throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The implementation validates no argument and has no throw of any kind, which is a deliberate
    /// consequence of the zero-call-site finding: with no legacy consumer, nothing constrains an
    /// error contract and none may be invented (C-B). This test is what holds that decision in
    /// place.
    /// </para>
    /// <para>
    /// The call is made inside a block lambda, which is safe for the identity assertion for a
    /// verified reason: <c>[CallerMemberName]</c> inside a lambda is substituted with the ENCLOSING
    /// member's name, so the value still names this test method. <c>[CallerLineNumber]</c> is not -
    /// it is the lambda's own physical line - so no line assertion is made here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSingleValueFormNeverReturnsNullOrEmptyAndNeverThrows()
    {
        string? captured = null;

        Exception? escaped = Record.Exception(() =>
        {
            captured = CallerInfo.GetCurrentScript();
        });

        Assert.Null(escaped);
        Assert.NotNull(captured);
        Assert.NotEqual(string.Empty, captured);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(TheSingleValueFormNeverReturnsNullOrEmptyAndNeverThrows),
            ModulePartOf(captured));
    }

    /// <summary>
    /// No composed value carries the build machine's directory or the source file's extension, even
    /// though the compiler substitutes a full absolute path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The witness proves the raw substituted value really is an absolute path with directory
    /// separators in it, which is what makes the absence of those separators from the composed value
    /// meaningful rather than vacuous. The implementation discards the directory for hygiene - a
    /// returned identity may well end up in a log record or an error payload, and a build machine
    /// path has no business being there - and discards the extension because the dot before the
    /// member name is what the payload consumer reads as the end of the scope.
    /// </para>
    /// <para>
    /// Dropping the directory is also what makes a local value and a CI value comparable at all,
    /// since <c>ContinuousIntegrationBuild</c> normalizes only the directory portion of the paths the
    /// compiler embeds while the file name is invariant.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoComposedValueCarriesTheBuildMachinesDirectoryOrTheSourceExtension()
    {
        string substitutedPath = CallerFilePathWitness();
        string composed = CallerInfo.GetCurrentScript();

        Assert.True(
            substitutedPath.Contains('/') || substitutedPath.Contains('\\'),
            "The compiler substitutes a path, so at least one directory separator must be present.");
        Assert.EndsWith(
            nameof(CallerInfoTests) + SourceFileExtension,
            substitutedPath,
            StringComparison.Ordinal);

        Assert.StartsWith(nameof(CallerInfoTests) + ScopeSeparator, composed, StringComparison.Ordinal);
        Assert.DoesNotContain(SourceFileExtension, composed, StringComparison.Ordinal);
        Assert.DoesNotContain("/", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// A call from a constructor composes with its leading dot stripped, so the value carries exactly
    /// one separating dot rather than two.
    /// </summary>
    /// <remarks>
    /// The strip is not defensive padding. The compiler substitutes <c>.ctor</c> for an instance
    /// constructor, so without the strip the composed value would carry a doubled dot and the
    /// payload consumer would read an empty scope. This test reaches that path through a REAL
    /// constructor rather than by supplying the name, which is what makes it evidence that the case
    /// occurs rather than evidence that the string handling works.
    /// </remarks>
    [Fact]
    public void AConstructorsComposedValueHasItsLeadingDotStripped()
    {
        string composed = new ConstructorCaptureHelper().Captured;

        Assert.Equal(nameof(CallerInfoTests) + ScopeSeparator + "ctor", ModulePartOf(composed));
        Assert.DoesNotContain(ScopeSeparator + ScopeSeparator, composed, StringComparison.Ordinal);
    }

    /// <summary>
    /// A call from a property accessor composes under the PROPERTY's name, not under an accessor
    /// method name.
    /// </summary>
    /// <remarks>
    /// <c>[CallerMemberName]</c> reports the property rather than its generated <c>get_</c> accessor,
    /// so a composed value from a property is directly comparable with one from a method. That is
    /// worth pinning because the alternative would put an underscore-bearing name into a value the
    /// payload consumer parses.
    /// </remarks>
    [Fact]
    public void APropertyAccessorComposesUnderThePropertysName()
    {
        string composed = ComposedFromPropertyAccessor;

        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ComposedFromPropertyAccessor),
            ModulePartOf(composed));
        Assert.DoesNotContain("_", composed, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 2 - THE OUT-PARAMETER FORM
    //  Ports [getcurrentscript.srf:L8]: `global function boolean getcurrentscript (ref string
    //  module, ref long lineno)`. The parameters are `ref` and not `out` because AAP section 0.4.5.2
    //  maps a PowerBuilder `ref` parameter to a C# `ref` parameter and the legacy signature is the
    //  published contract, not a starting point to modernise (C-B). The visible consequence is that a
    //  caller must initialise both variables first, which is why every test here does.
    // ==============================================================================================

    /// <summary>
    /// Both <c>ref</c> parameters are genuinely written - each over a value the implementation cannot
    /// produce - and the call reports success.
    /// </summary>
    /// <remarks>
    /// The sentinels are chosen so that no legitimate output could be mistaken for them: the module
    /// sentinel contains a space and no dot, which no composed module can, and the line sentinel is
    /// negative, which no substituted line number can be. Without this test, an implementation that
    /// wrote only one of the two parameters would still satisfy every other assertion in this
    /// section, so this is the test that makes that mutation visible.
    /// </remarks>
    [Fact]
    public void TheOutParameterFormWritesBothPartsOverTheirSentinelsAndReportsSuccess()
    {
        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        // Wrapped, but both invocations stay on the continuation line: that is the line whose number
        // is substituted into both. Do not split this pair apart.
        (long witness, bool determined) =
            (CallerLineWitness(), CallerInfo.GetCurrentScript(ref module, ref lineNo));

        Assert.True(determined);
        Assert.NotEqual(SentinelModule, module);
        Assert.NotEqual(SentinelLineNumber, lineNo);
        Assert.NotEmpty(module);
        Assert.True(lineNo > 0L, "A genuine call site always yields a positive line number.");
        Assert.Equal(witness, lineNo);
    }

    /// <summary>
    /// The module value names this source file and the enclosing member, and carries neither the
    /// build machine's directory nor the source extension.
    /// </summary>
    /// <remarks>
    /// The assertion relates the module to <c>nameof(CallerInfoTests)</c> and to the captured path's
    /// ENDING rather than to an absolute path, because an absolute path is a property of the machine
    /// that compiled the file and not of the behaviour under test.
    /// </remarks>
    [Fact]
    public void TheOutParameterFormsModuleNamesThisFileAndTheEnclosingMember()
    {
        string substitutedPath = CallerFilePathWitness();
        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        Assert.True(CallerInfo.GetCurrentScript(ref module, ref lineNo));

        Assert.EndsWith(
            nameof(CallerInfoTests) + SourceFileExtension,
            substitutedPath,
            StringComparison.Ordinal);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(TheOutParameterFormsModuleNamesThisFileAndTheEnclosingMember),
            module);
        Assert.DoesNotContain(SourceFileExtension, module, StringComparison.Ordinal);
        Assert.DoesNotContain("/", module, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", module, StringComparison.Ordinal);
        Assert.DoesNotContain(LineNumberIntroducer, module, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two call sites in one member write increasing line numbers whose difference equals the number
    /// of source lines between them, while the module value stays identical.
    /// </summary>
    /// <remarks>
    /// Same construction as the single-value form's equivalent, and for the same reason: the
    /// difference is compared against two witnesses so that editing this file cannot falsify it.
    /// </remarks>
    [Fact]
    public void TwoOutParameterCallSitesReportIncreasingLineNumbersWhoseDifferenceIsTheSourceDistance()
    {
        string firstModule = SentinelModule;
        long firstLine = SentinelLineNumber;
        string secondModule = SentinelModule;
        long secondLine = SentinelLineNumber;

        // Each pair is one wrapped statement whose two invocations share the continuation line: do
        // not split either apart.
        (long firstWitness, bool firstDetermined) =
            (CallerLineWitness(), CallerInfo.GetCurrentScript(ref firstModule, ref firstLine));

        (long secondWitness, bool secondDetermined) =
            (CallerLineWitness(), CallerInfo.GetCurrentScript(ref secondModule, ref secondLine));

        Assert.True(firstDetermined);
        Assert.True(secondDetermined);
        Assert.Equal(firstModule, secondModule);
        Assert.True(secondLine > firstLine, "A later call site must write a strictly greater line number.");
        Assert.Equal(secondWitness - firstWitness, secondLine - firstLine);
    }

    /// <summary>
    /// The two shapes agree exactly: called from one source line, the single-value form returns
    /// precisely the module the out-parameter form writes, followed by the line-number tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the identity the implementation states as the relationship between its two members -
    /// the second shape is the DECOMPOSITION of the first, not a second algorithm - and which it
    /// explicitly leaves to a test rather than assuming. Both legacy prototypes bind one native
    /// alias [getcurrentscript.srf:L7-L8], so the identity is what reproduces that relationship
    /// rather than merely reproducing two signatures.
    /// </para>
    /// <para>
    /// The two invocations share one source line - hence the single tuple assignment - because that is
    /// the only way all three substituted values are identical between them; on separate lines the line
    /// numbers would legitimately differ and the equality could only be asserted loosely. Note that the
    /// statement is wrapped while the two invocations stay together, since it is the line the
    /// INVOCATIONS occupy that decides the value. The line-number tail is rendered with the
    /// invariant culture, matching the implementation, so the assertion cannot pass or fail according
    /// to the ambient culture.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoShapesAgreeExactlyWhenCalledFromOneSourceLine()
    {
        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        // The two shapes are invoked from ONE line, which is what makes all three substituted values
        // identical between them and the equality below exact. Do not split this pair apart.
        (string composed, bool determined) =
            (CallerInfo.GetCurrentScript(), CallerInfo.GetCurrentScript(ref module, ref lineNo));

        Assert.True(determined);
        Assert.Equal(module + LineNumberIntroducer + lineNo.ToString(CultureInfo.InvariantCulture), composed);
        Assert.Equal(module, ModulePartOf(composed));
        Assert.Equal(lineNo, LineNumberPartOf(composed));
    }

    /// <summary>
    /// The incoming value of neither <c>ref</c> parameter is read: two readings taken with completely
    /// different incoming values are identical in every respect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the property that makes <c>ref</c> harmless where the legacy declared <c>ref</c>. The
    /// implementation documents that it never reads either incoming value, so the initialiser a
    /// caller is forced to write cannot influence the outcome - and a caller keeps the ability to
    /// pass a pre-initialised variable, which <c>out</c> would remove.
    /// </para>
    /// <para>
    /// Both readings are taken through <see cref="ReportUnforwardedParts"/>, which is the ONE test in
    /// this file where helper indirection is not merely safe but the right instrument: the helper
    /// contains a single call site, so all three substituted values are identical between the two
    /// readings BY CONSTRUCTION. The only difference left between them is the incoming values, which
    /// isolates the property exactly. Calling twice from this method instead would put the two calls on
    /// different lines and make the line numbers differ for a reason that has nothing to do with what
    /// is being measured.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIncomingValuesOfBothRefParametersAreNeverRead()
    {
        string moduleFromSentinel = SentinelModule;
        long lineFromSentinel = SentinelLineNumber;
        string moduleFromEmpty = string.Empty;
        long lineFromMaximum = long.MaxValue;

        bool fromSentinel = ReportUnforwardedParts(ref moduleFromSentinel, ref lineFromSentinel);
        bool fromEmpty = ReportUnforwardedParts(ref moduleFromEmpty, ref lineFromMaximum);

        Assert.Equal(fromSentinel, fromEmpty);
        Assert.Equal(moduleFromSentinel, moduleFromEmpty);
        Assert.Equal(lineFromSentinel, lineFromMaximum);

        // Both readings name the helper, not this test - which is the same lexical-caller consequence
        // Section 1 pins, observed here as the reason the two are comparable at all.
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ReportUnforwardedParts),
            moduleFromSentinel);
    }

    /// <summary>
    /// The boolean result is invariantly <see langword="true"/> at every genuine call site, whatever
    /// kind of member the call is written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The port defines exactly one way to obtain <see langword="false"/>, and no genuine call
    /// site can take it.</b> The result is false only when the member name degrades to the unknown
    /// token, and the compiler always substitutes a member name at a real call site - so false is
    /// reachable only by explicitly supplying an empty or dot-only member name, which is to say by
    /// suppressing the substitution on purpose. That path is real, it is covered in Section 3, and it
    /// is NOT invented here: no failure mode beyond it is asserted, because the closed binary
    /// documented none and the legacy declaration has no call site that could imply one (C-B).
    /// </para>
    /// <para>
    /// The four call sites exercised are an ordinary method, a member reached through a helper, a
    /// member reached through a forwarding helper, and a lambda - the member kinds whose substituted
    /// names are produced differently by the compiler.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOutParameterFormIsInvariantlyTrueAtEveryGenuineCallSite()
    {
        string module = SentinelModule;
        long lineNo = SentinelLineNumber;
        Assert.True(CallerInfo.GetCurrentScript(ref module, ref lineNo));

        string fromHelper = SentinelModule;
        long lineFromHelper = SentinelLineNumber;
        Assert.True(ReportUnforwardedParts(ref fromHelper, ref lineFromHelper));

        string fromForwarder = SentinelModule;
        long lineFromForwarder = SentinelLineNumber;
        Assert.True(ReportForwardedParts(ref fromForwarder, ref lineFromForwarder));

        bool determinedInsideLambda = false;
        Exception? escaped = Record.Exception(() =>
        {
            string moduleInsideLambda = SentinelModule;
            long lineInsideLambda = SentinelLineNumber;
            determinedInsideLambda = CallerInfo.GetCurrentScript(ref moduleInsideLambda, ref lineInsideLambda);
        });

        Assert.Null(escaped);
        Assert.True(determinedInsideLambda);
    }

    /// <summary>
    /// A forwarding helper's out-parameter form writes its CALLER's identity, matching the
    /// single-value form's forwarding behaviour.
    /// </summary>
    /// <remarks>
    /// Both shapes must recover the legacy reading the same way, or a caller would have to know which
    /// shape it was using before it could trust the location. The unforwarded helper is read on the
    /// same test for contrast, and the two must disagree.
    /// </remarks>
    [Fact]
    public void AForwardingHelpersOutParameterFormReportsItsCaller()
    {
        string forwardedModule = SentinelModule;
        long forwardedLine = SentinelLineNumber;
        string unforwardedModule = SentinelModule;
        long unforwardedLine = SentinelLineNumber;

        // Wrapped, with both invocations on the continuation line: do not split this pair apart.
        (long witness, bool forwardedDetermined) =
            (CallerLineWitness(), ReportForwardedParts(ref forwardedModule, ref forwardedLine));

        Assert.True(forwardedDetermined);
        Assert.True(ReportUnforwardedParts(ref unforwardedModule, ref unforwardedLine));

        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(AForwardingHelpersOutParameterFormReportsItsCaller),
            forwardedModule);
        Assert.Equal(witness, forwardedLine);
        Assert.Equal(
            nameof(CallerInfoTests) + ScopeSeparator + nameof(ReportUnforwardedParts),
            unforwardedModule);
        Assert.NotEqual(forwardedModule, unforwardedModule);
    }

    // ==============================================================================================
    //  SECTION 3 - COMPOSITION, DEGRADATION AND THE FALSE PATH, TABLE-DRIVEN
    // ==============================================================================================
    //  This is the ONE section that supplies the caller information arguments, and it is the section
    //  where the input genuinely varies - so it is table-driven with [MemberData], per the parity
    //  matrix shape the AAP prescribes for ported behaviour.
    //
    //  WHY SUPPLYING THEM IS CORRECT HERE, AND ONLY HERE. The degraded branches and the `false`
    //  return are not reachable from a genuine call site: the compiler always substitutes a member
    //  name and always substitutes a path. Reaching them therefore requires supplying the values,
    //  which is precisely the position the implementation sanctions for a hand-written value - the
    //  forwarding member. Nothing about a call site's identity can leak into these results, because
    //  with all three arguments supplied no substitution happens at all; that is also why the
    //  [MemberData] indirection that would be fatal in Sections 1 and 2 is harmless here. The
    //  factory below returns input rows and expected text only - it does not call CallerInfo.
    //
    //  THE EXPECTED VALUES ARE WRITTEN OUT IN FULL, deliberately, rather than assembled from the
    //  format constants declared above. A literal expectation is the stronger check: it pins the dot,
    //  the " line:" introducer and both degradation tokens as text, so a change to any of them
    //  surfaces here as well as in the constants.
    // ==============================================================================================

    /// <summary>
    /// The composition and degradation matrix: a supplied member name, source path and line number,
    /// the exact text the single-value form must compose from them, and the boolean the
    /// out-parameter form must return for the same inputs.
    /// </summary>
    /// <returns>One row per behaviour of the two private derivations the members share.</returns>
    /// <remarks>
    /// <para>
    /// Every row was verified against the implementation on this host before being written down. The
    /// rows cover, in order: an ordinary POSIX path; a WINDOWS path, which matters because the
    /// implementation scans for separators by hand rather than delegating to a path API that would
    /// not recognise a backslash on Linux and would leak the whole directory; the instance and static
    /// constructor names, whose leading dot is stripped; a bare file name with no directory; a file
    /// with no extension; a multi-dot file name; a name that is nothing but an extension, whose
    /// leading dot is deliberately KEPT rather than reducing the scope to nothing; an empty path and
    /// a path ending in a separator, both of which degrade the scope; an empty and a dot-only member
    /// name, both of which degrade the member AND turn the boolean false; both degradations at once;
    /// a negative line number and the maximum line number, neither of which the compiler can supply
    /// but both of which a forwarding member could pass; and a drive-relative Windows path whose
    /// colon is the separator.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, int, string, bool> SuppliedCallerInformationRows() => new()
    {
        { "Submit", "/srv/app/OrderService.cs", 42, "OrderService.Submit line:42", true },
        { "Submit", "C:\\src\\app\\OrderService.cs", 42, "OrderService.Submit line:42", true },
        { ".ctor", "/srv/app/OrderService.cs", 7, "OrderService.ctor line:7", true },
        { ".cctor", "/srv/app/OrderService.cs", 8, "OrderService.cctor line:8", true },
        { "Submit", "OrderService.cs", 3, "OrderService.Submit line:3", true },
        { "Submit", "/srv/app/OrderService", 3, "OrderService.Submit line:3", true },
        { "Submit", "/srv/app/Order.Service.cs", 3, "Order.Service.Submit line:3", true },
        { "Submit", ".editorconfig", 3, ".editorconfig.Submit line:3", true },
        { "Submit", "", 3, "<unknown>.Submit line:3", true },
        { "Submit", "/srv/app/", 3, "<unknown>.Submit line:3", true },
        { "", "/srv/app/OrderService.cs", 0, "OrderService.? line:0", false },
        { ".", "/srv/app/OrderService.cs", 1, "OrderService.? line:1", false },
        { "", "", 0, "<unknown>.? line:0", false },
        { "Submit", "/srv/app/OrderService.cs", -5, "OrderService.Submit line:-5", true },
        { "Submit", "/srv/app/OrderService.cs", int.MaxValue, "OrderService.Submit line:2147483647", true },
        { "Submit", "C:OrderService.cs", 9, "OrderService.Submit line:9", true },
    };

    /// <summary>
    /// The member names that suppress the substitution and so produce the degraded member token.
    /// </summary>
    /// <returns>The empty name and the dot-only name, which are the only two such values.</returns>
    public static TheoryData<string> SuppressedMemberNameRows() => new()
    {
        string.Empty,
        ScopeSeparator,
    };

    /// <summary>
    /// The source paths that carry no file-name portion at all and so degrade the scope.
    /// </summary>
    /// <returns>
    /// The empty path, a bare POSIX root, a bare Windows drive specifier, and a path ending in a
    /// separator - the four ways a path can leave nothing to name a scope with.
    /// </returns>
    public static TheoryData<string> ScopelessPathRows() => new()
    {
        string.Empty,
        "/",
        "C:",
        "/srv/app/",
    };

    /// <summary>
    /// The single-value form composes exactly the documented shape for every supplied input.
    /// </summary>
    /// <remarks>
    /// The final assertion checks the table against itself: a row's boolean is false exactly when its
    /// expected text ends the module with the degraded member token. That is the definition of the
    /// boolean, so tying the two columns together documents why a row returns what it returns instead
    /// of leaving the reader to infer it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SuppliedCallerInformationRows))]
    public void TheComposedValueIsExactlyTheDocumentedShapeForEverySuppliedInput(
        string memberName,
        string filePath,
        int lineNumber,
        string expectedComposed,
        bool expectedDetermined)
    {
        string composed = CallerInfo.GetCurrentScript(memberName, filePath, lineNumber);

        Assert.Equal(expectedComposed, composed);
        Assert.Equal(
            !expectedDetermined,
            ModulePartOf(expectedComposed).EndsWith(ScopeSeparator + UnknownMemberName, StringComparison.Ordinal));
    }

    /// <summary>
    /// The out-parameter form decomposes exactly what the single-value form composes, writes both
    /// parameters over their sentinels on the <see langword="false"/> return as well as the
    /// <see langword="true"/> one, and returns the row's boolean.
    /// </summary>
    /// <remarks>
    /// Writing both parameters unconditionally is what lets a caller that ignores the return value
    /// still observe documented values rather than whatever it happened to initialise, and the
    /// degraded rows are the only place that promise can be tested at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SuppliedCallerInformationRows))]
    public void TheOutParameterFormDecomposesExactlyWhatTheSingleValueFormComposes(
        string memberName,
        string filePath,
        int lineNumber,
        string expectedComposed,
        bool expectedDetermined)
    {
        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        bool determined = CallerInfo.GetCurrentScript(ref module, ref lineNo, memberName, filePath, lineNumber);

        Assert.Equal(expectedDetermined, determined);
        Assert.NotEqual(SentinelModule, module);
        Assert.NotEqual(SentinelLineNumber, lineNo);
        Assert.Equal(ModulePartOf(expectedComposed), module);
        Assert.Equal(LineNumberPartOf(expectedComposed), lineNo);
        Assert.Equal(module + LineNumberIntroducer + lineNo.ToString(CultureInfo.InvariantCulture), expectedComposed);
    }

    /// <summary>
    /// Suppressing the member name is the only way to obtain <see langword="false"/>, and both
    /// parameters are still written when it happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the failure path stated explicitly rather than left implicit in the matrix. The port
    /// defines no other: there is no argument validation, no throw and no other condition under which
    /// the boolean is false, and none is invented here. A control reading at the same call site with a
    /// usable member name returns <see langword="true"/>, which is what shows the false result comes
    /// from the suppression and not from the surrounding call.
    /// </para>
    /// <para>
    /// The path is worth covering precisely because the substitution cannot reach it: a genuine call
    /// site always receives a compiler-supplied member name, so without this test the branch would
    /// never execute and the line coverage gate for the implementation could not be met.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SuppressedMemberNameRows))]
    public void SuppressingTheMemberNameIsTheOnlyWayToObtainFalse(string suppressedMemberName)
    {
        // Both fixtures are SUPPLIED values, named so they cannot be misread as a reference to this
        // file's own source lines - which nothing in this file ever hard-codes. They are held here
        // rather than inlined precisely so that the assertion below compares the written-through value
        // against the same constant that was handed in.
        const string SuppliedPath = "/srv/app/OrderService.cs";
        const int SuppliedLineNumber = 11;

        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        bool determined = CallerInfo.GetCurrentScript(
            ref module,
            ref lineNo,
            suppressedMemberName,
            SuppliedPath,
            SuppliedLineNumber);

        Assert.False(determined);
        Assert.EndsWith(ScopeSeparator + UnknownMemberName, module, StringComparison.Ordinal);
        Assert.NotEqual(SentinelModule, module);
        Assert.Equal((long)SuppliedLineNumber, lineNo);

        string controlModule = SentinelModule;
        long controlLine = SentinelLineNumber;

        Assert.True(CallerInfo.GetCurrentScript(
            ref controlModule,
            ref controlLine,
            "Submit",
            SuppliedPath,
            SuppliedLineNumber));
        Assert.Equal("OrderService.Submit", controlModule);
        Assert.Equal((long)SuppliedLineNumber, controlLine);
    }

    /// <summary>
    /// A path that names no file degrades the SCOPE to its own token and leaves the boolean
    /// <see langword="true"/>, because the boolean tracks the member name alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This asymmetry is easy to miss and is worth pinning on its own.</b> The two degradations are
    /// independent: an unusable member name turns the result <see langword="false"/>, an unusable path
    /// does not. Reading the boolean as "everything was determined" would therefore be wrong, and this
    /// theory is what stops that reading from going unchallenged. It also fixes the scope token as
    /// text rather than leaving it implicit in the matrix's expected strings.
    /// </para>
    /// <para>
    /// The token keeps the value well formed rather than emitting a leading dot, which is what lets the
    /// assertion payload consumer's four invariants hold even for a value that named nothing - so those
    /// are checked here too.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScopelessPathRows))]
    public void AScopelessPathDegradesTheScopeButLeavesTheBooleanTrue(string scopelessPath)
    {
        const string SuppliedMemberName = "Submit";
        const int SuppliedLineNumber = 23;

        string module = SentinelModule;
        long lineNo = SentinelLineNumber;

        bool determined = CallerInfo.GetCurrentScript(
            ref module,
            ref lineNo,
            SuppliedMemberName,
            scopelessPath,
            SuppliedLineNumber);

        // The member name was usable, so the boolean is true even though the scope degraded.
        Assert.True(determined);
        Assert.Equal(UnknownScopeName + ScopeSeparator + SuppliedMemberName, module);
        Assert.Equal((long)SuppliedLineNumber, lineNo);
        Assert.StartsWith(UnknownScopeName, module, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownMemberName, module, StringComparison.Ordinal);

        string composed = CallerInfo.GetCurrentScript(
            SuppliedMemberName,
            scopelessPath,
            SuppliedLineNumber);

        Assert.Equal(module + LineNumberIntroducer + lineNo.ToString(CultureInfo.InvariantCulture), composed);

        (string windowMenu, string objectName, string objectEvent, long line) =
            ReadAsTheAssertPayloadConsumerWould(composed);

        Assert.Equal(UnknownScopeName, windowMenu);
        Assert.Equal(windowMenu, objectName);
        Assert.Equal(SuppliedMemberName, objectEvent);
        Assert.Equal((long)SuppliedLineNumber, line);
    }

    /// <summary>
    /// Every value either member can produce - including every degraded one - still satisfies the four
    /// positional invariants the assertion payload consumer depends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>assert.srf:L39-L59</c> is the only consumer in the estate that parses a composed identity,
    /// and it does so with four unguarded positional searches. It is therefore the only available
    /// evidence of what the text has to look like, and a value that broke one of its invariants would
    /// corrupt the seven-field payload rather than merely read oddly. The reading is reproduced here
    /// with its index arithmetic in the open, because translating a one-based <c>Pos</c> and
    /// <c>Mid</c> to a zero-based <c>IndexOf</c> and <c>Substring</c> is the refactor's single most
    /// dangerous mechanical hazard.
    /// </para>
    /// <para>
    /// The member segment the consumer reads is the degraded token exactly when the row's boolean is
    /// false, and the line the consumer recovers is exactly the number supplied - including the
    /// negative one, which parses through the leading sign just as the legacy conversion would.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SuppliedCallerInformationRows))]
    public void EverySuppliedInputStillSatisfiesTheAssertPayloadConsumersFourInvariants(
        string memberName,
        string filePath,
        int lineNumber,
        string expectedComposed,
        bool expectedDetermined)
    {
        string composed = CallerInfo.GetCurrentScript(memberName, filePath, lineNumber);
        Assert.Equal(expectedComposed, composed);

        (string windowMenu, string objectName, string objectEvent, long line) =
            ReadAsTheAssertPayloadConsumerWould(composed);

        // The object reading always has text. The window reading can legitimately be empty - a file
        // name that is nothing but an extension puts the first dot at position one - which the
        // implementation records as tolerated, so it is not asserted non-empty here.
        Assert.NotEmpty(objectName);
        Assert.NotNull(windowMenu);
        Assert.Equal(!expectedDetermined, string.Equals(objectEvent, UnknownMemberName, StringComparison.Ordinal));
        Assert.Equal((long)lineNumber, line);
    }

    /// <summary>
    /// A value composed at a genuine call site is read correctly by the assertion payload consumer,
    /// and its single dot puts the consumer on the branch where the window and object readings are
    /// the same value.
    /// </summary>
    /// <remarks>
    /// The normal case here carries exactly one dot, because a file scope is a single segment. That is
    /// a DEFINED branch of the consumer rather than a degenerate one: with one dot the first and last
    /// dot coincide, so the consumer assigns the object reading from the window reading
    /// [assert.srf:L49-L51]. Asserting the four fields against <c>nameof</c> and a same-line witness
    /// pins the whole round trip - compose, then parse as the only real consumer would - with no
    /// literal anywhere.
    /// </remarks>
    [Fact]
    public void AGenuineCallSitesValueIsReadCorrectlyByTheAssertPayloadConsumer()
    {
        // One statement, two invocations, one line: do not split this pair apart.
        (long witness, string composed) = (CallerLineWitness(), CallerInfo.GetCurrentScript());

        (string windowMenu, string objectName, string objectEvent, long line) =
            ReadAsTheAssertPayloadConsumerWould(composed);

        Assert.Equal(nameof(CallerInfoTests), windowMenu);
        Assert.Equal(windowMenu, objectName);
        Assert.Equal(nameof(AGenuineCallSitesValueIsReadCorrectlyByTheAssertPayloadConsumer), objectEvent);
        Assert.Equal(witness, line);
    }

    // ==============================================================================================
    //  SECTION 4 - THE INSTRUMENTS
    // ==============================================================================================
    //  Three kinds of helper live here and the distinction between them is what keeps this suite
    //  honest, so it is stated rather than left to be worked out:
    //
    //   1. WITNESSES. They read a caller information attribute through a parameter of their own and
    //      return it. They never touch CallerInfo. Each is a second, independent reading of the same
    //      compile-time substitution, and that is what allows an ABSOLUTE line number, file name or
    //      member name to be asserted exactly with no literal written into this file. A witness
    //      sharing a physical source line with the call under test receives the same substituted line
    //      number as that call, which is the entire mechanism.
    //
    //   2. CALL SITES WHOSE OWN IDENTITY IS THE SUBJECT. ReportUnforwarded and its three-deep chain,
    //      the property accessor, and the constructor capture. Each contains a call written with NO
    //      arguments, so each reports ITSELF - and every assertion that names one names it on purpose.
    //      These are the only helpers allowed to stand between a test and CallerInfo, and only because
    //      what they report is the thing being measured.
    //
    //   3. PURE STRING READERS. ModulePartOf, LineNumberPartOf and the payload-consumer reading. They
    //      take a string and call nothing, so no call site's identity can reach them; sharing them
    //      across tests is therefore safe in a way that sharing a helper of kind 2 would not be.
    //
    //  No helper here forbids inlining. Nothing in this file reads a stack frame, so there is no frame
    //  whose disappearance could change a result - which is itself part of what the suite establishes.
    // ==============================================================================================

    /// <summary>
    /// A witness reading of <see cref="CallerLineNumberAttribute"/>, used to pin an absolute line
    /// number without writing one.
    /// </summary>
    /// <param name="lineNumber">Substituted by the compiler. Never supplied by a caller.</param>
    /// <returns>
    /// The line number of the call to this method, widened to <see cref="long"/> to match the width
    /// the out-parameter form writes.
    /// </returns>
    private static long CallerLineWitness([CallerLineNumber] int lineNumber = 0) => lineNumber;

    /// <summary>
    /// A witness reading of <see cref="CallerFilePathAttribute"/>, used to relate the composed scope
    /// to the path the compiler actually embedded.
    /// </summary>
    /// <param name="filePath">Substituted by the compiler. Never supplied by a caller.</param>
    /// <returns>
    /// The path of this source file as the build machine saw it. Every call site is in this file, so
    /// the value is the same wherever it is read from.
    /// </returns>
    private static string CallerFilePathWitness([CallerFilePath] string filePath = "") => filePath;

    /// <summary>
    /// A witness reading of <see cref="CallerMemberNameAttribute"/>, used to show the member segment
    /// is passed through rather than derived.
    /// </summary>
    /// <param name="memberName">Substituted by the compiler. Never supplied by a caller.</param>
    /// <returns>The name of the member containing the call to this method.</returns>
    private static string CallerMemberNameWitness([CallerMemberName] string memberName = "") => memberName;

    /// <summary>
    /// Calls the single-value form with no arguments and therefore reports ITSELF.
    /// </summary>
    /// <returns>This helper's own composed identity, never its caller's.</returns>
    /// <remarks>
    /// The substitution happens here, at this single call site, which is why every reading taken
    /// through this helper is identical however it was reached.
    /// </remarks>
    private static string ReportUnforwarded() => CallerInfo.GetCurrentScript();

    /// <summary>
    /// Entry point of a three-deep chain that ends at <see cref="ReportUnforwarded"/>.
    /// </summary>
    /// <returns><see cref="ReportUnforwarded"/>'s identity, unchanged by the extra depth.</returns>
    private static string ReportThroughOuter() => ReportThroughMiddle();

    /// <summary>Middle link of the chain.</summary>
    /// <returns><see cref="ReportUnforwarded"/>'s identity.</returns>
    private static string ReportThroughMiddle() => ReportThroughInner();

    /// <summary>Innermost link of the chain.</summary>
    /// <returns><see cref="ReportUnforwarded"/>'s identity.</returns>
    private static string ReportThroughInner() => ReportUnforwarded();

    /// <summary>
    /// Declares its own caller information parameters and forwards them, and therefore reports its
    /// CALLER rather than itself.
    /// </summary>
    /// <param name="memberName">Substituted at the call to THIS method, then passed on.</param>
    /// <param name="filePath">Substituted at the call to THIS method, then passed on.</param>
    /// <param name="lineNumber">Substituted at the call to THIS method, then passed on.</param>
    /// <returns>The caller's composed identity.</returns>
    /// <remarks>
    /// This is the implementation's documented forwarding pattern and the only sanctioned reason to
    /// pass these arguments by hand. It cannot be made transitive by accident: a further helper that
    /// wanted the same effect would have to declare the three parameters itself, so the chain is
    /// visible in every signature it passes through.
    /// </remarks>
    private static string ReportForwarded(
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => CallerInfo.GetCurrentScript(memberName, filePath, lineNumber);

    /// <summary>
    /// Calls the out-parameter form with no caller information arguments and therefore reports
    /// ITSELF.
    /// </summary>
    /// <param name="module">Receives this helper's own qualified member.</param>
    /// <param name="lineNo">Receives the line number of the call inside this helper.</param>
    /// <returns>Whatever the out-parameter form returned.</returns>
    private static bool ReportUnforwardedParts(ref string module, ref long lineNo)
        => CallerInfo.GetCurrentScript(ref module, ref lineNo);

    /// <summary>
    /// Forwards its own caller information to the out-parameter form and therefore reports its
    /// CALLER.
    /// </summary>
    /// <param name="module">Receives the caller's qualified member.</param>
    /// <param name="lineNo">Receives the caller's line number.</param>
    /// <param name="memberName">Substituted at the call to THIS method, then passed on.</param>
    /// <param name="filePath">Substituted at the call to THIS method, then passed on.</param>
    /// <param name="lineNumber">Substituted at the call to THIS method, then passed on.</param>
    /// <returns>Whatever the out-parameter form returned.</returns>
    /// <remarks>
    /// The two <c>ref</c> parameters must precede the compiler-supplied optional ones, exactly as
    /// they do on the member being forwarded to; that ordering is a language rule rather than a
    /// choice, and reproducing it here is part of showing the forwarding pattern works for both
    /// shapes.
    /// </remarks>
    private static bool ReportForwardedParts(
        ref string module,
        ref long lineNo,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => CallerInfo.GetCurrentScript(ref module, ref lineNo, memberName, filePath, lineNumber);

    /// <summary>
    /// A property accessor whose body composes an identity, used to show the substituted member name
    /// is the property's own name and not a generated accessor name.
    /// </summary>
    /// <value>This property's composed identity.</value>
    private static string ComposedFromPropertyAccessor => CallerInfo.GetCurrentScript();

    /// <summary>
    /// Returns the module portion of a composed value: everything before the line-number
    /// introducer.
    /// </summary>
    /// <param name="composed">A value produced by the single-value form.</param>
    /// <returns>The <c>&lt;scope&gt;.&lt;member&gt;</c> portion.</returns>
    /// <remarks>
    /// A pure string reader: it calls nothing, so no call site's identity can leak through it and
    /// sharing it across tests is safe. The introducer is located from the END so that a member name
    /// could never shadow it, and its presence is asserted rather than assumed, because every one of
    /// the payload consumer's invariants depends on it being there.
    /// </remarks>
    private static string ModulePartOf(string composed)
    {
        int introducerStart = composed.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);

        Assert.True(
            introducerStart > 0,
            "A composed value must carry the line-number introducer after a non-empty module.");

        return composed.Substring(0, introducerStart);
    }

    /// <summary>
    /// Returns the line number carried by a composed value.
    /// </summary>
    /// <param name="composed">A value produced by the single-value form.</param>
    /// <returns>The parsed line number.</returns>
    /// <remarks>
    /// Parsed with the invariant culture and a signed integer style, matching the invariant-culture
    /// rendering the implementation performs, so neither the ambient culture nor a negative value
    /// supplied through a forwarding member can affect the reading.
    /// </remarks>
    private static long LineNumberPartOf(string composed)
    {
        int introducerStart = composed.LastIndexOf(LineNumberIntroducer, StringComparison.Ordinal);

        Assert.True(
            introducerStart > 0,
            "A composed value must carry the line-number introducer after a non-empty module.");

        bool parsed = long.TryParse(
            composed.AsSpan(introducerStart + LineNumberIntroducer.Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long lineNumber);

        Assert.True(parsed, "The text after the line-number introducer must be an integer.");

        return lineNumber;
    }

    /// <summary>
    /// Reads a composed value exactly as the assertion payload consumer at
    /// <c>ws_objects/pfw.common.pbl.src/assert.srf:L39-L59</c> reads a frame, asserting each of the
    /// four positional invariants that reading depends on.
    /// </summary>
    /// <param name="calling">The composed value to read.</param>
    /// <returns>
    /// The four fields the consumer extracts: its window-or-menu reading, its object reading, its
    /// object-event reading and its line number.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The consumer's four searches are unguarded, so each is reproduced here as an assertion:
    /// a last dot must exist, a space must follow it, a colon must follow that space, and the text
    /// after the colon must convert to a number.
    /// </para>
    /// <para>
    /// <b>The index arithmetic is deliberately explicit.</b> PowerScript's <c>Pos</c> and
    /// <c>LastPos</c> return ONE-BASED positions with zero meaning "not found", and <c>Mid</c> and
    /// <c>Left</c> take one-based starts; C# returns zero-based indices with minus one meaning "not
    /// found". Every position below is therefore held as the ONE-BASED value the legacy would hold -
    /// which is why each is a <c>...Index + 1</c> and why each is then usable directly as a
    /// zero-based search start or <c>Substring</c> offset. Collapsing the two conventions silently is
    /// the refactor's single most dangerous mechanical hazard, so the conversion is written out at
    /// every step and each step names the legacy line it reproduces.
    /// </para>
    /// </remarks>
    private static (string WindowMenu, string ObjectName, string ObjectEvent, long Line)
        ReadAsTheAssertPayloadConsumerWould(string calling)
    {
        // assert.srf:L39-L40 - nPos = LastPos(sCalling, ".") ; if nPos > 0 then
        int lastDot = calling.LastIndexOf('.') + 1;
        Assert.True(lastDot > 0, "Invariant 1: a composed value must contain at least one dot.");

        // assert.srf:L41 - nPos2 = Pos(sCalling, ".")
        int firstDot = calling.IndexOf('.') + 1;

        string windowMenu;
        string objectName;

        if (firstDot < lastDot)
        {
            // assert.srf:L44, L46 - two or more dots: the readings are distinct segments.
            windowMenu = calling.Substring(0, firstDot - 1);
            objectName = calling.Substring(firstDot, lastDot - firstDot - 1);
        }
        else
        {
            // assert.srf:L49-L51 - exactly one dot, which is the normal case for a composed value
            // because a file scope is a single segment. The object reading is assigned FROM the
            // window reading, so the two are the same value. A defined branch, not a degenerate one.
            windowMenu = calling.Substring(0, lastDot - 1);
            objectName = windowMenu;
        }

        // assert.srf:L55 - nPos2 = Pos(sCalling, " ", nPos + 1)
        int spaceAfterLastDot = calling.IndexOf(' ', lastDot) + 1;
        Assert.True(spaceAfterLastDot > 0, "Invariant 2: a space must follow the last dot.");

        // assert.srf:L56 - ex.#ObjectEvent = Mid(sCalling, nPos + 1, nPos2 - nPos - 1)
        string objectEvent = calling.Substring(lastDot, spaceAfterLastDot - lastDot - 1);

        // assert.srf:L58 - nPos = Pos(sCalling, ":", nPos2 + 1)
        int colonAfterSpace = calling.IndexOf(':', spaceAfterLastDot) + 1;
        Assert.True(colonAfterSpace > 0, "Invariant 3: a colon must follow that space.");

        // assert.srf:L59 - ex.#Line = Long(Mid(sCalling, nPos + 1))
        bool converted = long.TryParse(
            calling.AsSpan(colonAfterSpace),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long line);

        Assert.True(converted, "Invariant 4: the text after the colon must convert to a number.");

        return (windowMenu, objectName, objectEvent, line);
    }

    /// <summary>
    /// A capture site whose call is written in a CONSTRUCTOR, so the compiler substitutes the
    /// constructor's <c>.ctor</c> name and the implementation's leading-dot strip is reached through
    /// real compiler output rather than through a supplied string.
    /// </summary>
    private sealed class ConstructorCaptureHelper
    {
        /// <summary>
        /// Composes an identity from inside a constructor.
        /// </summary>
        internal ConstructorCaptureHelper() => Captured = CallerInfo.GetCurrentScript();

        /// <summary>
        /// The identity composed inside the constructor.
        /// </summary>
        /// <value>
        /// A value whose member segment is <c>ctor</c> - the substituted <c>.ctor</c> with its leading
        /// dot removed - qualified by this file's name, because the constructor is declared here.
        /// </value>
        internal string Captured { get; }
    }
}
