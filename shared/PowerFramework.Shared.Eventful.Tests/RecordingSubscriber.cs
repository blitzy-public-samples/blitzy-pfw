// =====================================================================================================
//  RecordingSubscriber.cs
//  =====================================================================================================
//  SHARED TEST INFRASTRUCTURE FOR THE EVENT BROKER SUITES. THIS FILE IS NOT A TEST SUITE.
//
//  It declares no [Fact], no [Theory] and calls no Assert, and its file name deliberately carries no
//  `Tests` suffix so a reader scanning the folder does not mistake it for a suite. Everything here is a
//  subscriber DOUBLE plus the ordered invocation log the suites assert against. One shared double is
//  correct rather than nine private copies: nine copies would be nine places for a semantic to drift,
//  and the semantics below are subtle enough that drift would not be obvious.
//
//  WHY A TARGET OBJECT WITH NAMED HANDLER METHODS IS THE ONLY SHAPE THAT WORKS
//      The broker's subscribe surface is the port of `of_on(name, powerobject object, string evtname)`
//      (ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L296, delegated to from :L293). A
//      subscription binds a TARGET OBJECT together with the NAME OF A HANDLER METHOD ON IT - which is
//      precisely why the seven `Unsubscribe` and seven `Disable` overloads can filter by object and by
//      handler name at all. `EventBroker.ResolveHandler` reflects that name over the target's type with
//      BindingFlags.Instance | Public | NonPublic | DeclaredOnly, walked up the base-type chain, matching
//      StringComparison.OrdinalIgnoreCase. So a double must be an object exposing publicly named
//      methods; a delegate or an interface implementation would not be reachable through that surface.
//
//  C-K - THE SUBSTITUTIONS THIS FILE EXISTS BECAUSE OF, NAMED HERE RATHER THAN LEFT TO BE INFERRED
//      SUBSTITUTION 1 - THE ARITY COLLAPSE, which is why there is a VARYING-ARITY HANDLER SET below
//      rather than one handler.
//          The legacy publishes ELEVEN fixed `of_trigger` overloads carrying zero through ten payload
//          arguments (n_cst_eventful.sru:L119-L129) and ELEVEN fixed `of_post` overloads carrying ten
//          down to zero (:L132-L142). Every one of the twenty-two is a single-line delegation to one
//          private routine. The port collapses BOTH families onto a single variadic method each -
//          `EventBroker.Trigger(string, params object?[]?)` and
//          `EventBroker.Post(string, params object?[]?)` - because PowerScript cannot forward an
//          arbitrary-length argument list and C# `params` can. TEN was therefore a LEGACY LIMIT and not
//          a behavioural rule, and AAP 0.2.1.4 gives the other legacy arity ceilings the same treatment.
//          OnTenArguments sits exactly on that ceiling and OnTwelveArguments deliberately exceeds it, so
//          a suite can prove the .NET contract carries more than ten arguments without regression.
//      SUBSTITUTION 2 - THE BROKER HANDOFF.
//          The legacy dispatch loop snapshots `Message.PowerObjectParm` and overwrites it with `this`
//          before invoking subscribers (n_cst_eventful.sru:L846-L847), restoring it afterwards (:L948),
//          and the oracle's own handler reads the broker straight back out of it
//          (ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L47). .NET has no `Message` object, so the
//          port substitutes the ambient `EventBroker.Current` (its DECISION 5) and this double ALSO
//          accepts an explicit broker reference at construction. RecordingSubscriber.Broker
//          documents which of the two wins and why both exist.
//      SUBSTITUTION 3 - THE DIALOG-FREE OBSERVER.
//          The oracle's handlers report through `MessageBox` (w_test_eventful.srw:L49, :L77, :L87), which
//          no headless test can read. The observation channel becomes DispatchLog: the same
//          facts - which subscriber ran, in what order, with which arguments - recorded as data.
//
//  C-B - THIS DOUBLE IS AN OBSERVER, NOT A PARTICIPANT. IT NORMALISES NOTHING.
//      Every handler records EXACTLY what it was handed, in the order it was handed it, and returns
//      EXACTLY what the suite told it to return. There is no trimming of trailing nulls, no coercion, no
//      defaulting, no de-duplication and no filtering anywhere in the recording path. That restraint is
//      load-bearing rather than stylistic: the suites assert that the BROKER loses no argument and
//      reorders nothing, so a double that tidied its input would be asserting its own behaviour instead
//      of the subject's. In particular the broker's own argument contract must remain visible through it:
//          * `PassArguments` computes the pass count as Min(declared arity - consumed, supplied count) -
//            the port of `_of_passargs` (n_cst_eventful.sru:L605-L618, the Min at :L613) - so SURPLUS
//            TRIGGER ARGUMENTS ARE SILENTLY DISCARDED (w_test_eventful.srw:L247), and
//          * a slot the payload never reaches keeps its parameter type's PowerScript initial value -
//            `""` for a string, the zero value for a value type, null for any other reference type
//            (w_test_eventful.srw:L246).
//      Those two rules are why the arity range below spans 0, 1, 4, 10 and 12, and why exactly one
//      strongly-typed handler is provided: an all-`object?` handler set cannot make the initial-value
//      half observable, because null is both `object`'s initial value and a legitimately passed value.
//      The ONLY validation in this file guards this file's OWN configuration API against null - which is
//      API hygiene on a member a suite calls directly, not normalisation of a dispatch payload.
//
//  C-C - THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE.
//      Every `ws_objects/**` path in this file appears in a COMMENT. Nothing here reads the legacy tree,
//      or any other file, at build time or at run time: this file performs NO file I/O, no network access
//      and no database access of any kind. The root .dockerignore excludes ws_objects/ from the build
//      context, so such a read would fail in a container and in CI even where it happened to work
//      locally.
//
//  C-D - NO DEFERRED CAPABILITY APPEARS HERE, NOT EVEN AS A NAME.
//      The legacy subscription entry carries an `n_scriptinvoker invoker` field (n_cst_eventful.sru:L21)
//      and `onprepare` takes one (:L33). `n_scriptinvoker` belongs to the DEFERRED ScriptBridge service,
//      so this file declares no script-invoker type, no stand-in for one and no concept borrowed from
//      one. That is safe rather than lossy, and AAP 0.2.1.4 says why: the invoker was only a
//      variadic-call escape hatch, and C# forwards arguments natively. SUBSTITUTION 1 above is that
//      forwarding, expressed as a handler set.
//
//  C-H - NULLABLE REFERENCE TYPES AND WARNINGS AS ERRORS APPLY HERE EXACTLY AS THEY DO TO SHIPPING CODE.
//      Directory.Build.props sets Nullable enable and TreatWarningsAsErrors true, and this project adds
//      no NoWarn blanket. So there is no `#pragma warning disable` in this file, no `!` null-forgiving
//      operator papering over a real nullability question, no unused parameter and no unused field. Every
//      handler parameter is READ - it is recorded - which is what makes an unused-parameter discard
//      unnecessary here.
//
//  THE NAMING RULING - BUILD BREAKING IF IGNORED
//      The repository-root .editorconfig scopes its naming-analyzer suppressions to a roster of
//      individually named IMPLEMENTATION files - see the BAND 3 roster in that file, which is the single
//      source of truth for the list and is deliberately not restated here - and it covers NO test file.
//      Every member declared here is therefore conventional PascalCase, and the legacy
//      spellings - `of_on`, `of_trigger`, `of_post`, `_of_passargs`, `PREVENT_ONCE`, `CAP_ALL` - appear in
//      documentation only. Where a suite needs one of those alphabets it references the subject's own
//      PascalCase member: VetoResult.PreventOnce, CaptureMode.All, Priorities.Low.
//
//  DETERMINISM - THERE IS NO CLOCK, NO TIMER AND NO RANDOMNESS IN THIS FILE
//      No DateTime read, no Guid.NewGuid, no Random, no Task.Delay, no Thread.Sleep and no thread
//      hand-off. Ordering is expressed solely by the monotonic sequence number
//      DispatchRecord.Sequence assigns on append. The broker's only asynchrony is its post
//      queue, and that is drained explicitly by `EventBroker.DrainPostedContinuations`, so every suite in
//      this folder is deterministic by construction rather than by tolerance.
//
//  NO LIFETIME PROTOCOL, AND THAT IS A DECISION RATHER THAN AN OVERSIGHT
//      This double implements no IDisposable, no finalizer and holds no weak reference, because there is
//      nothing in the subject for one to reach. `Predicates.IsValidObject` collapses to `value is not
//      null` and performs no disposal check by decision; `EventBroker` declines IDisposable by decision,
//      its legacy destructor having only destroyed invokers (n_cst_eventful.sru:L1315-L1321). A
//      subscription whose target has died is SKIPPED AND SWEPT rather than treated as an error - the
//      dispatch loop sets its collect flag and continues (:L834-L837) and `_of_collect` compacts the
//      table later (:L576-L603) - so target death must never be modelled by throwing, and nothing here
//      does.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// One row of the ordered invocation log: a single handler invocation, recorded exactly as it arrived.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a class rather than a record. A record's synthesised equality would compare
/// <see cref="Arguments"/> by reference, which reads as value equality and is not - two invocations
/// carrying equal payloads would compare unequal, and a suite could spend a long time discovering why.
/// The suites compare the fields they care about, or compare <see cref="Describe"/>.
/// </para>
/// <para>
/// Immutable once appended. A suite that could mutate a recorded row could make the log agree with an
/// assertion after the fact, which is the one thing an evidence log must not permit.
/// </para>
/// </remarks>
public sealed class DispatchRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DispatchRecord"/> class, taking a defensive copy of
    /// the arguments.
    /// </summary>
    /// <param name="sequence">The log-wide monotonic sequence number for this row.</param>
    /// <param name="label">The recording subscriber's label.</param>
    /// <param name="handlerName">The declared name of the handler method that ran.</param>
    /// <param name="topic">The topic the subscriber was told the dispatch would carry.</param>
    /// <param name="arguments">
    /// The arguments the handler received, in the order it received them. Copied, never aliased.
    /// </param>
    internal DispatchRecord(int sequence, string label, string handlerName, string topic, object?[] arguments)
    {
        Sequence = sequence;
        Label = label;
        HandlerName = handlerName;
        Topic = topic;

        // THE SNAPSHOT, and the reason it is not optional. The array a handler is invoked with is
        // assembled by the broker (EventBroker.PassArguments builds a fresh buffer per invocation) and a
        // subclassing broker may write into it through EventArgumentContext.SetArgument while the
        // invocation is being prepared. Recording the reference instead of a copy would let a later write
        // - by the broker, by a suite, or by a nested dispatch reusing a buffer - retroactively rewrite a
        // row that has already been observed. Array.Empty is used for the zero-argument case so the
        // common path allocates nothing.
        //
        // The copy is SHALLOW BY DESIGN. Cloning the referenced values would be normalisation of the
        // payload, which C-B forbids: a suite that passes a mutable object and then mutates it is
        // asserting something about reference identity, and this log must report what actually happened.
        object?[] snapshot = arguments.Length == 0 ? [] : (object?[])arguments.Clone();
        Arguments = Array.AsReadOnly(snapshot);
    }

    /// <summary>
    /// Gets the log-wide sequence number of this row, starting at one and increasing by one per appended
    /// record.
    /// </summary>
    /// <remarks>
    /// Unique for the lifetime of its <see cref="DispatchLog"/> and never reused, so a row captured
    /// before a <see cref="DispatchLog.Clear"/> can never be confused with one appended after it. This is
    /// the file's ONLY ordering mechanism: there is no timestamp anywhere, by requirement.
    /// </remarks>
    public int Sequence { get; }

    /// <summary>
    /// Gets the label of the <see cref="RecordingSubscriber"/> that recorded this row.
    /// </summary>
    /// <remarks>
    /// This is what makes several subscriber instances distinguishable in one shared log, and therefore
    /// what makes a cross-subscriber ordering assertion expressible at all.
    /// </remarks>
    public string Label { get; }

    /// <summary>
    /// Gets the DECLARED name of the handler method that ran, in its declared letter case.
    /// </summary>
    /// <remarks>
    /// Declared case, not the case the subscription was registered with. The broker folds handler names
    /// to lower case and matches them case-insensitively (the port of
    /// <c>newEvent.evtName = Lower(evtName)</c> at <c>n_cst_eventful.sru:L336</c>), so the registered
    /// spelling cannot identify which method ran. This carries the spelling as declared, which is exactly
    /// how a suite tells <see cref="RecordingSubscriber.OnCaseFolded"/> apart from
    /// <see cref="RecordingSubscriber.OnCasefolded"/>.
    /// </remarks>
    public string HandlerName { get; }

    /// <summary>
    /// Gets the topic the recording subscriber was told the dispatch would carry.
    /// </summary>
    /// <remarks>
    /// Reported by the subscriber from <see cref="RecordingSubscriber.Topic"/> rather than observed from
    /// the broker, because a handler is genuinely never told which event reached it: the broker invokes
    /// the handler with the payload alone, and the legacy is identical - the oracle's
    /// <c>onbuttonclicked1(commandbutton, long, long)</c> (<c>w_test_eventful.srw:L44</c>) receives no
    /// event name either. So this field is the suite's own statement of intent, and it is empty until the
    /// suite sets it.
    /// </remarks>
    public string Topic { get; }

    /// <summary>
    /// Gets the arguments the handler received, in order, as an immutable snapshot taken at invocation.
    /// </summary>
    /// <remarks>
    /// Its <see cref="System.Collections.Generic.IReadOnlyCollection{T}.Count"/> is the handler's declared
    /// arity, NOT the number of arguments the trigger supplied - the broker always fills every declared
    /// slot, using the parameter type's initial value for slots the payload did not reach. A trigger that
    /// supplied more arguments than the handler declares has had the surplus discarded before this point,
    /// which is the behaviour the varying-arity handler set exists to expose.
    /// </remarks>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>
    /// Gets the number of argument slots recorded, which is the handler's declared arity.
    /// </summary>
    public int ArgumentCount => Arguments.Count;

    /// <summary>
    /// Renders this row as a single compact line, for an ordering assertion that wants one string per
    /// invocation instead of a structural comparison.
    /// </summary>
    /// <returns>
    /// The row in the form <c>label.HandlerName(arg, arg)</c>, with a null argument rendered as
    /// <c>&lt;null&gt;</c> so it is distinguishable from an empty string.
    /// </returns>
    /// <remarks>
    /// Rendered with the invariant convention of <see cref="object.ToString"/> on each value and no
    /// culture-sensitive formatting introduced here, because a culture-sensitive render would make an
    /// ordering assertion depend on the ambient locale.
    /// </remarks>
    public string Describe()
    {
        StringBuilder builder = new();
        builder.Append(Label).Append('.').Append(HandlerName).Append('(');

        for (int index = 0; index < Arguments.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            object? value = Arguments[index];
            builder.Append(value is null ? "<null>" : value.ToString());
        }

        return builder.Append(')').ToString();
    }
}

