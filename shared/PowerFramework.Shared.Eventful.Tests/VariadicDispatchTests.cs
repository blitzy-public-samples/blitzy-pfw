// =================================================================================================
//  VariadicDispatchTests - the ARGUMENT FORWARDING parity suite for PowerFramework.Shared.Eventful.
//
//  SUBJECT
//  -------
//  EventBroker.Trigger / EventBroker.Post and the private argument-passing routine behind them,
//  ported from ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru. Three properties are pinned
//  here and nowhere else:
//
//    1. WHAT THE CALLER SUPPLIES ARRIVES INTACT - nothing lost, duplicated, reordered, retyped or
//       truncated - at every arity, including the legacy ceiling and BEYOND it.
//    2. THE ASYMMETRIC ARITY RULE. The handler's declared parameter count, not the payload length,
//       decides what the handler sees: surplus caller arguments are SILENTLY DISCARDED and slots the
//       payload never reached hold their type's PowerScript INITIAL VALUE.
//    3. THE PREPARATION HOOK'S LEADING-ARGUMENT INJECTION, including the reported consumed count
//       shifting the caller's arguments, its two clamps, and its two skip guards.
//
//  THE ARITY FAMILIES, AND WHY THERE IS ONE VARIADIC METHOD INSTEAD OF ELEVEN (AAP 0.7.3 C-K)
//  ------------------------------------------------------------------------------------------
//  The oracle declares of_trigger ELEVEN times, taking zero through ten trailing arguments
//  (n_cst_eventful.sru:L119-L129), and declares of_post eleven times as well (:L132-L142). Every one
//  of the twenty-two definitions is a single-line delegation that packs its arguments into an array
//  literal and calls one private dispatch routine (:L233-L264), for instance :L237:
//
//      public function any of_trigger (readonly string name, readonly any param1)
//          return _of_Trigger(name,{param1},false)
//
//  THE SUBSTITUTION THIS SUITE COVERS: all eleven of_trigger arities collapse onto the single
//  `Trigger(string name, params object?[]? args)` and all eleven of_post arities onto the single
//  `Post(...)`, because C# expresses natively the variadic parameter list PowerScript cannot. TEN was
//  therefore a LEGACY LIMIT and never a contract - the author had to hand-write each arity and stopped
//  at ten. AAP 0.2.1.4 gives the refactor's other legacy arity ceilings exactly this treatment
//  (twenty in n_cst_thread_task_sqlbase, eight in n_cst_thread_trans, eleven in n_sqlite), recording
//  them as legacy limits the .NET contract MAY EXCEED without behavioural regression. That is why the
//  twelve-argument row of the arity matrix asserts SUCCESS rather than a rejection: a suite that
//  stopped at ten would silently endorse a limit the plan calls a non-requirement.
//
//  THE ARGUMENT RULE, FROM THE SOURCE (n_cst_eventful.sru:L605-L618)
//  ----------------------------------------------------------------
//      nArgCnt = invoker.GetArgCount()                                                     :L607
//      if _bSubclassing then
//          if IsPrevented(Event OnPrepare(name,target,invoker,nArgCnt,ref nArgIdx)) then    :L609
//              return false
//          if nArgIdx <= 0 then nArgIdx = 0                                                :L610
//      end if
//      nParmCnt = Min(nArgCnt - nArgIdx,UpperBound(params))                                :L613
//      nArgIdx++                                                                           :L614
//      invoker.SetArgs(nArgIdx,params,1,nParmCnt)                                          :L615
//      return true                                                                         :L617
//
//  The Min at :L613 is ONE expression carrying BOTH halves of the rule, which is exactly why a suite
//  that pins one half has not pinned the other: its first operand truncates the payload to the room
//  the handler has left, and its second stops at the payload's own end. The oracle states the same
//  rule in prose, with a worked example, at ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L240-L247
//  - :L240 says the callback's parameter count need not match of_Trigger's but the ORDER must match
//  and the types must be COMPATIBLE; :L242 declares `event ontest(string arg1,string arg2)`; :L246
//  triggers it with ONE argument and notes that arg2 receives '' , THE TYPE'S INITIAL VALUE; and :L247
//  triggers it with three and notes that the surplus arguments WILL BE DISCARDED.
//
//  LOCATOR NOTE, REPORTED RATHER THAN ACCOMMODATED: this file's brief cites that prose as
//  w_test_eventful.srw:L245-L250 and its worked example as :L249-L250. Read from the read-only tree,
//  :L249-L250 are the of_On subscription statements and carry no prose at all; the passage is
//  :L240-L247 with the two worked triggers on :L246 and :L247. The verified locators are used
//  throughout this file, which is also what EventBroker.PassArguments itself cites.
//
//  THE PREPARATION HOOK (n_cst_threading_eventful.sru:L48-L58)
//  ----------------------------------------------------------
//  The one evidenced real override in the corpus is four statements long:
//
//      if argCount < 1 or target = _source then return 0            :L54  - the TWO SKIP GUARDS
//      invoker.SetArg(1,_source)                                    :L55  - inject into slot ONE
//      argPassed = 1                                                :L56  - report ONE consumed
//      return 0                                                     :L57
//
//  Both guards at :L54 are contract, and they are the reason the reported consumed count must be
//  OBSERVABLE: with the count applied before :L613's Min, injecting one leading argument necessarily
//  drops one caller argument from the tail, and a port that accepted the injection but ignored the
//  count would deliver the injected value AND every caller argument, silently overrunning nothing and
//  therefore failing nothing. TestEventBroker reports the count verbatim - negative and oversized
//  values included - so the clamp at :L610 and the Min at :L613 can be observed at their boundaries.
//
//  WHY NO SCRIPT-INVOKER TYPE APPEARS ANYWHERE IN THIS FOLDER (AAP 0.7.3 C-D)
//  -------------------------------------------------------------------------
//  The oracle's onprepare signature carries an n_scriptinvoker parameter (n_cst_eventful.sru:L33) and
//  _of_passargs reaches the argument buffer entirely through it. That type is the legacy's
//  VARIADIC-CALL ESCAPE HATCH: because PowerScript cannot forward an arbitrary-length argument list,
//  the framework unrolls calls positionally and falls back to dynamic invocation only past the unrolled
//  maximum (AAP 0.2.1.4). C# has native variadic support, SO THE WORKAROUND HAS NO ANALOGUE TO PORT,
//  and the type stays with the deferred ScriptBridge service, which is out of this phase's scope
//  entirely. Its two genuinely used capabilities - read the declared argument count, write a leading
//  slot and report how many were written - live on EventArgumentContext instead. The absence is
//  therefore deliberate and complete, and the class-level remarks below say so in the one place a
//  reader of this suite will look.
//
//  METHOD, AND THE PROHIBITIONS OBSERVED
//  -------------------------------------
//  Every matrix is a [Theory] with [MemberData] over TheoryData, so the asymmetry reads as a table.
//  Row types are scalars only: TheoryData<T> forwards a single row value through a params array, so a
//  single array-typed row value would splat into the parameter list - payloads are therefore BUILT
//  inside each test from a helper and never carried as a row.
//
//  Every test constructs a FRESH broker, fresh subscribers and a fresh log. Nothing here reads a
//  clock, a GUID or a random source, starts a thread, or touches a file, a socket or a database: the
//  whole dispatch path is synchronous and in-process, and Post's queue is drained explicitly.
//  Post's QUEUEING is not tested here - that belongs to PostQueueTests; the single Post test in this
//  file asserts only that a drained posted dispatch forwards the identical arguments Trigger does.
//  The whole-dispatch veto is not tested here either - that belongs to VetoSemanticsTests; what this
//  file pins is the per-subscriber reach of the preparation hook, and it asserts the contrast against
//  the whole-dispatch abort precisely so the two cannot be conflated.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Pins how <see cref="EventBroker"/> forwards a trigger's payload to a handler: the collapse of the
/// legacy's eleven arity overloads onto one variadic method, argument fidelity at and beyond the legacy
/// ten-argument ceiling, the asymmetric rule that lets the handler's declared arity decide what arrives,
/// and the preparation hook's leading-argument injection with its clamps and guards.
/// </summary>
/// <remarks>
/// <para>
/// <b>NO DEFERRED SCRIPT-INVOKER TYPE IS REFERENCED BY ANY TEST IN THIS FOLDER, AND THAT OMISSION IS
/// DELIBERATE (AAP 0.7.3 C-D).</b> The legacy reaches a handler's argument buffer through the
/// <c>n_scriptinvoker</c> parameter of its <c>onprepare</c> event
/// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L33</c>), and that type exists only
/// because PowerScript cannot forward an arbitrary-length argument list: the framework unrolls calls
/// positionally and falls back to dynamic invocation past the unrolled maximum. C# expresses the
/// variadic parameter list natively, so <b>the workaround has no analogue to port</b> - there is nothing
/// for a test to bind to. The type belongs to the deferred ScriptBridge service, which this phase does
/// not implement, not even as a stub. Its two genuinely used capabilities are carried by
/// <see cref="EventArgumentContext"/>. The absence is a finished decision, not an oversight, and no
/// reflective assertion is made to "prove" the type is missing: the genuine absence of any reference
/// is the discharge.
/// </para>
/// <para>
/// See the file header for the oracle locators behind every expectation, for the substitution of one
/// variadic method for the eleven arity families, for the reason the twelve-argument case must
/// SUCCEED, and for the citation divergence this suite reports rather than accommodates.
/// </para>
/// </remarks>
public class VariadicDispatchTests
{
    /// <summary>
    /// The event name every test in this file dispatches. A plain lower-case word with no leading
    /// symbol run, no priority prefix and no namespace suffix, so the topic grammar contributes nothing
    /// to what these assertions observe.
    /// </summary>
    private const string DispatchTopic = "variadic";

