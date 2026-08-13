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
//
//  🔴 THIS TABLE HAS A TWIN, AND THE TWO ARE THE WHOLE OF AN EQUIVALENCE CLAIM THAT NOTHING ELSE CHECKS.
//  DataServices publishes its own REST projection of the same operations, and the two surfaces are
//  documented as equivalent: the same refusal must carry the same HTTP status whichever one a caller
//  reached it through. Six codes broke that and neither side noticed, because on the OTHER side all six
//  fell into a default arm that answered 500. The twin is
//  PowerFramework.DataServices.Tests.RestProjectionStatusEquivalenceTests, and it states the same table
//  row for row.
//
//  THE TABLE IS DUPLICATED DELIBERATELY RATHER THAN HOISTED. Constraint C-A permits exactly one thing to
//  cross a service boundary - the published contract definitions - and that project carries NO behaviour.
//  A shared mapping table would be behaviour, and one service reading another's table would be the
//  coupling the decomposition exists to remove. Two identical tables that each fail loudly is the correct
//  shape for an equivalence between two independently deployable services; each names the other so a
//  reader changing one is told where the other is.
//
//  WHAT IS NOT COMPARED IS THE PROSE, and that is deliberate too. Each surface's fallback sentence names
//  the surface a caller is talking to - this one says "upstream" where the direct projection does not -
//  and either way the sentence yields to the upstream's own diagnostic whenever one was supplied, which is
//  the case behaviour preservation cares about (C-B). The STATUS is the contract; the sentence is the
//  courtesy.
// =====================================================================================================

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Gateway.Configuration;
using OperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using UpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using Xunit;
using DataServicesUpdateResponse = PowerFramework.Contracts.DataServices.V1.UpdateResponse;
using DropDownStateResponse =
    PowerFramework.Contracts.DataServices.V1.GetDropDownSearchStateResponse;
