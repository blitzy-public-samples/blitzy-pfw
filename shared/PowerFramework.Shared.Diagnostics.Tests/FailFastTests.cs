// ==================================================================================================
//  FailFastTests.cs - THE SUITE THAT PINS THE FAIL-FAST POSTURE ACROSS BOTH HALVES OF THE SEAM
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE CLAIMS, IN ONE SENTENCE
//  ------------------------------------------------------------------------------------------------
//  A structural failure ends in TERMINATION, never in a warning-and-continue - and it does so whether
//  or not the payload that carried it could be decoded.
//
//  THE POSTURE SPANS A BOUNDARY THE LEGACY DID NOT HAVE, SO THE SUITE IS SPLIT ALONG THAT BOUNDARY
//  ------------------------------------------------------------------------------------------------
//  In PowerBuilder the whole posture lived in one process. `assert.srf` raised the failure and
//  `pfw.sra`'s systemerror event killed the application, and nothing sat between them. The port splits
//  that in two:
//
//      THE THROW      belongs to shared/PowerFramework.Shared.Diagnostics. Its fail-fast contract is
//                     "a failure ALWAYS throws and NEVER returns" [assert.srf:L83].
//      THE TERMINATE  belongs to whichever host handles the resulting system error
//                     [pfw.sra:L141-L143].
//
//  Section 1 below pins the first claim against the library. Section 2 pins the second against a
//  replica of the consumer. Together the two are the legacy behaviour; separately, either one alone
//  leaves a hole a graceful-degradation regression could slip through - a library that returned after a
//  failure would never reach a consumer, and a consumer that warned instead of terminating would make
//  the library's throw pointless.
//
//  C-K - THE `HALT CLOSE` MAPPING, NAMED EXPLICITLY
//  ------------------------------------------------------------------------------------------------
//  The legacy's terminating statement is `HALT CLOSE` at ws_objects/pfw.pbl.src/pfw.sra:L143. In the
//  .NET port that maps to PROCESS TERMINATION: a headless service has no application object to halt,
//  so the equivalent of "stop, now, do not continue" is ending the process. AAP 0.1.4 states the
//  requirement in as many words - fail-fast must survive as fail-fast and never as graceful
//  degradation - and AAP 0.6.7 repeats it, which is a fair signal that softening it is the tempting
//  mistake.
//
//  THIS SUITE NEVER TERMINATES ANYTHING. It observes the terminating step through an INJECTED SEAM in
//  the test double - `ILegacyTerminationSink`, satisfied by `RecordingTerminationSink` - which records
//  the report and the exit code and then returns. That is the only way to assert "it terminated"
//  without destroying the test host that is doing the asserting.
//
//  C-A - WHY THE PRODUCTION TERMINATOR IS NOT REFERENCED
//  ------------------------------------------------------------------------------------------------
//  The shipping consumer is the Gateway composition root's `Diagnostics/SystemErrorHandler`, the port
//  of pfw.sra's systemerror event. THIS SUITE DOES NOT REFERENCE IT, and the reason is C-A: the only
//  coupling permitted across a service boundary is the published contracts project, so a
//  ProjectReference from a shared library's test project into services/** is not available. The test
//  project file says so in as many words and carries exactly one ProjectReference. The decode and the
//  terminating step are therefore observed through this project's own `LegacySystemErrorConsumer`
//  replica, which was authored FROM pfw.sra rather than from any services/** file.
//
//  THE SEAM LIVES IN THE DOUBLE, NOT IN THE PRODUCTION LIBRARY - AND THAT IS THE WHOLE DESIGN
//  ------------------------------------------------------------------------------------------------
//  The folder requirement is two clauses that have to be read as one instruction: seam the termination
//  so a test can observe it, AND do not weaken the production path to make it testable. Both are
//  honoured by putting the seam entirely inside the test double. Concretely, NOTHING asserted in this
//  file required:
//
//      * a public hook, callback or event on shared/PowerFramework.Shared.Diagnostics
//      * a `virtual` member, or any member made overridable for a test
//      * an `internal` visibility widening beyond the ONE pre-existing InternalsVisibleTo the library
//        already declared for its own payload-builder seam, which this suite consumes as-is and did
//        not ask for
//      * a configuration flag, an environment switch or a #if TEST branch
//      * any terminator abstraction inside the Diagnostics library - there is none, and this suite
//        does not add, request or assume one
//
//  A future contributor reading Section 1 may notice it leans on reflection and on the compiled IL
//  rather than on an injected observer. That is deliberate, and it is not a workaround: the property
//  under test is "there is no path on which this returns", which is a property OF THE CODE, and the
//  honest way to assert a property of the code is to inspect the code. Adding an observer to make it
//  observable would change the very thing being observed. Do not "improve" the testability of the
//  Diagnostics library on this suite's behalf.
//
//  WHY THE IL IS INSPECTED AT ALL - THE ONE REGRESSION NOTHING ELSE CATCHES
//  ------------------------------------------------------------------------------------------------
//  `AssertFailed` builds one of two payload shapes behind a gate - `if (frameCount > 2)` - and then
//  throws OUTSIDE that gate [assert.srf:L37, L83]. Suppose a port moved the throw INSIDE the gate.
//  Every live call from a test would still throw, because a test host's stack is always deeper than
//  two frames, so the shallow arm would never be exercised and the regression would ship silently:
//  assertions would be swallowed in exactly the scenarios where the stack could not be captured.
//
//  A live-call test therefore cannot catch it. What can is the compiled method's instruction stream:
//  a method whose only exit is `throw` contains NO `ret` instruction at all. Section 1 asserts
//  precisely that, and contrasts it with the six guards, which DO contain `ret` because each has a
//  legitimate non-firing path. Moving the throw inside the gate necessarily introduces a `ret` and
//  necessarily fails that assertion, for every input and both payload shapes at once.
//
//  C-B - EVERY POSTURE-PINNING ASSERTION PRESERVES LEGACY BEHAVIOUR DELIBERATELY
//  ------------------------------------------------------------------------------------------------
//  Three behaviours pinned below look, to a reader who has not read the oracle, like defects worth
//  fixing. All three are reproduced ON PURPOSE and each carries a comment saying so at its assertion:
//
//      B1  A FAILURE CANNOT BE HANDLED WITHOUT TERMINATING, even when the payload is malformed.
//          pfw.sra:L143 sits outside every `if` above it.
//      B2  A CAPTURE FAILURE IS SWALLOWED SILENTLY, losing the location fields, and the failure is
//          still raised. The legacy handler body at assert.srf:L29-L32 is empty.
//      B3  THERE IS NO NON-THROWING WAY TO ASK "DID IT FAIL". The legacy publishes seven
//          `global subroutine` declarations [assert.srf:L7-L13] and not one function.
//
//  C-C / C-D
//  ------------------------------------------------------------------------------------------------
//  ws_objects/** is the READ-ONLY behavioural oracle. Every locator cited here was READ as
//  specification; nothing under it is opened, embedded, copied or edited, and this suite takes no
//  build-time or run-time dependency on any path beneath it. No deferred capability appears here in
//  any form - not as a name, a string, a comment or a fixture.
//
//  C-H
//  ------------------------------------------------------------------------------------------------
//  Nullable reference types and TreatWarningsAsErrors are inherited from the repository root, so this
//  file is warning-clean or it does not build. The coverage gate is measured from the Cobertura report
//  over the Diagnostics assembly; Section 1 reaches the throw path on both payload shapes.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns exactly "No user rules provided." - verified for this file. NO user rule
//  governs it, nothing was invented in their place, and the absence is a finding rather than licence
//  to lower the bar: the standard applied instead is AAP 0.7.2, the enterprise baseline, plus the AAP
//  0.7.3 constraints cited above. Naming follows from the same position: the repository root
//  .editorconfig scopes its naming-analyzer suppressions to the named PRODUCTION files on its BAND 3
//  roster - the single source of truth for that list - and extends them to no test file, so every
//  identifier here is conventional PascalCase and no SCREAMING_SNAKE
//  constant is declared. The legacy vocabulary survives in the VALUES, where it is observable.
//
//  AAP 0.6.7 - TABLE-DRIVEN THROUGHOUT
//  ------------------------------------------------------------------------------------------------
//  The parity matrices are theories fed from member data, so a case's expected outcome reads as a row
//  rather than being buried in a body. The termination matrix in Section 2 enumerates the input
//  classes explicitly - deep, shallow, single-field, unparseable-number and gate-failing - because the
//  claim being made is universal quantification over them: EVERY row terminates, exactly once.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// Characterization tests for the fail-fast posture: the library half proves a failure always throws
/// and never returns, and the consumer half proves a handled system error always terminates.
/// </summary>
/// <remarks>
/// <para>
/// Read this type's file header before adding a test. It records why the suite is split across the
/// library and a consumer replica, why the production terminator is deliberately not referenced (C-A),
/// how the legacy's <c>HALT CLOSE</c> maps onto process termination (C-K), and why the compiled
/// instruction stream is inspected for the one regression no live-call test can detect.
/// </para>
/// <para>
/// <b>Nothing in this suite ends a process.</b> The terminating step is observed through the injected
/// <see cref="ILegacyTerminationSink"/> seam in the test double, which records and returns. The suite
/// completing at all is itself part of the evidence.
/// </para>
/// </remarks>
public class FailFastTests
{
    // ==============================================================================================
    //  PROTOCOL CONSTANTS
    //  ----------------------------------------------------------------------------------------------
    //  Declared locally rather than imported from the production library because the production
    //  library does not publish them: the delimiter is a `const` local inside the payload builder
    //  [assert.srf:L19] and the report labels live in the consumer. Re-spelling them here is what makes
    //  this suite an INDEPENDENT check - a test that read its expected delimiter out of the code under
    //  test would agree with that code by construction, including when both are wrong.
    // ==============================================================================================

