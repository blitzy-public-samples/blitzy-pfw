// =====================================================================================================
//  F-06 - IN-BAND OUTCOME TO HTTP STATUS PROJECTION
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using OperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using UpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The in-band outcome projection of <c>DataServicesProxyEndpoints</c>, and the composition root's reverse
/// classification that has to agree with it.
/// </summary>
/// <param name="host">
/// The deployed host, for the reverse-direction rows only. A CLASS fixture rather than a host per row: the
/// subject is one registration the composition root installs, so every row reads the same one and the suite
/// pays for a single boot.
/// </param>
public sealed class DataServicesProxyInBandStatusTests(GatewayTestHostFixture host)
    : IClassFixture<GatewayTestHostFixture>
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
    [InlineData(RetCode.E_NO_SUPPORT, StatusCodes.Status501NotImplemented)]
    [InlineData(RetCode.E_NO_IMPLEMENTATION, StatusCodes.Status501NotImplemented)]
    [InlineData(RetCode.E_DB_ERROR, StatusCodes.Status502BadGateway)]
    [InlineData(RetCode.E_INVALID_TRANSACTION, StatusCodes.Status502BadGateway)]
    public void AFailingInBandOutcomeProjectsOntoItsPublishedStatus(long retCode, int expected)
    {
        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection failure = AssertProjects(Failing(retCode));

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
        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection failure = AssertProjects(Failing(retCode));

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
            PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.InBandStatus.TryProjectFailure(Failing(retCode), out _),
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
        Assert.False(PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.InBandStatus.TryProjectFailure(new ConflictDetail { UpdateTable = "COMPANY" }, out _));

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
        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection failure = AssertProjects(Failing(RetCode.E_DB_ERROR));

        Assert.True(failure.FromUpstream);
        Assert.False(string.IsNullOrWhiteSpace(failure.Detail));
    }

    /// <summary>
    /// The upstream's own diagnostic is carried through when it sent one, and a fallback stands in when it
    /// did not.
    /// </summary>
    /// <remarks>
    /// THE LEGACY TEXT MUST REACH THE CALLER (C-B). The diagnostic is the legacy's own message and is
    /// relayed verbatim rather than replaced by a generic sentence; the built-in prose exists only for the
    /// case where the upstream sent none, so the body is never empty.
    /// </remarks>
    [Fact]
    public void TheUpstreamDiagnosticIsCarriedThroughAndAFallbackStandsInWhenAbsent()
    {
        OperationStatus carried = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_DB_ERROR,
            ErrorText = "检索失败",
        };

        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection withText = AssertProjects(new UpdateResponse { Status = carried });

        Assert.Equal("检索失败", withText.Detail);

        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection withoutText = AssertProjects(Failing(RetCode.E_DB_ERROR));

        Assert.NotEqual("检索失败", withoutText.Detail);
        Assert.False(string.IsNullOrWhiteSpace(withoutText.Detail));
    }

    // ==============================================================================================
    //  F-22 - THE REVERSE DIRECTION, WHICH HAS TO AGREE WITH THE FORWARD ONE
    //  --------------------------------------------------------------------------------------------
    //  The map at the top of this file sends an in-band outcome TO an HTTP status. The composition root
    //  carries the opposite map, classifying a FRAMEWORK-GENERATED status back into an outcome code so
    //  that a body this service did not compose still carries the member the published contract declares.
    //
    //  THEY LIVE IN DIFFERENT FILES AND MUST STILL AGREE, WHICH IS WHY BOTH ARE ASSERTED HERE. The
    //  classifier had no 429 arm although this service produces 429 twice - from an upstream
    //  ResourceExhausted and from an in-band E_BUSY outcome - so the one status the two maps most obviously
    //  shared round-tripped to UNKNOWN in one direction and to E_BUSY in the other.
    // ==============================================================================================

    /// <summary>
    /// Every status the framework can answer with carries its classified outcome code.
    /// </summary>
    /// <param name="status">The status the framework is answering with.</param>
    /// <param name="expected">The outcome code the published catalogue assigns it.</param>
    /// <remarks>
    /// Driven through the DEPLOYED registration - the problem-details customization the composition root
    /// installed - rather than through a copy of the switch, so a status this service answers but does not
    /// classify shows up here. The 429 row is the one that was missing.
    /// </remarks>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, RetCode.E_INVALID_ARGUMENT)]
    [InlineData(StatusCodes.Status401Unauthorized, RetCode.E_ACCESS_DENIED)]
    [InlineData(StatusCodes.Status403Forbidden, RetCode.E_ACCESS_DENIED)]
    [InlineData(StatusCodes.Status404NotFound, RetCode.E_OBJECT_NOT_FOUND)]
    [InlineData(StatusCodes.Status405MethodNotAllowed, RetCode.E_NO_SUPPORT)]
    [InlineData(StatusCodes.Status408RequestTimeout, RetCode.E_TIME_OUT)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, RetCode.E_INVALID_TYPE)]
    [InlineData(StatusCodes.Status429TooManyRequests, RetCode.E_BUSY)]
    [InlineData(StatusCodes.Status500InternalServerError, RetCode.E_INTERNAL_ERROR)]
    [InlineData(StatusCodes.Status501NotImplemented, RetCode.E_NO_IMPLEMENTATION)]
    [InlineData(StatusCodes.Status502BadGateway, RetCode.E_INTERNAL_ERROR)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, RetCode.E_BUSY)]
    [InlineData(StatusCodes.Status504GatewayTimeout, RetCode.E_TIME_OUT)]
    public void AFrameworkGeneratedStatusCarriesItsClassifiedOutcomeCode(int status, long expected)
    {
        Assert.Equal(expected, Classify(status));
    }

    /// <summary>
    /// The shed-load refusal round-trips: the outcome the forward map sends to 429 is the outcome the
    /// reverse map returns for 429.
    /// </summary>
    /// <remarks>
    /// <b>THE ROUND TRIP IS THE FINDING, NOT THE ARM.</b> A single missing arm reads as an omission; the
    /// consequence is that one event - a request declined for want of capacity - carried two different codes
    /// depending on which half of this service composed the body. Both halves are asserted in one place so
    /// that removing either direction fails here rather than passing in its own file.
    /// </remarks>
    [Fact]
    public void TheBusyOutcomeRoundTripsThroughItsPublishedStatus()
    {
        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection forward =
            AssertProjects(Failing(RetCode.E_BUSY));

        Assert.Equal(StatusCodes.Status429TooManyRequests, forward.HttpStatus);
        Assert.Equal(RetCode.E_BUSY, Classify(forward.HttpStatus));
    }

    /// <summary>
    /// A code this service already resolved is never overwritten by the classifier.
    /// </summary>
    /// <remarks>
    /// The projection forwards the UPSTREAM's own outcome, which is more specific than anything derivable
    /// from a status - the 409 the conflict path writes is the case that matters, since 409 has no arm at
    /// all and would be replaced by <c>UNKNOWN</c> were the guard absent.
    /// </remarks>
    [Fact]
    public void AnOutcomeThisServiceAlreadyResolvedIsNotReclassified()
    {
        ProblemDetails problem = new() { Status = StatusCodes.Status409Conflict };

        problem.Extensions["retCode"] = RetCode.E_RETRY;

        Customize(problem);

        Assert.Equal(RetCode.E_RETRY, Assert.IsType<long>(problem.Extensions["retCode"]));
    }

    /// <summary>Classifies one status through the deployed problem-details customization.</summary>
    /// <param name="status">The status to classify.</param>
    /// <returns>The outcome code the customization stamped.</returns>
    private long Classify(int status)
    {
        ProblemDetails problem = new() { Status = status };

        Customize(problem);

        Assert.True(
            problem.Extensions.TryGetValue("retCode", out object? stamped),
            $"Status {status} was answered with no retCode member, so a consumer of the published contract "
                + "reads a body missing the member every 4xx and 5xx response declares.");

        return Assert.IsType<long>(stamped);
    }

    /// <summary>Runs the deployed customization over one problem document.</summary>
    /// <param name="problem">The document to customize, mutated in place.</param>
    private void Customize(ProblemDetails problem)
    {
        Action<ProblemDetailsContext>? customize = host.Services
            .GetRequiredService<IOptions<ProblemDetailsOptions>>()
            .Value
            .CustomizeProblemDetails;

        Assert.NotNull(customize);

        customize(new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext(),
            ProblemDetails = problem,
        });
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
    private static PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection AssertProjects(Google.Protobuf.IMessage response)
    {
        Assert.True(
            PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.InBandStatus.TryProjectFailure(response, out PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection failure),
            "The response carries a failing in-band outcome and must project as a failure.");

        return failure;
    }
}
