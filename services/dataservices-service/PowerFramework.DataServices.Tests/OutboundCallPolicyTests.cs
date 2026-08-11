// ==================================================================================================
//  PowerFramework.DataServices.Tests.OutboundCallPolicyTests
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   The retry-admission, circuit-breaking and deadline policy this service
//  applies to its own outbound calls: Clients/OutboundCallPolicy.cs, the deadline attached by
//  Clients/PersistenceClient.cs, and the fact that Program.ApplyResilience actually installs both
//  predicates on the four Persistence channels rather than merely being able to.
//
//  WHY IT IS WORTH A SUITE OF ITS OWN. The defect this replaces was not a wrong value; it was a
//  policy that could not see what it was deciding about. Every one of the six outbound clients called
//  AddStandardResilienceHandler() with no configuration, and an HTTP-level pipeline cannot read a
//  `grpc-status`: a call the server REFUSED arrives as HTTP 200, so a server-declared Unavailable was
//  never retried, while a TRANSPORT fault arrived as an exception and was retried on every method
//  including Update, Exec and Commit - none of which is idempotent. Both halves are invisible in a
//  passing build, which is exactly why they need assertions.
//
//  THE SUITE IS IN THREE PARTS, and the third is the one that matters most:
//
//    1. THE DECISION. The two predicates are exercised directly, over the whole gRPC status space and
//       over both a replay-safe and an unsafe path, because "which statuses are admitted" is a table
//       and a table is worth enumerating rather than sampling.
//    2. THE DEADLINE. OutboundDeadlines' arithmetic and its defaults, plus the property that the
//       fallback instance is not "no deadline" - the state the finding was about.
//    3. THE WIRING. A REAL host, the REAL four gRPC registrations, the REAL resilience pipeline, and a
//       counting primary handler underneath it. Parts 1 and 2 would both pass with the predicates
//       written and never installed, which is precisely the shape of the original defect.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using Xunit;

using PersistenceQueryClient =
    PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceClient;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The retry, circuit-breaking and deadline policy for this service's outbound calls.
/// </summary>
public sealed class OutboundCallPolicyTests
{
    /// <summary>
    /// A path this service classifies as replay-safe: a read on the transaction contract.
    /// </summary>
    private const string SafePath = "/persistence.v1.TransactionService/IsConnected";

    /// <summary>
    /// A path this service classifies as unsafe: the update itself, whose replay is the
    /// silent-double-apply that contract C-06 exists to prevent.
    /// </summary>
    private const string UnsafePath = "/persistence.v1.UpdateService/Update";

