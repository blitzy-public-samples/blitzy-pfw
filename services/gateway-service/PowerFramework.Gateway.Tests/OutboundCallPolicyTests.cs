// ==================================================================================================
//  PowerFramework.Gateway.Tests.OutboundCallPolicyTests
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   The retry-admission, circuit-breaking and deadline policy Gateway applies to
//  the calls it makes outward: Clients/OutboundCallPolicy.cs, the deadline attached by
//  Clients/DataServicesClient.cs, and the fact that Program.ConfigureOutboundResilience actually
//  installs both predicates on all three outbound clients rather than merely being able to.
//
//  WHY IT IS WORTH A SUITE OF ITS OWN. The defect this replaces was not a wrong value; it was a policy
//  that could not see what it was deciding about. All three outbound clients called
//  AddStandardResilienceHandler() with NO configuration at all, and an HTTP-level pipeline cannot read
//  a `grpc-status`: a call the server REFUSED arrives as HTTP 200, so a server-declared Unavailable was
//  never retried, while a TRANSPORT fault arrived as an exception and was retried on every method -
//  including Update, whose replay after an unknown outcome is the silent double-apply that contract
//  C-06 exists to prevent, and including every session open, whose replay leaves a second server-held
//  session nobody holds a handle to. Both halves are invisible in a passing build.
//
//  THE SUITE IS IN THREE PARTS, and the third is the one that matters most: the decision, the
//  deadline, and then the WIRING - because parts one and two would both pass with the predicates
//  written and never installed, which is exactly the shape of the original defect.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The retry, circuit-breaking and deadline policy for Gateway's outbound calls.
/// </summary>
public sealed class OutboundCallPolicyTests
{
    /// <summary>A path Gateway classifies as replay-safe: a pure read of the event gate.</summary>
    private const string SafePath = "/dataservices.v1.DataWindowService/GetEventGate";

    /// <summary>
    /// A path Gateway classifies as unsafe: the update itself, whose replay is the silent double-apply
    /// that contract C-06 exists to prevent.
    /// </summary>
    private const string UnsafePath = "/dataservices.v1.DataWindowService/Update";

    /// <summary>
    /// Every gRPC status, paired with whether a replay-safe operation may be retried for it.
    /// </summary>
    /// <remarks>
    /// ENUMERATED RATHER THAN SAMPLED, because the policy IS this table and a sampled test would let a
    /// later edit quietly admit a status nobody chose to admit.
    /// </remarks>
    public static TheoryData<StatusCode, bool> GrpcStatusAdmission => new()
    {
        { StatusCode.OK, false },
        { StatusCode.Cancelled, false },
        { StatusCode.Unknown, false },
        { StatusCode.InvalidArgument, false },
        { StatusCode.DeadlineExceeded, false },
        { StatusCode.NotFound, false },
        { StatusCode.AlreadyExists, false },
        { StatusCode.PermissionDenied, false },
        { StatusCode.ResourceExhausted, true },
        { StatusCode.FailedPrecondition, false },
        { StatusCode.Aborted, false },
        { StatusCode.OutOfRange, false },
        { StatusCode.Unimplemented, false },
        { StatusCode.Internal, false },
        { StatusCode.Unavailable, true },
        { StatusCode.DataLoss, false },
        { StatusCode.Unauthenticated, false },
    };

    /// <summary>
    /// Every gRPC status, paired with whether it counts against the circuit breaker.
    /// </summary>
    /// <remarks>
    /// NARROWER THAN THE RETRY TABLE BY EXACTLY ONE ROW. Resource exhaustion is retryable because it
    /// clears, but it is a HEALTHY refusal aimed at one caller's quota, so counting it would let one
    /// caller's over-use open the breaker and deny the read path to everybody.
    /// </remarks>
    public static TheoryData<StatusCode, bool> GrpcStatusBreaking => new()
    {
        { StatusCode.OK, false },
        { StatusCode.Cancelled, false },
        { StatusCode.Unknown, false },
        { StatusCode.InvalidArgument, false },
        { StatusCode.DeadlineExceeded, false },
        { StatusCode.NotFound, false },
        { StatusCode.AlreadyExists, false },
        { StatusCode.PermissionDenied, false },
        { StatusCode.ResourceExhausted, false },
        { StatusCode.FailedPrecondition, false },
        { StatusCode.Aborted, false },
        { StatusCode.OutOfRange, false },
        { StatusCode.Unimplemented, false },
        { StatusCode.Internal, false },
        { StatusCode.Unavailable, true },
        { StatusCode.DataLoss, false },
        { StatusCode.Unauthenticated, false },
    };

