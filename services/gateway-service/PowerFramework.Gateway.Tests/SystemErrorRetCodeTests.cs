// ==================================================================================================
//  SystemErrorRetCodeTests - THE RETURN CODE A FATAL FAULT PUBLISHES, AND THE SHAPE OF THE PAYLOAD
//                            IT WAS DECODED FROM
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   Two properties of PowerFramework.Gateway.Diagnostics.SystemErrorHandler
//                         that sit either side of one line in the legacy protocol - what may leave
//                         on the CALLER channel, and what must be visible on the OPERATOR channel:
//
//                           1. A FATAL FAULT CAN NEVER PUBLISH A CODE A CONSUMER READS AS SUCCESS.
//                              The legacy algebra is tri-state and the hole is preserved verbatim:
//                              PREVENT is 1 and IsSucceeded tests >= 0, so a prevention reads as a
//                              SUCCESS [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13,
//                              retcode.sru:L42], and CANCELLED is excluded from IsFailed, so a
//                              cancellation is NEITHER [isfailed.srf:L11-L13]. Payload field 1 is
//                              arbitrary wire text and the legacy long conversion maps anything
//                              unparseable to ZERO [pfw.sra:L117], so an unguarded assert path can
//                              publish retCode 0 or 1 in a 500 body that has just terminated the process.
//                              ResolveRetCode applies the kernel's own IsFailed predicate, which
//                              is the same guard the four Program.cs ClassifyFailure methods apply.
//
//                           2. A DECODE THAT DISCARDS FIELDS SAYS SO. Two legacy behaviours discard
//                              fields silently and BOTH ARE REPRODUCED RATHER THAN CORRECTED (C-B):
//                              the split helper appends its last field only when non-empty
//                              [pfw.sra:L64-L66], and the arity test is exactly-seven rather than
//                              at-least-seven [pfw.sra:L119]. A payload written with seven fields
//                              whose call-stack field is EMPTY therefore splits into six, fails the
//                              arity test and loses four diagnostic fields - and the legacy loses
//                              them identically, which is why the decode may not be changed. The
//                              loss is made VISIBLE on the operator record, which is net-new: the
//                              legacy renders one dialog and halts, so there is nothing there to
//                              stay faithful to.
//
//  WHY THE DECODE IS NOT "FIXED"
//  ------------------------------------------------------------------------------------------------
//  Splitting with a fixed field count, or with StringSplitOptions.None, would make the .NET decoder
//  DIVERGE from the oracle for a payload the oracle decodes as six fields. AAP G2 and 0.8.1 require
//  documented defects be replicated rather than corrected, and 0.4.2.4 assigns this file the
//  assert-unpacking protocol of pfw.sra:L111-L144 specifically. So the parity rows below assert the
//  lossy behaviour as CORRECT, and the observation rows assert that it is no longer silent.
//
//  WHAT THIS SUITE DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  It does not re-test the formatter, the allowlist or the correlation bridge - the first is a pure
//  function covered by the decode-protocol rows elsewhere, and the last two belong to
//  SystemErrorObservabilityTests, whose RecordingLogger, RecordedLogEntry and
//  StubHostApplicationLifetime this file reuses rather than duplicating.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// ==================================================================================================

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The caller-channel return code and the operator-channel payload shape.
/// </summary>
public sealed class SystemErrorRetCodeTests
{
    /// <summary>The CRLF field delimiter [<c>ws_objects/pfw.common.pbl.src/assert.srf:L19</c>].</summary>
    private const string Delimiter = "\r\n";

    // ----------------------------------------------------------------------------------------------
    //  1. THE PURE NARROWING. No host, no context - ResolveRetCode over a decoded record.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Every value the legacy algebra does NOT classify as a failure is narrowed to the unknown code,
    /// however it arrived in payload field 1.
    /// </summary>
    /// <param name="firstField">The payload's first field, verbatim as it would arrive on the wire.</param>
    /// <remarks>
    /// THE FOUR SHAPES ARE THE FOUR ROUTES TO A NON-FAILURE, and each is a different mechanism rather
    /// than four spellings of one. <c>0</c> is <c>OK</c> written literally. <c>1</c> is <c>PREVENT</c>,
    /// which the preserved hole makes read as a success. <c>-2</c> is <c>CANCELLED</c>, which is
    /// neither succeeded nor failed and so must not appear on an error body either. And an unparseable
    /// field reaches zero through the legacy long conversion rather than through anything a caller
    /// wrote, which is the route that cannot be closed by validating the input.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-2")]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("   ")]
    public void ANonFailureCodeInPayloadFieldOneIsNarrowedToUnknown(string firstField)
    {
        SystemErrorInfo decoded = DecodeAssert(firstField, "the fault text");

        long resolved = SystemErrorHandler.ResolveRetCode(decoded, decodedFromAssertPayload: true);

        Assert.Equal(RetCode.UNKNOWN, resolved);

        // The property that matters to a consumer, stated in the consumer's own terms rather than as a
        // comparison against a constant: the kernel predicates are what a caller applies.
        Assert.False(Predicates.IsSucceeded(resolved));
        Assert.True(Predicates.IsFailed(resolved));
    }