    /// <summary>
    /// The expectation token standing for the value an <see cref="EventBroker.OnPrepare"/> override
    /// injected into the leading slot.
    /// </summary>
    /// <remarks>
    /// Upper case so it cannot be confused with a payload token. The only payload token that differs
    /// from it by case alone is <c>'i'</c> (position nine), and no tokenized expectation in this file is
    /// longer than four slots, so the two never appear together. Comparison is by character value and is
    /// therefore case-sensitive regardless.
    /// </remarks>
    private const char InjectedArgumentToken = 'I';

    /// <summary>
    /// The expectation token standing for a declared slot the payload never reached, which for an
    /// <see cref="object"/> parameter holds <see langword="null"/>.
    /// </summary>
    private const char UnreachedSlotToken = '-';

    /// <summary>
    /// The first payload token, <c>'a'</c>, from which a token's payload position is computed.
    /// </summary>
    private const char FirstPayloadToken = 'a';

    /// <summary>
    /// Twelve distinct payload values, one per position, spanning the legacy ceiling and reaching two
    /// past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Distinct, and never repeated, so that a transposition is detectable.</b> Two equal values in a
    /// payload would let an implementation that swapped them still satisfy an element-by-element
    /// comparison, and an all-null payload would let one that dropped a slot satisfy it too. Neither
    /// failure mode can hide behind these values.
    /// </para>
    /// <para>
    /// Their initials run <c>a</c> through <c>l</c> in order, which is what makes the tokenized
    /// expectations of the injection theories readable: token <c>'a'</c> is position one and its value is
    /// <c>"alpha"</c>, token <c>'d'</c> is position four and its value is <c>"delta"</c>.
    /// </para>
    /// </remarks>
    private static readonly string[] DistinctArgumentValues =
    [
        "alpha",
        "bravo",
        "charlie",
        "delta",
        "echo",
        "foxtrot",
        "golf",
        "hotel",
        "india",
        "juliett",
        "kilo",
        "lima"
    ];

    /// <summary>
    /// Builds a payload of <paramref name="count"/> distinct values, in order.
    /// </summary>
    /// <param name="count">
    /// How many values to supply. Zero yields an empty payload, which is the zero-argument trigger.
    /// </param>
    /// <returns>A fresh array, so a test that asserts the caller's array was not mutated owns it.</returns>
    /// <remarks>
    /// Typed <c>object?[]</c> rather than <c>string[]</c> so it binds to the variadic parameter as ONE
    /// payload array. Passing a <c>string[]</c> would bind as a single argument whose value is an array,
    /// which is a different call and would make every count assertion read one.
    /// </remarks>
    private static object?[] DistinctPayload(int count)
    {
        Assert.InRange(count, 0, DistinctArgumentValues.Length);

        object?[] payload = new object?[count];

        for (int position = 0; position < count; position++)
        {
            payload[position] = DistinctArgumentValues[position];
        }

        return payload;
    }

    /// <summary>
    /// Maps a declared arity to the <see cref="RecordingSubscriber"/> handler that declares it.
    /// </summary>
    /// <param name="arity">One of the arities the recording double provides: 0, 1, 4, 10 or 12.</param>
    /// <returns>The handler's declared name, as a compile-checked constant.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when no handler declares that arity, which is a fault in the theory's rows rather than in
    /// the subject and must fail loudly instead of silently selecting a neighbour.
    /// </exception>
    /// <remarks>
    /// The five arities are the double's own choice and each one earns its place: zero is the extreme of
    /// the discard rule, one is the shape of the oracle's own <c>ontest(string arg)</c>, four is the
    /// middling case where a transposition is visible, TEN is the legacy ceiling at
    /// <c>n_cst_eventful.sru:L129</c>, and TWELVE is unambiguously past it.
    /// </remarks>
    private static string HandlerNameForArity(int arity) => arity switch
    {
        0 => RecordingHandlerNames.NoArguments,
        1 => RecordingHandlerNames.OneArgument,
        4 => RecordingHandlerNames.FourArguments,
        10 => RecordingHandlerNames.TenArguments,
        12 => RecordingHandlerNames.TwelveArguments,
        _ => throw new ArgumentOutOfRangeException(
            nameof(arity),
            arity,
            "No RecordingSubscriber handler declares that arity.")
    };

    /// <summary>
    /// Subscribes one <see cref="RecordingSubscriber"/> to <see cref="DispatchTopic"/> against the
    /// handler of the given declared arity, asserting the subscription succeeded.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The log the subscriber records into.</param>
    /// <param name="label">The label the subscriber writes into every row.</param>
    /// <param name="arity">The declared arity of the handler to bind.</param>
    /// <returns>The subscriber, so a caller can attach an in-handler callback to it.</returns>
    /// <remarks>
    /// The subscription result is asserted rather than discarded because a silently rejected topic would
    /// leave nothing subscribed, and every argument assertion downstream would then be measuring an
    /// empty dispatch instead of a forwarded payload.
    /// </remarks>
    private static RecordingSubscriber SubscribeArityHandler(
        EventBroker broker,
        DispatchLog log,
        string label,
        int arity)
    {
        RecordingSubscriber subscriber = new(log, label, broker);

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(DispatchTopic, subscriber, HandlerNameForArity(arity)));