    /// <summary>
    /// Every operation of the two DataServices contracts, paired with whether a transport attempt may
    /// be replayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE WHOLE SURFACE, not the interesting parts, because an operation nobody classified inherits
    /// "attempted exactly once" - the safe default, but a silent one, and a table that omitted a method
    /// could not tell a deliberate exclusion from a forgotten one.
    /// </para>
    /// <para>
    /// The rows worth reading twice: <c>CloseValidationSession</c> is admitted while
    /// <c>OpenValidationSession</c> is not, because a replayed close is idempotent by contract - which
    /// is exactly what stops a lost close response from leaking a session - while a replayed open
    /// SUCCEEDS and leaves a second session behind. <c>ApplyContextMenuModel</c> is excluded even though
    /// its sibling applies are admitted, because whether it replaces or extends is a property of the
    /// REQUEST and a message handler cannot read the request body. And the <c>Set*</c> and <c>Calc*</c>
    /// families are excluded because a recalculation can invoke macros back across the inverted
    /// channel, so a replay is observable in the caller's own macro handler as an invocation it never
    /// asked for.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool> OperationAdmission
    {
        get
        {
            TheoryData<string, bool> data = [];

            string dataWindow = DataWindowService.Descriptor.FullName;

            Add(dataWindow, "Retrieve", replaySafe: false);
            Add(dataWindow, "OpenValidationSession", replaySafe: false);
            Add(dataWindow, "CloseValidationSession", replaySafe: true);
            Add(dataWindow, "EventChain", replaySafe: false);
            Add(dataWindow, "Update", replaySafe: false);
            Add(dataWindow, "GetEventGate", replaySafe: true);
            Add(dataWindow, "DisableEvent", replaySafe: false);
            Add(dataWindow, "EnableEvent", replaySafe: false);
            Add(dataWindow, "GetDropDownSearchState", replaySafe: true);
            Add(dataWindow, "ApplyDropDownSearch", replaySafe: false);
            Add(dataWindow, "GetColumnSortState", replaySafe: true);
            Add(dataWindow, "ApplyColumnSort", replaySafe: false);
            Add(dataWindow, "GetContextMenuModel", replaySafe: true);
            Add(dataWindow, "ApplyContextMenuModel", replaySafe: false);
            Add(dataWindow, "GetRowSelectState", replaySafe: true);
            Add(dataWindow, "ApplyRowSelectStyle", replaySafe: false);

            string columnExpression = ColumnExpressionService.Descriptor.FullName;

            Add(columnExpression, "OpenExpressionSession", replaySafe: false);
            Add(columnExpression, "CloseExpressionSession", replaySafe: true);
            Add(columnExpression, "AddExpression", replaySafe: false);
            Add(columnExpression, "SetExpression", replaySafe: false);
            Add(columnExpression, "GetExpression", replaySafe: true);
            Add(columnExpression, "RemoveExpression", replaySafe: false);
            Add(columnExpression, "RemoveAllExpressions", replaySafe: false);
            Add(columnExpression, "AddVariable", replaySafe: false);
            Add(columnExpression, "SetVariable", replaySafe: false);
            Add(columnExpression, "AddVariableExpression", replaySafe: false);
            Add(columnExpression, "SetVariableExpression", replaySafe: false);
            Add(columnExpression, "GetVariableExpression", replaySafe: true);
            Add(columnExpression, "AddForeignVariable", replaySafe: false);
            Add(columnExpression, "SetRelativeColumns", replaySafe: false);
            Add(columnExpression, "SetExpressionFlag", replaySafe: false);
            Add(columnExpression, "Calc", replaySafe: false);
            Add(columnExpression, "CalcAll", replaySafe: false);
            Add(columnExpression, "CalcEmpty", replaySafe: false);
            Add(columnExpression, "CalcItem", replaySafe: false);
            Add(columnExpression, "SetEnabled", replaySafe: false);
            Add(columnExpression, "SetTrace", replaySafe: false);
            Add(columnExpression, "GetServiceState", replaySafe: true);
            Add(columnExpression, "GetExpressionState", replaySafe: true);
            Add(columnExpression, "EventStream", replaySafe: false);
            Add(columnExpression, "InvokeMethodChannel", replaySafe: false);
            Add(columnExpression, "TraceChannel", replaySafe: false);

            return data;

            void Add(string contract, string method, bool replaySafe) =>
                data.Add(string.Concat("/", contract, "/", method), replaySafe);
        }
    }

    /// <summary>
    /// The Security REST paths, paired with whether a transport attempt may be replayed.
    /// </summary>
    /// <remarks>
    /// Gateway itself calls only the issuance endpoint, which is excluded; the crypto paths are
    /// classified so the table describes the whole outbound surface of the named Security client rather
    /// than only the part Gateway exercises today, and so a future consumer inherits the classification
    /// instead of the unclassified default.
    /// </remarks>
    public static TheoryData<string, bool> SecurityPathAdmission => new()
    {
        { "/v1/tokens", false },
        { "/v1/crypto/hash", true },
        { "/v1/crypto/hmac", true },
        { "/v1/crypto/hash-file", true },
        { "/v1/crypto/hmac-file", true },
        { "/v1/crypto/symmetric/encrypt", true },
        { "/v1/crypto/symmetric/decrypt", true },
        { "/v1/crypto/rsa/encrypt", true },
        { "/v1/crypto/rsa/decrypt", true },
        { "/v1/crypto/rsa/sign", true },
        { "/v1/crypto/rsa/verify", true },
        { "/v1/crypto/rsa/keys", false },
        { "/v1/crypto/random/blob", false },
        { "/v1/crypto/random/string", false },
        { "/v1/crypto/random/guid", false },
        { "/v1/crypto/encoding/string-to-blob", true },
        { "/v1/crypto/encoding/blob-to-string", true },
        { "/v1/crypto/encoding/blob-reverse", true },
    };