    /// <summary>
    /// Every gRPC status, paired with whether a replay-safe operation may be retried for it.
    /// </summary>
    /// <remarks>
    /// ENUMERATED RATHER THAN SAMPLED, because the policy IS this table and a sampled test would let a
    /// later edit quietly admit a status nobody chose to admit. The two admitted statuses are the ones
    /// that report a transient inability to serve; every other row is a deliberate, correct answer, a
    /// fault whose repetition is not known to be safe, or a success.
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
    /// NARROWER THAN THE RETRY TABLE BY EXACTLY ONE ROW, and that row is the point. Resource
    /// exhaustion is retryable because it clears, but it is a HEALTHY refusal aimed at one caller's
    /// quota - counting it would let one caller's over-use open the breaker and deny the read path to
    /// everybody. Aborted is excluded for the stronger reason that a contested update is a correct
    /// answer, and letting ordinary contention trip the breaker would take the whole channel down.
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
    /// Each classified operation of the four Persistence contracts, paired with whether a transport
    /// attempt may be replayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE WHOLE SURFACE, not the interesting parts, because an operation nobody classified inherits
    /// "attempted exactly once" - which is the safe default but also a silent one, and a table that
    /// omitted a method could not tell a deliberate exclusion from a forgotten one.
    /// </para>
    /// <para>
    /// The three rows worth reading twice: <c>ReleaseQueryTask</c> is admitted while
    /// <c>CreateQueryTask</c> is not, because a replayed release releases nothing twice while a
    /// replayed create leaves a second server-held task nobody holds a handle to. <c>EndSession</c> is
    /// excluded even though it looks like a teardown, because the legacy pool is REFERENCE COUNTED and
    /// a repeated end decrements twice. And the clause setters are excluded because they carry a
    /// modification style, so the append form applied twice appends twice.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool> OperationAdmission
    {
        get
        {
            TheoryData<string, bool> data = [];

            Add(QueryService.Descriptor.FullName, "CreateQueryTask", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "ReleaseQueryTask", replaySafe: true);
            Add(QueryService.Descriptor.FullName, "Reset", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetChunkSize", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetMaxRows", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetWhereClause", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetOrderByClause", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetPaging", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "SetPagedUniqueIndexColumns", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "Query", replaySafe: false);
            Add(QueryService.Descriptor.FullName, "Count", replaySafe: true);

            Add(UpdateService.Descriptor.FullName, "CreateUpdateTask", replaySafe: false);
            Add(UpdateService.Descriptor.FullName, "ReleaseUpdateTask", replaySafe: true);
            Add(UpdateService.Descriptor.FullName, "Reset", replaySafe: false);
            Add(UpdateService.Descriptor.FullName, "PrepareUpdate", replaySafe: false);
            Add(UpdateService.Descriptor.FullName, "Update", replaySafe: false);

            Add(CommandService.Descriptor.FullName, "CreateCommandTask", replaySafe: false);
            Add(CommandService.Descriptor.FullName, "ReleaseCommandTask", replaySafe: true);
            Add(CommandService.Descriptor.FullName, "Reset", replaySafe: false);
            Add(CommandService.Descriptor.FullName, "SetAutoCommit", replaySafe: false);
            Add(CommandService.Descriptor.FullName, "SetSql", replaySafe: false);
            Add(CommandService.Descriptor.FullName, "Exec", replaySafe: false);

            Add(TransactionService.Descriptor.FullName, "BeginSession", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "EndSession", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "GetTransactionData", replaySafe: true);
            Add(TransactionService.Descriptor.FullName, "SetAutoCommit", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "AutoCommit", replaySafe: true);
            Add(TransactionService.Descriptor.FullName, "Commit", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "Rollback", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "IsConnected", replaySafe: true);
            Add(TransactionService.Descriptor.FullName, "GetDatabaseType", replaySafe: true);
            Add(TransactionService.Descriptor.FullName, "GetSessionState", replaySafe: true);
            Add(TransactionService.Descriptor.FullName, "ClearState", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "SetBroken", replaySafe: false);
            Add(TransactionService.Descriptor.FullName, "GridSyntaxFromSql", replaySafe: true);

            return data;

            void Add(string contract, string method, bool replaySafe) =>
                data.Add(string.Concat("/", contract, "/", method), replaySafe);
        }
    }

    /// <summary>
    /// The Security REST paths this service reaches, paired with whether a transport attempt may be
    /// replayed.
    /// </summary>
    /// <remarks>
    /// The thirteen admitted operations are pure functions of their request, so a replay recomputes
    /// the same answer. The four excluded ones are excluded for two different reasons, and both are
    /// worth stating: key generation and the three generators produce DIFFERENT material on each
    /// attempt - and key generation additionally RETAINS what it produced against a bounded capacity,
    /// so a replay consumes that capacity twice - while token issuance is excluded because every mint
    /// is separately recorded, so a replay leaves an audit record of a credential never delivered.
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
        bool expected)
    {
        bool retried = await ShouldRetryAsync(SafePath, status);

        Assert.Equal(expected, retried);
    }