    /// <summary>
    /// A published FAILURE code in payload field 1 still passes through unchanged, so the narrowing is
    /// a guard rather than a blanket.
    /// </summary>
    /// <param name="firstField">The payload's first field.</param>
    /// <param name="expected">The code the wire must carry.</param>
    [Theory]
    [InlineData("-1", RetCode.FAILED)]
    [InlineData("-3", RetCode.E_INVALID_ARGUMENT)]
    [InlineData("-27", RetCode.E_INTERNAL_ERROR)]
    [InlineData("-2000", RetCode.E_NO_SUPPORT)]
    [InlineData("-2001", RetCode.E_NO_IMPLEMENTATION)]
    [InlineData("-4000", RetCode.UNKNOWN)]
    public void APublishedFailureCodeInPayloadFieldOnePassesThrough(string firstField, long expected)
    {
        SystemErrorInfo decoded = DecodeAssert(firstField, "the fault text");

        Assert.Equal(
            expected,
            SystemErrorHandler.ResolveRetCode(decoded, decodedFromAssertPayload: true));
    }

    /// <summary>
    /// The fixed sentinel the producer actually writes is not a published code, so it is narrowed - the
    /// row that pins the ordinary case rather than an adversarial one.
    /// </summary>
    /// <remarks>
    /// <c>-10000</c> is what <c>assert.srf:L22</c> emits, and it is deliberately outside the published
    /// closed set, so the contract is not widened to carry a value only this protocol uses. The raw
    /// number still reaches the operator channel; see the shape rows below.
    /// </remarks>
    [Fact]
    public void TheProducersOwnSentinelIsNarrowedToUnknown()
    {
        SystemErrorInfo decoded = DecodeAssert("-10000", "Assertion failed");

        Assert.Equal(-10000L, decoded.Number);
        Assert.Equal(
            RetCode.UNKNOWN,
            SystemErrorHandler.ResolveRetCode(decoded, decodedFromAssertPayload: true));
    }

    /// <summary>
    /// The non-assert path is the unknown code unconditionally, whatever numeric result the exception
    /// happened to carry.
    /// </summary>
    [Fact]
    public void TheNonAssertPathIsAlwaysUnknown()
    {
        SystemErrorInfo raised = SystemErrorHandler.FromException(
            new InvalidOperationException("boom"));

        Assert.Equal(
            RetCode.UNKNOWN,
            SystemErrorHandler.ResolveRetCode(raised, decodedFromAssertPayload: false));

        // And a null record too, which is the argument shape the entry point can never produce but the
        // method's own contract admits.
        Assert.Equal(
            RetCode.UNKNOWN,
            SystemErrorHandler.ResolveRetCode(null, decodedFromAssertPayload: true));
    }

