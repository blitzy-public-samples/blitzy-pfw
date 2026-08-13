// ==================================================================================================
//  RefusalIdentityDisclosureTests.cs - THE TWO-SIDED DISCLOSURE RULE ON A REFUSED WRITE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  A caller whose row is refused must be told WHICH COLUMN failed, and must NOT be told anything
//  else the upstream happened to say. Those two requirements pull in opposite directions, and every
//  test in this file is about the line between them.
//
//  WHAT WAS WRONG, AS THE CALLER EXPERIENCED IT
//  A row omitting a value for `AGE INT NOT NULL` was refused with a correct status - HTTP 400, legacy
//  return code -9 - and a body that named nothing at all: fixed prose saying the data had been
//  refused, the numeric outcome, the upstream, a trace identifier. The failing column reached this
//  gateway (the storage engine names it, and the redaction rule upstream deliberately keeps
//  `NOT NULL constraint failed: COMPANY.AGE` legible because a column name is schema metadata) and
//  the in-band failure renderer discarded the whole upstream message on the way out. So the caller
//  was told its payload was wrong and not told which part - and the corrective action was available
//  to the caller and to nobody else. An operator could read the column out of a log; the client,
//  which is the party that has to change something, could not.
//
//  WHY THE OBVIOUS FIX IS WRONG, WHICH IS WHY THIS FILE IS LARGER THAN THE FIX
//  Attaching the upstream message would name the column. It would also attach `sqlsyntax` - the
//  complete generated statement, which carries interpolated literal VALUES because the legacy runs
//  without bind variables when `DisableBind` is set - through the system's only external ingress, in
//  a payload nobody screened. That extension existed once and was removed, and a source guard in
//  DataServicesProxyInBandStatusTests.cs stops it returning. So the requirement is not "relay more",
//  it is "relay the identity and nothing else", and these tests pin both halves:
//
//    1. THE IDENTITY ARRIVES. The column name, ordinal, type, buffer and one-based row reach the
//       caller, from both upstream paths - the storage engine's diagnostic and the DataWindow
//       service's own row validator.
//    2. THE DATA DOES NOT. No column value, no generated statement, and none of the upstream's own
//       prose. The statement member is emitted EMPTY rather than omitted, because the published
//       shape is closed and requires it.
//    3. THE RELAY IS BOUNDED. Element count and per-string length are capped by this gateway rather
//       than by whatever an upstream produced.
//    4. NOTHING IS FABRICATED. A failure carrying no diagnostic and no per-row record attaches
//       neither member, so absence still means "not told" rather than "told there was none".
//
//  WHY THE METHOD IS DRIVEN DIRECTLY RATHER THAN THROUGH A ROUTE
//  What must hold is which members CAN appear for a given upstream message. Driving HTTP would test
//  whichever refusal a fake upstream could be provoked into producing, which is precisely the
//  coverage shape that let the whole-message extension survive unnoticed. The runtime behaviour is
//  covered end to end by the DataWindow workflow spec against a real stack; this file covers the
//  allow-list itself.
// ==================================================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Asserts that a refused write names the offending column and discloses nothing else.
/// </summary>
public sealed class RefusalIdentityDisclosureTests
{
    /// <summary>The extension member carrying the storage engine's diagnostic identity.</summary>
    private const string DbErrorMember = "dbError";

    /// <summary>The extension member carrying per-row validation identity.</summary>
    private const string ValidationErrorsMember = "validationErrors";

    /// <summary>The column the fixture refuses, matching the estate's one `NOT NULL` integer column.</summary>
    private const string RefusedColumn = "AGE";

    /// <summary>
    /// A value the caller sent, which must never come back. Chosen to match the workflow spec's own
    /// fixture so the two files refuse the same disclosure.
    /// </summary>
    private const string CallerSuppliedValue = "Texas";

    /// <summary>
    /// The provider's condition line as it reaches this gateway: the column legible, row data masked.
    /// </summary>
    /// <remarks>
    /// THE SHAPE IS THE MEASURED ONE rather than an invented sentence. The provider composes
    /// <c>SQLite Error 19: '&lt;message&gt;'.</c> and the upstream redaction rule masks what is quoted
    /// INSIDE the message while leaving the envelope's condition line intact - which is why the column
    /// survives to here at all. A test that used a tidier string would not exercise the case that failed.
    /// </remarks>
    private const string ProviderDiagnostic =
        "SQLite Error 19: 'NOT NULL constraint failed: COMPANY.AGE'.";