/// <summary>
/// The ordered, SHARED invocation log every subscriber double appends to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared is the whole point.</b> Several <see cref="RecordingSubscriber"/> instances constructed
/// against one instance of this class append to one list, in dispatch order, and each row carries the
/// appending subscriber's label. That is what makes a cross-subscriber ordering assertion expressible:
/// dispatch order is the ordinal sort of the subscription name ascending and then priority descending,
/// so an ordering suite has to compare invocations ACROSS subscribers, and a per-instance log cannot
/// express a relative order between two instances at all.
/// </para>
/// <para>
/// <b>Not thread-safe, deliberately.</b> One broker instance belongs to one logical thread of control -
/// <c>EventBroker</c> holds unsynchronised per-instance dispatch state and the legacy threading layer
/// gives every thread its own broker - so a log shared across threads would be describing a scenario the
/// subject does not support. Adding a lock would imply otherwise and would add a memory barrier to the
/// ordering evidence, which is exactly the kind of accidental sequencing an ordering suite must not rely
/// on.
/// </para>
/// <para>
/// The read helpers below are plain ordered projections. There is deliberately no query API, no
/// predicate overload and no fluent filter: an evidence log whose reads are as simple as possible is one
/// a failing assertion can be reasoned about from.
/// </para>
/// </remarks>
public sealed class DispatchLog
{
    private readonly List<DispatchRecord> _records = [];

