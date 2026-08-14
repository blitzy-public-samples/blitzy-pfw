// =====================================================================================================
//  IN-BAND OUTCOME TO HTTP STATUS PROJECTION
// =====================================================================================================
//
//  WHY THIS FILE EXISTS. Both REST projections answer a gRPC method whose failures arrive IN BAND: the
//  call itself succeeds and the outcome sits in a field on the response message. Rendering such a
//  response as HTTP 200 reports a failure as a success, which is the defect this suite guards. The
//  projection is driven directly rather than through a live upstream, because what can go wrong is the
//  MAPPING - a code sent to the wrong status, or a tri-state value misclassified - and a live upstream
//  cannot be made to emit every code on demand.
//
//  THE TRI-STATE ALGEBRA IS THE SUBTLE HALF AND IS ASSERTED EXPLICITLY. The ported failure predicate is
//  strictly-less-than-zero WITH CANCELLED EXCLUDED BY NAME [ws_objects/pfw.shared.pbl.src/isfailed.srf:
//  L11-L13], so PREVENT = 1 READS AS A SUCCESS and CANCELLED = -2 is NEITHER succeeded nor failed. A
//  projection that used a plain "not zero means failure" test would turn both into HTTP errors and change
//  observable behaviour, which constraint C-B forbids.
// =====================================================================================================

using Microsoft.AspNetCore.Http;
using PowerFramework.Contracts.Common.V1;
using OperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using UpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The in-band outcome projection of <c>RestProjectionEndpoints</c>.
/// </summary>
public sealed class RestProjectionInBandStatusTests
{
    /// <summary>
    /// A response whose nested status carries a failing outcome projects onto the published status.
    /// </summary>
    /// <param name="retCode">The in-band outcome.</param>
    /// <param name="expected">The HTTP status it must become.</param>
    /// <remarks>
    /// <para>
    /// EVERY ARM OF THE MAP IS COVERED, INCLUDING BOTH MEMBERS OF EACH PAIRED ARM, because a pair that
    /// shares a status today is two independent claims and a later edit can separate them.
    /// </para>
    /// <para>
    /// THE CONFLICT ARM IS THE ONE THAT MATTERS MOST: an <c>E_RETRY</c> outcome is the in-band form of the
    /// optimistic-concurrency mismatch, and 409 is the published mapping the whole no-silent-overwrite rule
    /// rests on.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RetCode.E_INVALID_ARGUMENT, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_INVALID_SQL, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_OUT_OF_RANGE, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_OUT_OF_BOUND, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_ACCESS_DENIED, StatusCodes.Status403Forbidden)]
    [InlineData(RetCode.E_INVALID_HANDLE, StatusCodes.Status404NotFound)]
    [InlineData(RetCode.E_OBJECT_NOT_FOUND, StatusCodes.Status404NotFound)]
    [InlineData(RetCode.E_RETRY, StatusCodes.Status409Conflict)]
    [InlineData(RetCode.E_BUSY, StatusCodes.Status429TooManyRequests)]
    [InlineData(RetCode.E_TIME_OUT, StatusCodes.Status504GatewayTimeout)]
    // 🔴 THE UNAVAILABLE-CAPABILITY PAIR IS 500, AND THESE TWO ROWS ARE THE C-D AUDIT MADE EXECUTABLE.
    // They answered 501, which no operation of this projection declares and which is reserved system-wide
    // for Gateway's four deferred-capability routes (AAP 0.4.4, C-D). An implemented operation reporting
    // that one cell of its surface has no available implementation - the pinyin matcher, the expression
    // engine's macro and foreign-variable arms, a disagreeing pinyin flag mask - is a different statement
    // from "this whole capability area is unbuilt", and answering both the same way left a caller unable to
    // tell them apart from the published contract. 500 carries the distinction on the retCode member, which
    // is how Security answers its two symmetric-cipher narrowings (docs/CONTRACTS.md 14.4). The ingress twin
    // states the identical two rows.
    [InlineData(RetCode.E_NO_SUPPORT, StatusCodes.Status500InternalServerError)]
    [InlineData(RetCode.E_NO_IMPLEMENTATION, StatusCodes.Status500InternalServerError)]
    [InlineData(RetCode.E_DB_ERROR, StatusCodes.Status502BadGateway)]
    [InlineData(RetCode.E_INVALID_TRANSACTION, StatusCodes.Status502BadGateway)]
    public void AFailingInBandOutcomeProjectsOntoItsPublishedStatus(long retCode, int expected)
    {
        PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection failure = AssertProjects(Failing(retCode));

        Assert.Equal(expected, failure.HttpStatus);
        Assert.Equal(retCode, failure.RetCode);
    }