    // ----------------------------------------------------------------------------------------------
    //  2. THE SAME PROPERTY, THROUGH THE REAL HANDLER AND OUT OF A REAL RESPONSE BODY.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The value a caller actually receives: a fatal fault's problem document never carries a code its
    /// own <c>IsSucceeded</c> would accept.
    /// </summary>
    /// <param name="firstField">The payload's first field.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-2")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public async Task TheFatalProblemDocumentNeverCarriesASuccessValuedCode(string firstField)
    {
        RecordingLogger logger = new();
        List<int> terminations = [];
        SystemErrorHandler handler = new(
            logger,
            new StubHostApplicationLifetime(),
            problemDetailsService: null,
            requestProcessTermination: terminations.Add);

        DefaultHttpContext httpContext = new();
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/v1/ping";

        bool handled = await handler.TryHandleAsync(
            httpContext,
            new AssertionFailure(Payload(firstField, "the fault text")),
            TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        // The halt half of the protocol is unaffected: a structural fault still terminates, exactly
        // once [ws_objects/pfw.pbl.src/pfw.sra:L143].
        Assert.Equal([70], terminations);

        long wireRetCode = await ReadRetCodeAsync(httpContext);

        Assert.Equal(RetCode.UNKNOWN, wireRetCode);
        Assert.False(Predicates.IsSucceeded(wireRetCode));
        Assert.True(Predicates.IsFailed(wireRetCode));
    }

    /// <summary>
    /// The operator record still carries the RAW decoded number, so narrowing the wire value loses
    /// nothing an operator needs.
    /// </summary>
    [Fact]
    public async Task TheOperatorRecordStillCarriesTheRawDecodedNumber()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = new(
            logger,
            new StubHostApplicationLifetime(),
            problemDetailsService: null,
            requestProcessTermination: static _ => { });

        DefaultHttpContext httpContext = new();
        httpContext.Response.Body = new MemoryStream();

        _ = await handler.TryHandleAsync(
            httpContext,
            new AssertionFailure(Payload("1", "the fault text")),
            TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();

        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Equal(1L, entry.Property("ErrorNumber"));
        Assert.Equal(RetCode.UNKNOWN, entry.Property("WireRetCode"));
    }

    // ----------------------------------------------------------------------------------------------
    //  3. THE PAYLOAD SHAPE. The lossy decode is asserted as CORRECT, and as no longer silent.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A canonical seven-field payload decodes completely and reports as complete.
    /// </summary>
    [Fact]
    public void ACompletePayloadReportsSevenFieldsAndSevenDecoded()
    {
        SystemErrorInfo raised = Raised(Payload(
            "-10000",
            "the assertion text",
            "w_demo_selector",
            "n_cst_example",
            "of_dowork",
            "42",
            "frame one"));

        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(raised);

        Assert.True(shape.SentinelMatched);
        Assert.Equal(7, shape.FieldCount);
        Assert.Equal(7, shape.DecodedFieldCount);
    }

    /// <summary>
    /// PRESERVED DEFECT, ASSERTED AS CORRECT: a payload WRITTEN with seven fields whose call-stack
    /// field is empty splits into six and decodes only two - and the shape says so.
    /// </summary>
    /// <remarks>
    /// The legacy split helper appends its tail only when non-empty
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L64-L66</c>] and the arity test is exact
    /// [<c>:L119</c>], so the four discarded fields are the oracle's behaviour rather than this port's.
    /// The assertion below is deliberately written as an equality against the LOSSY result: a future
    /// edit that "fixed" the split would fail here, which is the point.
    /// </remarks>
    [Fact]
    public void ASevenFieldPayloadWithAnEmptyTailSplitsIntoSixAndSaysSo()
    {
        SystemErrorInfo raised = Raised(Payload(
            "-10000",
            "the assertion text",
            "w_demo_selector",
            "n_cst_example",
            "of_dowork",
            "42",
            string.Empty));

        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(raised);

        Assert.True(shape.SentinelMatched);
        Assert.Equal(6, shape.FieldCount);
        Assert.Equal(2, shape.DecodedFieldCount);

        // And the decode itself is unchanged: number and text only, everything else untouched, with
        // the sentinel still standing in the object member because the complete branch never ran.
        SystemErrorInfo decoded = SystemErrorHandler.Decode(raised);

        Assert.Equal(-10000L, decoded.Number);
        Assert.Equal("the assertion text", decoded.Text);
        Assert.Equal(string.Empty, decoded.WindowMenu);
        Assert.Equal("assert", decoded.Object);
        Assert.Equal(string.Empty, decoded.ObjectEvent);
        Assert.Equal(0L, decoded.Line);
        Assert.Equal(string.Empty, decoded.StackTrace);
    }

    /// <summary>
    /// PRESERVED DEFECT, ASSERTED AS CORRECT: an eight-field payload whose eighth field is empty is
    /// indistinguishable from a seven-field one, because the trailing delimiter contributes no field.
    /// </summary>
    [Fact]
    public void AnEightFieldPayloadWithAnEmptyTailIsIndistinguishableFromSevenFields()
    {
        SystemErrorInfo raised = Raised(Payload(
            "-10000",
            "the assertion text",
            "w_demo_selector",
            "n_cst_example",
            "of_dowork",
            "42",
            "frame one",
            string.Empty));

        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(raised);

        Assert.Equal(7, shape.FieldCount);
        Assert.Equal(7, shape.DecodedFieldCount);
    }

    /// <summary>
    /// An eight-field payload with a NON-empty eighth field splits into eight, fails the exact-seven
    /// test, and decodes two - the other half of the exact-arity behaviour.
    /// </summary>
    [Fact]
    public void AnEightFieldPayloadDecodesOnlyTwoFields()
    {
        SystemErrorInfo raised = Raised(Payload(
            "-10000", "text", "w", "o", "e", "42", "frame one", "extra"));

        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(raised);

        Assert.Equal(8, shape.FieldCount);
        Assert.Equal(2, shape.DecodedFieldCount);
    }