    // ==============================================================================================
    //  1. THE DECISION
    // ==============================================================================================

    /// <summary>
    /// A replay-safe operation is retried for exactly the two transient statuses and no others.
    /// </summary>
    [Theory]
    [MemberData(nameof(GrpcStatusAdmission))]
    public async Task A_safe_operation_is_retried_only_for_the_admitted_statuses(
        StatusCode status,
        bool expected) =>
        Assert.Equal(expected, await ShouldRetryAsync(SafePath, status));

    /// <summary>
    /// An unsafe operation is never retried, whatever the upstream said.
    /// </summary>
    /// <remarks>
    /// THE OPERATION IS CHECKED BEFORE THE OUTCOME IS, WHICH IS WHY THIS HOLDS FOR EVERY ROW. Reverse
    /// the order and an Unavailable on an Update is retried - and an update replayed after an unknown
    /// outcome is the double-apply C-06 exists to prevent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GrpcStatusAdmission))]
    public async Task An_unsafe_operation_is_never_retried_whatever_the_status(
        StatusCode status,
        bool admittedForSafeOperations)
    {
        _ = admittedForSafeOperations;

        Assert.False(await ShouldRetryAsync(UnsafePath, status));
    }

    /// <summary>
    /// Only an upstream that cannot serve counts against the circuit breaker.
    /// </summary>
    [Theory]
    [MemberData(nameof(GrpcStatusBreaking))]
    public async Task Only_an_unserviceable_upstream_counts_against_the_breaker(
        StatusCode status,
        bool expected)
    {
        Assert.Equal(expected, await ShouldBreakAsync(SafePath, status));

        // The unsafe path answers identically: whether the UPSTREAM is healthy is a property of the
        // upstream and not of the call that discovered it, so unlike retry the breaker is not gated on
        // the operation. A test that drove only the safe path could not tell the two designs apart.
        Assert.Equal(expected, await ShouldBreakAsync(UnsafePath, status));
    }

    /// <summary>
    /// Every operation of the two DataServices contracts carries its stated classification.
    /// </summary>
    [Theory]
    [MemberData(nameof(OperationAdmission))]
    public async Task Every_dataservices_operation_carries_its_stated_classification(
        string path,
        bool replaySafe) =>
        // Unavailable is the probe because it is admitted for a safe operation, so the answer here is
        // decided entirely by the classification of the path.
        Assert.Equal(replaySafe, await ShouldRetryAsync(path, StatusCode.Unavailable));

    /// <summary>
    /// Every Security REST path carries its stated classification.
    /// </summary>
    /// <remarks>
    /// A REST response carries no gRPC status, so these rows exercise the third arm of the predicate -
    /// the package's own transient definition - rather than the status table.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SecurityPathAdmission))]
    public async Task Every_security_path_carries_its_stated_classification(string path, bool replaySafe)
    {
        ResilienceContext context = Context(path);

        bool retried = await OutboundCallPolicy.ShouldRetryAsync(
            new RetryPredicateArguments<HttpResponseMessage>(
                context,
                Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                0));

        Assert.Equal(replaySafe, retried);
    }

    /// <summary>
    /// A transport fault on a safe operation is retried; on an unsafe one it is not.
    /// </summary>
    /// <remarks>
    /// THIS IS THE HALF THE ORIGINAL POLICY GOT BACKWARDS. A reset connection arrives as an exception,
    /// which every stock transient predicate treats as retryable - and every gRPC call is a POST, so a
    /// method-based gate could not distinguish an Update from a read. The exception arm therefore has to
    /// consult the same classification the response arm does, and this drives it through the path where
    /// there is no response to read a status from.
    /// </remarks>
    [Theory]
    [InlineData(SafePath, true)]
    [InlineData(UnsafePath, false)]
    public async Task A_transport_fault_is_replayed_only_on_a_safe_operation(string path, bool expected)
    {
        bool retried = await OutboundCallPolicy.ShouldRetryAsync(
            new RetryPredicateArguments<HttpResponseMessage>(
                Context(path),
                Outcome.FromException<HttpResponseMessage>(new HttpRequestException("reset")),
                0));

        Assert.Equal(expected, retried);
    }