        return subscriber;
    }

    /// <summary>
    /// Returns the single row a named handler recorded, failing when it ran no times or more than once.
    /// </summary>
    /// <param name="log">The log to read.</param>
    /// <param name="handlerName">The declared handler name to select on.</param>
    /// <returns>That handler's only row.</returns>
    /// <remarks>
    /// Selects by handler name rather than by index because a suite sharing one log between a
    /// <see cref="TestEventBroker"/> and its subscribers interleaves hook rows with handler rows.
    /// Matching is ordinal, since <see cref="DispatchRecord.HandlerName"/> carries the DECLARED spelling
    /// and is never case-folded.
    /// </remarks>
    private static DispatchRecord SingleRowFor(DispatchLog log, string handlerName) =>
        Assert.Single(
            log.Records,
            row => string.Equals(row.HandlerName, handlerName, StringComparison.Ordinal));

    /// <summary>
    /// Counts the rows a named hook recorded.
    /// </summary>
    /// <param name="log">The hook log to read.</param>
    /// <param name="hookName">The hook name, from <see cref="TestEventBroker"/>'s constants.</param>
    /// <returns>How many times that hook fired.</returns>
    private static int HookRowCount(DispatchLog log, string hookName) =>
        log.Records.Count(row => string.Equals(row.HandlerName, hookName, StringComparison.Ordinal));

    /// <summary>
    /// Asserts a received argument sequence against a compact per-slot expectation.
    /// </summary>
    /// <param name="expectation">
    /// One character per declared slot: <see cref="InjectedArgumentToken"/> for the injected value,
    /// <see cref="UnreachedSlotToken"/> for a slot the payload never reached, and a lower-case letter for
    /// the payload position it carries, counting from <see cref="FirstPayloadToken"/>. Its LENGTH is the
    /// expected declared arity.
    /// </param>
    /// <param name="payload">The payload the caller supplied.</param>
    /// <param name="injected">The value the preparation hook was configured to inject.</param>
    /// <param name="received">The arguments the handler actually received.</param>
    /// <remarks>
    /// Spelled out per row rather than computed from the payload and the consumed count, on purpose: a
    /// computed expectation would re-implement <c>_of_passargs</c>'s own arithmetic inside the test and
    /// would then agree with any implementation that made the same mistake. The injected value is
    /// asserted by REFERENCE, because injection must hand the handler the very instance the override
    /// wrote and not an equal copy.
    /// </remarks>
    private static void AssertSlots(
        string expectation,
        object?[] payload,
        object? injected,
        IReadOnlyList<object?> received)
    {
        Assert.Equal(expectation.Length, received.Count);

        for (int slot = 0; slot < expectation.Length; slot++)
        {
            char token = expectation[slot];

            if (token == InjectedArgumentToken)
            {
                Assert.Same(injected, received[slot]);
            }
            else if (token == UnreachedSlotToken)
            {
                Assert.Null(received[slot]);
            }
            else
            {
                Assert.Equal(payload[token - FirstPayloadToken], received[slot]);
            }
        }
    }

    // =============================================================================================
    //  THE ARITY MATRIX - the eleven-overload collapse, from zero arguments to two past the ceiling
    //  of_trigger declared n_cst_eventful.sru:L119-L129, defined :L233-L264
    // =============================================================================================

    /// <summary>
    /// The declared arities the payload matrix runs at: zero, one, the middling four, the LEGACY CEILING
    /// of ten, and twelve - two past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row supplies exactly as many arguments as the handler declares, so this matrix isolates
    /// FIDELITY from the asymmetric arity rule; the disagreeing cases are
    /// <see cref="AsymmetricArityRows"/>'s subject.
    /// </para>
    /// <para>
    /// <b>Ten is the legacy maximum</b> - <c>of_trigger</c>'s longest overload declares <c>param1</c>
    /// through <c>param10</c> at <c>n_cst_eventful.sru:L129</c>, and <c>of_post</c>'s longest does the
    /// same at <c>:L132</c>. <b>Twelve is deliberately beyond it and must SUCCEED</b>, because the
    /// ceiling existed only to work around PowerScript's lack of a variadic parameter list and AAP
    /// 0.2.1.4 records such ceilings as legacy limits the .NET contract may exceed without behavioural
    /// regression. Twelve rather than eleven so the row cannot be satisfied by an off-by-one at the
    /// boundary.
    /// </para>
    /// </remarks>
    public static TheoryData<int> ArityRows => new() { 0, 1, 4, 10, 12 };

    /// <summary>
    /// At every arity the handler receives exactly the arguments supplied, in order, with none lost,
    /// duplicated or reordered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// COVERS THE SUBSTITUTION (AAP 0.7.3 C-K). The oracle needed eleven <c>of_trigger</c> overloads to
    /// express this, each packing its arguments into an array literal and delegating to one private
    /// routine (<c>n_cst_eventful.sru:L233-L264</c>). One variadic method replaces all eleven, and this
    /// matrix is what says the replacement forwards identically at every arity the overloads covered
    /// AND at one they could not reach.
    /// </para>
    /// <para>
    /// The comparison is element by element and the count is asserted separately, because either
    /// assertion alone is satisfiable by a defect: a count-only assertion passes when two arguments are
    /// transposed, and an element-only assertion over the supplied length passes when a trailing
    /// argument is dropped. The values are twelve distinct words, so a transposition changes the
    /// sequence rather than merely permuting equals.
    /// </para>
    /// </remarks>
    /// <param name="arity">The handler's declared arity, and the number of arguments supplied.</param>
    [Theory]
    [MemberData(nameof(ArityRows))]
    public void EveryArityForwardsThePayloadCompleteAndInOrder(int arity)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", arity);

        object?[] payload = DistinctPayload(arity);

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, HandlerNameForArity(arity));

        // The declared arity IS the recorded slot count: the broker fills every declared slot and
        // discards anything past it (n_cst_eventful.sru:L613).
        Assert.Equal(arity, row.ArgumentCount);
        Assert.Equal(payload.Length, row.ArgumentCount);

        for (int position = 0; position < payload.Length; position++)
        {
            Assert.Equal(payload[position], row.Arguments[position]);
        }

        // The whole sequence in one comparison as well, so a dropped-and-shifted pair that happened to
        // satisfy every positional assertion could not also satisfy this one.
        Assert.Equal(payload, row.Arguments);
    }

    /// <summary>
    /// A trigger issued with NO argument list at all runs the zero-parameter handler and hands it an
    /// empty argument set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The port of <c>of_trigger(readonly string name)</c>, whose body builds an EMPTY array and
    /// delegates - <c>any emptyParams[]</c> then <c>return _of_Trigger(name,emptyParams,false)</c>
    /// (<c>n_cst_eventful.sru:L233-L234</c>). Written as a bare <c>Trigger(name)</c> call rather than as
    /// <c>Trigger(name, [])</c> so the ARGUMENT-OMITTED call form is exercised, which is the one an
    /// eleven-overload consumer used and the one a variadic signature must still accept.
    /// </para>
    /// <para>
    /// This is also the reason a null payload cannot be spelled as <c>Trigger(name, null)</c> anywhere in
    /// this file: with a variadic parameter that binds <see langword="null"/> to the WHOLE array, not to a
    /// single null argument. <see cref="EventBroker.Trigger"/> treats a null array as no payload, so the
    /// two forms agree here - and every test that means "one argument whose value is null" passes an
    /// explicit single-element array instead.
    /// </para>
    /// </remarks>
    [Fact]
    public void TriggeringWithNoArgumentListRunsTheZeroParameterHandlerWithAnEmptyArgumentSet()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 0);

        broker.Trigger(DispatchTopic);

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.NoArguments);

        Assert.Empty(row.Arguments);
    }

    /// <summary>
    /// A payload of TWELVE arguments - two past the legacy ceiling - is forwarded in full and throws
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ROW THAT CARRIES THE PLAN'S INTENT (AAP 0.7.3 C-K).</b> The legacy stopped at ten because
    /// its language could not go further: <c>of_trigger</c> is hand-written eleven times
    /// (<c>n_cst_eventful.sru:L119-L129</c>) precisely because PowerScript cannot forward an
    /// arbitrary-length argument list. AAP 0.2.1.4 records such arity ceilings as LEGACY LIMITS the .NET
    /// contract may exceed without behavioural regression, alongside the twenty in
    /// <c>n_cst_thread_task_sqlbase</c>, the eight in <c>n_cst_thread_trans</c> and the eleven in
    /// <c>n_sqlite</c>. So the correct assertion here is SUCCESS, and a suite that instead asserted a
    /// rejection - or that simply stopped at ten - would silently endorse a limit the plan calls a
    /// non-requirement.
    /// </para>
    /// <para>
    /// Asserted three ways because each rules out a different way of "succeeding" wrongly: nothing was
    /// thrown, every one of the twelve values arrived, and they arrived in order. The eleventh and
    /// twelfth positions are also checked by name so a failure message points straight at the boundary.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnArityAboveTheLegacyCeilingSucceedsAndForwardsEveryArgument()
    {
        const int aboveCeiling = 12;

        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", aboveCeiling);

        object?[] payload = DistinctPayload(aboveCeiling);

        // The legacy could not even express this call. Exceeding the ceiling is permitted, so the
        // assertion is that nothing was raised - not that something was.
        Assert.Null(Record.Exception(() => broker.Trigger(DispatchTopic, payload)));

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.TwelveArguments);

        Assert.Equal(aboveCeiling, row.ArgumentCount);
        Assert.Equal(payload, row.Arguments);

        // The two positions past the ceiling, named explicitly.
        Assert.Equal(DistinctArgumentValues[10], row.Arguments[10]);
        Assert.Equal(DistinctArgumentValues[11], row.Arguments[11]);
    }

    // =============================================================================================
    //  ARGUMENT FIDELITY - nulls in every position, types uncoerced, identity preserved, no mutation
    // =============================================================================================

    /// <summary>
    /// Every position at which a null may appear: first, interior, and TRAILING - the last at the
    /// single-argument arity, at the legacy ceiling, and above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The trailing rows are the load-bearing ones.</b> A trailing null that silently shortens the
    /// delivered sequence is a real and easy port defect - an implementation that trimmed trailing nulls,
    /// or that stopped copying at the first null, would satisfy every non-null matrix in this file and
    /// fail only here. Rows are supplied at three different arities so a trim cannot be mistaken for a
    /// property of one handler.
    /// </para>
    /// <para>
    /// The interior rows exist for the complementary defect: a copy loop that skipped nulls rather than
    /// writing them would SHIFT every argument after the null forward by one, which changes the sequence
    /// without changing its length.
    /// </para>
    /// </remarks>
    public static TheoryData<int, int> NullPositionRows => new()
    {
        // arity, the payload position replaced by null
        { 1, 0 },
        { 4, 0 },
        { 4, 1 },
        { 4, 2 },
        { 4, 3 },
        { 10, 0 },
        { 10, 5 },
        { 10, 9 },
        { 12, 11 }
    };

    /// <summary>
    /// A null argument arrives as a null IN ITS OWN POSITION, leaves every other position untouched, and
    /// does not truncate the delivered sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). PowerScript has null for value types and the
    /// framework's own predicates depend on it, which AAP 0.4.5.4 states as a translation hazard: null
    /// must never be collapsed to a zero or elided. An EXPLICITLY SUPPLIED null is therefore forwarded as
    /// null and is NOT replaced by the parameter type's initial value - that substitution belongs only to
    /// slots the payload never reached (<c>n_cst_eventful.sru:L613</c>'s <c>Min</c> decides which is
    /// which).
    /// </para>
    /// <para>
    /// Handlers of these three arities declare <see cref="object"/> parameters, whose initial value is
    /// ALSO null, so "the payload carried null here" and "the payload never reached here" are
    /// indistinguishable on them by design. That is why the surrounding positions are asserted too: the
    /// null is pinned by the fact that the sequence still has its full length and every OTHER position
    /// still carries its own distinct value.
    /// <see cref="AShortfallAgainstATypedHandlerFillsEachSlotWithItsInitialValue"/> is where the two are
    /// genuinely told apart, on a handler whose parameter types have non-null initial values.
    /// </para>
    /// </remarks>
    /// <param name="arity">The handler's declared arity, and the number of arguments supplied.</param>
    /// <param name="nullPosition">The payload position whose value is replaced by null.</param>
    [Theory]
    [MemberData(nameof(NullPositionRows))]
    public void ANullArgumentArrivesInItsOwnPositionAndDoesNotTruncateThePayload(
        int arity,
        int nullPosition)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", arity);

        object?[] payload = DistinctPayload(arity);
        payload[nullPosition] = null;

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, HandlerNameForArity(arity));

        // NOT TRUNCATED: the full declared arity still arrived, null included.
        Assert.Equal(arity, row.ArgumentCount);

        // The null is where it was put, and nowhere else.
        Assert.Null(row.Arguments[nullPosition]);

        for (int position = 0; position < arity; position++)
        {
            if (position == nullPosition)
            {
                continue;
            }

            Assert.Equal(DistinctArgumentValues[position], row.Arguments[position]);
        }
    }

    /// <summary>
    /// Builds a ten-value payload spanning ten distinct runtime types, one per position.
    /// </summary>
    /// <param name="reference">
    /// The reference-typed value for position seven, supplied by the caller so a test can assert its
    /// IDENTITY rather than merely its equality.
    /// </param>
    /// <returns>The payload, in the order <see cref="MixedTypePositionRows"/> describes it.</returns>
    /// <remarks>
    /// <para>
    /// Ten values so the ten-parameter handler is filled exactly, which keeps this matrix about TYPE
    /// FIDELITY and free of any shortfall or surplus. Every parameter of that handler is declared
    /// <see cref="object"/>, and the broker's coercion step passes a value straight through when the
    /// target is <see cref="object"/>, so whatever arrives is what the caller boxed - which is precisely
    /// the property being asserted.
    /// </para>
    /// <para>
    /// The date and time values are fixed literals - the framework version date from <c>logfile.md</c> -
    /// so nothing here reads a clock. No random source, no GUID and no culture-sensitive formatting is
    /// involved in constructing any of them.
    /// </para>
    /// </remarks>
    private static object?[] MixedTypePayload(object reference) =>
    [
        "text",
        42,
        true,
        12.34m,
        new DateOnly(2022, 4, 14),
        new DateTime(2022, 4, 14, 9, 30, 0, DateTimeKind.Utc),
        reference,
        9_000_000_000L,
        2.5d,
        'Z'
    ];

    /// <summary>
    /// The runtime type expected at each position of <see cref="MixedTypePayload"/>, keyed by a readable
    /// label so a failed row names the type rather than an index.
    /// </summary>
    public static TheoryData<int, string> MixedTypePositionRows => new()
    {
        // position, type label
        { 0, "string" },
        { 1, "int" },
        { 2, "bool" },
        { 3, "decimal" },
        { 4, "date" },
        { 5, "datetime" },
        { 6, "reference" },
        { 7, "long" },
        { 8, "double" },
        { 9, "char" }
    };

    /// <summary>
    /// Resolves a <see cref="MixedTypePositionRows"/> label to the type it names.
    /// </summary>
    /// <param name="label">The row's type label.</param>
    /// <returns>The expected runtime type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for an unrecognised label, which is a fault in the rows rather than in the subject.
    /// </exception>
    /// <remarks>
    /// A label rather than a type name string, because the reference type is nested and its full name
    /// would have to be spelled with a <c>+</c> separator in every row - a brittle literal that would
    /// silently stop matching if the class were ever renamed.
    /// </remarks>
    private static Type TypeForLabel(string label) => label switch
    {
        "string" => typeof(string),
        "int" => typeof(int),
        "bool" => typeof(bool),
        "decimal" => typeof(decimal),
        "date" => typeof(DateOnly),
        "datetime" => typeof(DateTime),
        "reference" => typeof(ReferencePayload),
        "long" => typeof(long),
        "double" => typeof(double),
        "char" => typeof(char),
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unrecognised type label.")
    };

    /// <summary>
    /// Each argument of a mixed-type payload arrives with its runtime type and its value intact, and in
    /// particular NOTHING is coerced to a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The oracle's payload element type is <c>any</c>,
    /// which AAP 0.4.5.2 maps to <c>object?</c>, and <c>_of_passargs</c> moves the values across without
    /// inspecting them (<c>n_cst_eventful.sru:L615</c>). The rule the oracle states is that the types
    /// must be COMPATIBLE (<c>w_test_eventful.srw:L240</c>) - not that they are normalised - so a port
    /// that stringified, rounded, widened or otherwise tidied a payload value on the way in would be
    /// changing behaviour, not preserving it.
    /// </para>
    /// <para>
    /// Asserting the exact runtime type is what rules stringification out: had any value been rendered on
    /// the way through, its type would read <see cref="string"/> and the row would fail. The value is
    /// asserted alongside the type because a type-only assertion passes when a number is replaced by a
    /// different number of the same type.
    /// </para>
    /// </remarks>
    /// <param name="position">The payload position under test.</param>
    /// <param name="label">The runtime type expected at that position.</param>
    [Theory]
    [MemberData(nameof(MixedTypePositionRows))]
    public void EveryMixedTypeArgumentArrivesWithItsTypeAndValueIntact(int position, string label)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        SubscribeArityHandler(broker, log, "subscriber", 10);

        ReferencePayload reference = new("reference");
        object?[] payload = MixedTypePayload(reference);

        broker.Trigger(DispatchTopic, payload);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.TenArguments);

        Assert.Equal(payload.Length, row.ArgumentCount);

        object? received = row.Arguments[position];

        Assert.NotNull(received);
        Assert.Equal(TypeForLabel(label), received.GetType());
        Assert.Equal(payload[position], received);
    }

    /// <summary>
    /// A reference argument arrives as the SAME instance, never as a copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). In PowerBuilder an object's identity IS its
    /// pointer, and the framework's own subscribers rely on that: the oracle's handler signature is
    /// <c>onbuttonclicked1(commandbutton, long, long)</c> (<c>w_test_eventful.srw:L44</c>), and a control
    /// forwarded by value rather than by reference would be a different control. A port that cloned,
    /// serialised or otherwise round-tripped a payload reference would break every handler that compares
    /// the argument it received against something it already holds.
    /// </para>
    /// <para>
    /// Asserted at the FIRST and the LAST position of the same dispatch, because the copy loop writes
    /// slots one at a time and a defect at the boundary would not show in the middle. Both are the same
    /// instance, which also confirms that supplying one reference twice does not de-duplicate it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AReferenceArgumentArrivesAsTheSameInstance()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        SubscribeArityHandler(broker, log, "subscriber", 4);

        ReferencePayload reference = new("shared-instance");
        object?[] payload = [reference, DistinctArgumentValues[1], DistinctArgumentValues[2], reference];

        broker.Trigger(DispatchTopic, payload);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.FourArguments);

        Assert.Same(reference, row.Arguments[0]);
        Assert.Same(reference, row.Arguments[3]);
    }

    /// <summary>
    /// Dispatch does not modify the caller's argument array, and dispatching the same array a second time
    /// delivers the same values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Load-bearing because the preparation hook writes into an argument buffer.</b>
    /// <see cref="EventArgumentContext.SetArgument"/> hands an override direct write access to the array
    /// the handler will be invoked with - the port of <c>invoker.SetArg</c>
    /// (<c>n_cst_threading_eventful.sru:L55</c>). Had the broker handed the CALLER'S array to the hook
    /// instead of a per-invocation buffer, an injection would overwrite the caller's own data and the
    /// second dispatch would deliver something different from the first. The oracle cannot have that
    /// defect - its payload parameter is <c>readonly any params[]</c>
    /// (<c>n_cst_eventful.sru:L605</c>) - so the port must not either.
    /// </para>
    /// <para>
    /// Both halves are asserted because they fail independently: a broker that copied defensively but
    /// consumed the copy destructively would pass the first and fail the second.
    /// </para>
    /// </remarks>
    [Fact]
    public void DispatchDoesNotMutateTheCallersArrayAndASecondDispatchDeliversTheSameValues()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        SubscribeArityHandler(broker, log, "subscriber", 4);

        object?[] payload = DistinctPayload(4);
        object?[] before = (object?[])payload.Clone();

        broker.Trigger(DispatchTopic, payload);

        // The caller's array is untouched, element for element.
        Assert.Equal(before, payload);

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(2, log.Count);
        Assert.Equal(before, log.ArgumentsAt(0));
        Assert.Equal(log.ArgumentsAt(0), log.ArgumentsAt(1));
    }

    /// <summary>
    /// Three subscribers on one topic all receive the SAME argument sequence: the payload is shared, not
    /// consumed by the first handler to read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The oracle passes the one <c>params</c> array to
    /// <c>_of_passargs</c> once per subscriber inside the dispatch loop
    /// (<c>n_cst_eventful.sru:L605</c>, <c>:L615</c>), so every subscriber sees the same values however
    /// many precede it. A port that moved out of the payload, enumerated it destructively, or reused one
    /// buffer that a handler could write through would deliver a full payload to the first subscriber and
    /// a degraded one to the rest - a defect invisible to any single-subscriber test.
    /// </para>
    /// <para>
    /// The reference element is asserted by identity across all three rows, which is the sharpest form of
    /// the claim: not merely equal sequences, but the very same instance reaching every subscriber.
    /// </para>
    /// </remarks>
    [Fact]
    public void EverySubscriberOnOneTopicReceivesTheSameArgumentSequence()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeArityHandler(broker, log, "first", 4);
        SubscribeArityHandler(broker, log, "second", 4);
        SubscribeArityHandler(broker, log, "third", 4);

        ReferencePayload reference = new("shared-instance");
        object?[] payload = [reference, DistinctArgumentValues[1], null, DistinctArgumentValues[3]];

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(3, log.Count);
        Assert.Equal(["first", "second", "third"], log.Labels);

        for (int index = 0; index < log.Count; index++)
        {
            Assert.Equal(payload, log.ArgumentsAt(index));
            Assert.Same(reference, log.ArgumentsAt(index)[0]);
        }
    }

    // =============================================================================================
    //  THE ASYMMETRIC ARITY RULE - _of_passargs, n_cst_eventful.sru:L605-L618
    //  Surplus caller arguments are SILENTLY DISCARDED; unreached slots hold the type's INITIAL VALUE.
    //  Stated in prose with two worked triggers at w_test_eventful.srw:L240-L247.
    // =============================================================================================

    /// <summary>
    /// The supplied-count against declared-arity matrix, with the expected delivered count spelled out per
    /// row so the ASYMMETRY reads as a table: surplus rows deliver fewer than were supplied, shortfall rows
    /// deliver fewer than were declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delivered count is DECLARED in each row rather than computed inside the test. Computing it as
    /// <c>Min(declared, supplied)</c> would re-implement <c>n_cst_eventful.sru:L613</c> inside the
    /// assertion, and a re-implemented expectation agrees with any subject that makes the same mistake.
    /// </para>
    /// <para>
    /// Every arm of the rule is covered at more than one arity: the discard at its extreme (a
    /// zero-parameter handler triggered with three arguments delivers NOTHING), at the oracle's own worked
    /// example (<c>:L247</c> - one declared, several supplied), in the middle, and AT THE LEGACY CEILING
    /// (ten declared, twelve supplied). The shortfall likewise runs from one missing argument to all
    /// twelve missing. Two exact rows are included as controls, one of them above the ceiling, so a
    /// subject that simply delivered nothing could not pass the table.
    /// </para>
    /// </remarks>
    public static TheoryData<int, int, int> AsymmetricArityRows => new()
    {
        // declared arity, supplied count, expected delivered count
        { 0, 3, 0 },
        { 1, 5, 1 },
        { 4, 10, 4 },
        { 10, 12, 10 },
        { 4, 4, 4 },
        { 12, 12, 12 },
        { 1, 0, 0 },
        { 4, 1, 1 },
        { 10, 4, 4 },
        { 12, 0, 0 }
    };

    /// <summary>
    /// The handler's DECLARED arity decides what arrives: surplus caller arguments are discarded
    /// SILENTLY - nothing is thrown, nothing is reported - and slots the payload never reached are left at
    /// their parameter type's initial value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Both arms come out of ONE expression,
    /// <c>nParmCnt = Min(nArgCnt - nArgIdx,UpperBound(params))</c> at
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L613</c>: its first operand truncates
    /// the payload to the room the handler has left, its second stops at the payload's own end, and
    /// <c>:L615</c> then copies exactly that many. The oracle states the same rule in prose with two
    /// worked triggers at <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L240-L247</c> - <c>:L246</c>
    /// triggers a two-parameter handler with ONE argument and notes the second parameter receives the
    /// type's initial value; <c>:L247</c> triggers it with three and notes the surplus WILL BE DISCARDED.
    /// </para>
    /// <para>
    /// <b>THIS IS ASSERTED AS CORRECT, AND DELIBERATELY NOT AS AN ERROR.</b> No exception, no failure code
    /// and no warning is asserted for a surplus argument, because the legacy drops it in silence and
    /// "improving" that into a diagnostic would be a behavioural change - exactly what C-B forbids. The
    /// absence of a throw is asserted positively, so a port that started validating the payload length
    /// would fail this test rather than pass it by accident.
    /// </para>
    /// <para>
    /// The unreached slots are asserted to be <see langword="null"/> because these handlers declare
    /// <see cref="object"/> parameters and <see langword="null"/> IS an object's PowerScript initial value.
    /// That coincidence is why it is not the whole story:
    /// <see cref="AShortfallAgainstATypedHandlerFillsEachSlotWithItsInitialValue"/> makes the same
    /// assertion against a handler whose parameter types have NON-null initial values, where the oracle's
    /// worked example can actually be distinguished from "the slot was left null".
    /// </para>
    /// </remarks>
    /// <param name="declaredArity">The handler's declared parameter count.</param>
    /// <param name="suppliedCount">How many arguments the trigger supplies.</param>
    /// <param name="expectedDeliveredCount">How many of them are expected to reach the handler.</param>
    [Theory]
    [MemberData(nameof(AsymmetricArityRows))]
    public void TheDeclaredArityDecidesWhatArrivesAndSurplusArgumentsAreDiscardedSilently(
        int declaredArity,
        int suppliedCount,
        int expectedDeliveredCount)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber =
            SubscribeArityHandler(broker, log, "subscriber", declaredArity);

        object?[] payload = DistinctPayload(suppliedCount);

        // SILENTLY. A surplus argument is dropped, not rejected (w_test_eventful.srw:L247).
        Assert.Null(Record.Exception(() => broker.Trigger(DispatchTopic, payload)));

        // The handler RAN. A subject that skipped the subscriber on a length mismatch would also throw
        // nothing, and only this assertion tells the two apart.
        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, HandlerNameForArity(declaredArity));

        // Every declared slot is present regardless of how many arguments were supplied.
        Assert.Equal(declaredArity, row.ArgumentCount);

        // The delivered prefix, in order.
        for (int position = 0; position < expectedDeliveredCount; position++)
        {
            Assert.Equal(DistinctArgumentValues[position], row.Arguments[position]);
        }

        // The surplus, absent entirely - not appended, not folded into a final slot, not reported.
        for (int position = expectedDeliveredCount; position < suppliedCount; position++)
        {
            Assert.DoesNotContain(DistinctArgumentValues[position], row.Arguments);
        }

        // The shortfall, at the initial value of an object parameter.
        for (int slot = expectedDeliveredCount; slot < declaredArity; slot++)
        {
            Assert.Null(row.Arguments[slot]);
        }
    }

    /// <summary>
    /// The supplied counts run against the typed three-parameter handler: none, each partial prefix, the
    /// exact count, and a surplus.
    /// </summary>
    /// <remarks>
    /// The handler is <c>OnTypedArguments(long count, string text, bool flag)</c> - the one recording
    /// handler whose parameters are not <see cref="object"/>, and therefore the only one on which "the
    /// type's initial value" is distinguishable from <see langword="null"/>.
    /// </remarks>
    public static TheoryData<int> TypedShortfallRows => new() { 0, 1, 2, 3, 5 };

    /// <summary>
    /// Against a handler whose parameter types have non-null initial values, each slot the payload did not
    /// reach holds THAT INITIAL VALUE - zero, the empty string, false - and never null; and a surplus is
    /// still discarded silently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and this is the test that discharges the oracle's
    /// own worked example verbatim. <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L242</c> declares
    /// <c>event ontest(string arg1,string arg2)</c> and <c>:L246</c> triggers it with ONE argument,
    /// annotating that <c>arg2</c> receives <c>''</c> - explicitly the TYPE'S INITIAL VALUE, and explicitly
    /// not null. Every PowerScript type has such a value: zero for a number, false for a boolean, the empty
    /// string for a string, and null only for an object reference.
    /// </para>
    /// <para>
    /// <b>THE DIFFERENCE THAT IS RECORDED RATHER THAN PAPERED OVER.</b> Where the port declares a parameter
    /// as <c>object?</c>, an unreached slot arrives as <see langword="null"/> - because null genuinely IS
    /// that type's initial value, so the two descriptions coincide and nothing is lost. Where the port
    /// declares a parameter as a string, a number or a boolean - as this handler does - the slot arrives at
    /// the non-null initial value the oracle names. Both are asserted, in this test and in
    /// <see cref="TheDeclaredArityDecidesWhatArrivesAndSurplusArgumentsAreDiscardedSilently"/>
    /// respectively, so the behaviour is pinned as the implementation genuinely does it rather than as one
    /// half of it generalised.
    /// </para>
    /// <para>
    /// The empty string is additionally asserted to be non-null and to be a string, because
    /// <c>Assert.Equal(string.Empty, null)</c> would fail but a reader cannot see from an equality
    /// assertion alone which of the two possible defects it was guarding against.
    /// </para>
    /// </remarks>
    /// <param name="suppliedCount">How many of the three typed arguments the trigger supplies.</param>
    [Theory]
    [MemberData(nameof(TypedShortfallRows))]
    public void AShortfallAgainstATypedHandlerFillsEachSlotWithItsInitialValue(int suppliedCount)
    {
        const long suppliedCountValue = 7L;
        const string suppliedText = "text";
        const bool suppliedFlag = true;

        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = new(log, "subscriber", broker);

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(DispatchTopic, subscriber, RecordingHandlerNames.TypedArguments));

        // The values are spelled with the parameters' own types, so nothing here depends on the coercion
        // step and the assertion is purely about which slots the payload reached.
        object?[] available =
        [
            suppliedCountValue,
            suppliedText,
            suppliedFlag,
            DistinctArgumentValues[3],
            DistinctArgumentValues[4]
        ];

        object?[] payload = available.Take(suppliedCount).ToArray();

        Assert.Null(Record.Exception(() => broker.Trigger(DispatchTopic, payload)));

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.TypedArguments);

        // Three declared slots, whatever was supplied.
        Assert.Equal(3, row.ArgumentCount);

        // Slot one: the supplied number, or a numeric zero - NOT null.
        Assert.Equal(suppliedCount >= 1 ? (object?)suppliedCountValue : 0L, row.Arguments[0]);
        Assert.NotNull(row.Arguments[0]);

        // Slot two: the supplied text, or the EMPTY STRING - the oracle's own worked example at :L246.
        Assert.Equal(suppliedCount >= 2 ? (object?)suppliedText : string.Empty, row.Arguments[1]);
        Assert.NotNull(row.Arguments[1]);
        Assert.IsType<string>(row.Arguments[1]);

        // Slot three: the supplied flag, or false - NOT null.
        Assert.Equal(suppliedCount >= 3 ? (object?)suppliedFlag : false, row.Arguments[2]);
        Assert.NotNull(row.Arguments[2]);
    }

    // =============================================================================================
    //  THE PREPARATION HOOK - leading-argument injection and the reported consumed count
    //  onprepare declared n_cst_eventful.sru:L33, called from _of_passargs at :L609
    //  The one real override: n_cst_threading_eventful.sru:L48-L58
    // =============================================================================================

    /// <summary>
    /// Builds a derived broker configured to inject a leading argument and report a consumed count.
    /// </summary>
    /// <param name="leadingArgument">The value the hook writes into slot one.</param>
    /// <param name="consumedArgumentCount">
    /// The count the hook reports, forwarded to the broker VERBATIM - negative and oversized values
    /// included, which is what lets the clamp at <c>n_cst_eventful.sru:L610</c> and the <c>Min</c> at
    /// <c>:L613</c> be observed at their boundaries.
    /// </param>
    /// <returns>
    /// The broker. It carries its OWN hook log, deliberately separate from the subscribers' log, so a
    /// count of handler rows is never inflated by hook rows.
    /// </returns>
    /// <remarks>
    /// The two skip guards are left at their legacy defaults here - both ENABLED, matching
    /// <c>n_cst_threading_eventful.sru:L54</c> - and the tests that exercise them set them explicitly.
    /// </remarks>
    private static TestEventBroker InjectingBroker(object? leadingArgument, int consumedArgumentCount) =>
        new()
        {
            InjectLeadingArgument = true,
            LeadingArgument = leadingArgument,
            ConsumedArgumentCount = consumedArgumentCount
        };

    /// <summary>
    /// The injected value arrives FIRST and the caller's own arguments follow it, in order - and because
    /// the handler's capacity is fixed, the caller's last argument is dropped from the tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The one evidenced override in the corpus is four
    /// statements long (<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L48-L58</c>): it
    /// writes the source object into slot ONE (<c>:L55</c>) and reports that it consumed one argument
    /// (<c>:L56</c>). Those two statements together are what establish the threading layer's
    /// <c>Handler(source, ...)</c> calling convention, and a port that honoured the write but ignored the
    /// count would hand the handler the injected value AND all four caller arguments - overrunning nothing,
    /// reporting nothing, and quietly changing which value every parameter holds.
    /// </para>
    /// <para>
    /// The dropped tail is asserted explicitly rather than left implied, because it is the observable
    /// consequence of <c>:L613</c> computing capacity AFTER the reservation:
    /// <c>Min(4 - 1, 4)</c> is three, so three of the four caller arguments fit and the fourth is
    /// discarded by exactly the same rule that discards a surplus argument.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInjectedLeadingArgumentArrivesFirstAndTheCallerArgumentsFollowIt()
    {
        ReferencePayload injected = new("injected-source");
        TestEventBroker broker = InjectingBroker(injected, 1);

        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 4);

        object?[] payload = DistinctPayload(4);

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.FourArguments);

        // The write happened, into the ONE-BASED slot one (n_cst_threading_eventful.sru:L55).
        Assert.Equal(RetCode.OK, broker.LeadingArgumentWriteResult);

        // Injected first - by IDENTITY, since injection must hand over the very instance written.
        Assert.Same(injected, row.Arguments[0]);

        // Then the caller's arguments, in order, shifted one place right.
        Assert.Equal(payload[0], row.Arguments[1]);
        Assert.Equal(payload[1], row.Arguments[2]);
        Assert.Equal(payload[2], row.Arguments[3]);

        // And the tail is gone: four slots, four received, so something had to give.
        Assert.Equal(4, row.ArgumentCount);
        Assert.DoesNotContain(payload[3], row.Arguments);
    }

    /// <summary>
    /// The consumed counts an override may report, each with the exact four-slot outcome spelled out:
    /// <c>I</c> is the injected value, <c>-</c> a slot the payload never reached, and a letter the payload
    /// position that landed there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row uses the four-parameter handler and supplies four arguments, so the ONLY variable is the
    /// reported count - which is what makes the table a clean reading of
    /// <c>n_cst_eventful.sru:L610</c> and <c>:L613</c>.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <b>0</b> reserves nothing, so <c>Min(4 - 0, 4)</c> is four and the payload OVERWRITES the
    ///   injected slot. The injection is not undone - it is simply written over, which is what
    ///   <c>:L615</c> copying from slot one does.
    ///   </description></item>
    ///   <item><description>
    ///   <b>1</b> is the value the real override reports (<c>n_cst_threading_eventful.sru:L56</c>).
    ///   </description></item>
    ///   <item><description>
    ///   <b>2</b> and <b>3</b> reserve more slots than were written, so the unwritten reserved slots stay
    ///   at their initial value and the payload starts after them - the proof that capacity is computed
    ///   AFTER the reservation rather than before it.
    ///   </description></item>
    ///   <item><description>
    ///   <b>4</b> leaves no room at all: <c>Min(4 - 4, 4)</c> is zero and not one caller argument arrives.
    ///   </description></item>
    ///   <item><description>
    ///   <b>-3</b> exercises the CLAMP at <c>:L610</c> (<c>if nArgIdx &lt;= 0 then nArgIdx = 0</c>), which
    ///   is a clamp and not a validation: a negative count is silently treated as zero rather than
    ///   reported, so the outcome is identical to zero's.
    ///   </description></item>
    ///   <item><description>
    ///   <b>6</b> exceeds the declared arity, so <c>Min(4 - 6, 4)</c> is negative and nothing is copied.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<int, string> ConsumedCountShiftRows => new()
    {
        // reported consumed count, expected four slots
        { 0, "abcd" },
        { 1, "Iabc" },
        { 2, "I-ab" },
        { 3, "I--a" },
        { 4, "I---" },
        { -3, "abcd" },
        { 6, "I---" }
    };

    /// <summary>
    /// The consumed count the hook reports genuinely shifts the caller's arguments, and both of its
    /// boundaries are clamped rather than rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>_of_passargs</c> reads the reported count,
    /// clamps a non-positive value to zero (<c>n_cst_eventful.sru:L610</c>), then computes
    /// <c>Min(declared - consumed, supplied)</c> (<c>:L613</c>) and copies that many arguments starting at
    /// the slot AFTER the reservation (<c>:L614-L615</c>). Neither boundary is an error in the oracle and
    /// neither is one here: a negative count behaves as zero and an oversized one simply leaves no room.
    /// </para>
    /// <para>
    /// The write result is asserted on every row because it is the evidence that the injection itself
    /// happened even on the rows where the payload then overwrote it - without that, the zero and negative
    /// rows would be indistinguishable from an injection that never occurred.
    /// </para>
    /// </remarks>
    /// <param name="consumedArgumentCount">The count the hook reports.</param>
    /// <param name="expectedSlots">The four expected slots, one character each.</param>
    [Theory]
    [MemberData(nameof(ConsumedCountShiftRows))]
    public void TheReportedConsumedCountShiftsTheCallerArgumentsAndItsBoundariesAreClamped(
        int consumedArgumentCount,
        string expectedSlots)
    {
        ReferencePayload injected = new("injected-source");
        TestEventBroker broker = InjectingBroker(injected, consumedArgumentCount);

        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 4);

        object?[] payload = DistinctPayload(4);

        // No boundary is an error: every row runs the handler and raises nothing.
        Assert.Null(Record.Exception(() => broker.Trigger(DispatchTopic, payload)));

        Assert.Equal(1, subscriber.InvocationCount);

        // Slot one exists on a four-parameter handler, so the write always succeeds.
        Assert.Equal(RetCode.OK, broker.LeadingArgumentWriteResult);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.FourArguments);

        AssertSlots(expectedSlots, payload, injected, row.Arguments);
    }

    /// <summary>
    /// The source-target guard: injection is suppressed when the subscription's target IS the designated
    /// source object, and happens when it is not.
    /// </summary>
    /// <remarks>
    /// Both rows are present so the assertion cannot pass vacuously: an implementation that never injected
    /// at all would satisfy the suppressed row and fail the other.
    /// </remarks>
    public static TheoryData<bool, string> SourceTargetGuardRows => new()
    {
        // target is the source object, expected four slots
        { true, "abcd" },
        { false, "Iabc" }
    };

    /// <summary>
    /// Injection does not occur when the subscription's target is the designated source object - the second
    /// half of the legacy guard - and the caller's arguments then arrive unshifted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The guard is
    /// <c>if argCount &lt; 1 or target = _source then return 0</c> at
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L54</c>: an object must not have
    /// itself injected as its own event source, so the override returns before writing the slot and before
    /// reporting a consumed count. Both omissions matter - had it reported the count without writing the
    /// slot, the caller's first argument would be silently displaced by an empty slot.
    /// </para>
    /// <para>
    /// The comparison is by REFERENCE, because PowerScript's <c>target = _source</c> compares object
    /// identity. That is asserted here only in the sense that it is what the row relies on; the identity
    /// semantics of the guard itself belong to the double.
    /// </para>
    /// <para>
    /// The absence of a reported write is the discriminating evidence: the write result stays unset when
    /// the guard fired, which distinguishes "not attempted" from "attempted and refused" without a second
    /// flag.
    /// </para>
    /// </remarks>
    /// <param name="targetIsSourceObject">Whether the subscriber is the broker's designated source.</param>
    /// <param name="expectedSlots">The four expected slots, one character each.</param>
    [Theory]
    [MemberData(nameof(SourceTargetGuardRows))]
    public void InjectionIsSuppressedWhenTheTargetIsTheDesignatedSourceObject(
        bool targetIsSourceObject,
        string expectedSlots)
    {
        ReferencePayload injected = new("injected-source");
        TestEventBroker broker = InjectingBroker(injected, 1);

        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 4);

        // The guard itself is left at its legacy default (enabled); only WHAT the source is varies.
        broker.SourceObject = targetIsSourceObject ? subscriber : new ReferencePayload("other-source");

        object?[] payload = DistinctPayload(4);

        broker.Trigger(DispatchTopic, payload);

        Assert.Equal(1, subscriber.InvocationCount);

        if (targetIsSourceObject)
        {
            // The override returned at :L54, so no write was even attempted.
            Assert.Null(broker.LeadingArgumentWriteResult);
        }
        else
        {
            Assert.Equal(RetCode.OK, broker.LeadingArgumentWriteResult);
        }

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.FourArguments);

        AssertSlots(expectedSlots, payload, injected, row.Arguments);
    }

    /// <summary>
    /// The zero-argument guard, with and without the guard in place.
    /// </summary>
    /// <remarks>
    /// With the guard enabled - the legacy behaviour - no write is attempted at all. With it lifted, the
    /// write IS attempted and the broker refuses it, because slot one lies outside a handler that declares
    /// no slots. Both rows exist because they pin different things: the first pins the guard, the second
    /// pins the one-based bound the guard exists to avoid tripping.
    /// </remarks>
    public static TheoryData<bool, bool> ZeroArityGuardRows => new()
    {
        // guard enabled, a refusal is expected to be reported
        { true, false },
        { false, true }
    };

    /// <summary>
    /// Injection does not occur for a handler that declares no argument slots - the first half of the
    /// legacy guard - and the handler still runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>argCount &lt; 1</c> is the first half of
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L54</c>, and it is why the DECLARED
    /// argument count has to be visible to an override at all: with no slot to inject into there is nothing
    /// the override can usefully do, so it returns before writing. The subscriber is NOT skipped by this -
    /// the guard returns the continue value, so the handler runs with an empty argument set.
    /// </para>
    /// <para>
    /// With the guard lifted, the write is attempted and refused with the invalid-argument code, because
    /// slot addressing is ONE-BASED and a zero-parameter handler has no slot one. That is asserted because
    /// AAP 0.4.5.4 names one-based-to-zero-based translation the single most dangerous mechanical hazard in
    /// this refactor: a port that had silently treated slot one as a zero-based index would have written
    /// past the end of an empty buffer, or worse, resized it.
    /// </para>
    /// <para>
    /// The hook's own row is asserted too, carrying the target it was given and the declared count it saw,
    /// which is what shows the guard - and not the subclassing gate - is what suppressed the injection.
    /// </para>
    /// </remarks>
    /// <param name="guardEnabled">Whether the zero-slot guard is left in place.</param>
    /// <param name="expectRefusalReported">Whether a refused write is expected to be reported.</param>
    [Theory]
    [MemberData(nameof(ZeroArityGuardRows))]
    public void InjectionIsSuppressedForAHandlerThatDeclaresNoArgumentSlots(
        bool guardEnabled,
        bool expectRefusalReported)
    {
        ReferencePayload injected = new("injected-source");
        TestEventBroker broker = InjectingBroker(injected, 1);
        broker.SkipInjectionWithoutArgumentSlots = guardEnabled;

        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 0);

        object?[] payload = DistinctPayload(3);

        // Neither arrangement is an error, with or without the guard.
        Assert.Null(Record.Exception(() => broker.Trigger(DispatchTopic, payload)));

        // The subscriber was NOT skipped: the guard returns the continue value (:L54).
        Assert.Equal(1, subscriber.InvocationCount);

        DispatchRecord row = SingleRowFor(log, RecordingHandlerNames.NoArguments);
        Assert.Empty(row.Arguments);

        if (expectRefusalReported)
        {
            // Slot one does not exist on a zero-parameter handler, so the one-based write is refused.
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.LeadingArgumentWriteResult);
        }
        else
        {
            Assert.Null(broker.LeadingArgumentWriteResult);
        }

        // The hook DID fire, saw this target, and saw a declared count of zero - so the suppression came
        // from the guard rather than from the hook never running.
        DispatchRecord prepareRow = SingleRowFor(broker.Log, TestEventBroker.PrepareHookName);
        Assert.Same(subscriber, prepareRow.Arguments[0]);
        Assert.Equal((object?)0, prepareRow.Arguments[1]);
    }

    /// <summary>
    /// A prevented preparation hook skips the subscriber it was called for and the walk CONTINUES to the
    /// next one: the hook fires once per subscriber, not once per dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and pins a REACH that is easy to conflate. A
    /// prevented result makes <c>_of_passargs</c> return false (<c>n_cst_eventful.sru:L609</c>) and the
    /// dispatch loop then <c>continue</c>s rather than leaving - the oracle spells that out as
    /// <c>if Not _of_PassArgs(name,Events[nIndex].object,invoker,params) then continue</c>
    /// (<c>:L862-L864</c>). So with three matching subscribers and a hook that prevents every time, the
    /// hook fires THREE times and no handler runs at all; a port that had aborted the walk on the first
    /// prevention would fire it once.
    /// </para>
    /// <para>
    /// This is not the whole-dispatch veto. That is <see cref="VetoSemanticsTests"/>'s subject, it uses a
    /// different mechanism, and the contrast is asserted in
    /// <see cref="AWholeDispatchVetoNeverReachesThePreparationHookAtAll"/> precisely so the two cannot be
    /// mistaken for one another. Note also that the two share the numeral one - the hook's alphabet is the
    /// return-code prevention value, not the veto result type - which is exactly why the reach has to be
    /// pinned by observation rather than inferred from the value.
    /// </para>
    /// </remarks>
    [Fact]
    public void APreventedPreparationHookSkipsThatSubscriberAndTheWalkContinuesThroughTheRest()
    {
        TestEventBroker broker = new() { PrepareResult = RetCode.PREVENT };

        DispatchLog log = new();
        RecordingSubscriber first = SubscribeArityHandler(broker, log, "first", 4);
        RecordingSubscriber second = SubscribeArityHandler(broker, log, "second", 4);
        RecordingSubscriber third = SubscribeArityHandler(broker, log, "third", 4);

        broker.Trigger(DispatchTopic, DistinctPayload(4));

        // Nobody ran.
        Assert.Empty(log.Records);
        Assert.Equal(0, first.InvocationCount);
        Assert.Equal(0, second.InvocationCount);
        Assert.Equal(0, third.InvocationCount);

        // But every subscriber was individually considered - three hook calls, not one.
        Assert.Equal(3, HookRowCount(broker.Log, TestEventBroker.PrepareHookName));

        // And the whole-dispatch hook still fired exactly once, on the first match.
        Assert.Equal(1, HookRowCount(broker.Log, TestEventBroker.TriggeringHookName));
    }

    /// <summary>
    /// A hook that begins preventing partway through a dispatch leaves the subscriber that already ran
    /// intact and skips only those that follow - the sharpest available form of "this subscriber only".
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The hook is consulted immediately before each
    /// invocation from inside <c>_of_passargs</c> (<c>n_cst_eventful.sru:L609</c>), so a decision taken
    /// during the first subscriber's handler is in force for the second and third. The first subscriber's
    /// completed invocation is NOT unwound, no exception is raised, and the walk still reaches every
    /// remaining subscriber to ask about it - which together are what "skips that subscriber only" actually
    /// means at the level of observable behaviour.
    /// </para>
    /// <para>
    /// The decision is flipped from inside a handler rather than by configuring a per-target predicate,
    /// because the one real override in the corpus takes its decision from state it reads at call time -
    /// the cancellation condition at <c>n_cst_threading_eventful.sru:L49</c> - and that state can change
    /// mid-dispatch exactly like this.
    /// </para>
    /// </remarks>
    [Fact]
    public void APreventedPreparationHookMidDispatchLeavesTheAlreadyInvokedSubscriberIntact()
    {
        TestEventBroker broker = new();

        DispatchLog log = new();
        RecordingSubscriber first = SubscribeArityHandler(broker, log, "first", 4);
        RecordingSubscriber second = SubscribeArityHandler(broker, log, "second", 4);
        RecordingSubscriber third = SubscribeArityHandler(broker, log, "third", 4);

        // The hook allows the first subscriber, whose handler then turns the hook to preventing.
        first.DuringAnyHandler = (_, _) => broker.PrepareResult = RetCode.PREVENT;

        object?[] payload = DistinctPayload(4);

        broker.Trigger(DispatchTopic, payload);

        // The first ran, with its full payload, and was not unwound.
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(["first"], log.Labels);
        Assert.Equal(payload, log.ArgumentsAt(0));

        // The rest were skipped.
        Assert.Equal(0, second.InvocationCount);
        Assert.Equal(0, third.InvocationCount);

        // And the walk still reached all three to ask - it did not abort at the first prevention.
        Assert.Equal(3, HookRowCount(broker.Log, TestEventBroker.PrepareHookName));
    }

    /// <summary>
    /// A whole-dispatch veto never reaches the preparation hook at all: the two outcomes have different
    /// REACH and are not the same mechanism.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and exists to make conflation impossible. The
    /// whole-dispatch hook is consulted once, inside the first-match block, and a prevented result leaves
    /// the loop outright (<c>n_cst_eventful.sru:L839-L843</c>) - before <c>_of_passargs</c> is called for
    /// even the first subscriber. So the preparation hook fires ZERO times, where the per-subscriber skip
    /// of <see cref="APreventedPreparationHookSkipsThatSubscriberAndTheWalkContinuesThroughTheRest"/>
    /// fires it once per subscriber.
    /// </para>
    /// <para>
    /// Only the reach is asserted here. The veto's own semantics - its tri-valued alphabet, its depth, and
    /// what survives into an enclosing dispatch - are <see cref="VetoSemanticsTests"/>'s subject and are
    /// deliberately not re-asserted in this file.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWholeDispatchVetoNeverReachesThePreparationHookAtAll()
    {
        TestEventBroker broker = new() { TriggeringResult = RetCode.PREVENT };

        DispatchLog log = new();
        RecordingSubscriber first = SubscribeArityHandler(broker, log, "first", 4);
        RecordingSubscriber second = SubscribeArityHandler(broker, log, "second", 4);

        broker.Trigger(DispatchTopic, DistinctPayload(4));

        Assert.Empty(log.Records);
        Assert.Equal(0, first.InvocationCount);
        Assert.Equal(0, second.InvocationCount);

        // The whole-dispatch hook was consulted once and aborted the walk there and then.
        Assert.Equal(1, HookRowCount(broker.Log, TestEventBroker.TriggeringHookName));

        // THE DISCRIMINATOR: the preparation hook never ran, for any subscriber.
        Assert.Equal(0, HookRowCount(broker.Log, TestEventBroker.PrepareHookName));
    }

    /// <summary>
    /// The preparation hook runs only for a DERIVED broker: a plain broker forwards the caller's arguments
    /// unmodified and never consults a hook.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>_of_passargs</c> guards the hook with
    /// <c>if _bSubclassing then</c> (<c>n_cst_eventful.sru:L608</c>), and the port computes the same gate
    /// as a type-identity comparison at construction. So a plain broker cannot inject, cannot reserve a
    /// slot, and cannot skip a subscriber - the payload it forwards is the payload it was given.
    /// </para>
    /// <para>
    /// Asserted as an A/B in ONE test on purpose. A plain-broker-only assertion is nearly vacuous, since
    /// there is no hook to observe and no row could appear; running the identically-configured derived case
    /// beside it is what makes the difference attributable to the gate rather than to the arrangement. The
    /// injected value is asserted ABSENT from the plain broker's delivery, which is the strongest available
    /// statement that nothing intervened.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePreparationHookRunsOnlyForADerivedBrokerAndAPlainBrokerForwardsUnmodified()
    {
        ReferencePayload injected = new("injected-source");
        object?[] payload = DistinctPayload(4);

        // A - the plain broker. No subclass, so no hook.
        EventBroker plain = new();
        DispatchLog plainLog = new();
        SubscribeArityHandler(plain, plainLog, "plain", 4);

        plain.Trigger(DispatchTopic, payload);

        DispatchRecord plainRow = SingleRowFor(plainLog, RecordingHandlerNames.FourArguments);
        AssertSlots("abcd", payload, injected, plainRow.Arguments);
        Assert.DoesNotContain(injected, plainRow.Arguments);
        Assert.Equal(0, HookRowCount(plainLog, TestEventBroker.PrepareHookName));

        // B - the same arrangement on a derived broker configured to inject. The gate is the only
        // difference, and it is the difference that shows.
        TestEventBroker derived = InjectingBroker(injected, 1);
        DispatchLog derivedLog = new();
        SubscribeArityHandler(derived, derivedLog, "derived", 4);

        derived.Trigger(DispatchTopic, payload);

        DispatchRecord derivedRow = SingleRowFor(derivedLog, RecordingHandlerNames.FourArguments);
        AssertSlots("Iabc", payload, injected, derivedRow.Arguments);
        Assert.Equal(1, HookRowCount(derived.Log, TestEventBroker.PrepareHookName));
    }

    // =============================================================================================
    //  POST - argument forwarding only. of_post declared n_cst_eventful.sru:L132-L142
    // =============================================================================================

    /// <summary>
    /// A posted dispatch forwards exactly the arguments a triggered dispatch does, once the queue has been
    /// drained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// COVERS THE SUBSTITUTION (AAP 0.7.3 C-K). <c>of_post</c> is declared in the same eleven arities as
    /// <c>of_trigger</c> (<c>n_cst_eventful.sru:L132-L142</c>) and every one of its definitions is
    /// <c>Post _of_Trigger(name,{...},true)</c> - the same array literal, the same private routine, the
    /// same argument rule. Both families collapse onto one variadic method each, so the property worth
    /// pinning here is that the collapse did not make the two disagree.
    /// </para>
    /// <para>
    /// <b>ONLY the argument forwarding is asserted.</b> The queue itself - its ordering, its FIFO drain,
    /// its re-entrancy, and the fact that a posted payload is snapshotted at the moment of posting -
    /// belongs to <c>PostQueueTests</c> and is deliberately not duplicated here. The drain call in this
    /// test is a precondition, not the subject.
    /// </para>
    /// <para>
    /// The reference element is asserted by identity on the posted delivery too, because the snapshot the
    /// post path takes is a SHALLOW copy of the payload array: the array is copied, the values it holds are
    /// not. A deep copy would break every handler that compares a forwarded reference against something it
    /// already holds, and it would break it only on the posted path - which no trigger-only test could
    /// detect.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostForwardsTheSameArgumentsAsTriggerOnceTheQueueHasBeenDrained()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber subscriber = SubscribeArityHandler(broker, log, "subscriber", 4);

        ReferencePayload reference = new("shared-instance");
        object?[] payload = [reference, DistinctArgumentValues[1], null, DistinctArgumentValues[3]];

        broker.Trigger(DispatchTopic, payload);

        // Queued, not run: the drain below is what runs it.
        broker.Post(DispatchTopic, payload);

        Assert.Equal(1, subscriber.InvocationCount);
        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(2, subscriber.InvocationCount);

        Assert.Equal(2, log.Count);

        // The posted delivery matches the triggered one, value for value.
        Assert.Equal(log.ArgumentsAt(0), log.ArgumentsAt(1));
        Assert.Equal(payload, log.ArgumentsAt(1));

        // And the shallow snapshot preserved the reference's identity.
        Assert.Same(reference, log.ArgumentsAt(1)[0]);
    }

    /// <summary>
    /// A reference-typed payload value, used wherever an assertion is about object IDENTITY rather than
    /// about equality.
    /// </summary>
    /// <remarks>
    /// A dedicated type rather than a bare <see cref="object"/> so a failure message names the instance
    /// instead of printing a type name, and so the mixed-type matrix has a value whose runtime type is
    /// unmistakably not one of the framework's own. It carries no equality override, which is the point:
    /// two instances are equal only when they are the same instance, so <c>Assert.Same</c> and
    /// <c>Assert.Equal</c> agree here only when identity genuinely survived.
    /// </remarks>
    private sealed class ReferencePayload
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferencePayload"/> class.
        /// </summary>
        /// <param name="label">The name this instance renders as.</param>
        public ReferencePayload(string label)
        {
            Label = label;
        }

        /// <summary>
        /// Gets the name this instance renders as.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Renders the instance as its label, so an assertion failure names it.
        /// </summary>
        /// <returns>The label.</returns>
        public override string ToString() => Label;
    }
}