    /// <summary>
    /// The next sequence number to hand out. Monotonic for the lifetime of this log and never reset, so a
    /// number is never reused even across a <see cref="Clear"/>.
    /// </summary>
    private int _nextSequence = 1;

    /// <summary>
    /// Gets every recorded invocation, in the order it happened.
    /// </summary>
    /// <remarks>
    /// A live read-only view over the backing list, so a reference held across further dispatches keeps
    /// reporting the current contents. That is the useful behaviour for a log; a suite wanting a frozen
    /// copy takes one itself.
    /// </remarks>
    public IReadOnlyList<DispatchRecord> Records => _records;

    /// <summary>
    /// Gets the number of invocations recorded so far.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// Gets the subscriber labels of every invocation, in dispatch order.
    /// </summary>
    /// <remarks>
    /// The projection an ordering suite asserts against - it answers "which subscribers ran, in what
    /// order" and nothing else. A fresh list is built per read, so the value a suite captured before a
    /// further dispatch stays exactly as it was when captured.
    /// </remarks>
    public IReadOnlyList<string> Labels
    {
        get
        {
            List<string> labels = new(_records.Count);
            for (int index = 0; index < _records.Count; index++)
            {
                labels.Add(_records[index].Label);
            }

            return labels;
        }
    }

    /// <summary>
    /// Gets the declared handler names of every invocation, in dispatch order.
    /// </summary>
    /// <remarks>
    /// Declared case, from <see cref="DispatchRecord.HandlerName"/> - see the remarks there for why the
    /// registered case cannot serve.
    /// </remarks>
    public IReadOnlyList<string> HandlerNames
    {
        get
        {
            List<string> handlerNames = new(_records.Count);
            for (int index = 0; index < _records.Count; index++)
            {
                handlerNames.Add(_records[index].HandlerName);
            }

            return handlerNames;
        }
    }

    /// <summary>
    /// Gets the compact rendering of every invocation, in dispatch order.
    /// </summary>
    /// <remarks>
    /// The one-line-per-invocation form from <see cref="DispatchRecord.Describe"/>, for a suite that wants
    /// to assert order and payload together in a single sequence comparison.
    /// </remarks>
    public IReadOnlyList<string> Descriptions
    {
        get
        {
            List<string> descriptions = new(_records.Count);
            for (int index = 0; index < _records.Count; index++)
            {
                descriptions.Add(_records[index].Describe());
            }

            return descriptions;
        }
    }

    /// <summary>
    /// Returns the record of the nth invocation, zero-based.
    /// </summary>
    /// <param name="index">The zero-based invocation index.</param>
    /// <returns>The record at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is negative or is not less than <see cref="Count"/>.
    /// </exception>
    /// <remarks>
    /// Zero-based on purpose, and the one place in this file where that is worth stating. The subject
    /// keeps several one-based surfaces because its oracle is one-based - <c>EventArgumentContext</c>
    /// addresses argument slots from 1 - and AAP 0.4.5.4 names one-based-to-zero-based translation the
    /// single most dangerous mechanical hazard in the whole refactor. This log is NOT one of those
    /// surfaces: it is new test infrastructure with no legacy counterpart, so it uses the .NET convention
    /// and says so rather than importing a one-based index nothing asked for.
    /// </remarks>
    public DispatchRecord RecordAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _records.Count);

        return _records[index];
    }

    /// <summary>
    /// Returns the arguments recorded for the nth invocation, zero-based.
    /// </summary>
    /// <param name="index">The zero-based invocation index.</param>
    /// <returns>The immutable argument snapshot of that invocation, in the order the handler received it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is negative or is not less than <see cref="Count"/>.
    /// </exception>
    public IReadOnlyList<object?> ArgumentsAt(int index) => RecordAt(index).Arguments;

    /// <summary>
    /// Discards every recorded invocation, leaving the sequence counter where it is.
    /// </summary>
    /// <remarks>
    /// For a suite that dispatches, asserts, and then dispatches again against the same subscribers.
    /// The sequence counter deliberately does NOT reset: a row captured before this call keeps a
    /// <see cref="DispatchRecord.Sequence"/> no later row can collide with, so two phases of one test can
    /// never be confused for each other. Subscriber-side counters are independent of this and are not
    /// affected - see <see cref="RecordingSubscriber.InvocationCount"/>.
    /// </remarks>
    public void Clear() => _records.Clear();

    /// <summary>
    /// Appends one invocation and returns the row that was appended.
    /// </summary>
    /// <param name="label">The recording subscriber's label.</param>
    /// <param name="handlerName">The declared name of the handler that ran.</param>
    /// <param name="topic">The topic the subscriber was told the dispatch would carry.</param>
    /// <param name="arguments">The arguments the handler received. Copied by the record, never aliased.</param>
    /// <returns>
    /// The appended row, so the caller can hand it to an in-handler interceptor that wants to see what was
    /// just recorded.
    /// </returns>
    /// <remarks>
    /// <b>Internal by design.</b> Only <see cref="RecordingSubscriber"/> appends, so a suite cannot forge
    /// an invocation into the evidence it is about to assert on. Nothing outside this assembly can reach
    /// it either way, but keeping it internal states the intent to the next reader.
    /// </remarks>
    internal DispatchRecord Append(string label, string handlerName, string topic, object?[] arguments)
    {
        DispatchRecord record = new(_nextSequence, label, handlerName, topic, arguments);

        // Incremented only after the record is constructed, so a throwing constructor cannot burn a
        // sequence number and leave a gap that looks like a lost invocation.
        _nextSequence++;
        _records.Add(record);

        return record;
    }
}


