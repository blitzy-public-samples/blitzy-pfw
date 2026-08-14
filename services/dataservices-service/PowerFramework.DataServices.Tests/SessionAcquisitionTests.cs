// ==================================================================================================
//  SessionAcquisitionTests - taking a server-held session into use is ONE step, and losing a trace
//  record is REPORTED
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//    1. ATOMIC ACQUISITION, on both server-held session types. Validating that a session may be used
//       and recording the activity happen under ONE acquisition of the session's gate. Asking "is it
//       open", then "has it expired", then "record activity" as three separate calls takes the gate
//       three times, and a close landing in either interval produces an outcome no single state of the
//       session justifies: both checks pass, the touch silently does nothing because the session is
//       already closed, and the caller receives a CLOSED session together with a success code.
//
//       On the expression session the consequence is sharper than a stale read. Closing there clears
//       every host registration and every calculation stack, so a caller handed a closed session
//       resolves handles against emptied state instead of being told the session is gone.
//
//    2. EXPIRY IS TERMINAL UNDER THE SAME LOCK. A session found stale is closed by the attempt that
//       found it, so no second observer can look at it and conclude anything different. That is what
//       removes the window rather than narrowing it, and it is what the second-acquisition tests below
//       assert: the second attempt reports CLOSED, not EXPIRED.
//
//    3. A LOST TRACE RECORD IS COUNTED. The expression trace is fire-and-forget and a failing
//       diagnostic sink must not break the calculation it observes - so the exception is still
//       absorbed. What must not happen is DISCARDING it: a sink that throws on every record loses
//       every trace, and nothing anywhere would report that the diagnostic channel had stopped working. An
//       invisibly failing diagnostic is worse than a disabled one, because it looks enabled.
//
//  WHY THERE IS NO WALL-CLOCK RACE TEST
//  ------------------------------------------------------------------------------------------------
//  A test that starts a close and an acquisition on two threads and hopes to hit the window would be
//  flaky in both directions - it can pass against broken code and fail against correct code, because
//  the interleaving is not under its control. What IS deterministic is the property that removes the
//  window: expiry is decided and acted on together, and a second attempt on the same instance therefore
//  reports a closed session. That is asserted directly, and the concurrent test below asserts the
//  invariant that no interleaving can produce a self-contradictory answer - a success carrying no
//  session, or a refusal carrying one.
// ==================================================================================================

using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A minimal expression-service host. Every member answers the empty string, because no test here
/// calculates anything - a host is needed only so a DataWindow can be REGISTERED, which is what gives
/// the session a calculation stack for a trace record to be built from.
/// </summary>
internal sealed class StubExpressionServiceHost : IExpressionServiceHost
{
    /// <inheritdoc/>
    public int FindVarIndex(string? name) => 0;

    /// <inheritdoc/>
    public string CalcVarExpValue(
        long row,
        IDataWindowObject? dwo,
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc) => string.Empty;

    /// <inheritdoc/>
    public string CalcVarExpValue(
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc) => string.Empty;

    /// <inheritdoc/>
    public string GetItemExpValue(long row, string? columnName) => string.Empty;
}

/// <summary>
/// A trace sink that either records or throws, so both delivery outcomes are reachable.
/// </summary>
internal sealed class FaultingTraceSink : IExpressionTraceSink
{
    private readonly Exception? _failure;

    /// <summary>Creates the sink.</summary>
    /// <param name="failure">The exception to throw on every record, or null to accept them.</param>
    internal FaultingTraceSink(Exception? failure = null) => _failure = failure;

    /// <summary>Every record the sink accepted.</summary>
    internal List<ExpressionTraceRecord> Accepted { get; } = [];

    /// <inheritdoc/>
    public void Emit(ExpressionTraceRecord record)
    {
        if (_failure is not null)
        {
            throw _failure;
        }

        Accepted.Add(record);
    }
}