    /// <summary>
    /// The payload FIELD delimiter: CRLF, and CRLF only. [assert.srf:L19, pfw.sra:L115]
    /// </summary>
    /// <remarks>
    /// Never <see cref="Environment.NewLine"/>. On Linux - the target operating system - that is a
    /// bare line feed, which is the separator used INSIDE fields 2 and 7, so splitting on it would
    /// shatter those fields and turn a two-field payload into three or a seven-field payload into a
    /// dozen. The asymmetry between the two separators is the only reason the consumer's field count
    /// is unambiguous.
    /// </remarks>
    private const string PayloadFieldDelimiter = "\r\n";

    /// <summary>
    /// The separator used INSIDE a single payload field: a bare line feed.
    /// [assert.srf:L26, L63, L71]
    /// </summary>
    private const string IntraFieldSeparator = "\n";

    /// <summary>Payload field 1, the legacy assertion error number, as text. [assert.srf:L22]</summary>
    private const string AssertionNumberField = "-10000";

    /// <summary>
    /// Payload field 2's fixed opening text, before any info continuation. [assert.srf:L24]
    /// </summary>
    private const string BareAssertionText = "Assertion failed";

    /// <summary>
    /// The oracle's own info string [w_test_assert.srw:L42], used verbatim so the suite exercises a
    /// real legacy scenario rather than an invented one.
    /// </summary>
    private const string OracleInfo = "Invalid Number!";

    /// <summary>
    /// The field count of the SHALLOW payload shape, emitted when the frame count fails the gate.
    /// [assert.srf:L37 taken false, so only sMessages[1..2] exist]
    /// </summary>
    private const int ShallowFieldCount = 2;

    /// <summary>
    /// The field count of the DEEP payload shape, emitted when the frame count passes the gate.
    /// [assert.srf:L37 taken true, filling sMessages[3..7]]
    /// </summary>
    private const int DeepFieldCount = 7;

    /// <summary>
    /// The number of members the legacy publishes on the assertion surface: seven
    /// <c>global subroutine</c> declarations and not one function. [assert.srf:L7-L13]
    /// </summary>
    private const int LegacySubroutineCount = 7;

    // ----------------------------------------------------------------------------------------------
    // THE LEGACY REPORT LABELS, PRESERVED VERBATIM (AAP 0.8.2)
    // ----------------------------------------------------------------------------------------------
    // Byte-for-byte from pfw.sra:L129-L138. They are re-spelled here, rather than read from the
    // replica, for the reason given above: an independent expectation is the only kind worth having.
    // The report is assembled with BARE LINE FEEDS between its lines - the legacy source writes `~n`,
    // never `~r~n` - so a port that used CRLF here would produce a report that, if it were ever fed
    // back through the field splitter, would fragment.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The report's opening line. [pfw.sra:L129]</summary>
    private const string ReportOpeningLine = "类型: SYSTEM";

    /// <summary>The message line's label, line feed included. [pfw.sra:L130]</summary>
    private const string ReportMessageLabel = "\n信息: ";

    /// <summary>The window line's label, emitted only when the window and object differ.
    /// [pfw.sra:L132]</summary>
    private const string ReportWindowLabel = "\n窗口: ";

    /// <summary>The object line's label, unconditional. [pfw.sra:L134]</summary>
    private const string ReportObjectLabel = "\n对象: ";

    /// <summary>The function line's label, unconditional. [pfw.sra:L135]</summary>
    private const string ReportFunctionLabel = "\n函数: ";

    /// <summary>The line-number line's label, unconditional. [pfw.sra:L136]</summary>
    private const string ReportLineLabel = "\n行号: ";

    /// <summary>The call-stack line's label, emitted only for a non-empty trace. [pfw.sra:L138]</summary>
    private const string ReportStackTraceLabel = "\n调用栈:\n";

    // ----------------------------------------------------------------------------------------------
    // THE FIVE TERMINATION INPUT CLASSES
    // ----------------------------------------------------------------------------------------------
    // Case keys for the termination matrix. Named constants rather than bare strings so a typo in a
    // row is a compile error instead of a silently skipped case.
    // ----------------------------------------------------------------------------------------------

    /// <summary>A well-formed DEEP payload: seven fields, everything decodes. [pfw.sra:L119]</summary>
    private const string SevenFieldCase = "seven-field";

    /// <summary>
    /// A well-formed SHALLOW payload: two fields, so the deep branch never fires. [pfw.sra:L116]
    /// </summary>
    private const string TwoFieldCase = "two-field";

    /// <summary>
    /// A payload with ONE field, which fails the two-or-more test outright. [pfw.sra:L116]
    /// </summary>
    private const string SingleFieldCase = "single-field";

    /// <summary>
    /// Seven fields whose numeric fields are not numbers, so both parses degrade silently.
    /// [pfw.sra:L117, L123]
    /// </summary>
    private const string UnparseableNumberCase = "unparseable-number";

    /// <summary>
    /// An object name that fails the gate, so the decode is skipped entirely. [pfw.sra:L114]
    /// </summary>
    private const string GateFailingCase = "gate-failing";

    // ==============================================================================================
    //  MEMBER DATA (AAP 0.6.7)
    // ==============================================================================================

    /// <summary>
    /// Every shape of <c>info</c> a caller can hand to the payload builder, paired with the payload
    /// field-2 continuation each one produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of the matrix is universal quantification: there must be NO value of
    /// <c>info</c> for which the failure is not raised. The rows therefore reach past the two obvious
    /// cases into the shapes a defensive implementation might be tempted to special-case - a null, a
    /// string that is only whitespace, one that already contains the intra-field separator, one that
    /// contains the FIELD delimiter and so could corrupt the payload, and one long enough to look like
    /// a resource concern.
    /// </para>
    /// <para>
    /// <b>The null row is a real input, not a hypothetical.</b> The parameter is declared
    /// non-nullable, so a caller inside the nullable context cannot pass null by accident - but a
    /// caller outside one can, and the legacy parameter is a PowerScript string that the runtime can
    /// present as null. The production method carries no null guard and does not need one: its
    /// emptiness test is an inequality, so null takes the same branch a non-empty string takes and
    /// concatenates as empty. The row pins that a null neither returns nor degenerates into a
    /// <see cref="NullReferenceException"/>.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// The expected value in the third column is the opening of the failure's <c>Info</c> member, which
    /// is payload field 2 held as a single value [assert.srf:L35]. It is asserted there rather than
    /// against a field of the split payload on purpose: one row's info deliberately CONTAINS the field
    /// delimiter, so splitting the payload fragments field 2 and the two views disagree. Which one is
    /// correct is not a matter of taste - <c>Info</c> is the unsplit field, so it is the value that
    /// answers "did the info reach the failure intact". The fragmentation itself is pinned separately.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string?, string> InfoInputRows
    {
        get
        {
            TheoryData<string, string?, string> matrix = [];

            // The info-less arity's own argument [assert.srf:L91]: the emptiness test at :L25 is
            // satisfied, so nothing is appended.
            matrix.Add("empty", string.Empty, BareAssertionText);

            // The oracle's info string [w_test_assert.srw:L42]. One line feed, then the info verbatim
            // [assert.srf:L26].
            matrix.Add("oracle info", OracleInfo, BareAssertionText + IntraFieldSeparator + OracleInfo);

            // A single space is NOT empty, so it appends. A trimming implementation would treat it as
            // empty and this row would fail - which is the point of including it.
            matrix.Add("whitespace only", " ", BareAssertionText + IntraFieldSeparator + " ");

            // Info that already contains the intra-field separator. Field 2 is legitimately
            // multi-line [assert.srf:L26 and :L71 both append one], so this must pass through intact.
            matrix.Add(
                "info spanning lines",
                "first" + IntraFieldSeparator + "second",
                BareAssertionText + IntraFieldSeparator + "first" + IntraFieldSeparator + "second");

            // Info containing the FIELD delimiter. This one genuinely corrupts the field count on the
            // consumer's side, and it is included precisely because the failure must still be raised
            // regardless: the library's job is to throw, not to validate its own diagnostic text.
            matrix.Add(
                "info containing the field delimiter",
                "before" + PayloadFieldDelimiter + "after",
                BareAssertionText + IntraFieldSeparator + "before" + PayloadFieldDelimiter + "after");

            // A long info string. No length limit exists anywhere in the legacy - the byte-length
            // check that might have imposed one is commented out in the consumer chain - so there is
            // no truncation to preserve and none to introduce.
            string longInfo = new('x', 4096);
            matrix.Add("long info", longInfo, BareAssertionText + IntraFieldSeparator + longInfo);

            // NULL. Read the remarks above before changing this row. The expected continuation is a
            // line feed followed by NOTHING, because null concatenates as the empty string.
            matrix.Add("null", null, BareAssertionText + IntraFieldSeparator);

            return matrix;
        }
    }

