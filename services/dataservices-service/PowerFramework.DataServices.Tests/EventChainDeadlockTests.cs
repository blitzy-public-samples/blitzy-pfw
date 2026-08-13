// ==================================================================================================
//  EventChainDeadlockTests.cs - THE C-03 EVENT CHAIN ANSWERS ITS OWN QUESTIONS
//  ------------------------------------------------------------------------------------------------
//  ONE SUBJECT: what the `EventChain` stream does with a question the chain asks BACK - that it can be
//  answered on that same stream, that an unanswered one ends the call instead of holding it for ever,
//  and that the notifications a client sends WHILE one is outstanding are bounded rather than retained
//  without limit. Everything else about the chain - the 22 arms, the four alphabets, the ordering
//  disciplines, the sequencer's accept rule - belongs to DataWindowServiceContractTests.cs and
//  EventOrderingPatternTests.cs and is deliberately not re-asserted here.
//
//  WHY THE FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Nine of the 22 events are questions rather than notifications [se_cst_dw.sru:L11-L14, :L24-L26,
//  :L28, :L32]: the legacy expects the APPLICATION to implement them, so across this boundary the
//  server asks the client and the dispatch that raised the question BLOCKS on the answer - which is
//  the oracle's own shape, because in process the handler simply returned a value [:L194].
//
//  The only thing that can produce that answer is a `Result` message on the request stream, and the
//  only thing that reads one is the request-stream loop. A loop that AWAITED the dispatch was therefore
//  waiting for a message it had made itself unable to read: permanently, with no deadline, holding the
//  validation session, the chain, its five attached services and the stream. Every one of the nine
//  question-shaped events was unreachable over the wire, which is the whole synchronous half of AAP
//  0.6.1.4's pattern (b).
//
//  Both rows below FAIL BY TIMING OUT rather than by asserting, if the defect returns - which is
//  exactly the shape a deadlock regression has to take. Each is bounded by its own budget so a
//  regression reports in seconds instead of hanging the suite.
//
//  THE COMPOSITION IS THE PRODUCTION ONE, DELIBERATELY
//  ------------------------------------------------------------------------------------------------
//  `HeadlessDataWindowEventChainFactory` over `HeadlessDataWindowModelSetProvider` is what Program.cs
//  registers, and its chain's semantic members really do call the responder
//  [Domain/DataWindowComposition.cs `Ask`, whose wait is a blocking `GetAwaiter().GetResult()` because
//  the member is synchronous by contract]. A chain double that answered locally would exercise none of
//  this: the deadlock lived in the interaction between the real blocking wait and the real read loop,
//  so only the real pair can witness it.
//
//  ================================================================================================
//  USER RULES: NONE
//  ================================================================================================
//  `review_rules` returns exactly "No user rules provided." No assertion below exists because a
//  coding guideline demanded it. The binding constraints of AAP 0.7.3 are honoured as follows:
//
//    C-B   Behaviour is preserved, not improved. The blocking wait is KEPT - it is the oracle's
//          synchronous shape - and only the thread that performs it moves. The answer is never
//          fabricated: an elapsed backstop is a defined status, not a guessed return value.
//    C-C   The legacy tree is read only and is the oracle. It is cited, never written.
//    C-G   Nothing here weakens a boundary: no token, key or credential appears.
//    C-H   Both new production behaviours - the handover and the backstop - are covered.
//    C-K   Every expectation carries its locator or its reason.
// ==================================================================================================