/// <summary>
/// A callback a <see cref="RecordingSubscriber"/> runs from INSIDE a dispatch, while that dispatch is
/// still in flight.
/// </summary>
/// <param name="broker">
/// The broker running the dispatch, resolved by <see cref="RecordingSubscriber.Broker"/>. May be
/// <see langword="null"/> only when the subscriber was given no explicit broker AND the handler was
/// invoked outside a dispatch - which is to say, called directly by a suite rather than reached through
/// <c>Trigger</c>.
/// </param>
/// <param name="record">The row that was appended for this invocation, immediately before this call.</param>
/// <remarks>
/// <para>
/// <b>This one member is what makes four of the nine suites possible.</b> Every interesting broker
/// behaviour is only observable from inside a dispatch, because the state it acts on is scoped to the
/// dispatch level and restored when the level unwinds. From here a suite can:
/// </para>
/// <list type="bullet">
///   <item><description>
///   call <c>Prevent()</c> or <c>Prevent(deep: true)</c> - the tri-valued veto whose once-versus-deep
///   distinction survives the unwind differently, and which returns <c>RetCode.FAILED</c> when there is
///   no dispatch to prevent;
///   </description></item>
///   <item><description>
///   fire a NESTED <c>Trigger</c>, exactly as the oracle's own handler does
///   (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L80</c>);
///   </description></item>
///   <item><description>
///   call <c>Subscribe</c> or <c>Unsubscribe</c> mid-dispatch, to reach the rule that a subscription made
///   during a dispatch takes effect only on the NEXT one (<c>w_test_eventful.srw:L237</c>) and the
///   tombstoning that makes a mid-dispatch removal safe;
///   </description></item>
///   <item><description>
///   read <c>IsProcessed()</c>, <c>GetReturnValue()</c> and <c>IsPost()</c>, which the oracle's second
///   handler does at <c>w_test_eventful.srw:L72</c> and <c>:L74</c>, and read the ambient
///   <c>EventBroker.Current</c> that stands in for <c>Message.PowerObjectParm</c>.
///   </description></item>
/// </list>
/// <para>
/// It returns nothing on purpose. The invocation's return value is configured separately through
/// <see cref="RecordingSubscriber.SetReturnValue"/>, so that the handled-state path and the side-effect
/// path stay independent and a suite can exercise either without disturbing the other. A callback that
/// does want to change the answer can still call <c>SetReturnValue</c> on the subscriber it closes over -
/// which takes effect on the NEXT invocation, not this one, and that is stated here so the ordering is
/// not a surprise.
/// </para>
/// </remarks>
public delegate void DispatchInterceptor(EventBroker? broker, DispatchRecord record);

/// <summary>
/// The declared names of every <see cref="RecordingSubscriber"/> handler, so a suite subscribes and
/// configures by constant rather than by magic string.
/// </summary>
/// <remarks>
/// <para>
/// Each value is a <c>nameof</c> over the method it names, so renaming a handler is a compile error here
/// rather than a silent <c>RetCode.E_EVENT_NOT_FOUND</c> at subscribe time in nine suites at once.
/// </para>
/// <para>
/// Top-level rather than nested inside <see cref="RecordingSubscriber"/> purely so no visible-nested-type
/// analyzer diagnostic can ever apply to it. Nothing about it is otherwise unusual.
/// </para>
/// </remarks>
public static class RecordingHandlerNames
{
    /// <summary>The zero-parameter handler.</summary>
    public const string NoArguments = nameof(RecordingSubscriber.OnNoArguments);

    /// <summary>The one-parameter handler.</summary>
    public const string OneArgument = nameof(RecordingSubscriber.OnOneArgument);

    /// <summary>The four-parameter handler.</summary>
    public const string FourArguments = nameof(RecordingSubscriber.OnFourArguments);

    /// <summary>The ten-parameter handler, sitting exactly on the legacy arity ceiling.</summary>
    public const string TenArguments = nameof(RecordingSubscriber.OnTenArguments);

    /// <summary>The twelve-parameter handler, deliberately beyond the legacy arity ceiling.</summary>
    public const string TwelveArguments = nameof(RecordingSubscriber.OnTwelveArguments);

    /// <summary>The strongly-typed handler.</summary>
    public const string TypedArguments = nameof(RecordingSubscriber.OnTypedArguments);

    /// <summary>
    /// The zero-parameter half of the case-folding pair, spelled with a capital <c>F</c>.
    /// </summary>
    /// <remarks>
    /// This and <see cref="CasefoldedPairLowerF"/> differ ONLY by the case of one letter, and they fold to
    /// the same configuration key. See <see cref="RecordingSubscriber.OnCaseFolded"/> for the whole reason
    /// the pair exists and why their arities differ.
    /// </remarks>
    public const string CasefoldedPairCapitalF = nameof(RecordingSubscriber.OnCaseFolded);

    /// <summary>
    /// The one-parameter half of the case-folding pair, spelled with a lower-case <c>f</c>.
    /// </summary>
    /// <remarks>See <see cref="CasefoldedPairCapitalF"/>.</remarks>
    public const string CasefoldedPairLowerF = nameof(RecordingSubscriber.OnCasefolded);
}


/// <summary>
/// The shared subscriber double: a plain object exposing publicly named handler methods, each of which
/// records its invocation into a shared <see cref="DispatchLog"/> and then returns whatever the suite
/// configured.
/// </summary>
/// <remarks>
/// <para>
/// This is the target half of a subscription. <c>Subscribe(topic, subscriber, handlerName)</c> binds an
/// instance of this class together with the name of one of the handlers below, which is the shape the
/// legacy <c>of_on(name, powerobject object, string evtname)</c> requires
/// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L296</c>).
/// </para>
/// <para>
/// <b>Configuration is PER HANDLER, not per instance.</b> Return values, in-handler callbacks and
/// throw-on-invoke are each keyed by handler name, because one subscriber is routinely subscribed to
/// several topics through several handlers within one test and those handlers need to behave differently.
/// Keys are compared with <c>SubscriptionOptions.HandlerNameComparer</c> - the subject's own published
/// comparer, which is case-insensitive - so a suite that configures <c>"onnoarguments"</c> reaches the
/// handler declared <c>OnNoArguments</c>, exactly as the broker itself would.
/// </para>
/// <para>
/// <b>No lifetime protocol.</b> See the file header: <c>IsValidObject</c> collapses to a null test, the
/// broker declines <see cref="IDisposable"/>, and a dead target is skipped and swept rather than reported.
/// There is therefore nothing for a disposal, a finalizer or a weak reference to reach, and modelling
/// target death by throwing would contradict <c>n_cst_eventful.sru:L834-L837</c>.
/// </para>
/// </remarks>
public sealed class RecordingSubscriber
{
    private readonly DispatchLog _log;