    /// <summary>
    /// The frame count handed to the payload builder, and the payload shape it selects.
    /// [assert.srf:L37]
    /// </summary>
    /// <remarks>
    /// The gate is <c>if nCount &gt; 2</c>, so this is the boundary in both directions: the last count
    /// that still selects the shallow shape and the first that selects the deep one are adjacent rows.
    /// A port that used <c>&gt;=</c> would move the boundary by one and exactly one row would fail.
    /// </remarks>
    public static TheoryData<int, int> PayloadShapeRows =>
        new()
        {
            // No frames at all - the state a swallowed capture failure leaves behind
            // [assert.srf:L29-L32 leaves nCount at its zero default].
            { 0, ShallowFieldCount },

            // One frame: still short of the gate.
            { 1, ShallowFieldCount },

            // The boundary count that still FAILS the gate.
            { LegacyStackFrames.MaxShallowFrameCount, ShallowFieldCount },

            // The first count that PASSES it.
            { LegacyStackFrames.MinimumDeepFrameCount, DeepFieldCount },

            // Comfortably past it, so the deep shape is not an artefact of the boundary.
            { 4, DeepFieldCount },
            { 8, DeepFieldCount },
        };

    /// <summary>
    /// Every input class the consumer can be handed, with the decode outcome each produces - and, in
    /// the last column, the assertion this whole suite exists to make: it terminates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The final column is <see langword="true"/> in every row, and that uniformity is the claim
    /// rather than a redundancy. <c>HALT CLOSE</c> sits outside every conditional above it
    /// [pfw.sra:L141-L143], so there is no input class for which the legacy declines to terminate. The
    /// column is spelled out per row anyway so that a future contributor who believes they have found
    /// a case that should merely warn has to change a visible <see langword="true"/> to
    /// <see langword="false"/>, rather than quietly adding a row a loop skips.
    /// </para>
    /// <para>
    /// The three middle rows are the classes that FAIL TO DECODE. They are the sharpest form of the
    /// requirement: a port that terminated only on a successful decode would convert a structural
    /// fault into a silent continue, which is the exact regression this matrix forbids.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int, bool, bool, bool> TerminationInputRows =>
        new()
        {
            // label, expected segment count, number+text decoded, seven-field branch, terminates

            // A well-formed deep payload: the two-or-more test passes and the exactly-seven test
            // passes [pfw.sra:L116, L119].
            { SevenFieldCase, DeepFieldCount, true, true, true },

            // A well-formed shallow payload: the two-or-more test passes, the exactly-seven test does
            // not. The legacy reaches its terminate statement on this shape too.
            { TwoFieldCase, ShallowFieldCount, true, false, true },

            // ---------- THE THREE CLASSES THAT FAIL TO DECODE ----------

            // One field: the two-or-more test fails, so NOTHING is decoded - the error text is still
            // the whole undecoded payload - and it terminates anyway.
            { SingleFieldCase, 1, false, false, true },

            // Seven fields whose numeric fields are not numbers. The branches both fire, so this row
            // records `true` twice, but the two numeric VALUES degrade silently to zero. Asserted
            // separately below, because a silent degradation is a defect worth pinning on its own.
            { UnparseableNumberCase, DeepFieldCount, true, true, true },

            // An object name that fails the gate. The splitter is never called, so the segment count
            // keeps its zero default [pfw.sra:L114 skips :L115 entirely] - and it terminates anyway.
            { GateFailingCase, 0, false, false, true },
        };

    // ==============================================================================================
    //  SECTION 1 - THE LIBRARY HALF: A FAILURE ALWAYS THROWS AND NEVER RETURNS
    //  ----------------------------------------------------------------------------------------------
    //  Scope, stated so nobody looks for the other half here. There is NO terminator type in
    //  shared/PowerFramework.Shared.Diagnostics - its files are the project file, Assert.cs,
    //  AssertionFailure.cs, StackTraceProvider.cs and CallerInfo.cs, and none of them ends a process or
    //  can be configured to. That is correct, and this suite does not ask for one: the library's share
    //  of the posture is the UNCONDITIONAL THROW, and the terminating step is Section 2's subject.
    // ==============================================================================================

    /// <summary>
    /// Every <c>info</c> input raises the failure. There is no value for which the payload builder
    /// returns normally. [assert.srf:L83]
    /// </summary>
    [Theory]
    [MemberData(nameof(InfoInputRows))]
    public void TheFailureIsRaisedForEveryInfoInputAndNeverReturnsNormally(
        string caseLabel,
        string? info,
        string expectedInfoOpening)
    {
        // The sentinel is the whole experiment. If the production method ever returned - for this
        // input, on this branch - the line after the call would run and this flag would flip. It is
        // asserted false at the end, so a silent return fails loudly instead of passing quietly by
        // simply not throwing.
        bool executionContinuedPastTheFailure = false;

        AssertionFailure thrown = Assert.Throws<AssertionFailure>(() =>
        {
            // `info!` suppresses the null-argument diagnostic for the null row only; the parameter is
            // declared non-nullable and the production code deliberately carries no null guard.
            Assertions.AssertFailed(info!);

            // Deliberately unreachable, and the compiler agrees: AssertFailed is annotated
            // [DoesNotReturn], so this line exists purely as the runtime witness for that annotation.
            executionContinuedPastTheFailure = true;
        });

        Assert.False(
            executionContinuedPastTheFailure,
            $"AssertFailed returned normally for the '{caseLabel}' input instead of throwing.");

        // The failure carries the info, so the input reached it rather than being discarded on the way.
        // Asserted against Info - payload field 2 held unsplit [assert.srf:L35] - because one row's info
        // contains the field delimiter and would fragment a split view. StartsWith rather than Equal
        // because the deep arm appends a source-location suffix [assert.srf:L71], and a live call from a
        // test host always takes the deep arm.
        Assert.StartsWith(expectedInfoOpening, thrown.Info, StringComparison.Ordinal);

        // Field 1 is unaffected by any info at all: it is written before the info is consulted
        // [assert.srf:L22 precedes :L25] and contains no delimiter, so it survives every row.
        string[] fields = thrown.Message.Split(PayloadFieldDelimiter);

        Assert.Equal(AssertionNumberField, fields[0]);
    }

    /// <summary>
    /// Info containing the payload's own field delimiter fragments the payload, and the failure is still
    /// raised and still terminates. [assert.srf:L19, L26]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The payload builder joins its fields with
    /// CRLF and interpolates the caller's info into field 2 without escaping, sanitizing or rejecting
    /// anything [assert.srf:L26, L74-L78]. Info that itself contains CRLF therefore adds fields, and the
    /// consumer's field count - which tests for exactly two or exactly seven [pfw.sra:L116, L119] -
    /// lands on neither, so the payload decodes as though it were malformed.
    /// </para>
    /// <para>
    /// It is reproduced rather than fixed, and the reason is the whole subject of this suite: the
    /// library's contract is to RAISE the failure, not to validate its own diagnostic text. Adding
    /// escaping here would be a behaviour change, and rejecting the info would be far worse - it would
    /// turn a caller's formatting choice into a reason not to report a failure, which is the exact
    /// inversion of fail-fast. What matters, and what is asserted, is that the failure survives the
    /// corruption: it is raised, and when handled it still terminates.
    /// </para>
    /// </remarks>
    [Fact]
    public void InfoContainingTheFieldDelimiterFragmentsThePayloadAndStillTerminates()
    {
        string corruptingInfo = "before" + PayloadFieldDelimiter + "after";

        AssertionFailure thrown = Assert.Throws<AssertionFailure>(
            () => Assertions.AssertFailed(corruptingInfo));

        // The info reached the failure intact as a single value.
        Assert.StartsWith(
            BareAssertionText + IntraFieldSeparator + corruptingInfo,
            thrown.Info,
            StringComparison.Ordinal);

        // But the PAYLOAD now has more fields than either shape the consumer recognises. A live call
        // takes the deep arm, so the well-formed count would have been seven.
        string[] fields = thrown.Message.Split(PayloadFieldDelimiter);

        Assert.True(
            fields.Length > DeepFieldCount,
            $"Expected the embedded delimiter to add fields; got {fields.Length}.");

        // And it terminates anyway - a fragmented payload is just another undecodable one, and
        // pfw.sra:L143 does not consult the field count before terminating.
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(
            LegacySystemErrorState.FromAssertPayload(thrown.Message));

        Assert.False(outcome.Decoded.SevenFieldBranchTaken);
        Assert.Single(sink.Reports);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
    }