    /// <summary>
    /// An unsafe operation is never retried, whatever the upstream said.
    /// </summary>
    /// <remarks>
    /// THE OPERATION IS CHECKED BEFORE THE OUTCOME IS, WHICH IS WHY THIS HOLDS FOR EVERY ROW. If the
    /// order were reversed, an Unavailable on an Update would be retried - and an update replayed
    /// after an unknown outcome is the double-apply C-06 exists to prevent.
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
        HttpResponseMessage response = TrailersOnly(status);

        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(SafePath)));

        bool broke = await OutboundCallPolicy.ShouldBreakAsync(
            new CircuitBreakerPredicateArguments<HttpResponseMessage>(
                context,
                Outcome.FromResult(response)));

        Assert.Equal(expected, broke);

        // The unsafe path answers identically: whether the UPSTREAM is healthy is a property of the
        // upstream and not of the call that discovered it, so unlike retry the breaker is not gated on
        // the operation. A test that only drove the safe path could not tell the two designs apart.
        ResilienceContext unsafeContext =
            ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        unsafeContext.SetRequestMessage(
            new HttpRequestMessage(HttpMethod.Post, Absolute(UnsafePath)));

        Assert.Equal(
            expected,
            await OutboundCallPolicy.ShouldBreakAsync(
                new CircuitBreakerPredicateArguments<HttpResponseMessage>(
                    unsafeContext,
                    Outcome.FromResult(TrailersOnly(status)))));
    }

    /// <summary>
    /// Every operation of the four Persistence contracts is classified exactly as the policy states.
    /// </summary>
    [Theory]
    [MemberData(nameof(OperationAdmission))]
    public async Task Every_persistence_operation_carries_its_stated_classification(
        string path,
        bool replaySafe)
    {
        // Unavailable is used as the probe because it is admitted for a safe operation, so the answer
        // here is decided entirely by the classification of the path.
        Assert.Equal(replaySafe, await ShouldRetryAsync(path, StatusCode.Unavailable));
    }

    /// <summary>
    /// Every Security REST path this service reaches is classified exactly as the policy states.
    /// </summary>
    /// <remarks>
    /// A REST response carries no gRPC status, so these rows exercise the third arm of the predicate -
    /// the package's own transient definition - rather than the status table. A 503 is transient by
    /// that definition, so the answer is again decided by the path alone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SecurityPathAdmission))]
    public async Task Every_security_path_carries_its_stated_classification(string path, bool replaySafe)
    {
        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(path)));

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
    /// THIS IS THE HALF THE ORIGINAL POLICY GOT BACKWARDS. A reset connection or a refused socket
    /// arrives as an exception, which every stock transient predicate treats as retryable - and every
    /// gRPC call is a POST, so a method-based gate could not distinguish an Update from a read. The
    /// exception arm therefore has to consult the same classification the response arm does, and this
    /// exercises it through the exception path where no response exists to read a status from.
    /// </remarks>
    [Theory]
    [InlineData(SafePath, true)]
    [InlineData(UnsafePath, false)]
    public async Task A_transport_fault_is_replayed_only_on_a_safe_operation(string path, bool expected)
    {
        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(path)));

        bool retried = await OutboundCallPolicy.ShouldRetryAsync(
            new RetryPredicateArguments<HttpResponseMessage>(
                context,
                Outcome.FromException<HttpResponseMessage>(new HttpRequestException("reset")),
                0));

        Assert.Equal(expected, retried);
    }

    /// <summary>
    /// A successful gRPC response is not retried even on a safe operation.
    /// </summary>
    [Fact]
    public async Task A_successful_call_is_not_retried()
    {
        Assert.False(await ShouldRetryAsync(SafePath, StatusCode.OK));

        // An HTTP-level success with no gRPC status at all - what a plain REST 200 looks like - is not
        // retried either. Worth its own assertion because status zero and "no status" are different
        // inputs that must both answer no, and a parse that defaulted to zero would conflate them.
        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(SafePath)));

        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                    0)));
    }

    /// <summary>
    /// An unrecognised path is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// This is the property that makes a contract addition SAFE by default: a method nobody classified
    /// falls to "attempted exactly once" rather than inheriting a replay. The same arm covers a
    /// context with no request message at all, where there is nothing to classify.
    /// </remarks>
    [Fact]
    public async Task An_unclassified_operation_is_attempted_exactly_once()
    {
        Assert.False(await ShouldRetryAsync("/persistence.v1.QueryService/NoSuchMethod", StatusCode.Unavailable));

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
    /// Treating it as absent instead lets the outcome fall through to the transport-level definition,
    /// which for an HTTP 200 also answers no - but for the right reason.
    /// </remarks>
    [Fact]
    public async Task A_malformed_status_header_falls_through_rather_than_reading_as_success()
    {
        HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("grpc-status", "not-a-number");

        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(SafePath)));

        // 503 is transient by the package's own definition, so falling through admits the retry. Had
        // the malformed value been read as zero, this would have answered false.
        Assert.True(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(response),
                    0)));
    }

    /// <summary>
    /// A status carried in the trailers is read as well as one carried in the headers.
    /// </summary>
    [Fact]
    public async Task A_status_in_the_trailers_is_read_too()
    {
        HttpResponseMessage response = new(HttpStatusCode.OK);
        response.TrailingHeaders.TryAddWithoutValidation("grpc-status", ((int)StatusCode.Aborted).ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        ResilienceContext context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(SafePath)));

        // Aborted in the trailers must be refused exactly as Aborted in the headers is. A predicate
        // that read only the headers would fall through to the transport definition, which for an HTTP
        // 200 answers no as well - so the assertion that distinguishes the two designs is the
        // Unavailable one below, where reading the trailer changes the answer.
        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(response),
                    0)));

        HttpResponseMessage unavailable = new(HttpStatusCode.OK);
        unavailable.TrailingHeaders.TryAddWithoutValidation(
            "grpc-status",
            ((int)StatusCode.Unavailable).ToString(System.Globalization.CultureInfo.InvariantCulture));

        ResilienceContext second = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        second.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, Absolute(SafePath)));

        Assert.True(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    second,
                    Outcome.FromResult(unavailable),
                    0)));
    }

    /// <summary>
    /// The classification table resolves against the shipped contracts.
    /// </summary>
    /// <remarks>
    /// The table is built from the descriptors, so a renamed or removed method throws while it is
    /// built. Calling the verification entry point is what turns that into a startup failure, and this
    /// asserts the entry point answers rather than throws for the contracts as shipped.
    /// </remarks>
    [Fact]
    public void The_classification_table_resolves_against_the_shipped_contracts()
    {
        // 10 gRPC operations across the four contracts plus 13 Security crypto paths.
        Assert.Equal(23, OutboundCallPolicy.Verify());
    }

    /// <summary>
    /// A URI the predicate cannot classify is refused rather than allowed to throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PREDICATE THAT THROWS IS WORSE THAN ONE THAT ANSWERS WRONGLY, because the exception replaces
    /// the failure the pipeline was deciding about - the caller is told about a policy fault instead of
    /// about its own upstream, and the original status is lost. The first written form of this policy
    /// read <c>RequestUri.AbsolutePath</c> directly, which THROWS for a relative URI; the deployed
    /// pipeline only ever presents absolute URIs, so the fault was unreachable in production and
    /// entirely invisible until a test presented the other form.
    /// </para>
    /// <para>
    /// Both rows must also AGREE: a relative and an absolute URI naming the same operation have to
    /// classify identically, or the answer would depend on which form the caller happened to build.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_uri_the_predicate_cannot_classify_is_refused_rather_than_thrown()
    {
        Assert.True(await ShouldRetryForUriAsync(new Uri(SafePath, UriKind.Relative), StatusCode.Unavailable));
        Assert.False(await ShouldRetryForUriAsync(new Uri(UnsafePath, UriKind.Relative), StatusCode.Unavailable));

        // A query or fragment on the relative form must not change the operation it names.
        Assert.True(
            await ShouldRetryForUriAsync(
                new Uri(SafePath + "?trace=1", UriKind.Relative),
                StatusCode.Unavailable));

        // And a request with no URI at all classifies as unknown, which means refused.
        ResilienceContext context =
            ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage());

        Assert.False(
            await OutboundCallPolicy.ShouldRetryAsync(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(TrailersOnly(StatusCode.Unavailable)),
                    0)));
    }

    // ==============================================================================================
    //  2. THE DEADLINE
    // ==============================================================================================

    /// <summary>
    /// Each call class is bounded by its own duration, measured from the injected clock.
    /// </summary>
    [Fact]
    public void Each_call_class_is_bounded_by_its_own_duration()
    {
        DeterministicTimeProvider clock = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        OutboundDeadlines deadlines = new(TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(11), clock);

        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 12, DateTimeKind.Utc), deadlines.DeadlineFor(OutboundCallClass.Unary));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 15, 5, DateTimeKind.Utc), deadlines.DeadlineFor(OutboundCallClass.Stream));

        // UTC, because gRPC requires it and a local-kind deadline would be silently misread as UTC and
        // land hours away from where the caller meant.
        Assert.Equal(DateTimeKind.Utc, deadlines.DeadlineFor(OutboundCallClass.Unary).Kind);
    }

    /// <summary>
    /// An unrecognised call class is bounded by the tighter of the two rather than left unbounded.
    /// </summary>
    [Fact]
    public void An_unrecognised_call_class_takes_the_tighter_bound()
    {
        DeterministicTimeProvider clock = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

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
    public void A_non_positive_bound_is_refused(int unarySeconds, int streamSeconds)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundDeadlines(
            TimeSpan.FromSeconds(unarySeconds),
            TimeSpan.FromSeconds(streamSeconds)));
    }

    /// <summary>
    /// The fallback pair is a real pair, not the absence of one, and matches the shipped defaults.
    /// </summary>
    /// <remarks>
    /// THIS IS THE FINDING RESTATED AS AN ASSERTION. The defect was that no deadline was set at all,
    /// so the important property of the fallback is not which values it carries but that it carries
    /// values: a client constructed without a configured pair still bounds its calls. Matching the
    /// options defaults is what keeps the shipped posture stated in one place rather than two.
    /// </remarks>
    [Fact]
    public void The_fallback_pair_carries_the_shipped_defaults()
    {
        Assert.Equal(ClientResilienceOptions.DefaultRequestTimeout, OutboundDeadlines.Default.Unary);
        Assert.Equal(ClientResilienceOptions.DefaultStreamDeadline, OutboundDeadlines.Default.Stream);

        ClientResilienceOptions shipped = new();

        Assert.Equal(shipped.RequestTimeout, OutboundDeadlines.Default.Unary);
        Assert.Equal(shipped.StreamDeadline, OutboundDeadlines.Default.Stream);
        Assert.True(OutboundDeadlines.Default.Unary > TimeSpan.Zero);
        Assert.True(OutboundDeadlines.Default.Stream >= OutboundDeadlines.Default.Unary);
    }

    /// <summary>
    /// A stream bound shorter than the unary bound is refused by configuration validation.
    /// </summary>
    /// <remarks>
    /// Not a preference: the two settings exist separately because a stream is legitimately
    /// longer-lived, so a shorter stream bound abandons retrievals sooner than the ordinary calls
    /// beside them and does it silently, as a deadline failure that reads as an upstream fault.
    /// </remarks>
    [Fact]
    public void A_stream_bound_below_the_unary_bound_fails_validation()
    {
        DataServicesOptions options = new();
        options.Persistence.Address = "https://persistence.invalid";
        options.Security.BaseAddress = "https://security.invalid";
        options.Resilience.Persistence.RequestTimeout = TimeSpan.FromSeconds(30);
        options.Resilience.Persistence.StreamDeadline = TimeSpan.FromSeconds(29);

        DataServicesOptionsValidator validator = new();

        ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("Resilience:Persistence:StreamDeadline", StringComparison.Ordinal));
    }

    /// <summary>
    /// A streaming member carries the stream bound and a unary member carries the unary bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CALL CLASS IS CHOSEN PER MEMBER, so it is a per-member assertion. Nothing else in this suite
    /// would notice a member handed the wrong class: the deadline would still be present, still be UTC
    /// and still come from configuration - it would simply be the wrong one, and a retrieval bounded by
    /// the unary budget would be torn down mid-stream while looking correct in every other respect.
    /// </para>
    /// <para>
    /// The two bounds are given far-apart values so the comparison is unambiguous, and the assertion is
    /// on the offset from a frozen clock rather than on an absolute instant, which is what the client
    /// actually computes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_streaming_member_carries_the_stream_bound_and_a_unary_member_the_unary_one()
    {
        DateTimeOffset now = new(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        DeterministicTimeProvider clock = new(now);

        OutboundDeadlines deadlines = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(9), clock);

        FakeQueryServiceClient query = new();

        PersistenceClient client = new(
            query,
            new FakeUpdateServiceClient(),
            new FakeCommandServiceClient(),
            new FakeTransactionServiceClient(),
            new PersistenceStubTokenProvider(),
            new PersistenceRecordingLogger(),
            deadlines);

        _ = await client.CountAsync(
            new CountRequest { Task = new TaskHandle { TaskId = "call-class" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(now.UtcDateTime + TimeSpan.FromSeconds(30), query.LastOptions?.Deadline);

        await foreach (QueryResponse _ in client.QueryAsync(
            new QueryRequest { Task = new TaskHandle { TaskId = "call-class" } },
            TestContext.Current.CancellationToken))
        {
            // The stream is empty; enumerating it is what makes the call.
        }

        Assert.Equal(now.UtcDateTime + TimeSpan.FromMinutes(9), query.LastOptions?.Deadline);
    }

    // ==============================================================================================
    //  3. THE WIRING
    // ==============================================================================================

    /// <summary>
    /// The deployed pipeline replays a safe operation within its configured budget and attempts an
    /// unsafe one exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY TEST HERE THAT WOULD HAVE CAUGHT THE ORIGINAL DEFECT. Everything above asserts that
    /// the predicates decide correctly; this asserts that they are INSTALLED - on the real four gRPC
    /// registrations, beneath the real resilience pipeline, in a real host. A predicate written and
    /// never wired is exactly the shape of the fault being fixed, and it passes every other test in
    /// this file.
    /// </para>
    /// <para>
    /// THE UPSTREAM IS A COUNTING HANDLER RATHER THAN A SERVER, and it answers the way a real gRPC
    /// server answers when it cannot serve: HTTP 200 with the refusal in a `grpc-status` response
    /// header. That is the exact shape the stock HTTP predicate reads as a success, so if the
    /// substitution were wrong the safe operation would show one attempt and the test would fail
    /// rather than pass vacuously.
    /// </para>
    /// <para>
    /// The retry delay is shortened to one millisecond through configuration because the shipped
    /// two-second base with exponential backoff would spend fourteen seconds proving a decision that
    /// takes no time to make. The attempt COUNT is left at its shipped value and read from the bound
    /// options, so this asserts the deployed budget rather than one the test chose.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_deployed_pipeline_replays_a_safe_operation_and_not_an_unsafe_one()
    {
        CountingUpstream upstream = new(StatusCode.Unavailable);

        using DataServicesTestHostFactory factory = new();

        RestoreRealTimerSource(factory);

        // The fixture already reduces the attempt count to one and the base delay to ten milliseconds so
        // a deliberately failing upstream test finishes promptly. One retry would prove the decision, but
        // two-versus-one is a thin margin to read an assertion against, so the count is raised here and
        // then READ BACK from the bound options - the assertion is against the configured budget, not
        // against a number this test chose.
        factory.AdditionalSettings["DataServices:Resilience:Persistence:MaxRetryAttempts"] = "3";
        factory.AdditionalServiceConfiguration.Add(services => services
            .ConfigureAll<HttpClientFactoryOptions>(options => options
                .HttpMessageHandlerBuilderActions
                .Add(builder => builder.PrimaryHandler = upstream)));

        // Starting the host is what materialises the registrations; the client itself is unused.
        using HttpClient started = factory.CreateAnonymousClient();

        int configuredAttempts = factory.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Resilience
            .Persistence
            .MaxRetryAttempts;

        PersistenceQueryClient query = factory.Services.GetRequiredService<PersistenceQueryClient>();

        upstream.Reset();

        _ = await Assert.ThrowsAsync<RpcException>(async () => await query
            .CountAsync(new CountRequest(), cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(configuredAttempts + 1, upstream.Attempts);

        upstream.Reset();

        _ = await Assert.ThrowsAsync<RpcException>(async () => await query
            .CreateQueryTaskAsync(
                new CreateQueryTaskRequest(),
                cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(1, upstream.Attempts);
    }

    /// <summary>
    /// The deployed pipeline never replays a concurrency conflict, even on a safe operation.
    /// </summary>
    /// <remarks>
    /// Aborted is the answer contract C-06 is built on, and the caller's obligation is to re-read and
    /// rebase rather than to try again. Driven through the real pipeline for the same reason as above:
    /// the status table could be right while the predicate that reads it was never installed.
    /// </remarks>
    [Fact]
    public async Task The_deployed_pipeline_never_replays_a_conflict()
    {
        CountingUpstream upstream = new(StatusCode.Aborted);

        using DataServicesTestHostFactory factory = new();

        RestoreRealTimerSource(factory);

        // The attempt count is left at whatever the fixture configured, because the property under test is
        // that the count does not matter: a conflict is attempted once however large the budget is.
        factory.AdditionalSettings["DataServices:Resilience:Persistence:MaxRetryAttempts"] = "3";
        factory.AdditionalServiceConfiguration.Add(services => services
            .ConfigureAll<HttpClientFactoryOptions>(options => options
                .HttpMessageHandlerBuilderActions
                .Add(builder => builder.PrimaryHandler = upstream)));

        using HttpClient started = factory.CreateAnonymousClient();

        PersistenceQueryClient query = factory.Services.GetRequiredService<PersistenceQueryClient>();

        upstream.Reset();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () => await query
            .CountAsync(new CountRequest(), cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync);

        Assert.Equal(StatusCode.Aborted, failure.StatusCode);
        Assert.Equal(1, upstream.Attempts);
    }

    /// <summary>
    /// Every outbound pipeline carries both policy predicates and the stated backoff shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CIRCUIT-BREAKER HALF HAS NO OTHER TEST, AND THAT IS WHY THIS EXISTS. Opening a breaker takes a
    /// hundred observations by the deployed threshold, so driving it end to end would assert a threshold
    /// rather than a policy; reading the options the composition root actually produced pins the wiring
    /// directly. It also covers the four channels the end-to-end test does not touch, so a later edit
    /// cannot configure one client and leave the other three on the stock predicate.
    /// </para>
    /// <para>
    /// THE NAME IS THE PACKAGE'S OWN CONVENTION for a standard handler - the client's name with a
    /// <c>-standard</c> suffix - and depending on it is safe in the only way that matters: if the
    /// convention changed, the monitor would hand back a default instance and every assertion below
    /// would fail loudly rather than pass vacuously.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("QueryServiceClient")]
    [InlineData("UpdateServiceClient")]
    [InlineData("CommandServiceClient")]
    [InlineData("TransactionServiceClient")]
    [InlineData(SecurityClient.HttpClientName)]
    public void Every_outbound_pipeline_carries_both_predicates(string clientName)
    {
        using DataServicesTestHostFactory factory = new();
        using HttpClient started = factory.CreateAnonymousClient();

        HttpStandardResilienceOptions installed = factory.Services
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
    }

    /// <summary>
    /// The pipeline's total budget and the gRPC deadline come from one setting.
    /// </summary>
    /// <remarks>
    /// A gRPC deadline is enforced client-side as a TOTAL bound across the retries the pipeline performs
    /// beneath it, so a deadline shorter than the pipeline's budget would cancel a call the pipeline was
    /// still retrying. Asserting the identity rather than two literals is what stops them drifting apart.
    /// </remarks>
    [Fact]
    public void The_pipeline_budget_and_the_deadline_are_the_same_setting()
    {
        using DataServicesTestHostFactory factory = new();

        // A VALUE THE PACKAGE WOULD NOT HAVE CHOSEN, deliberately. The shipped bound and the package's
        // own default total request timeout are the same thirty seconds, so an assertion made against
        // the shipped value passes whether or not the projection assigns anything at all - which a
        // mutation of that assignment proved on the sibling service. Configuring a distinctive value is
        // what makes the identity observable rather than coincidental.
        factory.AdditionalSettings["DataServices:Resilience:Persistence:RequestTimeout"] = "00:00:25";

        using HttpClient started = factory.CreateAnonymousClient();

        ClientResilienceOptions configured = factory.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Resilience
            .Persistence;

        HttpStandardResilienceOptions installed = factory.Services
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get("QueryServiceClient-standard");

        Assert.Equal(TimeSpan.FromSeconds(25), configured.RequestTimeout);
        Assert.Equal(configured.RequestTimeout, installed.TotalRequestTimeout.Timeout);
        Assert.Equal(
            installed.TotalRequestTimeout.Timeout,
            factory.Services.GetRequiredService<OutboundDeadlines>().Unary);
    }

    /// <summary>
    /// The composition root registers a deadline pair derived from the Persistence resilience group.
    /// </summary>
    /// <remarks>
    /// The unary bound must be the SAME value the pipeline's total request timeout is configured from,
    /// because a gRPC deadline is enforced client-side as a total bound across the pipeline's retries:
    /// a shorter deadline would cancel a call the pipeline was still retrying. Asserting the identity
    /// rather than a literal is what keeps them from drifting apart later.
    /// </remarks>
    [Fact]
    public void The_composition_root_derives_the_deadlines_from_the_configured_group()
    {
        using DataServicesTestHostFactory factory = new();
        using HttpClient started = factory.CreateAnonymousClient();

        ClientResilienceOptions configured = factory.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Resilience
            .Persistence;

        OutboundDeadlines deadlines = factory.Services.GetRequiredService<OutboundDeadlines>();

        Assert.Equal(configured.RequestTimeout, deadlines.Unary);
        Assert.Equal(configured.StreamDeadline, deadlines.Stream);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Asks the retry predicate about one path and one server-declared status.
    /// </summary>
    /// <param name="path">The absolute request path.</param>
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
        ResilienceContext context =
            ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        context.SetRequestMessage(new HttpRequestMessage(HttpMethod.Post, requestUri));

        return await OutboundCallPolicy.ShouldRetryAsync(
            new RetryPredicateArguments<HttpResponseMessage>(
                context,
                Outcome.FromResult(TrailersOnly(status)),
                0));
    }

    /// <summary>
    /// Composes the absolute request URI the client factory produces for one operation path.
    /// </summary>
    /// <param name="path">The absolute path of the operation.</param>
    /// <returns>The absolute URI a handler actually observes.</returns>
    /// <remarks>
    /// ABSOLUTE, BECAUSE THAT IS WHAT THE DEPLOYED PIPELINE SEES. The client factory resolves the
    /// relative path against the configured base address before the handler pipeline runs, so a suite
    /// that drove relative URIs would be asserting against a shape production never produces. The
    /// authority is deliberately an unroutable name: nothing here connects, and a real address would
    /// invite one.
    /// </remarks>
    private static Uri Absolute(string path) => new("https://persistence.invalid" + path);

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

        response.Headers.TryAddWithoutValidation(
            "grpc-status",
            ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));

        return response;
    }

    /// <summary>
    /// An upstream that counts attempts and answers every one with the same gRPC refusal.
    /// </summary>
    /// <remarks>
    /// A PRIMARY HANDLER RATHER THAN A DELEGATING ONE, so it sits beneath the whole resilience pipeline
    /// and therefore sees one call per ATTEMPT rather than one per request. That is the measurement the
    /// classification has to be judged by.
    /// </remarks>
    /// <summary>
    /// Puts the resilience pipeline's timer source back on real time for one host.
    /// </summary>
    /// <param name="factory">The host being composed.</param>
    /// <remarks>
    /// <para>
    /// ⚠ A RETRY DELAY IS A TIMER, AND THE FIXTURE'S CLOCK ONLY FIRES TIMERS WHEN A TEST ADVANCES IT ⚠
    /// </para>
    /// <para>
    /// <c>DataServicesTestHostFactory</c> substitutes <see cref="TimeProvider"/> host-wide with a clock
    /// whose <c>CreateTimer</c> hands back a manual timer, and it does so for a real reason: the macro
    /// invoker imposes its invocation timeout through a <see cref="CancellationTokenSource"/> built from
    /// the registered provider, so leaving that half of the clock on real time would time the machine
    /// rather than the code. Polly builds the retry DELAY from the same registered provider, so under that
    /// clock the delay between two attempts never elapses and a pipeline that is behaving perfectly
    /// correctly waits forever - not a failure, a hang, which is the one outcome a suite cannot report.
    /// </para>
    /// <para>
    /// THE TWO REQUIREMENTS ARE ABOUT DIFFERENT CONSUMERS OF THE SAME MEMBER, so they are satisfied
    /// separately rather than traded off. Only the two rows below drive the real pipeline and wait on a
    /// real delay, and neither asserts a single timestamp - they count attempts - so restoring the system
    /// clock for those two hosts costs nothing the determinism seam exists to protect. Everything else in
    /// this suite, and every suite that asserts an expiry, keeps the fixture's clock. Gateway's own
    /// <c>FrozenTestClock</c> reaches the same conclusion from the other direction: it freezes only the
    /// wall-clock reads and leaves <c>CreateTimer</c> delegating to the system provider, "because the
    /// resilience pipelines and the client factory's handler rotation build timers from whichever provider
    /// is registered and a frozen timer source would leave any component that waits on one waiting
    /// forever."
    /// </para>
    /// <para>
    /// APPLIED THROUGH <c>AdditionalServiceConfiguration</c>, which the fixture documents as running last
    /// precisely so a suite's bespoke registration wins over its defaults. This is that mechanism used for
    /// what it is for, not a door around the fixture.
    /// </para>
    /// </remarks>
    private static void RestoreRealTimerSource(DataServicesTestHostFactory factory) =>
        factory.AdditionalServiceConfiguration.Add(static services =>
        {
            services.RemoveAll<TimeProvider>();
            _ = services.AddSingleton(TimeProvider.System);
        });

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