/// <summary>
/// The acquisition and trace-delivery suite.
/// </summary>
public sealed class SessionAcquisitionTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A hostile sink failure message. If any of it ever reaches the delivery report, a sink's arbitrary
    /// content has become readable through the session.
    /// </summary>
    private const string SinkFailureMessage =
        "Host=trace.internal;Password=hunter2;Token=eyJhbGciOiJSUzI1NiJ9.payload.signature";

    // ----------------------------------------------------------------------------------------------
    //  F11 - THE VALIDATION SESSION.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public void ValidationAcquireRefusesAnAlreadyClosedSession()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock _) = NewValidationRegistry();
        ValidationSession session = registry.OpenWithId("id").Session!;

        Assert.True(session.Close());

        Assert.Equal(ValidationSessionAcquisition.Closed, session.Acquire());
    }

    [Fact]
    public void ValidationAcquireOnAnExpiredSessionClosesItUnderTheSameLock()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock clock) = NewValidationRegistry();
        ValidationSession session = registry.OpenWithId("id").Session!;

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(ValidationSessionAcquisition.Expired, session.Acquire());

        // DECIDED AND ACTED ON TOGETHER. This is the property that removes the window rather than
        // narrowing it: no other observer can now look at this session and reach a different conclusion.
        Assert.False(session.IsOpen);

        // And a closed session is not "expired": it has already gone, and reporting it as expired would
        // invite a second release.
        Assert.False(session.HasExpired());
    }

    [Fact]
    public void ASecondValidationAcquireOnAnExpiredSessionReportsClosedRatherThanExpired()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock clock) = NewValidationRegistry();
        ValidationSession session = registry.OpenWithId("id").Session!;

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(ValidationSessionAcquisition.Expired, session.Acquire());
        Assert.Equal(ValidationSessionAcquisition.Closed, session.Acquire());
    }

    [Fact]
    public void ValidationAcquireRecordsActivitySoASessionInUseNeverExpires()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock clock) = NewValidationRegistry();
        ValidationSession session = registry.OpenWithId("id").Session!;

        // Two hops, each just short of the timeout. Without the activity stamp being advanced by the
        // first acquisition, the second would be past it.
        clock.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(ValidationSessionAcquisition.Acquired, session.Acquire());

        clock.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(ValidationSessionAcquisition.Acquired, session.Acquire());
    }

    [Fact]
    public void ValidationResolveRefusesAndReleasesTheSlotWhenTheSessionHasExpired()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock clock) = NewValidationRegistry();
        Assert.True(registry.OpenWithId("id").IsOpened);

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        ValidationSessionResolution refused = registry.Resolve("id");

        Assert.Null(refused.Session);
        Assert.Equal(RetCode.E_NOT_EXISTS, refused.ReturnCode);

        // The entry is gone and its slot is back, so the same identifier can be opened again rather than
        // being permanently poisoned by a session nobody closed.
        Assert.Equal(0, registry.Count);
        Assert.True(registry.OpenWithId("id").IsOpened);
    }

    [Fact]
    public void ValidationResolveRefusesASessionClosedDirectlyOnTheSession()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock _) = NewValidationRegistry();
        ValidationSession session = registry.OpenWithId("id").Session!;

        // Closed on the session rather than through the registry, so the registry still holds the entry.
        Assert.True(session.Close());

        ValidationSessionResolution refused = registry.Resolve("id");

        Assert.Null(refused.Session);
        Assert.Equal(RetCode.E_NOT_EXISTS, refused.ReturnCode);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task ConcurrentValidationResolveAndCloseNeverProduceAContradictoryOutcome()
    {
        (ValidationSessionRegistry registry, ValidationSessionTestClock _) = NewValidationRegistry();

        for (int attempt = 0; attempt < 200; attempt++)
        {
            string id = $"id-{attempt}";
            Assert.True(registry.OpenWithId(id).IsOpened);

            Task<ValidationSessionResolution> resolving = Task.Run(
                () => registry.Resolve(id),
                TestContext.Current.CancellationToken);

            Task closing = Task.Run(() => registry.Close(id), TestContext.Current.CancellationToken);

            ValidationSessionResolution resolution = await resolving;
            await closing;

            // The two halves of a resolution always agree, whichever order the two operations landed in:
            // a success carries a session and a refusal carries none. A resolution that returned a
            // session alongside a failure code, or a success alongside no session, would mean the outcome
            // was assembled from state read at two different instants - which is precisely the defect
            // this acquisition is meant to remove.
            if (resolution.ReturnCode == RetCode.OK)
            {
                Assert.NotNull(resolution.Session);
            }
            else
            {
                Assert.Null(resolution.Session);

                // BOTH refusal codes are correct here, and which one appears is decided by where the
                // close landed - not by any looseness in the acquisition. A registry close REMOVES the
                // entry, so a resolution arriving after it finds no entry at all and reports the
                // never-registered code; a resolution arriving before it finds the entry and is refused
                // by the acquisition itself, which reports the registered-but-gone code. The registry
                // documents the two as deliberately distinct because only the second is worth retrying
                // with a fresh open, so collapsing them here would assert away a real distinction. What
                // matters for the race is the property asserted above and below: the outcome is always
                // one DEFINED refusal, never OK paired with a session the close already released.
                Assert.Contains(
                    resolution.ReturnCode,
                    new[] { RetCode.E_INVALID_HANDLE, RetCode.E_NOT_EXISTS });
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    //  F11 - THE EXPRESSION SESSION.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public void ExpressionAcquireRefusesAnAlreadyClosedSession()
    {
        ExpressionSession session = NewExpressionSession(new ValidationSessionTestClock());

        Assert.True(session.Close());

        Assert.Equal(ExpressionSessionAcquisition.Closed, session.Acquire());
    }

    [Fact]
    public void ExpressionAcquireOnAnExpiredSessionClosesItAndReleasesEveryRegistration()
    {
        ValidationSessionTestClock clock = new();
        ExpressionSession session = NewExpressionSession(clock);

        DataWindowHandle handle = session.Register(new StubExpressionServiceHost());
        Assert.NotNull(session.CalcStackFor(handle));

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(ExpressionSessionAcquisition.Expired, session.Acquire());

        // Closed under the same lock that decided it, AND its registrations released - so a handle
        // cannot keep resolving against state the expiry was supposed to have freed.
        Assert.False(session.IsOpen);
        Assert.Null(session.CalcStackFor(handle));
    }

    [Fact]
    public void ASecondExpressionAcquireOnAnExpiredSessionReportsClosedRatherThanExpired()
    {
        ValidationSessionTestClock clock = new();
        ExpressionSession session = NewExpressionSession(clock);

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(ExpressionSessionAcquisition.Expired, session.Acquire());
        Assert.Equal(ExpressionSessionAcquisition.Closed, session.Acquire());
    }

    [Fact]
    public void ExpressionAcquireRecordsActivitySoASessionInUseNeverExpires()
    {
        ValidationSessionTestClock clock = new();
        ExpressionSession session = NewExpressionSession(clock);

        clock.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(ExpressionSessionAcquisition.Acquired, session.Acquire());

        clock.Advance(IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(ExpressionSessionAcquisition.Acquired, session.Acquire());
    }

    [Fact]
    public void ExpressionTryGetRefusesAndReleasesTheSlotWhenTheSessionHasExpired()
    {
        ValidationSessionTestClock clock = new();
        ExpressionSessionRegistry registry = NewExpressionRegistry(clock);

        Assert.True(registry.Open("id").IsOpened);

        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.False(registry.TryGet("id", out ExpressionSession? refused));
        Assert.Null(refused);
        Assert.Equal(0, registry.Count);

        // The slot is back, so the identifier is reusable.
        Assert.True(registry.Open("id").IsOpened);
    }

    // ----------------------------------------------------------------------------------------------
    //  F12 - TRACE DELIVERY IS REPORTED.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public void TraceDeliveryReportsNothingUntilASinkIsGivenARecord()
    {
        ExpressionSession session = NewExpressionSession(new ValidationSessionTestClock());

        ExpressionTraceDeliveryReport report = session.TraceDelivery;

        Assert.Equal(0, report.Attempted);
        Assert.Equal(0, report.Delivered);
        Assert.Equal(0, report.Failed);
        Assert.Null(report.LastFailureType);
    }

    [Fact]
    public void ASinkThatAcceptsARecordIsCountedAsDelivered()
    {
        FaultingTraceSink sink = new();
        ExpressionSession session = NewExpressionSession(new ValidationSessionTestClock(), sink);
        DataWindowHandle handle = session.Register(new StubExpressionServiceHost());
        session.TraceEnabled = true;

        Assert.NotNull(session.EmitTrace(handle, row: 1, dwo: null, "salary * 2", "2000"));

        ExpressionTraceDeliveryReport report = session.TraceDelivery;

        Assert.Equal(1, report.Attempted);
        Assert.Equal(1, report.Delivered);
        Assert.Equal(0, report.Failed);
        Assert.Null(report.LastFailureType);
        Assert.Single(sink.Accepted);
    }

    [Fact]
    public void ASinkThatThrowsIsCountedAndItsTypeRetainedWhileTheRecordIsStillReturned()
    {
        FaultingTraceSink sink = new(new InvalidOperationException(SinkFailureMessage));
        ExpressionSession session = NewExpressionSession(new ValidationSessionTestClock(), sink);
        DataWindowHandle handle = session.Register(new StubExpressionServiceHost());
        session.TraceEnabled = true;

        // THE CALCULATION IS UNAFFECTED. The record is still built and still returned, because a failing
        // diagnostic sink must not break the thing it is observing.
        ExpressionTraceRecord? first = session.EmitTrace(handle, row: 1, dwo: null, "salary", "1000");
        ExpressionTraceRecord? second = session.EmitTrace(handle, row: 2, dwo: null, "salary", "2000");

        Assert.NotNull(first);
        Assert.NotNull(second);

        ExpressionTraceDeliveryReport report = session.TraceDelivery;

        // AND THE LOSS IS VISIBLE. Two records were handed over, neither arrived, and the report says so
        // - which nothing else in the system would.
        Assert.Equal(2, report.Attempted);
        Assert.Equal(0, report.Delivered);
        Assert.Equal(2, report.Failed);
        Assert.Equal("System.InvalidOperationException", report.LastFailureType);
    }

    [Fact]
    public void ASinkFailureNeverPublishesItsOwnMessage()
    {
        FaultingTraceSink sink = new(new InvalidOperationException(SinkFailureMessage));
        ExpressionSession session = NewExpressionSession(new ValidationSessionTestClock(), sink);
        DataWindowHandle handle = session.Register(new StubExpressionServiceHost());
        session.TraceEnabled = true;

        session.EmitTrace(handle, row: 1, dwo: null, "salary", "1000");

        string? published = session.TraceDelivery.LastFailureType;

        Assert.NotNull(published);
        Assert.DoesNotContain("hunter2", published, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9", published, StringComparison.Ordinal);
        Assert.DoesNotContain("trace.internal", published, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDeliveryIsAttemptedWhenTracingIsOffOrNoSinkIsRegistered()
    {
        // Tracing off, sink present.
        FaultingTraceSink sink = new();
        ExpressionSession traceOff = NewExpressionSession(new ValidationSessionTestClock(), sink);
        DataWindowHandle offHandle = traceOff.Register(new StubExpressionServiceHost());

        Assert.Null(traceOff.EmitTrace(offHandle, row: 1, dwo: null, "salary", "1000"));
        Assert.Equal(0, traceOff.TraceDelivery.Attempted);

        // Tracing on, no sink.
        ExpressionSession noSink = NewExpressionSession(new ValidationSessionTestClock());
        DataWindowHandle noSinkHandle = noSink.Register(new StubExpressionServiceHost());
        noSink.TraceEnabled = true;

        Assert.NotNull(noSink.EmitTrace(noSinkHandle, row: 1, dwo: null, "salary", "1000"));
        Assert.Equal(0, noSink.TraceDelivery.Attempted);
    }

    // ----------------------------------------------------------------------------------------------
    //  FIXTURE CONSTRUCTION.
    // ----------------------------------------------------------------------------------------------

    private static (ValidationSessionRegistry Registry, ValidationSessionTestClock Clock)
        NewValidationRegistry()
    {
        ValidationSessionTestClock clock = new();

        DataServicesOptions options = new();
        options.Sessions.ValidationSession.IdleTimeout = IdleTimeout;
        options.Sessions.ValidationSession.MaxConcurrentSessions = 500;

        return (new ValidationSessionRegistry(options, i18n: null, timeProvider: clock), clock);
    }

    private static ExpressionSessionRegistry NewExpressionRegistry(ValidationSessionTestClock clock)
    {
        DataServicesOptions options = new();
        options.Sessions.ExpressionSession.IdleTimeout = IdleTimeout;
        options.Sessions.ExpressionSession.MaxConcurrentSessions = 500;

        return new ExpressionSessionRegistry(Options.Create(options), clock);
    }

    private static ExpressionSession NewExpressionSession(
        ValidationSessionTestClock clock,
        IExpressionTraceSink? sink = null)
    {
        SessionLifetimeOptions lifetime = new()
        {
            IdleTimeout = IdleTimeout,
            MaxConcurrentSessions = 500,
        };

        return new ExpressionSession(
            "session-under-test",
            lifetime,
            columnExpression: null,
            timeProvider: clock,
            traceSink: sink);
    }
}