    /// <summary>
    /// A successful call is not retried, whether it declares status zero or no status at all.
    /// </summary>
    [Fact]
    public async Task A_successful_call_is_not_retried()
    {
        Assert.False(await ShouldRetryAsync(SafePath, StatusCode.OK));

        // Status zero and "no status" are different inputs that must both answer no; a parse that
        // defaulted to zero would conflate them.
        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    Context(SafePath),
                    Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                    0)));
    }

    /// <summary>
    /// An unrecognised operation is attempted exactly once.
    /// </summary>
    /// <remarks>
    /// This is what makes a contract addition safe by default: a method nobody classified falls to
    /// "attempted exactly once" rather than inheriting a replay.
    /// </remarks>
    [Fact]
    public async Task An_unclassified_operation_is_attempted_exactly_once()
    {
        Assert.False(
            await ShouldRetryAsync(
                "/dataservices.v1.DataWindowService/NoSuchMethod",
                StatusCode.Unavailable));

        ResilienceContext bare = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);

        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    bare,
                    Outcome.FromResult(TrailersOnly(StatusCode.Unavailable)),
                    0)));
    }

    /// <summary>
    /// A malformed status header is treated as no status rather than as status zero.
    /// </summary>
    /// <remarks>
    /// Status zero is OK, so a parse that defaulted to it would read a malformed refusal as a success.
    /// Falling through instead lets the transport-level definition decide - and for a 503 that admits
    /// the retry, which is what distinguishes the two designs.
    /// </remarks>
    [Fact]
    public async Task A_malformed_status_header_falls_through_rather_than_reading_as_success()
    {
        HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("grpc-status", "not-a-number");

        Assert.True(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    Context(SafePath),
                    Outcome.FromResult(response),
                    0)));
    }

    /// <summary>
    /// A status carried in the trailers is read as well as one carried in the headers.
    /// </summary>
    [Fact]
    public async Task A_status_in_the_trailers_is_read_too()
    {
        HttpResponseMessage unavailable = new(HttpStatusCode.OK);
        unavailable.TrailingHeaders.TryAddWithoutValidation("grpc-status", StatusValue(StatusCode.Unavailable));

        Assert.True(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    Context(SafePath),
                    Outcome.FromResult(unavailable),
                    0)));

        HttpResponseMessage aborted = new(HttpStatusCode.OK);
        aborted.TrailingHeaders.TryAddWithoutValidation("grpc-status", StatusValue(StatusCode.Aborted));

        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    Context(SafePath),
                    Outcome.FromResult(aborted),
                    0)));
    }

    /// <summary>
    /// A URI the predicate cannot classify is refused rather than allowed to throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PREDICATE THAT THROWS IS WORSE THAN ONE THAT ANSWERS WRONGLY, because the exception replaces
    /// the failure the pipeline was deciding about: the caller is told about a policy fault instead of
    /// about its own upstream, and the original status is lost. The first written form of this policy
    /// read <c>RequestUri.AbsolutePath</c> directly, which THROWS for a relative URI - unreachable in
    /// the deployed pipeline, which always presents an absolute URI, and therefore entirely invisible
    /// until a test presented the other form.
    /// </para>
    /// <para>
    /// Both forms must also AGREE, or the answer would depend on which one the caller happened to build.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_uri_the_predicate_cannot_classify_is_refused_rather_than_thrown()
    {
        Assert.True(await ShouldRetryForUriAsync(new Uri(SafePath, UriKind.Relative), StatusCode.Unavailable));
        Assert.False(await ShouldRetryForUriAsync(new Uri(UnsafePath, UriKind.Relative), StatusCode.Unavailable));

        Assert.True(
            await ShouldRetryForUriAsync(
                new Uri(SafePath + "?trace=1", UriKind.Relative),
                StatusCode.Unavailable));

        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage());

        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(TrailersOnly(StatusCode.Unavailable)),
                    0)));
    }

    /// <summary>
    /// The classification table resolves against the shipped contracts.
    /// </summary>
    /// <remarks>
    /// The table is built from the descriptors, so a renamed or removed method throws while it is
    /// built; the verification entry point is what turns that into a startup failure rather than a
    /// first-failure surprise. This asserts it answers rather than throws for the contracts as shipped.
    /// </remarks>
    [Fact]
    public void The_classification_table_resolves_against_the_shipped_contracts() =>
        // 11 gRPC operations across the two contracts plus 13 Security crypto paths.
        Assert.Equal(24, OutboundCallPolicy.Verify());

    // ==============================================================================================
    //  2. THE DEADLINE
    // ==============================================================================================

    /// <summary>
    /// Each call class is bounded by its own duration, measured from the injected clock.
    /// </summary>
    [Fact]
    public void Each_call_class_is_bounded_by_its_own_duration()
    {
        StoppedClock clock = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        OutboundDeadlines deadlines = new(TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(11), clock);

        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 12, DateTimeKind.Utc),
            deadlines.DeadlineFor(OutboundCallClass.Unary));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 15, 5, DateTimeKind.Utc),
            deadlines.DeadlineFor(OutboundCallClass.Stream));

        // UTC, because gRPC requires it and a local-kind deadline would be silently misread as UTC.
        Assert.Equal(DateTimeKind.Utc, deadlines.DeadlineFor(OutboundCallClass.Unary).Kind);
    }

    /// <summary>
    /// An unrecognised call class is bounded by the tighter of the two rather than left unbounded.
    /// </summary>
    [Fact]
    public void An_unrecognised_call_class_takes_the_tighter_bound()
    {
        StoppedClock clock = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        OutboundDeadlines deadlines = new(TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(11), clock);

        Assert.Equal(
            deadlines.DeadlineFor(OutboundCallClass.Unary),
            deadlines.DeadlineFor((OutboundCallClass)99));
    }

    /// <summary>
    /// A non-positive duration is refused, because it would expire before the request left.
    /// </summary>
    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(30, 0)]
    [InlineData(30, -1)]
    public void A_non_positive_bound_is_refused(int unarySeconds, int streamSeconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundDeadlines(
            TimeSpan.FromSeconds(unarySeconds),
            TimeSpan.FromSeconds(streamSeconds)));

    /// <summary>
    /// The fallback pair is a real pair, not the absence of one, and matches the shipped defaults.
    /// </summary>
    /// <remarks>
    /// THIS IS THE FINDING RESTATED AS AN ASSERTION. The defect was that no deadline was set at all, so
    /// the important property of the fallback is not which values it carries but that it carries
    /// values: a client constructed without a configured pair still bounds its calls.
    /// </remarks>
    [Fact]
    public void The_fallback_pair_carries_the_shipped_defaults()
    {
        GatewayOptions.OutboundCallOptions shipped = new();

        Assert.Equal(shipped.RequestTimeout, OutboundDeadlines.Default.Unary);
        Assert.Equal(shipped.StreamDeadline, OutboundDeadlines.Default.Stream);
        Assert.True(OutboundDeadlines.Default.Unary > TimeSpan.Zero);
        Assert.True(OutboundDeadlines.Default.Stream >= OutboundDeadlines.Default.Unary);
    }

    /// <summary>
    /// The two outbound bounds are validated as a pair, and each failure names its own key.
    /// </summary>
    /// <remarks>
    /// A total budget smaller than one attempt is refused because the resilience package's own
    /// validator refuses it too, and a configuration that faults pipeline construction is precisely the
    /// structural fault startup validation exists to catch. A stream bound below the unary bound is
    /// refused because the two settings exist separately only because a stream is legitimately
    /// longer-lived, so the inversion abandons retrievals sooner than the ordinary calls beside them.
    /// </remarks>
    [Theory]
    [InlineData(5, 300, "RequestTimeout")]
    [InlineData(30, 29, "StreamDeadline")]
    public void An_incoherent_outbound_bound_fails_validation(
        int requestSeconds,
        int streamSeconds,
        string expectedKey)
    {
        GatewayOptions options = new()
        {
            Upstreams = { DataServices = "https://dataservices.invalid", Security = "https://security.invalid" },
            HealthProbes =
            {
                Persistence = "https://persistence.invalid",
                DataServices = "https://dataservices.invalid",
                Security = "https://security.invalid",
            },
            Outbound =
            {
                RequestTimeout = TimeSpan.FromSeconds(requestSeconds),
                StreamDeadline = TimeSpan.FromSeconds(streamSeconds),
            },
        };

        List<System.ComponentModel.DataAnnotations.ValidationResult> results =
            [.. options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))];

        Assert.Contains(
            results,
            result => result.ErrorMessage is { } message
                && message.Contains($"{GatewayOptions.SectionName}:Outbound:{expectedKey}", StringComparison.Ordinal));
    }

    /// <summary>
    /// A coherent pair validates cleanly, so the rules above reject only what they name.
    /// </summary>
    [Fact]
    public void The_shipped_outbound_bounds_validate_cleanly()
    {
        GatewayOptions options = new()
        {
            Upstreams = { DataServices = "https://dataservices.invalid", Security = "https://security.invalid" },
            HealthProbes =
            {
                Persistence = "https://persistence.invalid",
                DataServices = "https://dataservices.invalid",
                Security = "https://security.invalid",
            },
        };

        Assert.DoesNotContain(
            options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)),
            result => result.ErrorMessage?.Contains(":Outbound:", StringComparison.Ordinal) ?? false);
    }

    /// <summary>
    /// A streaming member carries the stream bound and a unary member carries the unary bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CALL CLASS IS CHOSEN PER MEMBER, so it is a per-member assertion. Nothing else in this suite
    /// would notice a member handed the wrong class: the deadline would still be present, still be UTC,
    /// and still come from configuration - it would simply be the wrong one, and a retrieval bounded by
    /// the unary budget would be torn down mid-stream while looking entirely correct in every other
    /// respect.
    /// </para>
    /// <para>
    /// The two bounds are given far-apart values so the comparison is unambiguous, and the assertion is
    /// on the DIFFERENCE from the frozen clock rather than on an absolute instant, which is what the
    /// client actually computes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_streaming_member_carries_the_stream_bound_and_a_unary_member_the_unary_one()
    {
        DateTimeOffset now = new(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        StoppedClock clock = new(now);

        OutboundDeadlines deadlines = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(9), clock);

        CallClassRecordingClient dataWindow = new();

        DataServicesClient client = new(
            dataWindow,
            new FakeColumnExpressionServiceClient([]),
            new StubServiceTokenProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataServicesClient>.Instance,
            deadlines);

        _ = await client.GetEventGateAsync(
            new GetEventGateRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(now.UtcDateTime + TimeSpan.FromSeconds(30), dataWindow.LastUnaryOptions?.Deadline);

        await foreach (RetrieveChunk _ in client.RetrieveAsync(
            new RetrieveRequest(),
            TestContext.Current.CancellationToken))
        {
            // The stream is empty; enumerating it is what makes the call.
        }

        Assert.Equal(now.UtcDateTime + TimeSpan.FromMinutes(9), dataWindow.LastStreamOptions?.Deadline);
    }

    // ==============================================================================================
    //  3. THE WIRING
    // ==============================================================================================

    /// <summary>
    /// The deployed pipeline replays a safe operation and attempts an unsafe one exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY TEST HERE THAT WOULD HAVE CAUGHT THE ORIGINAL DEFECT. Everything above asserts that the
    /// predicates decide correctly; this asserts that they are INSTALLED - on the real gRPC
    /// registration, beneath the real resilience pipeline, in a real host. A predicate written and never
    /// wired is exactly the shape of the fault being fixed, and it passes every other test in this file.
    /// </para>
    /// <para>
    /// THE UPSTREAM IS A COUNTING HANDLER RATHER THAN A SERVER, and it answers the way a real gRPC
    /// server answers when it cannot serve: HTTP 200 with the refusal in a `grpc-status` response
    /// header. That is the exact shape the stock HTTP predicate reads as a success, so a wrong
    /// substitution shows one attempt on the safe operation and the test fails rather than passing
    /// vacuously.
    /// </para>
    /// <para>
    /// THE EXPECTED COUNT IS READ FROM THE PACKAGE'S OWN DEFAULT rather than written as a literal,
    /// because Gateway deliberately does not configure the attempt count - choosing one would assert an
    /// availability posture the repository publishes nothing to derive from. The retry DELAY is
    /// shortened here only so the assertion does not spend the package's exponential backoff proving a
    /// decision that takes no time to make.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_deployed_pipeline_replays_a_safe_operation_and_not_an_unsafe_one()
    {
        CountingUpstream upstream = new(StatusCode.Unavailable);

        using GatewayTestHostFixture fixture = new();

        fixture.AdditionalServiceConfiguration.Add(services =>
        {
            _ = services.ConfigureAll<HttpClientFactoryOptions>(options => options
                .HttpMessageHandlerBuilderActions
                .Add(builder => builder.PrimaryHandler = upstream));

            _ = services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                options.Retry.Delay = TimeSpan.FromMilliseconds(1));
        });

        // Starting the host is what materialises the registrations; the client itself is unused.
        using HttpClient started = fixture.CreateAnonymousClient();

        int expectedAttempts = new HttpStandardResilienceOptions().Retry.MaxRetryAttempts + 1;

        DataWindowService.DataWindowServiceClient dataWindow =
            fixture.Services.GetRequiredService<DataWindowService.DataWindowServiceClient>();

        upstream.Reset();

        _ = await Assert.ThrowsAsync<RpcException>(async () => await dataWindow
            .GetEventGateAsync(
                new GetEventGateRequest(),
                cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(expectedAttempts, upstream.Attempts);

        upstream.Reset();

        _ = await Assert.ThrowsAsync<RpcException>(async () => await dataWindow
            .UpdateAsync(new UpdateRequest(), cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(1, upstream.Attempts);
    }

    /// <summary>
    /// The deployed pipeline never replays a concurrency conflict, even on a safe operation.
    /// </summary>
    /// <remarks>
    /// Aborted is the answer contract C-06 is built on, and the caller's obligation is to re-read and
    /// rebase rather than to try again. Driven through the real pipeline for the same reason as above.
    /// </remarks>
    [Fact]
    public async Task The_deployed_pipeline_never_replays_a_conflict()
    {
        CountingUpstream upstream = new(StatusCode.Aborted);

        using GatewayTestHostFixture fixture = new();

        fixture.AdditionalServiceConfiguration.Add(services =>
        {
            _ = services.ConfigureAll<HttpClientFactoryOptions>(options => options
                .HttpMessageHandlerBuilderActions
                .Add(builder => builder.PrimaryHandler = upstream));

            _ = services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                options.Retry.Delay = TimeSpan.FromMilliseconds(1));
        });

        using HttpClient started = fixture.CreateAnonymousClient();

        DataWindowService.DataWindowServiceClient dataWindow =
            fixture.Services.GetRequiredService<DataWindowService.DataWindowServiceClient>();

        upstream.Reset();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () => await dataWindow
            .GetEventGateAsync(
                new GetEventGateRequest(),
                cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(StatusCode.Aborted, failure.StatusCode);
        Assert.Equal(1, upstream.Attempts);
    }

    /// <summary>
    /// Every outbound pipeline carries both policy predicates, the stated backoff shape, and a total
    /// budget taken from the same setting as the gRPC deadline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CIRCUIT-BREAKER HALF HAS NO OTHER TEST, AND THAT IS WHY THIS EXISTS. Opening a breaker takes a
    /// hundred observations by the package's own threshold, so driving it end to end would assert a
    /// threshold rather than a policy; reading the options the composition root actually produced pins
    /// the wiring directly. It also covers the two channels the end-to-end test does not touch - the
    /// column-expression client and the token channel - so a later edit cannot configure one and leave
    /// the others on the stock predicate, which is precisely how all three came to be unconfigured.
    /// </para>
    /// <para>
    /// THE NAME IS THE PACKAGE'S OWN CONVENTION for a standard handler - the client's name with a
    /// <c>-standard</c> suffix - and depending on it is safe in the only way that matters: if the
    /// convention changed, the monitor would hand back a default instance and every assertion below
    /// would fail loudly rather than pass vacuously.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("DataWindowServiceClient")]
    [InlineData("ColumnExpressionServiceClient")]
    [InlineData(SecurityClient.HttpClientName)]
    public void Every_outbound_pipeline_carries_both_predicates(string clientName)
    {
        using GatewayTestHostFixture fixture = new();

        // A VALUE THE PACKAGE WOULD NOT HAVE CHOSEN, deliberately. The shipped bound and the package's
        // own default total request timeout are the same thirty seconds, so an assertion made against
        // the shipped value passes whether or not the composition root assigns anything at all - which a
        // mutation of that assignment proved. Configuring a distinctive value is what makes the identity
        // observable rather than coincidental.
        fixture.AdditionalSettings[$"{GatewayOptions.SectionName}:Outbound:RequestTimeout"] = "00:00:25";

        using HttpClient started = fixture.CreateAnonymousClient();

        HttpStandardResilienceOptions installed = fixture.Services
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get(clientName + "-standard");

        Assert.Equal(
            (Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>>)
                OutboundCallPolicy.ShouldRetryAsync,
            installed.Retry.ShouldHandle);
        Assert.Equal(
            (Func<CircuitBreakerPredicateArguments<HttpResponseMessage>, ValueTask<bool>>)
                OutboundCallPolicy.ShouldBreakAsync,
            installed.CircuitBreaker.ShouldHandle);

        // Both happen to match the package defaults, which is exactly why they are asserted: "bounded
        // backoff with jitter" is a requirement of this policy, and a requirement that holds only
        // because a dependency's default satisfies it is not being enforced by anything.
        Assert.Equal(DelayBackoffType.Exponential, installed.Retry.BackoffType);
        Assert.True(installed.Retry.UseJitter);

        // ONE SETTING, TWO USES. A gRPC deadline is enforced client-side as a total bound across the
        // retries the pipeline performs beneath it, so a deadline shorter than the pipeline's budget
        // would cancel a call the pipeline was still retrying.
        GatewayOptions.OutboundCallOptions configured = fixture.Services
            .GetRequiredService<IOptions<GatewayOptions>>()
            .Value
            .Outbound;

        Assert.Equal(TimeSpan.FromSeconds(25), configured.RequestTimeout);
        Assert.Equal(configured.RequestTimeout, installed.TotalRequestTimeout.Timeout);
        Assert.Equal(
            installed.TotalRequestTimeout.Timeout,
            fixture.Services.GetRequiredService<OutboundDeadlines>().Unary);
    }

    /// <summary>
    /// The composition root registers a deadline pair derived from the bound outbound options.
    /// </summary>
    /// <remarks>
    /// The unary bound must be the SAME value the pipeline's total request timeout is configured from,
    /// because a gRPC deadline is enforced client-side as a total bound across the pipeline's retries: a
    /// shorter deadline would cancel a call the pipeline was still retrying. Asserting the identity
    /// rather than a literal is what keeps the two from drifting apart later.
    /// </remarks>
    [Fact]
    public void The_composition_root_derives_the_deadlines_from_the_bound_options()
    {
        using GatewayTestHostFixture fixture = new();
        using HttpClient started = fixture.CreateAnonymousClient();

        GatewayOptions.OutboundCallOptions configured = fixture.Services
            .GetRequiredService<IOptions<GatewayOptions>>()
            .Value
            .Outbound;

        OutboundDeadlines deadlines = fixture.Services.GetRequiredService<OutboundDeadlines>();

        Assert.Equal(configured.RequestTimeout, deadlines.Unary);
        Assert.Equal(configured.StreamDeadline, deadlines.Stream);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Asks the retry predicate about one path and one server-declared status.
    /// </summary>
    /// <param name="path">The absolute path of the operation.</param>
    /// <param name="status">The status the upstream declared.</param>
    /// <returns>Whether the attempt may be replayed.</returns>
    private static Task<bool> ShouldRetryAsync(string path, StatusCode status) =>
        ShouldRetryForUriAsync(Absolute(path), status);

    /// <summary>
    /// Asks the retry predicate about one request URI and one server-declared status.
    /// </summary>
    /// <param name="requestUri">The request URI, in either form.</param>
    /// <param name="status">The status the upstream declared.</param>
    /// <returns>Whether the attempt may be replayed.</returns>
    private static async Task<bool> ShouldRetryForUriAsync(Uri requestUri, StatusCode status)
    {
        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, requestUri));

        return await OutboundCallPolicy.ShouldRetryAsync(
            new RetryPredicateArguments<HttpResponseMessage>(
                context,
                Outcome.FromResult(TrailersOnly(status)),
                0));
    }

    /// <summary>
    /// Asks the circuit-breaker predicate about one path and one server-declared status.
    /// </summary>
    /// <param name="path">The absolute path of the operation.</param>
    /// <param name="status">The status the upstream declared.</param>
    /// <returns>Whether the outcome counts as a failure.</returns>
    private static async Task<bool> ShouldBreakAsync(string path, StatusCode status) =>
        await OutboundCallPolicy.ShouldBreakAsync(
            new CircuitBreakerPredicateArguments<HttpResponseMessage>(
                Context(path),
                Outcome.FromResult(TrailersOnly(status))));

    /// <summary>
    /// Builds a resilience context carrying the request a handler would have observed.
    /// </summary>
    /// <param name="path">The absolute path of the operation.</param>
    /// <returns>The context.</returns>
    private static ResilienceContext Context(string path)
    {
        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(path)));

        return context;
    }

    /// <summary>
    /// Composes the absolute request URI the client factory produces for one operation path.
    /// </summary>
    /// <param name="path">The absolute path of the operation.</param>
    /// <returns>The absolute URI a handler actually observes.</returns>
    /// <remarks>
    /// ABSOLUTE, BECAUSE THAT IS WHAT THE DEPLOYED PIPELINE SEES. The client factory resolves the
    /// relative path against the configured address before the handler pipeline runs, so a suite driving
    /// relative URIs would be asserting against a shape production never produces. The authority is an
    /// unroutable name: nothing here connects, and a real address would invite one.
    /// </remarks>
    private static Uri Absolute(string path) => new("https://dataservices.invalid" + path);

    /// <summary>Renders a gRPC status as the wire value its metadata carries.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The decimal wire value.</returns>
    private static string StatusValue(StatusCode status) =>
        ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the response a gRPC server sends when it refuses a call before writing any message.
    /// </summary>
    /// <param name="status">The status to declare.</param>
    /// <returns>An HTTP 200 response carrying the status in its headers.</returns>
    /// <remarks>
    /// HTTP 200 IS THE POINT. This is what a trailers-only gRPC refusal looks like on the wire, and it
    /// is why an HTTP-level predicate reads a refusal as a success.
    /// </remarks>
    private static HttpResponseMessage TrailersOnly(StatusCode status)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent([])
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") },
            },
        };

        response.Headers.TryAddWithoutValidation("grpc-status", StatusValue(status));

        return response;
    }

    /// <summary>
    /// A DataWindow client that records the call options of one unary and one streaming member.
    /// </summary>
    /// <remarks>
    /// Only the two members the call-class assertion needs are overridden. A double that implemented the
    /// whole contract would be longer than the property it exists to pin.
    /// </remarks>
    private sealed class CallClassRecordingClient : DataWindowService.DataWindowServiceClient
    {
        /// <summary>The options of the last unary call.</summary>
        internal CallOptions? LastUnaryOptions { get; private set; }

        /// <summary>The options of the last streaming call.</summary>
        internal CallOptions? LastStreamOptions { get; private set; }

        /// <inheritdoc />
        public override AsyncUnaryCall<GetEventGateResponse> GetEventGateAsync(
            GetEventGateRequest request,
            CallOptions options)
        {
            LastUnaryOptions = options;

            return new AsyncUnaryCall<GetEventGateResponse>(
                Task.FromResult(new GetEventGateResponse()),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => [],
                static () => { });
        }

        /// <inheritdoc />
        public override AsyncServerStreamingCall<RetrieveChunk> Retrieve(
            RetrieveRequest request,
            CallOptions options)
        {
            LastStreamOptions = options;

            return new AsyncServerStreamingCall<RetrieveChunk>(
                new ListAsyncStreamReader<RetrieveChunk>([]),
                Task.FromResult(new Metadata()),
                static () => Status.DefaultSuccess,
                static () => [],
                static () => { });
        }
    }

    /// <summary>A clock frozen at one instant, so a computed deadline is exact.</summary>
    /// <param name="instant">The instant to report.</param>
    private sealed class StoppedClock(DateTimeOffset instant) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => instant;
    }

    /// <summary>
    /// An upstream that counts attempts and answers every one with the same gRPC refusal.
    /// </summary>
    /// <remarks>
    /// A PRIMARY HANDLER RATHER THAN A DELEGATING ONE, so it sits beneath the whole resilience pipeline
    /// and therefore sees one call per ATTEMPT rather than one per request. That is the measurement the
    /// classification has to be judged by.
    /// </remarks>
    private sealed class CountingUpstream(StatusCode status) : HttpMessageHandler
    {
        private int _attempts;

        /// <summary>The number of attempts observed since the last reset.</summary>
        internal int Attempts => Volatile.Read(ref _attempts);

        /// <summary>Clears the counter between two measurements.</summary>
        internal void Reset() => Volatile.Write(ref _attempts, 0);

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _attempts);

            return Task.FromResult(TrailersOnly(status));
        }
    }
}
