// ==============================================================================================
// ExceptionCaptureTests.cs
// A THROWING SUBSCRIBER IS A DECISION POINT, NOT AN ACCIDENT: the broker decorates, asks, and
// then swallows, continues, or rethrows - and the answer it gets decides all three.
// ==============================================================================================
//
// WHAT THIS FILE IS
// The dedicated suite for the per-subscriber exception path of the event broker. Three properties
// are under test and each one is a place a plausible port goes wrong:
//
//   1. ROUTING. A handler that throws reaches the exception hook exactly once, with the event
//      name and the very instance it threw, positioned in the dispatch immediately after that
//      subscriber's own invocation and before the triggered hook.
//   2. THE FAILURE LATCH. One boolean decides whether a captured exception is prefixed with its
//      bracketed type name. It is set on the first capture, CLEARED by a continue outcome, and
//      otherwise cleared only once the dispatch depth returns to zero - so a second failure inside
//      one top-level dispatch chain is deliberately NOT re-decorated.
//   3. CONTINUATION. The hook's answer selects the outcome. A prevent answer SWALLOWS the
//      exception. A continue answer swallows it AND clears the latch. Anything else rethrows, and
//      only then, and only at the outermost depth, does the broker's own class name go on the
//      front.
//
// THE MECHANISM, TRACED TO THE ORACLE. Every locator below points into
//     ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
// and was read off the file on disk rather than copied forward. The per-subscriber catch block is
// :L867-L902 and it runs in this order:
//
//     868: sException = ex.text                       read whatever text is CURRENTLY carried
//     871: if Not _bHasException then                 THE LATCH, and the first-failure gate
//     872:     _bHasException = true
//     873:     if ClassName(ex) = "assertionfailed"    the one special-cased shape
//     876:         sException = f.#Info + "~nStackTrace:~n" + f.#StackTraceInfo
//     877:     else
//     878:         sException = "[" + ClassName(ex) + "]~n" + sException
//     881: sException = _of_AlignString("Exception: ~n",sException)
//     883: ex.text = "Subscribe: " + name + "~n" +     the FOUR LABELLED LINES, byte for byte
//     884:           "Target: " + sObjClsChain + "~n" +
//     885:           "Event: " + sEvtName + "~n" +
//     886:           "Exception: ~n" + sException
//     888: if _bSubclassing then                      THE HOOK, gated on the subclassing flag
//     889:     choose case Event OnException(name,ex)
//     890:         case 1 //Prevent
//     891:             exit                            SWALLOWED - the loop is left, nothing throws
//     892:         case 2 //Continue
//     893:             _bHasException = false          THE LATCH IS CLEARED
//     894:             continue                        the next subscriber runs
//     899: if nDeep = 0 then                           OUTERMOST DEPTH ONLY
//     900:     ex.text = sThisClsName + "~n" + ex.text
//     902: throw ex
//
// and the latch's lifetime closes in the dispatch's outer finally:
//
//     959: if _nDeep = 0 then
//     960:     _bHasException = false
//
// so during a NESTED dispatch the latch survives the inner level's unwind. That is the whole
// reason the first-failure gate at :L871 is observable at all, and it is what section 5 measures.
//
// A NOTE ON THE LOCATORS. This file's own requirements quote the same ranges a line or two out -
// the catch block as :L866-L899, the latch clear as :L967-L969, the lower-casing as :L337, the
// triggered hook as :L948-L952. Every number used here was verified against the oracle on disk and
// agrees with the implementation's own comments in EventBroker.cs, so the verified numbers are the
// ones cited. Nothing behavioural turns on the difference; it is recorded so a reader who checks
// one against the other does not conclude that one of them describes different code.
//
// THREE ALPHABETS THAT SHARE THE NUMERALS 1 AND 2, AND ARE NOT INTERCHANGEABLE.
//   * THIS one - the exception hook's answer. 1 swallows, 2 swallows and clears the latch,
//     anything else rethrows. Published as EventBroker.ExceptionResultPrevent and
//     ExceptionResultContinue, and it is a `long` because the legacy event returns a long.
//   * THE DISPATCH VETO - VetoResult, whose PreventOnce is 1 and PreventDeep is 2 [:L110-L112].
//     Its 2 means "prevent the whole nested chain"; this file's 2 means very nearly the opposite,
//     "keep going". VetoSemanticsTests owns it.
//   * THE RETURN-CODE PREVENTION - RetCode.PREVENT, which is what the prepare and triggering hooks
//     answer to abort [:L840, :L609].
// Section 3's last test asserts the separation directly, and no assertion anywhere in this file
// states one alphabet in terms of another.
//
// WHY A DERIVED BROKER IS REQUIRED, AND WHY A PLAIN ONE IS THE CONTROL. The constructor computes
// the subclassing flag once [:L1324, `_bSubclassing = (_sThisClsName <> "n_cst_eventful")`] and
// every hook site is gated on it, including :L888. A plain EventBroker therefore fires no hook at
// all: its captured exception is still decorated, but no hook decision is taken and the exception
// always propagates. TestEventBroker exists for that reason, and section 2 runs one identical
// arrangement through both types - the difference between the two runs is the gate.
//
// THE ONE REAL SUBCLASS, for context on what the hook is for. n_cst_threading_eventful overrides
// it at ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L79-L88: it signals its
// cancellation and exception handles, then answers 1 after surfacing the error in a dialog [:L84]
// or 0 to let the exception propagate [:L87]. IT NEVER ANSWERS 2. The continue arm is therefore
// unreachable from any shipping subclass and is reachable only through a configured double, which
// is precisely why TestEventBroker.ExceptionResult is settable. (This file's requirements state
// that the real subclass does not override the hook; it does, at the locator above. Recorded
// rather than accommodated, because the correction strengthens the point: the outcome that matters
// most here is the one the oracle itself can never produce.)
//
// ------------------------------------------------------------------------------------------------
// GOVERNING CONSTRAINTS (AAP 0.7.3). `review_rules` reports NO USER RULES PROVIDED, so no user
// rule governs this file and none is invented; the constraints below are the plan's own, and
// enterprise-standard practice applies in the rules' place.
// ------------------------------------------------------------------------------------------------
//
// C-B - NO BEHAVIOUR IMPROVEMENT, AND THE OUTCOME IS ASSERTED AS *CORRECT*. Every expectation
//     here pins legacy semantics and names its locator in a comment beside it. Three assertions in
//     particular assert behaviour that a reader might mistake for a defect, so each says so:
//       - a PREVENT answer SWALLOWS the exception [:L890-L891]. The caller of Trigger sees a normal
//         return from a dispatch in which a handler failed. That is the legacy outcome and it is
//         asserted as correct, not worked around.
//       - a CONTINUE answer CLEARS the latch [:L893], so the very next failure is decorated as if
//         it were the first. Section 3 proves it by making a second subscriber throw as well.
//       - the decoration does NOT replace Exception.Message and does NOT wrap the exception. See
//         the carrier note below; section 1 asserts what the port actually does.
//
// C-D - NO DEFERRED CAPABILITY APPEARS. The legacy catch block's own cleanup path destroys or
//     releases an n_scriptinvoker [:L904, :L907], a type belonging to the deferred ScriptBridge
//     service. The port drops it and this file names no invoker, no substitute for one, and no
//     other deferred capability.
//
// C-H - NULLABLE AND WARNINGS AS ERRORS. Compiles warning-clean with no suppression, no #pragma
//     and no NoWarn. The null-forgiving operator appears nowhere: GetDispatchExceptionText returns
//     a nullable string by design - null means "this exception never passed through a dispatch" -
//     and that is a real question, so it is answered with Assert.NotNull and a local non-nullable
//     binding rather than silenced with `!`.
//
// C-K - THE SUBSTITUTION, NAMED. Two boundary decisions in the implementation are visible here and
//     both are asserted through their effect rather than through a type this project cannot see:
//       1. THE ASSERTION SHAPE. The legacy tests `ClassName(ex) = "assertionfailed"` and reads
//          `#Info` and `#StackTraceInfo` off it [:L873-L876]. `assertionfailed.sru` ports to
//          shared/PowerFramework.Shared.Diagnostics/AssertionFailure.cs, NOT to Kernel, and this
//          test project holds exactly ONE ProjectReference - to the library under test - so it
//          cannot and must not reach a real assertion type. The implementation anticipated that
//          with EventBroker.IAssertionDetail, a two-string contract any exception may implement.
//          Section 4 therefore supplies a local carrier implementing that interface and asserts
//          THE DECORATION - info, then the StackTrace label, then the frames - never a diagnostics
//          type. No second project reference is added, and none is needed.
//       2. THE TEXT CARRIER. PowerScript's `throwable.text` is assignable and the oracle overwrites
//          it outright [:L883, :L900]. .NET's Exception.Message is not settable, so the port
//          records the block on Exception.Data under EventBroker.DispatchExceptionTextKey and
//          rethrows THE SAME INSTANCE, unwrapped, with a bare `throw`. Section 1 pins all three
//          halves of that: the block is readable through GetDispatchExceptionText, Message is
//          untouched, and the instance that escapes is reference-equal to the one thrown.
//
// ------------------------------------------------------------------------------------------------
// HOW THE LATCH IS OBSERVED, since it has no accessor
// ------------------------------------------------------------------------------------------------
// `_bHasException` is private in the oracle [:L84] and private in the port, and neither publishes
// it. It is not observed here by reflection or by a widened accessor. It is observed through its
// ONE observable consequence: whether the NEXT capture in the same chain is prefixed with its
// bracketed type name [:L878]. That is exactly as much of the latch as the legacy ever exposed, so
// asserting through it is the faithful measurement rather than a compromise - and it has the
// property that matters, because it fails whenever the latch's lifetime changes:
//
//     latch was CLEAR at capture   ->  detail starts "[TypeName]" + newline
//     latch was SET at capture     ->  detail carries no bracketed type name at all
//
// THE KEY INSIGHT THIS SUITE IS BUILT AROUND. A continue answer does TWO things: it advances the
// chain and it clears the latch. A port that only advances passes every naive continue test and
// fails exactly one assertion - "the second exception is decorated as a first failure". That
// assertion is authored deliberately in section 3 and is the reason the arrangement there has a
// third subscriber that also throws.
//
// PROHIBITIONS OBSERVED. No timing, no clock, no Guid, no randomness, no thread, no file, no
// network and no database. No full exception message is asserted as one opaque literal: every
// labelled component is asserted separately with ordinal comparison, so a change to one label
// cannot mask a change to another. Assert.Throws is used only where the legacy throws, and a
// normal return is asserted only where the legacy swallows. No SCREAMING_SNAKE member is declared:
// the root .editorconfig scopes its CA1707 and IDE1006 suppressions to seven named implementation
// files and no test file, so such a member would be a build error under TreatWarningsAsErrors.
// Every test builds a fresh broker, fresh subscribers and a fresh log - hygiene, and NOT a
// substitute for section 2's assertion that the broker itself clears the latch at depth zero.
// Queue semantics are not re-asserted here: section 5 uses Post only to show that a drained
// posted dispatch routes to the hook identically, and PostQueueTests owns the queue itself.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Linq;