    /// <summary>
    /// The payload builder emits the shape the frame count selects - and the throw that follows is
    /// reached on BOTH shapes, because the method has no return instruction at all. [assert.srf:L37,
    /// L83]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read the file header's note on why the instruction stream is inspected. In short: a port that
    /// moved the throw inside the shape gate would still throw on every live call from a test host,
    /// because a test host's stack always passes the gate, so only the absence of a <c>ret</c> can
    /// distinguish the two.
    /// </para>
    /// <para>
    /// <b>The same limitation applies to line coverage, which is why the structural assertion is not
    /// redundant with it.</b> This theory drives both arms of the shape gate, and the coverage report
    /// confirms it - the gate reports full condition coverage. But the throw sits OUTSIDE the gate and
    /// is only ever reached by a live call, and a live call always builds the deep shape, so no coverage
    /// tool can attribute a hit on the throw to the shallow arm. Coverage can therefore show that both
    /// shapes are built and that the throw is reached; it cannot show that the throw is reached
    /// <i>from</i> both. The absence of a <c>ret</c> is what closes that gap, and it closes it for every
    /// input at once rather than one path at a time.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PayloadShapeRows))]
    public void BothPayloadShapesAreBuiltAndTheThrowIsReachedOnEachOfThem(
        int frameCount,
        int expectedFieldCount)
    {
        string[] callStack = LegacyStackFrames.WithDepth(frameCount);

        AssertionFailure failure = Assertions.BuildFailure(callStack, frameCount, OracleInfo);

        string[] fields = failure.Message.Split(PayloadFieldDelimiter);

        Assert.Equal(expectedFieldCount, fields.Length);
        Assert.Equal(AssertionNumberField, fields[0]);

        // The shape is only half the row. The other half is that the throw is not conditional on it:
        // the sole producer of this payload contains no `ret`, so whichever arm of the gate built the
        // payload, the next thing that happens is the throw.
        Assert.Equal(0, CountInstructions(PayloadProducer(), OpCodes.Ret));
        Assert.Equal(1, CountInstructions(PayloadProducer(), OpCodes.Throw));
    }

    /// <summary>
    /// The compiled payload producer has no return instruction; every guard has one. That contrast IS
    /// the fail-fast contract, expressed in the code rather than in a comment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The legacy is a
    /// <c>global subroutine</c> whose last statement is an unconditional <c>throw</c>
    /// [assert.srf:L83], with no conditional path that returns and with the surrounding try/catch
    /// commented out [assert.srf:L82, L84-L86]. The absence of a <c>ret</c> is what that shape
    /// compiles to, and it is asserted rather than assumed.
    /// </para>
    /// <para>
    /// The guards are the control case. Each has a legitimate non-firing path - the satisfied
    /// condition returns [assert.srf:L89, L95, L101, L107, L113, L119] - so each MUST contain a
    /// <c>ret</c>. If a future change made a guard unable to return, this test would catch that too,
    /// in the opposite direction.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePayloadProducerHasNoReturnInstructionWhileEveryGuardDoes()
    {
        MethodInfo producer = PayloadProducer();

        // The claim, stated at its strongest: not "it usually throws", but "there is no instruction in
        // this method that returns to the caller".
        Assert.DoesNotContain(
            InstructionsOf(producer),
            instruction => instruction.Code == OpCodes.Ret);
        Assert.Single(
            InstructionsOf(producer),
            instruction => instruction.Code == OpCodes.Throw);

        // The compile-time half of the same contract, so the two cannot drift apart.
        Assert.Single(producer.GetCustomAttributes<DoesNotReturnAttribute>(inherit: false));

        MethodInfo[] guards = GuardOverloads();

        Assert.Equal(LegacySubroutineCount - 1, guards.Length);

        Assert.All(
            guards,
            guard =>
            {
                Assert.Contains(
                    InstructionsOf(guard),
                    instruction => instruction.Code == OpCodes.Ret);

                // And no guard claims it cannot return, because every guard can.
                Assert.Empty(guard.GetCustomAttributes<DoesNotReturnAttribute>(inherit: false));
            });
    }

    /// <summary>
    /// A swallowed stack-capture failure loses the location fields and the failure is raised anyway.
    /// [assert.srf:L29-L32]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The legacy wraps the capture in
    /// <c>try ... catch(throwable ex1) end try</c> whose HANDLER BODY IS EMPTY and which never reads
    /// its caught variable [assert.srf:L31-L32]. A capture failure is therefore silent: the count stays
    /// at its zero default, the shape gate takes its shallow arm, and execution continues to the throw.
    /// It must not be "improved" by logging, rethrowing, recording a diagnostic or substituting a
    /// synthetic frame.
    /// </para>
    /// <para>
    /// <b>THE SWALLOW APPLIES TO THE CAPTURE, NEVER TO THE FAILURE. The two must not be confused.</b>
    /// What is discarded is the diagnostic detail - the window, the object, the event, the line number
    /// and the trace. What is NOT discarded, and cannot be, is the failure itself: it is raised on this
    /// path exactly as it is on the deep path. A reader who sees an empty catch block and concludes
    /// "assertion failures can be swallowed" has inverted the behaviour.
    /// </para>
    /// <para>
    /// <b>DOCUMENTED GAP (AAP 0.8.1).</b> The catch handler is not reachable through the public surface
    /// in the port, because the capture provider throws nothing - it answers an empty array and a zero
    /// count when it cannot capture, and handles per-frame degradation internally. So the LINE is
    /// unreachable and shows as uncovered, and this test does not manufacture a throwing double to
    /// reach it: doing so would be adding a production seam to satisfy a metric. What IS asserted is
    /// the ONLY observable consequence the swallow has - the exact state it leaves behind, an empty
    /// frame array with a count of zero - fed through the internal seam the library already exposed.
    /// Where the choice is between a partial implementation and a documented gap, the documented gap
    /// wins; this is that gap, recorded rather than papered over.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASwallowedCaptureFailureLosesTheLocationFieldsAndStillRaisesTheFailure()
    {
        // Exactly the state assert.srf:L17 and :L16 leave behind when the capture at :L30 throws: an
        // empty array local, and a count still at its zero default.
        string[] noFramesWereCaptured = [];
        const int theCountTheSwallowLeavesBehind = 0;

        AssertionFailure failure = Assertions.BuildFailure(
            noFramesWereCaptured,
            theCountTheSwallowLeavesBehind,
            OracleInfo);

        // A failure was still produced. Nothing about the capture going wrong prevented that.
        Assert.NotNull(failure);
        Assert.IsType<AssertionFailure>(failure, exactMatch: true);

        // The shallow shape, which is the observable footprint of the swallow.
        string[] fields = failure.Message.Split(PayloadFieldDelimiter);

        Assert.Equal(ShallowFieldCount, fields.Length);
        Assert.Equal(AssertionNumberField, fields[0]);
        Assert.Equal(BareAssertionText + IntraFieldSeparator + OracleInfo, fields[1]);

        // The diagnostic detail is gone - this is the LOSS the swallow causes, and it is preserved.
        Assert.Equal(string.Empty, failure.WindowMenu);
        Assert.Equal(string.Empty, failure.Object);
        Assert.Equal(string.Empty, failure.ObjectEvent);
        Assert.Equal(0L, failure.Line);
        Assert.Equal(string.Empty, failure.StackTraceInfo);
        Assert.Empty(failure.StackTrace);

        // No location suffix, because the suffix is appended inside the deep arm only
        // [assert.srf:L71].
        Assert.DoesNotContain("at ", failure.Info, StringComparison.Ordinal);

        // And the failure is not swallowed: the throw that follows this payload is unconditional, so it
        // is reached from the shallow arm exactly as from the deep one.
        Assert.Equal(0, CountInstructions(PayloadProducer(), OpCodes.Ret));

        // The same shallow payload, handed to the consumer, still terminates. Stated here as well as
        // in Section 2 because the whole point of this test is that a capture failure changes nothing
        // about the posture end to end.
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        consumer.HandleSystemError(LegacySystemErrorState.FromAssertPayload(failure.Message));

        Assert.Single(sink.Reports);
    }

    /// <summary>
    /// Nothing on the published surface offers a non-throwing way to signal or to poll for a failure.
    /// [assert.srf:L7-L13]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The legacy prototype list declares seven
    /// <c>global subroutine</c> members and not one <c>global function</c>. A subroutine cannot return
    /// a value, so in PowerScript the fail-fast contract was enforced by the declaration itself: there
    /// was no syntax available for asking "did it fail" without the failure being raised. That property
    /// is reproduced here rather than relaxed into a friendlier API.
    /// </para>
    /// <para>
    /// The check is deliberately wider than return types. A non-throwing escape hatch could be
    /// smuggled in as a <c>Try</c>-prefixed overload, as an <c>out</c> or <c>ref</c> parameter carrying
    /// a code, as a property or field a caller polls after the fact, or as an event a caller subscribes
    /// to instead of catching. Every one of those routes is closed below, because closing only the
    /// return type would leave the other four open.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedSurfaceOffersNoNonThrowingWayToSignalOrPollAFailure()
    {
        MethodInfo[] published = DeclaredPublicMethods();

        // Seven members, matching the legacy's seven subroutine declarations exactly.
        Assert.Equal(LegacySubroutineCount, published.Length);

        // ROUTE 1 - a return value. Every member is a subroutine.
        Assert.All(published, method => Assert.Equal(typeof(void), method.ReturnType));

        // ROUTE 2 - a Try-prefixed alternative, under any signature.
        Assert.DoesNotContain(
            published,
            method => method.Name.StartsWith("Try", StringComparison.Ordinal));

        // Nor any other spelling of the same idea. These are the names a well-meaning contributor
        // reaches for when they want to soften the posture.
        string[] softeningNames =
        [
            "TryAssert",
            "TryAssertFailed",
            "AssertOrDefault",
            "Check",
            "Validate",
            "IsSatisfied",
            "Verify",
            "Warn",
            "Report",
        ];

        Assert.All(
            softeningNames,
            name => Assert.DoesNotContain(published, method => method.Name == name));

        // ROUTE 3 - an out or ref parameter carrying a code or a failure back to the caller.
        Assert.All(
            published,
            method => Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.IsOut || parameter.ParameterType.IsByRef));