    /// <summary>
    /// An unrecognised failing outcome is 500 and never 400.
    /// </summary>
    /// <remarks>
    /// <b>THE DIRECTION OF BLAME IS THE ASSERTION.</b> An outcome the map has not been taught is a contract
    /// this projection does not yet understand - a fault on THIS side of the boundary. Answering 400 would
    /// blame the caller and invite a retry with different input that can never succeed, which is a worse
    /// answer than admitting the projection is incomplete.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.UNKNOWN)]
    [InlineData(-31337L)]
    public void AnUnrecognisedFailingOutcomeIsAnInternalFaultAndNotACallerFault(long retCode)
    {
        PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection failure = AssertProjects(Failing(retCode));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.HttpStatus);
    }

    /// <summary>
    /// The three non-failing outcomes of the tri-state algebra are not projected as failures.
    /// </summary>
    /// <remarks>
    /// <b>PREVENT AND CANCELLED ARE THE PRESERVED DEFECT (C-B).</b> PREVENT = 1 satisfies the legacy success
    /// predicate, so a prevention IS a success in this algebra; CANCELLED = -2 is excluded from failure by
    /// name and is therefore neither. Both must render as the ordinary success body carrying their own code,
    /// so a caller reads the specific outcome rather than an HTTP error the legacy never produced.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.OK)]
    [InlineData(RetCode.PREVENT)]
    [InlineData(RetCode.CANCELLED)]
    public void ANonFailingOutcomeIsNotProjectedAsAFailure(long retCode)
    {
        Assert.False(
            PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.InBandStatus.TryProjectFailure(Failing(retCode), out _),
            $"Outcome {retCode} must not be projected as a failure.");
    }

    /// <summary>
    /// A message carrying no in-band outcome at all is left alone.
    /// </summary>
    /// <remarks>
    /// A pure data response - one with neither a nested status nor a top-level outcome enum - has no outcome
    /// to read, and inventing one would turn every such response into a 500. The descriptor read must
    /// report "no outcome present" rather than "outcome zero".
    /// </remarks>
    [Fact]
    public void AMessageWithNoInBandOutcomeIsNotProjectedAsAFailure() =>
        Assert.False(PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.InBandStatus.TryProjectFailure(new ConflictDetail { UpdateTable = "COMPANY" }, out _));

    /// <summary>
    /// The projection is marked as originating in band rather than at this boundary.
    /// </summary>
    /// <remarks>
    /// The flag is what lets the rendered problem body say WHERE the failure came from. An in-band outcome
    /// was produced by the upstream and merely relayed here, and a reader who cannot tell that apart from a
    /// local rejection will look for the fault in the wrong service.
    /// </remarks>
    [Fact]
    public void AnInBandFailureIsAttributedToTheUpstream()
    {
        PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection failure = AssertProjects(Failing(RetCode.E_DB_ERROR));

        Assert.False(failure.FromUpstream);
        Assert.False(string.IsNullOrWhiteSpace(failure.Detail));
    }

    /// <summary>
    /// The in-band diagnostic never becomes the problem's <c>detail</c>; the fixed prose for the mapped
    /// outcome stands there whether the upstream sent text or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS ROW WAS INVERTED, AND THE REMARK IT REPLACED ARGUED C-B PROTECTED THE OLD BEHAVIOUR. It
    /// asserted the diagnostic was relayed verbatim into <c>detail</c>, on the reasoning that the legacy
    /// text must reach the caller. The premise is right and the conclusion did not follow from it.
    /// </para>
    /// <para>
    /// THE LEGACY TEXT STILL REACHES THE CALLER - through the <c>response</c> extension, which carries the
    /// contract's own message complete with its text, localization category, severity and, for an
    /// expression fault, its caret position. That is the structured-error carrier the AAP actually
    /// specifies (0.3.4, 0.6.2.5), it is a reviewed member, and StructuredErrorParityTests already screens
    /// every value in it against reading as a database statement and against credential markers. Nothing
    /// is withheld from the caller by this change.
    /// </para>
    /// <para>
    /// WHAT CHANGED IS THAT <c>detail</c> STOPPED DUPLICATING IT UNSCREENED. <c>detail</c> is RFC 9457's
    /// human-readable prose member, it passes through no such screen, and the identical construction one
    /// hop downstream in Gateway's proxy was the reported disclosure - so leaving it here would have
    /// reinstated that disclosure from behind, since this body is what that proxy mirrors. C-B is not
    /// engaged either way: the legacy had no process boundary and no network caller at all, and these
    /// diagnostics went to a MessageBox on the operator's own screen [AAP 0.6.1].
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInBandDiagnosticIsNeverPromotedIntoTheProblemDetail()
    {
        OperationStatus carried = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_DB_ERROR,
            ErrorText = "检索失败",
        };

        PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection withText = AssertProjects(new UpdateResponse { Status = carried });

        Assert.DoesNotContain("检索失败", withText.Detail, StringComparison.Ordinal);

        PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection withoutText = AssertProjects(Failing(RetCode.E_DB_ERROR));

        // THE TWO DETAILS ARE THE SAME STRING, which is the property that matters: a caller cannot infer
        // whether the upstream sent a diagnostic, or anything about it, from the detail it receives.
        Assert.Equal(withoutText.Detail, withText.Detail);
        Assert.False(string.IsNullOrWhiteSpace(withText.Detail));

        // AND THE OUTCOME IS STILL FULLY DISTINGUISHABLE, because the numeric code is what a caller
        // branches on and it is untouched.
        Assert.Equal((long)RetCode.E_DB_ERROR, withText.RetCode);
    }

    /// <summary>Builds a response whose nested status carries the given outcome.</summary>
    /// <param name="retCode">The outcome.</param>
    /// <returns>The response.</returns>
    private static UpdateResponse Failing(long retCode) => new()
    {
        Status = new OperationStatus { RetCode = (WireRetCode)(int)retCode },
    };

    /// <summary>Asserts the response projects as a failure and returns the projection.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The projection.</returns>
    private static PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection AssertProjects(Google.Protobuf.IMessage response)
    {
        Assert.True(
            PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.InBandStatus.TryProjectFailure(response, out PowerFramework.DataServices.Endpoints.RestProjectionEndpoints.StatusProjection failure),
            "The response carries a failing in-band outcome and must project as a failure.");

        return failure;
    }
}