using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Shared.Eventful.Tests
{
    /// <summary>
    /// Behavioural tests for the broker's per-subscriber exception capture: that a throwing handler
    /// routes to the exception hook, that the failure latch is set, cleared and scoped exactly as
    /// the oracle scopes it, that the hook's three-valued answer selects swallow, continue or
    /// rethrow, and that the four-line diagnostic decoration is produced byte for byte. Ported from
    /// the catch block of <c>_of_trigger</c> in
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru</c>.
    /// </summary>
    public class ExceptionCaptureTests
    {
        // =========================================================================================
        //  TOPICS AND LABELS
        //  Constants so that an expected ordered sequence and the subscription that produces it
        //  cannot drift apart. Every name is lower-case ASCII with no leading symbol run, so each
        //  subscription lands at the normal priority and dispatch order is subscription order - the
        //  broker appends at the tail of an equal-priority run (n_cst_eventful.sru:L419). The topic
        //  grammar has its own suite; nothing here depends on it beyond that.
        // =========================================================================================

        /// <summary>The single topic used by every single-level arrangement.</summary>
        private const string SoleTopic = "alpha";

        /// <summary>The enclosing topic of the nested arrangements.</summary>
        private const string OuterTopic = "outer";

        /// <summary>The topic dispatched from inside an outer subscriber.</summary>
        private const string InnerTopic = "inner";

        /// <summary>
        /// The topic dispatched from inside the exception hook itself, by the one arrangement that
        /// needs a capture to happen WHILE the hook is running.
        /// </summary>
        private const string HookTopic = "beta";

        /// <summary>The first subscriber of a single-level arrangement.</summary>
        private const string FirstLabel = "first";

        /// <summary>The second subscriber of a single-level arrangement - the one that throws.</summary>
        private const string SecondLabel = "second";

        /// <summary>
        /// The third subscriber of a single-level arrangement: the one whose fate distinguishes a
        /// prevent answer from a continue answer.
        /// </summary>
        private const string ThirdLabel = "third";

        /// <summary>The outer subscriber of a nested arrangement.</summary>
        private const string OuterLabel = "outer-subscriber";

        /// <summary>The nested arrangement's inner subscriber - the one that throws.</summary>
        private const string InnerLabel = "inner-subscriber";

        /// <summary>The subscriber reached only from inside the exception hook.</summary>
        private const string HookLabel = "hook-subscriber";

        /// <summary>The label the derived broker writes into every hook row it records.</summary>
        private const string BrokerLabel = "broker-under-test";

        // =========================================================================================
        //  THE DECORATION'S LITERALS
        //  Written out here, character for character, rather than read from the library. A test that
        //  takes its expectation from the production constant it is checking proves only that the
        //  constant equals itself; these are pinned independently against the oracle at
        //  n_cst_eventful.sru:L883-L886 so the assertion is a statement about the ORACLE.
        //
        //  Note the trailing space after each colon. It is in the oracle and it is load-bearing for
        //  the fourth label, whose length drives the indent width - see DetailIndent below.
        // =========================================================================================

        /// <summary>
        /// The first line's label, carrying the dispatched event name
        /// [<c>n_cst_eventful.sru:L883</c>].
        /// </summary>
        /// <remarks>
        /// The value it carries is the DISPATCH name, which the loop has already proved equal to the
        /// matched subscription's own name (<c>:L824</c> skips any entry whose name differs), so
        /// "the subscription name" and "the event name" are the same string here by construction.
        /// </remarks>
        private const string SubscribeLine = "Subscribe: ";

        /// <summary>
        /// The second line's label, carrying the target's class chain
        /// [<c>n_cst_eventful.sru:L884</c>].
        /// </summary>
        /// <remarks>
        /// The chain is built by <c>_of_getobjectclasschain</c> (<c>:L976-L991</c>), which names the
        /// target's own class and prepends each enclosing class. The subscriber double used here is
        /// a top-level type, so its chain is a single link;
        /// <c>EventBrokerDispatchInternalsTests</c> owns the multi-link case and it is not
        /// re-asserted here.
        /// </remarks>
        private const string TargetLine = "Target: ";

        /// <summary>
        /// The third line's label, carrying the handler event name
        /// [<c>n_cst_eventful.sru:L885</c>].
        /// </summary>
        private const string EventLine = "Event: ";

        /// <summary>
        /// The fourth line's label [<c>n_cst_eventful.sru:L886</c>], and also the alignment string
        /// the indent is derived from [<c>:L881</c>].
        /// </summary>
        /// <remarks>
        /// One literal at two sites in the oracle, which is why the port holds it once too. Its
        /// length including the trailing newline is twelve, and the aligner's indent is half of that
        /// - see <see cref="DetailIndent"/>.
        /// </remarks>
        private const string ExceptionLine = "Exception: ";

        /// <summary>
        /// The label the assertion-detail path puts between the information field and the frames
        /// [<c>n_cst_eventful.sru:L876</c>].
        /// </summary>
        private const string StackTraceLine = "StackTrace:";

        /// <summary>
        /// The six spaces the aligner indents every continuation line of the detail block by.
        /// </summary>
        /// <remarks>
        /// <para>
        /// DERIVED, NOT GUESSED. <c>_of_alignstring</c> computes <c>Fill(" ", Len(align) / 2)</c>
        /// (<c>n_cst_eventful.sru:L1255</c>) and its only caller passes <c>"Exception: ~n"</c>
        /// (<c>:L881</c>). In PowerScript <c>~n</c> is one character, so that label is twelve
        /// characters and the indent is exactly six. Because the label ends in a newline the
        /// aligner also indents the FIRST line (<c>:L1256-L1258</c>), which is why the detail block
        /// begins with this string rather than continuing straight after the label.
        /// </para>
        /// <para>
        /// Asserted rather than tolerated because the width is coupled to the label: change either
        /// and both move together, exactly as they do in the oracle.
        /// </para>
        /// </remarks>
        private const string DetailIndent = "      ";

        /// <summary>The message the throwing subscriber of a single-level arrangement carries.</summary>
        private const string FirstFailureMessage = "first-failure-detail";

        /// <summary>The message a SECOND throwing subscriber in the same chain carries.</summary>
        private const string SecondFailureMessage = "second-failure-detail";

        /// <summary>The message the nested arrangement's inner subscriber carries.</summary>
        private const string InnerFailureMessage = "inner-failure-detail";

        /// <summary>The information field of the assertion-detail carrier.</summary>
        private const string AssertionInfo = "assertion-info-field";

        /// <summary>The rendered frames of the assertion-detail carrier.</summary>
        private const string AssertionFrames = "assertion-stack-frames";

        /// <summary>
        /// The message the assertion-detail carrier's own <see cref="Exception.Message"/> holds.
        /// </summary>
        /// <remarks>
        /// Distinct from every other literal in this file on purpose: the assertion path REPLACES
        /// the detail with the two structured fields (<c>n_cst_eventful.sru:L876</c>) rather than
        /// prefixing the message, so a test can prove the replacement only by asserting that this
        /// string is absent from the decoration.
        /// </remarks>
        private const string AssertionCarrierMessage = "carrier-message-that-is-replaced";

        /// <summary>
        /// The value the first subscriber of the accumulated-value arrangement returns.
        /// </summary>
        /// <remarks>
        /// A <see langword="long"/> because the oracle's handlers return PowerScript <c>any</c> over
        /// values that are most often <c>long</c> (<c>w_test_eventful.srw:L62</c> returns the sum of
        /// two long arguments) and the broker's handled-state test compares boxed values.
        /// </remarks>
        private const long HandledReturnValue = 42L;

        /// <summary>
        /// The leading-run symbol claiming capture-everything: legacy <c>SYMBOL_ALL</c>
        /// [<c>n_cst_eventful.sru:L96</c>], selecting <c>CAP_ALL</c> [<c>:L102</c>] at
        /// <c>:L348-L350</c>.
        /// </summary>
        /// <remarks>
        /// Needed by exactly one arrangement and for a specific reason: the default capture mode is
        /// unhandled-only, and the dispatch skips such a subscriber once an earlier one has HANDLED
        /// the event (<c>:L831-L833</c>). A subscriber that must still run after a handling one has
        /// to claim this symbol (<c>w_test_eventful.srw:L229</c>). Without it, section 5's
        /// accumulated-value test would see its later subscribers not running and would credit the
        /// exception for a skip the capture filter had already performed.
        /// </remarks>
        private const string CaptureEverythingSymbol = "*";

        // =========================================================================================
        //  HELPERS
        //  So that each arrangement below reads as a statement about the exception path rather than
        //  as subscription plumbing. Each asserts the legacy success code as it goes, so a mistyped
        //  handler name fails at the line that caused it instead of surfacing later as a puzzling
        //  empty log.
        // =========================================================================================

        /// <summary>
        /// Creates a recording subscriber and subscribes its zero-argument handler to
        /// <paramref name="topic"/>, asserting the legacy success code.
        /// </summary>
        /// <param name="broker">The broker to subscribe on, and the one handed to the double.</param>
        /// <param name="log">The shared ordering log every subscriber of a test writes to.</param>
        /// <param name="topic">The subscription topic, with no leading symbol run.</param>
        /// <param name="label">The label this subscriber writes to the log.</param>
        /// <returns>The subscribed double, so the caller can arm a failure or an in-handler action.</returns>
        /// <remarks>
        /// The broker is handed over EXPLICITLY rather than left to the ambient
        /// <see cref="EventBroker.Current"/>. Both routes work, but an explicit reference keeps the
        /// nested arrangements independent of which level happens to be dispatching, which matters
        /// here because two levels of the SAME broker are on the stack at once.
        /// </remarks>
        private static RecordingSubscriber SubscribeTo(
            EventBroker broker,
            DispatchLog log,
            string topic,
            string label)
        {
            RecordingSubscriber subscriber = new(log, label, broker) { Topic = topic };

            // n_cst_eventful.sru:L442 - of_On answers RetCode.OK on a successful subscription.
            Assert.Equal(
                RetCode.OK,
                broker.Subscribe(topic, subscriber, RecordingHandlerNames.NoArguments));

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
        /// See <see cref="CaptureEverythingSymbol"/> for why one arrangement needs this. The symbol
        /// is stripped from the stored name (<c>n_cst_eventful.sru:L363</c>), so the residual
        /// dispatch key is <paramref name="topic"/> unchanged and this subscription joins the same
        /// run as any plain one beside it.
        /// </remarks>
        private static RecordingSubscriber SubscribeCapturingEverythingTo(
            EventBroker broker,
            DispatchLog log,
            string topic,
            string label)
        {
            RecordingSubscriber subscriber = new(log, label, broker) { Topic = topic };

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
        /// The double runs it as its THIRD step - after the invocation has been recorded and counted
        /// and BEFORE any configured failure is thrown - so one handler can both re-enter the broker
        /// and then throw, which is exactly what the nested arrangements need.
        /// </remarks>
        private static void WhileHandling(RecordingSubscriber subscriber, Action action) =>
            subscriber.SetInterceptor(
                RecordingHandlerNames.NoArguments,
                (_, _) => action());

        /// <summary>
        /// Every exception-hook row the log holds, in dispatch order.
        /// </summary>
        /// <param name="log">The log to project.</param>
        /// <returns>The hook rows, which is empty when the hook never fired.</returns>
        /// <remarks>
        /// Selected by <see cref="DispatchRecord.HandlerName"/> rather than by position, because the
        /// derived double records the prepare, triggering and triggered hooks into the same log and
        /// an index would move whenever an arrangement gained a subscriber. A concrete list is
        /// returned rather than an interface so no caller has to materialise it twice.
        /// </remarks>
        private static List<DispatchRecord> ExceptionHookRows(DispatchLog log) =>
            log.Records
                .Where(row => string.Equals(
                    row.HandlerName,
                    TestEventBroker.ExceptionHookName,
                    StringComparison.Ordinal))
                .ToList();

        /// <summary>
        /// The decorated diagnostic block one exception-hook row captured, asserted non-null.
        /// </summary>
        /// <param name="row">The hook row to read.</param>
        /// <returns>The block as the hook saw it.</returns>
        /// <remarks>
        /// <para>
        /// The double records <c>[exception, GetDispatchExceptionText(exception)]</c>, and the second
        /// slot has to be read from the ROW rather than from the exception afterwards: the broker
        /// rewrites the block with its own class name prefixed once the hook has answered and the
        /// dispatch is the outermost one (<c>n_cst_eventful.sru:L899-L901</c>), so a later read
        /// yields a different string. Section 4 asserts that difference deliberately.
        /// </para>
        /// <para>
        /// The nullability is resolved with <see cref="Assert.NotNull(object?)"/> and a local rather
        /// than with the null-forgiving operator: a null here would mean the block was never
        /// recorded, which is a real failure and must be reported as one (C-H).
        /// </para>
        /// </remarks>
        private static string HookCapturedText(DispatchRecord row)
        {
            // The row shape the double promises: the live exception, then the block it saw.
            Assert.Equal(2, row.ArgumentCount);

            // IsType both resolves the nullability and states the expected shape, so no
            // null-forgiving operator is needed and a null slot fails with a readable message.
            return Assert.IsType<string>(row.Arguments[1]);
        }

        /// <summary>
        /// The decorated diagnostic block currently recorded on an exception, asserted non-null.
        /// </summary>
        /// <param name="exception">The exception to read.</param>
        /// <returns>The block as it stands now.</returns>
        /// <remarks>
        /// The read half of the text-carrier substitution. Null would mean the exception never
        /// passed through a dispatch at all, so it is asserted away rather than suppressed.
        /// </remarks>
        private static string RecordedText(Exception exception)
        {
            string? text = EventBroker.GetDispatchExceptionText(exception);
            Assert.NotNull(text);

            return text;
        }

        /// <summary>
        /// The bracketed type-name prefix the first capture of a chain is decorated with
        /// [<c>n_cst_eventful.sru:L878</c>].
        /// </summary>
        /// <param name="exception">The exception whose runtime type name is wanted.</param>
        /// <returns>The prefix, without its trailing newline.</returns>
        /// <remarks>
        /// Built from the runtime type's simple name, which is what <c>ClassName(ex)</c> yields and
        /// what the port reproduces with <c>ex.GetType().Name</c>. Composed here rather than
        /// hard-coded so an arrangement may vary the exception type without the expectation drifting.
        /// </remarks>
        private static string BracketedTypeName(Exception exception) =>
            string.Concat("[", exception.GetType().Name, "]");

        // =========================================================================================
        //  THE TWO LOCAL DOUBLES
        //  Both are private, sealed and single-purpose. Neither replaces TestEventBroker, which
        //  remains the double for every outcome the hook can answer; each adds exactly one
        //  capability TestEventBroker cannot offer.
        // =========================================================================================

        /// <summary>
        /// An exception that carries assertion detail through the broker's published
        /// <see cref="IAssertionDetail"/> contract.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The C-K substitution made concrete.</b> The oracle special-cases one class by name and
        /// reads two members off it (<c>n_cst_eventful.sru:L873-L876</c>). That class ports to the
        /// Diagnostics library, and this test project holds exactly one project reference - to the
        /// library under test - so a real assertion type is out of reach and must stay out of reach.
        /// <see cref="IAssertionDetail"/> is the abstraction the implementation provides for exactly
        /// this: any exception may satisfy it, and the broker asks only for two strings.
        /// </para>
        /// <para>
        /// So this suite asserts THE DECORATION - the information field, the stack-trace label and
        /// the frames - and never a diagnostics type. No second project reference is added.
        /// </para>
        /// </remarks>
        private sealed class AssertionDetailCarrier : Exception, IAssertionDetail
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="AssertionDetailCarrier"/> class.
            /// </summary>
            /// <remarks>
            /// The base message is deliberately a string the decoration must NOT contain: the
            /// assertion arm REPLACES the detail rather than prefixing it, so its absence is the
            /// assertion.
            /// </remarks>
            internal AssertionDetailCarrier()
                : base(AssertionCarrierMessage)
            {
            }

            /// <inheritdoc/>
            public string Info => AssertionInfo;

            /// <inheritdoc/>
            public string StackTraceInfo => AssertionFrames;
        }

        /// <summary>
        /// A derived broker that answers the exception hook with a fixed outcome and runs a callback
        /// <b>from inside the hook</b>, while the failure latch is still set.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this exists at all.</b> <see cref="TestEventBroker"/> is
        /// <see langword="sealed"/> and offers no in-hook seam, and the latch has no accessor, so
        /// the only way to observe its state DURING the hook is to cause a second capture from
        /// inside the hook and read how that capture was decorated. This double adds exactly that
        /// one capability. Sibling suites take the same approach where a bespoke hook is needed.
        /// </para>
        /// <para>
        /// The callback fires at most once per instance. Without that guard the nested dispatch's
        /// own capture would re-enter the hook and re-trigger, and the arrangement would recurse
        /// rather than measure.
        /// </para>
        /// <para>
        /// It answers <see cref="EventBroker.ExceptionResultPrevent"/> at BOTH levels on purpose, so
        /// nothing propagates out of the nested dispatch into the middle of the hook - an exception
        /// escaping the hook would replace the very exception under observation.
        /// </para>
        /// </remarks>
        private sealed class InHookProbeBroker : EventBroker
        {
            private readonly List<string> _capturedTexts = [];

            private bool _callbackHasRun;

            /// <summary>
            /// Gets or sets the callback the hook runs, once, while the latch is set.
            /// </summary>
            internal Action? DuringExceptionHook { get; set; }

            /// <summary>
            /// Gets the decorated block each hook invocation saw, in the order the hook saw them.
            /// </summary>
            internal IReadOnlyList<string> CapturedTexts => _capturedTexts;

            /// <inheritdoc/>
            protected override long OnException(string name, Exception exception)
            {
                // Recorded before anything can divert, and read HERE because the broker rewrites the
                // block once this hook has answered (n_cst_eventful.sru:L899-L901).
                string? text = GetDispatchExceptionText(exception);
                Assert.NotNull(text);
                _capturedTexts.Add(text);

                if (!_callbackHasRun && DuringExceptionHook is not null)
                {
                    _callbackHasRun = true;
                    DuringExceptionHook();
                }

                // :L890-L891 - swallow at every level, so the arrangement ends without a throw and
                // the recorded texts are the whole of the evidence.
                return ExceptionResultPrevent;
            }
        }

        // =========================================================================================
        //  1. THE TEXT CARRIER - WHAT THE PORT ACTUALLY DOES WITH THE EXCEPTION
        //
        //  Authored first because every later assertion reads the decoration through it. The oracle
        //  ASSIGNS ex.text (:L883, :L900) and rethrows the same object; .NET has no settable
        //  Message, so this is the one place the port had to choose a mechanism, and a suite that
        //  did not pin the choice would be asserting against an implementation detail it never
        //  established.
        // =========================================================================================

        /// <summary>
        /// The decoration is recorded on the exception's data dictionary, the message is left
        /// untouched, and the instance that escapes is the instance that was thrown.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>C-K, the text-carrier substitution, asserted rather than assumed.</b> The oracle
        /// overwrites <c>ex.text</c> outright and rethrows the same object
        /// (<c>n_cst_eventful.sru:L883-L886</c>, <c>:L902</c>).
        /// <see cref="System.Exception.Message"/> is not settable, so the port records the block on
        /// <see cref="System.Exception.Data"/> under
        /// <see cref="EventBroker.DispatchExceptionTextKey"/> and rethrows the ORIGINAL instance.
        /// Three consequences are pinned here, and each rules out an alternative the port explicitly
        /// rejected:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///   <b>Not wrapped.</b> Reference equality and a null inner exception together say the
        ///   runtime type an enclosing <see langword="catch"/> matches is unchanged. Wrapping would
        ///   have been a behavioural change introduced by the port, since the oracle never changes
        ///   the type.
        ///   </description></item>
        ///   <item><description>
        ///   <b>Not message-mutated.</b> The message still reads exactly what the handler threw, so
        ///   nothing that formats an exception the ordinary way sees the block, and nothing that
        ///   matches on the message breaks.
        ///   </description></item>
        ///   <item><description>
        ///   <b>Reachable, and only through the published accessor.</b> The block sits under the
        ///   published key and <see cref="EventBroker.GetDispatchExceptionText"/> returns exactly
        ///   that value, so a consumer never has to know the key spelling.
        ///   </description></item>
        /// </list>
        /// <para>
        /// The stack trace still names the subscriber double, which is the observable difference
        /// between the bare <c>throw</c> the port uses and a <c>throw ex</c> that would reset the
        /// trace to the broker's own frame. It is deterministic despite being a stack trace: the
        /// handler is reached by reflection, so its frame cannot be inlined away, and no file, line
        /// or address is asserted.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheDecorationIsRecordedOnTheExceptionDataAndTheSameInstanceIsRethrown()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException failure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            // :L902 - throw ex. The base hook answers 0, which is neither arm of the choose case, so
            // the exception leaves the dispatch.
            InvalidOperationException escaped =
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic));

            // The same object, unwrapped.
            Assert.Same(failure, escaped);
            Assert.Null(escaped.InnerException);

            // The message is the handler's own, untouched by the decoration.
            Assert.Equal(FirstFailureMessage, escaped.Message);

            // The block is on Data under the published key, and the accessor returns that value.
            string recorded = RecordedText(escaped);
            Assert.Equal(
                recorded,
                Assert.IsType<string>(escaped.Data[EventBroker.DispatchExceptionTextKey]));

            // The block is a different thing from the message, which is the whole point of using a
            // separate carrier rather than mutating the message.
            Assert.NotEqual(escaped.Message, recorded);
            Assert.Contains(string.Concat(SubscribeLine, SoleTopic), recorded, StringComparison.Ordinal);

            // A bare rethrow, so the original throw site survives.
            Assert.NotNull(escaped.StackTrace);
            Assert.Contains(nameof(RecordingSubscriber), escaped.StackTrace, StringComparison.Ordinal);
        }

        // =========================================================================================
        //  2. ROUTING, AND THE LATCH'S LIFETIME
        //
        //  Where the exception goes, that it goes there exactly once, that the latch is set while
        //  the hook runs, that it is clear again once the depth returns to zero, and that a broker
        //  which is not a subclass takes no part in any of it.
        // =========================================================================================

        /// <summary>
        /// A throwing subscriber reaches the exception hook exactly once, with the dispatched event
        /// name and the very instance it threw, positioned immediately after its own invocation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ordered handler sequence is the assertion, because it says four things at once that
        /// four separate counts could not: the triggering hook fired first and only after a matching
        /// subscription was found (<c>n_cst_eventful.sru:L838-L843</c>), the prepare hook fired once
        /// for the subscriber (<c>:L609</c>, inside <c>_of_passargs</c>), the exception hook fired once
        /// and AFTER the handler ran rather than in place of it (<c>:L889</c>), and the triggered hook
        /// still fired on the unwind despite the failure (<c>:L942-L946</c>).
        /// </para>
        /// <para>
        /// The subscriber's own row appears before the hook's because the double records its
        /// invocation BEFORE it throws - which is what lets an assertion distinguish "ran and failed"
        /// from "was skipped".
        /// </para>
        /// </remarks>
        [Fact]
        public void AThrowingSubscriberReachesTheExceptionHookOnceWithTheTopicAndTheThrownInstance()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException failure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic));

            // Exactly one hook invocation for one failing subscriber.
            DispatchRecord hookRow = Assert.Single(ExceptionHookRows(log));

            // Recorded by the BROKER, so a reader of the log can tell a hook row from a subscriber
            // row without a lookup.
            Assert.Equal(BrokerLabel, hookRow.Label);

            // :L889 - Event OnException(name,ex). The first argument is the dispatched event name.
            Assert.Equal(SoleTopic, hookRow.Topic);

            // ...and the second is the live instance, not a copy or a rendering of it.
            Assert.Same(failure, hookRow.Arguments[0]);

            // The whole ordered sequence, ordinally: a label is an identifier, not prose.
            Assert.Equal(
                new[]
                {
                    TestEventBroker.TriggeringHookName,
                    TestEventBroker.PrepareHookName,
                    RecordingHandlerNames.NoArguments,
                    TestEventBroker.ExceptionHookName,
                    TestEventBroker.TriggeredHookName
                },
                log.HandlerNames,
                StringComparer.Ordinal);

            // The hook's row sits IMMEDIATELY after the subscriber's own, with nothing between them:
            // the capture happens in the catch of the very try the invocation was made in (:L857-L902),
            // so no other row can be interposed. Asserted on the monotonic sequence rather than on
            // list positions, so it stays true if an arrangement ever grows another subscriber.
            DispatchRecord subscriberRow = Assert.Single(
                log.Records,
                row => string.Equals(
                    row.HandlerName,
                    RecordingHandlerNames.NoArguments,
                    StringComparison.Ordinal));

            Assert.Equal(subscriberRow.Sequence + 1, hookRow.Sequence);

            // The handler ran once and was recorded before it threw.
            Assert.Equal(1, failing.InvocationCount);
        }

        /// <summary>
        /// The failure latch is already SET while the exception hook is executing, so a capture that
        /// happens from inside the hook is not decorated as a first failure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The latch has no accessor, so this is how its state during the hook is measured.</b>
        /// The oracle sets it at <c>n_cst_eventful.sru:L872</c>, BEFORE it composes the block at
        /// <c>:L883-L886</c> and before it calls the hook at <c>:L889</c>. The only observable
        /// consequence of it being set is that the next capture skips the bracketed type name at
        /// <c>:L878</c> - so a second capture raised from inside the hook reports the latch's state
        /// at that exact moment.
        /// </para>
        /// <para>
        /// <b>It is also the direct assertion that the latch PERSISTS ACROSS LEVELS while a nested
        /// dispatch is still in flight.</b> The dispatch the hook starts here runs at depth one with
        /// the enclosing dispatch still on the stack, and its capture meets a latch that is set -
        /// because <c>:L959-L960</c> clears the latch only when the depth returns to zero, and the
        /// enclosing level has not unwound yet. The companion theory further down measures the same
        /// persistence one step later, at the point where the inner level HAS unwound.
        /// </para>
        /// <para>
        /// Both levels swallow, so the arrangement completes without a throw and the two recorded
        /// blocks are the whole of the evidence. <see cref="Assert.Collection{T}"/> pins their number
        /// and their content together.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheFailureLatchIsAlreadySetWhileTheExceptionHookIsExecuting()
        {
            DispatchLog log = new();
            InHookProbeBroker broker = new();

            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber reachedFromHook = SubscribeTo(broker, log, HookTopic, HookLabel);

            InvalidOperationException firstFailure = new(FirstFailureMessage);
            InvalidOperationException hookFailure = new(SecondFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, firstFailure);
            reachedFromHook.ThrowOn(RecordingHandlerNames.NoArguments, hookFailure);

            // The second capture happens HERE - inside the hook, while the latch is set.
            broker.DuringExceptionHook = () => broker.Trigger(HookTopic);

            // Both levels answer prevent, so nothing escapes. Reaching the next line is that
            // assertion: :L890-L891 leaves the loop instead of throwing.
            broker.Trigger(SoleTopic);

            Assert.Collection(
                broker.CapturedTexts,
                outerCapture =>
                {
                    // The FIRST capture of the chain: latch was clear, so :L878 applied.
                    Assert.Contains(
                        string.Concat(SubscribeLine, SoleTopic),
                        outerCapture,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, BracketedTypeName(firstFailure)),
                        outerCapture,
                        StringComparison.Ordinal);
                },
                hookCapture =>
                {
                    // The capture raised from INSIDE the hook. Its own detail is present...
                    Assert.Contains(
                        string.Concat(SubscribeLine, HookTopic),
                        hookCapture,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, SecondFailureMessage),
                        hookCapture,
                        StringComparison.Ordinal);

                    // ...and it carries NO bracketed type name, which is only possible if the latch
                    // was set at the moment this capture was decorated - that is, while the hook was
                    // running. THIS IS THE ASSERTION.
                    Assert.DoesNotContain(
                        BracketedTypeName(hookFailure),
                        hookCapture,
                        StringComparison.Ordinal);
                });

            // Both handlers genuinely ran, so neither capture came from a skipped subscriber.
            Assert.Equal(1, failing.InvocationCount);
            Assert.Equal(1, reachedFromHook.InvocationCount);
        }

        /// <summary>
        /// A rethrowing answer delivers the exception to the trigger's caller, and the latch is clear
        /// again afterwards because the dispatch depth returned to zero.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two dispatches on ONE broker instance, which is what makes this a statement about the
        /// broker rather than about test hygiene: the second dispatch's failure is decorated as a
        /// FIRST failure, so the latch must have been cleared between them. The oracle clears it in
        /// the dispatch's outer finally and only at the outermost level
        /// (<c>n_cst_eventful.sru:L959-L960</c>), which is reached even though the loop ended in a
        /// throw.
        /// </para>
        /// <para>
        /// Both failures are of the same type on purpose, so the bracketed prefix cannot be
        /// distinguished by type and the two captures are told apart by their own messages instead.
        /// A port that left the latch set would produce a second block with the message but without
        /// the prefix, and only the second inspector below would fail.
        /// </para>
        /// </remarks>
        [Fact]
        public void ARethrowingAnswerReachesTheCallerAndTheLatchIsClearOnceTheDepthReturnsToZero()
        {
            DispatchLog log = new();

            // The default outcome rethrows - TestEventBroker.ExceptionResultRethrow is 0, the value
            // the base hook returns and the value the real subclass returns to let a thread
            // exception propagate (n_cst_threading_eventful.sru:L87).
            TestEventBroker broker = new(log, BrokerLabel);
            Assert.Equal(TestEventBroker.ExceptionResultRethrow, broker.ExceptionResult);

            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException firstFailure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, firstFailure);

            Assert.Same(
                firstFailure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic)));

            // The depth is back to zero, so the latch must be clear. Arm a fresh failure and look at
            // how the NEXT dispatch decorates it.
            InvalidOperationException secondFailure = new(SecondFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, secondFailure);

            Assert.Same(
                secondFailure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic)));

            Assert.Collection(
                ExceptionHookRows(log),
                first =>
                {
                    string text = HookCapturedText(first);
                    Assert.Contains(
                        string.Concat(DetailIndent, BracketedTypeName(firstFailure)),
                        text,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, FirstFailureMessage),
                        text,
                        StringComparison.Ordinal);
                },
                second =>
                {
                    // DECORATED AS A FIRST FAILURE AGAIN. This is the observable form of :L959-L960.
                    string text = HookCapturedText(second);
                    Assert.Contains(
                        string.Concat(DetailIndent, BracketedTypeName(secondFailure)),
                        text,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, SecondFailureMessage),
                        text,
                        StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// A plain, non-derived broker fires no hook at all and always lets the exception propagate,
        /// while the identical arrangement on a derived broker can swallow it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This contrast is what proves the subclassing gate survived the port.</b> The oracle
        /// computes the flag once in its constructor -
        /// <c>_bSubclassing = (_sThisClsName &lt;&gt; "n_cst_eventful")</c>
        /// (<c>n_cst_eventful.sru:L1324</c>) - and guards the hook call site with it (<c>:L888</c>),
        /// which the port expresses as <c>GetType() != typeof(EventBroker)</c>. Two runs of ONE
        /// arrangement differing only in the broker's type therefore differ in outcome, and nothing
        /// else about them can be blamed for the difference.
        /// </para>
        /// <para>
        /// The plain run's log holding exactly one row - the subscriber's - is a stronger statement
        /// than "the exception hook did not fire": it says none of the four hooks fired, because a
        /// derived broker records every one of them into this same shared log.
        /// </para>
        /// <para>
        /// <b>The decoration itself is NOT gated on the flag, and that asymmetry is deliberate in the
        /// oracle.</b> <c>:L871-L886</c> sits outside the <c>if _bSubclassing</c> at <c>:L888</c>, so
        /// a plain broker still produces the four-line block and still prefixes its own class name at
        /// the outermost level (<c>:L899-L900</c>). Only the hook DECISION is gated. The plain run
        /// below asserts the block is present and prefixed with the base type's own name, which also
        /// pins <c>ClassName(this)</c> to <c>GetType().Name</c>.
        /// </para>
        /// </remarks>
        [Fact]
        public void APlainBrokerFiresNoHookAndTheExceptionAlwaysPropagates()
        {
            // ----- RUN A: the plain broker. No hook exists to consult, so :L902 is inevitable. -----
            DispatchLog plainLog = new();
            EventBroker plain = new();

            RecordingSubscriber plainFailing = SubscribeTo(plain, plainLog, SoleTopic, FirstLabel);
            RecordingSubscriber plainLater = SubscribeTo(plain, plainLog, SoleTopic, SecondLabel);

            InvalidOperationException plainFailure = new(FirstFailureMessage);
            plainFailing.ThrowOn(RecordingHandlerNames.NoArguments, plainFailure);

            Assert.Same(
                plainFailure,
                Assert.Throws<InvalidOperationException>(() => plain.Trigger(SoleTopic)));

            // NONE of the four hooks fired: the only row in the log is the subscriber's own.
            Assert.Equal(
                new[] { RecordingHandlerNames.NoArguments },
                plainLog.HandlerNames,
                StringComparer.Ordinal);

            Assert.Empty(ExceptionHookRows(plainLog));

            // The dispatch left the loop by throwing, so the later subscriber never ran.
            Assert.Equal(0, plainLater.InvocationCount);

            // Still decorated, and still prefixed with the broker's OWN class name at depth zero -
            // :L899-L900 with sThisClsName = ClassName(this) = GetType().Name.
            string plainText = RecordedText(plainFailure);
            Assert.StartsWith(string.Concat(nameof(EventBroker), "\n"), plainText, StringComparison.Ordinal);
            Assert.Contains(string.Concat(SubscribeLine, SoleTopic), plainText, StringComparison.Ordinal);

            // ----- RUN B: the same arrangement on a DERIVED broker that answers prevent. -----
            DispatchLog derivedLog = new();
            TestEventBroker derived = new(derivedLog, BrokerLabel)
            {
                ExceptionResult = EventBroker.ExceptionResultPrevent
            };

            RecordingSubscriber derivedFailing = SubscribeTo(derived, derivedLog, SoleTopic, FirstLabel);
            RecordingSubscriber derivedLater = SubscribeTo(derived, derivedLog, SoleTopic, SecondLabel);

            InvalidOperationException derivedFailure = new(FirstFailureMessage);
            derivedFailing.ThrowOn(RecordingHandlerNames.NoArguments, derivedFailure);

            // NO throw. Reaching the next line is the assertion, and it is the outcome the plain run
            // above cannot produce however it is configured, because it has no hook to ask.
            derived.Trigger(SoleTopic);

            Assert.Single(ExceptionHookRows(derivedLog));
            Assert.Equal(0, derivedLater.InvocationCount);
            Assert.Equal(1, derivedFailing.InvocationCount);
        }

        /// <summary>
        /// A dispatch in which nothing throws leaves the failure latch clear, so a later failure on
        /// the same broker is still decorated as a first failure.
        /// </summary>
        /// <remarks>
        /// The control case for the whole latch story. It rules out the reading that the latch is
        /// merely "has this broker ever dispatched": the first dispatch here runs a handler to
        /// completion, fires no hook, and leaves the bracketed prefix available to the failure that
        /// follows it.
        /// </remarks>
        [Fact]
        public void ADispatchWithNoFailureLeavesTheLatchClear()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber subscriber = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            broker.Trigger(SoleTopic);

            // Nothing was captured, so the hook was never consulted.
            Assert.Empty(ExceptionHookRows(log));
            Assert.Equal(1, subscriber.InvocationCount);

            // Now fail, on the same instance.
            InvalidOperationException failure = new(FirstFailureMessage);
            subscriber.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            Assert.Same(
                failure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic)));

            // Decorated as a FIRST failure - :L871 found the latch clear, so :L878 applied.
            DispatchRecord hookRow = Assert.Single(ExceptionHookRows(log));
            Assert.Contains(
                string.Concat(DetailIndent, BracketedTypeName(failure)),
                HookCapturedText(hookRow),
                StringComparison.Ordinal);
        }

        // =========================================================================================
        //  3. THE THREE ANSWERS
        //
        //  ONE arrangement - three subscribers on one topic whose SECOND one throws - run once per
        //  answer, with the answer as the only variable. Nothing else about the runs differs, so no
        //  difference in outcome can be attributed to the topology, the subscription order or the
        //  number of subscribers.
        // =========================================================================================

        /// <summary>
        /// An answer that is neither of the broker's two published constants, standing in for the
        /// unenumerable "anything else" arm of the legacy <c>choose case</c>.
        /// </summary>
        /// <remarks>
        /// The rethrow arm has no constant of its own because it cannot be enumerated
        /// (<c>n_cst_eventful.sru:L889-L895</c> has exactly two cases and no <c>case else</c>), so the
        /// matrix carries TWO representatives of it: zero, which is what the base hook and the real
        /// subclass both answer, and this arbitrary value. A port that recognised only zero as
        /// "rethrow" - by testing equality rather than by falling through - would pass the zero row
        /// and fail this one.
        /// </remarks>
        private const long UnrecognisedHookAnswer = 7L;

        /// <summary>
        /// The outcome of one run of the three-subscriber arrangement, carried whole so a theory can
        /// assert on the ordering, the invocation counts and the escape together.
        /// </summary>
        private sealed class OutcomeRun
        {
            /// <summary>Initializes a new instance of the <see cref="OutcomeRun"/> class.</summary>
            /// <param name="log">The shared ordering log the run wrote to.</param>
            /// <param name="third">The third subscriber, whose fate distinguishes the answers.</param>
            /// <param name="failure">The instance the second subscriber threw.</param>
            /// <param name="escaped">
            /// What reached the trigger's caller, or <see langword="null"/> when the dispatch
            /// returned normally.
            /// </param>
            internal OutcomeRun(
                DispatchLog log,
                RecordingSubscriber third,
                InvalidOperationException failure,
                Exception? escaped)
            {
                Log = log;
                Third = third;
                Failure = failure;
                Escaped = escaped;
            }

            /// <summary>Gets the shared ordering log.</summary>
            internal DispatchLog Log { get; }

            /// <summary>Gets the third subscriber.</summary>
            internal RecordingSubscriber Third { get; }

            /// <summary>Gets the instance the second subscriber threw.</summary>
            internal InvalidOperationException Failure { get; }

            /// <summary>Gets what escaped to the caller, if anything.</summary>
            internal Exception? Escaped { get; }
        }

        /// <summary>
        /// Runs the three-subscriber arrangement on a fresh broker with one configured hook answer.
        /// </summary>
        /// <param name="hookAnswer">The value the exception hook answers.</param>
        /// <returns>The run's outcome.</returns>
        /// <remarks>
        /// <para>
        /// The <see langword="catch"/> here is a CAPTURE, not a swallow: every row of the theory then
        /// asserts either that the escape is the thrown instance or that there was no escape at all,
        /// so a run that threw when it should not have - or that did not throw when it should have -
        /// fails. Writing it as <see cref="Assert.Throws{T}(Action)"/> in the helper would force the
        /// helper to know the answer, which is the one thing the theory is varying.
        /// </para>
        /// <para>
        /// The answer-independent outcomes are asserted here so every row inherits them: the first
        /// subscriber ran, the second ran and failed, and the hook was consulted exactly once. Those
        /// three are what make the per-row difference attributable to the answer alone.
        /// </para>
        /// </remarks>
        private static OutcomeRun RunThreeSubscriberArrangement(long hookAnswer)
        {
            DispatchLog log = new();

            // A FRESH broker per run - hygiene, and not a substitute for section 2's assertion that
            // the broker itself clears the latch at depth zero.
            TestEventBroker broker = new(log, BrokerLabel) { ExceptionResult = hookAnswer };

            RecordingSubscriber first = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber third = SubscribeTo(broker, log, SoleTopic, ThirdLabel);

            InvalidOperationException failure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            Exception? escaped = null;

            try
            {
                broker.Trigger(SoleTopic);
            }
            catch (InvalidOperationException ex)
            {
                escaped = ex;
            }

            // Answer-independent: the subscriber before the failure ran, the failing one ran, and the
            // hook was asked exactly once.
            Assert.Equal(1, first.InvocationCount);
            Assert.Equal(1, failing.InvocationCount);
            Assert.Single(ExceptionHookRows(log));

            return new OutcomeRun(log, third, failure, escaped);
        }

        /// <summary>
        /// The three answers, the fate of the third subscriber under each, whether the exception
        /// escapes, and the ordered handler sequence each produces from the ONE arrangement.
        /// </summary>
        /// <remarks>
        /// The prevent and rethrow sequences are identical - both leave the loop after the hook - and
        /// they are told apart by the escape, which is exactly the distinction the legacy draws
        /// between <c>exit</c> at <c>n_cst_eventful.sru:L891</c> and <c>throw ex</c> at
        /// <c>:L902</c>. The continue sequence is the only one that grows, by the third subscriber's
        /// prepare-and-invoke pair.
        /// </remarks>
        public static TheoryData<long, bool, bool, string[]> ExceptionHookAnswerRows =>
            new()
            {
                // PREVENT. :L890-L891 leaves the dispatch loop and NOTHING IS THROWN. Under C-B this
                // is asserted as CORRECT behaviour: a handler failed and the caller of Trigger sees a
                // normal return. Swallowing is what the oracle does, and it is not softened here.
                {
                    EventBroker.ExceptionResultPrevent,
                    false,
                    false,
                    new[]
                    {
                        TestEventBroker.TriggeringHookName,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.ExceptionHookName,
                        TestEventBroker.TriggeredHookName
                    }
                },

                // CONTINUE. :L892-L894 clears the latch and `continue`s, so the third subscriber runs
                // and still nothing is thrown. One broken handler therefore does not stop the others.
                {
                    EventBroker.ExceptionResultContinue,
                    true,
                    false,
                    new[]
                    {
                        TestEventBroker.TriggeringHookName,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.ExceptionHookName,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.TriggeredHookName
                    }
                },

                // ANYTHING ELSE, representative one: zero. Falls past both cases to :L902.
                {
                    TestEventBroker.ExceptionResultRethrow,
                    false,
                    true,
                    new[]
                    {
                        TestEventBroker.TriggeringHookName,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.ExceptionHookName,
                        TestEventBroker.TriggeredHookName
                    }
                },

                // ANYTHING ELSE, representative two: an arbitrary value that is neither constant.
                {
                    UnrecognisedHookAnswer,
                    false,
                    true,
                    new[]
                    {
                        TestEventBroker.TriggeringHookName,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.PrepareHookName,
                        RecordingHandlerNames.NoArguments,
                        TestEventBroker.ExceptionHookName,
                        TestEventBroker.TriggeredHookName
                    }
                }
            };

        /// <summary>
        /// The exception hook's answer selects swallow, continue-to-the-next-subscriber, or rethrow.
        /// </summary>
        /// <param name="hookAnswer">The value the hook answers.</param>
        /// <param name="expectThirdSubscriberRan">Whether the third subscriber should run.</param>
        /// <param name="expectRethrow">Whether the exception should reach the trigger's caller.</param>
        /// <param name="expectedHandlers">The ordered handler sequence the answer must produce.</param>
        /// <remarks>
        /// <para>
        /// <b>Getting the throw/no-throw direction backwards is the single easiest error in this
        /// suite</b>, which is why the direction is a row value rather than a hard-coded shape: the
        /// swallowing rows assert <see cref="Assert.Null(object?)"/> on the escape and the rethrowing
        /// rows assert reference equality, and no row is free to do either.
        /// </para>
        /// <para>
        /// The class-name prefix is asserted here too, because it is a property of the ANSWER and not
        /// only of the depth: <c>:L899-L900</c> is reached only after the hook has failed to stop the
        /// exception, so a swallowed exception's block never gains it. Section 4 covers the depth
        /// dimension separately.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(ExceptionHookAnswerRows))]
        public void TheExceptionHookAnswerSelectsSwallowContinueOrRethrow(
            long hookAnswer,
            bool expectThirdSubscriberRan,
            bool expectRethrow,
            string[] expectedHandlers)
        {
            OutcomeRun run = RunThreeSubscriberArrangement(hookAnswer);

            // The ordered sequence IS the assertion: who ran, in what order, and - by absence - who
            // did not.
            Assert.Equal(expectedHandlers, run.Log.HandlerNames, StringComparer.Ordinal);

            Assert.Equal(expectThirdSubscriberRan ? 1 : 0, run.Third.InvocationCount);

            string recorded = RecordedText(run.Failure);

            if (expectRethrow)
            {
                // :L902 - the same instance reaches the caller...
                Assert.Same(run.Failure, run.Escaped);

                // ...and only this path prefixes the broker's own class name (:L899-L900).
                Assert.StartsWith(
                    string.Concat(nameof(TestEventBroker), "\n"),
                    recorded,
                    StringComparison.Ordinal);
            }
            else
            {
                // SWALLOWED, and asserted as correct under C-B: :L891 exits the loop and :L894
                // continues it, and neither reaches the throw.
                Assert.Null(run.Escaped);

                // The block was still recorded - the decoration is not gated on the answer - but the
                // class-name prefix was not, because :L899-L900 was never reached.
                Assert.StartsWith(SubscribeLine, recorded, StringComparison.Ordinal);
                Assert.DoesNotContain(nameof(TestEventBroker), recorded, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// A continue answer clears the failure latch, so the NEXT failure in the same dispatch is
        /// decorated as a first failure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>THE assertion this suite exists for.</b> A continue answer does two things, not one: it
        /// advances the chain (<c>continue</c> at <c>n_cst_eventful.sru:L894</c>) AND it clears the
        /// latch (<c>_bHasException = false</c> at <c>:L893</c>). A port that only advanced would pass
        /// every other continue assertion in this file and fail only this one, because only this one
        /// depends on the clearing.
        /// </para>
        /// <para>
        /// Both failures are the same type, so the bracketed prefix cannot be told apart by type; the
        /// two captures are distinguished by their own messages, and each is asserted to carry both
        /// its message and the prefix. Under a port that left the latch set, the second block would
        /// carry the message and NOT the prefix.
        /// </para>
        /// </remarks>
        [Fact]
        public void AContinueAnswerClearsTheLatchSoTheNextFailureIsDecoratedAsAFirstFailure()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel)
            {
                ExceptionResult = EventBroker.ExceptionResultContinue
            };

            RecordingSubscriber first = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber secondFailing = SubscribeTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber thirdFailing = SubscribeTo(broker, log, SoleTopic, ThirdLabel);

            InvalidOperationException firstFailure = new(FirstFailureMessage);
            InvalidOperationException secondFailure = new(SecondFailureMessage);
            secondFailing.ThrowOn(RecordingHandlerNames.NoArguments, firstFailure);
            thirdFailing.ThrowOn(RecordingHandlerNames.NoArguments, secondFailure);

            // Both failures are continued past, so the dispatch returns normally.
            broker.Trigger(SoleTopic);

            Assert.Equal(1, first.InvocationCount);
            Assert.Equal(1, secondFailing.InvocationCount);
            Assert.Equal(1, thirdFailing.InvocationCount);

            Assert.Collection(
                ExceptionHookRows(log),
                firstCapture =>
                {
                    string text = HookCapturedText(firstCapture);
                    Assert.Contains(
                        string.Concat(DetailIndent, BracketedTypeName(firstFailure)),
                        text,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, FirstFailureMessage),
                        text,
                        StringComparison.Ordinal);
                },
                secondCapture =>
                {
                    // A FIRST FAILURE AGAIN, in the same dispatch, at the same depth. Only :L893 can
                    // produce this.
                    string text = HookCapturedText(secondCapture);
                    Assert.Contains(
                        string.Concat(DetailIndent, BracketedTypeName(secondFailure)),
                        text,
                        StringComparison.Ordinal);

                    Assert.Contains(
                        string.Concat(DetailIndent, SecondFailureMessage),
                        text,
                        StringComparison.Ordinal);
                });
        }

        /// <summary>
        /// The exception hook's answer alphabet is not the dispatch veto's, and neither is the return
        /// code's - they merely share the numerals one and two.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Three alphabets, asserted independently and never one in terms of another.</b> They are
        /// not interchangeable and the port keeps them apart by TYPE as well as by value: this one is
        /// a <see langword="long"/> because the legacy event returns a long, the dispatch veto is the
        /// <see cref="VetoResult"/> enum, and the prepare and triggering hooks answer
        /// <see cref="RetCode"/> values.
        /// </para>
        /// <para>
        /// The meanings are the reason the separation matters. <see cref="VetoResult.PreventDeep"/> is
        /// two and means "prevent the whole nested chain"; this alphabet's two means "keep going",
        /// which is very nearly the opposite. A single shared alphabet would make a deep prevention
        /// and a continue indistinguishable on the wire, and AAP 0.6.1.2 requires the veto to be
        /// carried tri-valued precisely because that distinction has to survive.
        /// </para>
        /// <para>
        /// The type-level separation is asserted by construction: <see cref="VetoResult"/> is an
        /// enum, so no <see cref="VetoResult"/> value can be passed where the hook's
        /// <see langword="long"/> answer is expected without a deliberate cast, and this test's
        /// numeric assertions are made on each alphabet on its own terms.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheExceptionAnswerAlphabetIsNotTheDispatchVetoAlphabet()
        {
            // THIS alphabet, on its own terms. n_cst_eventful.sru:L890 and :L892.
            Assert.Equal(1L, EventBroker.ExceptionResultPrevent);
            Assert.Equal(2L, EventBroker.ExceptionResultContinue);

            // THE DISPATCH VETO, on its own terms. :L110-L112, and VetoSemanticsTests owns it.
            Assert.Equal(1, (int)VetoResult.PreventOnce);
            Assert.Equal(2, (int)VetoResult.PreventDeep);

            // The two are DISTINCT TYPES, so the compiler already refuses to confuse them. Stated as
            // an assertion so the claim is checked rather than merely commented.
            Assert.True(typeof(VetoResult).IsEnum);
            Assert.Equal(typeof(long), EventBroker.ExceptionResultPrevent.GetType());
            Assert.NotEqual(typeof(VetoResult), EventBroker.ExceptionResultContinue.GetType());

            // AND THE MEANINGS DIVERGE AT THE SAME NUMERAL. Two here continues the dispatch; two in
            // the veto alphabet stops the whole nested chain. Asserted through the behaviour of THIS
            // alphabet only - the veto's behaviour is asserted in its own suite, and nothing here
            // states one in terms of the other.
            OutcomeRun continued = RunThreeSubscriberArrangement(EventBroker.ExceptionResultContinue);
            Assert.Equal(1, continued.Third.InvocationCount);
            Assert.Null(continued.Escaped);

            // The third alphabet, named so no reader mistakes the prepare and triggering hooks for
            // this one: they are tested with the return-code prevention predicate (:L609, :L840).
            Assert.Equal(1L, RetCode.PREVENT);
            Assert.Equal(0L, RetCode.OK);
        }

        // =========================================================================================
        //  4. THE DECORATION
        //
        //  Four labelled lines, one indent width, one bracketed type name per chain, one special
        //  case for an assertion shape, and one class-name prefix at the outermost level. Every
        //  component is asserted SEPARATELY - never as one opaque literal - so a change to one label
        //  cannot mask a change to another.
        // =========================================================================================

        /// <summary>
        /// Splits a recorded block into its lines.
        /// </summary>
        /// <param name="text">The block.</param>
        /// <returns>The lines, in order, with their indentation intact.</returns>
        /// <remarks>
        /// The separator is a bare line feed because that is what the port emits for the oracle's
        /// <c>~n</c>, and the <see langword="char"/> overload is ordinal by definition. Indentation is
        /// deliberately NOT trimmed: the six-space indent the aligner produces
        /// (<c>n_cst_eventful.sru:L1255</c>) is part of the contract and is asserted, not normalised
        /// away.
        /// </remarks>
        private static string[] BlockLines(string text) => text.Split('\n');

        /// <summary>
        /// How many times one value occurs in a block, compared ordinally.
        /// </summary>
        /// <param name="text">The block to scan.</param>
        /// <param name="value">The value to count. Must not be empty.</param>
        /// <returns>The number of non-overlapping occurrences.</returns>
        /// <remarks>
        /// Needed because the nested cases turn on a value occurring exactly ONCE rather than merely
        /// being present: a port that decorated at every level instead of only the outermost would
        /// still satisfy a containment assertion and would fail a count.
        /// </remarks>
        private static int OccurrenceCount(string text, string value)
        {
            Assert.NotEmpty(value);

            int count = 0;

            for (int index = text.IndexOf(value, StringComparison.Ordinal);
                index >= 0;
                index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// The decoration is four labelled lines in the oracle's order, followed by the detail block
        /// indented six spaces under the fourth label.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asserted line by line against the hook-captured block, which is the block BEFORE the
        /// outermost class-name prefix is applied (<c>n_cst_eventful.sru:L886</c> precedes
        /// <c>:L899</c>), so the four lines are the whole of it and their number is pinned as well as
        /// their content.
        /// </para>
        /// <para>
        /// <b>The whitespace asserted here was verified twice</b>, against <c>:L883-L886</c> for the
        /// space after each colon and against the aligner at <c>:L1252-L1274</c> for the indent. Two
        /// details are easy to lose and both are pinned: the fourth label ends
        /// <c>"Exception: ~n"</c>, so its line has a TRAILING SPACE before the break; and because that
        /// label ends in a newline the aligner indents the FIRST detail line too
        /// (<c>:L1256-L1258</c>), which is why the bracketed type name is indented rather than sitting
        /// flush after the label.
        /// </para>
        /// <para>
        /// The indent width is not a literal choice: <c>Fill(" ", Len(align) / 2)</c> over a
        /// twelve-character label is six spaces, and <see cref="DetailIndent"/> records that
        /// derivation.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheDecorationIsFourLabelledLinesFollowedByTheIndentedDetail()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException failure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic));

            DispatchRecord hookRow = Assert.Single(ExceptionHookRows(log));

            Assert.Collection(
                BlockLines(HookCapturedText(hookRow)),

                // :L883 - the dispatched event name, which the loop proved equal to the matched
                // subscription's own name at :L824.
                line => Assert.Equal(string.Concat(SubscribeLine, SoleTopic), line, ignoreCase: false),

                // :L884 - the target's class chain. A single link here, because the subscriber double
                // is a top-level type; the multi-link case has its own test elsewhere.
                line => Assert.Equal(
                    string.Concat(TargetLine, nameof(RecordingSubscriber)),
                    line,
                    ignoreCase: false),

                // :L885 - the handler event name, LOWER-CASED at subscription time (:L336). The next
                // test isolates that folding.
                line => Assert.Equal(
                    string.Concat(EventLine, RecordingHandlerNames.NoArguments.ToLowerInvariant()),
                    line,
                    ignoreCase: false),

                // :L886 - the fourth label, alone on its line and keeping its trailing space.
                line => Assert.Equal(ExceptionLine, line, ignoreCase: false),

                // :L878 then :L881 - the bracketed type name, indented because the label ends in a
                // newline.
                line => Assert.Equal(
                    string.Concat(DetailIndent, BracketedTypeName(failure)),
                    line,
                    ignoreCase: false),

                // :L881 again - every continuation line of the detail carries the same indent.
                line => Assert.Equal(
                    string.Concat(DetailIndent, FirstFailureMessage),
                    line,
                    ignoreCase: false));
        }

        /// <summary>
        /// The handler event name in the decoration is lower-cased, because that is how the
        /// subscription stored it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>of_on</c> folds it on the way in - <c>newEvent.evtName = Lower(evtName)</c> at
        /// <c>n_cst_eventful.sru:L336</c> - and the decoration reads the stored value back at
        /// <c>:L885</c>. The port folds with the invariant culture, so the result does not vary with
        /// the ambient locale.
        /// </para>
        /// <para>
        /// The declared handler name is asserted to differ from its folded form first. Without that,
        /// a handler whose name happened to be all lower case would let this test pass under a port
        /// that had dropped the folding entirely.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheHandlerNameInTheDecorationIsLowerCased()
        {
            // The premise: the declared name genuinely has capitals to lose.
            Assert.NotEqual(
                RecordingHandlerNames.NoArguments,
                RecordingHandlerNames.NoArguments.ToLowerInvariant(),
                StringComparer.Ordinal);

            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);
            failing.ThrowMessageOn(RecordingHandlerNames.NoArguments, FirstFailureMessage);

            Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic));

            string text = HookCapturedText(Assert.Single(ExceptionHookRows(log)));

            // The folded spelling is present...
            Assert.Contains(
                string.Concat(EventLine, RecordingHandlerNames.NoArguments.ToLowerInvariant()),
                text,
                StringComparison.Ordinal);

            // ...and the declared spelling is not, so the folding is proved rather than assumed.
            Assert.DoesNotContain(
                string.Concat(EventLine, RecordingHandlerNames.NoArguments),
                text,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// The two captured blocks of a chain that produced exactly two captures, with the instances
        /// that produced them.
        /// </summary>
        private sealed class TwoCaptureChain
        {
            /// <summary>Initializes a new instance of the <see cref="TwoCaptureChain"/> class.</summary>
            /// <param name="firstCapture">The block the first capture produced.</param>
            /// <param name="secondCapture">The block the second capture produced.</param>
            /// <param name="firstFailure">The instance the first capture carried.</param>
            /// <param name="secondFailure">The instance the second capture carried.</param>
            internal TwoCaptureChain(
                string firstCapture,
                string secondCapture,
                Exception firstFailure,
                Exception secondFailure)
            {
                FirstCapture = firstCapture;
                SecondCapture = secondCapture;
                FirstFailure = firstFailure;
                SecondFailure = secondFailure;
            }

            /// <summary>Gets the block the first capture produced.</summary>
            internal string FirstCapture { get; }

            /// <summary>Gets the block the second capture produced.</summary>
            internal string SecondCapture { get; }

            /// <summary>Gets the instance the first capture carried.</summary>
            internal Exception FirstFailure { get; }

            /// <summary>Gets the instance the second capture carried.</summary>
            internal Exception SecondFailure { get; }
        }

        /// <summary>
        /// Runs a chain that captures exactly twice, either with the latch cleared between the two
        /// captures or with it left standing.
        /// </summary>
        /// <param name="clearTheLatchWithAContinueAnswer">
        /// <see langword="true"/> to clear it, by answering continue at one level;
        /// <see langword="false"/> to leave it standing, by nesting so that the depth never returns to
        /// zero between the two captures.
        /// </param>
        /// <returns>The two blocks and the two instances.</returns>
        /// <remarks>
        /// <para>
        /// <b>There are exactly two ways to capture twice inside one chain, and this helper is both of
        /// them.</b> A continue answer at one level clears the latch and moves to the next subscriber
        /// (<c>n_cst_eventful.sru:L893-L894</c>); anything else either leaves the loop or throws, so a
        /// second capture at the SAME level is unreachable without it. Nesting is the other way: the
        /// inner level's unwind does not clear the latch, because <c>:L959-L960</c> clears it only at
        /// depth zero, so a second capture at the enclosing level meets a latch that is still set.
        /// </para>
        /// <para>
        /// The two shapes therefore differ in more than one respect - one is nested and one is not -
        /// and that is unavoidable, because the shape IS the mechanism. What makes the comparison
        /// sound is that both produce exactly two captures with no escape, and that the FIRST capture
        /// is bracketed in both: the difference the theory asserts is confined to the second.
        /// </para>
        /// </remarks>
        private static TwoCaptureChain RunTwoCaptureChain(bool clearTheLatchWithAContinueAnswer)
        {
            DispatchLog log = new();
            InvalidOperationException firstFailure = new(FirstFailureMessage);
            InvalidOperationException secondFailure = new(SecondFailureMessage);

            TestEventBroker broker = new(log, BrokerLabel)
            {
                // Continue to clear the latch and carry on; prevent to swallow at each level while
                // leaving the latch standing.
                ExceptionResult = clearTheLatchWithAContinueAnswer
                    ? EventBroker.ExceptionResultContinue
                    : EventBroker.ExceptionResultPrevent
            };

            if (clearTheLatchWithAContinueAnswer)
            {
                // ONE level, two failing subscribers. The continue answer bridges them.
                RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);
                RecordingSubscriber alsoFailing = SubscribeTo(broker, log, SoleTopic, SecondLabel);
                failing.ThrowOn(RecordingHandlerNames.NoArguments, firstFailure);
                alsoFailing.ThrowOn(RecordingHandlerNames.NoArguments, secondFailure);

                broker.Trigger(SoleTopic);
            }
            else
            {
                // TWO levels. The outer subscriber first re-enters the broker - whose inner
                // subscriber fails and is swallowed - and then fails itself, so the second capture
                // happens at depth one with the latch still set.
                RecordingSubscriber outer = SubscribeTo(broker, log, OuterTopic, OuterLabel);
                RecordingSubscriber inner = SubscribeTo(broker, log, InnerTopic, InnerLabel);

                inner.ThrowOn(RecordingHandlerNames.NoArguments, firstFailure);
                outer.ThrowOn(RecordingHandlerNames.NoArguments, secondFailure);

                // The double runs this BEFORE it throws, so the inner capture is genuinely first.
                WhileHandling(outer, () => broker.Trigger(InnerTopic));

                broker.Trigger(OuterTopic);
            }

            // Both shapes swallow at every level, so reaching here is itself the no-escape assertion.
            List<DispatchRecord> rows = ExceptionHookRows(log);
            Assert.Equal(2, rows.Count);

            return new TwoCaptureChain(
                HookCapturedText(rows[0]),
                HookCapturedText(rows[1]),
                firstFailure,
                secondFailure);
        }

        /// <summary>
        /// The two ways a chain can capture twice, and whether the second capture is decorated as a
        /// first failure.
        /// </summary>
        public static TheoryData<bool, bool> SecondCaptureBracketRows =>
            new()
            {
                // CLEARED. :L893 sets the latch false before :L894 continues, so the second capture
                // meets a clear latch and :L878 applies again.
                { true, true },

                // LEFT STANDING. The inner level's unwind reaches :L959 with the depth back at one,
                // not zero, so the latch survives and :L871 skips the whole inner decoration.
                { false, false }
            };

        /// <summary>
        /// A second failure in one chain is decorated as a first failure only when the latch was
        /// cleared between the two.
        /// </summary>
        /// <param name="clearTheLatchWithAContinueAnswer">Which chain shape to run.</param>
        /// <param name="expectBracketedTypeName">
        /// Whether the second block should carry its bracketed type name.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>The difference between the two rows is the whole of the latch's contract.</b> The first
        /// block is bracketed in both rows, which anchors the divergence to the second block and rules
        /// out an arrangement in which the first capture was somehow different. The second block
        /// carries its own message in both rows, which rules out the reading that the un-bracketed row
        /// simply lost its detail.
        /// </para>
        /// <para>
        /// A port that cleared the latch on every unwind rather than only at depth zero would pass the
        /// first row and fail the second. A port that never cleared it on a continue answer would pass
        /// the second and fail the first. Neither row alone is sufficient.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(SecondCaptureBracketRows))]
        public void ASecondFailureIsBracketedOnlyWhenTheLatchWasClearedBetweenTheCaptures(
            bool clearTheLatchWithAContinueAnswer,
            bool expectBracketedTypeName)
        {
            TwoCaptureChain chain = RunTwoCaptureChain(clearTheLatchWithAContinueAnswer);

            // Shared by both rows: the FIRST capture of a chain is always bracketed, because the
            // latch starts clear (:L871 then :L878).
            Assert.Contains(
                string.Concat(DetailIndent, BracketedTypeName(chain.FirstFailure)),
                chain.FirstCapture,
                StringComparison.Ordinal);

            // Shared by both rows: the second capture always carries its own detail, so its
            // decoration was produced and only the PREFIX is in question.
            Assert.Contains(SecondFailureMessage, chain.SecondCapture, StringComparison.Ordinal);

            // THE DIVERGENCE.
            if (expectBracketedTypeName)
            {
                Assert.Contains(
                    string.Concat(DetailIndent, BracketedTypeName(chain.SecondFailure)),
                    chain.SecondCapture,
                    StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(
                    BracketedTypeName(chain.SecondFailure),
                    chain.SecondCapture,
                    StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// An exception carrying assertion detail is unpacked into its information field, a stack-trace
        /// label and its frames, instead of being prefixed with its bracketed type name.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The one special-cased shape, and the C-K substitution it rests on.</b> The oracle tests
        /// <c>ClassName(ex) = "assertionfailed"</c> and, when it matches, composes
        /// <c>f.#Info + "~nStackTrace:~n" + f.#StackTraceInfo</c>
        /// (<c>n_cst_eventful.sru:L873-L876</c>) INSTEAD of the bracketed form at <c>:L878</c>. The two
        /// arms are exclusive, so the absence of the bracket is as much the assertion as the presence
        /// of the two fields.
        /// </para>
        /// <para>
        /// <b>The legacy assertion type belongs to the Diagnostics library, and this project does not
        /// reference it.</b> <c>assertionfailed.sru</c> ports to
        /// <c>shared/PowerFramework.Shared.Diagnostics/AssertionFailure.cs</c>, while this test project
        /// holds exactly one project reference - to the library under test - and adding a second is
        /// barred. The implementation anticipated that with <see cref="IAssertionDetail"/>, a
        /// two-string contract any exception may satisfy, so this test supplies a local carrier and
        /// asserts THE DECORATION rather than a diagnostics type. The abstraction expresses the case
        /// fully; nothing had to be reported as unreachable.
        /// </para>
        /// <para>
        /// The carrier's own message is asserted ABSENT. The assertion arm REPLACES the detail rather
        /// than prefixing it, so a port that appended the two fields to the message instead of
        /// substituting for it would still contain both fields and would fail here.
        /// </para>
        /// </remarks>
        [Fact]
        public void AnAssertionDetailCarrierIsUnpackedInsteadOfBracketed()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            AssertionDetailCarrier carrier = new();
            failing.ThrowOn(RecordingHandlerNames.NoArguments, carrier);

            // The type is unchanged by the special case: the same instance still propagates.
            Assert.Same(
                carrier,
                Assert.Throws<AssertionDetailCarrier>(() => broker.Trigger(SoleTopic)));

            string text = HookCapturedText(Assert.Single(ExceptionHookRows(log)));

            Assert.Collection(
                BlockLines(text),
                line => Assert.Equal(string.Concat(SubscribeLine, SoleTopic), line, ignoreCase: false),
                line => Assert.Equal(
                    string.Concat(TargetLine, nameof(RecordingSubscriber)),
                    line,
                    ignoreCase: false),
                line => Assert.Equal(
                    string.Concat(EventLine, RecordingHandlerNames.NoArguments.ToLowerInvariant()),
                    line,
                    ignoreCase: false),
                line => Assert.Equal(ExceptionLine, line, ignoreCase: false),

                // :L876, first field - the information field, indented like any detail line.
                line => Assert.Equal(string.Concat(DetailIndent, AssertionInfo), line, ignoreCase: false),

                // :L876, the separator label between the two fields.
                line => Assert.Equal(
                    string.Concat(DetailIndent, StackTraceLine),
                    line,
                    ignoreCase: false),

                // :L876, second field - the rendered frames.
                line => Assert.Equal(
                    string.Concat(DetailIndent, AssertionFrames),
                    line,
                    ignoreCase: false));

            // The two arms are exclusive: the bracketed form of :L878 was NOT applied...
            Assert.DoesNotContain(BracketedTypeName(carrier), text, StringComparison.Ordinal);

            // ...and the carrier's own message was replaced rather than decorated.
            Assert.DoesNotContain(AssertionCarrierMessage, text, StringComparison.Ordinal);
            Assert.Equal(AssertionCarrierMessage, carrier.Message);
        }

        /// <summary>
        /// The broker's own class name is prefixed once, by the outermost dispatch only, and a nested
        /// level adds none - while each level still adds its own four-line block.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Two mechanisms meet here and both are asserted by COUNT rather than by containment.</b>
        /// The class-name prefix is guarded by <c>if nDeep = 0</c> (<c>n_cst_eventful.sru:L899</c>),
        /// where <c>nDeep</c> is the depth SAVED on entry - so it is zero only for the outermost
        /// dispatch. The bracketed type name is guarded by the latch (<c>:L871</c>), which the inner
        /// level set and the inner level's unwind did not clear. A port that prefixed at every level,
        /// or that re-bracketed at the enclosing level, would still satisfy a containment assertion
        /// and fails a count of one.
        /// </para>
        /// <para>
        /// <b>The four-line block, by contrast, IS applied at every level</b>, because <c>:L883-L886</c>
        /// sits outside the latch. That is what produces the progressively indented nesting the oracle
        /// emits: the enclosing level reads the inner level's block back as its own detail and runs it
        /// through the aligner, so the inner block appears indented six spaces and the innermost detail
        /// twelve. Both indents are asserted.
        /// </para>
        /// <para>
        /// The hook's view is asserted too, and it is the only way to see the un-prefixed state of the
        /// OUTERMOST block: the prefix is applied after the hook has answered (<c>:L899-L901</c>
        /// follows <c>:L889</c>), so the hook sees the block without it at every level, including the
        /// outermost.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheBrokerClassNameIsPrefixedOnceByTheOutermostDispatchOnly()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);

            RecordingSubscriber outer = SubscribeTo(broker, log, OuterTopic, OuterLabel);
            RecordingSubscriber inner = SubscribeTo(broker, log, InnerTopic, InnerLabel);

            InvalidOperationException failure = new(InnerFailureMessage);
            inner.ThrowOn(RecordingHandlerNames.NoArguments, failure);
            WhileHandling(outer, () => broker.Trigger(InnerTopic));

            // The default answer rethrows, so the failure climbs out through both levels.
            Assert.Same(
                failure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(OuterTopic)));

            Assert.Collection(
                ExceptionHookRows(log),
                innerRow =>
                {
                    // The INNER level, at depth one. Its hook sees its own block, un-prefixed.
                    Assert.Equal(InnerTopic, innerRow.Topic);

                    string innerText = HookCapturedText(innerRow);
                    Assert.StartsWith(
                        string.Concat(SubscribeLine, InnerTopic),
                        innerText,
                        StringComparison.Ordinal);

                    Assert.DoesNotContain(
                        nameof(TestEventBroker),
                        innerText,
                        StringComparison.Ordinal);
                },
                outerRow =>
                {
                    // The ENCLOSING level. Its hook fires with the inner block already folded into
                    // its detail, and still with no prefix - :L899 comes after :L889.
                    Assert.Equal(OuterTopic, outerRow.Topic);

                    string outerText = HookCapturedText(outerRow);
                    Assert.StartsWith(
                        string.Concat(SubscribeLine, OuterTopic),
                        outerText,
                        StringComparison.Ordinal);

                    Assert.DoesNotContain(
                        nameof(TestEventBroker),
                        outerText,
                        StringComparison.Ordinal);

                    // The inner level's block, indented under this level's exception label.
                    Assert.Contains(
                        string.Concat(DetailIndent, SubscribeLine, InnerTopic),
                        outerText,
                        StringComparison.Ordinal);
                });

            // What finally reaches the caller.
            string finalText = RecordedText(failure);

            // :L900 - prefixed, and prefixed exactly ONCE, by the outermost level alone.
            Assert.StartsWith(
                string.Concat(nameof(TestEventBroker), "\n"),
                finalText,
                StringComparison.Ordinal);

            Assert.Equal(1, OccurrenceCount(finalText, nameof(TestEventBroker)));

            // :L871 - one bracketed type name for the whole chain, however many levels decorated.
            Assert.Equal(1, OccurrenceCount(finalText, BracketedTypeName(failure)));

            // :L883-L886 at BOTH levels, so the block nests and the indent compounds.
            Assert.Equal(2, OccurrenceCount(finalText, SubscribeLine));
            Assert.Contains(
                string.Concat(DetailIndent, SubscribeLine, InnerTopic),
                finalText,
                StringComparison.Ordinal);

            Assert.Contains(
                string.Concat(DetailIndent, DetailIndent, BracketedTypeName(failure)),
                finalText,
                StringComparison.Ordinal);

            Assert.Contains(
                string.Concat(DetailIndent, DetailIndent, InnerFailureMessage),
                finalText,
                StringComparison.Ordinal);
        }

        // =========================================================================================
        //  5. NESTING, AND WHAT A FAILURE MUST NOT DISTURB
        //
        //  A capture is not an isolated event: it happens inside a dispatch that has state, possibly
        //  inside an enclosing dispatch that has more, and possibly inside a drained posted
        //  continuation. This section pins what survives it.
        // =========================================================================================

        /// <summary>
        /// A failure raised inside a nested dispatch climbs out through both levels, stops the
        /// enclosing level too, and leaves the latch clear once the outermost dispatch has unwound.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The exception passes through TWO catch blocks, so the hook fires once per level
        /// (<c>n_cst_eventful.sru:L889</c> is inside the per-subscriber try) and the enclosing level's
        /// loop is left by the throw rather than by a veto - which is why the subscriber after the
        /// re-entrant one never runs.
        /// </para>
        /// <para>
        /// The follow-up dispatch is the point of the test: a fresh top-level failure afterwards is
        /// decorated as a FIRST failure, so the latch was cleared on the way out even though the
        /// unwind happened along an exception path. That is <c>:L959-L960</c> running from the outer
        /// <see langword="finally"/>, and it is what stops one failure from suppressing the diagnostics
        /// of every later one for the lifetime of the broker.
        /// </para>
        /// </remarks>
        [Fact]
        public void ANestedFailureClimbsOutThroughBothLevelsAndTheLatchIsClearAfterwards()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);

            RecordingSubscriber outer = SubscribeTo(broker, log, OuterTopic, OuterLabel);
            RecordingSubscriber afterOuter = SubscribeTo(broker, log, OuterTopic, ThirdLabel);
            RecordingSubscriber inner = SubscribeTo(broker, log, InnerTopic, InnerLabel);
            RecordingSubscriber followUp = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException nestedFailure = new(InnerFailureMessage);
            InvalidOperationException followUpFailure = new(SecondFailureMessage);
            inner.ThrowOn(RecordingHandlerNames.NoArguments, nestedFailure);
            followUp.ThrowOn(RecordingHandlerNames.NoArguments, followUpFailure);
            WhileHandling(outer, () => broker.Trigger(InnerTopic));

            Assert.Same(
                nestedFailure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(OuterTopic)));

            // Both levels ran their whole hook sequence, and the exception hook fired at each of them.
            Assert.Equal(
                new[]
                {
                    TestEventBroker.TriggeringHookName,
                    TestEventBroker.PrepareHookName,
                    RecordingHandlerNames.NoArguments,
                    TestEventBroker.TriggeringHookName,
                    TestEventBroker.PrepareHookName,
                    RecordingHandlerNames.NoArguments,
                    TestEventBroker.ExceptionHookName,
                    TestEventBroker.TriggeredHookName,
                    TestEventBroker.ExceptionHookName,
                    TestEventBroker.TriggeredHookName
                },
                log.HandlerNames,
                StringComparer.Ordinal);

            // The enclosing loop was left by the throw, so the subscriber after the re-entrant one
            // never ran.
            Assert.Equal(0, afterOuter.InvocationCount);
            Assert.Equal(1, outer.InvocationCount);
            Assert.Equal(1, inner.InvocationCount);

            // A FRESH top-level dispatch now: the latch must be clear again.
            Assert.Same(
                followUpFailure,
                Assert.Throws<InvalidOperationException>(() => broker.Trigger(SoleTopic)));

            List<DispatchRecord> rows = ExceptionHookRows(log);
            Assert.Equal(3, rows.Count);

            // Decorated as a first failure - :L959-L960 ran during the exceptional unwind.
            Assert.Contains(
                string.Concat(DetailIndent, BracketedTypeName(followUpFailure)),
                HookCapturedText(rows[2]),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// A failure does not disturb the handled state or the accumulated return value an earlier
        /// subscriber contributed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The accumulated value is written only when a subscriber returns a non-null value that the
        /// handled detection accepts (<c>n_cst_eventful.sru:L909-L924</c>), and a throwing subscriber
        /// returns nothing at all - the local holding its value is nulled at the top of the try and is
        /// still null when the catch runs, so the <c>if Not IsNull(aVal)</c> guard at <c>:L909</c>
        /// skips the whole block. The earlier contribution therefore stands, and a later subscriber
        /// reached by a continue answer still observes it.
        /// </para>
        /// <para>
        /// <b>Every subscriber here claims capture-everything, and that is load-bearing.</b> Once the
        /// first one has handled the event, an unhandled-only subscriber would be skipped by the
        /// capture filter at <c>:L831-L833</c> - and the test would then credit the exception for a
        /// skip the filter had already performed.
        /// </para>
        /// <para>
        /// The observation is made from INSIDE the dispatch, because both quantities are scoped to the
        /// level that is running and are restored on the way out (<c>:L815-L816</c> and <c>:L951</c>).
        /// What <see cref="EventBroker.Trigger"/> returns is a different quantity - the last
        /// invocation's raw value (<c>:L973</c>) - and it is null here because the observing subscriber
        /// returns nothing.
        /// </para>
        /// </remarks>
        [Fact]
        public void AFailureDoesNotDisturbAnEarlierSubscribersHandledStateOrAccumulatedValue()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel)
            {
                ExceptionResult = EventBroker.ExceptionResultContinue
            };

            RecordingSubscriber handling =
                SubscribeCapturingEverythingTo(broker, log, SoleTopic, FirstLabel);
            RecordingSubscriber failing =
                SubscribeCapturingEverythingTo(broker, log, SoleTopic, SecondLabel);
            RecordingSubscriber observing =
                SubscribeCapturingEverythingTo(broker, log, SoleTopic, ThirdLabel);

            handling.SetReturnValue(RecordingHandlerNames.NoArguments, HandledReturnValue);
            failing.ThrowMessageOn(RecordingHandlerNames.NoArguments, FirstFailureMessage);

            object? observedValue = null;
            bool observedProcessed = false;

            WhileHandling(
                observing,
                () =>
                {
                    observedValue = broker.GetReturnValue();
                    observedProcessed = broker.IsProcessed();
                });

            object? triggerResult = broker.Trigger(SoleTopic);

            // All three ran, and the failure was captured between the first and the third.
            Assert.Equal(1, handling.InvocationCount);
            Assert.Equal(1, failing.InvocationCount);
            Assert.Equal(1, observing.InvocationCount);
            Assert.Single(ExceptionHookRows(log));

            // The earlier contribution survived the capture intact, boxed type included - a long, not
            // an int, which is the distinction the handled comparison depends on.
            Assert.Equal(HandledReturnValue, Assert.IsType<long>(observedValue));
            Assert.True(observedProcessed);

            // :L973 - Trigger answers the LAST invocation's raw value, and the observer returned
            // nothing, so this is null. A different quantity from the accumulated value above.
            Assert.Null(triggerResult);
        }

        /// <summary>
        /// A failure inside a drained posted dispatch routes to the exception hook exactly as a
        /// triggered one does, and propagates to the caller that drained it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The capture path does not consult the post flag anywhere - <c>n_cst_eventful.sru:L867-L902</c>
        /// never reads <c>isPost</c> - so a posted dispatch's failure must produce the same routing and
        /// the same decoration. That is asserted rather than assumed, because the two dispatch entry
        /// points are separate members in the port and a difference between them would be invisible
        /// from either one alone.
        /// </para>
        /// <para>
        /// <b>Queue semantics are NOT re-asserted here.</b> That a post defers, that the queue is
        /// FIFO, that the payload is snapshotted and that a continuation may itself post all belong to
        /// <c>PostQueueTests</c>. This test uses the queue only as the vehicle: it checks that nothing
        /// has run before the drain, drains once, and then asserts the capture. The one queue-adjacent
        /// fact it does pin is the consequence of the failure escaping - the continuation had already
        /// been dequeued, so nothing is left behind to retry.
        /// </para>
        /// <para>
        /// The triggering hook's recorded argument confirms the dispatch genuinely ran as a POST, so
        /// the arrangement cannot be mistaken for an ordinary trigger that happened to be late.
        /// </para>
        /// </remarks>
        [Fact]
        public void AFailureInsideADrainedPostRoutesToTheHookTheSameWay()
        {
            DispatchLog log = new();
            TestEventBroker broker = new(log, BrokerLabel);
            RecordingSubscriber failing = SubscribeTo(broker, log, SoleTopic, FirstLabel);

            InvalidOperationException failure = new(FirstFailureMessage);
            failing.ThrowOn(RecordingHandlerNames.NoArguments, failure);

            broker.Post(SoleTopic);

            // Deferred, not dispatched: nothing has run and one continuation is waiting.
            Assert.Equal(0, failing.InvocationCount);
            Assert.Empty(ExceptionHookRows(log));
            Assert.Equal(1, broker.PendingPostedContinuationCount);

            // The drain runs it, and the failure reaches the DRAIN's caller.
            Assert.Same(
                failure,
                Assert.Throws<InvalidOperationException>(() => broker.DrainPostedContinuations()));

            // Dequeued before it ran, so nothing is left queued to run twice.
            Assert.Equal(0, broker.PendingPostedContinuationCount);

            // Routed identically: one hook invocation, the event name, the same instance.
            DispatchRecord hookRow = Assert.Single(ExceptionHookRows(log));
            Assert.Equal(SoleTopic, hookRow.Topic);
            Assert.Same(failure, hookRow.Arguments[0]);

            // Decorated identically, down to the indent and the bracketed type name.
            string text = HookCapturedText(hookRow);
            Assert.StartsWith(
                string.Concat(SubscribeLine, SoleTopic),
                text,
                StringComparison.Ordinal);

            Assert.Contains(
                string.Concat(DetailIndent, BracketedTypeName(failure)),
                text,
                StringComparison.Ordinal);

            // And prefixed at the outermost level, because a drained continuation dispatches at depth
            // zero just as a trigger does (:L899-L900).
            Assert.StartsWith(
                string.Concat(nameof(TestEventBroker), "\n"),
                RecordedText(failure),
                StringComparison.Ordinal);

            // The dispatch really was a post: the triggering hook recorded the flag it was handed.
            DispatchRecord triggeringRow = Assert.Single(
                log.Records,
                row => string.Equals(
                    row.HandlerName,
                    TestEventBroker.TriggeringHookName,
                    StringComparison.Ordinal));

            Assert.True(Assert.IsType<bool>(triggeringRow.Arguments[0]));
        }

        /// <summary>
        /// The three answers and whether each lets the exception escape, for the unwind assertion.
        /// </summary>
        public static TheoryData<long, bool> UnwindHookAnswerRows =>
            new()
            {
                { EventBroker.ExceptionResultPrevent, false },
                { EventBroker.ExceptionResultContinue, false },
                { TestEventBroker.ExceptionResultRethrow, true }
            };

        /// <summary>
        /// The triggered hook still fires, exactly once and last, whatever the exception hook answered
        /// - including when the exception was rethrown.
        /// </summary>
        /// <param name="hookAnswer">The value the exception hook answers.</param>
        /// <param name="expectRethrow">Whether the exception should reach the trigger's caller.</param>
        /// <remarks>
        /// <para>
        /// The triggered hook runs from the dispatch's outer <see langword="finally"/>
        /// (<c>n_cst_eventful.sru:L942-L946</c>), guarded only by the same "was anything dispatched"
        /// latch that guards the triggering hook, so it is reached however the loop ended. The prevent
        /// and continue rows are the easy half of that.
        /// </para>
        /// <para>
        /// <b>The rethrow row is the one worth pinning, and this is what the implementation does:</b>
        /// the hook fires on the way out even though an exception is in flight, because the
        /// <see langword="finally"/> runs before the exception leaves the frame. That is faithful to
        /// the oracle - <c>:L936</c> closes the inner try and the outer <see langword="finally"/> at
        /// <c>:L941-L969</c> follows the <c>catch(throwable ex2) / throw ex2</c> pair at
        /// <c>:L939-L940</c>, so the oracle reaches its own <c>ontriggered</c> on the throwing path
        /// too. A port that had skipped it there would leave the triggering and triggered pair
        /// asymmetric on exactly the path where a subclass most needs to release what its triggering
        /// hook acquired - the real subclass resets a synchronisation handle there
        /// (<c>n_cst_threading_eventful.sru:L68-L72</c>).
        /// </para>
        /// <para>
        /// "Exactly once and last" is asserted rather than "present", because a port that fired it
        /// from both the catch and the finally would satisfy a presence check.
        /// </para>
        /// </remarks>
        [Theory]
        [MemberData(nameof(UnwindHookAnswerRows))]
        public void TheTriggeredHookFiresOnTheUnwindWhateverTheExceptionHookAnswered(
            long hookAnswer,
            bool expectRethrow)
        {
            OutcomeRun run = RunThreeSubscriberArrangement(hookAnswer);

            // Once, and last.
            Assert.Single(
                run.Log.Records,
                row => string.Equals(
                    row.HandlerName,
                    TestEventBroker.TriggeredHookName,
                    StringComparison.Ordinal));

            IReadOnlyList<string> handlerNames = run.Log.HandlerNames;
            Assert.Equal(TestEventBroker.TriggeredHookName, handlerNames[^1]);

            // And the escape is still exactly what the answer implies, so this test cannot pass by
            // accidentally changing the outcome it is measuring the unwind of.
            if (expectRethrow)
            {
                Assert.Same(run.Failure, run.Escaped);
            }
            else
            {
                Assert.Null(run.Escaped);
            }
        }
    }
}
