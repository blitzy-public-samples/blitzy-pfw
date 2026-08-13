// ==============================================================================================
// VetoSemanticsTests.cs
// The event broker's dispatch veto is TRI-VALUED, and its two prevent modes genuinely differ.
// ==============================================================================================
//
// WHAT THIS FILE IS, AND WHY IT IS THE FOLDER'S HEADLINE ASSERTION
// AAP 0.6.1.2 states the position this suite exists to defend: "The broker's veto is tri-valued
// ... Flattening it to a boolean would silently convert a deep prevention into a shallow one."
// AAP 0.4.2.3 repeats it as a hard instruction against VetoResult.cs - "Tri-valued ... Never
// flattened to boolean". A statement in a plan is not a guarantee; a test that fails when the
// statement stops being true is. This file is that test.
//
// THE MECHANISM, TRACED TO THE ORACLE. Every locator below points into
//     ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
// and was read off the file on disk rather than copied forward.
//
//   * ONE private field carries the state: `long _nPrevent` [:L85]. Its three reachable values
//     are 0, PREVENT_ONCE = 1 and PREVENT_DEEP = 2 [:L110-L112].
//   * `of_prevent(deep)` [:L1276-L1300] is the only writer. It first tests the dispatch depth and
//     RETURNS THE FAILURE CODE when there is no dispatch to prevent [:L1293], then assigns the
//     deep flag or the once flag by an if/else [:L1294-L1298] and answers OK [:L1299]. The
//     parameterless overload is a single-line delegation with `deep` false [:L1302], so it means
//     once.
//   * The dispatch loop tests the state at the TOP OF EVERY ITERATION and leaves the loop when it
//     is non-zero: `if _nPrevent <> 0 then exit` [:L822]. That single line is the whole of "stops
//     the dispatch", and to it once and deep are indistinguishable.
//   * On ENTRY a dispatch level saves the enclosing level's state and ZEROES ITS OWN
//     [:L812-L813], having already saved and incremented the depth [:L808-L809]. That is what
//     makes the broker genuinely re-entrant on ONE instance rather than two independent brokers.
//   * On UNWIND, after the depth has been restored [:L952], three branches decide what survives
//     [:L954-L958]:
//
//         954: if nPrevent <> 0 then
//         955:     _nPrevent = nPrevent
//         956: elseif _nPrevent = PREVENT_ONCE or _nDeep = 0 then
//         957:     _nPrevent = 0
//         958: end if
//
//     - :L955 RESTORE. An enclosing veto already in effect survives the inner dispatch verbatim.
//     - :L957 CONSUME. A once veto is consumed by the level that raised it, and any veto is
//       consumed once the depth is back to zero.
//     - the absent `else` LEAVES IT SET. A deep veto at a depth still above zero is deliberately
//       left standing, so the enclosing loop's :L822 test fires on its next iteration and the
//       outer chain aborts too.
//
// EVERYTHING IN THIS FILE HINGES ON THAT ONE `elseif` AT :L956.
// A boolean veto passes every single-level case in this file. It fails only the nested ones,
// because a boolean would clear or keep both modes identically. The nested divergence theory is
// therefore authored FIRST below and the single-level cases are its scaffolding, not the reverse:
// the single-level cases establish that both modes abort their own level, which is precisely what
// makes the nested divergence attributable to the mode and to nothing else.
//
// C-K (AAP 0.7.3) - THE BOUNDARY DECISION, NAMED. This is why the NUMBERS are asserted and not
// only the behaviours. The veto does not stay inside one process. AAP 0.4.3 C-03 puts
// DataServices' `EventChain` on a bidirectional gRPC stream "carrying all 13 raw and all 9
// semantic events with sequencing tokens", and AAP 0.6.1.2 names the tri-valued broker veto as
// part of what that stream must carry - "prevent-once is 1, prevent-deep is 2, and continue is
// the third state". A wire contract is pinned by its values, not by its identifiers: a peer
// speaking the contract reads 2 off the wire, and if this port ever renumbered or collapsed the
// alphabet the failure would surface as a remote deep prevention silently degrading to a shallow
// one. So VetoNumericValueRows below fixes 0, 1 and 2 against the legacy constant spellings, and
// the behavioural theories fix what each number DOES. Both halves are required; neither is
// sufficient.
//
// C-B (AAP 0.7.3) - THE DEEP MODE'S SURVIVAL IS ASSERTED AS CORRECT, NOT TIDIED AWAY.
// A reader meeting :L958 for the first time sees a `finally` block that leaves a private flag set
// on the way out, and the reflex is to call that a leak and "fix" it. It is not a leak, it is the
// feature: it is the only mechanism by which an inner subscriber can abort an outer chain, and
// AAP 0.6.1.5 shows the DataWindow item-change protocol firing a nested event from inside itself
// [se_cst_dw.sru:L182-L253], which is exactly the shape that depends on it. This suite asserts
// the survival positively, and separately asserts the ONE place the state really must not
// survive - past depth zero [:L956's second disjunct], where a leak would silently disable the
// broker for the remainder of the process.
//
// THREE DISTINCT NUMERIC ALPHABETS SHARE THE NUMERALS 1 AND 2. This file models exactly one of
// them and deliberately touches neither other:
//   1. THE DISPATCH VETO - `VetoResult`, 0 continue / 1 once / 2 deep. This file.
//   2. THE EXCEPTION HOOK'S OUTCOME - at [:L889-L895] a returned 1 means prevent and leaves the
//      loop, a returned 2 means CONTINUE and clears the has-exception latch, anything else falls
//      through to a rethrow [:L902]. Note 2 means continue there and deep prevention here: near
//      opposites at the same numeral. That alphabet belongs to ExceptionCaptureTests.
//   3. THE PREPARE AND TRIGGERING HOOKS' RETURN CODE - a RetCode tested with the shared
//      IsPrevented predicate against RetCode.PREVENT (1) [:L609, :L840]. That belongs to the hook
//      suites.
// The subclass at ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L48-L53 uses the
// first and third FOUR LINES APART - `of_Prevent()` at :L50 and then `return 1` at :L51 - which
// is the proof they are separate mechanisms rather than one value read twice. The only RetCode
// values this file names are OK and FAILED, and both are the documented answers of `of_prevent`
// itself [:L1293, :L1299] rather than a veto state.
//
// THE NESTED SHAPE IS THE ORACLE'S OWN, NOT AN INVENTION.
// ws_objects/pfw.tests.pbl.src/w_test_eventful.srw subscribes three handlers [:L250-L252], starts
// the outer dispatch with `of_Trigger("clicked", …)` [:L364, :L393], and from INSIDE the handler
// that dispatch invokes fires a second, nested dispatch: `evtful.of_Trigger("test","hello pfw!")`
// [:L80]. Every nested arrangement below is that shape - an outer topic with several subscribers,
// one of which triggers an inner topic that also has several subscribers.
//
// FOUR LOCATORS CORRECTED AGAINST THE FILE ON DISK, recorded so the next reader does not have to
// re-derive them. The authoring brief cited the top-of-iteration guard as :L820 (that line is the
// `for` header; the guard is :L822), the unwind block as :L961-L966 (it is :L954-L958, an offset
// of about seven lines), the depth guard inside `of_prevent` as :L1287 (that line is a comment in
// the function banner; the guard is :L1293), and the oracle's nested trigger as
// w_test_eventful.srw:L83 (that line is a Chinese comment; the trigger is :L80). The behaviour
// described at each was verified unchanged - only the line numbers moved.
//
// ONE BRIEF EXPECTATION CONTRADICTED THE SOURCE, AND THE SOURCE WINS.
// The brief asked for a row in which an outer subscriber vetoes DEEP and then triggers an inner
// dispatch, asserting "the inner dispatch is never entered at all". It is entered, and its
// subscribers run. The reason is :L813: a dispatch level zeroes ITS OWN prevent state on entry
// unconditionally, so the state an enclosing level is holding cannot stop an inner level from
// starting - it can only be restored over the top of it at :L955 once that level unwinds. Both
// the source read and a measured run agree. AAP 0.7.3 C-C makes ws_objects/** the behavioural
// oracle and C-B forbids inventing a behaviour it does not have, so the arrangement is kept as a
// theory row and the assertion is the source-faithful one. It is documented at the test rather
// than silently changed, and it turns out to be worth more than the expectation it replaced: it
// pins the :L955 restore branch, which is the branch a port most easily loses.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//   * No timing of any kind. No Task.Delay, no Thread.Sleep, no clock read, no GUID, no random
//     value, no thread creation, no asynchrony. Nesting here is SYNCHRONOUS RE-ENTRANCY on one
//     broker instance, exactly as the oracle's is, so every ordering assertion is deterministic
//     by construction rather than by a tolerance. Because nothing is awaited, xUnit1051 - which
//     TreatWarningsAsErrors turns into a build error - cannot arise.
//   * No file, network or database access, and nothing reads the legacy tree at run time. Every
//     expectation lives in C# member data.
//   * No reference to the deferred script-invoker type or to any other deferred capability
//     (AAP 0.7.3 C-D). `n_cst_eventful.sru` is the ONE object of pfw.utility.invoker that AAP
//     0.4.1 assigns to the shared in-scope layer; the seven dynamic-invocation natives beside it
//     are ScriptBridge and are not implemented, not stubbed and not named here.
//   * No SCREAMING_SNAKE member. The repository-root .editorconfig lowers CA1707 and IDE1006 for
//     ten named IMPLEMENTATION files and for no test file, and TreatWarningsAsErrors is true, so
//     such a member would be a build error rather than a style debate. Legacy spellings appear in
//     comments and in theory-row data instead, which is where they are readable and harmless.
//   * No suppression pragma, no NoWarn, and no null-forgiving operator used to dodge a real
//     nullability question (AAP 0.7.3 C-H). Where a nullable value must be narrowed, it is
//     narrowed by an assertion that returns the non-null value.
//
// The legacy tree is READ-ONLY specification and shares this working directory. It is the
// behavioural oracle for parity testing: read, never edited, moved, reformatted or built.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Linq;

