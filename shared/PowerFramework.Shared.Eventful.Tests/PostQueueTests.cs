// ==============================================================================================
// PostQueueTests.cs
// A POSTED trigger is an explicitly QUEUED CONTINUATION the host drains. It is not fire and
// forget, and it is not an inline call.
// ==============================================================================================
//
// C-K (AAP 0.7.3) - THE SUBSTITUTION, NAMED HERE BECAUSE THIS IS THE FILE THAT ASSERTS IT
//
// The legacy of_post is eleven one-line subroutines, and every one of them is the same statement:
//
//     public subroutine of_post (readonly string name, readonly any param1)
//         Post _of_Trigger(name,{param1},true)
//
// verified at ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L445-L477, declared at
// :L132-L142. PowerScript's `Post` keyword does not execute the call. It places it on the WIN32
// MESSAGE QUEUE, where it waits for the PowerBuilder application's message pump to turn. The only
// difference between of_trigger and of_post is therefore WHEN the dispatch runs - plus one shape
// difference that follows from it, that of_post is declared a `subroutine` and so yields NO RETURN
// VALUE while of_trigger is a `function` that does (:L119-L129 against :L132-L142).
//
// AAP 0.6.5 lists that message pump, and the `Post` idiom it enables, as a DELIBERATE NON-PORT:
// "a headless service has no message pump", so "the posted deferred-accept at se_cst_dw.sru:L387
// -L393 becomes an explicit queued continuation". AAP 0.4.5.4 states the same rule from the other
// direction - "A *posted* call becomes an explicitly queued continuation ... because the Win32
// message pump it relies on does not exist in a headless container". There is nothing to wrap and
// nothing to emulate: the mechanism the legacy relied on is absent from the target by design.
//
// So the port replaces the pump with two members on the broker and nothing else:
//
//     EventBroker.Post(name, args)             queues one continuation, returns void
//     EventBroker.DrainPostedContinuations()   runs the queue on the CALLING thread, returns the
//                                              number of continuations it executed
//     EventBroker.PendingPostedContinuationCount   how many are waiting, run by nobody yet
//
// AND THAT IS WHAT MAKES THIS SUITE DETERMINISTIC RATHER THAN TIMING DEPENDENT. The window
// between Post returning and the drain being called is a window in which, provably, NOTHING HAS
// HAPPENED. A test can stand in that window and assert an empty log. Against a real message pump
// the same assertion would be a race, and the only way to observe a posted call would be to sleep
// or to poll - which would pin nothing at all, because a passing sleep and a broken implementation
// are indistinguishable. There is therefore no Task.Delay, no Thread.Sleep, no SpinWait, no
// polling loop, no Stopwatch, no DateTime, no timeout, no async and no second thread anywhere in
// this file. Every observation in it is separated from the work by an explicit drain call.
//
// WHAT IS DELIBERATELY NOT ASSERTED, and this is C-B (no behaviour improvements) applied to a
// test: nothing here asserts that a post EVENTUALLY runs on its own. In the port it does not. It
// runs when the host drains and at no other time. An assertion that a post "arrives" without a
// drain would be asserting a message pump that this system does not have.
//
// THE SECOND POSTING SITE, so a reader does not think Post exists only for of_post. The
// modification engine posts too: when a subscription is removed while a dispatch is in flight the
// entry is tombstoned and the compaction pass is POSTED rather than run - `Post _of_Collect()` at
// :L1083, inside the depth branch at :L1081-L1087. The port sends it through the SAME FIFO queue,
// which is what preserves the two mechanisms' relative order exactly as one Win32 queue did. That
// half of the queue's traffic belongs to LifetimeNamespaceFilterTests and SubscriptionManagement
// Tests, which assert the tombstone-and-sweep behaviour; this file owns the posted DISPATCH.
//
// THE POST FLAG IS CONTRACT, NOT DIAGNOSTICS, and section 4 below is why it has its own section.
// A dispatch saves the enclosing level's flag and sets its own on entry (:L810-L811) and restores
// it on unwind (:L953), and of_ispost publishes it (:L722, declared :L153). The flag is handed to
// the triggering and triggered hooks as their `isPost` argument, and the ONE real subclass of the
// broker in the whole legacy estate branches on it:
//
//     ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru
//       :L62-L64   if Not isPost then SetEvent(_hEvtSync)
//       :L69-L71   if Not isPost then ResetEvent(_hEvtSync)
//
// A caller waiting on that synchronisation handle is woken by a triggered dispatch and is NOT
// woken by a posted one. Reporting the flag wrongly would therefore hang a thread rather than
// merely mislabel a log line, which is the difference between a diagnostic and a contract.
//
// RETURN VALUE HANDLING DIFFERS FOR A POST, and the difference is a GATE ON THE DISPATCH LEVEL.
// The final substitution of the resolved default for a null result is guarded by `if Not isPost`
// at :L966, with the substitution itself at :L967-L969. A post has no caller to return to - it is
// a subroutine - so nothing is substituted. Section 5 asserts that the gate reads the flag of the
// dispatch level that is unwinding and not of some ambient state, by nesting a TRIGGER inside a
// drained POST and showing the inner level still substitutes.
//
// THREE LOCATOR CORRECTIONS, recorded rather than silently absorbed. This file's brief cites the
// post-flag restore at :L964, the substitution at :L969-L973 and the posted sweep at :L1085-L1086.
// Read off the file on disk, :L964 is the `end if` closing the compaction block and the restore is
// at :L953; :L971 is `end try` and :L973 is `return aVal`, so the gate is :L966 and the
// substitution :L967-L969; and :L1085 is the depth-zero `Events = newEvents` arm, so the posted
// sweep is :L1083. The behaviour described by the brief is exactly right in all three cases and
// the ported implementation cites the correct lines already; only the line numbers were off, and
// the corrected ones are what appear below.
//
// USER RULES: `review_rules` reports that NO USER RULES WERE PROVIDED for this project. None is
// invented here. The constraints this file answers to are the Agent Action Plan's own (AAP 0.7.3),
// named at each point they bite: C-K above, C-B throughout, C-D (no deferred capability - this
// file names no script-invoker type and no deferred service), and C-H (nullable reference types
// and warnings as errors apply, and nothing is suppressed).
//
// HOW THE ASSERTIONS ARE MADE
//   * PendingPostedContinuationCount is asserted BEFORE the drain in every case that counts
//     drained work, so "the queue was empty to begin with" is evidence rather than assumption.
//   * DrainPostedContinuations' return value is asserted, not discarded. It is the only report of
//     how much work ran, and a drain that ran the wrong number of continuations while producing
//     the right log is a real failure mode.
//   * Ordering is asserted as a SEQUENCE through DispatchLog.Labels, never as a set. A queue that
//     reordered would still satisfy a set comparison.
//   * Every matrix is a [Theory] with [MemberData] over TheoryData. [Fact] is reserved for cases
//     with no table.
//   * A fresh EventBroker, fresh subscribers and a fresh DispatchLog are built inside every test.
//     There is no shared mutable state between tests and no fixture, so tests cannot influence one
//     another and the file's result does not depend on execution order or parallelisation.
//
// FOLDER NEIGHBOURS THIS FILE DEFERS TO, referenced by name and not by cref so that this file
// cannot be broken by one of them being renamed:
//   * VetoSemanticsTests owns the tri-valued veto itself. Section 3 here makes one claim only -
//     that a veto raised inside a drained post does not leak into the next drained post.
//   * DispatchOrderTests owns the subscription table's ordering, which is name ascending then
//     priority descending. Section 2 here asserts the QUEUE is not sorted that way, which is a
//     different claim and the reason a descending-name row appears in its matrix.
//   * EventBrokerTests, EventBrokerDispatchInternalsTests and the folder's priority and capture
//     coverage own the non-post return-value and handled-latch rules. Section 5 here asserts only
//     what is specific to the post path.
//
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Verifies that <see cref="EventBroker.Post"/> queues a dispatch that only
/// <see cref="EventBroker.DrainPostedContinuations"/> runs, that the queue is first in first out,
/// that the post flag is visible and correctly scoped, and that arguments survive the queue
/// boundary.
/// </summary>
/// <remarks>
/// See the file header for the whole of the message-pump substitution this suite exists to pin
/// (AAP 0.7.3 C-K) and for the reason none of these tests may use a clock, a sleep or a second
/// thread.
/// </remarks>
public class PostQueueTests
{
    /// <summary>
    /// The topic standing in for the legacy deferred accept, which is the concrete site AAP 0.6.5
    /// names as depending on the post idiom (<c>se_cst_dw.sru:L387-L393</c>).
    /// </summary>
    private const string AcceptTextTopic = "acceptText";

    /// <summary>
    /// A second topic, used wherever a post must be shown to be deferred past a dispatch of a
    /// DIFFERENT name.
    /// </summary>
    private const string LoseFocusTopic = "loseFocus";

    /// <summary>The outer topic of every nested-dispatch case in this file.</summary>
    private const string OuterTopic = "outer";

    /// <summary>The inner topic of every nested-dispatch case in this file.</summary>
    private const string InnerTopic = "inner";