using InBandStatus = PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.InBandStatus;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using StatusProjection = PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection;
using StructuredError = PowerFramework.Contracts.DataServices.V1.StructuredError;
using WireDwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
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
    // 🔴 THE UNAVAILABLE-CAPABILITY PAIR IS 500, AND THESE TWO ROWS ARE THE C-D AUDIT MADE EXECUTABLE.
    // They answered 501 and that broke the published contract twice: gateway.v1.yaml declares 501 on the
    // eight reserved deferred-capability operations and on NO other, so the status was undeclared on the
    // implemented operations that produce it; and it erased the one distinction those reserved routes exist
    // to draw - "this whole capability area is unbuilt" (AAP 0.4.4, C-D) versus "this implemented operation
    // has no implementation for the cell you asked for", which is what the pinyin matcher, the expression
    // engine's macro and foreign-variable arms, and a disagreeing pinyin flag mask all report. 500 is the
    // status every projected operation already declares, and the retCode member carries which code arose -
    // exactly as Security answers its two symmetric-cipher narrowings (docs/CONTRACTS.md 14.4).
    [InlineData(RetCode.E_NO_SUPPORT, StatusCodes.Status500InternalServerError)]
    [InlineData(RetCode.E_NO_IMPLEMENTATION, StatusCodes.Status500InternalServerError)]
    [InlineData(RetCode.E_DB_ERROR, StatusCodes.Status502BadGateway)]
    [InlineData(RetCode.E_INVALID_TRANSACTION, StatusCodes.Status502BadGateway)]

    // ⚠ THE THREE ROWS THAT USED TO FALL TO THE DEFAULT, each with a natural declared status the map
    // simply had not been taught. While they were unclassified this boundary answered 500 for all three,
    // which told a caller that Gateway had failed for outcomes an upstream reported perfectly normally.
    //
    //   E_INVALID_DATA is what the update path answers when the carrier it was handed cannot be applied
    //     [n_cst_thread_task_sqlupdate.sru], so the caller's PAYLOAD is at fault: 400.
    //   E_NOT_EXISTS names something the upstream could not find, exactly as its two siblings above do: 404.
    //   FAILED is the oracle's unspecific failure [retcode.sru:L43] and is RECOGNISED - so the default's
    //     own reasoning ("a code this projection has not been taught is a fault on THIS side") does not
    //     apply to it. 502 is the declared status meaning the service behind this gateway failed, and it is
    //     what sends an operator to the right service. 422 is forbidden - the status surface is closed.
    [InlineData(RetCode.E_INVALID_DATA, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_NOT_EXISTS, StatusCodes.Status404NotFound)]
    [InlineData(RetCode.FAILED, StatusCodes.Status502BadGateway)]
    //
    // 🔴 AND THREE MORE THAT FELL TO THE DEFAULT, each observed at run time as an HTTP 500 or 502 for an
    // outright caller mistake:
    //
    //   E_INVALID_DATAOBJECT is what the upstream answers when the DataWindow name a request carried
    //     resolves to nothing. It is 400 and NOT 404 for a specific reason: the RETRIEVAL side answers the
    //     same mistake with the oracle's own E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L554],
    //     which is already 400 above, and one caller mistake must not produce two different statuses
    //     depending on which verb was used. Unclassified it produced 500 on a retrieval and 502 on an
    //     update - neither of which says "you named a DataWindow that does not exist".
    //   E_VAR_NOT_FOUND and E_MEMBER_NOT_FOUND are the column-expression service's own not-found codes -
    //     a variable name no global-variable table carries, a member it cannot bind - which is the
    //     identical situation to the three not-found codes above and therefore the identical status.
    [InlineData(RetCode.E_INVALID_DATAOBJECT, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_VAR_NOT_FOUND, StatusCodes.Status404NotFound)]
    [InlineData(RetCode.E_MEMBER_NOT_FOUND, StatusCodes.Status404NotFound)]
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
    /// The upstream's own diagnostic is NEVER relayed: the detail is this gateway's own fixed prose
    /// whether the upstream sent text or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS ROW USED TO ASSERT THE OPPOSITE, AND THE BEHAVIOUR IT PINNED WAS THE DEFECT. It required
    /// the upstream's diagnostic to be relayed verbatim, citing C-B - preserve legacy behaviour. That
    /// reading of C-B does not hold here, and the reason is what the constraint is actually about: the
    /// legacy had NO process boundary and no external caller, so these diagnostics went to a MessageBox on
    /// the operator's own screen (AAP 0.6.1). There is no legacy behaviour in which an anonymous network
    /// caller receives them, so declining to forward them preserves nothing and changes nothing the legacy
    /// could observe.
    /// </para>
    /// <para>
    /// WHAT RELAYING THEM COST. Gateway is the system's only external ingress. The legacy diagnostics name
    /// DataWindow objects, column identifiers and buffer positions, and the SQL error path's own
    /// <c>sqlsyntax</c> field carries the complete generated statement including interpolated literal
    /// VALUES, with no redaction anywhere in the legacy logger (AAP 0.6.4). Relaying that text made the
    /// response body a disclosure channel for internal structure and row data.
    /// </para>
    /// <para>
    /// THE CALLER LOSES NOTHING IT COULD ACT ON. The numeric <c>retCode</c> is the legacy return-code
    /// algebra's own value, it is published in the contract, and it is what a client branches on - it is
    /// carried unchanged and is asserted below. What is withheld is prose no client could parse. The
    /// operator still receives the diagnostic, through this gateway's structured log.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUpstreamDiagnosticIsNeverRelayedAndTheFixedDetailStandsInEitherWay()
    {
        OperationStatus carried = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_DB_ERROR,
            ErrorText = "检索失败",
        };

        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection withText =
            AssertProjects(new UpdateResponse { Status = carried });

        // THE UPSTREAM TEXT DOES NOT APPEAR - not as the whole detail, and not embedded in it.
        Assert.DoesNotContain("检索失败", withText.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(withText.Detail));

        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection withoutText =
            AssertProjects(Failing(RetCode.E_DB_ERROR));

        // AND THE ANSWER IS THE SAME EITHER WAY, which is the property that makes the detail an
        // allow-list rather than a preference: whether the upstream sent prose is not observable from
        // outside, so no upstream text can be inferred from the shape of the response.
        Assert.Equal(withoutText.Detail, withText.Detail);

        // The numeric outcome is preserved on both, because that is what a client branches on.
        Assert.Equal(withoutText.RetCode, withText.RetCode);
        Assert.Equal((long)RetCode.E_DB_ERROR, withText.RetCode);
    }

    /// <summary>
    /// The whole upstream message is never attached to a problem body, and the ONE member that is
    /// attached is the one the contract declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE SECOND HALF OF THE SAME DISCLOSURE, AND THE SHARPER HALF. Withholding the upstream's
    /// <c>detail</c> prose achieves nothing if the entire upstream response is serialised into the same
    /// body under a <c>response</c> extension - which is what the in-band failure renderer used to do. A
    /// whole upstream message is an unbounded, unreviewed payload under a member no contract declared, and
    /// "it is redacted before it arrives" was doing all the work in the argument for it: a disclosure
    /// control that depends on another service having got it right is not a control at this boundary.
    /// </para>
    /// <para>
    /// 🔴 <b>AND WHY THIS ROW NO LONGER BANS THE FORMATTER OUTRIGHT, WHICH IS A STRENGTHENING RATHER THAN A
    /// RELAXATION.</b> The previous form asserted that <c>ResponseFormatter.Format</c> appeared nowhere
    /// inside the renderer, as a proxy for "no payload is attached". That proxy was too strong in one
    /// direction and too weak in the other. Too strong, because it also forbade attaching the <c>dbError</c>
    /// member the contract DECLARES - and forbidding it is what left a caller whose <c>NOT NULL</c>
    /// violation was refused with no way to learn which column, the finding this file's production
    /// counterpart now closes. Too weak, because moving the identical call one method away satisfies it
    /// while changing nothing about the body: a guard that a rename defeats is not a guard.
    /// </para>
    /// <para>
    /// SO THE INVARIANT IS STATED DIRECTLY AND IN BOTH DIRECTIONS. The negative half stays a SOURCE guard
    /// because it must hold for every outcome and every operation, not only for failures a test can
    /// provoke: the retired member name appears nowhere, and nothing anywhere formats the RESPONSE into a
    /// problem extension. The positive half is asserted on the RENDERED DOCUMENT by the two rows below,
    /// which is the only place "exactly one extension, and it is <c>dbError</c>" can actually be observed.
    /// The conflict path is untouched and is asserted elsewhere; its <c>conflict</c> extension is the same
    /// kind of thing - a reviewed, schema-declared, bounded payload rather than a whole relayed message.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWholeUpstreamMessageIsNeverAttachedToAProblemBody()
    {
        string? source = LocateProxyEndpointSource();

        if (source is null)
        {
            // Out-of-tree artifacts path: the production sources are not there, and a locator failure
            // would report a test-environment problem as a code defect.
            return;
        }

        string text = File.ReadAllText(source);

        Assert.DoesNotContain(
            "Extensions[InBandStatus.ResponseExtensionMember]",
            text,
            StringComparison.Ordinal);

        // NOTHING ANYWHERE IN THE FILE RENDERS THE RESPONSE INTO A PROBLEM EXTENSION. The success renderer
        // formats the response into the RESPONSE BODY, which is its whole job, and does so through
        // `Results.Text`; what must not exist is the message being turned into a node for attachment. Both
        // spellings are named so that neither the direct call nor the payload helper can be handed a
        // response, wherever in the file it is written.
        Assert.DoesNotContain("ToDeclaredPayload(response", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "JsonNode.Parse(ResponseFormatter.Format(response",
            text,
            StringComparison.Ordinal);

        // AND THE MEMBERS THE FAILURE PATH MAY ATTACH ARE EXACTLY THE TWO THE CONTRACT DECLARES, NAMED.
        // The renderer itself attaches none directly - it delegates to AttachRefusalIdentity, which is the
        // one method holding the upstream message - so the enumeration is read from THAT body. Both members
        // are declared in gateway.v1.yaml's ProblemDetails and both are bounded, reviewed shapes: dbError
        // for a refusal the storage engine raised, validationErrors for one the DataWindow service's own
        // row validator raised. A third member added there fails this row by name rather than by a
        // coverage gap, and a whole-message relay cannot be spelled as either of these two constants.
        Assert.DoesNotContain(
            "problem.Extensions[",
            RendererBody(text),
            StringComparison.Ordinal);

        string[] attachments =
        [
            .. AttachBody(text)
                .Split('\n')
                .Select(static line => line.Trim())
                .Where(static line =>
                    line.StartsWith("problem.Extensions[", StringComparison.Ordinal)),
        ];

        Assert.Equal(2, attachments.Length);

        Assert.Contains(
            attachments,
            static line => line.StartsWith(
                "problem.Extensions[DatabaseErrorExtensionMember]",
                StringComparison.Ordinal));

        Assert.Contains(
            attachments,
            static line => line.StartsWith(
                "problem.Extensions[ValidationErrorsExtensionMember]",
                StringComparison.Ordinal));

        // AND THE SAME RULE FOLLOWS THE ONE METHOD THE RENDERER NOW DELEGATES TO, because a guard that
        // stopped at the renderer's own closing brace would be satisfied by moving the forbidden call one
        // frame down. AttachRefusalIdentity is the only method the failure path calls with the upstream
        // message in hand, and it is allowed to read NAMED FIELDS while being forbidden the whole-message
        // formatter - which is precisely the distinction between relaying an identity and relaying a
        // payload. This half of the guard was added with that method: the letter of the check above would
        // have passed without it.
        string attachBody = AttachBody(text);

        Assert.DoesNotContain("ResponseFormatter.Format", attachBody, StringComparison.Ordinal);

        // NOR THE UPSTREAM'S OWN PROSE. The per-row record's sixth field is a structured error whose
        // message is legacy operator text; relaying it would put upstream prose in a caller's body through
        // the very member added to keep prose out of it. Named here as the field accessor rather than as a
        // rendered string, so the guard fails on the attempt rather than on a payload that happens to
        // contain one.
        Assert.DoesNotContain("refusal.Error", attachBody, StringComparison.Ordinal);
    }

    /// <summary>Delimits the in-band failure renderer's own body in the production source.</summary>
    /// <param name="text">The production file's text.</param>
    /// <returns>The renderer's body.</returns>
    private static string RendererBody(string text)
    {
        int renderer = text.IndexOf(
            "internal static IResult RenderInBandFailure",
            StringComparison.Ordinal);

        Assert.True(renderer >= 0, "RenderInBandFailure was not found, so this guard is reading nothing.");

        int rendererEnd = text.IndexOf("\n    }", renderer, StringComparison.Ordinal);

        Assert.True(rendererEnd > renderer, "The renderer's body could not be delimited.");

        return text[renderer..rendererEnd];
    }

    /// <summary>
    /// Delimits the one method the failure path calls with the upstream message in hand.
    /// </summary>
    /// <param name="text">The production file's text.</param>
    /// <returns>That method's body.</returns>
    /// <remarks>
    /// A guard that stopped at the renderer's own closing brace would be satisfied by moving a forbidden
    /// call one frame down, which is why every rule this row states is applied here as well.
    /// </remarks>
    private static string AttachBody(string text)
    {
        int attach = text.IndexOf(
            "static void AttachRefusalIdentity",
            StringComparison.Ordinal);

        Assert.True(
            attach >= 0,
            "AttachRefusalIdentity was not found. Either it was renamed - in which case this guard must "
                + "follow it - or the failure path no longer reads the upstream message at all, in which "
                + "case the refusal has stopped naming the offending column.");

        int attachEnd = text.IndexOf("\n    }", attach, StringComparison.Ordinal);

        Assert.True(attachEnd > attach, "AttachRefusalIdentity's body could not be delimited.");

        return text[attach..attachEnd];
    }

    /// <summary>
    /// An in-band failure carrying a database payload publishes it under the declared <c>dbError</c>
    /// member, with the offending column legible and no other member added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS ROW IS THE FINDING. An insert nulling a <c>NOT NULL</c> column is refused by the storage
    /// engine, which answers an IN-BAND failure - transport OK, body carrying <c>E_INVALID_DATA</c> - so it
    /// arrives on the failure renderer and not on the success path that forwards a message whole. The
    /// renderer discarded the response, so the payload naming the column was thrown away at the last
    /// boundary after Persistence and DataServices had each carried it intact, and a caller received
    /// <c>400</c> with fixed prose and nothing to act on.
    /// </para>
    /// <para>
    /// THE COLUMN NAME IS ASSERTED OVER THE SERIALISED BODY rather than one member, matching how the
    /// end-to-end suite asserts it: which member carries the driver's text is an implementation detail of
    /// the relay, and the requirement is that the identity reaches the caller at all.
    /// </para>
    /// <para>
    /// AND THE EXTENSION SET IS ASSERTED EXACTLY. <c>retCode</c>, <c>upstream</c>, <c>traceId</c> and
    /// <c>dbError</c> - four members, every one declared in <c>gateway.v1.yaml</c>'s <c>ProblemDetails</c>,
    /// and no fifth. That is what stops the payload's arrival being accompanied by anything else, which is
    /// the half of the retired guard worth keeping.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADatabaseFailureIsPublishedUnderTheDeclaredMemberAndNamesTheColumn()
    {
        const string providerDiagnostic = "SQLite Error 19: 'NOT NULL constraint failed: COMPANY.AGE'.";

        DataServicesUpdateResponse response = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_INVALID_DATA,
            Error = new DbError
            {
                Sqldbcode = 1299L,
                Sqlerrtext = providerDiagnostic,

                // REDACTED BY THE SERVICE THAT OWNS THE REDACTION, and empty is the contract's own
                // documented common case - so an empty value here is the realistic input rather than a
                // convenience.
                Sqlsyntax = string.Empty,
                Buffer = WireDwBuffer.Primary,
                Row = 1L,
            },
        };

        ProblemDetails problem = RenderAndReadProblem(response);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.True(
            problem.Extensions.ContainsKey("dbError"),
            "The declared member must be present when the response carries a database payload.");

        Assert.DoesNotContain(
            InBandStatus.ResponseExtensionMember,
            problem.Extensions.Keys,
            StringComparer.Ordinal);

        Assert.Equal(
            ["dbError", "retCode", "traceId", "upstream"],
            [.. problem.Extensions.Keys.Order(StringComparer.Ordinal)]);

        string serialized = JsonSerializer.Serialize(problem.Extensions["dbError"]);

        Assert.Contains("AGE", serialized.ToUpperInvariant(), StringComparison.Ordinal);
        Assert.Contains("1299", serialized, StringComparison.Ordinal);
    }

    /// <summary>
    /// An in-band failure with no database payload publishes no <c>dbError</c> member at all.
    /// </summary>
    /// <remarks>
    /// THE PAIRED NEGATIVE, AND IT IS NOT A FORMALITY. A renderer that attached the member unconditionally
    /// would satisfy the row above and would publish a payload carrying a zero code, empty text and row
    /// zero on every unrelated failure - a database error that did not happen, which a consumer branching
    /// on the member's presence would act on. The same outcome code is used as the row above, so presence
    /// is decided by the PAYLOAD and demonstrably not by the status.
    /// </remarks>
    [Fact]
    public void AFailureWithoutADatabasePayloadPublishesNoDatabaseMember()
    {
        ProblemDetails problem = RenderAndReadProblem(Failing(RetCode.E_INVALID_DATA));

        Assert.DoesNotContain("dbError", problem.Extensions.Keys, StringComparer.Ordinal);
        Assert.Equal(
            ["retCode", "traceId", "upstream"],
            [.. problem.Extensions.Keys.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// A default-valued database payload is treated as ABSENT rather than published as an empty one.
    /// </summary>
    /// <remarks>
    /// A producer that assigned an all-defaults instance - which protobuf cannot distinguish from a
    /// deliberate one once it is set - would otherwise publish <c>dbError</c> saying a database failure
    /// occurred with no code, no text and no row. Emptiness is decided by the payload's own equality, so
    /// this row keeps holding when a member is added to the shape.
    /// </remarks>
    [Fact]
    public void ADefaultValuedDatabasePayloadIsTreatedAsAbsent()
    {
        DataServicesUpdateResponse response = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_INVALID_DATA,
            Error = new DbError(),
        };

        Assert.False(
            InBandStatus.TryReadDatabaseError(response, out _),
            "An all-defaults payload carries no diagnosis and must not be published as one.");

        Assert.DoesNotContain(
            "dbError",
            RenderAndReadProblem(response).Extensions.Keys,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The payload is located by its TYPE, so a same-named field of a different shape is never published
    /// as a database error.
    /// </summary>
    /// <remarks>
    /// <c>error</c> is the field name of the update response's <c>common.v1.DbError</c> AND of a
    /// <c>StructuredError</c> on several model responses - a different shape with different members. Matching
    /// the NAME would relay one under a member the contract declares as the other, which is a contract
    /// break that a schema with <c>additionalProperties: false</c> turns into a consumer failure.
    /// </remarks>
    [Fact]
    public void APayloadOfAnotherShapeUnderTheSameFieldNameIsNotPublished()
    {
        DropDownStateResponse response = new()
        {
            RetCode = (WireRetCode)(int)RetCode.E_INVALID_DATA,
            Error = new StructuredError { Text = "a structured error is not a database error" },
        };

        // The field is named `error` on this shape too, and it is populated - so a name-matched read would
        // find it.
        Assert.NotNull(response.Error);
        Assert.False(
            InBandStatus.TryReadDatabaseError(response, out _),
            "Only a common.v1.DbError may be published under the dbError member.");
    }

    /// <summary>
    /// Renders an in-band failure through the deployed renderer and returns the problem document it built.
    /// </summary>
    /// <param name="response">The upstream response to render.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// THE DEPLOYED RENDERER RATHER THAN A COPY OF ITS LOGIC, which is the point of these rows: the
    /// invariant is a property of the document this code path builds, so a reimplementation here would
    /// assert nothing about it. The context carries only what the renderer reads - logging, options and the
    /// request line.
    /// </remarks>
    private static ProblemDetails RenderAndReadProblem(Google.Protobuf.IMessage response)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.Configure<GatewayOptions>(static _ => { });

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/datawindow/update";

        StatusProjection failure = AssertProjects(response);

        IResult rendered = PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints
            .RenderInBandFailure(context, response, failure);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(rendered);

        return problem.ProblemDetails;
    }

    /// <summary>Locates the proxy endpoint source file, or null when no source tree is reachable.</summary>
    /// <returns>The absolute path, or <see langword="null"/>.</returns>
    private static string? LocateProxyEndpointSource()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "PowerFramework.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        string candidate = Path.Combine(
            directory.FullName,
            "services",
            "gateway-service",
            "PowerFramework.Gateway",
            "Endpoints",
            "DataServicesProxyEndpoints.cs");

        return File.Exists(candidate) ? candidate : null;
    }

    // ==============================================================================================
    //  THE REVERSE DIRECTION, WHICH HAS TO AGREE WITH THE FORWARD ONE
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

    /// <summary>
    /// The table above covers every arm the map classifies, so an arm added later cannot go unasserted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE COMPLEMENT THAT MAKES THE TABLE TOTAL RATHER THAN A SAMPLE, AND THE ROW THAT WOULD HAVE
    /// CAUGHT THE DIVERGENCE.</b> Every row above proves one arm answers what it should; none of them
    /// notices an arm the table FORGOT - which is exactly how six codes came to be classified on one
    /// surface and unclassified on the other with both suites green. This walks every negative
    /// <c>RetCode</c> constant the kernel declares, asks the map for it, and requires that anything the map
    /// classifies appears in the table above.
    /// </para>
    /// <para>
    /// IT READS THE THEORY'S OWN ROWS BY REFLECTION rather than a second list kept beside them, so the
    /// table and the guard cannot describe different sets and both pass. And it walks the KERNEL's
    /// constants rather than a list of codes anyone chose, so a failure code that acquires a status later is
    /// caught by this row on the day it is classified.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTableCoversEveryClassifiedArm()
    {
        MethodInfo theory = typeof(DataServicesProxyInBandStatusTests)
            .GetMethod(nameof(AFailingInBandOutcomeProjectsOntoItsPublishedStatus))!;

        // READ AS ATTRIBUTE METADATA RATHER THAN THROUGH THE FRAMEWORK'S OWN ROW EXPANSION, which needs a
        // disposal tracker and would couple this row to a test-framework API for no gain. Each row is
        // `InlineData(code, status)`, so the first constructor argument of the params array is the code.
        HashSet<long> tabled =
        [
            .. theory
                .GetCustomAttributesData()
                .Where(row => row.AttributeType == typeof(InlineDataAttribute))
                .Select(row => (IReadOnlyList<CustomAttributeTypedArgument>)row
                    .ConstructorArguments[0]
                    .Value!)
                .Select(arguments => Convert.ToInt64(arguments[0].Value, CultureInfo.InvariantCulture)),
        ];

        // The table is not empty, so a reflection change that found no rows cannot make this row vacuous.
        Assert.NotEmpty(tabled);

        List<string> unasserted = [];

        foreach (FieldInfo field in typeof(RetCode).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not long value || value >= 0L || tabled.Contains(value))
            {
                continue;
            }

            PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.StatusProjection projected =
                PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.InBandStatus.Project(
                    value,
                    errorText: null);

            if (projected.HttpStatus != StatusCodes.Status500InternalServerError)
            {
                unasserted.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{field.Name} ({value}) maps to {projected.HttpStatus} but is not in the table"));
            }
        }

        Assert.Empty(unasserted);
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