using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Localization;
using Xunit;
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using Svc = PowerFramework.DataServices.Grpc.DataWindowService;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The event chain delivers an answer to its own outstanding question, and bounds one that never comes.
/// </summary>
public sealed class EventChainDeadlockTests
{
    /// <summary>
    /// How long a row waits for the whole call before declaring the deadlock has returned.
    /// </summary>
    /// <remarks>
    /// GENEROUS, BECAUSE IT IS A DEADLOCK DETECTOR RATHER THAN A LATENCY BUDGET. The exchange it bounds
    /// is two in-memory messages, so any value above a few milliseconds passes on a working build; a
    /// large one only affects how long a BROKEN build takes to report. There is no performance objective
    /// in this system to assert against (AAP 0.8.5) and this is not one.
    /// </remarks>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A semantic question is answered on the same stream, the blocked dispatch resumes, and the event's
    /// result is written - which is what the deadlocking read loop could never reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ondoitemchange</c> is the question chosen because it is the one whose answer is LOAD BEARING
    /// beyond itself: <c>:L195</c> stashes the raw code for <c>ondwnitemvalidationerror</c> to read and
    /// clear [<c>:L331-L332</c>], so a client that cannot answer it cannot drive the validation chain at
    /// all.
    /// </para>
    /// <para>
    /// THE ORDER OF THE TWO OUTBOUND MESSAGES IS ASSERTED, not just their presence. The question must go
    /// out BEFORE the result, because the result is a function of the answer; a build that wrote them the
    /// other way round would be answering from something other than the client.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASemanticQuestionAnsweredOnTheSameStreamCompletesTheDispatch()
    {
        Harness harness = new();

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(opened.SessionId));

        AnsweringStreams streams = new(
            opened.SessionId,
            [Notify(opened.SessionId, EventId.Ondoitemchange, 1L)]);