    /// <summary>
    /// A single-field payload decodes nothing at all, because the at-least-two gate refuses it
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L116</c>].
    /// </summary>
    [Fact]
    public void ASingleFieldPayloadDecodesNothing()
    {
        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(Raised("-10000"));

        Assert.True(shape.SentinelMatched);
        Assert.Equal(1, shape.FieldCount);
        Assert.Equal(0, shape.DecodedFieldCount);
    }

    /// <summary>
    /// A record whose object member is not the sentinel has no payload shape at all, rather than a
    /// shape reporting zero fields of some payload it never split.
    /// </summary>
    [Fact]
    public void ANonAssertRecordHasNoPayloadShape()
    {
        SystemErrorPayloadShape shape = SystemErrorHandler.DescribePayloadShape(
            SystemErrorHandler.FromException(new InvalidOperationException("a\r\nb\r\nc")));

        Assert.False(shape.SentinelMatched);
        Assert.Equal(0, shape.FieldCount);
        Assert.Equal(0, shape.DecodedFieldCount);

        // And the null record answers the same way rather than throwing.
        Assert.False(SystemErrorHandler.DescribePayloadShape(null).SentinelMatched);
    }

    /// <summary>
    /// The observation reaches the operator record, which is the whole point of computing it: a
    /// terminal fault whose payload lost four fields is diagnosable as such.
    /// </summary>
    [Fact]
    public async Task TheOperatorRecordReportsThatFourFieldsWereDiscarded()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = new(
            logger,
            new StubHostApplicationLifetime(),
            problemDetailsService: null,
            requestProcessTermination: static _ => { });

        DefaultHttpContext httpContext = new();
        httpContext.Response.Body = new MemoryStream();

        _ = await handler.TryHandleAsync(
            httpContext,
            new AssertionFailure(Payload(
                "-10000", "the assertion text", "w_demo_selector", "n_cst_example", "of_dowork", "42", string.Empty)),
            TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();

        Assert.Equal(6, entry.Property("PayloadFieldCount"));
        Assert.Equal(2, entry.Property("PayloadDecodedFieldCount"));
    }

    /// <summary>
    /// And it reports a complete decode as complete, so the pair is a measurement rather than a
    /// warning that is always on.
    /// </summary>
    [Fact]
    public async Task TheOperatorRecordReportsACompleteDecodeAsComplete()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = new(
            logger,
            new StubHostApplicationLifetime(),
            problemDetailsService: null,
            requestProcessTermination: static _ => { });

        DefaultHttpContext httpContext = new();
        httpContext.Response.Body = new MemoryStream();

        _ = await handler.TryHandleAsync(
            httpContext,
            new AssertionFailure(Payload(
                "-10000", "the assertion text", "w_demo_selector", "n_cst_example", "of_dowork", "42", "frame one")),
            TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();

        Assert.Equal(7, entry.Property("PayloadFieldCount"));
        Assert.Equal(7, entry.Property("PayloadDecodedFieldCount"));
    }

    /// <summary>
    /// The shape members are integers and a boolean, so nothing derived from payload CONTENT can reach
    /// a channel through them (C-F).
    /// </summary>
    [Fact]
    public void ThePayloadShapeCarriesNoPayloadContent()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(SystemErrorPayloadShape).GetProperties())
        {
            Assert.True(
                property.PropertyType == typeof(int) || property.PropertyType == typeof(bool),
                $"'{property.Name}' is '{property.PropertyType}'. Every member of the payload shape "
                    + "must be an integer or a boolean, because the shape is published on the operator "
                    + "channel and a member carrying payload text would make it a disclosure path.");
        }
    }

    // ----------------------------------------------------------------------------------------------
    //  HELPERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>Joins fields with the producer's CRLF delimiter.</summary>
    /// <param name="fields">The fields, in payload order.</param>
    /// <returns>The payload.</returns>
    private static string Payload(params string[] fields) => string.Join(Delimiter, fields);

    /// <summary>Builds the PRE-decode record an assertion failure produces.</summary>
    /// <param name="payload">The payload text.</param>
    /// <returns>The record, with the sentinel on its object member.</returns>
    private static SystemErrorInfo Raised(string payload) =>
        SystemErrorHandler.FromException(new AssertionFailure(payload));

    /// <summary>Builds and decodes a two-field assert payload.</summary>
    /// <param name="firstField">Payload field 1.</param>
    /// <param name="text">Payload field 2.</param>
    /// <returns>The decoded record.</returns>
    private static SystemErrorInfo DecodeAssert(string firstField, string text) =>
        SystemErrorHandler.Decode(Raised(Payload(firstField, text)));

    /// <summary>Reads the <c>retCode</c> member out of the written problem document.</summary>
    /// <param name="httpContext">The context whose response body to read.</param>
    /// <returns>The code the caller receives.</returns>
    private static async Task<long> ReadRetCodeAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;

        using System.Text.Json.JsonDocument document = await System.Text.Json.JsonDocument
            .ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        return document.RootElement.GetProperty("retCode").GetInt64();
    }
}