    /// <summary>
    /// The storage engine's refusal reaches the caller with the column named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THE FINDING WAS RAISED ON. Every member asserted here is schema metadata or the caller's
    /// own addressing of the row it just sent, so none of it discloses anything the caller did not
    /// already have - which is the argument for relaying it at the ingress at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStorageRefusalNamesTheOffendingColumn()
    {
        ProblemDetails problem = Attach(new UpdateResponse
        {
            Error = new DbError
            {
                Sqldbcode = 19L,
                Sqlerrtext = ProviderDiagnostic,
                Sqlsyntax = "INSERT INTO COMPANY (NAME, AGE) VALUES ('pfw-e2e-invalid-0', NULL)",
                Buffer = DwBuffer.Primary,
                Row = 1L,
            },
        });

        JsonObject dbError = RequireObject(problem, DbErrorMember);

        // THE COLUMN IS NAMED. This is the assertion the finding turns on.
        Assert.Contains(
            RefusedColumn,
            dbError["sqlerrtext"]!.GetValue<string>(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(19L, dbError["sqldbcode"]!.GetValue<long>());

        // ONE-BASED, VERBATIM. Legacy contract, not an off-by-one to normalise.
        Assert.Equal(1L, dbError["row"]!.GetValue<long>());

        // 🔴 THE PUBLISHED ENUMERATOR NAME, NOT THE C# ONE. `DwBuffer.Primary.ToString()` is "Primary";
        // the contract's DwBuffer schema enumerates "DW_BUFFER_PRIMARY", which is what the canonical
        // protobuf JSON mapping emits and what every other buffer-carrying member of every other body in
        // this contract already spells. The C# spelling was produced and observed against a running stack
        // before being corrected, so this row is a regression test rather than a convention check.
        Assert.Equal("DW_BUFFER_PRIMARY", dbError["buffer"]!.GetValue<string>());

        // 🔴 AND THE STATEMENT IS GONE, though the upstream sent one. Emitted EMPTY rather than omitted,
        // because the published DbError shape is closed and requires the member - so a body that dropped
        // it would not validate against the contract this gateway publishes.
        Assert.Equal(string.Empty, dbError["sqlsyntax"]!.GetValue<string>());
    }

    /// <summary>
    /// The DataWindow service's own row validator refusal reaches the caller as identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND UPSTREAM PATH TO THE SAME HTTP STATUS, AND IT NEEDED ITS OWN MEMBER. A refusal arrives
    /// as <c>E_INVALID_DATA</c> from either the storage engine or the row validator, and both used to be
    /// answered with identical prose naming nothing. A client cannot be asked to know which layer refused
    /// it, so both layers have to produce an actionable body.
    /// </para>
    /// </remarks>
    [Fact]
    public void AValidatorRefusalCarriesTheColumnIdentityForEveryRefusedRow()
    {
        ProblemDetails problem = Attach(new UpdateResponse
        {
            ValidationErrors =
            {
                new RowValidationError
                {
                    Buffer = DwBuffer.Primary,
                    Row = 1L,
                    ColumnName = "age",
                    ColumnId = 3L,
                    ColumnType = "long",
                },
                new RowValidationError
                {
                    Buffer = DwBuffer.Filter,
                    Row = 7L,
                    ColumnName = "birth",
                    ColumnId = 6L,
                    ColumnType = "date",
                },
            },
        });

        JsonArray refusals = RequireArray(problem, ValidationErrorsMember);

        Assert.Equal(2, refusals.Count);

        JsonObject first = Assert.IsType<JsonObject>(refusals[0]);

        Assert.Equal("age", first["columnName"]!.GetValue<string>());
        Assert.Equal(3L, first["columnId"]!.GetValue<long>());
        Assert.Equal("long", first["columnType"]!.GetValue<string>());
        Assert.Equal(1L, first["row"]!.GetValue<long>());
        Assert.Equal("DW_BUFFER_PRIMARY", first["buffer"]!.GetValue<string>());

        JsonObject second = Assert.IsType<JsonObject>(refusals[1]);

        // THE FILTER BUFFER IS CARRIED AS-IS, including the fact that its row order is inverted relative
        // to the source. Normalising it here would be this gateway reinterpreting a legacy semantic.
        Assert.Equal("DW_BUFFER_FILTER", second["buffer"]!.GetValue<string>());
        Assert.Equal("birth", second["columnName"]!.GetValue<string>());

        // EXACTLY FIVE MEMBERS PER ELEMENT, ASSERTED AS AN ALLOW-LIST RATHER THAN AS A DENY-LIST. An
        // allow-list also catches a member nobody thought to forbid, which is the failure mode that
        // matters here: the upstream record carries a sixth field, its own structured error, whose message
        // is legacy operator prose.
        foreach (JsonNode? element in refusals)
        {
            JsonObject entry = Assert.IsType<JsonObject>(element);

            Assert.Equal(
                new[] { "buffer", "columnId", "columnName", "columnType", "row" },
                entry.Select(static member => member.Key).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The upstream's own diagnostic prose is never relayed on the validator path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HALF THAT KEEPS "NAME THE COLUMN" FROM HAVING WIDENED INTO "RELAY THE MESSAGE". The fixed
    /// detail this gateway authors must stay the only prose in the body, so that no upstream text reaches
    /// a caller and none can be inferred from the response shape. The row is driven with prose that would
    /// be unmistakable if it leaked.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUpstreamsOwnProseIsNeverRelayed()
    {
        const string UpstreamProse = "\u68c0\u7d22\u5931\u8d25 - upstream operator prose";

        ProblemDetails problem = Attach(new UpdateResponse
        {
            ValidationErrors =
            {
                new RowValidationError
                {
                    Buffer = DwBuffer.Primary,
                    Row = 1L,
                    ColumnName = "age",
                    ColumnId = 3L,
                    ColumnType = "long",
                    Error = new StructuredError { Text = UpstreamProse },
                },
            },
        });

        string body = Serialize(problem);

        Assert.DoesNotContain(UpstreamProse, body, StringComparison.Ordinal);

        // The identity still arrived, so this row is about the omission rather than about the member
        // being empty.
        Assert.Contains("\"columnName\":\"age\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value the caller sent is never echoed back, from either upstream path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DISCLOSURE LINE, STATED AS ITS OWN ROW. A value may not have originated with this caller and
    /// the generated statement interpolates literals, so neither crosses. The fixture sends a statement
    /// containing a caller-supplied value precisely so that a future revision which relayed
    /// <c>sqlsyntax</c> "because the upstream redacts it" fails here.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoValueTheCallerSentIsEchoedBack()
    {
        ProblemDetails problem = Attach(new UpdateResponse
        {
            Error = new DbError
            {
                Sqldbcode = 19L,
                Sqlerrtext = ProviderDiagnostic,
                Sqlsyntax =
                    "INSERT INTO COMPANY (NAME, AGE, ADDRESS) VALUES "
                    + "('pfw-e2e-invalid-0', NULL, '" + CallerSuppliedValue + "')",
                Buffer = DwBuffer.Primary,
                Row = 1L,
            },
        });

        Assert.DoesNotContain(CallerSuppliedValue, Serialize(problem), StringComparison.Ordinal);
    }

    /// <summary>
    /// The relayed collection is bounded by this gateway rather than by the upstream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN UNBOUNDED RELAY IS A RESPONSE SIZE THE CALLER CONTROLS by sending a larger payload, and this is
    /// the system's only external ingress. The bound is asserted at one over it so the row fails if the
    /// cap is removed, and the surviving elements are asserted to be the FIRST ones so a future revision
    /// cannot satisfy the count by taking an arbitrary window.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRelayedRefusalCollectionIsCapped()
    {
        UpdateResponse response = new();

        for (int row = 1; row <= 33; row++)
        {
            response.ValidationErrors.Add(new RowValidationError
            {
                Buffer = DwBuffer.Primary,
                Row = row,
                ColumnName = "age",
                ColumnId = 3L,
                ColumnType = "long",
            });
        }

        JsonArray refusals = RequireArray(Attach(response), ValidationErrorsMember);

        Assert.Equal(32, refusals.Count);

        // THE FIRST ELEMENTS, IN ORDER.
        Assert.Equal(1L, Assert.IsType<JsonObject>(refusals[0])["row"]!.GetValue<long>());
        Assert.Equal(32L, Assert.IsType<JsonObject>(refusals[31])["row"]!.GetValue<long>());
    }

    /// <summary>
    /// A single pathological diagnostic string cannot become the response.
    /// </summary>
    /// <remarks>
    /// THE SECOND BOUND, ON A DIFFERENT AXIS FROM THE ELEMENT CAP: that one bounds how many records
    /// cross, this one how large one relayed string may be. Both are needed, because either alone leaves
    /// the other unbounded.
    /// </remarks>
    [Fact]
    public void ARelayedDiagnosticStringIsLengthBounded()
    {
        ProblemDetails problem = Attach(new UpdateResponse
        {
            Error = new DbError
            {
                Sqldbcode = 19L,
                Sqlerrtext = new string('x', 4096),
                Buffer = DwBuffer.Primary,
                Row = 1L,
            },
        });

        Assert.Equal(512, RequireObject(problem, DbErrorMember)["sqlerrtext"]!.GetValue<string>().Length);
    }

    /// <summary>
    /// A failure the upstream reported without any identity attaches neither member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ABSENCE MUST KEEP MEANING "NOT TOLD". Attaching an empty collection to make the shape uniform
    /// would assert that the upstream reported no failing column, which is a different claim and a false
    /// one - and a client branching on the member's presence would read a silent upstream as a clean row.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFailureCarryingNoIdentityAttachesNothing()
    {
        ProblemDetails problem = Attach(new UpdateResponse());

        Assert.False(problem.Extensions.ContainsKey(DbErrorMember));
        Assert.False(problem.Extensions.ContainsKey(ValidationErrorsMember));
    }

    /// <summary>
    /// A message of any other type attaches nothing, so no other operation's body can grow a member.
    /// </summary>
    /// <remarks>
    /// THE DISPATCH IS A TYPE TEST RATHER THAN A DESCRIPTOR WALK, and this row is what pins that. A
    /// reflective implementation would attach whatever it happened to find on any message that had a
    /// similarly named field, which is the unbounded behaviour this whole design exists to avoid.
    /// </remarks>
    [Fact]
    public void AMessageOfAnotherTypeAttachesNothing()
    {
        ProblemDetails problem = Attach(new RetrieveRequest { DatawindowHandle = "dw_sqlite" });

        Assert.Empty(problem.Extensions);
    }

    /// <summary>Runs the allow-list against one upstream message and returns the body it produced.</summary>
    /// <param name="response">The upstream message.</param>
    /// <returns>The problem body, carrying only what the allow-list attached.</returns>
    /// <remarks>
    /// A BARE PROBLEM IS USED rather than one built by the gateway's own builder, so that every member
    /// present afterwards was attached by the method under test and nothing is inherited.
    /// </remarks>
    private static ProblemDetails Attach(Google.Protobuf.IMessage response)
    {
        ProblemDetails problem = new();

        PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints.AttachRefusalIdentity(
            problem,
            response);

        return problem;
    }

    /// <summary>Requires an extension member to be present as an object.</summary>
    /// <param name="problem">The body.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member.</returns>
    private static JsonObject RequireObject(ProblemDetails problem, string member)
    {
        Assert.True(
            problem.Extensions.TryGetValue(member, out object? value),
            $"The problem body carries no '{member}' member, so the refusal names nothing.");

        return Assert.IsType<JsonObject>(value);
    }

    /// <summary>Requires an extension member to be present as an array.</summary>
    /// <param name="problem">The body.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member.</returns>
    private static JsonArray RequireArray(ProblemDetails problem, string member)
    {
        Assert.True(
            problem.Extensions.TryGetValue(member, out object? value),
            $"The problem body carries no '{member}' member, so the refusal names nothing.");

        return Assert.IsType<JsonArray>(value);
    }

    /// <summary>Serializes a problem body so an assertion can be made over the WHOLE of it.</summary>
    /// <param name="problem">The body.</param>
    /// <returns>The serialized body.</returns>
    /// <remarks>
    /// THE WHOLE BODY RATHER THAN ONE MEMBER, for the disclosure rows. Which member would carry a leaked
    /// value is an implementation detail of the relay; that no member carries it is the requirement, and
    /// only a whole-body assertion states that.
    /// </remarks>
    private static string Serialize(ProblemDetails problem) =>
        JsonSerializer.Serialize(problem.Extensions);
}