    // All four maps use the SUBJECT'S OWN handler-name comparer rather than a locally chosen one. That is
    // deliberate: if the broker's folding rule ever changed, this double would follow it instead of
    // silently disagreeing with it, and a disagreement here would look like a broker defect.
    private readonly Dictionary<string, object?> _returnValues =
        new(SubscriptionOptions.HandlerNameComparer);

    private readonly Dictionary<string, DispatchInterceptor> _interceptors =
        new(SubscriptionOptions.HandlerNameComparer);

    private readonly Dictionary<string, Exception> _failures =
        new(SubscriptionOptions.HandlerNameComparer);

    private readonly Dictionary<string, int> _invocationCounts =
        new(SubscriptionOptions.HandlerNameComparer);

    private string _topic = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingSubscriber"/> class.
    /// </summary>
    /// <param name="log">
    /// The SHARED invocation log. Several subscribers constructed against one log append to it in dispatch
    /// order, which is what makes a cross-subscriber ordering assertion expressible.
    /// </param>
    /// <param name="label">
    /// The label this subscriber writes into every row it records, so several instances are
    /// distinguishable. May be empty; may not be <see langword="null"/>.
    /// </param>
    /// <param name="broker">
    /// The broker to hand to an in-handler callback. Optional - see <see cref="Broker"/> for what
    /// happens when it is omitted.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="log"/> or <paramref name="label"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The two guards are hygiene on an API a suite calls directly, and are NOT normalisation of a
    /// dispatch payload - the distinction C-B turns on. Nothing a handler is HANDED is ever validated,
    /// defaulted or coerced anywhere in this class.
    /// </remarks>
    public RecordingSubscriber(DispatchLog log, string label, EventBroker? broker = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(label);

        _log = log;
        Label = label;
        Broker = broker;
    }

    /// <summary>
    /// Gets the label written into every row this subscriber records.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the broker handed to this subscriber at construction, or <see langword="null"/> when none was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SUBSTITUTION 2 from the file header, and the reason there are two ways to reach the broker.</b>
    /// The legacy hands the broker to its subscribers through the runtime global
    /// <c>Message.PowerObjectParm</c>, which the dispatch loop overwrites with <c>this</c> before invoking
    /// them (<c>n_cst_eventful.sru:L846-L847</c>) and restores afterwards (<c>:L948</c>). The oracle's
    /// handler reads it straight back out (<c>w_test_eventful.srw:L47</c>). .NET has no such global, so
    /// the port substitutes the ambient <c>EventBroker.Current</c>.
    /// </para>
    /// <para>
    /// An in-handler callback is therefore handed <c>this.Broker ?? EventBroker.Current</c>: an explicit
    /// reference wins when one was supplied, and the ambient value is used otherwise. Both paths are
    /// offered because they answer different questions. The explicit reference lets a suite reach a
    /// SPECIFIC broker - which matters when a test runs two brokers, or when a handler must act on the
    /// OUTER broker from inside a nested dispatch that a different broker is running. The ambient value is
    /// the faithful analogue and is the one a suite uses to assert that the substitution itself works.
    /// </para>
    /// </remarks>
    public EventBroker? Broker { get; }