        await harness.Service
            .EventChain(streams.Reader, streams.Writer, harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        // Two messages, in this order: the question, then the event's own result.
        Assert.Equal(2, streams.Writer.Written.Count);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, streams.Writer.Written[0].PayloadCase);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Result, streams.Writer.Written[1].PayloadCase);

        // The question was the semantic half of the event that was notified.
        Assert.Equal(EventId.Ondoitemchange, streams.Writer.Written[0].Invoke.EventId);

        // The result is the notified event's, and it carries the value the CLIENT answered - which is
        // the whole point: nothing here is invented on the client's behalf.
        Assert.Equal(EventId.Ondoitemchange, streams.Writer.Written[1].Result.EventId);
        Assert.Equal(AnsweringStreams.AnsweredReturnValue, streams.Writer.Written[1].Result.ReturnValue);

        // Exactly one answer was needed, so the client sent exactly one.
        Assert.Equal(1, streams.Reader.AnswersSent);
    }

    /// <summary>
    /// A notification that follows an answered question still dispatches, in arrival order, so the
    /// handover did not strand the queue behind the question it was waiting on.
    /// </summary>
    /// <remarks>
    /// THE REGRESSION THIS GUARDS IS THE OPPOSITE ONE. A fix that dispatched notifications concurrently
    /// would also have unblocked the question, and would have violated AAP 0.6.1.4's prohibition on
    /// reordering while appearing to work. One consumer taking one message at a time is what makes the
    /// second event's result arrive AFTER the first's, and this row reads the outbound sequence to say so.
    /// </remarks>
    [Fact]
    public async Task ANotificationFollowingAnAnsweredQuestionStillDispatchesInOrder()
    {
        Harness harness = new();

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        // The second notification is a RAW event, which the chain answers itself without asking - so the
        // stream carries the question, the first result, then the second result and nothing else.
        //
        // The tokens are what a client computes: one past the highest it has seen in either direction.
        // The first exchange consumes three - the question, the outcome report and the result write - so
        // the second notification's token is 4. This row is not about the sequencer's rule, but it must
        // satisfy it, and stating the arithmetic here is cheaper than a reader rediscovering it.
        AnsweringStreams streams = new(
            opened.SessionId,
            [
                Notify(opened.SessionId, EventId.Ondoitemchange, 1L),
                Notify(opened.SessionId, EventId.Ondwnsetfocus, 4L),
            ]);

        await harness.Service
            .EventChain(streams.Reader, streams.Writer, harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        Assert.Equal(3, streams.Writer.Written.Count);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, streams.Writer.Written[0].PayloadCase);
        Assert.Equal(EventId.Ondoitemchange, streams.Writer.Written[1].Result.EventId);
        Assert.Equal(EventId.Ondwnsetfocus, streams.Writer.Written[2].Result.EventId);
    }

    /// <summary>
    /// A question the client never answers ends the call with <c>DeadlineExceeded</c> on the configured
    /// backstop, rather than holding the stream open indefinitely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BACKSTOP IS THE SECOND HALF OF THE CORRECTION AND IT IS INDEPENDENT OF THE FIRST. Handing the
    /// dispatch to its own consumer stops a well-behaved client from deadlocking; it does nothing about a
    /// client that reads the question and stays silent, which after the handover would leave the consumer
    /// waiting for ever while the read loop sat idle. Only a deadline ends that, and only a DEFINED one:
    /// the alternative is to invent an answer, and a guessed <c>ondoitemchange</c> code selects an arm of
    /// the <c>{0,1,2,3}</c> alphabet the client never chose.
    /// </para>
    /// <para>
    /// A SHORT REAL BACKSTOP RATHER THAN A SUBSTITUTED CLOCK, for one reason: the wait being bounded is a
    /// BLOCKING wait on another thread, so a fake clock would have to be advanced from this thread at a
    /// moment this thread cannot observe. A small real duration keeps the row deterministic in the
    /// direction that matters - it can only elapse, never fail to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnansweredQuestionEndsTheCallOnTheBackstop()
    {
        DataServicesOptions configured = new();
        configured.EventChain.AnswerTimeout = TimeSpan.FromMilliseconds(250);

        Harness harness = new(configured);

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        SilentStreams streams = new(Notify(opened.SessionId, EventId.Ondoitemchange, 1L));

        RpcException failure = await Assert.ThrowsAsync<RpcException>(
            () => harness.Service
                .EventChain(streams.Reader, streams.Writer, harness.Context)
                .WaitAsync(CallBudget, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.DeadlineExceeded, failure.StatusCode);

        // The question WAS asked, so the client had every opportunity to answer it.
        EventChainResponse asked = Assert.Single(streams.Writer.Written);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, asked.PayloadCase);

        // The detail names the setting an operator would change and the identifier a client must echo,
        // rather than saying only that something timed out.
        Assert.Contains("AnswerTimeout", failure.Status.Detail, StringComparison.Ordinal);
        Assert.Contains(asked.Invoke.CorrelationId, failure.Status.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A client that half-closes its request stream with a question outstanding is told so IMMEDIATELY,
    /// rather than waiting out the backstop for an answer that can no longer travel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE THIRD HANG, AND THE ONE THE BACKSTOP ALONE DOES NOT COVER.</b> An answer travels on the
    /// request stream, so once that stream has ended the outcome is already decided - but a dispatch still
    /// blocked on the question would sit out the whole <c>AnswerTimeout</c>, and the CALL CANNOT FINISH
    /// UNTIL IT DOES. With the shipped five-minute default that is five minutes of a stream held open, a
    /// session reporting itself mid-handler, and the item-change re-entrancy flag still set
    /// [<c>se_cst_dw.sru:L92</c>]. Reproducible against the live service without the seal below.
    /// </para>
    /// <para>
    /// THE BUDGET IS DELIBERATELY FAR BELOW THE BACKSTOP HERE. The configured backstop is five minutes and
    /// this row allows thirty seconds, so passing it is only possible if the seal fired: a build that
    /// waited for the deadline instead would fail this row rather than pass it slowly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task HalfClosingWithAQuestionOutstandingEndsTheCallAtOnce()
    {
        DataServicesOptions configured = new();

        // THE SHIPPED DEFAULT, stated rather than shortened, because the whole point is that this row does
        // NOT wait for it.
        Assert.Equal(TimeSpan.FromMinutes(5), configured.EventChain.AnswerTimeout);

        Harness harness = new(configured);

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        AbandoningStreams streams = new(Notify(opened.SessionId, EventId.Ondoitemchange, 1L));

        RpcException failure = await Assert.ThrowsAsync<RpcException>(
            () => harness.Service
                .EventChain(streams.Reader, streams.Writer, harness.Context)
                .WaitAsync(CallBudget, TestContext.Current.CancellationToken));

        // CANCELLED, not DeadlineExceeded: the client's own half-close ended the exchange, which is
        // precisely what that status means - and it is a different remedy from a slow handler.
        Assert.Equal(StatusCode.Cancelled, failure.StatusCode);

        // NO RESULT WAS WRITTEN, which is the substantive assertion: the event's result is a function of
        // the answer, so a build that produced one here would have fabricated the answer.
        Assert.DoesNotContain(
            streams.Writer.Written,
            written => written.PayloadCase == EventChainResponse.PayloadOneofCase.Result);

        // WHETHER THE QUESTION WENT OUT AT ALL IS DELIBERATELY NOT ASSERTED, and the reason is worth
        // recording: the half-close can be read before the consumer has taken the notification off its
        // queue, in which case the seal is already in force and the question is refused BEFORE it is
        // written. That is strictly better than writing a question already known to be unanswerable, so
        // pinning either outcome here would freeze an implementation detail rather than a behaviour.
        Assert.All(
            streams.Writer.Written,
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, written.PayloadCase));

        // The detail names what the client must do, not merely that something was cancelled.
        Assert.Contains(
            "before completing the request stream",
            failure.Status.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A client that pipelines notifications instead of conversing is refused with
    /// <c>ResourceExhausted</c> once the pending ceiling is reached, and the refusal arrives WHILE a
    /// dispatch is blocked - which is only possible if the read loop was never stalled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>WHAT THE HANDOVER LEFT OPEN.</b> Accepting a notification is constant-time work on the read
    /// loop while the consumer may be blocked on one answer for as long as
    /// <c>DataServices:EventChain:AnswerTimeout</c> permits - five minutes by default. The only thing
    /// bounding the queue was therefore the synchronous discipline's promise that a client cannot
    /// pipeline, and that promise is the CLIENT'S. An authenticated client that simply declined it grew
    /// the queue with one protobuf message per write, unbounded (CWE-400).
    /// </para>
    /// <para>
    /// THE FIXTURE MAKES THE FLOOD DETERMINISTIC RATHER THAN RACING THE CONSUMER. The first notification
    /// is <c>ondoitemchange</c>, a question the chain asks BACK, and this client answers nothing - so the
    /// consumer is provably still holding that dispatch when the rest arrive. No sleep, no retry loop and
    /// no dependence on scheduling: the count can only be 1, then 2, then over.
    /// </para>
    /// <para>
    /// AND THE REFUSAL ITSELF IS THE PROOF THE READ LOOP NEVER BLOCKED. Reaching the third message means
    /// <c>MoveNext</c> was called three times while the consumer sat blocked on the first - which is
    /// exactly the property a bounded CHANNEL would have destroyed, because a full channel stalls the
    /// writer, and the writer here is the only thing that can deliver the answer the consumer is waiting
    /// for. That is the original deadlock, and this row would time out rather than fail if a future
    /// change reintroduced it.
    /// </para>
    /// <para>
    /// NO RESULT IS WRITTEN, which is the substantive behavioural assertion: the refused notification was
    /// never dispatched, so nothing was half-applied and no event the client believes ran actually did.
    /// The alternative implementation - dropping the excess message and continuing - would have left the
    /// chain's four cross-event fields describing an event that never happened
    /// [<c>se_cst_dw.sru:L89-L96</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APipelinedFloodIsRefusedOnTheCeilingWhileTheReadLoopStaysFree()
    {
        DataServicesOptions configured = new();

        // THE SMALLEST LEGAL CEILING, stated rather than defaulted, so the row needs three messages
        // instead of sixty-five and the arithmetic is readable. The shipped default is asserted
        // separately in DataServicesOptionsTests.
        configured.EventChain.MaxPendingNotifications = 2;

        Harness harness = new(configured);

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        // Three notifications, sent back to back with no response read in between. The first occupies the
        // consumer for the whole row; the second fills the ceiling; the third is the one refused.
        FloodingStreams streams = new(
            Notify(opened.SessionId, EventId.Ondoitemchange, 1L),
            Notify(opened.SessionId, EventId.Ondwnsetfocus, 2L),
            Notify(opened.SessionId, EventId.Ondwnsetfocus, 3L));

        RpcException failure = await Assert.ThrowsAsync<RpcException>(
            () => harness.Service
                .EventChain(streams.Reader, streams.Writer, harness.Context)
                .WaitAsync(CallBudget, TestContext.Current.CancellationToken));

        // RESOURCE EXHAUSTED, not FailedPrecondition: the client's state is coherent and its next attempt
        // succeeds unchanged if it converses, which is a quota's remedy rather than a precondition's.
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);

        // The detail names the ceiling that was applied and the setting an operator would change, so the
        // remedy is readable from the status alone.
        Assert.Contains("MaxPendingNotifications", failure.Status.Detail, StringComparison.Ordinal);
        Assert.Contains(
            configured.EventChain.MaxPendingNotifications.ToString(CultureInfo.InvariantCulture),
            failure.Status.Detail,
            StringComparison.Ordinal);

        // All three messages were read - the client was never blocked writing them, and neither was the
        // server reading them.
        Assert.Equal(3, streams.Reader.Delivered);

        // NOTHING WAS DISPATCHED TO A RESULT. The one dispatch that started is still waiting on an answer
        // that never came, and the two queued behind it were never reached.
        Assert.DoesNotContain(
            streams.Writer.Written,
            written => written.PayloadCase == EventChainResponse.PayloadOneofCase.Result);
    }

    /// <summary>
    /// A conforming conversation is unaffected by the ceiling even at its smallest legal value, because a
    /// slot is released when its dispatch completes rather than accumulating for the life of the stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OPPOSITE REGRESSION TO THE ROW ABOVE, AND THE ONE A CEILING INVITES. A ceiling counted
    /// CUMULATIVELY rather than concurrently would pass the flood row and then refuse the third
    /// notification of every long-lived conversation - a limit on how many events a chain may carry in
    /// total, which is not a limit anything asked for. Two notifications over a ceiling of two would still
    /// pass such a build; the THIRD is what distinguishes them, so this row sends three.
    /// </para>
    /// <para>
    /// EVERY NOTIFICATION HERE IS A QUESTION, deliberately. A question is the case where the slot is held
    /// longest - across a whole client round trip - so if the release is ever moved to the wrong place, it
    /// is this shape that catches it rather than a raw event the chain answers itself in microseconds.
    /// </para>
    /// <para>
    /// THE TOKENS ARE WHAT A CONFORMING CLIENT COMPUTES: one past the highest seen in either direction.
    /// Each exchange consumes three ordinals - the question, the outcome report and the result write - so
    /// the three notifications carry 1, 4 and 7. Stating the arithmetic here is cheaper than a reader
    /// rediscovering it; the sequencer's own rule is asserted in EventOrderingPatternTests.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AConformingConversationIsNotRefusedAtTheSmallestLegalCeiling()
    {
        DataServicesOptions configured = new();
        configured.EventChain.MaxPendingNotifications = 2;

        Harness harness = new(configured);

        OpenValidationSessionResponse opened = await harness.Service
            .OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                },
                harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        AnsweringStreams streams = new(
            opened.SessionId,
            [
                Notify(opened.SessionId, EventId.Ondoitemchange, 1L),
                Notify(opened.SessionId, EventId.Ondoitemchange, 4L),
                Notify(opened.SessionId, EventId.Ondoitemchange, 7L),
            ]);

        await harness.Service
            .EventChain(streams.Reader, streams.Writer, harness.Context)
            .WaitAsync(CallBudget, TestContext.Current.CancellationToken);

        // Three questions and three results, alternating - so no notification was refused and none was
        // dispatched out of turn.
        Assert.Equal(6, streams.Writer.Written.Count);
        Assert.Equal(3, streams.Reader.AnswersSent);

        Assert.Collection(
            streams.Writer.Written,
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, written.PayloadCase),
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Result, written.PayloadCase),
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, written.PayloadCase),
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Result, written.PayloadCase),
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, written.PayloadCase),
            written => Assert.Equal(EventChainResponse.PayloadOneofCase.Result, written.PayloadCase));
    }

    /// <summary>Builds one notification message.</summary>
    /// <param name="sessionId">The session every message on the stream names.</param>
    /// <param name="eventId">Which event is notified.</param>
    /// <param name="sequence">The ordering token the client computed.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// The body carries the fixture's own first column, because the chain resolves the DataWindow object
    /// from the HOST rather than from the wire - a reconstructed object would answer the buffer read from
    /// the request and make the item-change equality test compare the client's claim against itself.
    /// </remarks>
    private static EventChainRequest Notify(string sessionId, EventId eventId, long sequence)
    {
        EventNotification notification = new()
        {
            CorrelationId = string.Format(
                CultureInfo.InvariantCulture,
                "q-{0}-{1}",
                eventId,
                sequence),
            EventId = eventId,
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        if (eventId == EventId.Ondoitemchange)
        {
            notification.DoItemChange = new DoItemChangeEvent
            {
                Row = 1L,
                Dwo = new DwObjectRef { Name = "name", Id = 2L, ColType = "char(100)" },
                Data = "edited",
            };
        }
        else
        {
            // PARAMETERLESS AT SOURCE - `event ondwnsetfocus()` carries no arguments in the legacy
            // [se_cst_dw.sru:L31], so the wire message has no fields either.
            notification.DwnSetFocus = new DwnSetFocusEvent();
        }

        return new EventChainRequest
        {
            SessionId = sessionId,
            Token = new SequencingToken { Sequence = sequence },
            Notify = notification,
        };
    }

    /// <summary>
    /// The service under test composed over its PRODUCTION collaborators, so the chain's semantic members
    /// really ask the responder.
    /// </summary>
    private sealed class Harness
    {
        internal Harness(DataServicesOptions? configured = null)
        {
            Configured = configured ?? new DataServicesOptions();

            HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());

            HeadlessDataWindowModelSetProvider models = new(
                hosts,
                Options.Create(Configured),
                new I18n(),
                // The shipped BLOCKED matcher, which is what the composition root registers: the pinyin
                // lookup table lives only inside the closed pfw.dll, so AAP 0.6.5 records it as the
                // single genuine parity risk and requires it be reported blocked, never approximated.
                PinyinFirstLetterMatcher.Blocked,
                ExpressionPageResolverFactory.Create(
                    Configured.ColumnExpression.PageResolution,
                    Configured.ColumnExpression.PageRowsPerPage));

            // ONE ROW EXISTS, because the item-change protocol snapshots the cell before raising and
            // restores it afterwards on two of its four arms - a chain over an empty buffer would take
            // the degenerate path instead of the one this file is about.
            if (hosts.Create(DataWindowCatalogue.SqliteFixtureName) is HeadlessDataWindowHost host)
            {
                _ = host.AppendRow(
                    PowerFramework.Contracts.Common.V1.DwBuffer.Primary,
                    PowerFramework.Contracts.Common.V1.ItemStatus.NotModified,
                    1L,
                    "seed");
            }

            Service = new Svc(
                Options.Create(Configured),
                new ValidationSessionRegistry(Configured),
                new HeadlessDataWindowEventChainFactory(models),
                models,
                new C03PersistenceClient());
        }

        internal DataServicesOptions Configured { get; }

        internal Svc Service { get; }

        /// <summary>
        /// A CANCELLABLE token, because a production call context always carries one and a
        /// non-cancellable one hides the ASP.NET Core writer's refusal of the two-argument WriteAsync.
        /// </summary>
        /// <remarks>
        /// EXPOSED SO ONE ROW CAN ACTUALLY CANCEL THE CALL. A client disconnect and an expired deadline
        /// both reach a handler as this token firing, and there is no other way to reproduce either.
        /// </remarks>
        internal CancellationTokenSource Lifetime { get; } = new();

        internal ServerCallContext Context => _context ??= new C03CallContext(Lifetime.Token);

        private ServerCallContext? _context;
    }

    /// <summary>
    /// Records outbound messages and lets a reader wait for the next one.
    /// </summary>
    /// <remarks>
    /// IT IMPLEMENTS ONLY THE SINGLE-ARGUMENT <c>WriteAsync</c>, exactly as the production
    /// <c>HttpContextStreamWriter&lt;T&gt;</c> does, so a write that wrongly forwarded a cancellation
    /// token would fail here as it fails in production.
    /// </remarks>
    private sealed class SignallingWriter : IServerStreamWriter<EventChainResponse>
    {
        private readonly List<EventChainResponse> _written = [];
        private readonly SemaphoreSlim _arrived = new(0);

        public WriteOptions? WriteOptions { get; set; }

        internal IReadOnlyList<EventChainResponse> Written
        {
            get
            {
                lock (_written)
                {
                    return [.. _written];
                }
            }
        }

        public Task WriteAsync(EventChainResponse message)
        {
            lock (_written)
            {
                _written.Add(message);
            }

            _ = _arrived.Release();

            return Task.CompletedTask;
        }

        /// <summary>Waits for one more message than have already been consumed here.</summary>
        /// <param name="cancellationToken">Bounds the wait.</param>
        /// <returns>The message that arrived.</returns>
        internal async Task<EventChainResponse> NextAsync(CancellationToken cancellationToken)
        {
            await _arrived.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_written)
            {
                return _written[_consumed++];
            }
        }

        private int _consumed;
    }

    /// <summary>
    /// A client that replays notifications and ANSWERS every question the server asks in between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE WHOLE FIXTURE, AND ITS SHAPE IS THE POINT. Between two scripted notifications it waits
    /// on the response stream; if the message it finds is a question it answers it and keeps waiting, and
    /// when it finds a result it hands the next notification over. That is precisely what a real client
    /// does, and precisely what the deadlocking server could never be driven by: the server had to read
    /// the answer to unblock, and it could not read while blocked.
    /// </para>
    /// <para>
    /// The wait is bounded by the reader's own cancellation token, which is the call's - so a server that
    /// asks nothing and writes nothing fails the row by the outer budget rather than hanging inside it.
    /// </para>
    /// </remarks>
    private sealed class AnsweringStreams
    {
        /// <summary>The value the client answers every question with.</summary>
        /// <remarks>
        /// DELIBERATELY NOT 0, 1, 2 OR 3. The item-change alphabet's `case else` arm coerces by column
        /// type and then forcibly returns 2 [<c>se_cst_dw.sru:L226-L250</c>], and this file is about the
        /// answer TRAVELLING rather than about which arm it selects - so a value outside the alphabet
        /// keeps the assertion honest about where it came from. The raw code is what
        /// <c>ondoitemchange</c>'s own result carries [<c>:L292</c>].
        /// </remarks>
        internal const long AnsweredReturnValue = 4242L;

        internal AnsweringStreams(string sessionId, EventChainRequest[] notifications)
        {
            Writer = new SignallingWriter();
            Reader = new AnsweringReader(sessionId, notifications, Writer);
        }

        internal SignallingWriter Writer { get; }

        internal AnsweringReader Reader { get; }
    }

    /// <summary>The client half of <see cref="AnsweringStreams"/>.</summary>
    private sealed class AnsweringReader(
        string sessionId,
        EventChainRequest[] notifications,
        SignallingWriter writer)
        : IAsyncStreamReader<EventChainRequest>
    {
        private readonly Queue<EventChainRequest> _pendingNotifications = new(notifications);

        private EventChainRequest? _current;
        private bool _awaitingResult;

        public EventChainRequest Current => _current
            ?? throw new InvalidOperationException("MoveNext has not produced a message.");

        /// <summary>How many answers the client sent.</summary>
        internal int AnswersSent { get; private set; }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            // BETWEEN NOTIFICATIONS, DRAIN THE RESPONSE STREAM. A question is answered; a result means the
            // previous notification is finished and the next may go.
            while (_awaitingResult)
            {
                EventChainResponse response = await writer
                    .NextAsync(cancellationToken)
                    .ConfigureAwait(false);

                switch (response.PayloadCase)
                {
                    case EventChainResponse.PayloadOneofCase.Invoke:
                        AnswersSent++;

                        _current = new EventChainRequest
                        {
                            SessionId = sessionId,
                            Result = new EventResult
                            {
                                CorrelationId = response.Invoke.CorrelationId,
                                EventId = response.Invoke.EventId,
                                ReturnValue = AnsweringStreams.AnsweredReturnValue,
                            },
                        };

                        return true;

                    case EventChainResponse.PayloadOneofCase.Result:
                        _awaitingResult = false;

                        break;

                    default:
                        throw new InvalidOperationException(
                            "The server wrote a message that is neither a question nor a result: "
                                + response.PayloadCase.ToString());
                }
            }

            if (_pendingNotifications.Count == 0)
            {
                _current = null;

                return false;
            }

            _current = _pendingNotifications.Dequeue();
            _awaitingResult = true;

            return true;
        }
    }

    /// <summary>A client that sends one notification and then answers nothing at all.</summary>
    /// <remarks>
    /// IT NEVER COMPLETES THE STREAM EITHER, which is the case the backstop exists for. Completing would
    /// end the read loop and let the ordinary teardown cancel the question; staying open and silent is
    /// what a client with a broken handler actually looks like, and only a deadline ends it.
    /// </remarks>
    private sealed class SilentStreams
    {
        internal SilentStreams(EventChainRequest notification)
        {
            Writer = new SignallingWriter();
            Reader = new SilentReader(notification);
        }

        internal SignallingWriter Writer { get; }

        internal SilentReader Reader { get; }
    }

    /// <summary>
    /// A client that sends one notification and then HALF-CLOSES without answering the question it
    /// provoked.
    /// </summary>
    private sealed class AbandoningStreams
    {
        internal AbandoningStreams(EventChainRequest notification)
        {
            Writer = new SignallingWriter();
            Reader = new AbandoningReader(notification);
        }

        internal SignallingWriter Writer { get; }

        internal AbandoningReader Reader { get; }
    }

    /// <summary>The client half of <see cref="AbandoningStreams"/>.</summary>
    /// <remarks>
    /// COMPLETING IS THE WHOLE FIXTURE. <see cref="MoveNext"/> answering <see langword="false"/> is what a
    /// half-closed request stream looks like to the server, and it is what must make the outstanding
    /// question fail at once rather than at the backstop.
    /// </remarks>
    private sealed class AbandoningReader(EventChainRequest notification)
        : IAsyncStreamReader<EventChainRequest>
    {
        private bool _sent;

        public EventChainRequest Current => notification;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_sent)
            {
                return Task.FromResult(false);
            }

            _sent = true;

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// A client that PIPELINES every notification it has without reading a single response, and then stays
    /// connected and silent.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ABUSE, AND IT IS NOT AN EXOTIC ONE. It is what a client written against the wire
    /// contract but not against the synchronous discipline does naturally: write everything, then read. It
    /// stays open at the end rather than half-closing, because half-closing would end the read loop and
    /// give a build with no ceiling a way to finish the call - which would make a broken build pass.
    /// </remarks>
    private sealed class FloodingStreams
    {
        internal FloodingStreams(params EventChainRequest[] notifications)
        {
            Writer = new SignallingWriter();
            Reader = new FloodingReader(notifications);
        }

        internal SignallingWriter Writer { get; }

        internal FloodingReader Reader { get; }
    }

    /// <summary>The client half of <see cref="FloodingStreams"/>.</summary>
    private sealed class FloodingReader(EventChainRequest[] notifications)
        : IAsyncStreamReader<EventChainRequest>
    {
        private readonly Queue<EventChainRequest> _pending = new(notifications);

        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private EventChainRequest? _current;

        public EventChainRequest Current => _current
            ?? throw new InvalidOperationException("MoveNext has not produced a message.");

        /// <summary>How many notifications the server actually took off this stream.</summary>
        /// <remarks>
        /// THE ASSERTION THAT THE READ LOOP WAS NEVER STALLED. A build whose queue blocked the writer would
        /// leave this below the scripted count, because the loop would never come back for the next message.
        /// </remarks>
        internal int Delivered { get; private set; }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_pending.Count > 0)
            {
                _current = _pending.Dequeue();
                Delivered++;

                return true;
            }

            // Connected and silent, exactly as SilentReader is: the wait is released by the cancellation
            // that the refused call's teardown raises, so it is ended rather than abandoned.
            await _never.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }
    }

    /// <summary>The client half of <see cref="SilentStreams"/>.</summary>
    private sealed class SilentReader(EventChainRequest notification)
        : IAsyncStreamReader<EventChainRequest>
    {
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _sent;

        public EventChainRequest Current => notification;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!_sent)
            {
                _sent = true;

                return true;
            }

            // Open and silent. The read is cancelled when the dispatcher faults on the backstop, which
            // is what ends the call - so this wait is released rather than abandoned.
            await _never.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }
    }
}