        // No member accepts or produces the failure type as data either, so the failure can only ever
        // arrive by being thrown.
        Assert.All(
            published,
            method =>
            {
                Assert.NotEqual(typeof(AssertionFailure), method.ReturnType);
                Assert.DoesNotContain(
                    method.GetParameters(),
                    parameter => parameter.ParameterType == typeof(AssertionFailure));
            });

        // ROUTE 4 - a property or field to poll after the call. There are none of any accessibility,
        // so the type is stateless by construction and there is nowhere for a "last failure" to sit.
        const BindingFlags everyMember = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        Assert.Empty(typeof(Assertions).GetProperties(everyMember));
        Assert.Empty(typeof(Assertions).GetFields(everyMember));

        // ROUTE 5 - an event to subscribe to instead of catching.
        Assert.Empty(typeof(Assertions).GetEvents(everyMember));

        // And no nested type to hide any of the above in.
        Assert.Empty(typeof(Assertions).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
    }

    /// <summary>
    /// No member of the library writes a failure to a console, a trace listener, a debug channel or a
    /// logger instead of throwing. There is nowhere in this library for a warning to go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The legacy payload builder reports
    /// nothing and displays nothing; the ONLY thing it does with a failure is throw it
    /// [assert.srf:L83]. Reporting belongs to the consumer, which is Section 2's subject. A port that
    /// logged the failure here - even in addition to throwing - would be warn-and-continue wearing a
    /// throw as a disguise, because the log line is what a future contributor would then feel free to
    /// keep while relaxing the throw.
    /// </para>
    /// <para>
    /// The assertion walks the compiled call sites of the whole assembly and resolves each one to its
    /// declaring type. <b>The resolution failure count is asserted to be zero</b>, which matters more
    /// than it looks: a scan that silently failed to resolve anything would report "no banned calls"
    /// and pass forever. The scan is also asserted to have found a plausible number of call sites, for
    /// the same reason.
    /// </para>
    /// <para>
    /// <c>System.Diagnostics.StackTrace</c> is deliberately NOT on the banned list. It is the sanctioned
    /// substitute for the legacy's closed-binary stack-capture binding, and banning it by a careless
    /// substring match on "Trace" is the obvious way to get this test wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMemberOfTheLibraryWritesAFailureToAConsoleTraceOrLoggerInsteadOfThrowing()
    {
        CallSiteScan scan = ScanCallSites(typeof(Assertions).Assembly.GetTypes());

        // The scan actually ran and actually resolved. Without these two, everything below is vacuous.
        Assert.Equal(0, scan.UnresolvedCount);
        Assert.NotEmpty(scan.Targets);

        Assert.All(
            scan.Targets,
            target => Assert.False(
                IsDiagnosticSink(target),
                $"{target} writes a diagnostic instead of - or as well as - throwing."));

        // The assertion surface specifically, so the claim is not diluted by being assembly-wide.
        CallSiteScan assertionsOnly = ScanCallSites([typeof(Assertions)]);

        Assert.Equal(0, assertionsOnly.UnresolvedCount);
        Assert.NotEmpty(assertionsOnly.Targets);
        Assert.All(assertionsOnly.Targets, target => Assert.False(IsDiagnosticSink(target)));

        // And the library ends no process either. The terminating step is the HOST's, never a shared
        // library's: pfw.sra:L143 lives in the application object, not in assert.srf.
        Assert.All(
            scan.Targets,
            target => Assert.False(
                IsProcessTerminator(target),
                $"{target} ends the process from inside a shared library."));
    }

    // ==============================================================================================
    //  SECTION 2 - THE CONSUMER HALF: A HANDLED SYSTEM ERROR ALWAYS TERMINATES
    //  ----------------------------------------------------------------------------------------------
    //  Observed through LegacySystemErrorConsumer, this project's replica of pfw.sra's systemerror
    //  event, with RecordingTerminationSink injected as the termination seam. The production consumer -
    //  Gateway's SystemErrorHandler - is NOT referenced, for the C-A reason stated in the file header:
    //  the only coupling permitted across a service boundary is the published contracts project, so a
    //  shared library's test project cannot reference a services/** project, and the test project file
    //  carries exactly one ProjectReference to prove it.
    //
    //  The seam records; it does not terminate. `HALT CLOSE` [pfw.sra:L143] maps to process termination
    //  in the port, and a replica that really terminated would take this test host down with it.
    // ==============================================================================================

    /// <summary>
    /// A full seven-field payload terminates exactly once, and the report handed to the seam is the
    /// fully assembled legacy report. [pfw.sra:L119-L143]
    /// </summary>
    [Fact]
    public void AFullSevenFieldPayloadTerminatesExactlyOnceWithTheFullyAssembledReport()
    {
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        AssertionFailure failure = DeepFailure();

        // Precondition, asserted rather than assumed: this really is the deep shape, so the seven-field
        // branch below is genuinely exercised.
        Assert.Equal(DeepFieldCount, failure.Message.Split(PayloadFieldDelimiter).Length);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(
            LegacySystemErrorState.FromAssertPayload(failure.Message));

        // EXACTLY ONE termination. Not zero, and not repeated.
        Assert.Single(sink.Reports);
        Assert.Equal(1, sink.InvocationCount);
        Assert.Single(sink.ExitCodes);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);

        // The deep branch fired, so every field was decoded.
        Assert.True(outcome.Decoded.NumberAndTextDecoded);
        Assert.True(outcome.Decoded.SevenFieldBranchTaken);
        Assert.Equal(DeepFieldCount, outcome.Decoded.SegmentCount);

        // The report the seam received is the report the handler produced - the same string the legacy
        // would have passed to its dialog [pfw.sra:L141].
        Assert.Equal(outcome.Report, sink.LastReport);

        // FULLY ASSEMBLED, checked against independently spelled labels rather than by re-running the
        // assembler. Six of the seven lines are unconditional and the seventh is present because a deep
        // payload always carries a non-empty trace [pfw.sra:L129-L139].
        string report = sink.LastReport!;

        Assert.StartsWith(ReportOpeningLine, report, StringComparison.Ordinal);
        Assert.Contains(ReportMessageLabel + outcome.Decoded.Text, report, StringComparison.Ordinal);
        Assert.Contains(ReportObjectLabel + outcome.Decoded.Object, report, StringComparison.Ordinal);
        Assert.Contains(
            ReportFunctionLabel + outcome.Decoded.ObjectEvent,
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            ReportLineLabel + outcome.Decoded.Line.ToString(CultureInfo.InvariantCulture),
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            ReportStackTraceLabel + outcome.Decoded.StackTrace,
            report,
            StringComparison.Ordinal);