    /// <summary>
    /// Gets or sets the topic this subscriber records against. Empty until a suite sets it.
    /// </summary>
    /// <value>The topic string, never <see langword="null"/>.</value>
    /// <exception cref="ArgumentNullException">Thrown when the value assigned is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>Stated by the suite, not observed from the broker, and that is not a shortcut.</b> A handler is
    /// genuinely never told which event reached it: the broker invokes it with the payload alone and
    /// exposes no in-flight event name, and the legacy is identical - the oracle's
    /// <c>onbuttonclicked1(commandbutton, long, long)</c> (<c>w_test_eventful.srw:L44</c>) has no event-name
    /// parameter either. So a suite subscribing this instance to more than one topic sets this immediately
    /// before each <c>Trigger</c>; leaving it alone simply records an empty topic, which costs nothing when
    /// the suite is not asserting on it.
    /// </remarks>
    public string Topic
    {
        get => _topic;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _topic = value;
        }
    }

    /// <summary>
    /// Gets or sets the callback used for any handler that has no callback of its own.
    /// </summary>
    /// <remarks>
    /// The convenient form for the common case of a subscriber wired to exactly one handler. A per-handler
    /// callback registered through <see cref="SetInterceptor"/> takes precedence over this for that
    /// handler; every other handler falls back here. Both are needed: a suite exercising nested dispatch
    /// typically wants ONE handler to re-enter and the rest to stay inert.
    /// </remarks>
    public DispatchInterceptor? DuringAnyHandler { get; set; }

    /// <summary>
    /// Gets the total number of handler invocations this subscriber has recorded.
    /// </summary>
    /// <remarks>
    /// Counted per subscriber, which the shared log cannot answer without filtering by label. Independent
    /// of <see cref="DispatchLog.Clear"/> - clearing the evidence does not un-run an invocation.
    /// </remarks>
    public int InvocationCount { get; private set; }

    /// <summary>
    /// Returns how many times one named handler has run.
    /// </summary>
    /// <param name="handlerName">
    /// The handler name, matched case-insensitively with the subject's own handler-name comparer.
    /// </param>
    /// <returns>The invocation count, or zero when that handler has never run.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Because the comparer folds case, the two halves of the case-folding pair share one count. The way
    /// to tell them apart is <see cref="DispatchRecord.HandlerName"/>, which carries the declared spelling.
    /// </remarks>
    public int InvocationCountFor(string handlerName)
    {
        ArgumentNullException.ThrowIfNull(handlerName);

        _invocationCounts.TryGetValue(handlerName, out int count);
        return count;
    }

    /// <summary>
    /// Sets the value one named handler returns.
    /// </summary>
    /// <param name="handlerName">The handler name, matched case-insensitively.</param>
    /// <param name="value">
    /// The value to return. <see langword="null"/> is explicitly permitted and is the default.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>That <see langword="null"/> is expressible is load-bearing, not a convenience.</b> The broker
    /// decides the handled state from the return value: only a NON-NULL return participates at all, so a
    /// void handler and a handler that returned null are indistinguishable - "no return value is defined as
    /// not handled" (<c>w_test_eventful.srw:L234</c>). A double that could not return null could not reach
    /// the unhandled path, and the unhandled path is half the capture model: <c>CaptureMode.Unhandled</c>
    /// subscriptions run only while the event is unhandled, <c>CaptureMode.Handled</c> only once it is, and
    /// <c>CaptureMode.All</c> either way.
    /// </para>
    /// <para>
    /// Per handler rather than per instance, so one subscriber can leave an event unhandled through one
    /// handler and handle it through another within a single test.
    /// </para>
    /// </remarks>
    public void SetReturnValue(string handlerName, object? value)
    {
        ArgumentNullException.ThrowIfNull(handlerName);
        _returnValues[handlerName] = value;
    }

    /// <summary>
    /// Returns the value one named handler is configured to return.
    /// </summary>
    /// <param name="handlerName">The handler name, matched case-insensitively.</param>
    /// <returns>The configured value, or <see langword="null"/> when none was configured.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// A configured <see langword="null"/> and an absent configuration are deliberately indistinguishable
    /// through this member, because they are indistinguishable to the broker too: both make the handler
    /// return null, and both therefore read as unhandled.
    /// </remarks>
    public object? ReturnValueFor(string handlerName)
    {
        ArgumentNullException.ThrowIfNull(handlerName);

        _returnValues.TryGetValue(handlerName, out object? value);
        return value;
    }

    /// <summary>
    /// Registers, or removes, the callback one named handler runs from inside its dispatch.
    /// </summary>
    /// <param name="handlerName">The handler name, matched case-insensitively.</param>
    /// <param name="interceptor">
    /// The callback, or <see langword="null"/> to remove the one registered for this handler and fall back
    /// to <see cref="DuringAnyHandler"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> is <see langword="null"/>.
    /// </exception>
    public void SetInterceptor(string handlerName, DispatchInterceptor? interceptor)
    {
        ArgumentNullException.ThrowIfNull(handlerName);

        if (interceptor is null)
        {
            _interceptors.Remove(handlerName);
            return;
        }

        _interceptors[handlerName] = interceptor;
    }

    /// <summary>
    /// Makes one named handler throw the given exception instance.
    /// </summary>
    /// <param name="handlerName">The handler name, matched case-insensitively.</param>
    /// <param name="exception">The exception to throw.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> or <paramref name="exception"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// For the exception-capture suite. <b>The instance is thrown AS IS, so its identity is preserved</b>
    /// and a suite can assert reference equality against what the broker rethrows and can read the
    /// four-line diagnostic block the dispatch records on <see cref="Exception.Data"/> through
    /// <c>EventBroker.GetDispatchExceptionText</c>. That identity matters: the broker rethrows the same
    /// object rather than wrapping it, because the legacy replaced the caught exception's text in place and
    /// rethrew it, and wrapping would change the runtime type an enclosing catch matches.
    /// </para>
    /// <para>
    /// The invocation is RECORDED BEFORE the throw, so a suite can prove the handler ran. Any callback
    /// registered for the handler also runs first, which lets one handler both veto and throw.
    /// </para>
    /// <para>
    /// Passing a type of the suite's choosing is how the assertion-detail path is reached: the broker
    /// special-cases an exception exposing <c>IAssertionDetail</c>, or one whose type name matches the
    /// legacy assertion class, so a suite supplies such a type here rather than needing a hook in this
    /// double.
    /// </para>
    /// <para>
    /// Re-throwing one instance across several invocations is permitted and deterministic; .NET overwrites
    /// its stack trace at each throw, and nothing in these suites asserts on that.
    /// </para>
    /// </remarks>
    public void ThrowOn(string handlerName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(handlerName);
        ArgumentNullException.ThrowIfNull(exception);

        _failures[handlerName] = exception;
    }

    /// <summary>
    /// Makes one named handler throw an <see cref="InvalidOperationException"/> carrying the given message.
    /// </summary>
    /// <param name="handlerName">The handler name, matched case-insensitively.</param>
    /// <param name="message">The message the suite will assert on.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="handlerName"/> or <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The convenience form of <see cref="ThrowOn"/> for the common case where only the message is
    /// interesting - the broker's diagnostic block quotes it, so it is the string a suite matches on. Named
    /// distinctly rather than added as a second <c>ThrowOn</c> overload on purpose: an
    /// <c>Exception</c>-versus-<c>string</c> overload pair makes <c>ThrowOn(name, null)</c> ambiguous at
    /// every future call site, and a shared double that nine suites bind to should not carry that trap.
    /// </remarks>
    public void ThrowMessageOn(string handlerName, string message)
    {
        ArgumentNullException.ThrowIfNull(handlerName);
        ArgumentNullException.ThrowIfNull(message);

        ThrowOn(handlerName, new InvalidOperationException(message));
    }

    /// <summary>
    /// Discards every configured return value, callback and throw, leaving counters and the log untouched.
    /// </summary>
    /// <remarks>
    /// For a suite that reconfigures the same subscribers between phases of one test. It deliberately does
    /// NOT reset <see cref="InvocationCount"/> or clear the log: an invocation that happened stays
    /// recorded, and <see cref="DispatchLog.Clear"/> is the separate, explicit way to drop the evidence.
    /// <see cref="DuringAnyHandler"/> is cleared too, so a reset really does leave every handler inert.
    /// </remarks>
    public void ResetConfiguration()
    {
        _returnValues.Clear();
        _interceptors.Clear();
        _failures.Clear();
        DuringAnyHandler = null;
    }


    // =================================================================================================
    //  THE HANDLER SET - SUBSTITUTION 1 FROM THE FILE HEADER, MADE CONCRETE
    //
    //  The legacy published ELEVEN fixed of_trigger overloads carrying zero through ten payload arguments
    //  (n_cst_eventful.sru:L119-L129) and ELEVEN fixed of_post overloads carrying ten down to zero
    //  (:L132-L142). Both families collapse onto ONE variadic method each in the port. These handlers are
    //  the other side of that collapse: they span the arity range so a suite can observe what the broker
    //  does when the handler's arity and the payload's length disagree, which is the ONE argument rule the
    //  oracle documents in prose and which no single-arity double can reach:
    //
    //      supplied > declared   the surplus is SILENTLY DISCARDED   (w_test_eventful.srw:L247)
    //      supplied < declared   the shortfall gets each parameter type's PowerScript INITIAL VALUE
    //                            (:L246), which is "" for a string, the zero value for a value type and
    //                            null for any other reference type
    //
    //  Both come from one expression - Min(declared arity - consumed, supplied count), the port of
    //  _of_passargs's Min at n_cst_eventful.sru:L613 - so a suite that pins one has not pinned the other.
    //
    //  EVERY HANDLER IS public. Resolution would also find a non-public method, since ResolveHandler
    //  passes BindingFlags.NonPublic, but a subscriber double whose handlers are part of its published
    //  surface is the honest shape: these ARE its API, nine suites bind to them by name, and
    //  RecordingHandlerNames turns each name into a compile-checked constant.
    //
    //  EVERY PARAMETER IS READ. Each is recorded, so no unused-parameter discard is needed anywhere and
    //  C-H's no-unused-parameter requirement is met by construction rather than by suppression.
    // =================================================================================================

    /// <summary>
    /// A zero-parameter handler.
    /// </summary>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The shortest handler the broker can resolve, and therefore the one that shows the discard rule at
    /// its extreme: triggered with any payload at all it records an EMPTY argument list, because
    /// <c>Min(0 - 0, supplied)</c> is zero. It is also the handler an ordering suite reaches for, since a
    /// pure ordering assertion wants no payload noise in the log.
    /// </remarks>
    public object? OnNoArguments() => Record(RecordingHandlerNames.NoArguments, []);

    /// <summary>
    /// A one-parameter handler.
    /// </summary>
    /// <param name="first">The first payload argument, recorded exactly as received.</param>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The shape of the oracle's own <c>ontest(string arg)</c> (<c>w_test_eventful.srw:L87</c>), which its
    /// nested trigger calls with one argument (<c>:L80</c>). Declared <c>object?</c> rather than
    /// <c>string</c> so the payload arrives untouched - see <see cref="OnTypedArguments"/> for the
    /// deliberately typed counterpart.
    /// </remarks>
    public object? OnOneArgument(object? first) => Record(RecordingHandlerNames.OneArgument, first);

    /// <summary>
    /// A four-parameter handler, the middling arity.
    /// </summary>
    /// <param name="first">The first payload argument.</param>
    /// <param name="second">The second payload argument.</param>
    /// <param name="third">The third payload argument.</param>
    /// <param name="fourth">The fourth payload argument.</param>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Four rather than three so that it is neither the shortest nor the longest of anything, which makes
    /// it the natural handler for asserting that ORDER is preserved: with four distinct values a
    /// transposition is visible, whereas with two it can hide behind a symmetric expectation. Its arity
    /// also brackets the real consumers' - the DataWindow service's own subscriptions run from one
    /// argument to five (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L58-L72</c>).
    /// </remarks>
    public object? OnFourArguments(object? first, object? second, object? third, object? fourth) =>
        Record(RecordingHandlerNames.FourArguments, first, second, third, fourth);

    /// <summary>
    /// A ten-parameter handler, sitting exactly on the legacy arity ceiling.
    /// </summary>
    /// <param name="first">The first payload argument.</param>
    /// <param name="second">The second payload argument.</param>
    /// <param name="third">The third payload argument.</param>
    /// <param name="fourth">The fourth payload argument.</param>
    /// <param name="fifth">The fifth payload argument.</param>
    /// <param name="sixth">The sixth payload argument.</param>
    /// <param name="seventh">The seventh payload argument.</param>
    /// <param name="eighth">The eighth payload argument.</param>
    /// <param name="ninth">The ninth payload argument.</param>
    /// <param name="tenth">The tenth payload argument.</param>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// TEN is where the legacy stopped: <c>of_trigger</c>'s longest overload declares <c>param1</c> through
    /// <c>param10</c> (<c>n_cst_eventful.sru:L129</c>) and <c>of_post</c>'s does the same
    /// (<c>:L132</c>). This handler is the boundary case, so a suite can pin that all ten arrive, in order,
    /// with nothing lost at the edge.
    /// </remarks>
    public object? OnTenArguments(
        object? first,
        object? second,
        object? third,
        object? fourth,
        object? fifth,
        object? sixth,
        object? seventh,
        object? eighth,
        object? ninth,
        object? tenth) =>
        Record(
            RecordingHandlerNames.TenArguments,
            first,
            second,
            third,
            fourth,
            fifth,
            sixth,
            seventh,
            eighth,
            ninth,
            tenth);

    /// <summary>
    /// A twelve-parameter handler, deliberately BEYOND the legacy arity ceiling.
    /// </summary>
    /// <param name="first">The first payload argument.</param>
    /// <param name="second">The second payload argument.</param>
    /// <param name="third">The third payload argument.</param>
    /// <param name="fourth">The fourth payload argument.</param>
    /// <param name="fifth">The fifth payload argument.</param>
    /// <param name="sixth">The sixth payload argument.</param>
    /// <param name="seventh">The seventh payload argument.</param>
    /// <param name="eighth">The eighth payload argument.</param>
    /// <param name="ninth">The ninth payload argument.</param>
    /// <param name="tenth">The tenth payload argument.</param>
    /// <param name="eleventh">The eleventh payload argument, one past the legacy ceiling.</param>
    /// <param name="twelfth">The twelfth payload argument, two past the legacy ceiling.</param>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>This handler exists to prove a point about the ceiling, and the point is that it was never a
    /// contract.</b> The legacy stopped at ten only because PowerScript cannot forward an arbitrary-length
    /// argument list, so its author had to hand-write each arity and chose to stop; C# <c>params</c>
    /// forwards natively, so the .NET contract may exceed ten WITHOUT behavioural regression. AAP 0.2.1.4
    /// records the same treatment for the refactor's other legacy arity ceilings - twenty in
    /// <c>n_cst_thread_task_sqlbase</c>, eight in <c>n_cst_thread_trans</c>, eleven in <c>n_sqlite</c> - and
    /// names them legacy limits rather than behavioural rules.
    /// </para>
    /// <para>
    /// Twelve rather than eleven so the assertion cannot pass by an off-by-one: eleven would be satisfied by
    /// an implementation that merely mis-counted the boundary, whereas twelve is unambiguously past it.
    /// </para>
    /// </remarks>
    public object? OnTwelveArguments(
        object? first,
        object? second,
        object? third,
        object? fourth,
        object? fifth,
        object? sixth,
        object? seventh,
        object? eighth,
        object? ninth,
        object? tenth,
        object? eleventh,
        object? twelfth) =>
        Record(
            RecordingHandlerNames.TwelveArguments,
            first,
            second,
            third,
            fourth,
            fifth,
            sixth,
            seventh,
            eighth,
            ninth,
            tenth,
            eleventh,
            twelfth);

    /// <summary>
    /// A strongly-typed handler: the only one whose parameters are not <see cref="object"/>.
    /// </summary>
    /// <param name="count">A numeric parameter, which a compatible narrower number widens into.</param>
    /// <param name="text">A string parameter, whose PowerScript initial value is the EMPTY STRING.</param>
    /// <param name="flag">A boolean parameter, whose initial value is <see langword="false"/>.</param>
    /// <returns>The value configured for this handler, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>Required, not decorative: an all-<c>object?</c> handler set cannot observe half the argument
    /// contract.</b> An <c>object</c> parameter's initial value IS null, and an explicitly passed null is
    /// also null, so on an <c>object?</c> handler "the payload never reached this slot" and "the payload
    /// carried null here" are indistinguishable. These three types make both halves visible:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   trigger with fewer than three arguments and the unreached slots record <c>""</c> and
    ///   <see langword="false"/> rather than null - the rule <c>w_test_eventful.srw:L246</c> states with a
    ///   worked example, whose <c>arg2</c> receives <c>''</c>;
    ///   </description></item>
    ///   <item><description>
    ///   trigger with an <see cref="int"/> where <see cref="long"/> is declared and it arrives widened,
    ///   which is the "types must be COMPATIBLE, not identical" half of <c>:L240</c>;
    ///   </description></item>
    ///   <item><description>
    ///   trigger with a number where <see cref="string"/> is declared and the conversion is deliberately
    ///   NOT performed, because PowerScript does not implicitly stringify into a string argument - the
    ///   resulting reflection failure travels the dispatch's own exception-capture path, which is where a
    ///   legacy type mismatch surfaced too.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The recorded arguments are boxed on the way into the log, which is a representation change and not a
    /// value change: <c>0L</c> is recorded as a boxed <see cref="long"/> and compares equal to <c>0L</c>,
    /// never to <c>0</c> as an <see cref="int"/>. A suite asserting on this handler states its expectations
    /// with the same types the parameters declare.
    /// </para>
    /// </remarks>
    public object? OnTypedArguments(long count, string text, bool flag) =>
        Record(RecordingHandlerNames.TypedArguments, count, text, flag);

    /// <summary>
    /// The zero-parameter half of the case-folding pair, spelled with a capital <c>F</c>.
    /// </summary>
    /// <returns>The value configured for the folded key, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>This handler and <see cref="OnCasefolded"/> differ ONLY by the case of one letter, on purpose.</b>
    /// The legacy folds a handler name to lower case when it stores a subscription
    /// (<c>newEvent.evtName = Lower(evtName)</c>, <c>n_cst_eventful.sru:L336</c>) and its own documentation
    /// spells the point out at <c>w_test_eventful.srw:L250</c>: the EVENT name is case-sensitive while the
    /// CALLBACK name is not. The port matches handler names with
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, so subscribing with any casing of this name reaches
    /// the same handler - which is exactly what the pair lets a suite pin.
    /// </para>
    /// <para>
    /// <b>THE ARITIES DIFFER DELIBERATELY, AND CHANGING THAT WOULD MAKE THE SUITES NON-DETERMINISTIC.</b>
    /// Resolution collects every case-insensitive name match and then DROPS any candidate whose parameter
    /// list a candidate it already holds matches, on the assumption that the two are one overridable member
    /// seen twice. Two same-arity methods differing only by case would therefore collapse onto whichever
    /// one <c>Type.GetMethods</c> happened to return first - and reflection guarantees no member order. With
    /// the arities different, both survive as candidates and the documented tie-break applies instead:
    /// with no signature filter the FEWEST-parameter candidate wins, so this one runs; with a signature
    /// filter naming one parameter, <see cref="OnCasefolded"/> runs. Both outcomes are fixed by the
    /// subject's stated rules rather than by reflection's ordering.
    /// </para>
    /// <para>
    /// One consequence to know before configuring them: because the configuration maps fold case too, the
    /// pair SHARES one key for its return value, its callback and its throw. Which of the two actually ran
    /// is read from <see cref="DispatchRecord.HandlerName"/>, which carries the declared spelling.
    /// </para>
    /// </remarks>
    public object? OnCaseFolded() => Record(RecordingHandlerNames.CasefoldedPairCapitalF, []);

    /// <summary>
    /// The one-parameter half of the case-folding pair, spelled with a lower-case <c>f</c>.
    /// </summary>
    /// <param name="first">The first payload argument, recorded exactly as received.</param>
    /// <returns>The value configured for the folded key, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The counterpart to <see cref="OnCaseFolded"/>, which carries the full explanation of the pair, of why
    /// their arities differ, and of how a suite tells which one ran. Selected in preference to its sibling
    /// only by a signature filter naming one parameter, never by the casing used to subscribe.
    /// </remarks>
    public object? OnCasefolded(object? first) => Record(RecordingHandlerNames.CasefoldedPairLowerF, first);

    // =================================================================================================
    //  THE RECORDING CORE
    // =================================================================================================

    /// <summary>
    /// Records one invocation, runs any in-handler callback, applies any configured throw, and answers the
    /// configured return value.
    /// </summary>
    /// <param name="handlerName">The DECLARED name of the calling handler.</param>
    /// <param name="arguments">
    /// The arguments the handler received, in the order it received them and with nothing added or removed.
    /// </param>
    /// <returns>The value configured for <paramref name="handlerName"/>, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>The order of the four steps is itself a contract the suites depend on.</b>
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <b>Record first.</b> Before the callback and before any throw, so an invocation that vetoes or
    ///   fails is still evidenced. A double that recorded last would report a throwing handler as never
    ///   having run, which is precisely what the exception-capture suite needs to see the opposite of.
    ///   </description></item>
    ///   <item><description>
    ///   <b>Count second</b>, for the same reason.
    ///   </description></item>
    ///   <item><description>
    ///   <b>Callback third</b>, while the dispatch is still in flight and the broker's dispatch-level state
    ///   - the handled latch, the accumulated return value, the post flag, the veto - is still live. It is
    ///   handed the row just appended, so it can read exactly what was recorded.
    ///   </description></item>
    ///   <item><description>
    ///   <b>Throw fourth</b>, after the callback, so one handler can both veto and throw. A suite wanting a
    ///   throw with no side effect simply registers no callback.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Nothing here inspects, filters, reorders, coerces or defaults <paramref name="arguments"/>. That
    /// restraint is C-B applied to a test double: the suites assert that the BROKER loses no argument, so
    /// any tidying here would move the property under test into this file.
    /// </para>
    /// </remarks>
    private object? Record(string handlerName, params object?[] arguments)
    {
        // STEP 1 - the evidence, before anything that can divert or abort this invocation. DispatchRecord
        // takes its own defensive copy, so the params array this method allocated is never aliased into the
        // log even though it is already private to this call.
        DispatchRecord record = _log.Append(Label, handlerName, _topic, arguments);

        // STEP 2 - the counters. Per instance and per handler; the shared log cannot answer either without
        // filtering, and the per-handler map folds case exactly as the broker does.
        InvocationCount++;
        _invocationCounts.TryGetValue(handlerName, out int previous);
        _invocationCounts[handlerName] = previous + 1;

        // STEP 3 - the in-handler callback, with the broker resolved by SUBSTITUTION 2: an explicit
        // reference wins, and the ambient EventBroker.Current - the Message.PowerObjectParm stand-in - is
        // used otherwise. Null is passed through rather than guarded against, because a handler invoked
        // directly by a suite genuinely has no dispatch and no ambient broker, and the callback is entitled
        // to observe that rather than be denied the call.
        if (!_interceptors.TryGetValue(handlerName, out DispatchInterceptor? interceptor))
        {
            interceptor = DuringAnyHandler;
        }

        interceptor?.Invoke(Broker ?? EventBroker.Current, record);

        // STEP 4 - the configured failure, thrown as the very instance the suite supplied so its identity
        // survives into the broker's capture path.
        if (_failures.TryGetValue(handlerName, out Exception? failure))
        {
            throw failure;
        }

        // The answer. An absent entry yields null, which the broker reads as "not handled" - the same
        // outcome as a configured null, and deliberately indistinguishable from it.
        _returnValues.TryGetValue(handlerName, out object? value);
        return value;
    }
}