    /// <summary>
    /// Three topic names whose ORDINAL sort is exactly this order, so a post order that disagrees
    /// with it is evidence about the queue rather than about the subscription table.
    /// </summary>
    /// <remarks>
    /// Deliberately chosen so that <c>"alpha" &lt; "bravo" &lt; "charlie"</c> ordinally. The
    /// subscription table is held in ascending ordinal name order
    /// (<c>n_cst_eventful.sru:L407-L422</c>), which <c>DispatchOrderTests</c> owns; a port that
    /// reused that comparer for the continuation queue would silently reorder posted events, and
    /// the descending row in <see cref="CrossTopicPostOrderRows"/> is what catches it.
    /// </remarks>
    private const string FirstOrdinalTopic = "alpha";

    /// <summary>The ordinally middle topic. See <see cref="FirstOrdinalTopic"/>.</summary>
    private const string SecondOrdinalTopic = "bravo";

    /// <summary>The ordinally largest topic. See <see cref="FirstOrdinalTopic"/>.</summary>
    private const string ThirdOrdinalTopic = "charlie";

    /// <summary>
    /// Distinct, culture-free string payload values, indexed by argument slot.
    /// </summary>
    /// <remarks>
    /// Twelve entries, because the widest handler the shared double offers declares twelve
    /// parameters. Fixed literals rather than values formatted from an index, so no
    /// <see cref="IFormatProvider"/> and no culture can enter an argument-parity assertion.
    /// </remarks>
    private static readonly string[] PayloadTokens =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot",
        "golf", "hotel", "india", "juliett", "kilo", "lima",
    ];

    // =============================================================================================
    //  SHARED SCAFFOLDING
    //  Every helper below is deliberately dull. None of them touches a clock, a thread, the file
    //  system, a random source or a GUID, and none of them drains: a drain is always written out at
    //  the call site, because WHERE the drain happens is the property under test.
    // =============================================================================================

    /// <summary>
    /// Subscribes a fresh recording subscriber to a topic, labelled with the topic name so that
    /// <see cref="DispatchLog.Labels"/> reads back as the sequence of topics that were dispatched.
    /// </summary>
    /// <param name="broker">The broker to subscribe on, and the broker handed to any in-handler callback.</param>
    /// <param name="log">The shared log every subscriber in one test appends to.</param>
    /// <param name="topic">The event name to subscribe to, which is also the subscriber's label.</param>
    /// <param name="handlerName">The declared handler name to bind.</param>
    /// <returns>The subscriber, so a test can attach a callback or read its counters.</returns>
    /// <remarks>
    /// The subscribe result is asserted here rather than returned: a test whose subscription
    /// silently failed would report an empty log and read as "the post never ran", which is the one
    /// false negative this suite must never produce.
    /// </remarks>
    private static RecordingSubscriber SubscribeRecorder(
        EventBroker broker,
        DispatchLog log,
        string topic,
        string handlerName) =>
        SubscribeRecorderAs(broker, log, topic, topic, handlerName);

    /// <summary>
    /// Subscribes a fresh recording subscriber under an EXPLICIT label, for the cases that need two
    /// subscribers on one topic and must still tell them apart in the shared log.
    /// </summary>
    /// <param name="broker">The broker to subscribe on.</param>
    /// <param name="log">The shared log.</param>
    /// <param name="topic">The event name to subscribe to.</param>
    /// <param name="label">The label this subscriber records under.</param>
    /// <param name="handlerName">The declared handler name to bind.</param>
    /// <returns>The subscriber.</returns>
    private static RecordingSubscriber SubscribeRecorderAs(
        EventBroker broker,
        DispatchLog log,
        string topic,
        string label,
        string handlerName)
    {
        RecordingSubscriber subscriber = new(log, label, broker) { Topic = topic };

        Assert.Equal(RetCode.OK, broker.Subscribe(topic, subscriber, handlerName));

        return subscriber;
    }

    /// <summary>
    /// Asserts that no continuation is queued - the precondition of every case in this file that
    /// counts drained work.
    /// </summary>
    /// <param name="broker">The broker to inspect.</param>
    private static void AssertNothingIsQueued(EventBroker broker) =>
        Assert.Equal(0, broker.PendingPostedContinuationCount);

    /// <summary>
    /// Compares one boxed value - a recorded argument, or a dispatch's result - against its
    /// expectation.
    /// </summary>
    /// <param name="expected">The value that was supplied, or that the contract requires.</param>
    /// <param name="actual">The value that was observed.</param>
    /// <remarks>
    /// A one-line helper with both parameters typed <see cref="object"/> so that every such
    /// comparison in this file goes through one overload of the assertion regardless of whether the
    /// value happens to be a string, a number, a boolean or null. Comparing a boxed
    /// <see langword="bool"/> at the call site would otherwise select a different assertion overload
    /// from a boxed <see langword="long"/>, and the two report failures differently. Both the payload
    /// and the dispatch result are <c>any</c> in the legacy and <c>object?</c> here
    /// (AAP 0.4.5.2), so one weakly typed comparison is the faithful shape as well as the tidy one.
    /// </remarks>
    private static void AssertValue(object? expected, object? actual) =>
        Assert.Equal(expected, actual);

    /// <summary>
    /// Builds a deterministic payload of a given length, alternating a numeric slot with a string
    /// slot so a lost or reordered argument cannot be masked by two neighbours holding equal values.
    /// </summary>
    /// <param name="arity">How many arguments to build. Zero yields an empty payload.</param>
    /// <returns>The payload.</returns>
    private static object?[] BuildPayload(int arity)
    {
        if (arity == 0)
        {
            return [];
        }

        object?[] payload = new object?[arity];

        for (int index = 0; index < arity; index++)
        {
            // Even slots carry a long, odd slots a string. Both are distinct per slot, so a swap of
            // any two arguments changes the recorded row.
            payload[index] = index % 2 == 0 ? (long)(index + 1) : PayloadTokens[index];
        }

        return payload;
    }

    /// <summary>
    /// Selects the shared double's handler whose declared arity matches a payload length exactly, so
    /// every slot of the payload is reached and none is filled with a parameter's initial value.
    /// </summary>
    /// <param name="arity">The payload length.</param>
    /// <returns>The declared handler name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when no handler of that arity exists, which would make the matrix row untestable.
    /// </exception>
    private static string HandlerNameForArity(int arity) => arity switch
    {
        0 => RecordingHandlerNames.NoArguments,
        1 => RecordingHandlerNames.OneArgument,
        4 => RecordingHandlerNames.FourArguments,
        10 => RecordingHandlerNames.TenArguments,
        12 => RecordingHandlerNames.TwelveArguments,
        _ => throw new ArgumentOutOfRangeException(nameof(arity)),
    };

    /// <summary>
    /// Reads the post flag off every row a named broker hook recorded, in the order the hook fired.
    /// </summary>
    /// <param name="log">The shared log the hooks recorded into.</param>
    /// <param name="hookName">
    /// <see cref="TestEventBroker.TriggeringHookName"/> or
    /// <see cref="TestEventBroker.TriggeredHookName"/>.
    /// </param>
    /// <returns>The flags, in firing order.</returns>
    /// <remarks>
    /// The double records each of those two hooks as a row whose single argument IS the
    /// <c>isPost</c> value it was handed, which is that file's stand-in for the Win32 handle the real
    /// subclass would have signalled. Reading the row back is therefore reading exactly the argument
    /// <c>n_cst_threading_eventful.sru:L62</c> and <c>:L69</c> branch on.
    /// </remarks>
    private static List<bool> PostFlagsRecordedBy(DispatchLog log, string hookName)
    {
        List<bool> flags = [];

        for (int index = 0; index < log.Count; index++)
        {
            DispatchRecord record = log.RecordAt(index);

            if (!string.Equals(record.HandlerName, hookName, StringComparison.Ordinal))
            {
                continue;
            }

            // One argument exactly, and it is the flag. Asserted rather than assumed so a change to
            // the double's row shape fails here instead of silently changing what is being read.
            Assert.Equal(1, record.ArgumentCount);
            flags.Add(Assert.IsType<bool>(record.Arguments[0]));
        }

        return flags;
    }

    // =============================================================================================
    //  1. ENQUEUED, NOT INLINE - the suite's core claim
    //  of_post   n_cst_eventful.sru:L132-L142 declared, :L445-L477 defined, every body a `Post`
    //  of_trigger  :L119-L129 declared, :L233-L265 defined, every body a direct call
    // =============================================================================================

    /// <summary>
    /// <see cref="EventBroker.Post"/> queues the dispatch and returns. Before the drain, the
    /// subscriber has NOT run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS THE SUBSTITUTION AS CORRECT (AAP 0.7.3 C-K and C-B). <b>This single assertion is the
    /// whole suite.</b> The legacy <c>Post _of_Trigger(name,{...},true)</c> at
    /// <c>n_cst_eventful.sru:L445-L477</c> places the call on the Win32 message queue and does not
    /// execute it, so between the posting statement and the pump's next turn nothing has happened.
    /// AAP 0.6.5 makes that pump a deliberate non-port, so the port must offer the same window
    /// through an explicit drain - and if it did not, the queued-continuation substitution would not
    /// actually have been made.
    /// </para>
    /// <para>
    /// Three independent observations are taken inside the window, because each rules out a
    /// different wrong implementation. An empty log rules out an inline dispatch. A zero invocation
    /// count rules out a dispatch that ran but failed to record. A pending count of one rules out a
    /// post that was silently dropped - which would ALSO leave the log empty and would otherwise be
    /// indistinguishable from correct deferral.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostQueuesTheDispatchAndNothingHasRunBeforeTheDrain()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        broker.Post(AcceptTextTopic);

        // Not inline: the subscriber has not run.
        Assert.Empty(log.Records);
        Assert.Equal(0, subscriber.InvocationCount);

        // Not dropped either: exactly one continuation is waiting for a host that has not drained.
        Assert.Equal(1, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// The drain runs the queued dispatch exactly once and reports that it ran one continuation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the window. The drain is the port's replacement for the message pump
    /// turning, so this is where the posted dispatch actually happens - on the CALLING thread, with
    /// no hand-off anywhere.
    /// </para>
    /// <para>
    /// The drain's return value is asserted alongside the log because they answer different
    /// questions: the count says how many continuations were executed, the log says what they did. A
    /// drain that ran one continuation which dispatched nothing, and a drain that ran nothing at
    /// all, produce the same log and different counts.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDrainRunsTheQueuedDispatchExactlyOnce()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        broker.Post(AcceptTextTopic);

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal(1, log.Count);
        Assert.Equal(1, subscriber.InvocationCount);
        Assert.Equal(AcceptTextTopic, log.RecordAt(0).Topic);
        Assert.Equal(RecordingHandlerNames.NoArguments, log.RecordAt(0).HandlerName);

        // Consumed, not merely marked as run.
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// A second drain does not run the same continuation again: the queue is CONSUMED, not replayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mechanism is that each continuation is dequeued BEFORE it is invoked, so nothing can be
    /// executed twice even if it throws or itself drains. That matters more than it first appears:
    /// the posted deferred accept the legacy relies on (<c>se_cst_dw.sru:L389</c>) applies a pending
    /// edit, and applying it twice is a different outcome from applying it once.
    /// </para>
    /// <para>
    /// A Win32 queue behaves the same way - a dispatched message is removed from the queue - so this
    /// is fidelity rather than a design choice made freely.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondDrainDoesNotReplayAConsumedContinuation()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        broker.Post(AcceptTextTopic);

        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(1, subscriber.InvocationCount);

        // Nothing left to run, so nothing runs.
        Assert.Equal(0, broker.DrainPostedContinuations());
        Assert.Equal(0, broker.DrainPostedContinuations());

        Assert.Equal(1, log.Count);
        Assert.Equal(1, subscriber.InvocationCount);
    }

    /// <summary>
    /// Draining an empty queue is a harmless no-op: it throws nothing, runs nothing and reports
    /// zero.
    /// </summary>
    /// <remarks>
    /// A host drains at a point of its own choosing and cannot know whether anything is queued, so a
    /// defensive drain is the normal case rather than the exceptional one. Asserted with a
    /// subscription in place, so the case cannot pass merely because there was nothing to dispatch
    /// to.
    /// </remarks>
    [Fact]
    public void DrainingAnEmptyQueueRunsNothingAndThrowsNothing()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        AssertNothingIsQueued(broker);

        Assert.Equal(0, broker.DrainPostedContinuations());

        Assert.Empty(log.Records);
        Assert.Equal(0, subscriber.InvocationCount);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// <see cref="EventBroker.Post"/> yields no value while <see cref="EventBroker.Trigger"/> does -
    /// the subroutine-versus-function split, preserved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The legacy declares
    /// <c>public subroutine of_post(...)</c> in all eleven arities
    /// (<c>n_cst_eventful.sru:L132-L142</c>) and <c>public function any of_trigger(...)</c> in all
    /// eleven (<c>:L119-L129</c>). A subroutine has no return value in PowerScript, so the dispatch's
    /// result is discarded by the language itself; the port keeps that by declaring
    /// <see cref="EventBroker.Post"/> as <see langword="void"/>.
    /// </para>
    /// <para>
    /// <b>Asserted structurally, because there is no other way to assert an ABSENT return value.</b>
    /// A value that is never produced cannot be compared against anything, so the shape of the
    /// members is the evidence. This is also the assertion that would fail first if a future change
    /// "helpfully" made Post return the dispatch's value - which would be a widening of the contract
    /// under C-B, and would additionally imply that a posted dispatch had already run by the time
    /// Post returned.
    /// </para>
    /// <para>
    /// The drain's own <see cref="int"/> return is asserted in the same place, because it is the
    /// third member of this shape: Post reports nothing, Trigger reports the dispatch's value, and
    /// the drain reports how much work it did.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostIsASubroutineAndTriggerIsAFunction()
    {
        Type[] dispatchSignature = [typeof(string), typeof(object[])];

        MethodInfo? post = typeof(EventBroker).GetMethod(nameof(EventBroker.Post), dispatchSignature);
        MethodInfo? trigger = typeof(EventBroker).GetMethod(
            nameof(EventBroker.Trigger),
            dispatchSignature);
        MethodInfo? drain = typeof(EventBroker).GetMethod(
            nameof(EventBroker.DrainPostedContinuations),
            []);

        Assert.NotNull(post);
        Assert.NotNull(trigger);
        Assert.NotNull(drain);

        // :L132-L142 - `subroutine`, so no value reaches the caller.
        Assert.Equal(typeof(void), post.ReturnType);

        // :L119-L129 - `function any`, so the dispatch's value does. `any` maps to object? per
        // AAP 0.4.5.2.
        Assert.Equal(typeof(object), trigger.ReturnType);

        // The pump replacement reports its own work; see the file header.
        Assert.Equal(typeof(int), drain.ReturnType);
    }

    /// <summary>
    /// A post to a topic nobody subscribed to still queues, still drains, and runs nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The queue is upstream of the dispatch decision, which is exactly the legacy arrangement: the
    /// <c>Post</c> statement at <c>:L445-L477</c> enqueues unconditionally, and the "is anybody
    /// listening" question is not asked until <c>_of_Trigger</c> runs and takes one of its lexical
    /// rejects at <c>:L797-L798</c>. So an unsubscribed post costs one queue entry, and the entry is
    /// real work that the drain reports.
    /// </para>
    /// <para>
    /// Asserted because the alternative - checking for subscribers at post time and skipping the
    /// enqueue - is a plausible optimisation that would change observable behaviour: a subscription
    /// added between the post and the drain would then not be reached, whereas here it is. The
    /// second half of this test shows exactly that, which is why it subscribes AFTER posting.
    /// </para>
    /// </remarks>
    [Fact]
    public void APostToATopicWithNoSubscriberDrainsCleanlyAndRunsNothing()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        broker.Post(AcceptTextTopic);

        // Enqueued although nothing is listening.
        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Empty(log.Records);

        // And a subscription made after the post IS reached, because the dispatch decision is taken
        // at drain time and not at post time.
        broker.Post(AcceptTextTopic);
        RecordingSubscriber late = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(1, late.InvocationCount);
        Assert.Equal(1, log.Count);
    }

    // =============================================================================================
    //  2. ORDERING ACROSS MULTIPLE POSTS - first in, first out, and NOT sorted
    //  The Win32 message queue the legacy posts to is one strictly ordered queue, and the port sends
    //  posted dispatches and deferred compactions through one FIFO queue for the same reason.
    // =============================================================================================

    /// <summary>
    /// Four post orders of the same three topics, each paired with the drain order it must produce -
    /// which is always the post order itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second column never differs from the first, and that is the point of the table rather
    /// than an oversight: the claim is that the queue order is the POST order and is a function of
    /// nothing else.
    /// </para>
    /// <para>
    /// <b>The last row is the decisive one.</b> Its post order is strictly DESCENDING in ordinal
    /// name order, so a port that had reused the subscription table's comparer - ascending ordinal
    /// name, then descending priority (<c>n_cst_eventful.sru:L407-L422</c>) - for the continuation
    /// queue would drain it exactly backwards while still passing the ascending row. The first row
    /// is a second, weaker guard on the same defect, because its order matches neither ascending nor
    /// descending.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> CrossTopicPostOrderRows =>
        new()
        {
            // Neither ascending nor descending.
            {
                $"{ThirdOrdinalTopic} {FirstOrdinalTopic} {SecondOrdinalTopic}",
                $"{ThirdOrdinalTopic} {FirstOrdinalTopic} {SecondOrdinalTopic}"
            },

            // Rotated, so no single swap produces the ascending order either.
            {
                $"{SecondOrdinalTopic} {ThirdOrdinalTopic} {FirstOrdinalTopic}",
                $"{SecondOrdinalTopic} {ThirdOrdinalTopic} {FirstOrdinalTopic}"
            },

            // Ascending - the row a name-sorting queue would also pass, included so the matrix does
            // not accidentally exclude the correct-by-coincidence case.
            {
                $"{FirstOrdinalTopic} {SecondOrdinalTopic} {ThirdOrdinalTopic}",
                $"{FirstOrdinalTopic} {SecondOrdinalTopic} {ThirdOrdinalTopic}"
            },

            // Strictly descending - the row a name-sorting queue fails.
            {
                $"{ThirdOrdinalTopic} {SecondOrdinalTopic} {FirstOrdinalTopic}",
                $"{ThirdOrdinalTopic} {SecondOrdinalTopic} {FirstOrdinalTopic}"
            },
        };

    /// <summary>
    /// Posts drain in the order they were posted, across topics as well as within one topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Two posted events must arrive in the order
    /// they were posted, because the Win32 queue the legacy posts to is strictly ordered and
    /// single-threaded. AAP 0.4.5.4 is explicit that a fire-and-forget hand-off would reorder events
    /// relative to the legacy, and reordering is the failure this matrix detects.
    /// </para>
    /// <para>
    /// <b>Why the topics matter.</b> The subscription table IS sorted, by ascending ordinal name and
    /// then descending priority, and <c>DispatchOrderTests</c> owns that behaviour. The QUEUE is not.
    /// Sharing one comparer between the two would be an easy and completely silent defect - every
    /// single-topic test would still pass - so this matrix posts three differently named topics in
    /// orders that disagree with the name sort.
    /// </para>
    /// <para>
    /// The order is asserted as a sequence through <see cref="DispatchLog.Labels"/>, never as a set.
    /// Each subscriber is labelled with its own topic name by
    /// <see cref="SubscribeRecorder"/>, so the label sequence IS the dispatch sequence.
    /// </para>
    /// </remarks>
    /// <param name="postOrder">The space-separated topics to post, in order.</param>
    /// <param name="expectedDrainOrder">The space-separated order the drain must produce.</param>
    [Theory]
    [MemberData(nameof(CrossTopicPostOrderRows))]
    public void PostsDrainInTheOrderTheyWerePostedWhateverTheTopicNames(
        string postOrder,
        string expectedDrainOrder)
    {
        string[] posted = SplitTopics(postOrder);
        string[] expected = SplitTopics(expectedDrainOrder);

        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        // Subscribed in ASCENDING name order, so the subscription table is in its natural order and
        // any reordering seen below is attributable to the queue alone.
        SubscribeRecorder(broker, log, FirstOrdinalTopic, RecordingHandlerNames.NoArguments);
        SubscribeRecorder(broker, log, SecondOrdinalTopic, RecordingHandlerNames.NoArguments);
        SubscribeRecorder(broker, log, ThirdOrdinalTopic, RecordingHandlerNames.NoArguments);

        foreach (string topic in posted)
        {
            broker.Post(topic);
        }

        // Every post is its own entry; nothing has run yet.
        Assert.Equal(posted.Length, broker.PendingPostedContinuationCount);
        Assert.Empty(log.Records);

        Assert.Equal(posted.Length, broker.DrainPostedContinuations());

        Assert.Equal(expected, log.Labels);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// How many times to post one topic. Each row must produce that many distinct dispatches.
    /// </summary>
    /// <remarks>
    /// Two is the smallest count that can be coalesced at all, and is therefore the row that matters;
    /// three and four are included because a coalescing implementation that kept the first and the
    /// last would still pass at two.
    /// </remarks>
    public static TheoryData<int> RepeatedPostCountRows => new() { 2, 3, 4 };

    /// <summary>
    /// The queue holds ONE ENTRY PER POST and does not coalesce repeats, so each post arrives with
    /// its own arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). PowerScript's <c>Post</c> appends a message
    /// per statement; it does not deduplicate by target or by message, and neither does the port.
    /// Two posts of one topic with different payloads are two distinct dispatches with two distinct
    /// payloads.
    /// </para>
    /// <para>
    /// <b>Distinct arguments are what make this test able to fail.</b> Posting the same topic twice
    /// with the same payload would be satisfied by an implementation that ran one entry twice, or
    /// that ran a coalesced entry and reported two. Giving every post its own argument means the log
    /// must carry each argument exactly once, in post order.
    /// </para>
    /// </remarks>
    /// <param name="postCount">How many times to post the one topic.</param>
    [Theory]
    [MemberData(nameof(RepeatedPostCountRows))]
    public void TheQueueHoldsOneEntryPerPostAndDoesNotCoalesce(int postCount)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.OneArgument);

        for (int index = 0; index < postCount; index++)
        {
            broker.Post(AcceptTextTopic, PayloadTokens[index]);
        }

        Assert.Equal(postCount, broker.PendingPostedContinuationCount);
        Assert.Empty(log.Records);

        Assert.Equal(postCount, broker.DrainPostedContinuations());

        Assert.Equal(postCount, log.Count);
        Assert.Equal(postCount, subscriber.InvocationCount);

        for (int index = 0; index < postCount; index++)
        {
            DispatchRecord record = log.RecordAt(index);

            Assert.Equal(1, record.ArgumentCount);
            AssertValue(PayloadTokens[index], record.Arguments[0]);
        }
    }

    /// <summary>
    /// A direct <see cref="EventBroker.Trigger"/> issued between two posts runs IMMEDIATELY, before
    /// either of them, and the two posts keep their relative order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). This is the whole of the difference between
    /// the two members, expressed in one sequence: <c>of_trigger</c> is a direct call to
    /// <c>_of_Trigger</c> (<c>n_cst_eventful.sru:L233-L265</c>) and <c>of_post</c> hands the same
    /// call to the queue (<c>:L445-L477</c>). A triggered dispatch therefore overtakes every post
    /// already waiting, however long they have been waiting, because the pump has not turned.
    /// </para>
    /// <para>
    /// The consequence is worth stating because it is the reason the legacy uses a post at
    /// <c>se_cst_dw.sru:L389</c> at all: work posted from inside an event handler is guaranteed to
    /// run AFTER everything the current call stack does, including any further triggers that call
    /// stack issues. Ordering between the two channels is not merely defined, it is the mechanism.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADirectTriggerBetweenTwoPostsRunsImmediatelyAndThePostsKeepTheirOrder()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        SubscribeRecorder(broker, log, FirstOrdinalTopic, RecordingHandlerNames.NoArguments);
        SubscribeRecorder(broker, log, SecondOrdinalTopic, RecordingHandlerNames.NoArguments);
        SubscribeRecorder(broker, log, ThirdOrdinalTopic, RecordingHandlerNames.NoArguments);

        broker.Post(FirstOrdinalTopic);
        broker.Trigger(SecondOrdinalTopic);
        broker.Post(ThirdOrdinalTopic);

        // The trigger has already run; both posts are still waiting.
        Assert.Equal([SecondOrdinalTopic], log.Labels);
        Assert.Equal(2, broker.PendingPostedContinuationCount);

        Assert.Equal(2, broker.DrainPostedContinuations());

        // The trigger first, then the two posts in the order they were posted.
        Assert.Equal([SecondOrdinalTopic, FirstOrdinalTopic, ThirdOrdinalTopic], log.Labels);
    }

    /// <summary>
    /// Splits a space-separated topic list from a theory row.
    /// </summary>
    /// <param name="topics">The space-separated topics.</param>
    /// <returns>The topics in order.</returns>
    /// <remarks>
    /// Theory rows carry ordered topic lists as one string because a row must be a comparable value
    /// for the test name to be readable in a runner. This is the same convention
    /// <c>DispatchOrderTests</c> uses for the same reason.
    /// </remarks>
    private static string[] SplitTopics(string topics) =>
        topics.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // =============================================================================================
    //  3. POSTING FROM INSIDE A DISPATCH - the deferred-accept shape
    //  se_cst_dw.sru:L387-L393  event ondwnkillfocus
    //      if Not _bDoItemChange then
    //          Post _of_PostAcceptText()      <- posted PRECISELY so it runs after this event returns
    //      end if
    //  AAP 0.6.5 names this site as the one that depends on the post idiom, and AAP 0.4.5.4 requires
    //  it become "an explicitly queued continuation on the validation session".
    // =============================================================================================

    /// <summary>
    /// A post raised from INSIDE a triggered dispatch does not run within that dispatch, and runs on
    /// the next drain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B and C-K). <b>This is the deferred-accept shape
    /// the plan names.</b> The legacy kill-focus handler posts rather than calls, so the pending edit
    /// is applied after the handler - and after the whole event - has returned
    /// (<c>se_cst_dw.sru:L387-L393</c>). If the port ran the posted work inline, the accept would
    /// happen in the middle of the very event that scheduled it, which is the outcome the legacy uses
    /// a post to avoid.
    /// </para>
    /// <para>
    /// The topic posted here is the topic currently dispatching, which is the hardest case: an
    /// implementation that treated "already dispatching this name" as a reason to run inline, or a
    /// re-entrancy guard that dropped it, would both fail here and pass the different-topic case
    /// below.
    /// </para>
    /// <para>
    /// The callback posts only ONCE, guarded by its own counter. Without the guard the drained
    /// dispatch would post again, and again, and the test would be asserting a runaway rather than a
    /// deferral. The unbounded case is pinned deliberately and separately, by
    /// <see cref="ASelfPostingSubscriberIsDrainedUntilItStopsPosting"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void APostRaisedInsideATriggeredDispatchIsDeferredPastIt()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        int postsRaised = 0;
        List<EventBroker?> brokersSeen = [];

        subscriber.DuringAnyHandler = (dispatchingBroker, _) =>
        {
            brokersSeen.Add(dispatchingBroker);

            if (postsRaised > 0)
            {
                return;
            }

            postsRaised++;
            broker.Post(AcceptTextTopic);
        };

        broker.Trigger(AcceptTextTopic);

        // The enclosing dispatch has completed and the posted one has NOT run inside it.
        Assert.Equal(1, log.Count);
        Assert.Equal(1, subscriber.InvocationCount);
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal(2, log.Count);
        Assert.Equal(2, subscriber.InvocationCount);
        Assert.Equal(0, broker.PendingPostedContinuationCount);

        // The callback was handed this broker on both paths, so the post above went to the broker
        // that was dispatching rather than to some other instance.
        Assert.Equal([broker, broker], brokersSeen);
    }

    /// <summary>
    /// A post raised from inside a triggered dispatch for a DIFFERENT topic is deferred just the
    /// same.
    /// </summary>
    /// <remarks>
    /// The companion of the case above, and the shape the legacy site actually has: the kill-focus
    /// handler posts <c>_of_PostAcceptText</c>, which is not the event it is running inside. Asserted
    /// separately because the two would be told apart by any implementation that special-cased
    /// re-entrancy on the name, and because this is the version a reader of
    /// <c>se_cst_dw.sru:L389</c> will recognise.
    /// </remarks>
    [Fact]
    public void APostRaisedInsideATriggeredDispatchForAnotherTopicIsDeferredToo()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber killFocus = SubscribeRecorder(
            broker,
            log,
            LoseFocusTopic,
            RecordingHandlerNames.NoArguments);

        SubscribeRecorder(broker, log, AcceptTextTopic, RecordingHandlerNames.NoArguments);

        killFocus.DuringAnyHandler = (_, _) => broker.Post(AcceptTextTopic);

        broker.Trigger(LoseFocusTopic);

        // Only the triggered topic ran; the posted accept is queued.
        Assert.Equal([LoseFocusTopic], log.Labels);
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal([LoseFocusTopic, AcceptTextTopic], log.Labels);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// A post raised while a drain is in progress is run by THAT SAME drain call, not deferred to the
    /// next one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE IMPLEMENTATION'S CHOICE, PINNED (AAP 0.7.3 C-K).</b> Of the two defensible readings -
    /// defer newly queued work to a subsequent drain, or let the running drain pick it up - the port
    /// takes the second, and <see cref="EventBroker.DrainPostedContinuations"/> documents it: a
    /// continuation may itself post, and the newly queued work is drained by the same loop. The
    /// mechanism is that the loop's condition re-reads the queue length on every iteration rather
    /// than capturing a count at entry.
    /// </para>
    /// <para>
    /// <b>Why this reading is the faithful one.</b> A Win32 pump behaves the same way: a message
    /// posted from inside a message handler is appended to the same queue the pump is already
    /// draining, and the pump reaches it on a later turn of the SAME loop without the application
    /// doing anything. Deferring it to "the next drain" would introduce a boundary the legacy does
    /// not have, and a caller that drained once per host tick would then see posted work arrive a
    /// tick later than the legacy delivered it.
    /// </para>
    /// <para>
    /// The distinction is entirely invisible to a caller who drains in a loop, and entirely visible
    /// to one who drains once - which is why it is asserted through the drain's own return value.
    /// Two continuations ran, so the count is two, and neither entry is left behind.
    /// </para>
    /// </remarks>
    [Fact]
    public void APostRaisedWhileDrainingIsRunByTheSameDrainCall()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber first = SubscribeRecorder(
            broker,
            log,
            LoseFocusTopic,
            RecordingHandlerNames.NoArguments);

        SubscribeRecorder(broker, log, AcceptTextTopic, RecordingHandlerNames.NoArguments);

        int postsRaised = 0;

        first.DuringAnyHandler = (_, _) =>
        {
            if (postsRaised > 0)
            {
                return;
            }

            postsRaised++;
            broker.Post(AcceptTextTopic);
        };

        broker.Post(LoseFocusTopic);
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        // ONE drain call, TWO continuations executed: the second was queued while this call was
        // already running.
        Assert.Equal(2, broker.DrainPostedContinuations());

        Assert.Equal([LoseFocusTopic, AcceptTextTopic], log.Labels);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// How many times a subscriber re-posts itself before it stops. Each row must drain to exactly
    /// one more dispatch than that.
    /// </summary>
    /// <remarks>
    /// Zero is the control: a subscriber that posts nothing drains to one. The large row is what makes
    /// the case about termination rather than about arithmetic - the queue is consumed entry by entry
    /// by a loop, so a long chain finishes rather than accumulating, and each entry still runs exactly
    /// once.
    /// </remarks>
    public static TheoryData<int> SelfPostingRepostCountRows => new() { 0, 1, 3, 250 };

    /// <summary>
    /// A self-posting subscriber is drained until it stops posting, and every queued entry runs
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hazard this pins is the obvious one: a subscriber that posts the topic it is handling is a
    /// self-feeding queue, and the port's decision to let the running drain pick up newly queued work
    /// is precisely what makes it self-feeding. The behaviour must therefore be bounded by the
    /// subscriber and by nothing else, which is what this asserts - the drain runs exactly as many
    /// continuations as were ever queued, and stops when the subscriber stops.
    /// </para>
    /// <para>
    /// <b>Nothing here is a guard against a runaway, and none is added.</b> A cap, a depth limit or a
    /// deferral would each be a behavioural change of the kind C-B forbids: a Win32 pump has no such
    /// cap either, and a self-posting message handler loops there too. The correct statement is that
    /// the port terminates exactly when the legacy would, and this test is that statement.
    /// </para>
    /// <para>
    /// Deterministic in every row: the subscriber's own counter decides when to stop, so the drained
    /// count is a function of the row and not of scheduling.
    /// </para>
    /// </remarks>
    /// <param name="repostCount">How many times the subscriber re-posts before it stops.</param>
    [Theory]
    [MemberData(nameof(SelfPostingRepostCountRows))]
    public void ASelfPostingSubscriberIsDrainedUntilItStopsPosting(int repostCount)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        int reposted = 0;

        subscriber.DuringAnyHandler = (_, _) =>
        {
            if (reposted >= repostCount)
            {
                return;
            }

            reposted++;
            broker.Post(AcceptTextTopic);
        };

        broker.Post(AcceptTextTopic);
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        // The original entry plus one per re-post, all from one drain call.
        Assert.Equal(repostCount + 1, broker.DrainPostedContinuations());

        Assert.Equal(repostCount + 1, log.Count);
        Assert.Equal(repostCount + 1, subscriber.InvocationCount);
        Assert.Equal(repostCount, reposted);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// A veto raised inside one drained post does not reach the post drained after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Each drained continuation is its own
    /// top-level dispatch, entered at depth zero, so the unwind's consume arm applies to it:
    /// <c>elseif _nPrevent = PREVENT_ONCE or _nDeep = 0 then _nPrevent = 0</c>
    /// (<c>n_cst_eventful.sru:L956-L957</c>). Even the DEEP mode - the one that deliberately survives
    /// an unwind to stop an enclosing dispatch - is consumed here, because there is no enclosing
    /// dispatch to survive into once the depth is back to zero.
    /// </para>
    /// <para>
    /// <b>This file makes only the isolation claim.</b> The tri-valued veto itself, the once-versus-
    /// deep divergence and the prevent state's behaviour under nesting belong to
    /// <c>VetoSemanticsTests</c>, which owns them in full. What is specific to the queue is that two
    /// continuations drained by one call must not be able to veto each other, and the deep mode is
    /// used here precisely because it is the mode that COULD leak if the levels were not properly
    /// separated.
    /// </para>
    /// <para>
    /// Two subscribers per topic, because the evidence needs both halves: the veto must be shown to
    /// have taken effect within its own dispatch - the second subscriber of the vetoing topic is
    /// skipped - and to have had no effect on the next, where both subscribers run.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVetoRaisedInsideOneDrainedPostDoesNotReachTheNextOne()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber vetoing = SubscribeRecorderAs(
            broker,
            log,
            LoseFocusTopic,
            "veto-first",
            RecordingHandlerNames.NoArguments);

        SubscribeRecorderAs(
            broker,
            log,
            LoseFocusTopic,
            "veto-second",
            RecordingHandlerNames.NoArguments);

        SubscribeRecorderAs(
            broker,
            log,
            AcceptTextTopic,
            "next-first",
            RecordingHandlerNames.NoArguments);

        SubscribeRecorderAs(
            broker,
            log,
            AcceptTextTopic,
            "next-second",
            RecordingHandlerNames.NoArguments);

        List<long> preventCodes = [];

        vetoing.DuringAnyHandler = (_, _) => preventCodes.Add(broker.Prevent(deep: true));

        broker.Post(LoseFocusTopic);
        broker.Post(AcceptTextTopic);

        Assert.Equal(2, broker.PendingPostedContinuationCount);
        Assert.Equal(2, broker.DrainPostedContinuations());

        // The veto was accepted, which is what makes the rest of the assertion meaningful.
        Assert.Equal([RetCode.OK], preventCodes);

        // It stopped its OWN dispatch after the first subscriber, and reached neither subscriber of
        // the post drained next.
        Assert.Equal(["veto-first", "next-first", "next-second"], log.Labels);
    }

    // =============================================================================================
    //  4. THE POST FLAG - contract, not diagnostics
    //  n_cst_eventful.sru:L810-L811  bIsPost = _bIsPost / _bIsPost = isPost   (save, then set)
    //                    :L953       _bIsPost = bIsPost                        (restore on unwind)
    //                    :L722       return _bIsPost                           (of_ispost, decl :L153)
    //  n_cst_threading_eventful.sru:L62-L64 and :L69-L71 branch on the SAME flag, signalling and
    //  resetting a synchronisation handle only when the dispatch is NOT a post.
    // =============================================================================================

    /// <summary>
    /// <see cref="EventBroker.IsPost"/> answers <see langword="true"/> inside a drained post's
    /// subscriber and <see langword="false"/> inside a direct trigger's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Read from INSIDE the dispatch, which is the
    /// only place the flag means anything: the dispatch sets it on entry and restores it on unwind
    /// (<c>n_cst_eventful.sru:L810-L811</c> and <c>:L953</c>), so a reading taken afterwards is a
    /// reading of the resting value and not of the dispatch.
    /// </para>
    /// <para>
    /// One subscriber and one callback serve both paths, so the only difference between the two
    /// readings is the dispatch that produced them. The reading is taken again after the post but
    /// BEFORE the drain, and no third entry appears - which is the same core claim as section 1,
    /// re-stated on the flag: a post that had run inline would have added its reading there.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsPostIsTrueInsideADrainedPostAndFalseInsideADirectTrigger()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.NoArguments);

        List<bool> flagsSeenInsideTheDispatch = [];

        subscriber.DuringAnyHandler = (_, _) => flagsSeenInsideTheDispatch.Add(broker.IsPost());

        broker.Trigger(AcceptTextTopic);
        Assert.Equal([false], flagsSeenInsideTheDispatch);

        broker.Post(AcceptTextTopic);

        // Nothing ran, so nothing was read.
        Assert.Equal([false], flagsSeenInsideTheDispatch);

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal([false, true], flagsSeenInsideTheDispatch);
    }

    /// <summary>
    /// The triggering and triggered hooks receive the post flag, with the correct value on each path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and <b>this is the assertion that makes the
    /// flag a contract rather than a diagnostic.</b> The one real subclass of the broker in the whole
    /// legacy estate branches on exactly this argument:
    /// </para>
    /// <code>
    /// ontriggering  n_cst_threading_eventful.sru:L62-L64   if Not isPost then SetEvent(_hEvtSync)
    /// ontriggered                               :L69-L71   if Not isPost then ResetEvent(_hEvtSync)
    /// </code>
    /// <para>
    /// A caller waiting on that handle is woken by a triggered dispatch and deliberately NOT woken by
    /// a posted one. Reporting the flag wrongly would therefore hang a thread, or wake one that
    /// should still be waiting - a failure of a different order from a mislabelled log line, and the
    /// reason this is asserted at the hook boundary and not only through
    /// <see cref="EventBroker.IsPost"/>.
    /// </para>
    /// <para>
    /// <see cref="TestEventBroker"/> records each of those two hooks as a row whose single argument IS
    /// the <c>isPost</c> value it was handed - its own substitute for the handle it cannot signal in a
    /// headless service - so reading the row back reads the exact value the legacy branches on. The
    /// pair is asserted together because the broker guards both with the same "was anything
    /// dispatched" latch, so either both fire or neither does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTriggeringAndTriggeredHooksReceiveThePostFlagOnBothPaths()
    {
        DispatchLog log = new();
        TestEventBroker broker = new(log);

        AssertNothingIsQueued(broker);

        SubscribeRecorder(broker, log, AcceptTextTopic, RecordingHandlerNames.NoArguments);

        broker.Trigger(AcceptTextTopic);

        // The triggering hook fired first, on the dispatched name, with the flag clear.
        Assert.Equal(TestEventBroker.TriggeringHookName, log.RecordAt(0).HandlerName);
        Assert.Equal(AcceptTextTopic, log.RecordAt(0).Topic);
        Assert.Equal(TestEventBroker.DefaultLabel, log.RecordAt(0).Label);
        Assert.Equal([false], PostFlagsRecordedBy(log, TestEventBroker.TriggeringHookName));
        Assert.Equal([false], PostFlagsRecordedBy(log, TestEventBroker.TriggeredHookName));

        broker.Post(AcceptTextTopic);

        // Queued only: neither hook has fired for it.
        Assert.Equal([false], PostFlagsRecordedBy(log, TestEventBroker.TriggeringHookName));
        Assert.Equal([false], PostFlagsRecordedBy(log, TestEventBroker.TriggeredHookName));

        Assert.Equal(1, broker.DrainPostedContinuations());

        // Both hooks fired again, this time with the flag set.
        Assert.Equal([false, true], PostFlagsRecordedBy(log, TestEventBroker.TriggeringHookName));
        Assert.Equal([false, true], PostFlagsRecordedBy(log, TestEventBroker.TriggeredHookName));
    }

    /// <summary>
    /// The post flag is RESTORED when a nested dispatch unwinds: a trigger fired from inside a drained
    /// post reports <see langword="false"/>, and the enclosing post still reports
    /// <see langword="true"/> afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The flag is per dispatch LEVEL, not per
    /// broker. Every dispatch saves the enclosing level's value and installs its own
    /// (<c>n_cst_eventful.sru:L810-L811</c>) and puts the saved one back on the way out
    /// (<c>:L953</c>), which is why the broker is genuinely re-entrant on one instance rather than
    /// needing one instance per level.
    /// </para>
    /// <para>
    /// <b>Three readings, and each rules out a different defect.</b> The reading before the nested
    /// trigger establishes the outer level is a post. The reading inside the nested trigger must be
    /// false - a flag stored per broker instead of per level would report true there, and the
    /// threading subclass would then fail to signal its synchronisation handle for a genuinely
    /// synchronous dispatch. The reading after the nested trigger returns must be true again - a
    /// dispatch that reset the flag to false on unwind instead of restoring the saved value would
    /// report false there, and the outer level would have silently changed character halfway
    /// through.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePostFlagIsRestoredAfterANestedTriggerReturns()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber outer = SubscribeRecorder(
            broker,
            log,
            OuterTopic,
            RecordingHandlerNames.NoArguments);

        RecordingSubscriber inner = SubscribeRecorder(
            broker,
            log,
            InnerTopic,
            RecordingHandlerNames.NoArguments);

        List<bool> outerBeforeNested = [];
        List<bool> outerAfterNested = [];
        List<bool> insideNested = [];

        inner.DuringAnyHandler = (_, _) => insideNested.Add(broker.IsPost());

        outer.DuringAnyHandler = (_, _) =>
        {
            outerBeforeNested.Add(broker.IsPost());
            broker.Trigger(InnerTopic);
            outerAfterNested.Add(broker.IsPost());
        };

        broker.Post(OuterTopic);
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        // One continuation: the nested dispatch is a direct call and never enters the queue.
        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal([true], outerBeforeNested);
        Assert.Equal([false], insideNested);
        Assert.Equal([true], outerAfterNested);

        Assert.Equal([OuterTopic, InnerTopic], log.Labels);
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// Outside any dispatch the post flag reads <see langword="false"/>, on a fresh broker and after
    /// every path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The resting value is <see langword="false"/>, RECORDED rather than assumed.</b> It is not
    /// an assertion about anything meaningful happening - outside a dispatch there is no dispatch to
    /// describe - but it is the value a caller will actually observe, so it is written down here
    /// instead of being left for someone to rediscover. Two things produce it: the backing field
    /// starts <see langword="false"/> (the port of <c>boolean _bIsPost</c> at
    /// <c>n_cst_eventful.sru:L83</c>, which PowerScript initialises false), and every unwind restores
    /// the value saved on entry, so the outermost unwind necessarily restores the resting value.
    /// </para>
    /// <para>
    /// The reading taken between the post and the drain is the interesting one of the four: a queued
    /// post has not set the flag, because nothing about the dispatch has begun.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsPostRestsFalseOutsideAnyDispatch()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        // Fresh instance, nothing has ever been dispatched.
        Assert.False(broker.IsPost());

        SubscribeRecorder(broker, log, AcceptTextTopic, RecordingHandlerNames.NoArguments);

        broker.Trigger(AcceptTextTopic);
        Assert.False(broker.IsPost());

        broker.Post(AcceptTextTopic);

        // Queued but not begun: the flag belongs to a dispatch, and no dispatch has started.
        Assert.False(broker.IsPost());

        Assert.Equal(1, broker.DrainPostedContinuations());

        // Restored by the posted level's unwind (:L953).
        Assert.False(broker.IsPost());
        Assert.Equal(2, log.Count);
    }

    // =============================================================================================
    //  5. ARGUMENTS AND RETURN VALUES ACROSS THE QUEUE BOUNDARY
    //  The legacy packs its payload into an array literal AT THE POSTING STATEMENT - `{param1,...}`
    //  inside `Post _of_Trigger(name,{...},true)` (:L445-L477) - so the values are fixed at the
    //  moment of posting. The final default substitution is gated on the dispatch NOT being a post
    //  (:L966, substituting at :L967-L969), because a subroutine has no caller to return to.
    // =============================================================================================

    /// <summary>
    /// Payload lengths to post, each matched by a handler of exactly that declared arity.
    /// </summary>
    /// <remarks>
    /// Zero is the empty-payload case, which the legacy spells with its own overload -
    /// <c>any emptyParams[]</c> followed by the post (<c>n_cst_eventful.sru:L475-L477</c>). Ten sits
    /// exactly on the legacy arity ceiling, and twelve is deliberately past it: the ceiling existed
    /// only because PowerScript cannot forward an arbitrary-length argument list, and AAP 0.2.1.4
    /// records such ceilings as legacy limits the .NET contract may exceed without behavioural
    /// regression. The arities are matched exactly so that every slot is reached by the payload and
    /// none is filled with a parameter's initial value, which keeps this matrix about the QUEUE
    /// rather than about the argument-passing rules.
    /// </remarks>
    public static TheoryData<int> ArgumentArityRows => new() { 0, 1, 4, 10, 12 };

    /// <summary>
    /// A drained post delivers every argument, in order and unmodified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The enqueue must carry the payload across the
    /// queue boundary intact. This is not a given: the payload now has to survive the interval between
    /// the post and the drain, which in the legacy was the interval between the <c>Post</c> statement
    /// and the pump's next turn - and the legacy solved it by evaluating the array literal at the
    /// posting statement.
    /// </para>
    /// <para>
    /// Alternating numeric and string slots is what makes a reordering visible: every value in the
    /// payload is distinct from every other, so a swap of any two slots changes the recorded row.
    /// </para>
    /// </remarks>
    /// <param name="arity">The payload length, matched by a handler of the same declared arity.</param>
    [Theory]
    [MemberData(nameof(ArgumentArityRows))]
    public void ADrainedPostDeliversEveryArgumentInOrder(int arity)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            HandlerNameForArity(arity));

        object?[] payload = BuildPayload(arity);

        broker.Post(AcceptTextTopic, payload);

        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.Empty(log.Records);

        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord record = log.RecordAt(0);

        Assert.Equal(arity, record.ArgumentCount);

        for (int slot = 0; slot < arity; slot++)
        {
            AssertValue(payload[slot], record.Arguments[slot]);
        }
    }

    /// <summary>
    /// A payload length paired with the slot that carries <see langword="null"/>. The last three rows
    /// put it in the TRAILING position, which is the one an implementation is most likely to lose.
    /// </summary>
    /// <remarks>
    /// A trailing null is the interesting case because an array whose final element is null looks, to
    /// a length-trimming or "skip the empty tail" implementation, exactly like an array that is one
    /// element shorter. Leading and interior positions are included so that a defect confined to one
    /// end cannot pass by luck.
    /// </remarks>
    public static TheoryData<int, int> NullArgumentSlotRows =>
        new()
        {
            // The whole payload is one null.
            { 1, 0 },

            // Leading, interior and trailing within one four-slot payload.
            { 4, 0 },
            { 4, 2 },
            { 4, 3 },

            // Trailing at the legacy ceiling, and trailing past it.
            { 10, 9 },
            { 12, 11 },
        };

    /// <summary>
    /// A <see langword="null"/> argument survives the enqueue, in every slot including the trailing
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). A PowerScript <c>any</c> holds null, and the
    /// tri-state predicates that the framework's whole return-code algebra rests on depend on null
    /// being representable and distinguishable (AAP 0.4.2.3 on the predicates). A queue that dropped a
    /// null argument, or that replaced it with a type's initial value, would therefore change meaning
    /// and not merely representation.
    /// </para>
    /// <para>
    /// Every other slot is asserted alongside the null one, so a payload that survived only by being
    /// truncated at the null cannot pass.
    /// </para>
    /// </remarks>
    /// <param name="arity">The payload length.</param>
    /// <param name="nullSlot">The zero-based slot that carries null.</param>
    [Theory]
    [MemberData(nameof(NullArgumentSlotRows))]
    public void ANullArgumentSurvivesTheEnqueueAtEverySlot(int arity, int nullSlot)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        SubscribeRecorder(broker, log, AcceptTextTopic, HandlerNameForArity(arity));

        object?[] payload = BuildPayload(arity);
        payload[nullSlot] = null;

        broker.Post(AcceptTextTopic, payload);

        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.Equal(1, broker.DrainPostedContinuations());

        DispatchRecord record = log.RecordAt(0);

        Assert.Equal(arity, record.ArgumentCount);
        Assert.Null(record.Arguments[nullSlot]);

        for (int slot = 0; slot < arity; slot++)
        {
            AssertValue(payload[slot], record.Arguments[slot]);
        }
    }

    /// <summary>
    /// A posted <see langword="null"/> is DELIVERED as null and is not confused with a slot the
    /// payload never reached, which receives its parameter type's initial value instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and <b>this is the assertion that makes the
    /// null-survival claim decisive.</b> Against handlers whose parameters are all weakly typed, a
    /// dropped argument and a delivered null are indistinguishable - both read back as null. Against a
    /// STRONGLY typed handler they are not: the legacy fills a slot the payload did not reach with the
    /// type's initial value, which for a string is the empty string and explicitly not null
    /// (<c>w_test_eventful.srw:L246</c>), while an explicitly supplied null stays null.
    /// </para>
    /// <para>
    /// So the two halves below are the same handler reached two ways. The full payload puts a real
    /// null in the string slot and the handler receives null. The short payload reaches only the first
    /// slot, and the handler receives the empty string and <see langword="false"/> for the two it did
    /// not reach. If the queue had silently dropped the null, the first half would read like the
    /// second.
    /// </para>
    /// </remarks>
    [Fact]
    public void APostedNullIsDeliveredAsNullAndNotAsAnInitialValue()
    {
        EventBroker deliveringBroker = new();
        DispatchLog deliveringLog = new();

        SubscribeRecorder(
            deliveringBroker,
            deliveringLog,
            AcceptTextTopic,
            RecordingHandlerNames.TypedArguments);

        // A real null in the middle slot, whose parameter is a string.
        deliveringBroker.Post(AcceptTextTopic, 7L, null, true);

        Assert.Equal(1, deliveringBroker.PendingPostedContinuationCount);
        Assert.Equal(1, deliveringBroker.DrainPostedContinuations());

        DispatchRecord delivered = deliveringLog.RecordAt(0);

        Assert.Equal(3, delivered.ArgumentCount);
        AssertValue(7L, delivered.Arguments[0]);
        Assert.Null(delivered.Arguments[1]);
        AssertValue(true, delivered.Arguments[2]);

        // The contrast: a payload that reaches only the first slot. A fresh broker and log, so the two
        // halves cannot influence one another.
        EventBroker shortBroker = new();
        DispatchLog shortLog = new();

        SubscribeRecorder(shortBroker, shortLog, AcceptTextTopic, RecordingHandlerNames.TypedArguments);

        shortBroker.Post(AcceptTextTopic, 7L);

        Assert.Equal(1, shortBroker.DrainPostedContinuations());

        DispatchRecord unreached = shortLog.RecordAt(0);

        Assert.Equal(3, unreached.ArgumentCount);
        AssertValue(7L, unreached.Arguments[0]);

        // Initial values, NOT null: the empty string and false.
        AssertValue(string.Empty, unreached.Arguments[1]);
        AssertValue(false, unreached.Arguments[2]);
    }

    /// <summary>
    /// <see cref="EventBroker.Post"/> snapshots the caller's array AT POST TIME, so a mutation made
    /// before the drain does not reach the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The legacy builds its payload as an array
    /// literal inside the posting statement itself -
    /// <c>Post _of_Trigger(name,{param1,param2},true)</c> at <c>n_cst_eventful.sru:L445-L477</c> - and
    /// that literal is evaluated when the statement executes. The values a posted dispatch sees are
    /// therefore the values that existed at the moment of posting, and nothing the caller does
    /// afterwards can reach them. A snapshot is the faithful reading, and the port takes one.
    /// </para>
    /// <para>
    /// <b>This is the one place where Post and Trigger genuinely differ in their treatment of the
    /// payload, and the difference is required rather than incidental.</b>
    /// <see cref="EventBroker.Trigger"/> takes no copy, and correctly so: its dispatch is synchronous,
    /// so the caller has no opportunity to mutate the array between the call and the last handler
    /// returning. A post has a window, so it must copy. A port that copied in neither place would
    /// deliver drain-time values, which for a caller reusing one buffer across several posts is a
    /// different payload entirely - and every post would then deliver whatever the LAST one wrote.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostSnapshotsTheCallersArrayAtPostTimeNotAtDrainTime()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        SubscribeRecorder(broker, log, AcceptTextTopic, RecordingHandlerNames.FourArguments);

        object?[] arguments = BuildPayload(4);
        object?[] postedValues = [arguments[0], arguments[1], arguments[2], arguments[3]];

        broker.Post(AcceptTextTopic, arguments);

        // Every slot rewritten after the post and before the drain.
        arguments[0] = 999L;
        arguments[1] = "mutated";
        arguments[2] = -1L;
        arguments[3] = null;

        Assert.Equal(1, broker.DrainPostedContinuations());

        DispatchRecord record = log.RecordAt(0);

        Assert.Equal(4, record.ArgumentCount);

        for (int slot = 0; slot < 4; slot++)
        {
            AssertValue(postedValues[slot], record.Arguments[slot]);
        }
    }

    /// <summary>
    /// The snapshot is SHALLOW: the array is copied, the values it references are not, so a mutation
    /// of a referenced object IS observed by the drained handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and recorded explicitly because it is the
    /// boundary of the case above rather than a contradiction of it. What is snapshotted is the
    /// payload's SHAPE - which slot holds which reference - and not the state behind each reference.
    /// </para>
    /// <para>
    /// <b>This is the faithful reading, not a limitation of the port.</b> A PowerScript <c>any</c>
    /// holding an object holds a POINTER, and the array literal at the posting statement copies that
    /// pointer. A posted dispatch in the legacy therefore also observes whatever state the referenced
    /// object has reached by the time the pump turns. Deep-copying here would be the behavioural
    /// improvement C-B forbids, and it would additionally break the legacy's own idiom of posting an
    /// object so that a handler can act on its CURRENT state.
    /// </para>
    /// <para>
    /// The value is read from inside the handler as well as off the recorded row, because those are
    /// two different claims: the row shows the same instance arrived, and the in-handler reading shows
    /// the handler observed its mutated state.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArgumentSnapshotIsShallowSoAMutatedReferenceIsObserved()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber subscriber = SubscribeRecorder(
            broker,
            log,
            AcceptTextTopic,
            RecordingHandlerNames.OneArgument);

        List<string> valuesSeenByTheHandler = [];

        subscriber.DuringAnyHandler = (_, record) =>
            valuesSeenByTheHandler.Add(Assert.IsType<MutableToken>(record.Arguments[0]).Value);

        MutableToken token = new("original");

        broker.Post(AcceptTextTopic, token);

        // Mutated between the post and the drain, through the reference the payload carries.
        token.Value = "mutated";

        Assert.Equal(1, broker.DrainPostedContinuations());

        // The same instance arrived - the slot was snapshotted...
        Assert.Same(token, log.RecordAt(0).Arguments[0]);

        // ...and its CURRENT state is what the handler saw, because the instance was not.
        Assert.Equal(["mutated"], valuesSeenByTheHandler);
    }

    /// <summary>
    /// A drained post produces no default-value substitution, while a trigger nested inside it still
    /// does - the gate reads the flag of the dispatch level that is unwinding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The final substitution of the resolved
    /// default for a null result is guarded by <c>if Not isPost then</c>
    /// (<c>n_cst_eventful.sru:L966</c>), substituting at <c>:L967-L969</c>. A posted dispatch skips it,
    /// and the reason is structural: <c>of_post</c> is a <c>subroutine</c>, so there is no caller to
    /// return to and a substituted value would have nowhere to go.
    /// </para>
    /// <para>
    /// <b>The gate is therefore only OBSERVABLE through nesting, and that is what this test does.</b>
    /// The substitution writes the value the dispatch is about to return; on the posted path nothing
    /// reads that value, so the skip cannot be seen directly. What can be seen is that the gate reads
    /// the <c>isPost</c> of the level that is unwinding rather than any ambient state: a
    /// <see cref="EventBroker.Trigger"/> issued from INSIDE a drained post is its own dispatch level
    /// with its own flag clear, so its substitution still happens and its caller - the posted
    /// subscriber - receives the default. A port that had hoisted the flag to the broker would return
    /// null there instead, and every nested trigger inside posted work would silently lose its
    /// default return value.
    /// </para>
    /// <para>
    /// The non-posted baseline is asserted first, on the same broker and the same topic, so the
    /// substitution is known to be working before the posted path is examined - otherwise a port that
    /// never substituted at all would pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADrainedPostGetsNoDefaultSubstitutionWhileANestedTriggerStillDoes()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        // Registered at depth zero, which is where the setter accepts them (:L500 and :L536).
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(OuterTopic, "outer-default"));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(InnerTopic, "inner-default"));

        RecordingSubscriber outer = SubscribeRecorder(
            broker,
            log,
            OuterTopic,
            RecordingHandlerNames.NoArguments);

        SubscribeRecorder(broker, log, InnerTopic, RecordingHandlerNames.NoArguments);

        // BASELINE, non-posted: both subscribers return null, so the resolved default is substituted
        // and reaches the caller.
        AssertValue("outer-default", broker.Trigger(OuterTopic));
        log.Clear();

        List<object?> nestedResults = [];

        outer.DuringAnyHandler = (_, _) => nestedResults.Add(broker.Trigger(InnerTopic));

        broker.Post(OuterTopic);
        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal([OuterTopic, InnerTopic], log.Labels);

        // The nested level is NOT a post, so its own substitution still applies.
        Assert.Single(nestedResults);
        AssertValue("inner-default", nestedResults[0]);

        // The posted level has no return channel at all - Post is a subroutine - and it leaves nothing
        // accumulated behind it either.
        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());
    }

    /// <summary>
    /// A drained post accumulates a handled return value visibly from INSIDE the dispatch and leaves
    /// nothing behind once it unwinds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Posting changes the RETURN CHANNEL and
    /// nothing else: the accumulation rules, the handled latch and the dispatch-level scoping of
    /// <see cref="EventBroker.GetReturnValue"/> are identical on both paths. The accumulated value is
    /// saved on entry and restored on unwind (<c>n_cst_eventful.sru:L811-L812</c> and <c>:L951</c>),
    /// so it is readable only while the dispatch is running - which for a post means only from inside
    /// a drained continuation, because that is the only moment the dispatch exists.
    /// </para>
    /// <para>
    /// <b>The accumulation rules themselves are not this file's subject.</b> The handled latch, the
    /// capture filter and the interaction with a registered default belong to the folder's dispatch
    /// and capture coverage - <c>EventBrokerTests</c>, <c>EventBrokerDispatchInternalsTests</c> and
    /// <c>DispatchOrderTests</c>. What is asserted here is only that the queue boundary does not
    /// change them.
    /// </para>
    /// <para>
    /// The second subscriber claims capture <c>*</c> deliberately. Once the first subscriber has
    /// handled the event, an unhandled-only subscriber stops being eligible - that is the capture
    /// filter at <c>:L831-L833</c>, and it is legacy behaviour owned elsewhere - so a reader that did
    /// not claim capture-all would simply never run and would report nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADrainedPostAccumulatesItsReturnValueInsideAndLeavesNothingBehind()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        AssertNothingIsQueued(broker);

        RecordingSubscriber handling = SubscribeRecorderAs(
            broker,
            log,
            AcceptTextTopic,
            "handling",
            RecordingHandlerNames.NoArguments);

        handling.SetReturnValue(RecordingHandlerNames.NoArguments, "handled-by-first");

        // Capture-all, so this one remains eligible after the event has been handled.
        RecordingSubscriber reading = new(log, "reading", broker) { Topic = AcceptTextTopic };

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(
                WithCaptureAll(AcceptTextTopic),
                reading,
                RecordingHandlerNames.NoArguments));

        List<object?> accumulatedInside = [];
        List<bool> processedInside = [];

        reading.DuringAnyHandler = (_, _) =>
        {
            accumulatedInside.Add(broker.GetReturnValue());
            processedInside.Add(broker.IsProcessed());
        };

        broker.Post(AcceptTextTopic);

        // Nothing has run, so there is nothing accumulated.
        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal(["handling", "reading"], log.Labels);

        // Visible from inside the drained dispatch, on both members.
        Assert.Single(accumulatedInside);
        AssertValue("handled-by-first", accumulatedInside[0]);
        Assert.Equal([true], processedInside);

        // And scoped to the level, so the unwind leaves the broker exactly as it was found.
        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());
    }

    /// <summary>
    /// Prefixes a topic with the capture-all symbol, so a subscription stays eligible after the event
    /// has been handled.
    /// </summary>
    /// <param name="topic">The logical event name.</param>
    /// <returns>The subscription topic carrying the capture-all symbol.</returns>
    /// <remarks>
    /// Composed from <see cref="TopicSymbols.All"/> rather than written as a literal <c>'*'</c>, so the
    /// spelling here cannot drift from the parser that reads it back. The symbol is stripped from the
    /// stored name by the leading-symbol run at <c>n_cst_eventful.sru:L340-L363</c>, so the
    /// subscription's dispatch name is <paramref name="topic"/> unchanged - which is why the log rows
    /// still read against the plain topic.
    /// </remarks>
    private static string WithCaptureAll(string topic) =>
        string.Concat(TopicSymbols.All.ToString(), topic);

    /// <summary>
    /// A mutable payload value, for the one case that pins the argument snapshot as SHALLOW.
    /// </summary>
    /// <remarks>
    /// Private and nested because it exists for exactly one test and has no meaning outside it. It
    /// carries no behaviour: a settable property is the whole of it, because the property under test
    /// belongs to the broker's snapshot and not to this class.
    /// </remarks>
    private sealed class MutableToken
    {
        /// <summary>Initializes a new instance of the <see cref="MutableToken"/> class.</summary>
        /// <param name="value">The initial value.</param>
        internal MutableToken(string value) => Value = value;

        /// <summary>Gets or sets the carried value.</summary>
        internal string Value { get; set; }
    }
}