        // The window line appears only when the window and the object differ [pfw.sra:L131-L132]. The
        // deep payload's caller frame carries a declaring scope, so the two DO differ here and the line
        // is present - which is what makes this the shape that exercises all seven lines.
        Assert.NotEqual(outcome.Decoded.WindowMenu, outcome.Decoded.Object);
        Assert.Contains(ReportWindowLabel + outcome.Decoded.WindowMenu, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// A two-field payload also terminates. [pfw.sra:L116, L143]
    /// </summary>
    /// <remarks>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> The legacy reaches its terminate
    /// statement on BOTH payload shapes. The exactly-seven branch is skipped on this shape
    /// [pfw.sra:L119], so five of the seven decoded values keep their incoming defaults and the report
    /// is correspondingly thin - and none of that reduces the outcome from "terminate" to "warn".
    /// Treating a shallow payload as merely informational would be exactly the graceful degradation
    /// AAP 0.1.4 forbids.
    /// </remarks>
    [Fact]
    public void ATwoFieldPayloadAlsoTerminates()
    {
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        AssertionFailure failure = ShallowFailure();

        Assert.Equal(ShallowFieldCount, failure.Message.Split(PayloadFieldDelimiter).Length);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(
            LegacySystemErrorState.FromAssertPayload(failure.Message));

        Assert.Single(sink.Reports);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
        Assert.Equal(outcome.Report, sink.LastReport);

        // The shallow shape: the two-or-more test passed, the exactly-seven test did not.
        Assert.True(outcome.Decoded.NumberAndTextDecoded);
        Assert.False(outcome.Decoded.SevenFieldBranchTaken);
        Assert.Equal(ShallowFieldCount, outcome.Decoded.SegmentCount);

        // No trace was decoded, so the call-stack line is suppressed [pfw.sra:L137]. The report is
        // thinner and the termination is identical.
        Assert.Equal(string.Empty, outcome.Decoded.StackTrace);
        Assert.DoesNotContain(ReportStackTraceLabel, sink.LastReport!, StringComparison.Ordinal);
        Assert.StartsWith(ReportOpeningLine, sink.LastReport!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A payload that FAILS TO DECODE still terminates - one field, an unparseable number, or an object
    /// name that fails the gate. [pfw.sra:L114, L116, L141-L143]
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B). This is the sharpest form of the fail-fast
    /// requirement.</b> The legacy's terminate statement is not inside any conditional: pfw.sra:L143
    /// sits outside the gate at :L114, outside the two-or-more test at :L116 and outside the
    /// exactly-seven test at :L119. A port that terminated only on a successful decode would convert a
    /// structural fault - a malformed payload is itself a structural fault - into a silent continue,
    /// which is the single most damaging regression available in this area: the process would carry on
    /// after the exact class of failure the mechanism exists to stop it for.
    /// </para>
    /// <para>
    /// The undecodable classes are also where a defensive implementation is most tempted to be helpful.
    /// Note what is NOT asserted here, because the legacy does none of it: no repair of the payload, no
    /// substituted default, no second decode attempt, and no diagnostic about the malformation.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(SingleFieldCase)]
    [InlineData(UnparseableNumberCase)]
    [InlineData(GateFailingCase)]
    public void APayloadThatFailsToDecodeStillTerminates(string caseLabel)
    {
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(StateFor(caseLabel));

        Assert.Single(sink.Reports);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
        Assert.Equal(outcome.Report, sink.LastReport);

        // A report was still assembled and handed over: the six unconditional lines cannot be empty, so
        // a non-empty report is itself evidence the terminating step received something real.
        Assert.NotEmpty(sink.LastReport!);
        Assert.StartsWith(ReportOpeningLine, sink.LastReport!, StringComparison.Ordinal);
    }

    /// <summary>
    /// There is no input class for which the handler returns without terminating, and the termination
    /// count is exactly one per handled error - never zero, never repeated. [pfw.sra:L141-L143]
    /// </summary>
    /// <remarks>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> This is the universal-quantification form
    /// of the claim the other tests in this section make case by case. Every row of
    /// <see cref="TerminationInputRows"/> terminates, which is what it means for the terminating
    /// statement to sit outside every conditional above it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TerminationInputRows))]
    public void NoInputClassProducesAnOutcomeThatReturnsWithoutTerminating(
        string caseLabel,
        int expectedSegmentCount,
        bool expectedNumberAndTextDecoded,
        bool expectedSevenFieldBranchTaken,
        bool expectedToTerminate)
    {
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        // Nothing has happened yet, so the null here is meaningful: it distinguishes "not invoked" from
        // "invoked with an empty report", which cannot occur.
        Assert.Equal(0, sink.InvocationCount);
        Assert.Null(sink.LastReport);
        Assert.Null(sink.LastExitCode);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(StateFor(caseLabel));

        // The decode outcome this row expects, so a row cannot pass for the wrong reason - a case that
        // silently stopped being undecodable would still terminate and would otherwise slip through.
        Assert.Equal(expectedSegmentCount, outcome.Decoded.SegmentCount);
        Assert.Equal(expectedNumberAndTextDecoded, outcome.Decoded.NumberAndTextDecoded);
        Assert.Equal(expectedSevenFieldBranchTaken, outcome.Decoded.SevenFieldBranchTaken);

        // THE CLAIM. Exactly one termination, on every row, whatever the decode did.
        Assert.True(
            expectedToTerminate,
            $"The '{caseLabel}' row claims a handled system error may return without terminating; "
                + "pfw.sra:L143 sits outside every conditional, so no such row is legitimate.");

        Assert.Single(sink.Reports);
        Assert.Equal(1, sink.InvocationCount);
        Assert.Single(sink.ExitCodes);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
        Assert.Equal(outcome.Report, sink.LastReport);

        // NEVER REPEATED. A second error handled by the same consumer adds exactly one more, so the
        // count tracks handled errors one for one - a replica that terminated twice for one error, or
        // that batched terminations, would fail here rather than in some later integration.
        consumer.HandleSystemError(StateFor(caseLabel));

        Assert.Equal(2, sink.InvocationCount);
        Assert.Equal(2, sink.ExitCodes.Count);
        Assert.Equal(sink.Reports[0], sink.Reports[1]);

        // And the recorder is not accumulating state that would mask a later zero.
        sink.Reset();

        Assert.Equal(0, sink.InvocationCount);
        Assert.Null(sink.LastReport);
    }

    /// <summary>
    /// The two numeric fields degrade silently to zero when they are not numbers, and the failure still
    /// terminates. [pfw.sra:L117, L123]
    /// </summary>
    /// <remarks>
    /// <b>PRESERVED LEGACY BEHAVIOUR, DELIBERATELY (C-B).</b> PowerScript's <c>Long</c> answers zero for
    /// text it cannot parse and raises nothing, so a corrupt number field is absorbed rather than
    /// reported. That silent degradation is reproduced: the port must not throw, must not substitute a
    /// sentinel and must not refuse the payload. Note the direction of the risk - zero is a plausible
    /// line number and a plausible error code, so this is a defect that hides rather than announces
    /// itself, which is exactly why it is pinned explicitly instead of being left implicit in the
    /// matrix row.
    /// </remarks>
    [Fact]
    public void UnparseableNumericFieldsDegradeToZeroAndStillTerminate()
    {
        RecordingTerminationSink sink = new();
        LegacySystemErrorConsumer consumer = new(sink);

        LegacySystemErrorOutcome outcome = consumer.HandleSystemError(StateFor(UnparseableNumberCase));

        // Both branches fired - the payload is structurally a deep one - yet both numeric values are
        // zero, because both came through the tolerant parse.
        Assert.True(outcome.Decoded.NumberAndTextDecoded);
        Assert.True(outcome.Decoded.SevenFieldBranchTaken);
        Assert.Equal(0L, outcome.Decoded.Number);
        Assert.Equal(0L, outcome.Decoded.Line);

        // The zero reaches the report as a plain zero, with no marker distinguishing it from a real
        // line number of zero.
        Assert.Contains(ReportLineLabel + "0", sink.LastReport!, StringComparison.Ordinal);

        // And it terminates.
        Assert.Single(sink.Reports);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
    }

    // ==============================================================================================
    //  SECTION 3 - THE PRODUCTION PATH MUST NOT BE WEAKENED
    //  ----------------------------------------------------------------------------------------------
    //  Two claims, both about this suite rather than about the code under test, and both worth asserting
    //  because a future contributor's most likely "improvement" is to open the library up.
    // ==============================================================================================

    /// <summary>
    /// The observability this suite relies on came entirely from the test double and from one pre-existing
    /// internal seam. No production member was made public, virtual, overridable or configurable for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DESIGN INTENT, RECORDED SO IT IS NOT "IMPROVED" AWAY.</b> The termination seam lives in the
    /// test double - <see cref="ILegacyTerminationSink"/> and
    /// <see cref="RecordingTerminationSink"/> - and NOT in
    /// <c>shared/PowerFramework.Shared.Diagnostics</c>. Making the real fail-fast path injectable so a
    /// test could watch it would be weakening production code to make it testable, and would hand a
    /// later contributor a supported way to substitute a non-terminating implementation in production.
    /// The library's own contract stays exactly as its folder specified it: the payload builder always
    /// throws and never returns.
    /// </para>
    /// <para>
    /// The assertions below are structural, and they are the enforceable form of that intent. The
    /// library exposes precisely seven public members, all of them subroutines; it declares exactly one
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>, which pre-dates this
    /// suite and exists for the payload builder that the frame-shape tests consume; and it publishes no
    /// virtual, abstract or overridable member on the assertion surface that a test double could stand
    /// in for. If any of that changes, this test fails, which is the intended outcome.
    /// </para>
    /// </remarks>
    [Fact]
    public void ObservabilityCameFromTheTestDoubleAndNoProductionMemberWasOpenedUpForIt()
    {
        Assembly library = typeof(Assertions).Assembly;

        // EXACTLY ONE friend assembly, and it is this one. A suite that needed more access would have
        // had to add another, and adding another is the change this assertion is here to notice.
        string[] friends =
        [
            .. library
                .GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName),
        ];

        Assert.Single(friends);
        Assert.Equal(typeof(FailFastTests).Assembly.GetName().Name, friends[0]);

        // Nothing on the assertion surface is overridable, so nothing there can be replaced by a
        // subclass or a mock. A static class cannot be derived from in the first place; this states the
        // stronger member-level property so that converting it to an instance type would not silently
        // open a door.
        Assert.True(typeof(Assertions).IsAbstract && typeof(Assertions).IsSealed);

        Assert.All(
            DeclaredPublicMethods(),
            method =>
            {
                Assert.False(method.IsVirtual);
                Assert.False(method.IsAbstract);
                Assert.True(method.IsStatic);
            });

        // No configuration flag, switch or feature gate: the type holds no state of any accessibility,
        // so there is nothing to set that could change whether a failure throws.
        const BindingFlags everyMember = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        Assert.Empty(typeof(Assertions).GetFields(everyMember));
        Assert.Empty(typeof(Assertions).GetProperties(everyMember));

        // The seam this suite DOES use is the pre-existing payload builder, and it is internal rather
        // than public - so consuming it widened nothing.
        MethodInfo seam = PayloadBuilder();

        Assert.True(seam.IsAssembly);
        Assert.False(seam.IsPublic);
    }

    /// <summary>
    /// Nothing in this suite can end the test host - not directly, and not through the double.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy's <c>HALT CLOSE</c> [pfw.sra:L143] maps to process termination in the port (C-K), and
    /// a replica that reproduced it literally would kill the runner and take the whole run down with
    /// it. So the terminating step stops at an injected seam that records, and this test asserts that
    /// the seam really is where it stops: no call to a process-ending API exists anywhere in this
    /// suite's own types, in the consumer replica, or in the recording implementation.
    /// </para>
    /// <para>
    /// Nested types are included in the scan on purpose. A lambda compiles into a nested closure type
    /// rather than into the method that wrote it, so a scan of declared methods alone would look at
    /// none of the code inside the many lambdas above.
    /// </para>
    /// <para>
    /// The suite reaching its end is corroborating evidence of the same claim - a truncated run or a
    /// missing coverage report would mean something really did terminate - but corroboration is not
    /// proof, which is why the structural scan is here as well.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingInThisSuiteOrItsDoubleCanEndTheTestHost()
    {
        Type[] scanned =
        [
            typeof(FailFastTests),
            typeof(LegacySystemErrorConsumer),
            typeof(RecordingTerminationSink),
            typeof(ILegacyTerminationSink),
            typeof(LegacyStackFrames),
        ];

        CallSiteScan scan = ScanCallSites(scanned);

        // The scan resolved everything it found, so a clean result below means "nothing banned" rather
        // than "nothing inspected".
        Assert.Equal(0, scan.UnresolvedCount);
        Assert.NotEmpty(scan.Targets);

        Assert.All(
            scan.Targets,
            target => Assert.False(
                IsProcessTerminator(target),
                $"{target} can end the test host. The terminating step must stop at the injected seam."));

        // The seam is one void method, so there is no second, real terminating path hiding behind it.
        MethodInfo[] seamMembers = typeof(ILegacyTerminationSink).GetMethods();

        Assert.Single(seamMembers);
        Assert.Equal(typeof(void), seamMembers[0].ReturnType);

        // And the recording implementation only records: invoked directly through the seam, it returns
        // normally and the invocation is observable afterwards. The interface member is implemented
        // explicitly, hence the cast.
        RecordingTerminationSink sink = new();
        ILegacyTerminationSink seam = sink;

        seam.Terminate("report", LegacySystemErrorConsumer.HaltCloseExitCode);

        Assert.Single(sink.Reports);
        Assert.Equal("report", sink.LastReport);
        Assert.Equal(LegacySystemErrorConsumer.HaltCloseExitCode, sink.LastExitCode);
    }

    // ==============================================================================================
    //  HELPERS - THE ASSERTION SURFACE
    // ==============================================================================================

    /// <summary>
    /// The sole producer of the assertion payload, which is the member whose fail-fast contract Section
    /// 1 pins. [assert.srf:L16-L87]
    /// </summary>
    private static MethodInfo PayloadProducer()
    {
        MethodInfo? producer = typeof(Assertions).GetMethod(
            nameof(Assertions.AssertFailed),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string)]);