using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Shared.Eventful.Tests
{
    /// <summary>
    /// Behavioural tests for the broker's tri-valued dispatch veto: that both prevent modes abort
    /// the level that raised them, that only the deep mode survives the unwind of a nested
    /// dispatch, that an enclosing veto is restored rather than lost, and that no veto outlives
    /// the outermost dispatch. Ported from <c>of_prevent</c> and the dispatch unwind in
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru</c>.
    /// </summary>
    public class VetoSemanticsTests
    {
        // =========================================================================================
        //  TOPIC NAMES AND LABELS
        //  Held as constants so an expected label sequence in a theory row and the subscription
        //  that produces it cannot drift apart. The names are lower-case ASCII with no leading
        //  symbol, so every subscription lands at Priorities.Normal and the dispatch order is the
        //  SUBSCRIPTION order - the broker appends at the tail of an equal-priority run
        //  (n_cst_eventful.sru:L419). Ordering symbols are deliberately not used here: this suite
        //  is about the veto, and the topic grammar has its own suite.
        // =========================================================================================

        /// <summary>The enclosing dispatch's topic.</summary>
        private const string OuterTopic = "outer";

        /// <summary>The nested dispatch's topic, triggered from inside an outer subscriber.</summary>
        private const string InnerTopic = "inner";

        /// <summary>The middle topic of the three-level arrangement.</summary>
        private const string MiddleTopic = "middle";

        /// <summary>The innermost topic of the three-level arrangement.</summary>
        private const string CoreTopic = "core";

        /// <summary>The single topic used by the single-level arrangements.</summary>
        private const string SoleTopic = "sole";

        /// <summary>The first subscriber of the outermost run, and of a single-level run.</summary>
        private const string FirstLabel = "first";

        /// <summary>
        /// The second subscriber of the outermost run, and of a single-level run: the one that
        /// vetoes, or that triggers the nested dispatch.
        /// </summary>
        private const string SecondLabel = "second";

        /// <summary>
        /// The third subscriber of the outermost run, and of a single-level run: the one whose fate
        /// IS the assertion in every case below.
        /// </summary>
        private const string ThirdLabel = "third";

        /// <summary>The first subscriber of the nested run.</summary>
        private const string InnerFirstLabel = "inner-first";

        /// <summary>The second subscriber of the nested run - the one that vetoes.</summary>
        private const string InnerSecondLabel = "inner-second";

        /// <summary>The third subscriber of the nested run - skipped by either prevent mode.</summary>
        private const string InnerThirdLabel = "inner-third";

        /// <summary>The first subscriber of the middle run of the three-level arrangement.</summary>
        private const string MiddleFirstLabel = "middle-first";

        /// <summary>
        /// The second subscriber of the middle run - the one that triggers the innermost dispatch.
        /// </summary>
        private const string MiddleSecondLabel = "middle-second";

        /// <summary>
        /// The third subscriber of the middle run: it runs under a once veto and does not under a
        /// deep one, which is how the middle level's abort is observed.
        /// </summary>
        private const string MiddleThirdLabel = "middle-third";

        /// <summary>The first subscriber of the innermost run of the three-level arrangement.</summary>
        private const string CoreFirstLabel = "core-first";

        /// <summary>The second subscriber of the innermost run - the one that vetoes.</summary>
        private const string CoreSecondLabel = "core-second";

        /// <summary>The third subscriber of the innermost run - skipped by either prevent mode.</summary>
        private const string CoreThirdLabel = "core-third";

        /// <summary>
        /// The leading-run symbol that claims capture-everything, prefixed to a topic by the one
        /// arrangement that needs it. Legacy spelling <c>SYMBOL_ALL</c>
        /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L96</c>], selecting the
        /// capture flag <c>CAP_ALL</c> [<c>:L102</c>] at <c>:L348-L350</c>.
        /// </summary>
        /// <remarks>
        /// Written as a literal here rather than read from the library's own symbol catalogue, for
        /// two reasons. It keeps this suite's compile-time surface to exactly the two types under
        /// test plus the recording double - the veto enum and the broker - so a test of the veto
        /// cannot be broken by an unrelated change to the topic grammar. And a test should not take
        /// its input from the production constant it is exercising: pinning the character
        /// independently is what makes the arrangement verifiable against the oracle rather than
        /// against the port. The topic grammar has its own suite, where the catalogue itself is the
        /// subject.
        /// </remarks>
        private const string CaptureEverythingSymbol = "*";

        /// <summary>
        /// The value the first subscriber of the return-value arrangement returns, which latches the
        /// dispatch as handled and is the contribution a veto must not disturb.
        /// </summary>
        /// <remarks>
        /// A <see langword="long"/> rather than an <see cref="int"/>, because the oracle's handler
        /// return type is PowerScript <c>any</c> over values that are most often <c>long</c>
        /// (<c>w_test_eventful.srw:L62</c> returns the sum of two <c>long</c> arguments), and the
        /// broker's handled-state comparison is a comparison of boxed values.
        /// </remarks>
        private const long HandledReturnValue = 42L;

        // =========================================================================================
        //  HELPERS
        //  Three of them, so that the arrangements below read as statements about the veto rather
        //  than as subscription plumbing. Each asserts the legacy success code as it goes, so a
        //  mis-typed handler name fails at the line that caused it instead of surfacing later as a
        //  puzzling empty log.
        // =========================================================================================

        /// <summary>
        /// Creates a recording subscriber and subscribes its zero-argument handler to
        /// <paramref name="topic"/>, asserting the legacy success code.
        /// </summary>
        /// <param name="broker">The broker to subscribe on, and the one handed to the double.</param>
        /// <param name="log">The shared ordering log every subscriber of a test writes to.</param>
        /// <param name="topic">The subscription topic, with no leading symbol run.</param>
        /// <param name="label">The label this subscriber writes to the log.</param>
        /// <returns>The subscribed double, so the caller can attach an in-handler action.</returns>
        /// <remarks>
        /// The broker is handed to the double EXPLICITLY rather than left to the ambient
        /// <see cref="EventBroker.Current"/>. Both routes work - the double resolves
        /// <c>Broker ?? EventBroker.Current</c>, and the ambient one is the port of
        /// <c>Message.PowerObjectParm</c> which the oracle's own handler reads
        /// (<c>w_test_eventful.srw:L69</c>) - but an explicit reference keeps every arrangement
        /// below independent of which broker happens to be dispatching, which matters in the
        /// nested cases where two levels of the SAME broker are on the stack at once.
        /// </remarks>
        private static RecordingSubscriber SubscribeTo(
            EventBroker broker,
            DispatchLog log,
            string topic,
            string label)
        {
            RecordingSubscriber subscriber = new(log, label, broker) { Topic = topic };

            // n_cst_eventful.sru:L442 - of_On answers RetCode.OK on a successful subscription.
            Assert.Equal(RetCode.OK, broker.Subscribe(topic, subscriber, RecordingHandlerNames.NoArguments));

            return subscriber;
        }

        /// <summary>
        /// Creates a recording subscriber that captures every handled state and subscribes it to
        /// <paramref name="topic"/>, asserting the legacy success code.
        /// </summary>
        /// <param name="broker">The broker to subscribe on, and the one handed to the double.</param>
        /// <param name="log">The shared ordering log.</param>
        /// <param name="topic">The subscription topic, WITHOUT the capture symbol.</param>
        /// <param name="label">The label this subscriber writes to the log.</param>
        /// <returns>The subscribed double.</returns>
        /// <remarks>
        /// Needed by exactly one arrangement, and needed there for a reason worth stating: the
        /// default capture mode is unhandled-only, and the dispatch skips an unhandled-only
        /// subscriber as soon as an earlier one has HANDLED the event
        /// (<c>n_cst_eventful.sru:L831-L833</c>). So a subscriber that must still run after a
        /// handling subscriber has to claim the capture-everything symbol
        /// (<c>w_test_eventful.srw:L229</c>). Without it, the return-value theory below would
        /// observe its later subscribers not running and would credit the veto for a skip the
        /// capture filter had already performed - a false pass.
        /// </remarks>
        private static RecordingSubscriber SubscribeCapturingEverythingTo(
            EventBroker broker,
            DispatchLog log,
            string topic,
            string label)
        {
            RecordingSubscriber subscriber = new(log, label, broker) { Topic = topic };

            // The '*' of the leading symbol run: CAP_ALL (n_cst_eventful.sru:L348-L350). The symbol
            // is stripped from the stored name (:L363), so the residual dispatch key is `topic`
            // unchanged and this subscription joins the same run as the plain ones beside it.
            Assert.Equal(
                RetCode.OK,
                broker.Subscribe(
                    string.Concat(CaptureEverythingSymbol, topic),
                    subscriber,
                    RecordingHandlerNames.NoArguments));

            return subscriber;
        }

        /// <summary>
        /// Attaches an action that runs while <paramref name="subscriber"/>'s handler is executing
        /// and the dispatch is still in flight.
        /// </summary>
        /// <param name="subscriber">The double to attach to.</param>
        /// <param name="action">The in-handler action.</param>
        /// <remarks>
        /// This is the route by which a subscriber reaches the broker from inside a dispatch, which
        /// is the only place a veto is meaningful (<c>n_cst_eventful.sru:L1293</c>). The double runs
        /// it as its THIRD step, after the invocation has already been recorded and counted, so a
        /// subscriber that vetoes still appears in the log as having run - which is what lets an
        /// assertion distinguish "ran and vetoed" from "was skipped".
        /// </remarks>
        private static void WhileHandling(RecordingSubscriber subscriber, Action action) =>
            subscriber.SetInterceptor(
                RecordingHandlerNames.NoArguments,
                (_, _) => action());

        // =========================================================================================
        //  1. THE DECISIVE CASE - THE NESTED DIVERGENCE
        //
        //  Authored first on purpose. Every single-level case in this file passes under a boolean
        //  veto; only this one fails. It is therefore the assertion the suite exists for, and the
        //  cases further down are its scaffolding rather than the other way round.
        //
        //  ONE arrangement, run TWICE, with the prevent mode as the ONLY variable - so the
        //  difference in outcome cannot be attributed to the topology, to the subscription order,
        //  to the number of subscribers or to anything else the two runs might otherwise not share.
        // =========================================================================================

        /// <summary>
        /// The outcome of one run of the two-level arrangement, carried whole so a test can assert
        /// on the ordering, on the codes and on the broker's post-dispatch state together.
        /// </summary>
        private sealed class TwoLevelRun
        {
            /// <summary>Initializes a new instance of the <see cref="TwoLevelRun"/> class.</summary>
            /// <param name="broker">The broker the run used, for post-dispatch probing.</param>
            /// <param name="log">The shared ordering log the run wrote to.</param>
            /// <param name="preventCode">The code the in-handler veto answered.</param>
            /// <param name="innerResult">What the nested trigger returned.</param>
            /// <param name="outerResult">What the outermost trigger returned.</param>
            internal TwoLevelRun(
                EventBroker broker,
                DispatchLog log,
                long preventCode,
                object? innerResult,
                object? outerResult)
            {
                Broker = broker;
                Log = log;
                PreventCode = preventCode;
                InnerResult = innerResult;
                OuterResult = outerResult;
            }

            /// <summary>Gets the broker the run used.</summary>
            internal EventBroker Broker { get; }

            /// <summary>Gets the shared ordering log.</summary>
            internal DispatchLog Log { get; }

            /// <summary>Gets the code the in-handler veto answered.</summary>
            internal long PreventCode { get; }

            /// <summary>Gets what the nested trigger returned.</summary>
            internal object? InnerResult { get; }

            /// <summary>Gets what the outermost trigger returned.</summary>
            internal object? OuterResult { get; }
        }

        /// <summary>
        /// Builds and runs the oracle's nested shape on a FRESH broker: an outer topic with three
        /// subscribers whose second one triggers an inner topic that also has three subscribers, and
        /// whose second inner subscriber vetoes in the mode under test.
        /// </summary>
        /// <param name="deep">
        /// <see langword="true"/> for the deep prevent mode, <see langword="false"/> for the once
        /// mode. The mode is passed straight to <see cref="EventBroker.Prevent(bool)"/>, so a row
        /// exercises the legacy <c>of_prevent(deep)</c> if/else at
        /// <c>n_cst_eventful.sru:L1294-L1298</c> and nothing else differs between the two runs.
        /// </param>
        /// <returns>The run's outcome.</returns>
        /// <remarks>
        /// <para>
        /// The shape is <c>w_test_eventful.srw</c>'s own: that window subscribes its handlers at
        /// <c>:L250-L252</c>, starts the outer dispatch with <c>of_Trigger("clicked", …)</c> at
        /// <c>:L364</c>, and fires <c>of_Trigger("test","hello pfw!")</c> at <c>:L80</c> from INSIDE
        /// the handler that dispatch invoked. Reproducing the oracle's own nesting rather than
        /// inventing one matters, because the whole claim under test is a claim about the oracle.
        /// </para>
        /// <para>
        /// The nesting is genuine RE-ENTRANCY on one broker instance, not two brokers: the depth is
        /// saved and incremented on entry (<c>:L808-L809</c>) and restored on unwind
        /// (<c>:L952</c>), which is what gives the unwind an enclosing level to hand the state back
        /// to. Two brokers would have nothing to diverge about.
        /// </para>
        /// </remarks>
        private static TwoLevelRun RunTwoLevelArrangement(bool deep)
        {
            DispatchLog log = new();

            // A FRESH broker per run. Test hygiene, so no veto state can arrive from a previous
            // test - and NOT a substitute for the depth-zero clearing asserted further down, which
            // is a property of the broker itself and is measured on a single instance.
            EventBroker broker = new();

            RecordingSubscriber outerFirst = SubscribeTo(broker, log, OuterTopic, FirstLabel);
            RecordingSubscriber outerSecond = SubscribeTo(broker, log, OuterTopic, SecondLabel);
            RecordingSubscriber outerThird = SubscribeTo(broker, log, OuterTopic, ThirdLabel);

            RecordingSubscriber innerFirst = SubscribeTo(broker, log, InnerTopic, InnerFirstLabel);
            RecordingSubscriber innerSecond = SubscribeTo(broker, log, InnerTopic, InnerSecondLabel);
            RecordingSubscriber innerThird = SubscribeTo(broker, log, InnerTopic, InnerThirdLabel);

            object? innerResult = null;
            long preventCode = long.MinValue;

            // The nested trigger, fired from inside a running subscriber - w_test_eventful.srw:L80.
            WhileHandling(outerSecond, () => innerResult = broker.Trigger(InnerTopic));

            // The veto, raised from inside the nested dispatch by its SECOND subscriber, so there
            // is a subscriber before it that must have run and one after it that must not have.
            WhileHandling(innerSecond, () => preventCode = broker.Prevent(deep));

            object? outerResult = broker.Trigger(OuterTopic);

            // The MODE-INDEPENDENT outcomes, asserted here so every caller inherits them and so a
            // future edit cannot quietly drop a subscriber and leave the expected sequences
            // describing an arrangement that no longer exists.
            //
            // The two FIRST subscribers ran: a veto stops later delivery (w_test_eventful.srw:L236)
            // and does not undo what already ran, so it is not a cancellation of the dispatch.
            Assert.Equal(1, outerFirst.InvocationCount);
            Assert.Equal(1, outerSecond.InvocationCount);
            Assert.Equal(1, innerFirst.InvocationCount);
            Assert.Equal(1, innerSecond.InvocationCount);

            // The inner run's third subscriber never runs, in EITHER mode: :L822 tests only that the
            // state is non-zero and cannot tell PREVENT_ONCE from PREVENT_DEEP.
            Assert.Equal(0, innerThird.InvocationCount);

            // The outer run's third subscriber is the ONE mode-dependent outcome in the whole
            // arrangement, and it is the observable form of the absent `else` at :L958.
            Assert.Equal(deep ? 0 : 1, outerThird.InvocationCount);

            return new TwoLevelRun(broker, log, preventCode, innerResult, outerResult);
        }

        /// <summary>
        /// The two prevent modes and the ordered label sequence each produces from the ONE nested
        /// arrangement. The rows differ by a single trailing label, and that label is the whole
        /// semantic difference between <see cref="VetoResult.PreventOnce"/> and
        /// <see cref="VetoResult.PreventDeep"/>.
        /// </summary>
        public static TheoryData<bool, VetoResult, string[]> NestedVetoModeRows =>
            new()
            {
                // ONCE. n_cst_eventful.sru:L1297 assigns PREVENT_ONCE for a false `deep`. The inner
                // level's unwind reaches :L956, finds the state IS PREVENT_ONCE, and clears it at
                // :L957 - so the enclosing loop's :L822 test sees zero on its next iteration and
                // THE THIRD OUTER SUBSCRIBER RUNS. The veto was consumed by the level that raised
                // it, which is exactly what "once" names.
                {
                    false,
                    VetoResult.PreventOnce,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        InnerFirstLabel,
                        InnerSecondLabel,
                        ThirdLabel
                    }
                },

                // DEEP. :L1295 assigns PREVENT_DEEP. The inner level's unwind reaches :L956, finds
                // the state is NOT PREVENT_ONCE and the depth is NOT yet zero - it was restored to
                // 1 on the line above at :L952 - so NEITHER branch fires and the state is left
                // standing. The enclosing loop's :L822 test then sees a non-zero state and leaves
                // the loop, so THE THIRD OUTER SUBSCRIBER DOES NOT RUN.
                //
                // The missing trailing label is the observable form of the absent `else` at :L958.
                {
                    true,
                    VetoResult.PreventDeep,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        InnerFirstLabel,
                        InnerSecondLabel
                    }
                }
            };

        /// <summary>
        /// A veto raised inside a NESTED dispatch stops the enclosing dispatch when it is deep and
        /// does not when it is once - the divergence the tri-valued veto exists to carry.
        /// </summary>
        /// <param name="deep">The prevent mode under test.</param>
        /// <param name="expectedState">
        /// The <see cref="VetoResult"/> the mode corresponds to, asserted so the row names the
        /// state rather than only the boolean that selects it.
        /// </param>
        /// <param name="expectedLabels">The ordered label sequence the mode must produce.</param>
        /// <remarks>
        /// <para>
        /// <b>This is the test a boolean veto fails.</b> Collapse the state to a flag and both rows
        /// behave as the once row, because a flag cannot distinguish "clear me on the way out" from
        /// "leave me set on the way out" - and the failure would appear ONLY in nested dispatches,
        /// which is the hardest place to notice it and the hardest place to attribute it. AAP
        /// 0.6.1.2 names that failure mode explicitly.
        /// </para>
        /// <para>
        /// Both rows share four assertions and differ in one, and the shared four are what make the
        /// difference attributable: in both modes the subscriber BEFORE the vetoer ran, the vetoer
        /// itself ran, the subscriber AFTER it within the same level did not, and the veto answered
        /// the legacy success code. Only the fate of the third OUTER subscriber differs.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(NestedVetoModeRows))]
        public void ADeepVetoRaisedInANestedDispatchAlsoStopsTheEnclosingOneAndAOnceVetoDoesNot(
            bool deep,
            VetoResult expectedState,
            string[] expectedLabels)
        {
            TwoLevelRun run = RunTwoLevelArrangement(deep);

            // The ordered sequence IS the assertion: it says who ran, in what order, and - by
            // absence - who did not. Ordinal, because a label is an identifier and not prose.
            Assert.Equal(expectedLabels, run.Log.Labels, StringComparer.Ordinal);

            // :L1299 - of_prevent answers RetCode.OK from inside a dispatch, whichever mode.
            Assert.Equal(RetCode.OK, run.PreventCode);

            // The mode-to-state mapping of :L1294-L1298, stated in the row and checked here so the
            // theory reads as a statement about PreventOnce and PreventDeep rather than about a
            // bare boolean. The enum is the port's name for _nPrevent's value; the numbers
            // themselves are pinned by VetoNumericValueRows below.
            Assert.Equal(deep ? VetoResult.PreventDeep : VetoResult.PreventOnce, expectedState);

            // The inner level always aborts, in both modes: the third inner subscriber is absent
            // from both expected sequences. This is :L822 treating the two states identically, and
            // it is the half of the mechanism the two modes SHARE.
            Assert.DoesNotContain(InnerThirdLabel, run.Log.Labels, StringComparer.Ordinal);

            // A veto is not an exception and not a cancellation: both triggers returned normally.
            // Nothing is thrown anywhere on this path - reaching this line at all is that
            // assertion, and the two nulls below record what a normal return carries.
            //
            // Both are null because Trigger answers the LAST INVOCATION'S RAW VALUE (:L973) and no
            // subscriber in this arrangement returns anything; the ACCUMULATED value is a different
            // quantity, reachable only from inside a dispatch, and is asserted separately below.
            Assert.Null(run.InnerResult);
            Assert.Null(run.OuterResult);
        }

        /// <summary>
        /// The two prevent modes produce DIFFERENT ordered label sequences from the identical
        /// arrangement, and the deep sequence is a strict prefix of the once sequence.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The difference asserted directly, as a difference.</b> The theory above checks each
        /// mode against its own expected sequence, which is necessary but leaves one loophole: if
        /// BOTH expected sequences were wrong in the same direction the theory would still pass.
        /// This test closes it by comparing the two runs against each other rather than against a
        /// literal, so it fails whenever the two modes become indistinguishable - no matter what
        /// they both become.
        /// </para>
        /// <para>
        /// <b>That indistinguishability is exactly what a boolean flattening would produce</b>, and
        /// it is why this assertion is phrased as an inequality. A boolean cannot carry the
        /// distinction the unwind at <c>n_cst_eventful.sru:L954-L958</c> draws, so under a
        /// flattened implementation these two sequences become equal and this line fails first.
        /// </para>
        /// <para>
        /// The prefix relation is asserted as well as the inequality, because it says WHICH way the
        /// two differ: the deep run is the once run truncated at the point the enclosing loop
        /// aborted, so nothing extra happened under the deep mode and nothing was reordered. An
        /// inequality alone would also be satisfied by two sequences that merely differed in order.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheTwoPreventModesProduceDifferentOrderedLabelSequences()
        {
            IReadOnlyList<string> onceLabels = RunTwoLevelArrangement(deep: false).Log.Labels;
            IReadOnlyList<string> deepLabels = RunTwoLevelArrangement(deep: true).Log.Labels;

            // THE assertion. If this ever passes trivially - because the two became equal - the
            // veto has been flattened and every claim AAP 0.6.1.2 makes about it is void.
            Assert.NotEqual(onceLabels, deepLabels, StringComparer.Ordinal);

            // The deep run is the once run truncated: same prefix, one label shorter, and the label
            // it lacks is the third subscriber of the ENCLOSING run.
            Assert.Equal(deepLabels.Count + 1, onceLabels.Count);
            Assert.Equal(deepLabels, onceLabels.Take(deepLabels.Count), StringComparer.Ordinal);
            Assert.Equal(ThirdLabel, onceLabels[^1], StringComparer.Ordinal);
        }

        // =========================================================================================
        //  2. THREE LEVELS - THE DEEP VETO KEEPS ABORTING OUTWARD
        //
        //  Two levels prove the state survives ONE unwind. Three prove it keeps surviving, which is
        //  a different claim: an implementation that cleared the state one level up would pass every
        //  two-level case and fail here. The once mode is carried alongside as the contrast, because
        //  the middle level is where the two modes visibly part company for a second time.
        // =========================================================================================

        /// <summary>
        /// The two prevent modes and the ordered label sequence each produces from the ONE
        /// three-level arrangement, where the veto is raised at the INNERMOST level.
        /// </summary>
        public static TheoryData<bool, string[]> ThreeLevelVetoModeRows =>
            new()
            {
                // ONCE, raised in the core run. Consumed by the core level's own unwind at :L957, so
                // the middle run resumes and finishes, and then the outer run resumes and finishes.
                // Both third subscribers run, and the order records the unwinding: the middle run's
                // tail comes back BEFORE the outer run's tail.
                {
                    false,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        MiddleFirstLabel,
                        MiddleSecondLabel,
                        CoreFirstLabel,
                        CoreSecondLabel,
                        MiddleThirdLabel,
                        ThirdLabel
                    }
                },

                // DEEP, raised in the core run. It survives the core unwind (depth back to 2, so
                // :L956 is false on both disjuncts), aborts the middle run at its next :L822 test,
                // survives the middle unwind too (depth back to 1, still non-zero), and aborts the
                // outer run. THREE runs stopped by ONE call, and both third subscribers are absent.
                {
                    true,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        MiddleFirstLabel,
                        MiddleSecondLabel,
                        CoreFirstLabel,
                        CoreSecondLabel
                    }
                }
            };

        /// <summary>
        /// A deep veto raised at the innermost of three nested dispatches aborts the middle and the
        /// outer chains too, while a once veto raised in the same place aborts only its own.
        /// </summary>
        /// <param name="deep">The prevent mode under test.</param>
        /// <param name="expectedLabels">The ordered label sequence the mode must produce.</param>
        /// <remarks>
        /// <para>
        /// The deep state's survival is not a one-level courtesy, it is a property of every unwind
        /// whose restored depth is still above zero (<c>n_cst_eventful.sru:L956</c>). Two levels
        /// cannot tell those two readings apart; three can, which is the only reason this
        /// arrangement exists as well as the two-level one.
        /// </para>
        /// <para>
        /// The middle level is the interesting one. Under the once mode its third subscriber runs,
        /// which proves the middle level was never aborted at all. Under the deep mode it does not,
        /// which proves the middle level's own <c>:L822</c> guard fired on state it did not itself
        /// set - the definition of "deep".
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(ThreeLevelVetoModeRows))]
        public void ADeepVetoAtTheInnermostLevelAbortsEveryEnclosingLevel(bool deep, string[] expectedLabels)
        {
            DispatchLog log = new();
            EventBroker broker = new();

            // Three runs of three, each subscribed in dispatch order. No ordering symbol is used:
            // every topic here carries the normal priority, and the broker appends at the tail of an
            // equal-priority run (n_cst_eventful.sru:L419), so subscription order IS dispatch order.
            SubscribeTo(broker, log, OuterTopic, FirstLabel);
            RecordingSubscriber outerSecond = SubscribeTo(broker, log, OuterTopic, SecondLabel);
            RecordingSubscriber outerThird = SubscribeTo(broker, log, OuterTopic, ThirdLabel);

            SubscribeTo(broker, log, MiddleTopic, MiddleFirstLabel);
            RecordingSubscriber middleSecond = SubscribeTo(broker, log, MiddleTopic, MiddleSecondLabel);
            RecordingSubscriber middleThird = SubscribeTo(broker, log, MiddleTopic, MiddleThirdLabel);

            SubscribeTo(broker, log, CoreTopic, CoreFirstLabel);
            RecordingSubscriber coreSecond = SubscribeTo(broker, log, CoreTopic, CoreSecondLabel);
            RecordingSubscriber coreThird = SubscribeTo(broker, log, CoreTopic, CoreThirdLabel);

            // outer -> middle -> core, each nested trigger fired from inside a running subscriber,
            // which is the oracle's shape at w_test_eventful.srw:L80 applied twice.
            WhileHandling(outerSecond, () => broker.Trigger(MiddleTopic));
            WhileHandling(middleSecond, () => broker.Trigger(CoreTopic));

            long preventCode = long.MinValue;
            WhileHandling(coreSecond, () => preventCode = broker.Prevent(deep));

            broker.Trigger(OuterTopic);

            Assert.Equal(expectedLabels, log.Labels, StringComparer.Ordinal);

            // :L1299 - the depth was 3 when the veto was raised, so it is well inside a dispatch.
            Assert.Equal(RetCode.OK, preventCode);

            // The innermost run always aborts, whichever mode: :L822 cannot tell them apart.
            Assert.Equal(0, coreThird.InvocationCount);

            // The two levels the deep mode reaches and the once mode does not, stated as counts as
            // well as by position in the sequence above. The middle level is the one that makes this
            // a three-level test rather than a longer two-level one.
            Assert.Equal(deep ? 0 : 1, middleThird.InvocationCount);
            Assert.Equal(deep ? 0 : 1, outerThird.InvocationCount);

            // Whatever survived the unwinds, nothing survives depth zero (:L956's second disjunct),
            // so the broker is usable again the moment the outermost trigger has returned.
            Assert.Equal(RetCode.FAILED, broker.Prevent());
        }

        // =========================================================================================
        //  3. THE ENCLOSING VETO IS RESTORED, NOT LOST - AND THE INNER LEVEL STILL RUNS
        //
        //  The branch at :L954-L955, which is the branch a port most easily loses because it looks
        //  redundant next to the clearing branch beside it. It is not redundant: it takes PRIORITY
        //  over the clearing branch, so an inner level can neither consume nor overwrite a veto the
        //  enclosing level was already holding.
        //
        //  A DOCUMENTED CORRECTION TO THE AUTHORING BRIEF, kept here rather than absorbed silently.
        //  The brief asked this arrangement to assert that "the inner dispatch is never entered at
        //  all". It IS entered, and its subscribers DO run. The reason is one line: :L813 zeroes the
        //  level's OWN prevent state on entry, unconditionally, so a veto held by an enclosing level
        //  cannot prevent an inner level from starting - it can only be laid back over the top of it
        //  at :L955 once that level unwinds. The source read and a measured run agree. AAP 0.7.3 C-C
        //  makes ws_objects/** the oracle and C-B forbids asserting a behaviour it does not have, so
        //  the arrangement is kept and the expectation is the oracle's.
        // =========================================================================================

        /// <summary>
        /// The two prevent modes, an enclosing veto raised in each, and the ordered label sequence
        /// each produces. The two sequences are DELIBERATELY IDENTICAL: the restore branch at
        /// <c>n_cst_eventful.sru:L954-L955</c> runs before the once-versus-deep discrimination at
        /// <c>:L956</c> and is therefore blind to the mode.
        /// </summary>
        public static TheoryData<bool, VetoResult, string[]> EnclosingVetoModeRows =>
            new()
            {
                // ONCE held by the OUTER level. Note what this row proves that no single-level case
                // can: a once veto is consumed by THE LEVEL THAT RAISED IT, and the inner level is
                // not that level - so the inner unwind restores it instead of consuming it, and the
                // outer run is still aborted afterwards.
                {
                    false,
                    VetoResult.PreventOnce,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        InnerFirstLabel,
                        InnerSecondLabel,
                        InnerThirdLabel
                    }
                },

                // DEEP held by the OUTER level. Identical outcome, by the same branch.
                {
                    true,
                    VetoResult.PreventDeep,
                    new[]
                    {
                        FirstLabel,
                        SecondLabel,
                        InnerFirstLabel,
                        InnerSecondLabel,
                        InnerThirdLabel
                    }
                }
            };

        /// <summary>
        /// A veto raised by an outer subscriber survives a nested dispatch triggered from that same
        /// handler - and that nested dispatch nevertheless runs every one of its own subscribers.
        /// </summary>
        /// <param name="deep">The prevent mode the outer subscriber raises.</param>
        /// <param name="expectedState">
        /// The state that mode puts the broker into, asserted so the row names it.
        /// </param>
        /// <param name="expectedLabels">The ordered label sequence, identical for both modes.</param>
        /// <remarks>
        /// <para>
        /// <b>Two independent behaviours in one arrangement, and both are easy to lose.</b> First,
        /// ALL THREE inner subscribers run: the inner level zeroed its own state on entry
        /// (<c>:L813</c>), so it starts clean no matter what the enclosing level is holding. An
        /// implementation that let the state leak inward would run none of them, because the loop
        /// would leave at its very first <c>:L822</c> test. Second, the third OUTER subscriber does
        /// not run: the inner unwind put the enclosing state back (<c>:L955</c>) instead of clearing
        /// it, so the outer loop's guard fires on the state its own subscriber set.
        /// </para>
        /// <para>
        /// <b>Why the two rows are identical, stated so the duplication is not read as an
        /// oversight.</b> The restore branch is tested FIRST and returns; the once-versus-deep
        /// discrimination at <c>:L956</c> is only reached when the enclosing state was zero. So when
        /// an enclosing veto exists the mode is irrelevant, and asserting that equality is itself a
        /// finding - it is the boundary of the divergence asserted in section 1.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(EnclosingVetoModeRows))]
        public void AnEnclosingVetoSurvivesANestedDispatchWhichStillRunsEveryOneOfItsSubscribers(
            bool deep,
            VetoResult expectedState,
            string[] expectedLabels)
        {
            DispatchLog log = new();
            EventBroker broker = new();

            SubscribeTo(broker, log, OuterTopic, FirstLabel);
            RecordingSubscriber outerSecond = SubscribeTo(broker, log, OuterTopic, SecondLabel);
            RecordingSubscriber outerThird = SubscribeTo(broker, log, OuterTopic, ThirdLabel);

            RecordingSubscriber innerFirst = SubscribeTo(broker, log, InnerTopic, InnerFirstLabel);
            RecordingSubscriber innerSecond = SubscribeTo(broker, log, InnerTopic, InnerSecondLabel);
            RecordingSubscriber innerThird = SubscribeTo(broker, log, InnerTopic, InnerThirdLabel);

            long outerPreventCode = long.MinValue;
            EventBroker? ambientInsideInnerDispatch = null;

            // VETO FIRST, THEN NEST - the order the brief asked for, preserved exactly.
            WhileHandling(
                outerSecond,
                () =>
                {
                    outerPreventCode = broker.Prevent(deep);
                    broker.Trigger(InnerTopic);
                });

            // Read from inside the nested dispatch. The ambient broker is the port of the runtime
            // global the oracle's own handler reads (w_test_eventful.srw:L69, from :L846-L847), and
            // it is non-null only while a dispatch is on the stack - so capturing it here is
            // independent evidence that the inner level was genuinely entered rather than
            // short-circuited before its loop. It is READ and not written, so unlike a veto probe it
            // cannot itself alter the sequence being measured.
            innerFirst.SetInterceptor(
                RecordingHandlerNames.NoArguments,
                (ambient, _) => ambientInsideInnerDispatch = ambient);

            broker.Trigger(OuterTopic);

            Assert.Equal(expectedLabels, log.Labels, StringComparer.Ordinal);
            Assert.Equal(RetCode.OK, outerPreventCode);
            Assert.Equal(deep ? VetoResult.PreventDeep : VetoResult.PreventOnce, expectedState);

            // BEHAVIOUR ONE - the inner level started clean (:L813), so every one of its three
            // subscribers ran even though the enclosing level was holding a veto the whole time.
            Assert.Equal(1, innerFirst.InvocationCount);
            Assert.Equal(1, innerSecond.InvocationCount);
            Assert.Equal(1, innerThird.InvocationCount);
            Assert.Same(broker, ambientInsideInnerDispatch);

            // BEHAVIOUR TWO - the enclosing veto was put back rather than cleared (:L955), so the
            // third OUTER subscriber never ran even though a whole nested dispatch completed between
            // the veto and the outer loop's next guard.
            Assert.Equal(0, outerThird.InvocationCount);

            // And nothing survives depth zero, whichever branch ran on the way out (:L956).
            Assert.Equal(RetCode.FAILED, broker.Prevent());
        }

        /// <summary>
        /// The restore branch takes PRIORITY over the once-consumption branch: an enclosing deep
        /// veto survives an inner dispatch in which a once veto was also raised.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The sharpest available test of branch ORDER at
        /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L954-L957</c>, and it is
        /// built as a controlled comparison so the outcome cannot be explained by anything else.
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///   <b>Control</b> - no enclosing veto, inner once veto: the enclosing state is zero, so
        ///   <c>:L954</c> is false, <c>:L956</c> fires on its first disjunct, the state is cleared,
        ///   and the third outer subscriber RUNS.
        ///   </description></item>
        ///   <item><description>
        ///   <b>Subject</b> - enclosing DEEP veto, the SAME inner once veto: <c>:L954</c> is now
        ///   true, so the deep state is restored and <c>:L956</c> is never evaluated. The third
        ///   outer subscriber DOES NOT run.
        ///   </description></item>
        /// </list>
        /// <para>
        /// The inner veto is identical in both, so the difference isolates the branch order. An
        /// implementation that evaluated the clearing branch first - or that used an
        /// <c>if</c>/<c>if</c> pair instead of <c>if</c>/<c>elseif</c> - would clear the enclosing
        /// deep veto on the strength of the inner once veto and both runs would end the same way.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheRestoreBranchTakesPriorityOverTheOnceConsumptionBranch()
        {
            // CONTROL: the once row of section 1's arrangement, whose third outer subscriber runs.
            Assert.Contains(ThirdLabel, RunTwoLevelArrangement(deep: false).Log.Labels, StringComparer.Ordinal);

            // SUBJECT: the same inner once veto, with a deep veto already held by the outer level.
            DispatchLog log = new();
            EventBroker broker = new();

            SubscribeTo(broker, log, OuterTopic, FirstLabel);
            RecordingSubscriber outerSecond = SubscribeTo(broker, log, OuterTopic, SecondLabel);
            SubscribeTo(broker, log, OuterTopic, ThirdLabel);

            SubscribeTo(broker, log, InnerTopic, InnerFirstLabel);
            RecordingSubscriber innerSecond = SubscribeTo(broker, log, InnerTopic, InnerSecondLabel);
            SubscribeTo(broker, log, InnerTopic, InnerThirdLabel);

            WhileHandling(
                outerSecond,
                () =>
                {
                    Assert.Equal(RetCode.OK, broker.Prevent(deep: true));
                    broker.Trigger(InnerTopic);
                });

            // The inner veto is the ONCE one - the only mode :L956's first disjunct recognises.
            WhileHandling(innerSecond, () => Assert.Equal(RetCode.OK, broker.Prevent(deep: false)));

            broker.Trigger(OuterTopic);

            // The inner run aborted at its own second subscriber, as a once veto does...
            Assert.Equal(
                new[] { FirstLabel, SecondLabel, InnerFirstLabel, InnerSecondLabel },
                log.Labels,
                StringComparer.Ordinal);

            // ...and the enclosing deep veto was NOT consumed by it, so the outer run aborted too.
            Assert.DoesNotContain(ThirdLabel, log.Labels, StringComparer.Ordinal);
            Assert.Equal(RetCode.FAILED, broker.Prevent());
        }

        // =========================================================================================
        //  4. THE SINGLE-LEVEL SCAFFOLDING - WHAT THE TWO MODES SHARE
        //
        //  These cases are deliberately the WEAK ones: a boolean veto passes every one of them. They
        //  are here because they are what makes the nested divergence above attributable. If the two
        //  modes already behaved differently at one level, section 1's result would prove nothing
        //  about the unwind - it could be explained by the modes simply aborting differently. These
        //  cases close that alternative explanation by measuring the two modes as identical here.
        // =========================================================================================

        /// <summary>
        /// Runs a single-level arrangement of three subscribers whose SECOND one vetoes in the mode
        /// given, on a fresh broker, and returns the ordered label sequence.
        /// </summary>
        /// <param name="deep">The prevent mode.</param>
        /// <returns>The ordered label sequence the dispatch produced.</returns>
        /// <remarks>
        /// A subscriber before the vetoer and one after it are both required. The one BEFORE proves a
        /// veto is not a cancellation of the whole dispatch - the oracle documents it as stopping
        /// "every later subscription" (<c>w_test_eventful.srw:L236</c>), not as undoing what already
        /// ran. The one AFTER is the subscriber whose absence is the veto's only visible effect.
        /// </remarks>
        private static IReadOnlyList<string> RunSingleLevelArrangement(bool deep)
        {
            DispatchLog log = new();
            EventBroker broker = new();

            SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber second = SubscribeTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber third = SubscribeTo(broker, log, SoleTopic, ThirdLabel);

            // :L1299 - asserted at the call site, so a mode that stopped being permitted inside a
            // dispatch would fail here rather than surface later as a puzzling label sequence.
            WhileHandling(second, () => Assert.Equal(RetCode.OK, broker.Prevent(deep)));

            // A veto returns the dispatch normally. Nothing is thrown, and the value is the last
            // invocation's raw value (:L973), which is null because no subscriber here returns one.
            Assert.Null(broker.Trigger(SoleTopic));

            // The subscriber after the vetoer was never invoked, not merely absent from the log.
            Assert.Equal(0, third.InvocationCount);

            // :L1293 - the depth is back to zero, so the broker is out of dispatch.
            Assert.Equal(RetCode.FAILED, broker.Prevent());

            return log.Labels;
        }

        /// <summary>
        /// With nothing vetoing, every subscriber of a topic runs, in subscription order.
        /// </summary>
        /// <remarks>
        /// The baseline the other cases are read against, and it is the <see cref="VetoResult.Continue"/>
        /// state observed behaviourally: <c>_nPrevent</c> is zeroed on entry to a dispatch
        /// (<c>n_cst_eventful.sru:L813</c>) and nothing writes it, so the loop's guard at <c>:L822</c>
        /// never fires and delivery runs to the end of the matching run. Without this row a suite of
        /// veto tests could be passing because delivery was broken rather than because the veto works.
        /// </remarks>
        [Fact]
        public void WithNoVetoEverySubscriberRunsInSubscriptionOrder()
        {
            DispatchLog log = new();
            EventBroker broker = new();

            RecordingSubscriber first = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber second = SubscribeTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber third = SubscribeTo(broker, log, SoleTopic, ThirdLabel);

            Assert.Null(broker.Trigger(SoleTopic));

            Assert.Equal(
                new[] { FirstLabel, SecondLabel, ThirdLabel },
                log.Labels,
                StringComparer.Ordinal);

            Assert.Equal(1, first.InvocationCount);
            Assert.Equal(1, second.InvocationCount);
            Assert.Equal(1, third.InvocationCount);
        }

        /// <summary>
        /// The two prevent modes and the state each names. The expected label sequence is not a
        /// column because it is the SAME for both rows - which is the finding, not an omission.
        /// </summary>
        public static TheoryData<bool, VetoResult> SingleLevelVetoModeRows =>
            new()
            {
                // n_cst_eventful.sru:L1297 - a false `deep` assigns PREVENT_ONCE.
                { false, VetoResult.PreventOnce },

                // :L1295 - a true `deep` assigns PREVENT_DEEP.
                { true, VetoResult.PreventDeep }
            };

        /// <summary>
        /// At a single dispatch level either prevent mode stops delivery from the vetoer onwards, and
        /// leaves everything already delivered alone.
        /// </summary>
        /// <param name="deep">The prevent mode under test.</param>
        /// <param name="expectedState">The state that mode names.</param>
        /// <remarks>
        /// The guard at <c>n_cst_eventful.sru:L822</c> is <c>_nPrevent &lt;&gt; 0</c> - a test for
        /// "not <see cref="VetoResult.Continue"/>" and nothing more. Neither mode is privileged
        /// there, which is why one theory covers both and why the expected sequence is shared.
        /// </remarks>
        [Theory]
        [MemberData(nameof(SingleLevelVetoModeRows))]
        public void EitherPreventModeStopsTheRemainingSubscribersOfItsOwnLevel(
            bool deep,
            VetoResult expectedState)
        {
            Assert.Equal(deep ? VetoResult.PreventDeep : VetoResult.PreventOnce, expectedState);

            Assert.Equal(
                new[] { FirstLabel, SecondLabel },
                RunSingleLevelArrangement(deep),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// At a single dispatch level the two prevent modes are INDISTINGUISHABLE.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asserted directly, as an equality, because it is the premise section 1's divergence rests
        /// on. The pair of assertions reads: the modes behave identically here, and differently when
        /// nested - therefore the difference is a property of the UNWIND
        /// (<c>n_cst_eventful.sru:L954-L958</c>) and not of the abort (<c>:L822</c>).
        /// </para>
        /// <para>
        /// It is also the reason a boolean implementation is so easy to ship by accident: every
        /// obvious test of a veto is a single-level test, and every single-level test passes.
        /// </para>
        /// </remarks>
        [Fact]
        public void AtASingleLevelTheTwoPreventModesAreIndistinguishable()
        {
            Assert.Equal(
                RunSingleLevelArrangement(deep: false),
                RunSingleLevelArrangement(deep: true),
                StringComparer.Ordinal);
        }

        // =========================================================================================
        //  5. THE THREE NUMBERS
        //
        //  C-K (AAP 0.7.3): these values cross a service boundary. AAP 0.4.3 C-03 puts the DataWindow
        //  event chain on a bidirectional gRPC stream and AAP 0.6.1.2 requires that stream to carry
        //  this tri-valued veto, so a peer reads the NUMBER off the wire and never sees the C#
        //  identifier. Renumbering or collapsing the alphabet would therefore be a silent contract
        //  break at a remote boundary, which is why the numbers are pinned here as well as the
        //  behaviours above - and why they are referenced through VetoResult's members rather than
        //  spelled as bare literals in the assertions.
        //
        //  The legacy declares the two non-zero values as a pair of adjacent constants under a single
        //  comment, at n_cst_eventful.sru:L110-L112, and leaves the third as the implicit zero of the
        //  private field at :L85. All three are pinned below.
        // =========================================================================================

        /// <summary>
        /// Each veto state, the legacy numeric value it must carry, and the ported member name.
        /// </summary>
        /// <remarks>
        /// The legacy SCREAMING_SNAKE spellings are recorded in the row comments rather than as
        /// members of this class: the repository-root <c>.editorconfig</c> lowers the naming
        /// diagnostics for ten named implementation files and for no test file, and
        /// <c>TreatWarningsAsErrors</c> is true, so such a member would be a build error here
        /// (AAP 0.4.5.3).
        /// </remarks>
        public static TheoryData<VetoResult, int, string> VetoNumericValueRows =>
            new()
            {
                // 0 - no legacy constant. This is the implicit zero of `long _nPrevent`
                // [n_cst_eventful.sru:L85], written on entry to every dispatch level [:L813] and on
                // the clearing branch of the unwind [:L957]. The port names it so it can be asserted
                // and read in a recording; the value is untouched.
                { VetoResult.Continue, 0, nameof(VetoResult.Continue) },

                // 1 - legacy spelling PREVENT_ONCE [:L111], assigned by of_prevent for a false
                // `deep` [:L1297] and therefore also by the parameterless overload [:L1302].
                { VetoResult.PreventOnce, 1, nameof(VetoResult.PreventOnce) },

                // 2 - legacy spelling PREVENT_DEEP [:L112], assigned for a true `deep` [:L1295].
                { VetoResult.PreventDeep, 2, nameof(VetoResult.PreventDeep) }
            };

        /// <summary>
        /// Each veto state carries its legacy numeric value exactly, round-trips from that value, and
        /// keeps the ported member name.
        /// </summary>
        /// <param name="state">The state under test.</param>
        /// <param name="expectedValue">The legacy numeric value it must carry.</param>
        /// <param name="expectedName">The ported member name it must keep.</param>
        /// <remarks>
        /// The NAME is pinned as well as the value because the name is what appears in a log record
        /// and in a characterization recording rendered from the enum, while the VALUE is what
        /// appears on the wire. Both are observable artifacts, so both are fixed; AAP 0.4.5.3 draws
        /// exactly that distinction for the constant catalogues.
        /// </remarks>
        [Theory]
        [MemberData(nameof(VetoNumericValueRows))]
        public void EachVetoStateCarriesItsLegacyNumericValueAndItsPortedName(
            VetoResult state,
            int expectedValue,
            string expectedName)
        {
            Assert.Equal(expectedValue, (int)state);
            Assert.Equal(state, (VetoResult)expectedValue);
            Assert.Equal(expectedName, Enum.GetName(state));
            Assert.True(Enum.IsDefined(state));
        }

        /// <summary>
        /// The veto alphabet has exactly three states, they are 0, 1 and 2, the combination 3 is not
        /// one of them, and the underlying type is 32 bits wide.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Exactly three, so the type cannot quietly become a boolean or grow a fourth state.</b>
        /// A boolean-flattened port would have two; a port that added a "prevent and rethrow" state
        /// borrowed from the exception hook's alphabet (<c>n_cst_eventful.sru:L889-L895</c>) would
        /// have four. Both are failures, and the count catches both before any behavioural test runs.
        /// </para>
        /// <para>
        /// <b>3 is deliberately not a state.</b> <c>of_prevent</c> assigns one flag or the other
        /// through an if/else (<c>:L1294-L1298</c>) and never combines them, so the legacy cannot
        /// hold the bitwise union and neither can this enum. That is also why the type carries no
        /// <c>[Flags]</c> attribute, and asserting the absence of 3 is what makes that decision
        /// verifiable rather than merely documented.
        /// </para>
        /// <para>
        /// <b>32 bits, because the legacy field is a PowerBuilder <c>long</c></b> [<c>:L85</c>],
        /// which is 32 bits wide. AAP 0.4.5.2 maps that to <c>long</c> only where the legacy type is
        /// <c>longlong</c>; here the faithful width is the enum's default underlying type.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheVetoAlphabetHasExactlyThreeStatesAndIsNotComposable()
        {
            VetoResult[] states = Enum.GetValues<VetoResult>();

            Assert.Equal(3, states.Length);
            Assert.Equal(new[] { 0, 1, 2 }, states.Select(state => (int)state));

            // The bitwise union of the two flags. Reachable as a cast, never as a broker state.
            Assert.False(Enum.IsDefined((VetoResult)((int)VetoResult.PreventOnce | (int)VetoResult.PreventDeep)));

            Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(VetoResult)));
        }

        // =========================================================================================
        //  6. LIFECYCLE - WHERE THE STATE MUST NOT SURVIVE, AND WHAT IT MUST NOT DISTURB
        //
        //  Section 1 asserts the deep state's survival as correct. This section draws the boundary of
        //  that correctness. The state must stop existing at depth zero, must not be settable outside
        //  a dispatch, and must not touch the dispatch's accumulated return value on its way out.
        //  Each is a separate failure a port can ship while passing every case above.
        // =========================================================================================

        /// <summary>
        /// No veto outlives the OUTERMOST dispatch, whichever mode raised it and however deeply it
        /// was nested.
        /// </summary>
        /// <param name="deep">The prevent mode under test.</param>
        /// <param name="expectedState">The state that mode names.</param>
        /// <param name="expectedLabels">The ordered label sequence, asserted twice - see below.</param>
        /// <remarks>
        /// <para>
        /// The second disjunct of <c>n_cst_eventful.sru:L956</c>, <c>or _nDeep = 0</c>, and it is the
        /// counterweight to everything section 1 asserts. Read on its own, "a deep veto survives the
        /// unwind" describes a flag that is never cleared - and a flag that is never cleared would
        /// break the loop's first <c>:L822</c> test on every subsequent dispatch, silently disabling
        /// the broker for the remainder of the process. This is the ONLY assertion that catches that,
        /// and it catches it for both modes.
        /// </para>
        /// <para>
        /// <b>Pass two is measured to reproduce pass one exactly, subscribers and interceptors
        /// untouched.</b> That is a stronger statement than "something ran": it says the second
        /// dispatch behaved as though the first had never happened - the same subscribers ran in the
        /// same order and the same veto fired again in the same place. A leak would show as an empty
        /// pass two, because the loop would leave before delivering to anyone at all.
        /// </para>
        /// <para>
        /// The nested topic is then triggered on its own, and its first subscriber runs. That
        /// distinguishes a broker that has recovered from one that merely happens to reach its outer
        /// subscribers, and it does so on the topic whose subscriber raised the veto in the first
        /// place.
        /// </para>
        /// <para>
        /// <b>Which row of this theory actually discriminates, measured rather than assumed.</b> The
        /// clearing branch was deliberately disabled and the suite re-run, and the ONCE row failed
        /// while the DEEP row still passed. The reason is worth recording, because it is not obvious
        /// from reading <c>:L956</c>: a leaked veto cannot block a LATER top-level dispatch, because
        /// every dispatch level zeroes the field on entry (<c>:L813</c>) and hands the saved value
        /// back on the way out (<c>:L955</c>) - so a deep state left standing past depth zero is
        /// masked from every subsequent dispatch and is unobservable through the broker's public
        /// surface. The <c>or _nDeep = 0</c> disjunct is therefore load bearing for the once path and
        /// defensive for the deep path. The deep row is kept regardless: it costs nothing, it is a
        /// regression guard if the entry-zeroing is ever weakened, and asserting the same recovery
        /// for both modes is the honest statement of the requirement. What is NOT done is inventing
        /// a hook to observe the private field - that would test the port's internals instead of the
        /// oracle's behaviour.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(NestedVetoModeRows))]
        public void NoVetoOutlivesTheOutermostDispatch(
            bool deep,
            VetoResult expectedState,
            string[] expectedLabels)
        {
            TwoLevelRun run = RunTwoLevelArrangement(deep);

            Assert.Equal(expectedLabels, run.Log.Labels, StringComparer.Ordinal);
            Assert.Equal(deep ? VetoResult.PreventDeep : VetoResult.PreventOnce, expectedState);

            // :L1293 - the depth guard answers the failure code, which is only possible if the depth
            // really is back to zero. Both overloads, because both consult the same guard.
            Assert.Equal(RetCode.FAILED, run.Broker.Prevent());
            Assert.Equal(RetCode.FAILED, run.Broker.Prevent(deep: true));

            // PASS TWO on the SAME broker, with the SAME subscribers still attached.
            run.Log.Clear();
            Assert.Null(run.Broker.Trigger(OuterTopic));
            Assert.Equal(expectedLabels, run.Log.Labels, StringComparer.Ordinal);

            // And the nested topic dispatches on its own account too. Its first subscriber running is
            // the assertion; its third is absent because the veto fires again, exactly as before.
            run.Log.Clear();
            Assert.Null(run.Broker.Trigger(InnerTopic));
            Assert.Equal(
                new[] { InnerFirstLabel, InnerSecondLabel },
                run.Log.Labels,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Either <c>Prevent</c> overload called with no dispatch in flight answers the legacy failure
        /// code, throws nothing, and arms nothing for the next dispatch.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>of_prevent</c> tests the dispatch depth before it does anything else and returns
        /// <c>RetCode.FAILED</c> when there is no dispatch to prevent
        /// (<c>n_cst_eventful.sru:L1293</c>). Three properties of that one line matter, and all three
        /// are asserted:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   <b>It reports rather than throws.</b> A misplaced call is a returned code, so it cannot
        ///   take down a caller that was only being defensive.
        ///   </description></item>
        ///   <item><description>
        ///   <b>It arms nothing.</b> The state is left untouched, so an unrelated LATER dispatch is
        ///   unaffected - which the three subscribers below all running demonstrates. Assigning
        ///   before the guard would suppress the next event to arrive, and the returned code would
        ///   look identical.
        ///   </description></item>
        ///   <item><description>
        ///   <b>The guard is on the DEPTH, not on the caller.</b> The same broker answers OK from
        ///   inside a dispatch - measured in every arrangement above - and the failure code from
        ///   outside one, which is asserted here on one instance so the difference cannot be
        ///   attributed to the broker.
        ///   </description></item>
        /// </list>
        /// <para>
        /// The parameterless overload is included because it is a distinct entry point:
        /// <c>:L1302</c> delegates to the two-argument form with <c>deep</c> false, so it reaches the
        /// same guard by a different route and a port that forgot to delegate would answer OK here.
        /// </para>
        /// </remarks>
        [Fact]
        public void PreventOutsideAnyDispatchAnswersTheLegacyFailureCodeAndArmsNothing()
        {
            DispatchLog log = new();
            EventBroker broker = new();

            // No dispatch is on the stack, which the ambient broker - the port of the runtime global
            // an oracle handler reads (:L846-L847) - independently confirms.
            Assert.Null(EventBroker.Current);

            // :L1302 then :L1293, and :L1293 directly for both explicit modes.
            Assert.Equal(RetCode.FAILED, broker.Prevent());
            Assert.Equal(RetCode.FAILED, broker.Prevent(deep: false));
            Assert.Equal(RetCode.FAILED, broker.Prevent(deep: true));

            RecordingSubscriber first = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber second = SubscribeTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber third = SubscribeTo(broker, log, SoleTopic, ThirdLabel);

            // Refused, and therefore also NOT armed: the dispatch that follows is untouched.
            Assert.Equal(RetCode.FAILED, broker.Prevent(deep: true));

            Assert.Null(broker.Trigger(SoleTopic));

            Assert.Equal(
                new[] { FirstLabel, SecondLabel, ThirdLabel },
                log.Labels,
                StringComparer.Ordinal);
            Assert.Equal(1, first.InvocationCount);
            Assert.Equal(1, second.InvocationCount);
            Assert.Equal(1, third.InvocationCount);

            // Still refused afterwards, so nothing about the dispatch left the guard open.
            Assert.Equal(RetCode.FAILED, broker.Prevent());
            Assert.Null(EventBroker.Current);
        }

        /// <summary>
        /// Whether the dispatch is vetoed or runs to completion, and the ordered label sequence each
        /// produces from the return-value arrangement.
        /// </summary>
        public static TheoryData<bool, string[]> VetoAndReturnValueRows =>
            new()
            {
                // THE CONTROL. Nothing vetoes, so all three subscribers run and the third's presence
                // proves it was eligible - which is what makes its absence in the row below
                // attributable to the veto rather than to the capture filter at :L831-L833.
                { false, new[] { FirstLabel, SecondLabel, ThirdLabel } },

                // THE SUBJECT. The second subscriber vetoes after reading the accumulated value.
                { true, new[] { FirstLabel, SecondLabel } }
            };

        /// <summary>
        /// A veto leaves the dispatch's accumulated return value and handled state exactly as the
        /// subscribers before it left them.
        /// </summary>
        /// <param name="vetoes">Whether the second subscriber vetoes.</param>
        /// <param name="expectedLabels">The ordered label sequence to expect.</param>
        /// <remarks>
        /// <para>
        /// The veto and the accumulated value are independent pieces of per-dispatch state - the
        /// prevent field at <c>n_cst_eventful.sru:L85</c> and the return value at <c>:L81</c> - and
        /// the unwind touches them on adjacent lines, <c>:L951</c> and <c>:L954-L958</c>. A port that
        /// reset the value while clearing the veto, or that treated a veto as "not handled", would
        /// still pass every ordering assertion in this file, so the two are measured together here.
        /// </para>
        /// <para>
        /// <b>Read from INSIDE the dispatch, because that is the only place the accumulated value
        /// exists.</b> Every level saves it on entry and restores it on unwind (<c>:L815-L816</c> and
        /// <c>:L951</c>), so a caller reading it after <c>Trigger</c> has returned sees whatever was
        /// there beforehand - which is why the post-dispatch assertion below expects the handled state
        /// to be false rather than true. That is the oracle's scoping, reproduced, and not a lost
        /// value.
        /// </para>
        /// <para>
        /// <b>Why the later subscribers claim the capture-everything symbol.</b> The first subscriber
        /// returns a value, which latches the dispatch as handled (<c>:L910-L924</c>), and from that
        /// moment the capture filter at <c>:L831-L833</c> skips every unhandled-only subscriber. The
        /// second and third therefore have to capture everything (<c>w_test_eventful.srw:L229</c>) or
        /// they would be skipped by the filter and the test would credit the veto for it.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(VetoAndReturnValueRows))]
        public void AVetoDoesNotDisturbTheAccumulatedReturnValue(bool vetoes, string[] expectedLabels)
        {
            DispatchLog log = new();
            EventBroker broker = new();

            RecordingSubscriber first = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber second = SubscribeCapturingEverythingTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber third = SubscribeCapturingEverythingTo(broker, log, SoleTopic, ThirdLabel);

            // The contribution whose survival is under test. A non-null return is what the oracle
            // counts as "handled" (:L909), and no default return value is registered, so this value
            // latches the handled state on its first comparison (:L912).
            first.SetReturnValue(RecordingHandlerNames.NoArguments, HandledReturnValue);

            object? valueSeenAfterTheVeto = null;
            bool processedSeenAfterTheVeto = false;

            WhileHandling(
                second,
                () =>
                {
                    if (vetoes)
                    {
                        Assert.Equal(RetCode.OK, broker.Prevent(deep: false));
                    }

                    // Read AFTER the veto, deliberately: the question is whether raising a veto
                    // disturbs the accumulated value, so the observation has to follow it.
                    valueSeenAfterTheVeto = broker.GetReturnValue();
                    processedSeenAfterTheVeto = broker.IsProcessed();
                });

            object? triggerResult = broker.Trigger(SoleTopic);

            Assert.Equal(expectedLabels, log.Labels, StringComparer.Ordinal);
            Assert.Equal(1, first.InvocationCount);
            Assert.Equal(1, second.InvocationCount);
            Assert.Equal(vetoes ? 0 : 1, third.InvocationCount);

            // THE ASSERTION. The first subscriber's contribution is intact, and the dispatch still
            // reports itself handled, in the vetoed row exactly as in the control row (:L922-L924).
            Assert.Equal(HandledReturnValue, valueSeenAfterTheVeto);
            Assert.True(processedSeenAfterTheVeto);

            // Trigger answers the LAST INVOCATION'S RAW VALUE (:L973), which is a different quantity
            // from the accumulated one and is null here because no subscriber after the first returns
            // anything. Asserted so the two are not confused: the accumulated value is 42 at the same
            // moment this is null.
            Assert.Null(triggerResult);

            // And the accumulated value is scoped to the dispatch, so it is gone once the dispatch
            // has unwound (:L951) - in both rows, veto or no veto.
            Assert.Null(broker.GetReturnValue());
            Assert.False(broker.IsProcessed());
        }
    }
}