        Assert.NotNull(producer);

        return producer;
    }

    /// <summary>
    /// The internal payload-builder seam the shape theory drives directly. [assert.srf:L21-L80]
    /// </summary>
    /// <remarks>
    /// Reached through the ONE <c>InternalsVisibleTo</c> the library already declared for its own frame
    /// parsing. This suite consumes that grant as it found it and did not ask for it.
    /// </remarks>
    private static MethodInfo PayloadBuilder()
    {
        MethodInfo? builder = typeof(Assertions).GetMethod(
            "BuildFailure",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(builder);

        return builder;
    }

    /// <summary>
    /// The six guard overloads, which are every public member except the payload producer.
    /// [assert.srf:L8-L13]
    /// </summary>
    private static MethodInfo[] GuardOverloads()
    {
        return
        [
            .. DeclaredPublicMethods()
                .Where(method => method.Name != nameof(Assertions.AssertFailed)),
        ];
    }

    /// <summary>
    /// Every public member declared on the assertion surface, in a stable order.
    /// </summary>
    /// <remarks>
    /// <c>DeclaredOnly</c> matters: without it the inherited <see cref="object"/> members would be
    /// counted and the seven-member assertion would be meaningless. Ordered by name and then by
    /// parameter count so a failure message is reproducible rather than dependent on reflection order.
    /// </remarks>
    private static MethodInfo[] DeclaredPublicMethods()
    {
        return
        [
            .. typeof(Assertions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.GetParameters().Length),
        ];
    }

    // ==============================================================================================
    //  HELPERS - THE PAYLOAD AND CONSUMER FIXTURES
    // ==============================================================================================

    /// <summary>
    /// A well-formed DEEP failure: seven payload fields, built from the oracle's own frame chain.
    /// </summary>
    private static AssertionFailure DeepFailure()
    {
        string[] callStack = LegacyStackFrames.Deep();

        return Assertions.BuildFailure(callStack, callStack.Length, OracleInfo);
    }

    /// <summary>
    /// A well-formed SHALLOW failure: two payload fields, from a frame count that fails the shape gate.
    /// </summary>
    private static AssertionFailure ShallowFailure()
    {
        string[] callStack = LegacyStackFrames.Shallow(LegacyStackFrames.MaxShallowFrameCount);

        return Assertions.BuildFailure(callStack, callStack.Length, OracleInfo);
    }

    /// <summary>
    /// Builds the incoming system-error state for one of the five input classes.
    /// </summary>
    /// <param name="caseLabel">One of the five case-key constants.</param>
    /// <returns>The state to hand to the consumer.</returns>
    /// <remarks>
    /// <para>
    /// A single factory rather than five, so the theory rows carry a label and the construction of each
    /// class lives in exactly one place. Every payload is assembled from the fixtures and the delimiter
    /// constant; none is read from a file, and nothing under <c>ws_objects/**</c> is opened (C-C).
    /// </para>
    /// <para>
    /// The default arm throws rather than returning a fallback. A mistyped label must fail the row
    /// loudly; silently substituting a well-formed payload would turn an undecodable case into a
    /// decodable one and the theory would pass while testing nothing.
    /// </para>
    /// </remarks>
    private static LegacySystemErrorState StateFor(string caseLabel)
    {
        switch (caseLabel)
        {
            case SevenFieldCase:
                return LegacySystemErrorState.FromAssertPayload(DeepFailure().Message);

            case TwoFieldCase:
                return LegacySystemErrorState.FromAssertPayload(ShallowFailure().Message);

            case SingleFieldCase:
                // ONE field: no delimiter anywhere, so the splitter answers 1 and the two-or-more test
                // at pfw.sra:L116 fails. Nothing is decoded and the error text stays as it arrived.
                return LegacySystemErrorState.FromAssertPayload(AssertionNumberField);

            case UnparseableNumberCase:
                // SEVEN fields, so both branches fire, but fields 1 and 6 are not numbers. PowerScript
                // `Long` answers zero for text it cannot parse [pfw.sra:L117, L123], so both values
                // degrade silently. Field 7 is non-empty because an empty last field would be dropped
                // by the splitter's trailing-empty defect and the count would be six, not seven.
                return LegacySystemErrorState.FromAssertPayload(
                    string.Join(
                        PayloadFieldDelimiter,
                        "not-a-number",
                        BareAssertionText,
                        "w_test_assert",
                        "cb_1",
                        "clicked",
                        "not-a-line-number",
                        LegacyStackFrames.ExpectedCallerFrame));

            case GateFailingCase:
                // The gate at pfw.sra:L114 compares the error object name with `"assert"` using
                // PowerScript `=`, which is case-sensitive for strings. A capitalised name therefore
                // FAILS the gate, the splitter is never called, and the payload is left entirely
                // undecoded - and the terminate statement is still reached.
                return new LegacySystemErrorState(
                    0L,
                    DeepFailure().Message,
                    string.Empty,
                    "Assert",
                    string.Empty,
                    0L);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(caseLabel),
                    caseLabel,
                    "Unknown termination input class.");
        }
    }

    // ==============================================================================================
    //  HELPERS - THE INSTRUCTION-STREAM READER
    //  ----------------------------------------------------------------------------------------------
    //  WHY THIS EXISTS. Two properties asserted above are properties OF THE CODE rather than of any one
    //  execution: "there is no path on which the payload producer returns", and "no member here calls a
    //  process-ending or diagnostic-writing API". Neither can be established by running the code -
    //  running it only ever exercises one path - so the compiled instruction stream is read instead.
    //
    //  WHY A FULL WALK RATHER THAN A BYTE SEARCH. Searching the raw bytes for an opcode value would
    //  produce false positives, because an opcode's value can appear inside a preceding instruction's
    //  OPERAND. The walk below therefore decodes each instruction's operand width and steps over it,
    //  which is the only way to know that a byte is an opcode and not data.
    //
    //  THIS READER IS INERT. It loads no assembly, opens no file and executes nothing it inspects. It
    //  reads metadata already loaded into this process by the ordinary project reference.
    // ==============================================================================================

    /// <summary>
    /// Every IL opcode in the runtime, indexed by its numeric value.
    /// </summary>
    /// <remarks>
    /// Built once and reused. It is immutable after construction and only ever read, so sharing it
    /// across the parallel test classes in this assembly is safe.
    /// </remarks>
    private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeTable();

    /// <summary>
    /// The declaring types whose every member is a diagnostic sink: writing a failure to one of these
    /// instead of throwing it would be warn-and-continue.
    /// </summary>
    /// <remarks>
    /// <c>System.Diagnostics.StackTrace</c> and <c>System.Diagnostics.StackFrame</c> are deliberately
    /// ABSENT. They are the sanctioned substitutes for the legacy's closed-binary stack-capture binding,
    /// so a careless substring match on "Trace" would ban exactly the API the port is required to use.
    /// Matching is on the full type name, never on a fragment.
    /// </remarks>
    private static readonly string[] DiagnosticSinkTypes =
    [
        "System.Console",
        "System.Diagnostics.Trace",
        "System.Diagnostics.Debug",
        "System.Diagnostics.Debugger",
        "System.Diagnostics.TraceSource",
        "System.Diagnostics.TraceListener",
        "System.Diagnostics.EventLog",
    ];

    /// <summary>
    /// Namespace prefixes whose every member is a logging sink.
    /// </summary>
    private static readonly string[] LoggingNamespacePrefixes =
    [
        "Microsoft.Extensions.Logging.",
        "Serilog.",
        "NLog.",
        "OpenTelemetry.",
    ];

    /// <summary>
    /// The exact members that end a process or a thread, spelled as declaring type plus member name.
    /// </summary>
    /// <remarks>
    /// The legacy's terminating statement is <c>HALT CLOSE</c> [pfw.sra:L143] and this is the set of
    /// managed equivalents. None may appear in a shared library, in this suite or in its double: the
    /// terminating step belongs to the host, and inside a test assembly reaching one of these would
    /// destroy the run.
    /// </remarks>
    private static readonly string[] ProcessTerminatingMembers =
    [
        "System.Environment::Exit",
        "System.Environment::FailFast",
        "System.Environment::set_ExitCode",
        "System.Diagnostics.Process::Kill",
        "System.Diagnostics.Process::CloseMainWindow",
        "System.Threading.Thread::Abort",
        "System.Threading.Thread::Interrupt",
        "System.Runtime.InteropServices.NativeLibrary::Free",
    ];

    /// <summary>
    /// One decoded IL instruction: its opcode and, when it carries one, its metadata token.
    /// </summary>
    /// <param name="Code">The decoded opcode.</param>
    /// <param name="MetadataToken">
    /// The token operand for a method or member reference, or zero for every other operand kind.
    /// </param>
    private readonly record struct IlInstruction(OpCode Code, int MetadataToken);

    /// <summary>
    /// The result of walking a set of types' call sites.
    /// </summary>
    /// <param name="Targets">
    /// Every distinct callee, as <c>DeclaringType::MemberName</c>, sorted for reproducible failures.
    /// </param>
    /// <param name="UnresolvedCount">
    /// How many call tokens could not be resolved. <b>Asserted to be zero by every caller</b>, because a
    /// scan that resolved nothing would report a clean result for the wrong reason.
    /// </param>
    private readonly record struct CallSiteScan(IReadOnlyList<string> Targets, int UnresolvedCount);

    /// <summary>
    /// Indexes every opcode the runtime defines by its numeric value.
    /// </summary>
    private static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        Dictionary<short, OpCode> table = [];

        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code)
            {
                table[code.Value] = code;
            }
        }

        return table;
    }

    /// <summary>
    /// Decodes <paramref name="method"/>'s instruction stream.
    /// </summary>
    /// <param name="method">The method to decode. May be abstract or extern, in which case the result is
    /// empty.</param>
    /// <returns>The decoded instructions in order.</returns>
    /// <exception cref="NotSupportedException">
    /// An operand kind the walker does not know how to step over was encountered. Thrown rather than
    /// skipped: guessing an operand width would desynchronise the walk and every instruction after it
    /// would be misread, which would make a clean scan meaningless.
    /// </exception>
    private static IReadOnlyList<IlInstruction> InstructionsOf(MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        byte[]? il = body?.GetILAsByteArray();

        if (il is null || il.Length == 0)
        {
            return [];
        }

        List<IlInstruction> instructions = [];
        int offset = 0;

        while (offset < il.Length)
        {
            // A two-byte opcode is introduced by 0xFE; everything else is one byte.
            short value = il[offset];

            if (value == 0xFE && offset + 1 < il.Length)
            {
                value = (short)(0xFE00 | il[offset + 1]);
                offset += 2;
            }
            else
            {
                offset += 1;
            }

            Assert.True(
                OpCodesByValue.TryGetValue(value, out OpCode code),
                $"Unrecognised IL opcode 0x{value:X4} in {method.DeclaringType?.FullName}::{method.Name}.");

            int operandSize = OperandSize(code, il, offset);
            int token = code.OperandType is OperandType.InlineMethod or OperandType.InlineTok
                ? BitConverter.ToInt32(il, offset)
                : 0;

            instructions.Add(new IlInstruction(code, token));
            offset += operandSize;
        }

        return instructions;
    }

    /// <summary>
    /// How many operand bytes follow <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The decoded opcode.</param>
    /// <param name="il">The instruction stream, needed only for the variable-width switch operand.</param>
    /// <param name="offset">The offset of the first operand byte.</param>
    /// <returns>The operand width in bytes.</returns>
    private static int OperandSize(OpCode code, byte[] il, int offset)
    {
        return code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget
                or OperandType.InlineField
                or OperandType.InlineI
                or OperandType.InlineMethod
                or OperandType.InlineSig
                or OperandType.InlineString
                or OperandType.InlineTok
                or OperandType.InlineType
                or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,

            // A jump table: the case count, then one four-byte target per case.
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
            _ => throw new NotSupportedException(
                $"IL operand kind {code.OperandType} is not handled by this walker."),
        };
    }

    /// <summary>
    /// Counts how many times <paramref name="code"/> appears in <paramref name="method"/>.
    /// </summary>
    /// <param name="method">The method to inspect.</param>
    /// <param name="code">The opcode to count.</param>
    /// <returns>The occurrence count.</returns>
    private static int CountInstructions(MethodBase method, OpCode code)
    {
        return InstructionsOf(method).Count(instruction => instruction.Code == code);
    }

    /// <summary>
    /// Walks every call site declared by <paramref name="types"/> and their nested types, resolving each
    /// callee to <c>DeclaringType::MemberName</c>.
    /// </summary>
    /// <param name="types">The types to scan.</param>
    /// <returns>The distinct callees and the count of tokens that could not be resolved.</returns>
    /// <remarks>
    /// Nested types are included because a lambda compiles into a nested closure type rather than into
    /// the method that wrote it, so scanning declared methods alone would inspect none of the code inside
    /// a lambda.
    /// </remarks>
    private static CallSiteScan ScanCallSites(IEnumerable<Type> types)
    {
        const BindingFlags everyMethod = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        SortedSet<string> targets = new(StringComparer.Ordinal);
        int unresolved = 0;

        foreach (Type type in Expand(types))
        {
            List<MethodBase> members = [.. type.GetMethods(everyMethod)];
            members.AddRange(type.GetConstructors(everyMethod));

            foreach (MethodBase member in members)
            {
                foreach (IlInstruction instruction in InstructionsOf(member))
                {
                    if (instruction.Code != OpCodes.Call
                        && instruction.Code != OpCodes.Callvirt
                        && instruction.Code != OpCodes.Newobj
                        && instruction.Code != OpCodes.Ldftn
                        && instruction.Code != OpCodes.Ldvirtftn)
                    {
                        continue;
                    }

                    MethodBase? callee = ResolveCallee(type.Module, instruction.MetadataToken);

                    if (callee is null)
                    {
                        unresolved++;
                        continue;
                    }

                    targets.Add($"{callee.DeclaringType?.FullName}::{callee.Name}");
                }
            }
        }

        return new CallSiteScan([.. targets], unresolved);
    }

    /// <summary>
    /// Yields each type in <paramref name="types"/> followed by all of its nested types, recursively.
    /// </summary>
    /// <param name="types">The types to expand.</param>
    /// <returns>The types and their nested types.</returns>
    private static IEnumerable<Type> Expand(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            yield return type;

            foreach (Type nested in Expand(
                type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Resolves a call token to the member it names.
    /// </summary>
    /// <param name="module">The module the token belongs to.</param>
    /// <param name="token">The metadata token.</param>
    /// <returns>The resolved member, or <see langword="null"/> when it cannot be resolved.</returns>
    /// <remarks>
    /// A token can legitimately fail to resolve when it names a member of an open generic context. That
    /// is counted rather than ignored, and every caller asserts the count is zero, so a change that made
    /// the scan blind would fail a test instead of quietly weakening one.
    /// </remarks>
    private static MethodBase? ResolveCallee(Module module, int token)
    {
        try
        {
            return module.ResolveMethod(token);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> names a member that writes a diagnostic somewhere.
    /// </summary>
    /// <param name="target">A callee as <c>DeclaringType::MemberName</c>.</param>
    /// <returns><see langword="true"/> when the callee is a diagnostic sink.</returns>
    private static bool IsDiagnosticSink(string target)
    {
        int separator = target.IndexOf("::", StringComparison.Ordinal);
        string declaringType = separator < 0 ? target : target[..separator];

        return DiagnosticSinkTypes.Contains(declaringType, StringComparer.Ordinal)
            || LoggingNamespacePrefixes.Any(
                prefix => declaringType.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether <paramref name="target"/> names a member that ends a process or a thread.
    /// </summary>
    /// <param name="target">A callee as <c>DeclaringType::MemberName</c>.</param>
    /// <returns><see langword="true"/> when the callee terminates execution.</returns>
    private static bool IsProcessTerminator(string target)
    {
        return ProcessTerminatingMembers.Contains(target, StringComparer.Ordinal);
    }
}
