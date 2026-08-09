// ==================================================================================================
//  TestDoubles.cs - the hand-written doubles and determinism seams shared across this test project.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS, AND WHAT IT IS NOT
//
//  `FakeDataWindowHost.cs` owns the DataWindow host double - the single largest and most specialised
//  double in the suite. This file owns EVERYTHING ELSE that more than one test class needs:
//
//    1. the event-broker recording seam, including the DECOMPOSED topic identity   (section 1)
//    2. the localization provider seam, and the exact legacy message keys          (section 2)
//    3. the macro-invocation and expression-trace seams of contract C-04           (section 3)
//    4. the two outbound-edge seams: HTTP and the Persistence gRPC answers         (section 4)
//    5. the determinism seams - a fixed clock and a fully specified PRNG           (section 5)
//
//  It is NOT a place for a double that exactly one test class uses. Several such doubles already live
//  beside their suite (`RecordingMacroChannel` and `RecordingTraceSink` in ColumnExpressionEngineTests,
//  `RecordingHandler` and `MutableClock` in SecurityClientTests, the four generated-stub doubles and
//  `PersistenceListStreamReader<T>`/`PersistenceCallFactory` in PersistenceClientTests, and
//  `RecordingI18nProvider` in RowSelectServiceTests). Those are deliberately NOT moved here and
//  deliberately NOT duplicated here: moving them would rewrite files this file has no mandate over,
//  and duplicating them would create a second source of truth for the same behaviour. Where a type
//  below overlaps one of them in purpose, the difference in shape is stated on the type, and the
//  transport-level plumbing that already exists (the gRPC call wrappers and the list-backed stream
//  reader) is REUSED rather than re-implemented - which is why section 4 produces messages and
//  failures rather than calls.
//
//  WHY EVERY DOUBLE IS HAND-WRITTEN
//  ------------------------------------------------------------------------------------------------
//  AAP 0.5.1 lists the entire dependency inventory for this service, and it contains NO mocking
//  library and NO fluent assertion library; AAP 0.5.3 then forbids adding packages, on the ground
//  that anything not required by the technology transition is scope creep. So every double here is
//  written by hand and every assertion in the consuming files uses plain xunit `Assert`. That is a
//  constraint rather than a preference, and it has one happy consequence worth naming: a hand-written
//  double is READ as a specification, whereas a configured mock has to be reconstructed from its
//  setup calls.
//
//  THE GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH  (AAP 0.7.3)
//  ------------------------------------------------------------------------------------------------
//  * C-B, no behaviour improvements. EVERY DOUBLE HERE IS SCRIPTABLE AND INERT. It records what it
//    was given and returns what the test told it to return - it never normalises, never sorts, never
//    retries, never coalesces, never "corrects". This is the single most important property in the
//    file, because these doubles exist to pin PRESERVED DEFECTS, and a double that tidied its input
//    would hide the very defect its suite was written to catch. Two concrete cases:
//      - the broker recorder keeps dispatches in ARRIVAL order and never sorts them, because
//        `Domain/DataWindowEventChain.cs` dispatches and sorts by `SubscriptionTopic.LegacyName` and
//        two of the twelve legacy topics carry a lexical ordering prefix inside the constant VALUE
//        ("0-itemchanged" at se_cst_dw.sru:L54, "1-editchanged" at :L57). A double that sorted by
//        `LogicalName` would hide an ordering regression outright;
//      - the macro channel can answer with a value the engine must REFUSE, because two of the
//        engine's error paths are reachable only that way.
//  * C-E, no fabricated database. Nothing here touches SQLite, EF Core, the file system, or a socket.
//  * C-F, secrets are never replicated. No key, certificate, password, token or connection string
//    appears in this file in any form, and specifically nothing is copied from
//    `tests/blink/test_jws.htm` or `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw`, which are
//    two of the eight in-source secret sites AAP 0.6.6.1 inventories. Where a credential-shaped value
//    is unavoidable - a bearer credential has to be SOMETHING for the header to carry - it is
//    GENERATED LOCALLY AT TEST TIME by `DeterministicRandomSource` behind an obviously-fake prefix,
//    so it cannot match any provider's credential pattern and cannot be mistaken for real material.
//    The HTTP recorder additionally records only the PRESENCE and the SCHEME of an Authorization
//    header, never its parameter, so a captured exchange cannot leak a credential into an assertion
//    message or a CI log.
//  * C-G, every new boundary is authenticated. No double here switches authentication off. The HTTP
//    recorder neither adds nor strips an Authorization header - it observes one and reports whether it
//    was there, which is what lets a test PROVE the credential was attached. Deciding whether a
//    given in-process host requires a bearer token is the host factory's business, not a double's.
//  * C-K, document every decision. Every reproduced legacy quirk below names its oracle locator.
//  * AAP 0.7.2 baseline. Compiles warning-clean under nullable reference types with
//    `TreatWarningsAsErrors`, which the repository-root `Directory.Build.props` sets for every project
//    including this one.
//
//  NAMING - WHY NOTHING HERE IS SCREAMING_SNAKE
//  ------------------------------------------------------------------------------------------------
//  The repository-root `.editorconfig` lowers CA1707 and IDE1006 to `none` for exactly seven paths
//  (RetCode.cs, Enums.cs, Categories.cs, LegacyDefaults.cs, ClauseModifier.cs, EventGate.cs and
//  ItemChangeProtocol.cs), which are the constant catalogues whose legacy identifier SPELLINGS are
//  themselves recording-visible artifacts. NO TEST FILE IS IN THAT LIST, this one included. So this
//  file REFERENCES legacy constants freely by their preserved names - `RetCode.E_INVALID_ARGUMENT`,
//  `Categories.CAT_DWSVC`, `DataWindowEventChain.EVT_ITEMCHANGED` - and DECLARES none of its own.
//  Referencing is also the better engineering: `LegacyMessageKeys` below surfaces the four legacy
//  message keys by pointing at the constants the application already declares, so there is exactly
//  one source of truth for each string and no way for the two to drift apart.
//
//  THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY
//  ------------------------------------------------------------------------------------------------
//  Every `:Lnnn` locator in this file points into `ws_objects/` or `docs/`, which are the behavioural
//  oracle: read as specification, never edited, moved or reformatted. Every line number below was
//  verified against the file on disk rather than copied forward from another document.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;
using Xunit;

// ALIASED RATHER THAN IMPORTED WHOLESALE, for the reason DataWindowEventChainTests states: the
// contracts assembly and the application assembly each publish types whose simple names collide - most
// sharply `RetCode`, which names the ported constant catalogue on one side and the generated protobuf
// wrapper message on the other, so a bare use is CS0104. Naming the types this file needs keeps every
// use unambiguous without importing either contracts namespace and without a `global::` prefix at the
// point of use.
using AnyValue = PowerFramework.Contracts.Common.V1.AnyValue;
using CarrierBufferSegment = PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment;
using CarrierState = PowerFramework.Contracts.Persistence.V1.CarrierState;
using ColumnValue = PowerFramework.Contracts.Common.V1.ColumnValue;
using CommonV1Extensions = PowerFramework.Contracts.Common.V1.CommonV1Extensions;
using ConflictDetail = PowerFramework.Contracts.Common.V1.ConflictDetail;
using ConflictRow = PowerFramework.Contracts.Common.V1.ConflictRow;
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ExpansionMode = PowerFramework.Contracts.DataServices.V1.ExpansionMode;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using OperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using QueryDataChunk = PowerFramework.Contracts.Persistence.V1.QueryDataChunk;
using QueryResponse = PowerFramework.Contracts.Persistence.V1.QueryResponse;
using QueryRowCount = PowerFramework.Contracts.Persistence.V1.QueryRowCount;
using RichErrorBinding = PowerFramework.Contracts.Common.V1.RichErrorBinding;
using RichErrorTrailer = PowerFramework.Contracts.Common.V1.RichErrorTrailer;
using SetChunkSizeResponse = PowerFramework.Contracts.Persistence.V1.SetChunkSizeResponse;
using UpdateService = PowerFramework.Contracts.Persistence.V1.UpdateService;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

// ==================================================================================================
//  SECTION 1 - THE EVENT-BROKER RECORDING SEAM
//  ------------------------------------------------------------------------------------------------
//  Oracle: ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru (1,328 lines, pure PowerScript)
//          and its twelve subscription topics at
//          ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L47-L76.
//
//  THREE THINGS THIS SEAM MUST GET RIGHT, each of which a naive recorder gets wrong:
//
//  1. THE TOPIC IS THREE ENCODINGS FUSED INTO ONE STRING, and the recorder must keep all three.
//     AAP 0.6.1.2: the legacy topic string carries a LEXICAL ORDERING PREFIX, the LOGICAL NAME and -
//     through the `.^persistent` namespace convention seen at
//     ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544,L596 - a LIFETIME, all in one opaque
//     value. `shared/PowerFramework.Shared.Eventful/SubscriptionTopic.cs` decomposes it, and every
//     record below carries the decomposed topic so a test can assert on whichever encoding it means.
//     DIVERGENCE FROM THE PROMPT'S PARAPHRASE, NOTED AS INSTRUCTED: `SubscriptionTopic` does not hold
//     `sequence`, `name` and `lifetime` as three STORED fields. `LegacyName` and `NamespaceCriterion`
//     are stored; `Sequence`, `LogicalName` and `Lifetime` are COMPUTED PROJECTIONS of them, recomputed
//     on each read precisely so the reconstitution invariant cannot be broken (see the decision note on
//     `SubscriptionTopic.Sequence`). This file therefore stores the topic OBJECT and projects the three
//     wire fields from it, rather than copying three values that could then disagree.
//
//  2. DISPATCH ORDER IS THE ASSERTION, so the log never sorts. `DataWindowEventChain` dispatches and
//     sorts by `SubscriptionTopic.LegacyName`, NEVER by `LogicalName` - the ordering prefix lives in
//     the legacy name and is absent from the logical one. `ScriptedDispatchLog` is append-only and
//     hands its records back in arrival order.
//
//  3. THE VETO IS TRI-VALUED. `VetoResult` is Continue = 0, PreventOnce = 1, PreventDeep = 2, and the
//     difference between the two preventions is what SURVIVES the unwind of a nested dispatch
//     (n_cst_eventful.sru:L954-L957). Nothing here exposes a boolean veto, because flattening one
//     silently converts every deep prevention into a shallow one - and only for nested dispatches,
//     which is the failure mode hardest to notice.
// ==================================================================================================

/// <summary>
/// Which point of a broker dispatch a <see cref="ScriptedDispatch"/> record was taken at.
/// </summary>
/// <remarks>
/// One record type and one log serve all five points, so a single ordered sequence shows the whole
/// dispatch rather than forcing a reader to interleave several lists by hand. The five values are the
/// four overridable broker hooks plus the handler body itself; see
/// <c>shared/PowerFramework.Shared.Eventful/EventBroker.cs</c> for the hooks and
/// <c>n_cst_eventful.sru:L33-L36</c> for the legacy events they port.
/// </remarks>
internal enum ScriptedDispatchStage
{
    /// <summary>
    /// The <c>OnTriggering</c> hook - fired once per dispatch, on the FIRST matching subscription only,
    /// so a dispatch with no matching subscription never produces this record at all
    /// [n_cst_eventful.sru:L839-L843].
    /// </summary>
    Triggering = 0,

    /// <summary>
    /// The <c>OnPrepare</c> hook - fired once per subscriber, before that subscriber's handler, and the
    /// only point at which the argument slots are visible to the broker [n_cst_eventful.sru:L604-L609].
    /// </summary>
    Prepare = 1,

    /// <summary>
    /// A subscriber's handler body. This is the record that carries the ARGUMENTS the handler actually
    /// received and the VALUE it returned.
    /// </summary>
    Handler = 2,

    /// <summary>
    /// The <c>OnException</c> hook - fired when a handler threw [n_cst_eventful.sru:L889-L895]. Its
    /// return value is a THIRD numeric alphabet in which 1 means prevent and 2 means CONTINUE, the
    /// near-opposite of <see cref="VetoResult.PreventDeep"/> at the same numeral.
    /// </summary>
    Exception = 3,

    /// <summary>
    /// The <c>OnTriggered</c> hook - fired after the dispatch loop, and only when at least one
    /// subscription was dispatched.
    /// </summary>
    Triggered = 4
}

/// <summary>
/// One recorded point of one broker dispatch: where it was taken, which topic it belonged to, the
/// arguments in play, and the value produced.
/// </summary>
/// <remarks>
/// <para>
/// A record is immutable and is stamped with a monotonic <see cref="Ordinal"/> by
/// <see cref="ScriptedDispatchLog"/>, so a test can assert relative order without depending on list
/// indices that shift when an unrelated dispatch is added to a scenario.
/// </para>
/// <para>
/// <b>The three wire fields are projections, not copies.</b> <see cref="Sequence"/>,
/// <see cref="LogicalName"/> and <see cref="Lifetime"/> all read through <see cref="Topic"/> so they
/// cannot disagree with it. When <see cref="Topic"/> is <see langword="null"/> - a dispatch to a name
/// that is not one of the twelve, which is legal and which a test may well want to drive - the legacy
/// and logical names both fall back to the raw <see cref="Name"/> and the other two report
/// <see langword="null"/>. Falling back rather than throwing is deliberate: a recorder that refused to
/// record an unexpected dispatch would destroy the evidence that the dispatch happened.
/// </para>
/// </remarks>
internal sealed record ScriptedDispatch
{
    /// <summary>The position of this record in its log, one-based, assigned on append.</summary>
    /// <remarks>
    /// ONE-BASED to match the surrounding codebase's treatment of ordinals, and because every ported
    /// index in this service is one-based for the reason AAP 0.4.5.4 gives. It is a log position, not an
    /// array index, so nothing indexes with it.
    /// </remarks>
    public required int Ordinal { get; init; }

    /// <summary>Which point of the dispatch this record was taken at.</summary>
    public required ScriptedDispatchStage Stage { get; init; }

    /// <summary>
    /// The event name exactly as it was dispatched - the broker's own dispatch key, which is
    /// <see cref="SubscriptionTopic.LegacyName"/> for any of the twelve legacy topics.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The decomposed topic identity, or <see langword="null"/> when <see cref="Name"/> did not parse as
    /// a subscription topic.
    /// </summary>
    public SubscriptionTopic? Topic { get; init; }

    /// <summary>
    /// The arguments in play at this point, in slot order, empty when the stage carries none.
    /// </summary>
    /// <remarks>
    /// For <see cref="ScriptedDispatchStage.Handler"/> these are the values the handler's parameters
    /// received, which is what makes the broker's argument injection observable: the broker passes
    /// <c>Min(declared - consumed, payload)</c> values starting after the last slot the prepare hook
    /// consumed [n_cst_eventful.sru:L604-L605], so the CONTENT of this array is evidence about the
    /// injection and not merely about the payload.
    /// </remarks>
    public ImmutableArray<object?> Arguments { get; init; } = [];

    /// <summary>
    /// The value produced at this point: a handler's return value, or a hook's return code.
    /// </summary>
    public object? ReturnValue { get; init; }

    /// <summary>
    /// The veto state this point requested, expressed on the tri-valued contract and defaulting to
    /// <see cref="VetoResult.Continue"/>.
    /// </summary>
    public VetoResult Veto { get; init; }

    /// <summary>
    /// What <see cref="EventBroker.Prevent(bool)"/> answered when <see cref="Veto"/> was raised, or
    /// <see langword="null"/> when no veto was raised at this point.
    /// </summary>
    /// <remarks>
    /// Recorded because the answer is itself observable behaviour: a veto raised outside a dispatch is
    /// reported as <see cref="RetCode.FAILED"/> rather than silently ignored [n_cst_eventful.sru:L1293],
    /// so a test that scripts a veto on a subscriber which was never dispatched can prove the veto did
    /// not take effect.
    /// </remarks>
    public long? VetoOutcome { get; init; }

    /// <summary>
    /// Whether the broker reported a subscriber for <see cref="Name"/> at the moment this record was
    /// taken, or <see langword="null"/> when the stage did not sample it.
    /// </summary>
    /// <remarks>
    /// THE PRE-CHECK IS OBSERVABLE BEHAVIOUR, WHICH IS WHY IT IS RECORDED. The oracle asks
    /// <c>of_IsSubscribed</c> BEFORE triggering at two sites - <c>se_cst_dw.sru:L166</c> for the
    /// edit-changed topic and <c>:L316</c> for the item-changed topic - and only those two, so the
    /// presence or absence of the question is part of what a conformance test asserts.
    /// </remarks>
    public bool? HadSubscriber { get; init; }

    /// <summary>Whether the dispatch was queued by <c>Post</c> rather than triggered directly.</summary>
    public bool IsPost { get; init; }

    /// <summary>
    /// A caller-chosen label identifying which double produced the record, so several subscribers can
    /// share one log and still be told apart.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// The exception a handler threw, for a <see cref="ScriptedDispatchStage.Exception"/> record.
    /// </summary>
    public Exception? Fault { get; init; }

    /// <summary>
    /// <b>Wire field 1 of 3.</b> The dispatch key and ordinal sort key - the whole of the legacy
    /// spelling, ordering prefix included.
    /// </summary>
    /// <value>
    /// <c>"0-itemchanged"</c> for the item-changed topic [se_cst_dw.sru:L54]. Falls back to
    /// <see cref="Name"/> when <see cref="Topic"/> is <see langword="null"/>.
    /// </value>
    public string LegacyName => Topic?.LegacyName ?? Name;

    /// <summary>
    /// <b>Wire field 2 of 3.</b> The legacy spelling with the <c>[digits]-</c> ordering prefix removed.
    /// </summary>
    /// <value>
    /// <c>"itemchanged"</c> for the item-changed topic. NOTHING DISPATCHES OR SORTS BY THIS - it is the
    /// identity a contract presents to a consumer that has no business knowing how the legacy encoded
    /// ordering, and asserting order against it would hide an ordering regression.
    /// </value>
    public string LogicalName => Topic?.LogicalName ?? Name;

    /// <summary>
    /// <b>Wire field 3 of 3, part one.</b> The ordering prefix's numeric value, or
    /// <see langword="null"/> when the name carries none.
    /// </summary>
    /// <value><c>0</c> for <c>"0-itemchanged"</c>, <c>1</c> for <c>"1-editchanged"</c>, otherwise
    /// <see langword="null"/> - which is the case for ten of the twelve legacy topics.</value>
    public int? Sequence => Topic?.Sequence;

    /// <summary>
    /// <b>Wire field 3 of 3, part two.</b> Whether the subscription is spared by the
    /// <c>".^persistent"</c> bulk unsubscribe, or <see langword="null"/> when no topic was resolved.
    /// </summary>
    public SubscriptionLifetime? Lifetime => Topic?.Lifetime;

    /// <summary>A compact one-line rendering, for a failure message that has to show the whole log.</summary>
    /// <returns>The rendering.</returns>
    public override string ToString()
    {
        StringBuilder text = new();

        text.Append(Ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(". ")
            .Append(Stage)
            .Append(' ')
            .Append(LegacyName);

        if (Label is not null)
        {
            text.Append(" [").Append(Label).Append(']');
        }

        if (!Arguments.IsDefaultOrEmpty)
        {
            text.Append(" args=").Append(Arguments.Length.ToString(CultureInfo.InvariantCulture));
        }

        if (Veto != VetoResult.Continue)
        {
            text.Append(" veto=").Append(Veto);
        }

        if (HadSubscriber is not null)
        {
            text.Append(" subscribed=").Append(HadSubscriber.Value ? "yes" : "no");
        }

        if (Fault is not null)
        {
            text.Append(" fault=").Append(Fault.GetType().Name);
        }

        return text.ToString();
    }
}

/// <summary>
/// An append-only, ordered record of broker dispatches, shared by every double in section 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>IT NEVER SORTS AND NEVER DEDUPLICATES.</b> Arrival order IS the assertion, for the reason stated
/// at the head of this section: the chain dispatches and sorts by
/// <see cref="SubscriptionTopic.LegacyName"/>, and a log that re-ordered its records - by logical name,
/// by topic, by anything - would silently absorb an ordering regression. The projections below select
/// and filter; none of them reorders.
/// </para>
/// <para>
/// <b>Guarded by a lock, because a dispatch can reach it from a continuation.</b> The broker's posted
/// continuations are drained by <c>DrainPostedContinuations</c>, and a validation session queues one
/// where the oracle would have posted to the Win32 message queue [se_cst_dw.sru:L387-L393]. The lock
/// costs nothing at test scale and removes a whole class of intermittent failure.
/// </para>
/// </remarks>
internal sealed class ScriptedDispatchLog
{
    private readonly object _gate = new();
    private readonly List<ScriptedDispatch> _records = [];

    /// <summary>Every record, in arrival order.</summary>
    /// <remarks>
    /// A snapshot: the returned array is disconnected from this log, so a test can iterate it while the
    /// code under test continues to dispatch without risking a concurrent-modification failure that
    /// would look like a product defect.
    /// </remarks>
    public ImmutableArray<ScriptedDispatch> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    /// <summary>How many records have been appended.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    /// <summary>Appends a record, stamping it with the next one-based ordinal.</summary>
    /// <param name="stage">Which point of the dispatch the record was taken at.</param>
    /// <param name="name">The event name as dispatched.</param>
    /// <param name="arguments">The arguments in play, or <see langword="null"/> for none.</param>
    /// <param name="returnValue">The value produced, if any.</param>
    /// <param name="veto">The veto state requested, defaulting to <see cref="VetoResult.Continue"/>.</param>
    /// <param name="vetoOutcome">What the broker answered when the veto was raised, if it was.</param>
    /// <param name="hadSubscriber">Whether a subscriber was reported, when the stage sampled it.</param>
    /// <param name="isPost">Whether the dispatch was queued rather than triggered.</param>
    /// <param name="label">Which double produced the record.</param>
    /// <param name="fault">The exception a handler threw, if it threw.</param>
    /// <returns>The appended record, so a caller can inspect the ordinal it was given.</returns>
    public ScriptedDispatch Append(
        ScriptedDispatchStage stage,
        string name,
        IReadOnlyList<object?>? arguments = null,
        object? returnValue = null,
        VetoResult veto = VetoResult.Continue,
        long? vetoOutcome = null,
        bool? hadSubscriber = null,
        bool isPost = false,
        string? label = null,
        Exception? fault = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Parsed rather than constructed, so the decomposition is the shared parser's and not this
        // file's reading of the convention. A name that does not parse yields a null topic and the raw
        // name is still recorded - see the fallback note on ScriptedDispatch.
        _ = SubscriptionTopic.ParseSubscription(name, out SubscriptionTopic? topic);

        lock (_gate)
        {
            ScriptedDispatch record = new()
            {
                Ordinal = _records.Count + 1,
                Stage = stage,
                Name = name,
                Topic = topic,
                Arguments = arguments is null ? [] : [.. arguments],
                ReturnValue = returnValue,
                Veto = veto,
                VetoOutcome = vetoOutcome,
                HadSubscriber = hadSubscriber,
                IsPost = isPost,
                Label = label,
                Fault = fault,
            };

            _records.Add(record);

            return record;
        }
    }

    /// <summary>Every record taken at one stage, in arrival order.</summary>
    /// <param name="stage">The stage to select.</param>
    /// <returns>The matching records.</returns>
    public ImmutableArray<ScriptedDispatch> OfStage(ScriptedDispatchStage stage) =>
        [.. Records.Where(record => record.Stage == stage)];

    /// <summary>Every record for one legacy topic spelling, in arrival order.</summary>
    /// <param name="legacyName">
    /// The legacy spelling, ordering prefix included - for example
    /// <c>DataWindowEventChain.EVT_ITEMCHANGED</c>.
    /// </param>
    /// <returns>The matching records.</returns>
    /// <remarks>
    /// Matched ORDINALLY and case-SENSITIVELY against <see cref="ScriptedDispatch.LegacyName"/>. The
    /// broker's own handler-name comparison is case-insensitive but its TOPIC comparison is not, so a
    /// case-insensitive match here would find records the broker itself would not have dispatched.
    /// </remarks>
    public ImmutableArray<ScriptedDispatch> For(string legacyName)
    {
        ArgumentNullException.ThrowIfNull(legacyName);

        return [.. Records.Where(record =>
            string.Equals(record.LegacyName, legacyName, StringComparison.Ordinal))];
    }

    /// <summary>
    /// The legacy topic spellings in dispatch order - the projection an ordering assertion should use.
    /// </summary>
    /// <param name="stage">A stage to restrict to, or <see langword="null"/> for every stage.</param>
    /// <returns>The spellings, in arrival order, with duplicates preserved.</returns>
    public ImmutableArray<string> LegacyNames(ScriptedDispatchStage? stage = null) =>
        [.. Select(stage).Select(record => record.LegacyName)];

    /// <summary>
    /// The logical topic names in dispatch order - offered for completeness, and deliberately NOT the
    /// projection an ordering assertion should use.
    /// </summary>
    /// <param name="stage">A stage to restrict to, or <see langword="null"/> for every stage.</param>
    /// <returns>The names, in arrival order, with duplicates preserved.</returns>
    /// <remarks>
    /// The ordering prefix is absent from a logical name, so <c>"itemchanged"</c> and
    /// <c>"editchanged"</c> sort alphabetically here in the opposite relative order to their legacy
    /// spellings <c>"0-itemchanged"</c> and <c>"1-editchanged"</c>. That is exactly the trap AAP 0.6.1.2
    /// warns about; this projection exists so a test can DEMONSTRATE the trap, not fall into it.
    /// </remarks>
    public ImmutableArray<string> LogicalNames(ScriptedDispatchStage? stage = null) =>
        [.. Select(stage).Select(record => record.LogicalName)];

    /// <summary>The ordering-prefix values in dispatch order, with absent prefixes as nulls.</summary>
    /// <param name="stage">A stage to restrict to, or <see langword="null"/> for every stage.</param>
    /// <returns>The sequence values, in arrival order.</returns>
    public ImmutableArray<int?> Sequences(ScriptedDispatchStage? stage = null) =>
        [.. Select(stage).Select(record => record.Sequence)];

    /// <summary>The subscription lifetimes in dispatch order, with unresolved topics as nulls.</summary>
    /// <param name="stage">A stage to restrict to, or <see langword="null"/> for every stage.</param>
    /// <returns>The lifetimes, in arrival order.</returns>
    public ImmutableArray<SubscriptionLifetime?> Lifetimes(ScriptedDispatchStage? stage = null) =>
        [.. Select(stage).Select(record => record.Lifetime)];

    /// <summary>Discards every record, so one fixture can drive several scenarios.</summary>
    /// <remarks>
    /// The ordinal counter restarts with the records, because an ordinal is a position in THIS log and a
    /// cleared log has no history for a later record to be relative to.
    /// </remarks>
    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }

    /// <summary>Every record on its own line, for a failure message that has to show the whole log.</summary>
    /// <returns>The rendering, or a stated marker when nothing was recorded.</returns>
    public string Describe()
    {
        ImmutableArray<ScriptedDispatch> snapshot = Records;

        return snapshot.IsEmpty
            ? "(no dispatches recorded)"
            : string.Join(Environment.NewLine, snapshot.Select(record => record.ToString()));
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();

    /// <summary>Selects a stage without reordering.</summary>
    /// <param name="stage">The stage, or <see langword="null"/> for every stage.</param>
    /// <returns>The selected records, in arrival order.</returns>
    private IEnumerable<ScriptedDispatch> Select(ScriptedDispatchStage? stage) =>
        stage is null ? Records : Records.Where(record => record.Stage == stage.Value);
}

/// <summary>
/// An <see cref="EventBroker"/> that records every dispatch it performs and lets a test script each of
/// the four subclassing hooks.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY DERIVE RATHER THAN WRAP.</b> <see cref="EventBroker.Trigger(string, object?[])"/> and
/// <see cref="EventBroker.IsSubscribed(string)"/> are deliberately NOT virtual - they are the oracle's
/// own entry points and re-implementing them in a double would mean re-implementing the dispatch loop,
/// the priority ordering and the veto unwind, which is the code a test is trying to exercise. The four
/// hooks ARE virtual, and they are the seam the legacy itself uses: the real subclass at
/// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L48-L88</c> overrides all four and
/// every override begins with <c>call super::&lt;event&gt;</c>, so this double's shape - override, chain
/// to the base, then record - is the intended one rather than a test artifice.
/// </para>
/// <para>
/// <b>Inert by default.</b> Every scripting dictionary starts empty and every hook then returns exactly
/// what the base returned, so an unconfigured instance behaves as a plain broker that happens to keep a
/// diary. That matters for C-B: a double whose default behaviour differed from the real thing would make
/// every suite using it assert against the double instead of against the product.
/// </para>
/// <para>
/// <b>The three numeric alphabets are kept apart.</b> <see cref="TriggeringResults"/> and
/// <see cref="PrepareResults"/> hold RETURN CODES, tested by the broker with the shared
/// <c>IsPrevented</c> predicate against <see cref="RetCode.PREVENT"/> (1)
/// [n_cst_eventful.sru:L609, :L840]. <see cref="ExceptionResults"/> holds values from the exception
/// hook's own alphabet, where <see cref="EventBroker.ExceptionResultPrevent"/> is 1 and
/// <see cref="EventBroker.ExceptionResultContinue"/> is 2 - and 2 there means CONTINUE, the
/// near-opposite of <see cref="VetoResult.PreventDeep"/> at the same numeral. Neither is
/// <see cref="VetoResult"/>, and this double never converts between them.
/// </para>
/// </remarks>
internal sealed class ScriptedEventBroker : EventBroker
{
    /// <summary>Creates a broker writing into a fresh log.</summary>
    public ScriptedEventBroker()
        : this(new ScriptedDispatchLog())
    {
    }

    /// <summary>Creates a broker writing into a shared log.</summary>
    /// <param name="log">
    /// The log to append to, shared with any <see cref="ScriptedTopicSubscriber"/> whose dispatches
    /// should interleave with this broker's hook records in one ordered sequence.
    /// </param>
    public ScriptedEventBroker(ScriptedDispatchLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Log = log;
    }

    /// <summary>The log this broker appends to.</summary>
    public ScriptedDispatchLog Log { get; }

    /// <summary>
    /// The label written onto this broker's own records, distinguishing them from a subscriber's.
    /// </summary>
    public string Label { get; set; } = "broker";

    /// <summary>
    /// A scripted <c>OnTriggering</c> return code per event name; an absent name uses the base's
    /// neutral <see cref="RetCode.OK"/>.
    /// </summary>
    /// <remarks>
    /// Script <see cref="RetCode.PREVENT"/> to abort a whole dispatch BEFORE its first handler runs. That
    /// abort is not a <see cref="VetoResult"/>: it leaves the loop directly [n_cst_eventful.sru:L841]
    /// without setting the prevent state, so nothing survives into an enclosing dispatch.
    /// </remarks>
    public Dictionary<string, long> TriggeringResults { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A scripted <c>OnPrepare</c> return code per event name; an absent name uses
    /// <see cref="RetCode.OK"/>.
    /// </summary>
    public Dictionary<string, long> PrepareResults { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many leading argument slots <c>OnPrepare</c> should report as consumed, per event name; an
    /// absent name consumes none.
    /// </summary>
    /// <remarks>
    /// This is the knob that makes argument injection observable. The oracle's own chain sets slot one to
    /// the parent and declares one slot consumed [se_cst_dw.sru:L604-L605], after which the broker passes
    /// <c>Min(declared - consumed, payload)</c> values from the slot after the last consumed one. Use it
    /// together with <see cref="PrepareInjections"/> to reproduce that shape without the chain.
    /// </remarks>
    public Dictionary<string, int> PrepareConsumedSlots { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A value to write into argument slot one from <c>OnPrepare</c>, per event name; an absent name
    /// writes nothing.
    /// </summary>
    /// <remarks>
    /// Slot one specifically, and one slot only, because that is what the oracle injects. A double that
    /// offered arbitrary slot writes would invite a test to construct a payload shape the product cannot
    /// produce.
    /// </remarks>
    public Dictionary<string, object?> PrepareInjections { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A scripted <c>OnException</c> return code per event name; an absent name returns <c>0</c>, which
    /// is the base's value and means "rethrow".
    /// </summary>
    /// <remarks>
    /// The two meaningful values are <see cref="EventBroker.ExceptionResultPrevent"/> (1, prevent and
    /// leave the loop) and <see cref="EventBroker.ExceptionResultContinue"/> (2, continue and clear the
    /// has-exception latch). Anything else rethrows [n_cst_eventful.sru:L889-L902].
    /// </remarks>
    public Dictionary<string, long> ExceptionResults { get; } = new(StringComparer.Ordinal);

    /// <summary>How many times <c>OnTriggering</c> ran.</summary>
    /// <remarks>
    /// Counted because the hook fires ONCE PER DISPATCH on the first matching subscription only, so a
    /// dispatch with no matching subscription never increments it and a dispatch with ten subscribers
    /// increments it once. That asymmetry is worth an assertion of its own.
    /// </remarks>
    public int TriggeringCount { get; private set; }

    /// <summary>How many times <c>OnTriggered</c> ran.</summary>
    public int TriggeredCount { get; private set; }

    /// <summary>How many times <c>OnPrepare</c> ran.</summary>
    public int PrepareCount { get; private set; }

    /// <summary>How many times <c>OnException</c> ran.</summary>
    public int ExceptionCount { get; private set; }

    /// <inheritdoc/>
    protected override long OnTriggering(string name, bool isPost)
    {
        long result = base.OnTriggering(name, isPost);

        if (TriggeringResults.TryGetValue(name, out long scripted))
        {
            result = scripted;
        }

        TriggeringCount++;

        // Sampled here rather than asserted later: IsSubscribed is a live query, and by the time a test
        // reads it the scenario may have unsubscribed. Recording the answer AT the dispatch is what makes
        // the oracle's two pre-check sites (se_cst_dw.sru:L166, :L316) assertable.
        _ = Log.Append(
            ScriptedDispatchStage.Triggering,
            name,
            returnValue: result,
            hadSubscriber: IsSubscribed(name),
            isPost: isPost,
            label: Label);

        return result;
    }

    /// <inheritdoc/>
    protected override void OnTriggered(string name, bool isPost)
    {
        base.OnTriggered(name, isPost);

        TriggeredCount++;

        _ = Log.Append(
            ScriptedDispatchStage.Triggered,
            name,
            returnValue: GetReturnValue(),
            isPost: isPost,
            label: Label);
    }

    /// <inheritdoc/>
    protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        long result = base.OnPrepare(name, target, arguments);

        if (PrepareInjections.TryGetValue(name, out object? injected))
        {
            // The return code is deliberately DISCARDED, exactly as the oracle discards it at
            // se_cst_dw.sru:L604. SetArgument reports E_INVALID_ARGUMENT for an out-of-range slot, and a
            // handler declaring no parameters at all legitimately has no slot one - so a failure here is
            // an expected outcome of a legal scenario, not an error to raise.
            _ = arguments.SetArgument(1, injected);
        }

        if (PrepareConsumedSlots.TryGetValue(name, out int consumed))
        {
            arguments.ConsumedArgumentCount = consumed;
        }

        if (PrepareResults.TryGetValue(name, out long scripted))
        {
            result = scripted;
        }

        PrepareCount++;

        List<object?> slots = new(arguments.DeclaredArgumentCount);
        for (int slot = 1; slot <= arguments.DeclaredArgumentCount; slot++)
        {
            // ONE-BASED, because EventArgumentContext is: slot 1 is the first declared parameter and
            // GetArgument answers null for anything outside 1..DeclaredArgumentCount.
            slots.Add(arguments.GetArgument(slot));
        }

        _ = Log.Append(
            ScriptedDispatchStage.Prepare,
            name,
            arguments: slots,
            returnValue: result,
            label: Label);

        return result;
    }

    /// <inheritdoc/>
    protected override long OnException(string name, Exception exception)
    {
        long result = base.OnException(name, exception);

        if (ExceptionResults.TryGetValue(name, out long scripted))
        {
            result = scripted;
        }

        ExceptionCount++;

        _ = Log.Append(
            ScriptedDispatchStage.Exception,
            name,
            returnValue: result,
            label: Label,
            fault: exception);

        return result;
    }
}

/// <summary>
/// A broker subscriber bound to ONE topic, which records every dispatch it receives and answers with a
/// scripted return code, a scripted tri-valued veto, or a scripted exception.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE TOPIC PER INSTANCE, AND THAT IS THE DESIGN.</b> The broker gives a handler no way to ask which
/// topic it is being dispatched for - the dispatch name is not passed to the handler and
/// <see cref="EventBroker"/> publishes no current-name member - so a subscriber serving several topics
/// could not record the topic identity accurately, which is the one thing this double exists to do.
/// Binding the topic at construction removes the ambiguity entirely. Several instances sharing one
/// <see cref="ScriptedDispatchLog"/> give cross-topic ordering, which is what an ordering assertion
/// needs.
/// </para>
/// <para>
/// <b>Works against ANY broker, including the real one.</b> Nothing here depends on
/// <see cref="ScriptedEventBroker"/>: <see cref="Subscribe"/> takes a plain
/// <see cref="EventBroker"/>, so the same subscriber can be attached to the chain's own internal broker
/// (reached through <c>DataWindowServiceHost.Eventful</c>) to observe a real dispatch, or to a scripted
/// broker to observe the hooks around it.
/// </para>
/// <para>
/// <b>ONE HANDLER NAME PER ARITY, NEVER OVERLOADS.</b> The broker resolves a handler by name, collecting
/// candidates up the type hierarchy and - when no explicit signature is supplied - choosing the one with
/// the FEWEST parameters. Overloading a single handler name would therefore always resolve to the
/// zero-parameter arm and silently discard the arguments this double is meant to record. The six
/// distinctly-named handlers below exist for that reason; pick one with <see cref="HandlerNameFor"/>.
/// </para>
/// <para>
/// <b>The veto is raised through the broker, not returned.</b> Answering
/// <see cref="RetCode.PREVENT"/> is the mechanism every <c>= 1</c> site in <c>se_cst_dw.sru</c> tests;
/// <see cref="EventBroker.Prevent(bool)"/> is the separate mechanism that aborts the broker's own
/// dispatch loop and, for <see cref="VetoResult.PreventDeep"/>, keeps aborting outward as the stack
/// unwinds. They are different alphabets and this double exposes BOTH independently:
/// <see cref="Answer"/> for the first and <see cref="Veto"/> for the second.
/// </para>
/// </remarks>
internal sealed class ScriptedTopicSubscriber
{
    /// <summary>The stem every handler name shares.</summary>
    /// <remarks>
    /// A field rather than an inline literal in <see cref="HandlerNameFor"/> so that the name a test
    /// passes to the broker and the name this type actually declares cannot drift apart.
    /// </remarks>
    private const string HandlerStem = "OnDispatch";

    /// <summary>The largest arity this double declares a handler for.</summary>
    /// <remarks>
    /// Five, because the widest payload any of the twelve legacy topics carries is four values - the
    /// mouse topics pass <c>xpos, ypos, row, dwo</c> [se_cst_dw.sru:L63-L72] - and one further slot is
    /// needed for the parent the prepare hook injects into slot one [:L604-L605].
    /// </remarks>
    public const int MaximumArity = 5;

    /// <summary>Creates a subscriber for one topic, writing into a fresh log.</summary>
    /// <param name="topicName">
    /// The topic, spelled as the legacy constant spells it - pass
    /// <c>DataWindowEventChain.EVT_ITEMCHANGED</c> rather than a literal, so the ordering prefix comes
    /// from the one place that owns it.
    /// </param>
    public ScriptedTopicSubscriber(string topicName)
        : this(topicName, new ScriptedDispatchLog())
    {
    }

    /// <summary>Creates a subscriber for one topic, writing into a shared log.</summary>
    /// <param name="topicName">The topic, spelled as the legacy constant spells it.</param>
    /// <param name="log">The log to append to.</param>
    /// <param name="label">
    /// A label distinguishing this subscriber's records from another's on a shared log; defaults to the
    /// topic name.
    /// </param>
    public ScriptedTopicSubscriber(string topicName, ScriptedDispatchLog log, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(topicName);
        ArgumentNullException.ThrowIfNull(log);

        TopicName = topicName;
        Log = log;
        Label = label ?? topicName;

        // Parsed, not constructed - the same reason the chain parses its twelve literals rather than
        // building topics field by field: the decomposition then belongs to the shared parser and
        // round-trips back to these exact bytes.
        _ = SubscriptionTopic.ParseSubscription(topicName, out SubscriptionTopic? topic);
        Topic = topic;
    }

    /// <summary>The topic this subscriber is bound to, exactly as spelled.</summary>
    public string TopicName { get; }

    /// <summary>
    /// The decomposed topic identity, or <see langword="null"/> when <see cref="TopicName"/> did not
    /// parse - which a test may drive deliberately, since the broker itself accepts names the
    /// subscription grammar rejects.
    /// </summary>
    public SubscriptionTopic? Topic { get; }

    /// <summary>The label written onto this subscriber's records.</summary>
    public string Label { get; }

    /// <summary>The log this subscriber appends to.</summary>
    public ScriptedDispatchLog Log { get; }

    /// <summary>
    /// The return code every handler answers with, defaulting to the neutral
    /// <see cref="RetCode.OK"/>.
    /// </summary>
    /// <remarks>
    /// Set <see cref="RetCode.PREVENT"/> to drive the oracle's <c>= 1</c> tests - for example
    /// <c>if Eventful.of_Trigger(EVT_RBUTTONDOWN,...) = 1 then return 1</c> [se_cst_dw.sru:L116].
    /// </remarks>
    public long Answer { get; set; } = RetCode.OK;

    /// <summary>
    /// The veto to raise through the broker before answering, defaulting to
    /// <see cref="VetoResult.Continue"/> which raises none.
    /// </summary>
    public VetoResult Veto { get; set; } = VetoResult.Continue;

    /// <summary>
    /// An exception to throw after recording, or <see langword="null"/> to answer normally.
    /// </summary>
    /// <remarks>
    /// Thrown AFTER the record is appended, deliberately: the dispatch is evidence whether or not the
    /// handler completed, and a double that recorded only its successes would make the broker's exception
    /// hook look as though it had fired for a dispatch that never happened.
    /// </remarks>
    public Exception? Fault { get; set; }

    /// <summary>
    /// A side effect to run inside the handler, after recording and before answering - the seam for a
    /// re-entrant scenario.
    /// </summary>
    public Action<ScriptedDispatch>? InHandler { get; set; }

    /// <summary>How many dispatches this subscriber has received.</summary>
    public int Dispatches { get; private set; }

    /// <summary>The arguments the most recent dispatch delivered, in slot order.</summary>
    public ImmutableArray<object?> LastArguments { get; private set; } = [];

    /// <summary>
    /// What <see cref="EventBroker.Prevent(bool)"/> answered on the most recent dispatch, or
    /// <see langword="null"/> when no veto was raised.
    /// </summary>
    /// <remarks>
    /// <see cref="RetCode.FAILED"/> here means the veto had no effect because there was no dispatch in
    /// progress [n_cst_eventful.sru:L1293] - which happens when a handler is invoked directly by a test
    /// rather than through the broker, and is worth distinguishing from a veto that was honoured.
    /// </remarks>
    public long? LastVetoOutcome { get; private set; }

    /// <summary>The handler name for a given arity.</summary>
    /// <param name="arity">How many parameters the handler should declare, from zero to
    /// <see cref="MaximumArity"/>.</param>
    /// <returns>The handler name to hand to <see cref="EventBroker.Subscribe(string, object, string)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The arity is outside the declared range.</exception>
    public static string HandlerNameFor(int arity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arity, MaximumArity);

        return HandlerStem + arity.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Subscribes this double to a broker for its bound topic.</summary>
    /// <param name="broker">The broker to subscribe to - a real one or a scripted one.</param>
    /// <param name="arity">Which handler arity to subscribe, defaulting to none.</param>
    /// <returns>
    /// The broker's own return code, returned rather than asserted so a test can pin it: a subscription
    /// naming a handler that does not exist reports <see cref="RetCode.E_EVENT_NOT_FOUND"/> rather than
    /// throwing.
    /// </returns>
    public long Subscribe(EventBroker broker, int arity = 0)
    {
        ArgumentNullException.ThrowIfNull(broker);

        return broker.Subscribe(TopicName, this, HandlerNameFor(arity));
    }

    /// <summary>Unsubscribes every handler of this double from a broker.</summary>
    /// <param name="broker">The broker to detach from.</param>
    /// <returns>The broker's own return code.</returns>
    /// <remarks>
    /// The target-only overload, so a subscriber attached at several arities detaches in one call. This is
    /// the seam a test uses to make <c>of_IsSubscribed</c> answer false at one of the oracle's two
    /// pre-check sites without tearing down the whole fixture.
    /// </remarks>
    public long Unsubscribe(EventBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);

        return broker.Unsubscribe(this);
    }

    /// <summary>A handler declaring no parameters - nothing for the prepare hook to inject into.</summary>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch0() => Record();

    /// <summary>A handler declaring one parameter.</summary>
    /// <param name="slot1">Slot one - the injected value, when the prepare hook injects.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch1(object? slot1) => Record(slot1);

    /// <summary>A handler declaring two parameters.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch2(object? slot1, object? slot2) => Record(slot1, slot2);

    /// <summary>A handler declaring three parameters.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch3(object? slot1, object? slot2, object? slot3) => Record(slot1, slot2, slot3);

    /// <summary>A handler declaring four parameters.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <param name="slot4">Slot four.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch4(object? slot1, object? slot2, object? slot3, object? slot4) =>
        Record(slot1, slot2, slot3, slot4);

    /// <summary>A handler declaring five parameters - the widest shape any legacy topic needs.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <param name="slot4">Slot four.</param>
    /// <param name="slot5">Slot five.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnDispatch5(object? slot1, object? slot2, object? slot3, object? slot4, object? slot5) =>
        Record(slot1, slot2, slot3, slot4, slot5);

    /// <summary>
    /// Records one dispatch, raises the scripted veto, runs the side effect, then answers or throws.
    /// </summary>
    /// <param name="arguments">The values this handler's parameters received.</param>
    /// <returns><see cref="Answer"/>.</returns>
    private long Record(params object?[] arguments)
    {
        Dispatches++;
        LastArguments = [.. arguments];

        // Raised through the broker that is CURRENTLY dispatching, which is how the legacy reaches it: a
        // subscriber has no reference to the broker, so `EventBroker.Current` stands in for the ambient
        // dispatch. Outside a dispatch it is null and no veto is attempted, which is the honest answer -
        // there is nothing to prevent.
        LastVetoOutcome = Veto == VetoResult.Continue
            ? null
            : EventBroker.Current?.Prevent(Veto == VetoResult.PreventDeep);

        ScriptedDispatch record = Log.Append(
            ScriptedDispatchStage.Handler,
            TopicName,
            arguments: arguments,
            returnValue: Answer,
            veto: Veto,
            vetoOutcome: LastVetoOutcome,
            label: Label,
            fault: Fault);

        InHandler?.Invoke(record);

        if (Fault is not null)
        {
            throw Fault;
        }

        return Answer;
    }
}

// ==================================================================================================
//  SECTION 2 - THE LOCALIZATION SEAM AND THE EXACT LEGACY MESSAGE KEYS
//  ------------------------------------------------------------------------------------------------
//  Oracle: ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru (the one-event contract),
//          ws_objects/pfw.ui.pbl.src/i18n.srf (the facade and its SILENT PASSTHROUGH),
//          ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17 (the two categories).
//
//  THE CONTRACT IS ONE MEMBER AND IT MUTATES A REF PARAMETER. `II18nProvider.OnTranslate(long source,
//  long category, ref string? text)` returns 1 when it handled the request and 0 when it did not, and it
//  delivers a translation ONLY by assigning through `text`. Three obligations follow, all behavioural:
//  a provider that answers 0 must leave `text` exactly as it found it; a null `text` must answer 0
//  rather than throw; and a 0 answer must be INDISTINGUISHABLE from having no provider installed at all,
//  because the facade discards the return value and returns the text either way [i18n.srf:L17-L18, :L21-L22].
//
//  THE CATEGORY IS AN OFFSET AND IS NEVER WRITTEN AS A NUMBER. `Categories.CAT_DWSVC` is
//  `Enums.I18N_CAT_CUSTOM + 2` [ne_cst_i18n.sru:L17]. Writing the resolved value would silently detach
//  from the base constant, so every reference below reads `Categories.CAT_DWSVC`.
// ==================================================================================================

/// <summary>
/// One localization request as a provider saw it.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is the value on the way IN - the lookup key - captured before the provider had the
/// chance to overwrite it. A recorder that captured it afterwards could not tell a translated request
/// from an untranslated one.
/// </remarks>
/// <param name="Source">The originating subsystem, from <c>Enums.I18N_SRC_*</c>.</param>
/// <param name="Category">The resource category, from <c>Enums.I18N_CAT_*</c> or <c>Categories</c>.</param>
/// <param name="Text">The lookup key, as received.</param>
internal readonly record struct I18nRequest(long Source, long Category, string? Text);

/// <summary>
/// A localization provider that answers from a taught table and records every request.
/// </summary>
/// <remarks>
/// <para>
/// <b>It obeys the contract's return alphabet exactly.</b> A hit assigns the translation and returns
/// <c>1</c>; a miss leaves the text untouched and returns <c>0</c>; a <see langword="null"/> key returns
/// <c>0</c> without dereferencing anything. That last case matters: the annotation on
/// <c>II18nProvider.OnTranslate</c> is nullable deliberately, and the contract requires a null key to
/// answer "not handled" rather than throw.
/// </para>
/// <para>
/// <b>Source and category are GATES, not decoration.</b> All three shipped providers translate only for
/// the framework's own source and return <c>0</c> for anything else [n_cst_i18n_chs.sru:L25,
/// n_cst_i18n_en.sru:L32, n_cst_i18n_cht.sru:L33], and the two table-driven providers switch on the
/// category and fall straight through when it matches nothing [n_cst_i18n_en.sru:L34-L47]. This double
/// reproduces both gates so a test can prove a lookup was made with the RIGHT category - which is the
/// only way to catch a caller that passed <c>CAT_MSGBOX</c> where the oracle passes
/// <c>Categories.CAT_DWSVC</c>.
/// </para>
/// </remarks>
internal sealed class ScriptedI18nProvider : II18nProvider
{
    /// <summary>The value the contract uses for "I handled this request".</summary>
    private const long Handled = 1L;

    /// <summary>The value the contract uses for "not mine".</summary>
    private const long NotHandled = 0L;

    private readonly Dictionary<string, string?> _table = new(StringComparer.Ordinal);
    private readonly List<I18nRequest> _requests = [];

    /// <summary>The source this provider answers for; any other source answers "not handled".</summary>
    /// <remarks>
    /// Defaults to the framework's own source, which is what the two-argument facade overload supplies -
    /// so the default is the value every in-scope call site actually produces rather than a convenience.
    /// </remarks>
    public long Source { get; set; } = Enums.I18N_SRC_PFW;

    /// <summary>The category this provider answers for; any other category answers "not handled".</summary>
    /// <remarks>
    /// Defaults to <see cref="Categories.CAT_DWSVC"/>, the category BOTH in-scope call sites pass: the
    /// validation-error path [se_cst_dw.sru:L355, :L357] and the row-select rejection
    /// [n_cst_dwsvc_rowselect.sru:L239] each call <c>I18N(ne_cst_i18n.CAT_DWSVC, ...)</c>. Read from
    /// <c>Categories</c> and never written as the resolved number, because it is an OFFSET from
    /// <c>Enums.I18N_CAT_CUSTOM</c> [ne_cst_i18n.sru:L17].
    /// </remarks>
    public long Category { get; set; } = Categories.CAT_DWSVC;

    /// <summary>Every request, in order, with each key captured as received.</summary>
    public ImmutableArray<I18nRequest> Requests => [.. _requests];

    /// <summary>The keys this provider was asked for, in order.</summary>
    public ImmutableArray<string?> RequestedKeys => [.. _requests.Select(request => request.Text)];

    /// <summary>Teaches one key its translation.</summary>
    /// <param name="key">The lookup key, transcribed exactly - see <see cref="LegacyMessageKeys"/>.</param>
    /// <param name="translation">
    /// The translation to deliver. May be <see langword="null"/>, which models a table hit whose
    /// replacement text is null - the case that makes a caller's null handling observable, since
    /// PowerScript strings can be null and AAP 0.4.5.4 forbids collapsing that null.
    /// </param>
    /// <returns>This provider, so teaching can be chained.</returns>
    public ScriptedI18nProvider Teach(string key, string? translation)
    {
        ArgumentNullException.ThrowIfNull(key);

        _table[key] = translation;

        return this;
    }

    /// <summary>
    /// Teaches every legacy key of <see cref="LegacyMessageKeys"/> a visibly distinct marker
    /// translation.
    /// </summary>
    /// <returns>This provider, so teaching can be chained.</returns>
    /// <remarks>
    /// A MARKER RATHER THAN THE REAL ENGLISH, ON PURPOSE. The shipped English renderings would also work,
    /// but an assertion against them cannot distinguish "the lookup happened and returned English" from
    /// "the lookup was skipped and the key happened to read as English". A marker that could not occur
    /// naturally proves the lookup path was taken. The real renderings are not this file's business
    /// anyway: they live in <c>pfw.i18n.xml</c>, which is read-only oracle data.
    /// </remarks>
    public ScriptedI18nProvider TeachMarkers()
    {
        foreach (string key in LegacyMessageKeys.All)
        {
            _ = Teach(key, LegacyMessageKeys.MarkerFor(key));
        }

        return this;
    }

    /// <inheritdoc/>
    public long OnTranslate(long source, long category, ref string? text)
    {
        _requests.Add(new I18nRequest(source, category, text));

        // The two gates, in the oracle's order: source first [n_cst_i18n_en.sru:L32], then category
        // [:L34-L47]. Either failing leaves `text` untouched, which is the contract's requirement for a
        // zero answer and not merely this double's politeness.
        if (source != Source || category != Category)
        {
            return NotHandled;
        }

        // The null key answers "not mine" WITHOUT dereferencing. Ordered before the lookup deliberately:
        // a dictionary lookup on a null key would throw, and the contract says this case must not.
        if (text is null)
        {
            return NotHandled;
        }

        if (!_table.TryGetValue(text, out string? translation))
        {
            return NotHandled;
        }

        text = translation;

        return Handled;
    }
}

/// <summary>
/// A localization provider that is installed but translates nothing - the double that proves a zero
/// answer is indistinguishable from no provider at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a separate type rather than an empty <see cref="ScriptedI18nProvider"/>.</b> An
/// untaught table-driven provider answers zero for the right reason but by a longer path; this type
/// answers zero unconditionally, so a suite pinning the SILENT PASSTHROUGH has a double that cannot
/// accidentally translate. The passthrough is a preserved behaviour with three parts, all of which the
/// facade owes and none of which this provider may disturb: with nothing installed the text comes back
/// unchanged, and the facade must never throw, never log, and never mark the text untranslated
/// [i18n.srf:L17-L22].
/// </para>
/// <para>
/// It still records, so a test can prove the provider WAS consulted - the difference between a
/// passthrough that went through the provider and one that short-circuited before it.
/// </para>
/// </remarks>
internal sealed class PassthroughI18nProvider : II18nProvider
{
    private readonly List<I18nRequest> _requests = [];

    /// <summary>Every request, in order.</summary>
    public ImmutableArray<I18nRequest> Requests => [.. _requests];

    /// <inheritdoc/>
    public long OnTranslate(long source, long category, ref string? text)
    {
        _requests.Add(new I18nRequest(source, category, text));

        // `text` is deliberately not assigned - not even to itself. A zero answer must leave it exactly as
        // found, and a self-assignment would be indistinguishable here but would set a precedent that is
        // wrong the moment the parameter type acquires a setter with a side effect.
        return 0L;
    }
}

/// <summary>
/// The exact localization lookup keys the in-scope legacy code paths use, together with the composition
/// rules that turn them into the messages the oracle shows.
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY KEY IS SURFACED BY REFERENCE, NEVER RE-TRANSCRIBED.</b> Each constant below points at the
/// constant the application already declares, reachable from this assembly through the
/// <c>InternalsVisibleTo</c> item in <c>PowerFramework.DataServices.csproj</c>. So there is exactly ONE
/// source of truth per string and no way for a test's copy to drift from the product's. The locators are
/// repeated here because a reader of a test should not have to open the product to find them.
/// </para>
/// <para>
/// <b>The composition rules below are restatements of the ORACLE, not copies of the product.</b> That
/// distinction is the whole of golden-master discipline: an expectation computed by calling the code
/// under test asserts only that the code equals itself. Each helper therefore reproduces the PowerScript
/// expression at its cited line, using the ported PowerScript primitive
/// <c>Formatting.Sprintf</c> where the oracle uses <c>Sprintf</c>, and was written from
/// <c>ws_objects/</c> rather than from <c>Domain/ValidationSession.cs</c> or
/// <c>Services/RowSelectService.cs</c>.
/// </para>
/// </remarks>
internal static class LegacyMessageKeys
{
    /// <summary>
    /// The validation-error fallback text - <c>"输入了无效的值"</c>, "an invalid value was entered".
    /// </summary>
    /// <remarks>
    /// Used at <c>se_cst_dw.sru:L355</c> as <c>I18N(ne_cst_i18n.CAT_DWSVC,"输入了无效的值") + "!"</c>, and
    /// reached only when the column's own validation message is empty or is the bare placeholder
    /// <see cref="NoValidationMessagePlaceholder"/>. See
    /// <see cref="ComposeValidationErrorFallback"/> for the trailing-<c>!</c> rule.
    /// </remarks>
    public const string ValidationErrorFallbackText = ValidationStructuredError.LegacyFallbackTextSource;

    /// <summary>
    /// The <c>"!"</c> the oracle appends to the TRANSLATED fallback text, and to nothing else.
    /// </summary>
    /// <remarks>
    /// It is appended OUTSIDE the localization call at <c>se_cst_dw.sru:L355</c>, so it is not part of the
    /// lookup key and a provider never sees it. A test that included it in the key would be asserting a
    /// lookup the oracle never makes.
    /// </remarks>
    public const string ValidationErrorFallbackSuffix = ValidationStructuredError.LegacyFallbackSuffix;

    /// <summary>The validation-error dialog title - <c>"错误"</c>, "error".</summary>
    /// <remarks>
    /// The FIRST argument of <c>MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"),sErrMsg,StopSign!)</c> at
    /// <c>se_cst_dw.sru:L357</c>, translated through the same category as the body text.
    /// </remarks>
    public const string ValidationErrorTitle = ValidationStructuredError.LegacyTitleSource;

    /// <summary>
    /// The bare <c>"?"</c> a DataWindow reports when a column has no validation message at all.
    /// </summary>
    /// <remarks>
    /// Not a localization key - a SENTINEL. <c>se_cst_dw.sru:L353</c> treats an empty message and this
    /// single character alike and substitutes the localized fallback for both, so a test driving the
    /// fallback path needs this value to get there.
    /// </remarks>
    public const string NoValidationMessagePlaceholder =
        ValidationStructuredError.NoValidationMessagePlaceholder;

    /// <summary>
    /// The row-select rejection's first key - <c>"第{}行"</c>, "row {}", carrying a
    /// <c>Sprintf</c> placeholder.
    /// </summary>
    /// <remarks>
    /// TRANSLATED FIRST, FORMATTED SECOND. <c>n_cst_dwsvc_rowselect.sru:L239</c> reads
    /// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)</c>, so the lookup key still contains the
    /// placeholder and the row number is substituted into whatever the provider returned. Reversing the
    /// order would look for a key that has the row number baked into it and would miss every time.
    /// </remarks>
    public const string RowSelectRowNumberKey = RowSelectService.RowNumberMessageKey;

    /// <summary>
    /// The row-select rejection's second key - <c>"修改数据被拒绝"</c>, "the data change was refused".
    /// </summary>
    /// <remarks>Carries no placeholder [n_cst_dwsvc_rowselect.sru:L239, third operand].</remarks>
    public const string RowSelectRejectionKey = RowSelectService.RejectionMessageKey;

    /// <summary>The <c>"!"</c> appended after the second translated fragment, outside the lookup.</summary>
    public const string RowSelectRejectionSuffix = RowSelectService.RejectionMessageSuffix;

    /// <summary>
    /// The separator between the row fragment and the rejection fragment - the oracle's <c>"~n"</c>.
    /// </summary>
    /// <remarks>
    /// PowerScript's <c>~n</c> escape is a single line feed, which is what the port carries; it is NOT the
    /// platform newline, and on Windows it is not a carriage-return pair either.
    /// </remarks>
    public const string RowSelectLineSeparator = RowSelectService.LineSeparator;

    /// <summary>
    /// The column-expression engine's dialog title - the same <c>"错误"</c> text, reached by a path that
    /// does NOT localize it.
    /// </summary>
    /// <remarks>
    /// A PRESERVED INCONSISTENCY, AND THE REASON THIS CONSTANT IS SEPARATE FROM
    /// <see cref="ValidationErrorTitle"/> DESPITE HOLDING THE SAME CHARACTERS. All 28 live message sites
    /// in <c>n_cst_dwsvc_columnexp.sru</c> are hardcoded Chinese that never route through <c>I18N</c> -
    /// compare <c>:L739</c> and <c>:L762</c>, which call <c>MessageBox("错误", ...)</c> directly, with
    /// <c>se_cst_dw.sru:L357</c>, which translates the identical text. AAP 0.6.2.5 requires that
    /// inconsistency be reproduced rather than harmonised, so this text must NOT be taught to a provider
    /// as though it were a key: it is a literal, and a test that translated it would assert behaviour the
    /// engine does not have. It is deliberately absent from <see cref="All"/>. The application declares it
    /// on <c>ExpressionErrorCatalog</c> rather than on <c>ParseErrorFormatter</c>, which is the type that
    /// consumes it.
    /// </remarks>
    public const string ExpressionErrorDialogTitle = ExpressionErrorCatalog.LegacyTitle;

    /// <summary>
    /// Every key that genuinely IS a localization key, for a test that wants to teach or assert them all.
    /// </summary>
    /// <remarks>
    /// Four entries. <see cref="ExpressionErrorDialogTitle"/> is excluded because it is a hardcoded
    /// literal rather than a key, and <see cref="NoValidationMessagePlaceholder"/> because it is a
    /// DataWindow sentinel. The order is the order the oracle reaches them in: the validation-error pair
    /// [se_cst_dw.sru:L355, :L357] before the row-select pair [n_cst_dwsvc_rowselect.sru:L239].
    /// </remarks>
    public static ImmutableArray<string> All { get; } =
    [
        ValidationErrorFallbackText,
        ValidationErrorTitle,
        RowSelectRowNumberKey,
        RowSelectRejectionKey
    ];

    /// <summary>The marker translation <see cref="ScriptedI18nProvider.TeachMarkers"/> teaches a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>A translation that could not occur naturally, so a hit is unmistakable.</returns>
    /// <remarks>
    /// The key is embedded in the marker so a failure message names which lookup produced the value, and
    /// the placeholder in <see cref="RowSelectRowNumberKey"/> survives intact - which is required, because
    /// the row number is substituted into the TRANSLATION and a marker that dropped the placeholder would
    /// make that substitution untestable.
    /// </remarks>
    public static string MarkerFor(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return "<t:" + key + ">";
    }

    /// <summary>
    /// Composes the validation-error fallback message the way <c>se_cst_dw.sru:L355</c> does.
    /// </summary>
    /// <param name="translatedFallbackText">
    /// What the localization facade answered for <see cref="ValidationErrorFallbackText"/>. May be
    /// <see langword="null"/>, which models a provider that answered null; PowerScript concatenation
    /// treats that as the empty string and so does this.
    /// </param>
    /// <returns>The composed message.</returns>
    /// <remarks>
    /// The rule is one line long and is worth stating exactly: the text is translated and the <c>"!"</c>
    /// is appended AFTERWARDS. The suffix is therefore never part of the lookup, and a message asserted
    /// without it is asserting the pre-composition value.
    /// </remarks>
    public static string ComposeValidationErrorFallback(string? translatedFallbackText) =>
        translatedFallbackText + ValidationErrorFallbackSuffix;

    /// <summary>
    /// Composes the row-select rejection message the way <c>n_cst_dwsvc_rowselect.sru:L239</c> does.
    /// </summary>
    /// <param name="row">The rejected row, substituted into the translated row fragment.</param>
    /// <param name="translatedRowNumberTemplate">
    /// What the facade answered for <see cref="RowSelectRowNumberKey"/>, placeholder intact.
    /// </param>
    /// <param name="translatedRejection">
    /// What the facade answered for <see cref="RowSelectRejectionKey"/>.
    /// </param>
    /// <returns>The composed message.</returns>
    /// <remarks>
    /// Four operands in the oracle's order: the formatted row fragment, the line feed, the rejection
    /// fragment, then the <c>"!"</c>. The formatting uses the ported <c>Formatting.Sprintf</c> so the
    /// placeholder grammar is the framework's own and not this file's guess at it.
    /// </remarks>
    public static string ComposeRowSelectRejection(
        long row,
        string? translatedRowNumberTemplate,
        string? translatedRejection) =>
        Formatting.Sprintf(translatedRowNumberTemplate, row)
            + RowSelectLineSeparator
            + translatedRejection
            + RowSelectRejectionSuffix;
}

/// <summary>
/// Factories for the two localization arrangements a suite needs: a facade with a provider installed and
/// a facade with none.
/// </summary>
/// <remarks>
/// The facade is <c>I18n</c>, whose installer overload writes the single provider slot the oracle's
/// global stands in for [i18n.srf:L13]; AAP 0.4.5.1 turns that global into an injected dependency, which
/// is why a test constructs its own facade rather than reaching for an ambient one. There is deliberately
/// no <c>Current</c> or <c>Default</c> holder to reach for.
/// </remarks>
internal static class ScriptedLocalization
{
    /// <summary>A facade with NO provider installed - the silent-passthrough arrangement.</summary>
    /// <returns>The facade.</returns>
    /// <remarks>
    /// Every lookup returns its key unchanged, and nothing is thrown, logged or flagged. This is the
    /// arrangement the oracle is in before <c>pfw.sra</c>'s open event installs a locale
    /// [ws_objects/pfw.pbl.src/pfw.sra:L95-L103], so it is a real production state and not a degenerate
    /// test-only one.
    /// </remarks>
    public static I18n WithoutProvider() => new();

    /// <summary>A facade with a provider installed.</summary>
    /// <param name="provider">The provider to install.</param>
    /// <returns>The facade.</returns>
    /// <remarks>
    /// The installer's return code is DISCARDED here because a non-null provider always installs; a suite
    /// that wants to pin the <c>E_INVALID_OBJECT</c> answer to a null provider should call the installer
    /// itself rather than route through this factory.
    /// </remarks>
    public static I18n With(II18nProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        I18n facade = new();
        _ = facade.I18N(provider);

        return facade;
    }

    /// <summary>
    /// A facade whose provider translates all four legacy keys to markers, plus that provider.
    /// </summary>
    /// <returns>The facade and the provider, so a test can assert on the requests as well as the text.</returns>
    public static (I18n Facade, ScriptedI18nProvider Provider) WithMarkers()
    {
        ScriptedI18nProvider provider = new ScriptedI18nProvider().TeachMarkers();

        return (With(provider), provider);
    }
}

// ==================================================================================================
//  SECTION 3 - THE C-04 CLIENT-CALLBACK SEAMS: MACRO INVOCATION AND EXPRESSION TRACE
//  ------------------------------------------------------------------------------------------------
//  Oracle: ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L14  (oncolumnexpinvokemethod)
//          ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L32  (oncolumnexptrace)
//          ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L751-L759 (the trace)
//          docs/n_cst_dwsvc_columnexp.md, section 宏函数 (the documented macro handler)
//          ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L305-L309 (the live one)
//
//  BOTH OF THESE STREAMS RUN BACKWARDS, AND THAT IS STRUCTURAL RATHER THAN STYLISTIC. The legacy expects
//  the APPLICATION to implement the macro switch and to consume the trace, so once the expression engine
//  lives behind a service boundary, DataServices - the server - must call back into its client. AAP 0.4.3
//  records that as the reason contract C-04 needs a bidirectional stream at all, and the two interfaces
//  below are that inversion expressed as in-process types, which is what lets every path be reached with
//  no host, no port and no certificate.
//
//  THE ARGUMENT LIST IS ONE-BASED. Both the documented handler [docs/n_cst_dwsvc_columnexp.md, 宏函数]
//  and the live one [w_test_dwsvc_columnexp.srw:L306-L308] read `args[1]` and `args[2]`.
//  DIVERGENCE FROM THE PROMPT'S PARAPHRASE, NOTED AS INSTRUCTED: the script delegate below takes
//  `MacroArgumentList` rather than `string[]`. That is the type the production contract actually carries
//  (`MacroInvocation.Arguments`), it is genuinely one-based - its indexer rejects 0 with a message citing
//  docs/n_cst_dwsvc_columnexp.md:L126 - and it reads at the call site exactly as the PowerScript does. A
//  `string[]` could only be made one-based by adding an unused slot 0, which would be a fiction a reader
//  has to be warned about rather than a contract the compiler enforces.
// ==================================================================================================

/// <summary>
/// A scripted stand-in for the legacy <c>OnColumnExpInvokeMethod</c> event handler - the application-side
/// half of contract C-04's inverted <c>InvokeMethodChannel</c>.
/// </summary>
/// <param name="row">The row being calculated. ONE-BASED, as every DataWindow row is.</param>
/// <param name="dwo">The column whose expression is being evaluated.</param>
/// <param name="name">
/// The macro name being invoked. For the dynamic form the engine has already resolved the callee, so this
/// is the function actually being asked for rather than the literal <c>"Invoke"</c>.
/// </param>
/// <param name="args">
/// The evaluated arguments, ONE-BASED: <c>args[1]</c> is the first, exactly as the legacy handler reads
/// it. Index 0 throws.
/// </param>
/// <returns>
/// The value the handler produced, as the legacy <c>any</c>. Returning a value whose runtime type matches
/// none of the engine's accepted arms drives the engine's "invalid return value" refusal - see
/// <see cref="MacroScripts.InvalidReturnValue"/>.
/// </returns>
internal delegate object? ScriptedMacro(long row, IDataWindowObject dwo, string name, MacroArgumentList args);

/// <summary>
/// Ready-made macro scripts, so the several suites that need the documented handler share one
/// transcription of it.
/// </summary>
internal static class MacroScripts
{
    /// <summary>The macro name the legacy documentation and the live test window both use.</summary>
    public const string FormatPriceName = "FormatPrice";

    /// <summary>
    /// The documented <c>FormatPrice</c> handler: <c>Round(Double(args[1]),Long(args[2]))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from the two places the oracle states it identically - the specification's 宏函数
    /// section in <c>docs/n_cst_dwsvc_columnexp.md</c> and the live handler at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L305-L309</c>. The documented call site
    /// is <c>of_SetExp("n1", "$FormatPrice(n2, $精度)")</c> with the precision variable set to 1, so both
    /// arguments arrive as rendered strings and the handler converts them itself.
    /// </para>
    /// <para>
    /// <b>PowerScript conversion semantics, reproduced rather than tightened.</b> <c>Double</c> and
    /// <c>Long</c> of a non-numeric string yield 0 in PowerScript rather than raising, so
    /// <see cref="ParseDouble"/> and <see cref="ParseLong"/> do the same. Tightening this into a throw
    /// would make the double refuse inputs the legacy handler accepts, and would turn a calculation that
    /// the oracle completes into a test failure.
    /// </para>
    /// <para>
    /// <b>Rounding is half-away-from-zero and scale-based, which covers the whole integer domain.</b>
    /// PowerScript's <c>Round</c> rounds halves away from zero and accepts a NEGATIVE precision to round
    /// to tens or hundreds, so the scale-and-round form below is used in preference to
    /// <c>Math.Round(double, int)</c>, whose precision argument is limited to 0..15 and which would throw
    /// on a negative one. At extreme magnitudes the scale factor overflows to an IEEE infinity and the
    /// result is the infinity or NaN that double arithmetic dictates; that is returned as-is, because this
    /// is the client's handler and inventing a guard would be inventing behaviour the oracle has not got.
    /// </para>
    /// </remarks>
    /// <param name="row">Unused - the documented handler ignores it.</param>
    /// <param name="dwo">Unused - the documented handler ignores it.</param>
    /// <param name="name">Unused - dispatch on the name has already happened.</param>
    /// <param name="args">The evaluated arguments, one-based.</param>
    /// <returns>The rounded amount.</returns>
    public static object? FormatPrice(long row, IDataWindowObject dwo, string name, MacroArgumentList args)
    {
        ArgumentNullException.ThrowIfNull(args);

        double amount = ParseDouble(args.TryGet(1, out string first) ? first : string.Empty);
        long digits = ParseLong(args.TryGet(2, out string second) ? second : string.Empty);

        double scale = Math.Pow(10d, digits);

        return Math.Round(amount * scale, MidpointRounding.AwayFromZero) / scale;
    }

    /// <summary>The documented <c>FormatPrice</c> handler as a script.</summary>
    /// <remarks>
    /// A property rather than a field so the delegate is created on demand and no static mutable state
    /// exists for a test to disturb.
    /// </remarks>
    public static ScriptedMacro FormatPriceScript => FormatPrice;

    /// <summary>
    /// A script whose return value the engine MUST refuse, so both invalid-return-value paths are
    /// reachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine renders a macro result only for the scalar types the oracle's <c>choose case</c>
    /// enumerates; a value whose class matches no arm produces
    /// <c>"...函数[...],返回值无效"</c> and aborts the preprocessing pass. There are TWO such sites and
    /// they differ only in which name they report: the dynamic form names its first argument
    /// [<c>n_cst_dwsvc_columnexp.sru:L2282</c>] and the direct form names the reference
    /// [<c>:L2306</c>]. A plain <see cref="object"/> is the cleanest way to reach either, because no arm
    /// can ever claim it.
    /// </para>
    /// <para>
    /// A fresh instance per call, deliberately: a shared singleton would let a test assert reference
    /// equality on the refused value and so depend on an identity the contract does not promise.
    /// </para>
    /// </remarks>
    /// <param name="row">Unused.</param>
    /// <param name="dwo">Unused.</param>
    /// <param name="name">Unused.</param>
    /// <param name="args">Unused.</param>
    /// <returns>A value of a type no rendering arm accepts.</returns>
    public static object? InvalidReturnValue(
        long row,
        IDataWindowObject dwo,
        string name,
        MacroArgumentList args) => new object();

    /// <summary>The invalid-return-value script as a script.</summary>
    public static ScriptedMacro InvalidReturnValueScript => InvalidReturnValue;

    /// <summary>A script that echoes its arguments joined by a comma - the simplest observable handler.</summary>
    /// <param name="row">Unused.</param>
    /// <param name="dwo">Unused.</param>
    /// <param name="name">Unused.</param>
    /// <param name="args">The evaluated arguments, one-based.</param>
    /// <returns>The joined arguments, or the empty string when there are none.</returns>
    /// <remarks>
    /// Iterated from 1 to <c>UpperBound</c> because <c>MacroArgumentList</c> is one-based and its upper
    /// bound is the LAST VALID INDEX, matching PowerScript's <c>UpperBound</c> rather than a .NET length.
    /// AAP 0.4.5.4 names that difference the single most dangerous mechanical hazard in the refactor, so
    /// even a test helper spells the loop out rather than reaching for a zero-based idiom.
    /// </remarks>
    public static object? EchoArguments(
        long row,
        IDataWindowObject dwo,
        string name,
        MacroArgumentList args)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<string> values = new(args.Count);
        for (int index = 1; index <= args.UpperBound; index++)
        {
            values.Add(args[index]);
        }

        return string.Join(",", values);
    }

    /// <summary>The argument-echo script as a script.</summary>
    public static ScriptedMacro EchoArgumentsScript => EchoArguments;

    /// <summary>PowerScript <c>Double(string)</c>: a non-numeric string yields 0 rather than raising.</summary>
    /// <param name="text">The rendered argument.</param>
    /// <returns>The value, or 0.</returns>
    /// <remarks>
    /// Invariant culture, because a DataWindow expression renders with a fixed decimal point and a
    /// container's locale must not change what a macro computes.
    /// </remarks>
    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0d;

    /// <summary>PowerScript <c>Long(string)</c>: a non-numeric string yields 0 rather than raising.</summary>
    /// <param name="text">The rendered argument.</param>
    /// <returns>The value, or 0.</returns>
    private static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0L;
}

/// <summary>
/// A macro-invocation channel that records every invocation and answers from scripted handlers.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it differs from <c>RecordingMacroChannel</c> in ColumnExpressionEngineTests.</b> That one maps a
/// macro name to a FIXED value, which is all its suite needs. This one maps a name to a HANDLER that sees
/// the row, the column and the one-based arguments - the shape of the legacy event itself - so a test can
/// assert what the engine PASSED as well as what it did with the answer, and so the documented
/// <c>FormatPrice</c> behaviour can be exercised rather than approximated by a constant. Both are kept:
/// the fixed-value form is simpler where it suffices.
/// </para>
/// <para>
/// <b>Inert and total.</b> A name with no script answers <see cref="MacroInvocationResponse.Unhandled"/>,
/// which the contract defines as the client saying "not mine" and which the engine maps onto the legacy's
/// own outcome for a <c>choose case</c> that falls through without returning - the value's class matches no
/// arm, so the oracle raises <c>返回值无效</c> and aborts [<c>n_cst_dwsvc_columnexp.sru:L2282, :L2306</c>].
/// The channel therefore never returns <see langword="null"/>, which the contract calls a protocol
/// violation.
/// </para>
/// <para>
/// <b>It can also break the protocol on purpose.</b> <see cref="OverrideInvocationId"/> and
/// <see cref="OverrideSequence"/> make the correlation checks reachable, because a channel that always
/// echoed correctly would leave <c>MacroProtocolViolationException</c> untested - and that exception is the
/// guard standing between the inverted stream and a macro result being applied to the wrong calculation.
/// </para>
/// </remarks>
internal sealed class ScriptedMacroChannel : IMacroInvocationChannel
{
    private readonly List<MacroInvocation> _invocations = [];
    private readonly Dictionary<string, ScriptedMacro> _scripts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unhandled = new(StringComparer.Ordinal);

    /// <summary>Every invocation the engine issued, in order.</summary>
    /// <remarks>
    /// Order is the assertion for the same reason it is in section 1: the macro form is decided per
    /// reference, and a nested expression issues its inner invocations before its outer one.
    /// </remarks>
    public ImmutableArray<MacroInvocation> Invocations => [.. _invocations];

    /// <summary>The names invoked, in order, with repeats preserved.</summary>
    public ImmutableArray<string> InvokedNames => [.. _invocations.Select(invocation => invocation.Name)];

    /// <summary>The expansion form of each invocation, in order.</summary>
    /// <remarks>
    /// The form is a PER-REFERENCE property, never per-expression: AAP 0.6.2.2 records that the legacy
    /// specification documents an expression mixing dynamic and static expansion, so a suite has to be able
    /// to see the form of each invocation separately.
    /// </remarks>
    public ImmutableArray<ExpansionMode> InvokedForms => [.. _invocations.Select(invocation => invocation.Form)];

    /// <summary>How many invocations were issued.</summary>
    public int InvocationCount => _invocations.Count;

    /// <summary>The most recent invocation, or <see langword="null"/> when none was issued.</summary>
    public MacroInvocation? LastInvocation => _invocations.Count == 0 ? null : _invocations[^1];

    /// <summary>
    /// An invocation identifier to answer with instead of the one that was asked, or
    /// <see langword="null"/> to echo correctly.
    /// </summary>
    public string? OverrideInvocationId { get; set; }

    /// <summary>
    /// A sequence number to answer with instead of the one that was asked, or <see langword="null"/> to
    /// echo the invocation's own.
    /// </summary>
    public long? OverrideSequence { get; set; }

    /// <summary>
    /// Whether to echo the sequence number at all; clearing it models a client that answers without one,
    /// which the contract permits.
    /// </summary>
    public bool EchoSequence { get; set; } = true;

    /// <summary>
    /// An exception to throw instead of answering, or <see langword="null"/> to answer.
    /// </summary>
    /// <remarks>
    /// Thrown AFTER the invocation is recorded, so a suite can prove the engine issued the call before the
    /// transport failed - which is the difference between a failure the engine caused and one it merely
    /// observed.
    /// </remarks>
    public Exception? Fault { get; set; }

    /// <summary>A side effect to run inside the channel, after recording and before answering.</summary>
    /// <remarks>
    /// The channel is the only await point inside a calculation pass, so it is the only place from which a
    /// nested pass can be driven. A suite needing re-entrancy hangs it here.
    /// </remarks>
    public Action<MacroInvocation>? OnInvoke { get; set; }

    /// <summary>Scripts a macro name with a handler.</summary>
    /// <param name="name">The macro name, compared ordinally and case-sensitively.</param>
    /// <param name="script">The handler.</param>
    /// <returns>This channel, so scripting can be chained.</returns>
    /// <remarks>
    /// ORDINAL AND CASE-SENSITIVE, because the oracle's dispatch is a PowerScript <c>choose case</c> on the
    /// name and PowerScript string comparison is case-sensitive. A case-insensitive table here would answer
    /// invocations the oracle would have fallen through on.
    /// </remarks>
    public ScriptedMacroChannel Script(string name, ScriptedMacro script)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(script);

        _scripts[name] = script;
        _ = _unhandled.Remove(name);

        return this;
    }

    /// <summary>Scripts a macro name with a fixed value.</summary>
    /// <param name="name">The macro name.</param>
    /// <param name="value">The value to answer with.</param>
    /// <returns>This channel, so scripting can be chained.</returns>
    public ScriptedMacroChannel ScriptValue(string name, object? value) =>
        Script(name, (_, _, _, _) => value);

    /// <summary>Scripts a macro name to answer with a value the engine must refuse.</summary>
    /// <param name="name">The macro name.</param>
    /// <returns>This channel, so scripting can be chained.</returns>
    public ScriptedMacroChannel ScriptInvalidReturnValue(string name) =>
        Script(name, MacroScripts.InvalidReturnValueScript);

    /// <summary>Scripts a macro name to answer "not mine", even if it was previously scripted.</summary>
    /// <param name="name">The macro name.</param>
    /// <returns>This channel, so scripting can be chained.</returns>
    public ScriptedMacroChannel ScriptUnhandled(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        _ = _unhandled.Add(name);

        return this;
    }

    /// <summary>Scripts the documented <c>FormatPrice</c> handler under its documented name.</summary>
    /// <returns>This channel, so scripting can be chained.</returns>
    public ScriptedMacroChannel ScriptFormatPrice() =>
        Script(MacroScripts.FormatPriceName, MacroScripts.FormatPriceScript);

    /// <summary>Every invocation of one macro name, in order.</summary>
    /// <param name="name">The macro name.</param>
    /// <returns>The matching invocations.</returns>
    public ImmutableArray<MacroInvocation> InvocationsOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return [.. _invocations.Where(invocation =>
            string.Equals(invocation.Name, name, StringComparison.Ordinal))];
    }

    /// <inheritdoc/>
    public ValueTask<MacroInvocationResponse> InvokeAsync(
        MacroInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        _invocations.Add(invocation);
        OnInvoke?.Invoke(invocation);

        if (Fault is not null)
        {
            throw Fault;
        }

        string invocationId = OverrideInvocationId ?? invocation.InvocationId;
        long? sequence = EchoSequence ? OverrideSequence ?? invocation.Sequence : null;

        if (_unhandled.Contains(invocation.Name)
            || !_scripts.TryGetValue(invocation.Name, out ScriptedMacro? script))
        {
            return ValueTask.FromResult(new MacroInvocationResponse
            {
                InvocationId = invocationId,
                Sequence = sequence,
                Unhandled = true,
            });
        }

        object? value = script(invocation.Row, invocation.Dwo, invocation.Name, invocation.Arguments);

        // SYNCHRONOUSLY COMPLETED, with no continuation and no retry of its own. A double that awaited
        // anything would let a test hang where it should fail, and a double that retried would make the
        // invocation count - the evidence that the engine issued exactly one call - meaningless.
        return ValueTask.FromResult(new MacroInvocationResponse
        {
            InvocationId = invocationId,
            Sequence = sequence,
            Value = value,
        });
    }
}

/// <summary>
/// An expression-trace sink that keeps every record and can compute the payload the oracle would have
/// produced, so a trace can be asserted as a whole rather than field by field.
/// </summary>
/// <remarks>
/// <para>
/// <b>The trace is contract C-04's <c>TraceChannel</c>, and it is the second inverted stream.</b> The
/// oracle raises it on the DataWindow itself -
/// <c>#DataWindow.Event OnColumnExpTrace(row,dwo,sCallStack,sExp,value)</c> at
/// <c>n_cst_dwsvc_columnexp.sru:L758</c> - and the live consumer at
/// <c>ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L317-L320</c> simply appends it to a
/// multi-line edit, which is what makes it fire-and-forget diagnostics rather than control flow. AAP
/// 0.6.1.4 assigns it the sequencing-token discipline for exactly that reason, so this sink records the
/// sequence number and never re-orders.
/// </para>
/// <para>
/// <b>The two computed helpers are restatements of the ORACLE, not of the port.</b>
/// <see cref="ComposeStack"/> and <see cref="ExpectedValue"/> were written from
/// <c>n_cst_dwsvc_columnexp.sru:L753-L758</c>, not from <c>Expressions/ColumnExpressionEngine.cs</c>. An
/// expectation derived from the code under test would assert only that the code equals itself, which is
/// the one thing a characterization test must not do.
/// </para>
/// <para>
/// <b>How it differs from <c>RecordingTraceSink</c> in ColumnExpressionEngineTests.</b> That one is a bare
/// list, which is all its suite needs. This one adds the two oracle restatements and a fault seam, so a
/// suite in another file can compute an expected payload without re-deriving the composition rules.
/// </para>
/// </remarks>
internal sealed class ScriptedTraceSink : IExpressionTraceSink
{
    /// <summary>The separator the oracle joins call-stack frames with.</summary>
    /// <remarks>
    /// A single <c>&gt;</c>. The oracle appends it after EACH frame and then appends the DataWindow
    /// object's name [<c>n_cst_dwsvc_columnexp.sru:L753-L757</c>], so the payload has NO TRAILING
    /// SEPARATOR - the terminal name closes it. That is why <see cref="ComposeStack"/> joins frames plus
    /// terminal rather than suffixing each frame.
    /// </remarks>
    public const string StackSeparator = ">";

    /// <summary>
    /// The literal the oracle traces in place of an empty value - four characters, parentheses included.
    /// </summary>
    /// <remarks>
    /// Emitted by <c>iif(sVal = "" and (emptyStringIsNull or colType &lt;&gt; COL_TYPE_STRING),"(null)",sVal)</c>
    /// at <c>n_cst_dwsvc_columnexp.sru:L758</c>. It is a RENDERING, not a null: the traced string is
    /// literally these characters, which is what a golden-master comparison of a recording will contain.
    /// </remarks>
    public const string NullValueSentinel = "(null)";

    private readonly List<ExpressionTraceRecord> _records = [];

    /// <summary>Every record, in emission order.</summary>
    public ImmutableArray<ExpressionTraceRecord> Records => [.. _records];

    /// <summary>How many records were emitted.</summary>
    public int Count => _records.Count;

    /// <summary>The most recent record, or <see langword="null"/> when none was emitted.</summary>
    public ExpressionTraceRecord? LastRecord => _records.Count == 0 ? null : _records[^1];

    /// <summary>The call-stack payloads, in emission order.</summary>
    public ImmutableArray<string> Stacks => [.. _records.Select(record => record.Stack)];

    /// <summary>The expression texts, in emission order.</summary>
    public ImmutableArray<string> Expressions => [.. _records.Select(record => record.Expression)];

    /// <summary>The traced values, in emission order, sentinels included as literal text.</summary>
    public ImmutableArray<string> Values => [.. _records.Select(record => record.Value)];

    /// <summary>The sequence numbers, in emission order.</summary>
    /// <remarks>
    /// Recorded so a test can assert MONOTONICITY, which is the whole point of the token: under the
    /// sequencing-token discipline an out-of-order arrival is detectable, and a sink that discarded the
    /// number would make it undetectable.
    /// </remarks>
    public ImmutableArray<long> SequenceNumbers => [.. _records.Select(record => record.SequenceNumber)];

    /// <summary>
    /// An exception to throw from <see cref="Emit"/>, or <see langword="null"/> to accept records.
    /// </summary>
    /// <remarks>
    /// Thrown AFTER the record is kept, so a suite can prove the trace was produced even when its delivery
    /// failed - the trace is diagnostics, and a diagnostic sink that fails must not lose the evidence that
    /// the engine tried to emit.
    /// </remarks>
    public Exception? Fault { get; set; }

    /// <summary>Composes the call-stack payload the way <c>:L753-L757</c> does.</summary>
    /// <param name="frames">
    /// The calc-stack frames, outermost first, exactly as the oracle walks its vector from index 1 to
    /// <c>Count()</c>.
    /// </param>
    /// <param name="terminalName">
    /// The DataWindow object's name, which the oracle appends after the last separator.
    /// </param>
    /// <returns>The payload, with no trailing separator.</returns>
    /// <remarks>
    /// The oracle's loop appends <c>frame + "&gt;"</c> for each frame and then appends the terminal name, so
    /// the result is identical to joining the frames AND the terminal with the separator. Writing it as a
    /// join makes the absence of a trailing separator structural rather than incidental. An empty frame
    /// sequence yields the terminal name alone, which is what the oracle produces for a top-level
    /// calculation - its vector is empty until the first nested call pushes onto it.
    /// </remarks>
    public static string ComposeStack(IEnumerable<string> frames, string terminalName)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(terminalName);

        return string.Join(StackSeparator, frames.Append(terminalName));
    }

    /// <summary>Computes the traced value the way the <c>iif</c> at <c>:L758</c> does.</summary>
    /// <param name="value">The evaluated value, as rendered by the DataWindow expression evaluator.</param>
    /// <param name="emptyStringIsNull">
    /// The column expression's own <c>emptyStringIsNull</c> flag - the port of
    /// <c>ColExpDatas[index].emptyStringIsNull</c>.
    /// </param>
    /// <param name="colType">
    /// The column type code, compared against <c>DataWindowServiceBase.COL_TYPE_STRING</c>.
    /// </param>
    /// <returns><see cref="NullValueSentinel"/> or the value unchanged.</returns>
    /// <remarks>
    /// TWO CONDITIONS SELECT THE SENTINEL, and their conjunction matters: the value must be empty AND
    /// either the column treats empty as null or the column is not a string column. So a STRING column
    /// without the flag traces the empty string rather than the sentinel - the one combination a reader is
    /// most likely to get wrong. A null <paramref name="value"/> is treated as empty, because PowerScript
    /// compares a null string to <c>""</c> as unequal only in a three-valued sense the oracle never
    /// reaches here: the evaluator returns a rendered string, and the port's own contract carries a
    /// non-null one.
    /// </remarks>
    public static string ExpectedValue(string? value, bool emptyStringIsNull, long colType) =>
        string.IsNullOrEmpty(value)
            && (emptyStringIsNull || colType != DataWindowServiceBase.COL_TYPE_STRING)
                ? NullValueSentinel
                : value ?? string.Empty;

    /// <summary>Every record whose column name matches, in emission order.</summary>
    /// <param name="columnName">The column name, compared ordinally.</param>
    /// <returns>The matching records.</returns>
    public ImmutableArray<ExpressionTraceRecord> For(string columnName)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        return [.. _records.Where(record =>
            string.Equals(record.ColumnName, columnName, StringComparison.Ordinal))];
    }

    /// <summary>Discards every record, so one fixture can drive several scenarios.</summary>
    public void Clear() => _records.Clear();

    /// <inheritdoc/>
    public void Emit(ExpressionTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _records.Add(record);

        if (Fault is not null)
        {
            throw Fault;
        }
    }
}

// ==================================================================================================
//  SECTION 4 - THE TWO OUTBOUND-EDGE SEAMS
//  ------------------------------------------------------------------------------------------------
//  DataServices has exactly two outbound edges, and BOTH are substitutable through the same seam without
//  a mocking package:
//
//    * Security  - `Clients/SecurityClient.cs` is a typed `HttpClient` (contracts C-01 and C-02, REST).
//    * Persistence - `Clients/PersistenceClient.cs` runs over a `Grpc.Net.Client` channel registered by
//      `AddGrpcClient` (contracts C-05 to C-08, gRPC).
//
//  Both resolve their PRIMARY HANDLER through `IHttpClientFactory`, so replacing that handler substitutes
//  both. `ScriptedHttpMessageHandler` is that replacement. Where a suite needs to reach a specific gRPC
//  RESPONSE rather than a transport, `ScriptedPersistenceResponses` builds the messages and failures
//  directly and hands them to the generated-stub doubles that already exist in PersistenceClientTests -
//  which is why nothing here re-implements a gRPC call wrapper or a stream reader.
//
//  NOTHING HERE REACHES THE NETWORK, AND NOTHING HERE HANGS. Every scripted answer completes
//  SYNCHRONOUSLY and no double retries on its own account. Both properties are deliberate: a double that
//  awaited a real socket would turn a wiring mistake into a timeout instead of a failure, and a double
//  that retried would destroy the call-count evidence that proves the no-silent-overwrite guarantee. An
//  unscripted route THROWS with the method and path it saw, so a test that has wired the wrong URL fails
//  loudly and immediately.
//
//  AUTHENTICATION IS OBSERVED, NEVER DISABLED (C-G). The recorder neither adds nor strips an Authorization
//  header; it reports whether one arrived and under which scheme, which is what lets a test PROVE the
//  credential was attached. And it records only the PRESENCE of the credential parameter, never the
//  parameter itself, so a captured exchange cannot leak one into an assertion message or a CI log (C-F).
// ==================================================================================================

/// <summary>
/// One HTTP request as <see cref="ScriptedHttpMessageHandler"/> observed it.
/// </summary>
/// <remarks>
/// <b>THE CREDENTIAL VALUE IS DELIBERATELY NOT CAPTURED.</b> <see cref="AuthorizationScheme"/> and
/// <see cref="HasAuthorizationParameter"/> record that a credential arrived and how it was framed;
/// the parameter itself is discarded on the spot. Constraint C-F forbids a credential appearing anywhere
/// in this file, and a recorder that stored one would put it into every failure message that renders the
/// exchange - which is precisely how credentials end up in CI logs.
/// </remarks>
internal sealed record HttpExchangeRecord
{
    /// <summary>The position of this exchange, one-based.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The request method.</summary>
    public required HttpMethod Method { get; init; }

    /// <summary>The request URI, which may be relative if the client sent one.</summary>
    public Uri? RequestUri { get; init; }

    /// <summary>
    /// The URI's path, or the empty string when the request carried no URI - the value a route matches on.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>The request body as text, empty when the request carried no content.</summary>
    public required string Body { get; init; }

    /// <summary>The request content type, or <see langword="null"/> when there was no content.</summary>
    public string? ContentType { get; init; }

    /// <summary>Whether an Authorization header arrived.</summary>
    public required bool HasAuthorization { get; init; }

    /// <summary>
    /// The Authorization scheme - <c>Bearer</c> for a token-authenticated call - or
    /// <see langword="null"/> when no Authorization header arrived.
    /// </summary>
    public string? AuthorizationScheme { get; init; }

    /// <summary>Whether the Authorization header carried a parameter, without recording it.</summary>
    public bool HasAuthorizationParameter { get; init; }

    /// <summary>The request header names, so a test can assert a header WITHOUT reading its value.</summary>
    public ImmutableArray<string> HeaderNames { get; init; } = [];

    /// <summary>
    /// Whether the cancellation token the client passed could actually be cancelled.
    /// </summary>
    /// <remarks>
    /// A client that dropped its caller's token and substituted <see cref="CancellationToken.None"/> would
    /// still compile and still work, and would silently make a cancelled request uncancellable. This flag
    /// is the only way a test can tell.
    /// </remarks>
    public required bool CancellationTokenCanBeCanceled { get; init; }

    /// <summary>A compact one-line rendering, safe to put in a failure message.</summary>
    /// <returns>The rendering.</returns>
    public override string ToString() =>
        Ordinal.ToString(CultureInfo.InvariantCulture)
            + ". "
            + Method.Method
            + " "
            + Path
            + (HasAuthorization ? " auth=" + (AuthorizationScheme ?? "(no scheme)") : " auth=absent");
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from routes scripted by method and path, and records
/// every request it was given.
/// </summary>
/// <remarks>
/// <para>
/// <b>Route-scripted rather than queue-scripted, and that is the point of it.</b> The FIFO handler in
/// SecurityClientTests is the right shape for a suite driving one endpoint repeatedly. This one is the
/// right shape for a scenario that touches SEVERAL endpoints in an order the test does not want to
/// hard-code - a crypto call that must first acquire a credential from a different path, for instance -
/// because a route answers however many times it is hit and the test never has to count.
/// </para>
/// <para>
/// <b>Routes are matched in registration order and are never consumed.</b> Registering the same
/// method and path twice therefore means the FIRST registration wins for every hit; that is stated rather
/// than defended against, because silently replacing an earlier route would hide a scenario's own
/// mistake. Use <see cref="RouteSequence"/> when successive hits must answer differently.
/// </para>
/// </remarks>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly List<ScriptedRoute> _routes = [];
    private readonly List<HttpExchangeRecord> _exchanges = [];

    /// <summary>Every request, in order.</summary>
    public ImmutableArray<HttpExchangeRecord> Exchanges => [.. _exchanges];

    /// <summary>How many requests were made.</summary>
    public int RequestCount => _exchanges.Count;

    /// <summary>The most recent request, or <see langword="null"/> when none was made.</summary>
    public HttpExchangeRecord? LastExchange => _exchanges.Count == 0 ? null : _exchanges[^1];

    /// <summary>The paths requested, in order, with repeats preserved.</summary>
    public ImmutableArray<string> RequestedPaths => [.. _exchanges.Select(exchange => exchange.Path)];

    /// <summary>Scripts a route with a responder the caller builds.</summary>
    /// <param name="method">The method to match, or <see langword="null"/> to match any method.</param>
    /// <param name="path">The absolute path to match, compared ordinally.</param>
    /// <param name="responder">
    /// Builds the answer. Invoked synchronously; it may throw, which models a transport failure.
    /// </param>
    /// <returns>This handler, so routing can be chained.</returns>
    public ScriptedHttpMessageHandler Route(
        HttpMethod? method,
        string path,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(responder);

        _routes.Add(new ScriptedRoute(method, path, responder));

        return this;
    }

    /// <summary>Scripts a route answering a JSON body.</summary>
    /// <param name="method">The method to match, or <see langword="null"/> for any.</param>
    /// <param name="path">The absolute path to match.</param>
    /// <param name="statusCode">The status to answer with.</param>
    /// <param name="json">The body, or <see langword="null"/> for no content.</param>
    /// <returns>This handler, so routing can be chained.</returns>
    /// <remarks>
    /// UTF-8 and <c>application/json</c>, because that is what both REST contracts publish. The body is
    /// taken verbatim - it is NOT reformatted, reindented or validated, so a suite can script a malformed
    /// document and reach the client's own decoding failure path.
    /// </remarks>
    public ScriptedHttpMessageHandler RouteJson(
        HttpMethod? method,
        string path,
        HttpStatusCode statusCode,
        string? json) =>
        Route(method, path, _ => new HttpResponseMessage(statusCode)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Scripts a route answering a bare status with no body.</summary>
    /// <param name="method">The method to match, or <see langword="null"/> for any.</param>
    /// <param name="path">The absolute path to match.</param>
    /// <param name="statusCode">The status to answer with.</param>
    /// <returns>This handler, so routing can be chained.</returns>
    public ScriptedHttpMessageHandler RouteStatus(
        HttpMethod? method,
        string path,
        HttpStatusCode statusCode) =>
        Route(method, path, _ => new HttpResponseMessage(statusCode));

    /// <summary>Scripts a route that fails in transit.</summary>
    /// <param name="method">The method to match, or <see langword="null"/> for any.</param>
    /// <param name="path">The absolute path to match.</param>
    /// <param name="fault">The exception to throw.</param>
    /// <returns>This handler, so routing can be chained.</returns>
    /// <remarks>
    /// THIS IS THE FAILURE MODE DECOMPOSITION ITSELF CREATED. An in-process call cannot fail in transit
    /// and a network call can, which is the whole justification AAP 0.5.3 gives for the resilience
    /// package - so the transit failure has to be reachable in a test or the resilience wiring is
    /// unverified.
    /// </remarks>
    public ScriptedHttpMessageHandler RouteFault(HttpMethod? method, string path, Exception fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        return Route(method, path, _ => throw fault);
    }

    /// <summary>Scripts a route whose successive hits answer from a sequence.</summary>
    /// <param name="method">The method to match, or <see langword="null"/> for any.</param>
    /// <param name="path">The absolute path to match.</param>
    /// <param name="responders">The answers, in order.</param>
    /// <returns>This handler, so routing can be chained.</returns>
    /// <remarks>
    /// The LAST responder answers every hit beyond the sequence, rather than the route falling through to
    /// the loud failure. That choice keeps the sequence about the interesting prefix - "fail once, then
    /// succeed" - without a test having to know exactly how many times the client will call.
    /// </remarks>
    public ScriptedHttpMessageHandler RouteSequence(
        HttpMethod? method,
        string path,
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        ArgumentNullException.ThrowIfNull(responders);

        if (responders.Length == 0)
        {
            throw new ArgumentException(
                "A scripted route sequence needs at least one responder.",
                nameof(responders));
        }

        int hits = 0;

        return Route(method, path, request =>
        {
            int index = Math.Min(hits, responders.Length - 1);
            hits++;

            return responders[index](request);
        });
    }

    /// <summary>Every request whose path matches, in order.</summary>
    /// <param name="path">The path, compared ordinally.</param>
    /// <returns>The matching exchanges.</returns>
    public ImmutableArray<HttpExchangeRecord> ExchangesFor(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return [.. _exchanges.Where(exchange =>
            string.Equals(exchange.Path, path, StringComparison.Ordinal))];
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        // Task.FromResult, never Task.Run and never an await: the answer is already computed, so the
        // continuation a test observes is its own and not a thread-pool artefact.
        Task.FromResult(Answer(request, cancellationToken));

    /// <inheritdoc/>
    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        // Overridden as well as SendAsync, because the base throws NotSupportedException and a client that
        // legitimately sends synchronously would otherwise fail for a reason that has nothing to do with
        // what its test was checking.
        Answer(request, cancellationToken);

    /// <summary>Records the request, finds its route, and produces the answer.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The token the client passed.</param>
    /// <returns>The scripted answer.</returns>
    /// <exception cref="InvalidOperationException">No route matched.</exception>
    private HttpResponseMessage Answer(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string path = request.RequestUri is null
            ? string.Empty
            : request.RequestUri.IsAbsoluteUri
                ? request.RequestUri.AbsolutePath
                : request.RequestUri.OriginalString;

        AuthenticationHeaderValue? authorization = request.Headers.Authorization;

        _exchanges.Add(new HttpExchangeRecord
        {
            Ordinal = _exchanges.Count + 1,
            Method = request.Method,
            RequestUri = request.RequestUri,
            Path = path,
            Body = ReadBody(request),
            ContentType = request.Content?.Headers.ContentType?.ToString(),
            HasAuthorization = authorization is not null,
            AuthorizationScheme = authorization?.Scheme,

            // The parameter's PRESENCE only. Its value is never stored - see the note on
            // HttpExchangeRecord.
            HasAuthorizationParameter = !string.IsNullOrEmpty(authorization?.Parameter),
            HeaderNames = [.. request.Headers.Select(header => header.Key)],
            CancellationTokenCanBeCanceled = cancellationToken.CanBeCanceled,
        });

        foreach (ScriptedRoute route in _routes)
        {
            if (route.Matches(request.Method, path))
            {
                return route.Responder(request);
            }
        }

        // LOUD, NOT DEFAULTED. Answering 500 here would let a test that wired the wrong path pass through
        // the client's error handling and assert something plausible about a request that never reached its
        // intended endpoint. The message names what arrived and what was scripted, which is everything
        // needed to fix the wiring.
        throw new InvalidOperationException(
            "No scripted route matched "
                + request.Method.Method
                + " "
                + (path.Length == 0 ? "(no URI)" : path)
                + ". Scripted routes: "
                + (_routes.Count == 0 ? "(none)" : string.Join(", ", _routes.Select(route => route.ToString())))
                + ". A double must never reach the network, so an unmatched request fails here rather than "
                + "being answered with a plausible status.");
    }

    /// <summary>Reads a request body as text, synchronously.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The body, or the empty string when there was none.</returns>
    /// <remarks>
    /// Blocking is safe and intended: every body a client under test produces is already buffered in
    /// memory, so the read completes without yielding. <see cref="CancellationToken.None"/> is passed
    /// because cancelling this read would report a body the client did send as absent.
    /// </remarks>
    private static string ReadBody(HttpRequestMessage request) =>
        request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// One scripted route. Named <c>ScriptedRoute</c> rather than <c>Route</c> because the enclosing type
    /// already declares a <see cref="Route(HttpMethod, string, Func{HttpRequestMessage, HttpResponseMessage})"/>
    /// method, and a nested type may not share a name with a member of its container.
    /// </summary>
    /// <param name="Method">The method to match, or <see langword="null"/> for any.</param>
    /// <param name="Path">The path to match.</param>
    /// <param name="Responder">Builds the answer.</param>
    private sealed record ScriptedRoute(
        HttpMethod? Method,
        string Path,
        Func<HttpRequestMessage, HttpResponseMessage> Responder)
    {
        /// <summary>Whether this route matches a request.</summary>
        /// <param name="method">The request method.</param>
        /// <param name="path">The request path.</param>
        /// <returns><see langword="true"/> when it matches.</returns>
        /// <remarks>
        /// The path comparison is ORDINAL and case-sensitive. Both REST contracts publish lower-case paths
        /// and a case-insensitive match here would accept a client that got the casing wrong, which a real
        /// server need not.
        /// </remarks>
        public bool Matches(HttpMethod method, string path) =>
            (Method is null || Method == method)
                && string.Equals(Path, path, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override string ToString() => (Method?.Method ?? "*") + " " + Path;
    }
}

/// <summary>
/// Scripted Persistence answers: the three shapes a DataServices suite has to be able to produce on the
/// gRPC edge without a Persistence instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>It builds MESSAGES AND FAILURES, not calls.</b> The transport wrappers already exist beside the
/// suite that owns them - the unary and server-streaming call factories and the list-backed stream reader
/// in <c>PersistenceClientTests.cs</c> - and duplicating them here would create a second definition of
/// how a gRPC answer is framed. So this type produces exactly the payloads, and a suite hands them to
/// whichever stub double it is already using.
/// </para>
/// <para>
/// <b>Every legacy guard the payloads express is traced to its locator</b>, because these are the three
/// shapes contract C-05 and C-06 are actually judged on: progressive delivery, the chunk-size refusal, and
/// the conflict that must never become a silent overwrite.
/// </para>
/// </remarks>
internal static class ScriptedPersistenceResponses
{
    /// <summary>
    /// The only update table the repository evidences, from the sole updatable DataWindow
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>.
    /// </summary>
    /// <remarks>
    /// Named here so a conflict payload does not invent a table. AAP 0.6.3.1 records that this single file
    /// is the golden-master fixture for the whole retrieval / validation / update triple, and AAP 0.2.2.5
    /// forbids fabricating a schema.
    /// </remarks>
    public const string EvidencedUpdateTable = "COMPANY";

    /// <summary>
    /// The gRPC method name contract C-06 publishes its conflict binding on.
    /// </summary>
    private const string UpdateMethodName = "Update";

    /// <summary>The row-count message a retrieval leads with.</summary>
    /// <param name="rowCount">The row count to report.</param>
    /// <returns>The message.</returns>
    public static QueryResponse RowCountMessage(long rowCount) =>
        new() { RowCount = new QueryRowCount { RowCount = rowCount } };

    /// <summary>A buffer-shaped data chunk.</summary>
    /// <param name="chunkIndex">
    /// The chunk's position. ONE-BASED, matching the legacy chunk numbering and every other index in this
    /// service (AAP 0.4.5.4).
    /// </param>
    /// <param name="chunkCount">How many chunks the retrieval will deliver in total.</param>
    /// <param name="fullState">
    /// Whether this chunk carries FULL STATE rather than a changeset. The two transfer modes have different
    /// documented defects - full state needs sort and filter synchronised
    /// [<c>n_cst_thread_task_sqlquery.sru:L563</c>] and crashes on a wide crosstab [<c>:L673</c>], while a
    /// changeset may lose rows on a sorted multi-block DataWindow [<c>:L148</c>] - so which one a chunk
    /// claims to be is part of what a test asserts.
    /// </param>
    /// <param name="buffers">
    /// The buffers this chunk carries a segment for, in order. THE BUFFER SHAPE IS THE POINT: the legacy
    /// carrier derives from a datastore, so the result IS a DataWindow with Primary, Delete and Filter
    /// buffers and per-row item statuses, and a flat rowset could not carry the original values that
    /// <c>updatewhere=1</c> concurrency depends on (AAP 0.3.4).
    /// </param>
    /// <returns>The message.</returns>
    public static QueryResponse DataChunkMessage(
        long chunkIndex,
        long chunkCount,
        bool fullState,
        params DwBuffer[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);

        CarrierState state = new();
        foreach (DwBuffer buffer in buffers)
        {
            state.Segments.Add(new CarrierBufferSegment { Buffer = buffer });
        }

        return new QueryResponse
        {
            DataChunk = new QueryDataChunk
            {
                State = state,
                ChunkCount = chunkCount,
                ChunkIndex = chunkIndex,
                FullState = fullState,
            },
        };
    }

    /// <summary>The terminating status message of a retrieval.</summary>
    /// <param name="retCode">The outcome code.</param>
    /// <param name="errorText">The diagnostic, or the empty string for a success.</param>
    /// <returns>The message.</returns>
    public static QueryResponse StatusMessage(WireRetCode retCode, string errorText = "") =>
        new() { Status = new OperationStatus { RetCode = retCode, ErrorText = errorText ?? string.Empty } };

    /// <summary>
    /// A complete, well-formed server-streamed retrieval: the row count, then one Primary-buffer chunk per
    /// chunk index, then a success status.
    /// </summary>
    /// <param name="rowCount">The row count to lead with.</param>
    /// <param name="chunkCount">How many data chunks to produce; must be at least one.</param>
    /// <param name="fullStateOnFinalChunk">
    /// Whether the LAST chunk claims full state, which is the shape the client's own suite exercises.
    /// </param>
    /// <returns>The messages, in delivery order.</returns>
    /// <remarks>
    /// The chunk loop runs from 1 to <paramref name="chunkCount"/> INCLUSIVE, because the chunk index is
    /// one-based. Returned as an ordered list rather than an async stream so the caller decides how it is
    /// delivered - which is what lets the same script drive a progressive-delivery assertion and a plain
    /// content assertion.
    /// </remarks>
    public static ImmutableArray<QueryResponse> QueryStream(
        long rowCount,
        int chunkCount = 1,
        bool fullStateOnFinalChunk = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkCount, 1);

        List<QueryResponse> messages = new(chunkCount + 2) { RowCountMessage(rowCount) };

        for (int index = 1; index <= chunkCount; index++)
        {
            messages.Add(DataChunkMessage(
                index,
                chunkCount,
                fullStateOnFinalChunk && index == chunkCount,
                DwBuffer.Primary));
        }

        messages.Add(StatusMessage(WireRetCode.Ok));

        return [.. messages];
    }

    /// <summary>
    /// The refusal the chunk-size setting answers for a value the legacy guard rejects.
    /// </summary>
    /// <param name="requestedChunkSize">
    /// The value that was asked for, echoed into the diagnostic so a failure names it.
    /// </param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// <b>THE GUARD IS "AT OR BELOW 1000", SO 1001 IS THE SMALLEST LEGAL VALUE</b>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410</c>], and the refusal code
    /// is <c>RetCode.E_INVALID_ARGUMENT</c>. The boundary is the whole assertion: 1000 is refused and 1001
    /// is accepted, and a double that rejected at the wrong side of it would pin the wrong contract. The
    /// two spellings of the same number are deliberately BOTH referenced - the kernel constant for the
    /// documented value and the wire enumerator for the payload - because the client converts between the
    /// two families at exactly one place and the numeric agreement is a review-time invariant.
    /// </remarks>
    public static SetChunkSizeResponse ChunkSizeRejection(long requestedChunkSize) =>
        new()
        {
            Status = new OperationStatus
            {
                RetCode = WireRetCode.EInvalidArgument,
                ErrorText = "The requested chunk size "
                    + requestedChunkSize.ToString(CultureInfo.InvariantCulture)
                    + " is at or below the legacy guard's 1000 "
                    + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410], so it is "
                    + "refused with "
                    + RetCode.E_INVALID_ARGUMENT.ToString(CultureInfo.InvariantCulture)
                    + ".",
            },
        };

    /// <summary>
    /// A conflict row carrying BOTH the current and the original value of one column.
    /// </summary>
    /// <param name="row">The row, one-based.</param>
    /// <param name="columnName">The column's name.</param>
    /// <param name="columnId">The column's one-based identifier.</param>
    /// <param name="currentValue">The value now in the database.</param>
    /// <param name="originalValue">The value the failed statement's where-clause compared against.</param>
    /// <returns>The row.</returns>
    /// <remarks>
    /// <b>BOTH VALUE SETS, ALWAYS.</b> The sole updatable DataWindow declares
    /// <c>updatewhere=1</c> with all six columns marked <c>updatewhereclause=yes</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so the generated statement compared the
    /// ORIGINAL value of every marked column. A conflict payload carrying only current values could not
    /// tell a caller which column moved, which is the difference between a caller that can act and one
    /// that can only fail.
    /// </remarks>
    public static ConflictRow ModifiedConflictRow(
        long row,
        string columnName,
        long columnId,
        double currentValue,
        double originalValue)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        ConflictRow conflictRow = new()
        {
            Buffer = DwBuffer.Primary,
            Row = row,
            ItemStatus = ItemStatus.DataModified,
        };

        conflictRow.CurrentValues.Add(new ColumnValue
        {
            ColumnName = columnName,
            ColumnId = columnId,
            Value = new AnyValue { DoubleValue = currentValue },
            ItemStatus = ItemStatus.DataModified,
        });

        conflictRow.OriginalValues.Add(new ColumnValue
        {
            ColumnName = columnName,
            ColumnId = columnId,
            Value = new AnyValue { DoubleValue = originalValue },
            ItemStatus = ItemStatus.DataModified,
        });

        return conflictRow;
    }

    /// <summary>A conflict detail over the only evidenced update table.</summary>
    /// <param name="rowsExpected">How many rows the statement expected to match.</param>
    /// <param name="rowsMatched">How many it actually matched.</param>
    /// <param name="rows">The rows in conflict, with their current and original state.</param>
    /// <returns>The detail.</returns>
    public static ConflictDetail Conflict(long rowsExpected, long rowsMatched, params ConflictRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        ConflictDetail detail = new()
        {
            UpdateTable = EvidencedUpdateTable,
            RowsExpected = rowsExpected,
            RowsMatched = rowsMatched,
        };

        detail.Rows.Add(rows);

        return detail;
    }

    /// <summary>
    /// The rich-error binding contract C-06 declares for its update operation, read from the generated
    /// descriptor.
    /// </summary>
    /// <returns>The binding.</returns>
    /// <exception cref="InvalidOperationException">The contract declares no binding.</exception>
    /// <remarks>
    /// READ FROM THE DESCRIPTOR, NEVER WRITTEN AS A LITERAL. The trailer key, the status code and the
    /// payload type are all published BY THE CONTRACT, and the client refuses to decode a trailer whose
    /// binding disagrees with it. A test that hard-coded the key would keep passing after a contract change
    /// that had already broken the product.
    /// </remarks>
    public static RichErrorBinding UpdateConflictBinding()
    {
        RichErrorBinding? binding = UpdateService.Descriptor
            .FindMethodByName(UpdateMethodName)
            ?.GetOptions()
            ?.GetExtension(CommonV1Extensions.RichError);

        return binding
            ?? throw new InvalidOperationException(
                "persistence.v1.UpdateService."
                    + UpdateMethodName
                    + " declares no rich-error binding, so a conflict trailer cannot be located. The "
                    + "contract and this test double have drifted apart.");
    }

    /// <summary>
    /// The gRPC failure a concurrency mismatch produces: <c>Aborted</c> carrying the conflict detail in the
    /// contract's own trailer.
    /// </summary>
    /// <param name="conflict">The conflict detail to carry.</param>
    /// <param name="retCode">
    /// The reconciled outcome code, defaulting to <c>RetCode.E_DB_ERROR</c> which is what the legacy's
    /// defensive override produces when the transaction reports an error
    /// [<c>n_cst_thread_task_sqlupdate.sru:L208-L210</c>].
    /// </param>
    /// <param name="detail">The status detail text.</param>
    /// <returns>The failure.</returns>
    /// <remarks>
    /// <b>ABORTED IS NOT A CHOICE, IT IS THE CANONICAL MAPPING.</b> gRPC <c>Aborted</c> is the status that
    /// projects to HTTP 409, which is what Gateway's REST projection must surface and what AAP 0.6.3.8
    /// requires; and the detail must carry the CURRENT ROW STATE so the caller can implement an explicit
    /// retry-or-surface policy. There is no silent overwrite anywhere in the system, so a double that
    /// produced a bare <c>Aborted</c> with no trailer would be modelling the UNDECODABLE case instead - which
    /// is a real and separate path, reached by passing an empty <see cref="Metadata"/> rather than by
    /// omitting the detail here.
    /// </remarks>
    public static RpcException AbortedConflict(
        ConflictDetail conflict,
        long retCode = RetCode.E_DB_ERROR,
        string detail = "conflict")
    {
        ArgumentNullException.ThrowIfNull(conflict);

        RichErrorTrailer trailer = new()
        {
            Conflict = conflict,
            RetCode = retCode,
        };

        Metadata trailers = [];

        // The key ends in "-bin", which is what makes gRPC treat the value as binary rather than ASCII.
        // Taken from the binding so the suffix comes from the contract rather than from this file.
        trailers.Add(UpdateConflictBinding().TrailerKey, trailer.ToByteArray());

        return new RpcException(new Status(StatusCode.Aborted, detail), trailers);
    }
}

/// <summary>
/// A service-token provider that answers immediately with an OPAQUE PLACEHOLDER credential and records
/// every request.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE CREDENTIAL IS NOT A CREDENTIAL, AND IT IS GENERATED HERE RATHER THAN WRITTEN DOWN.</b> Constraint
/// C-F forbids any key, certificate, password or token appearing in this file, and specifically forbids
/// reusing anything from the two in-source secret sites AAP 0.6.6.1 names -
/// <c>tests/blink/test_jws.htm</c>, which hardcodes a PEM RSA private key and signs a JWS with it, and
/// <c>ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw</c>, which carries a certificate, its matching
/// private key and a live broker credential triple. Nothing from either appears here. The value this
/// provider hands out is composed at construction from
/// <see cref="DeterministicRandomSource"/> behind the fixed prefix
/// <see cref="DeterministicRandomSource.PlaceholderCredentialPrefix"/>, so it is
/// deterministic for a golden-master comparison, unique per instance seed, and structurally incapable of
/// matching a real provider's credential pattern - it is not a JWT, has no dot-separated segments, and
/// begins with a phrase that says what it is.
/// </para>
/// <para>
/// <b>It never disables authentication (C-G).</b> It always issues a credential, so a client under test
/// still attaches an Authorization header and <see cref="ScriptedHttpMessageHandler"/> can prove it did.
/// A suite that needs the UNAUTHENTICATED path sets <see cref="Fault"/>, which models an issuer that
/// refused - the honest way to reach that path, as against a provider that quietly answered with nothing.
/// </para>
/// </remarks>
internal sealed class ScriptedTokenProvider : IServiceTokenProvider
{
    /// <summary>The token type the scheme of the Authorization header will carry.</summary>
    /// <remarks>
    /// <c>Bearer</c>, because that is what the JWT bearer handler on every internal edge expects. It is
    /// settable so a suite can drive a client's handling of an unexpected scheme.
    /// </remarks>
    public const string BearerTokenType = "Bearer";

    private readonly List<ServiceTokenRequest> _requests = [];
    private readonly TimeProvider _clock;

    /// <summary>Creates a provider with a deterministic clock and a deterministic credential.</summary>
    public ScriptedTokenProvider()
        : this(new DeterministicTimeProvider(), new DeterministicRandomSource())
    {
    }

    /// <summary>Creates a provider over an explicit clock and credential source.</summary>
    /// <param name="clock">
    /// The clock the expiry is computed from - pass the SAME instance the client under test reads, or its
    /// expiry arithmetic and this provider's will disagree for reasons that have nothing to do with the
    /// product.
    /// </param>
    /// <param name="random">The source the placeholder credential is composed from.</param>
    public ScriptedTokenProvider(TimeProvider clock, DeterministicRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(random);

        _clock = clock;
        Credential = random.NextPlaceholderCredential();
    }

    /// <summary>The opaque placeholder credential this provider issues.</summary>
    public string Credential { get; }

    /// <summary>The token type to report; defaults to <see cref="BearerTokenType"/>.</summary>
    public string TokenType { get; set; } = BearerTokenType;

    /// <summary>How long after the clock's current instant the issued token expires.</summary>
    /// <remarks>
    /// Five minutes, matching the short-lived posture AAP 0.4.3 gives contract C-01. Settable so a suite can
    /// issue an already-expired token and reach a client's re-acquisition path.
    /// </remarks>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The scopes to report as GRANTED, or <see langword="null"/> to grant exactly what was requested.
    /// </summary>
    /// <remarks>
    /// Granting less than was asked is a real issuer behaviour and a client must cope with it, so it has to
    /// be scriptable. An empty list is distinct from <see langword="null"/>: it grants nothing.
    /// </remarks>
    public IReadOnlyList<string>? GrantedScopes { get; set; }

    /// <summary>An exception to throw instead of issuing, or <see langword="null"/> to issue.</summary>
    /// <remarks>Thrown AFTER the request is recorded, so the attempt is evidence either way.</remarks>
    public Exception? Fault { get; set; }

    /// <summary>Every request, in order.</summary>
    public ImmutableArray<ServiceTokenRequest> Requests => [.. _requests];

    /// <summary>How many times a token was asked for.</summary>
    /// <remarks>
    /// THE COUNT IS AN ASSERTION IN ITS OWN RIGHT. A client that cached its credential asks once for many
    /// calls; one that did not asks every time. Nothing in a signature distinguishes them.
    /// </remarks>
    public int RequestCount => _requests.Count;

    /// <summary>The most recent request, or <see langword="null"/> when none was made.</summary>
    public ServiceTokenRequest? LastRequest => _requests.Count == 0 ? null : _requests[^1];

    /// <inheritdoc/>
    public ValueTask<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _requests.Add(request);

        if (Fault is not null)
        {
            throw Fault;
        }

        // Completed synchronously and with no retry: see the section header. A provider that awaited would
        // let a mis-wired client hang instead of fail.
        return ValueTask.FromResult(new ServiceToken(
            Credential,
            TokenType,
            _clock.GetUtcNow() + Lifetime,
            GrantedScopes ?? request.Scopes));
    }
}

// ==================================================================================================
//  SECTION 5 - THE DETERMINISM SEAMS
//  ------------------------------------------------------------------------------------------------
//  WHY THESE EXIST, STATED PLAINLY (C-K, AAP 0.6.7).
//
//  Parity in this refactor is established by CHARACTERIZATION, also called golden-master testing: the
//  legacy tree is exercised to produce a recording, the .NET tree is exercised to produce a candidate, and
//  the two are compared field by field. The technique has exactly ONE hard prerequisite - REPEATABILITY -
//  and one hard rule about the values that cannot be repeated: a non-deterministic value must be MASKED
//  FROM BOTH the master and the candidate, never from just one, or the comparison silently stops proving
//  anything.
//
//  AAP 0.6.7 enumerates the non-determinism sources across the whole estate. On the DataServices side
//  exactly two are reachable, and both are covered here:
//
//    1. EVERY CLOCK READ, and any ordering that depends on wall time. The service takes its clock as an
//       injected `TimeProvider` everywhere it needs one - the expression session, the macro invoker's
//       timeout, the Security client's expiry arithmetic, and the health and ping endpoints - and
//       `Program.cs` registers `TimeProvider.System` for production. `DeterministicTimeProvider` is the
//       substitution.
//    2. RANDOMNESS, which on this side is delegated rather than local: the crypto surface's GUID,
//       random-string and random-blob generators belong to Security, and DataServices reaches them over
//       contract C-02. So `DeterministicRandomSource` is not a replacement for an in-process generator -
//       there is none - it is how a suite SCRIPTS those answers reproducibly, and how the placeholder
//       credential of `ScriptedTokenProvider` is composed without a literal.
//
//  Everything else on this side is already deterministic: the expression engine, the event chain and the
//  validators are pure functions of their input, which is why the parity matrices for them need no seam at
//  all.
// ==================================================================================================

/// <summary>
/// A clock that does not move unless a test moves it.
/// </summary>
/// <remarks>
/// <para>
/// Substituted wherever the service takes a <see cref="TimeProvider"/>, which is everywhere it reads a
/// clock. A frozen instant makes any recorded timestamp reproducible; <see cref="Step"/> makes a SEQUENCE
/// of reads reproducible too, for the paths that measure an interval rather than stamping an instant.
/// </para>
/// <para>
/// <b>How it differs from <c>MutableClock</c> in SecurityClientTests.</b> That one exposes a settable
/// instant and nothing else, which is all its suite needs. This one additionally overrides the TIMESTAMP
/// pair, so elapsed-time measurement is deterministic as well as instant reading, counts its reads so a
/// test can prove a value came from the clock rather than from a cache, and can advance itself by a fixed
/// step per read.
/// </para>
/// <para>
/// <b>Thread-safe, because a clock is read from wherever the code under test happens to be.</b> The lock is
/// uncontended at test scale and removes an entire class of intermittent failure - which matters
/// disproportionately here, since an intermittently wrong timestamp is indistinguishable from a parity
/// defect.
/// </para>
/// </remarks>
internal sealed class DeterministicTimeProvider : TimeProvider
{
    /// <summary>The instant this clock starts at, unless one is supplied.</summary>
    /// <remarks>
    /// A round UTC instant with no fractional part, chosen so a rendered timestamp is stable and readable
    /// in a recording. Nothing in the product depends on the value; what matters is that it never changes
    /// between a master capture and a candidate capture.
    /// </remarks>
    public static readonly DateTimeOffset DefaultInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private DateTimeOffset _instant;
    private int _reads;

    /// <summary>Creates a clock frozen at <see cref="DefaultInstant"/>.</summary>
    public DeterministicTimeProvider()
        : this(DefaultInstant)
    {
    }

    /// <summary>Creates a clock frozen at an explicit instant.</summary>
    /// <param name="instant">The instant to report.</param>
    public DeterministicTimeProvider(DateTimeOffset instant) => _instant = instant;

    /// <summary>The instant this clock currently reports.</summary>
    /// <remarks>
    /// Reading this property does NOT count as a clock read and does not advance the clock, so a test can
    /// inspect the clock without perturbing what it is measuring.
    /// </remarks>
    public DateTimeOffset Instant
    {
        get
        {
            lock (_gate)
            {
                return _instant;
            }
        }
    }

    /// <summary>
    /// How far the clock advances after each read, defaulting to <see cref="TimeSpan.Zero"/> - frozen.
    /// </summary>
    /// <remarks>
    /// A non-zero step is DETERMINISTIC MOTION, not real motion: the nth read is always the start instant
    /// plus n-1 steps, whatever the machine was doing. That is what lets a duration be asserted exactly
    /// rather than within a tolerance, and a tolerance is precisely what a golden-master comparison cannot
    /// express.
    /// </remarks>
    public TimeSpan Step { get; set; } = TimeSpan.Zero;

    /// <summary>How many times the clock has been read through <see cref="GetUtcNow"/>.</summary>
    public int Reads
    {
        get
        {
            lock (_gate)
            {
                return _reads;
            }
        }
    }

    /// <summary>Always UTC, so no ambient time zone can reach a recorded value.</summary>
    /// <remarks>
    /// A container's time zone is an environment property, and a recording that varied with it would fail a
    /// paired comparison for a reason that has nothing to do with the code.
    /// </remarks>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>One tick per 100 nanoseconds, so a timestamp difference converts to a
    /// <see cref="TimeSpan"/> exactly.</summary>
    /// <remarks>
    /// The system provider's frequency is platform-dependent, so a test asserting an elapsed value would
    /// otherwise carry a rounding error that varies by machine. Fixing the frequency to
    /// <see cref="TimeSpan.TicksPerSecond"/> removes it.
    /// </remarks>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            DateTimeOffset current = _instant;
            _reads++;

            if (Step != TimeSpan.Zero)
            {
                _instant += Step;
            }

            return current;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from the same instant as <see cref="GetUtcNow"/> so the two cannot disagree, and DELIBERATELY
    /// NOT counted as a read and not subject to <see cref="Step"/>: a timestamp is taken in pairs to measure
    /// an interval, and advancing on each would make every measured interval non-zero whether the code took
    /// any time or not.
    /// </remarks>
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _instant.UtcTicks;
        }
    }

    /// <summary>Moves the clock forward, or backward for a negative value.</summary>
    /// <param name="by">How far to move.</param>
    /// <remarks>
    /// Backward motion is permitted on purpose. A clock that only went forward could not reproduce the
    /// behaviour of a host whose clock was corrected, and a cache keyed on an expiry has to be shown to
    /// cope with it.
    /// </remarks>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _instant += by;
        }
    }

    /// <summary>Returns the clock to an explicit instant and clears the read count.</summary>
    /// <param name="instant">The instant to report next.</param>
    public void Reset(DateTimeOffset instant)
    {
        lock (_gate)
        {
            _instant = instant;
            _reads = 0;
        }
    }
}

/// <summary>
/// A pseudo-random source whose algorithm is written out in full, so its sequence is reproducible across
/// runtimes, platforms and framework versions.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY NOT <c>Random</c> WITH A SEED.</b> A seeded <see cref="Random"/> is reproducible within one
/// framework version and is documented as NOT being stable across versions - the implementation is free to
/// change. A golden master captured on one toolchain and compared on another would then differ for a reason
/// that has nothing to do with the code under test, which is the exact failure mode
/// characterization testing exists to avoid. The generator below is a plain xorshift64*: eight lines, no
/// dependencies, and identical output forever.
/// </para>
/// <para>
/// <b>What it is FOR.</b> DataServices holds no in-process generator - the crypto surface's GUID,
/// random-string and random-blob operations belong to Security and are reached over contract C-02 - so this
/// type's job is to make a SCRIPTED answer for those operations reproducible, and to compose the
/// placeholder credential of <see cref="ScriptedTokenProvider"/> without writing a credential-shaped
/// literal into this file (C-F).
/// </para>
/// <para>
/// <b>It is not a security primitive and must never be used as one.</b> xorshift64* is a statistical
/// generator, not a cryptographic one. Nothing here produces key material, and nothing here may be
/// substituted for <c>System.Security.Cryptography</c>. That is stated because the type sits next to a
/// credential and the temptation is real.
/// </para>
/// </remarks>
internal sealed class DeterministicRandomSource
{
    /// <summary>
    /// The prefix every placeholder credential carries, so a reader and a scanner both see at once that it
    /// is not real.
    /// </summary>
    /// <remarks>
    /// Chosen to be STRUCTURALLY incapable of matching a provider's credential pattern: it is not a JWT and
    /// has no dot-separated segments, it does not begin with any of the vendor prefixes the secrets sweep
    /// looks for (AAP 0.6.6.1 lists them), and it says in words what it is.
    /// </remarks>
    public const string PlaceholderCredentialPrefix = "placeholder-not-a-credential-";

    /// <summary>The seed used when none is supplied.</summary>
    /// <remarks>
    /// Any non-zero value serves; xorshift requires a non-zero state and produces an all-zero sequence from
    /// an all-zero one, which is why <see cref="DeterministicRandomSource(ulong)"/> rejects zero rather than
    /// silently degenerating.
    /// </remarks>
    public const ulong DefaultSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>The alphabet a placeholder credential's random tail is drawn from.</summary>
    /// <remarks>
    /// Lower-case hexadecimal, so the tail is URL-safe, header-safe and unambiguous when read aloud in a
    /// failure message.
    /// </remarks>
    private const string HexAlphabet = "0123456789abcdef";

    private readonly object _gate = new();
    private ulong _state;

    /// <summary>Creates a source over <see cref="DefaultSeed"/>.</summary>
    public DeterministicRandomSource()
        : this(DefaultSeed)
    {
    }

    /// <summary>Creates a source over an explicit seed.</summary>
    /// <param name="seed">The seed; must not be zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">The seed is zero.</exception>
    public DeterministicRandomSource(ulong seed)
    {
        ArgumentOutOfRangeException.ThrowIfZero(seed);

        Seed = seed;
        _state = seed;
    }

    /// <summary>The seed this source was created with.</summary>
    public ulong Seed { get; }

    /// <summary>How many 64-bit values have been drawn.</summary>
    /// <remarks>
    /// Exposed so a test can assert that a code path consumed randomness exactly once - the difference
    /// between a value that was generated and one that was cached.
    /// </remarks>
    public long Draws { get; private set; }

    /// <summary>Draws the next 64-bit value.</summary>
    /// <returns>The value.</returns>
    /// <remarks>
    /// xorshift64*: three shift-xor steps on the state, then one multiply on the way out. Written out rather
    /// than delegated precisely so the sequence is pinned by this file and cannot move under it.
    /// </remarks>
    public ulong NextUInt64()
    {
        lock (_gate)
        {
            ulong state = _state;

            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;

            _state = state;
            Draws++;

            return unchecked(state * 0x2545F4914F6CDD1DUL);
        }
    }

    /// <summary>Draws the next value in <c>[0, exclusiveUpperBound)</c>.</summary>
    /// <param name="exclusiveUpperBound">The bound; must be positive.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not positive.</exception>
    /// <remarks>
    /// A plain remainder, whose modulo bias is irrelevant here and would be a real defect in a security
    /// context - see the warning on the type. Reproducibility is the only property that matters.
    /// </remarks>
    public int Next(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        return (int)(NextUInt64() % (ulong)exclusiveUpperBound);
    }

    /// <summary>Draws a byte array.</summary>
    /// <param name="count">How many bytes; may be zero.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
    /// <remarks>
    /// The seam for scripting a random-blob answer from contract C-02 reproducibly. One draw yields eight
    /// bytes, consumed most-significant first so the byte order is fixed by this file rather than by the
    /// host's endianness - a recording captured on one architecture must compare on another.
    /// </remarks>
    public byte[] NextBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        byte[] bytes = new byte[count];

        for (int index = 0; index < count; index += sizeof(ulong))
        {
            ulong value = NextUInt64();
            int remaining = Math.Min(sizeof(ulong), count - index);

            for (int offset = 0; offset < remaining; offset++)
            {
                bytes[index + offset] = (byte)(value >> (8 * (sizeof(ulong) - 1 - offset)));
            }
        }

        return bytes;
    }

    /// <summary>Draws a lower-case hexadecimal string.</summary>
    /// <param name="length">How many characters; may be zero.</param>
    /// <returns>The string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
    public string NextHex(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        StringBuilder text = new(length);

        for (int index = 0; index < length; index++)
        {
            text.Append(HexAlphabet[Next(HexAlphabet.Length)]);
        }

        return text.ToString();
    }

    /// <summary>
    /// Composes an opaque placeholder credential: the fixed prefix and a 32-character hexadecimal tail.
    /// </summary>
    /// <returns>The placeholder.</returns>
    /// <remarks>
    /// GENERATED, NOT WRITTEN DOWN. This is how constraint C-F's "generate it locally at test time" clause
    /// is discharged: the value exists only in memory for the life of the test, is deterministic for a given
    /// seed so a recording is reproducible, and is not a credential of any scheme. Nothing in this file, and
    /// nothing committed to the repository, contains it.
    /// </remarks>
    public string NextPlaceholderCredential() => PlaceholderCredentialPrefix + NextHex(32);

    /// <summary>Draws a deterministic identifier in the version-4 shape.</summary>
    /// <returns>The identifier.</returns>
    /// <remarks>
    /// The version and variant bits are set so the value is a WELL-FORMED version-4 identifier and not
    /// merely sixteen random bytes - a consumer that validates the shape must not reject it. It is
    /// nonetheless NOT random: it is the seam for scripting the crypto surface's GUID generator, which
    /// AAP 0.6.7 names as a non-determinism source that has to be masked from both sides of a paired
    /// recording.
    /// </remarks>
    public Guid NextGuid()
    {
        byte[] bytes = NextBytes(16);

        // Version 4 in the high nibble of byte 7, and the RFC variant in the two high bits of byte 8.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }

    /// <summary>Returns the source to its seed, so a scenario can be replayed.</summary>
    /// <remarks>
    /// The draw count is cleared with the state, because a draw count is a position in THIS sequence and a
    /// reset sequence has no history.
    /// </remarks>
    public void Reset()
    {
        lock (_gate)
        {
            _state = Seed;
            Draws = 0;
        }
    }
}

// ==================================================================================================
//  THE SANITY SUITE
//  ------------------------------------------------------------------------------------------------
//  WHY IT LIVES IN THIS FILE. A double that is never constructed is untested code shipped inside a test
//  assembly: it counts against the per-service line-coverage gate exactly like production code (C-H), and
//  a double whose own contract is unverified is worse than no double at all, because every suite that
//  later leans on it inherits the defect silently. So each double is exercised here, in the same file it
//  is declared in - which keeps the whole of this unit reviewable in one place, touches none of the
//  eighteen sibling test files, and creates no additional file (AAP 0.3.1 lists the files this refactor
//  creates, and a separate doubles-test file is not among them).
//
//  WHAT IT ASSERTS. Only the properties the doubles PROMISE - arrival order, the decomposed topic
//  identity, the tri-valued veto, the localization return alphabet, the one-based argument list, the
//  "(null)" sentinel selection, route matching with credential-presence-only recording, the three scripted
//  Persistence shapes, and reproducibility of the two determinism seams. It does NOT re-assert product
//  behaviour: that is the business of the suites these doubles serve.
// ==================================================================================================

/// <summary>
/// The self-verification suite for the doubles declared in this file.
/// </summary>
public sealed class TestDoublesSanityTests
{
    /// <summary>
    /// The log preserves arrival order and decomposes the fused topic string into all three wire fields.
    /// </summary>
    /// <remarks>
    /// The two prefixed topics are appended in the order the chain reaches them, which is the REVERSE of
    /// their alphabetical order once the prefix is stripped. Asserting both projections in one test is what
    /// makes the trap explicit: the legacy names keep the chain's order, the logical names do not.
    /// </remarks>
    [Fact]
    public void ScriptedDispatchLog_PreservesArrivalOrderAndDecomposesTheFusedTopic()
    {
        ScriptedDispatchLog log = new();

        _ = log.Append(ScriptedDispatchStage.Handler, DataWindowEventChain.EVT_EDITCHANGED);
        _ = log.Append(ScriptedDispatchStage.Handler, DataWindowEventChain.EVT_ITEMCHANGED);
        _ = log.Append(ScriptedDispatchStage.Triggered, DataWindowEventChain.EVT_CLICKED);

        // ARRIVAL ORDER, NOT SORTED ORDER. "1-editchanged" was dispatched first and is recorded first.
        Assert.Equal(
            [
                DataWindowEventChain.EVT_EDITCHANGED,
                DataWindowEventChain.EVT_ITEMCHANGED,
                DataWindowEventChain.EVT_CLICKED
            ],
            log.LegacyNames());

        // The same three dispatches by logical name - and note the first two now read in the opposite
        // alphabetical relation to their legacy spellings. This is the projection an ordering assertion
        // must NOT use.
        Assert.Equal(["editchanged", "itemchanged", "clicked"], log.LogicalNames());

        // Wire field one: the ordering prefix, present on exactly two of the twelve topics
        // [se_cst_dw.sru:L54, :L57].
        Assert.Equal<int?[]>([1, 0, null], [.. log.Sequences()]);

        // Wire field three: no topic here carries a namespace, so every lifetime is transient - the state
        // swept by the ".^persistent" bulk unsubscribe.
        Assert.All(log.Lifetimes(), lifetime => Assert.Equal(SubscriptionLifetime.Transient, lifetime));

        Assert.Equal(3, log.Count);
        Assert.Equal([1, 2, 3], log.Records.Select(record => record.Ordinal));
        Assert.Equal(2, log.OfStage(ScriptedDispatchStage.Handler).Length);
        Assert.Single(log.For(DataWindowEventChain.EVT_ITEMCHANGED));
        Assert.Contains(DataWindowEventChain.EVT_ITEMCHANGED, log.Describe(), StringComparison.Ordinal);

        log.Clear();

        Assert.Equal(0, log.Count);
        Assert.Equal("(no dispatches recorded)", log.Describe());
    }

    /// <summary>
    /// A record whose name is not one of the twelve still records, falling back to the raw name.
    /// </summary>
    [Fact]
    public void ScriptedDispatch_FallsBackToTheRawNameWhenNoTopicParses()
    {
        ScriptedDispatchLog log = new();

        // The empty string is rejected by the subscription grammar [n_cst_eventful.sru:L381], so no topic
        // is produced - and the record must still exist, because the evidence that a dispatch happened is
        // more valuable than the decomposition of its name.
        ScriptedDispatch record = log.Append(ScriptedDispatchStage.Handler, string.Empty);

        Assert.Null(record.Topic);
        Assert.Equal(string.Empty, record.LegacyName);
        Assert.Equal(string.Empty, record.LogicalName);
        Assert.Null(record.Sequence);
        Assert.Null(record.Lifetime);
    }

    /// <summary>
    /// The broker records the dispatch, samples the subscriber pre-check, and shows the handler's
    /// arguments.
    /// </summary>
    /// <remarks>
    /// The pre-check sample is what makes the oracle's two <c>of_IsSubscribed</c> sites assertable
    /// [se_cst_dw.sru:L166, :L316]: the hook fires only when a subscription matched, so a triggering record
    /// with <c>HadSubscriber</c> true is direct evidence the dispatch reached a handler.
    /// </remarks>
    [Fact]
    public void ScriptedEventBroker_RecordsTheDispatchTheArgumentsAndThePreCheck()
    {
        ScriptedDispatchLog log = new();
        ScriptedEventBroker broker = new(log);
        ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_ITEMCHANGED, log, "subA");

        Assert.Equal(RetCode.OK, subscriber.Subscribe(broker, arity: 2));
        Assert.True(broker.IsSubscribed(DataWindowEventChain.EVT_ITEMCHANGED));

        _ = broker.Trigger(DataWindowEventChain.EVT_ITEMCHANGED, 7L, "dwo-stand-in");

        Assert.Equal(1, broker.TriggeringCount);
        Assert.Equal(1, broker.PrepareCount);
        Assert.Equal(1, broker.TriggeredCount);
        Assert.Equal(0, broker.ExceptionCount);

        ScriptedDispatch triggering = Assert.Single(log.OfStage(ScriptedDispatchStage.Triggering));
        Assert.True(triggering.HadSubscriber);
        Assert.False(triggering.IsPost);
        Assert.Equal("broker", triggering.Label);

        ScriptedDispatch handler = Assert.Single(log.OfStage(ScriptedDispatchStage.Handler));
        Assert.Equal("subA", handler.Label);
        Assert.Equal([7L, "dwo-stand-in"], handler.Arguments);
        Assert.Equal(RetCode.OK, handler.ReturnValue);
        Assert.Equal(VetoResult.Continue, handler.Veto);
        Assert.Null(handler.VetoOutcome);
        Assert.Equal(1, subscriber.Dispatches);
        Assert.Equal([7L, "dwo-stand-in"], subscriber.LastArguments);

        // The hooks bracket the handler, which is the ordering a reader of the log relies on.
        Assert.Equal(
            [
                ScriptedDispatchStage.Triggering,
                ScriptedDispatchStage.Prepare,
                ScriptedDispatchStage.Handler,
                ScriptedDispatchStage.Triggered
            ],
            log.Records.Select(record => record.Stage));
    }

    /// <summary>
    /// A topic with no subscriber never reaches the hooks, so the pre-check answers false.
    /// </summary>
    /// <remarks>
    /// This is the "no subscriber" report the oracle's two pre-check sites depend on: with nothing
    /// subscribed the triggering hook does not fire at all [n_cst_eventful.sru:L839-L843], so the log stays
    /// empty and <see cref="EventBroker.IsSubscribed(string)"/> is the only witness.
    /// </remarks>
    [Fact]
    public void ScriptedEventBroker_ReportsNoSubscriberForAnUnsubscribedTopic()
    {
        ScriptedEventBroker broker = new();
        ScriptedTopicSubscriber subscriber =
            new(DataWindowEventChain.EVT_EDITCHANGED, broker.Log, "subB");

        Assert.False(broker.IsSubscribed(DataWindowEventChain.EVT_EDITCHANGED));

        _ = subscriber.Subscribe(broker, arity: 0);
        Assert.True(broker.IsSubscribed(DataWindowEventChain.EVT_EDITCHANGED));

        Assert.Equal(RetCode.OK, subscriber.Unsubscribe(broker));
        Assert.False(broker.IsSubscribed(DataWindowEventChain.EVT_EDITCHANGED));

        _ = broker.Trigger(DataWindowEventChain.EVT_EDITCHANGED);

        Assert.Equal(0, broker.TriggeringCount);
        Assert.Equal(0, broker.Log.Count);
        Assert.Equal(0, subscriber.Dispatches);
    }

    /// <summary>
    /// The prepare hook can inject slot one and declare it consumed, and the triggering hook can abort a
    /// whole dispatch.
    /// </summary>
    [Fact]
    public void ScriptedEventBroker_ScriptsTheInjectedSlotAndTheTriggeringPrevention()
    {
        ScriptedDispatchLog log = new();
        ScriptedEventBroker broker = new(log);
        ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_CLICKED, log);

        broker.PrepareInjections[DataWindowEventChain.EVT_CLICKED] = "injected-parent";
        broker.PrepareConsumedSlots[DataWindowEventChain.EVT_CLICKED] = 1;
        _ = subscriber.Subscribe(broker, arity: 3);

        _ = broker.Trigger(DataWindowEventChain.EVT_CLICKED, 11L, 22L);

        // Slot one carries the injection and the payload starts at slot two, which is the shape
        // se_cst_dw.sru:L604-L605 produces.
        Assert.Equal(["injected-parent", 11L, 22L], subscriber.LastArguments);

        log.Clear();
        subscriber.Answer = RetCode.OK;

        // RetCode.PREVENT from the triggering hook aborts BEFORE the first handler runs, so the handler is
        // never reached and no Triggered record is produced either.
        broker.TriggeringResults[DataWindowEventChain.EVT_CLICKED] = RetCode.PREVENT;

        _ = broker.Trigger(DataWindowEventChain.EVT_CLICKED, 33L);

        Assert.Empty(log.OfStage(ScriptedDispatchStage.Handler));
        Assert.Single(log.OfStage(ScriptedDispatchStage.Triggering));
        Assert.Equal(1, subscriber.Dispatches);
    }

    /// <summary>
    /// A handler fault reaches the exception hook, and the hook's own alphabet decides what happens next.
    /// </summary>
    /// <remarks>
    /// <see cref="EventBroker.ExceptionResultContinue"/> is 2 and means CONTINUE - the near-opposite of
    /// <see cref="VetoResult.PreventDeep"/>, which is also 2. Driving it here is what proves this file
    /// keeps the two alphabets apart rather than merely claiming to.
    /// </remarks>
    [Fact]
    public void ScriptedEventBroker_RecordsAHandlerFaultThroughTheExceptionHook()
    {
        ScriptedDispatchLog log = new();
        ScriptedEventBroker broker = new(log);
        ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_RBUTTONUP, log)
        {
            Fault = new InvalidOperationException("scripted handler fault"),
        };

        _ = subscriber.Subscribe(broker, arity: 0);
        broker.ExceptionResults[DataWindowEventChain.EVT_RBUTTONUP] = EventBroker.ExceptionResultContinue;

        _ = broker.Trigger(DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(1, broker.ExceptionCount);

        ScriptedDispatch faulted = Assert.Single(log.OfStage(ScriptedDispatchStage.Exception));
        Assert.IsType<InvalidOperationException>(faulted.Fault);
        Assert.Equal(EventBroker.ExceptionResultContinue, faulted.ReturnValue);

        // The handler record was appended BEFORE the throw, so the attempt is evidence either way.
        ScriptedDispatch handler = Assert.Single(log.OfStage(ScriptedDispatchStage.Handler));
        Assert.NotNull(handler.Fault);
    }

    /// <summary>
    /// A subscriber raises a tri-valued veto through the broker, and reports what the broker answered.
    /// </summary>
    /// <remarks>
    /// Both preventions abort the level that raised them, so this asserts the OUTCOME CODE rather than the
    /// abort - the once-versus-deep difference is in what survives an unwind [n_cst_eventful.sru:L954-L957]
    /// and belongs to the broker's own suite. What matters here is that the double carries the tri-valued
    /// value through unflattened and reports <see cref="RetCode.FAILED"/> when there was no dispatch to
    /// prevent [:L1293].
    /// </remarks>
    [Fact]
    public void ScriptedTopicSubscriber_RaisesTheTriValuedVetoThroughTheBroker()
    {
        ScriptedDispatchLog log = new();
        ScriptedEventBroker broker = new(log);
        ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_LBUTTONUP, log)
        {
            Veto = VetoResult.PreventDeep,
        };

        _ = subscriber.Subscribe(broker, arity: 0);
        _ = broker.Trigger(DataWindowEventChain.EVT_LBUTTONUP);

        ScriptedDispatch handler = Assert.Single(log.OfStage(ScriptedDispatchStage.Handler));
        Assert.Equal(VetoResult.PreventDeep, handler.Veto);
        Assert.Equal(RetCode.OK, handler.VetoOutcome);

        // Invoked DIRECTLY, outside any dispatch: EventBroker.Current is null, so no veto is attempted and
        // the outcome is recorded as absent rather than as a success.
        subscriber.Veto = VetoResult.PreventOnce;
        _ = subscriber.OnDispatch0();

        Assert.Null(subscriber.LastVetoOutcome);
    }

    /// <summary>
    /// One handler name exists per arity, each resolves, and the range is enforced.
    /// </summary>
    /// <remarks>
    /// Six arities are declared because the broker chooses the FEWEST-parameter candidate when no explicit
    /// signature is supplied, so an overloaded name could never carry arguments. Subscribing every arity in
    /// turn is the only way to prove all six names resolve.
    /// </remarks>
    [Fact]
    public void ScriptedTopicSubscriber_DeclaresOneResolvableHandlerPerArity()
    {
        for (int arity = 0; arity <= ScriptedTopicSubscriber.MaximumArity; arity++)
        {
            ScriptedEventBroker broker = new();
            ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_GETFOCUS, broker.Log);

            Assert.Equal(RetCode.OK, subscriber.Subscribe(broker, arity));

            _ = broker.Trigger(DataWindowEventChain.EVT_GETFOCUS, 1L, 2L, 3L, 4L, 5L);

            Assert.Equal(1, subscriber.Dispatches);
            Assert.Equal(arity, subscriber.LastArguments.Length);
        }

        Assert.Equal("OnDispatch4", ScriptedTopicSubscriber.HandlerNameFor(4));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ScriptedTopicSubscriber.HandlerNameFor(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ScriptedTopicSubscriber.HandlerNameFor(ScriptedTopicSubscriber.MaximumArity + 1));
    }

    /// <summary>
    /// The side-effect hook runs inside the handler and sees the record that was just appended.
    /// </summary>
    [Fact]
    public void ScriptedTopicSubscriber_RunsItsSideEffectInsideTheHandler()
    {
        ScriptedEventBroker broker = new();
        ScriptedTopicSubscriber subscriber = new(DataWindowEventChain.EVT_LOSEFOCUS, broker.Log)
        {
            Answer = RetCode.PREVENT,
        };

        int observedCount = -1;
        subscriber.InHandler = record => observedCount = record.Ordinal;

        _ = subscriber.Subscribe(broker, arity: 0);
        _ = broker.Trigger(DataWindowEventChain.EVT_LOSEFOCUS);

        ScriptedDispatch handler =
            Assert.Single(broker.Log.OfStage(ScriptedDispatchStage.Handler));
        Assert.Equal(handler.Ordinal, observedCount);
        Assert.Equal(RetCode.PREVENT, handler.ReturnValue);
    }

    /// <summary>
    /// The scripted provider translates only for its own source and category, and obeys the return
    /// alphabet.
    /// </summary>
    /// <remarks>
    /// Both gates are asserted with a value the oracle itself distinguishes:
    /// <c>Enums.I18N_SRC_CUSTOM</c> for the wrong source and <c>Categories.CAT_MSGBOX</c> for the wrong
    /// category - the sibling category from the same declaration [ne_cst_i18n.sru:L16-L17], which is the
    /// value a caller is most likely to pass by mistake.
    /// </remarks>
    [Fact]
    public void ScriptedI18nProvider_TranslatesOnlyForItsSourceAndCategory()
    {
        ScriptedI18nProvider provider = new ScriptedI18nProvider()
            .Teach(LegacyMessageKeys.ValidationErrorTitle, "Error");

        Assert.Equal(Enums.I18N_SRC_PFW, provider.Source);

        // The category is read from Categories and never written as a number - it is an OFFSET from
        // Enums.I18N_CAT_CUSTOM, and the arithmetic is asserted here rather than the resolved value.
        Assert.Equal(Categories.CAT_DWSVC, provider.Category);
        Assert.Equal(Enums.I18N_CAT_CUSTOM + 2, provider.Category);

        string? text = LegacyMessageKeys.ValidationErrorTitle;
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
        Assert.Equal("Error", text);

        // WRONG CATEGORY: not handled, and the text is left exactly as it arrived.
        string? untouchedByCategory = LegacyMessageKeys.ValidationErrorTitle;
        Assert.Equal(
            0L,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref untouchedByCategory));
        Assert.Equal(LegacyMessageKeys.ValidationErrorTitle, untouchedByCategory);

        // WRONG SOURCE: likewise.
        string? untouchedBySource = LegacyMessageKeys.ValidationErrorTitle;
        Assert.Equal(
            0L,
            provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, ref untouchedBySource));
        Assert.Equal(LegacyMessageKeys.ValidationErrorTitle, untouchedBySource);

        // A KEY THAT WAS NEVER TAUGHT: not handled, text untouched.
        string? unknown = "never-taught";
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref unknown));
        Assert.Equal("never-taught", unknown);

        // Every attempt was recorded, keys as received.
        Assert.Equal(4, provider.Requests.Length);
        Assert.Equal(
            [
                LegacyMessageKeys.ValidationErrorTitle,
                LegacyMessageKeys.ValidationErrorTitle,
                LegacyMessageKeys.ValidationErrorTitle,
                "never-taught"
            ],
            provider.RequestedKeys);
    }

    /// <summary>
    /// A null key answers "not handled" without throwing, and a taught null translation is delivered.
    /// </summary>
    /// <remarks>
    /// The nullable annotation on the contract is deliberate - PowerScript strings can be null and
    /// AAP 0.4.5.4 forbids collapsing that null - so both directions have to work: a null going IN must not
    /// throw, and a null coming OUT must be deliverable.
    /// </remarks>
    [Fact]
    public void ScriptedI18nProvider_HandlesNullInBothDirections()
    {
        ScriptedI18nProvider provider = new ScriptedI18nProvider()
            .Teach(LegacyMessageKeys.RowSelectRejectionKey, null);

        string? nothing = null;
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref nothing));
        Assert.Null(nothing);

        string? taughtNull = LegacyMessageKeys.RowSelectRejectionKey;
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref taughtNull));
        Assert.Null(taughtNull);
    }

    /// <summary>
    /// The passthrough provider leaves the text untouched, and the facade returns it unchanged either way.
    /// </summary>
    /// <remarks>
    /// THE SILENT PASSTHROUGH HAS THREE PARTS AND ALL THREE ARE PINNED HERE: the text comes back unchanged,
    /// nothing is thrown, and nothing marks it untranslated [i18n.srf:L17-L22]. The no-provider arrangement
    /// and the zero-answering-provider arrangement must be INDISTINGUISHABLE at the facade, which is why
    /// both are asserted against the same expected value.
    /// </remarks>
    [Fact]
    public void PassthroughI18nProvider_IsIndistinguishableFromNoProviderAtAll()
    {
        I18n bare = ScriptedLocalization.WithoutProvider();
        PassthroughI18nProvider passthrough = new();
        I18n installed = ScriptedLocalization.With(passthrough);

        foreach (string key in LegacyMessageKeys.All)
        {
            Assert.Equal(key, bare.I18N(Categories.CAT_DWSVC, key));
            Assert.Equal(key, installed.I18N(Categories.CAT_DWSVC, key));
        }

        // The installed provider WAS consulted - the difference between a passthrough that went through it
        // and one that short-circuited before reaching it.
        Assert.Equal(LegacyMessageKeys.All.Length, passthrough.Requests.Length);
        Assert.All(
            passthrough.Requests,
            request => Assert.Equal(Categories.CAT_DWSVC, request.Category));
    }

    /// <summary>
    /// The marker factory teaches every key, and the facade delivers the marker.
    /// </summary>
    [Fact]
    public void ScriptedLocalization_WithMarkersTranslatesEveryLegacyKey()
    {
        (I18n facade, ScriptedI18nProvider provider) = ScriptedLocalization.WithMarkers();

        foreach (string key in LegacyMessageKeys.All)
        {
            Assert.Equal(LegacyMessageKeys.MarkerFor(key), facade.I18N(Categories.CAT_DWSVC, key));
        }

        Assert.Equal(LegacyMessageKeys.All.Length, provider.Requests.Length);

        // The placeholder survives the marker, which is required: the row number is substituted into the
        // TRANSLATION, so a marker that dropped "{}" would make that substitution untestable.
        Assert.Contains(
            "{}",
            LegacyMessageKeys.MarkerFor(LegacyMessageKeys.RowSelectRowNumberKey),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The legacy keys carry the oracle's spellings, and the two composition rules reproduce its messages.
    /// </summary>
    /// <remarks>
    /// The spellings are asserted as LITERALS here on purpose - it is the one place in this file where the
    /// characters appear twice - because a constant that merely forwards another constant proves nothing
    /// about what either holds. The literals are transcribed from <c>se_cst_dw.sru:L355</c>, <c>:L357</c>
    /// and <c>n_cst_dwsvc_rowselect.sru:L239</c>.
    /// </remarks>
    [Fact]
    public void LegacyMessageKeys_CarryTheOracleSpellingsAndCompositionRules()
    {
        Assert.Equal("输入了无效的值", LegacyMessageKeys.ValidationErrorFallbackText);
        Assert.Equal("错误", LegacyMessageKeys.ValidationErrorTitle);
        Assert.Equal("第{}行", LegacyMessageKeys.RowSelectRowNumberKey);
        Assert.Equal("修改数据被拒绝", LegacyMessageKeys.RowSelectRejectionKey);
        Assert.Equal("!", LegacyMessageKeys.ValidationErrorFallbackSuffix);
        Assert.Equal("!", LegacyMessageKeys.RowSelectRejectionSuffix);
        Assert.Equal("\n", LegacyMessageKeys.RowSelectLineSeparator);
        Assert.Equal("?", LegacyMessageKeys.NoValidationMessagePlaceholder);

        // THE SAME CHARACTERS, A DIFFERENT MECHANISM, AND THAT COINCIDENCE IS THE TRAP. The engine's title
        // is a hardcoded literal that never routes through I18N [n_cst_dwsvc_columnexp.sru:L739, :L762],
        // whereas se_cst_dw.sru:L357 translates the identical text. Equal text, two declarations, and
        // deliberately not one constant - so an engine message asserted through a provider would be
        // asserting behaviour the engine has not got.
        Assert.Equal(LegacyMessageKeys.ValidationErrorTitle, LegacyMessageKeys.ExpressionErrorDialogTitle);

        // Four genuine keys. The validation title IS one; the sentinel is not, and neither is anything the
        // engine hardcodes - the roster is by MECHANISM, not by text.
        Assert.Equal(4, LegacyMessageKeys.All.Length);
        Assert.Contains(LegacyMessageKeys.ValidationErrorTitle, LegacyMessageKeys.All);
        Assert.DoesNotContain(LegacyMessageKeys.NoValidationMessagePlaceholder, LegacyMessageKeys.All);
        Assert.DoesNotContain(LegacyMessageKeys.ValidationErrorFallbackSuffix, LegacyMessageKeys.All);

        // se_cst_dw.sru:L355 - translate, THEN append. The suffix is never part of the lookup.
        Assert.Equal("Invalid input value!", LegacyMessageKeys.ComposeValidationErrorFallback("Invalid input value"));

        // n_cst_dwsvc_rowselect.sru:L239, all four operands in the oracle's order, with the untranslated
        // Chinese standing in for what a null provider would return.
        Assert.Equal(
            "第3行\n修改数据被拒绝!",
            LegacyMessageKeys.ComposeRowSelectRejection(
                3L,
                LegacyMessageKeys.RowSelectRowNumberKey,
                LegacyMessageKeys.RowSelectRejectionKey));

        // A null translation concatenates as the empty string, which is how PowerScript would treat it.
        Assert.Equal("\n!", LegacyMessageKeys.ComposeRowSelectRejection(9L, null, null));
    }

    /// <summary>
    /// The documented <c>FormatPrice</c> script reproduces <c>Round(Double(args[1]),Long(args[2]))</c>,
    /// including PowerScript's tolerance of a non-numeric argument.
    /// </summary>
    /// <remarks>
    /// The one-based indices are the point: <c>args[1]</c> is the amount and <c>args[2]</c> the precision,
    /// exactly as both the specification's 宏函数 section and the live handler at
    /// <c>w_test_dwsvc_columnexp.srw:L306-L308</c> read them.
    /// </remarks>
    [Theory]
    [InlineData("12.345", "1", 12.3d)]
    [InlineData("12.35", "1", 12.4d)]
    [InlineData("-12.35", "1", -12.4d)]
    [InlineData("12.345", "0", 12d)]
    [InlineData("1250", "-2", 1300d)]
    [InlineData("not-a-number", "1", 0d)]
    [InlineData("12.345", "not-a-number", 12d)]
    public void MacroScripts_FormatPriceReproducesTheDocumentedHandler(
        string amount,
        string precision,
        double expected)
    {
        FakeDataWindowObject column = new("n1", FakeColumnType.DecimalOf(2));
        MacroArgumentList args = MacroArgumentList.FromEvaluated(amount, precision);

        object? result = MacroScripts.FormatPrice(1L, column, MacroScripts.FormatPriceName, args);

        Assert.Equal(expected, Assert.IsType<double>(result), precision: 10);
    }

    /// <summary>
    /// The argument list the scripts receive is ONE-BASED, and index zero is refused.
    /// </summary>
    /// <remarks>
    /// AAP 0.4.5.4 names one-based-to-zero-based translation the single most dangerous mechanical hazard in
    /// this refactor, so the double asserts the hazard is closed rather than trusting it.
    /// </remarks>
    [Fact]
    public void MacroScripts_ArgumentListIsOneBased()
    {
        FakeDataWindowObject column = new("n1", FakeColumnType.DecimalOf(2));
        MacroArgumentList args = MacroArgumentList.FromEvaluated("first", "second");

        Assert.Equal("first,second", MacroScripts.EchoArguments(1L, column, "Echo", args));
        Assert.Equal("first", args[1]);
        Assert.Equal(2, args.UpperBound);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => args[0]);

        // The invalid-return script answers a type no rendering arm accepts, which is what makes the
        // engine's 返回值无效 refusal reachable [n_cst_dwsvc_columnexp.sru:L2282, :L2306].
        object? invalid = MacroScripts.InvalidReturnValue(1L, column, "Whatever", args);
        Assert.NotNull(invalid);
        Assert.Equal(typeof(object), invalid.GetType());
    }

    /// <summary>
    /// The channel answers from scripts, echoes the correlation identity, and reports "not mine" for an
    /// unscripted name.
    /// </summary>
    [Fact]
    public async Task ScriptedMacroChannel_AnswersScriptsAndReportsUnhandled()
    {
        FakeDataWindowObject column = new("n1", FakeColumnType.DecimalOf(2));
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .ScriptFormatPrice()
            .ScriptValue("Constant", 42L)
            .ScriptInvalidReturnValue("Broken")
            .ScriptUnhandled("Missing");

        MacroInvocationResponse formatted = await channel.InvokeAsync(
            Invocation(column, MacroScripts.FormatPriceName, ExpansionMode.MacroDirect, "12.345", "1"),
            TestContext.Current.CancellationToken);

        Assert.Equal("inv-1", formatted.InvocationId);
        Assert.Equal(1L, formatted.Sequence);
        Assert.False(formatted.Unhandled);
        Assert.Equal(12.3d, Assert.IsType<double>(formatted.Value), precision: 10);

        MacroInvocationResponse constant = await channel.InvokeAsync(
            Invocation(column, "Constant", ExpansionMode.MacroDynamic),
            TestContext.Current.CancellationToken);
        Assert.Equal(42L, constant.Value);

        MacroInvocationResponse broken = await channel.InvokeAsync(
            Invocation(column, "Broken", ExpansionMode.MacroDirect),
            TestContext.Current.CancellationToken);
        Assert.NotNull(broken.Value);
        Assert.False(broken.Unhandled);

        // An unscripted name and an explicitly unhandled name answer the same way, because the contract has
        // one way of saying "not mine" and the engine maps it onto the legacy fall-through outcome.
        foreach (string name in new[] { "Missing", "NeverScripted" })
        {
            MacroInvocationResponse unhandled = await channel.InvokeAsync(
                Invocation(column, name, ExpansionMode.MacroDirect),
                TestContext.Current.CancellationToken);

            Assert.True(unhandled.Unhandled);
            Assert.Null(unhandled.Value);
        }

        Assert.Equal(5, channel.InvocationCount);
        Assert.Equal(
            [MacroScripts.FormatPriceName, "Constant", "Broken", "Missing", "NeverScripted"],
            channel.InvokedNames);
        Assert.Equal(
            [
                ExpansionMode.MacroDirect,
                ExpansionMode.MacroDynamic,
                ExpansionMode.MacroDirect,
                ExpansionMode.MacroDirect,
                ExpansionMode.MacroDirect
            ],
            channel.InvokedForms);
        Assert.Single(channel.InvocationsOf("Constant"));
        Assert.Equal("NeverScripted", channel.LastInvocation?.Name);
    }

    /// <summary>
    /// The channel can break the correlation protocol on purpose, and can fail in transit.
    /// </summary>
    /// <remarks>
    /// Both paths exist so the guards around the inverted stream are reachable. A channel that always
    /// answered correctly would leave <c>MacroProtocolViolationException</c> - the guard standing between a
    /// late answer and a macro result reaching the wrong calculation - permanently untested.
    /// </remarks>
    [Fact]
    public async Task ScriptedMacroChannel_CanBreakTheProtocolAndCanFail()
    {
        FakeDataWindowObject column = new("n1", FakeColumnType.DecimalOf(2));
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptValue("Constant", 1L);

        channel.OverrideInvocationId = "not-the-one-that-was-asked";
        channel.OverrideSequence = 99L;

        MacroInvocationResponse mismatched = await channel.InvokeAsync(
            Invocation(column, "Constant", ExpansionMode.MacroDirect),
            TestContext.Current.CancellationToken);

        Assert.Equal("not-the-one-that-was-asked", mismatched.InvocationId);
        Assert.Equal(99L, mismatched.Sequence);

        channel.OverrideInvocationId = null;
        channel.OverrideSequence = null;
        channel.EchoSequence = false;

        MacroInvocationResponse withoutSequence = await channel.InvokeAsync(
            Invocation(column, "Constant", ExpansionMode.MacroDirect),
            TestContext.Current.CancellationToken);

        Assert.Equal("inv-1", withoutSequence.InvocationId);
        Assert.Null(withoutSequence.Sequence);

        MacroInvocation? observed = null;
        channel.OnInvoke = invocation => observed = invocation;
        channel.Fault = new TimeoutException("scripted transport failure");

        _ = await Assert.ThrowsAsync<TimeoutException>(() => channel.InvokeAsync(
            Invocation(column, "Constant", ExpansionMode.MacroDirect),
            TestContext.Current.CancellationToken).AsTask());

        // Recorded BEFORE the throw, so the engine's issuing of the call is evidence either way.
        Assert.NotNull(observed);
        Assert.Equal(3, channel.InvocationCount);
    }

    /// <summary>
    /// The trace sink keeps records in order and its two oracle restatements agree with
    /// <c>:L753-L758</c>.
    /// </summary>
    [Fact]
    public void ScriptedTraceSink_ComposesTheStackAndSelectsTheNullSentinel()
    {
        Assert.Equal(">", ScriptedTraceSink.StackSeparator);
        Assert.Equal("(null)", ScriptedTraceSink.NullValueSentinel);

        // :L753-L757 - frames joined, terminal appended, NO TRAILING SEPARATOR.
        Assert.Equal("outer>inner>n1", ScriptedTraceSink.ComposeStack(["outer", "inner"], "n1"));

        // A top-level calculation has an empty calc stack, so the payload is the column name alone.
        Assert.Equal("n1", ScriptedTraceSink.ComposeStack([], "n1"));

        // :L758 - the two conditions that select the sentinel, and the one combination that does not.
        Assert.Equal("(null)", ScriptedTraceSink.ExpectedValue("", true, DataWindowServiceBase.COL_TYPE_STRING));
        Assert.Equal("(null)", ScriptedTraceSink.ExpectedValue("", false, DataWindowServiceBase.COL_TYPE_DECIMAL));
        Assert.Equal("(null)", ScriptedTraceSink.ExpectedValue(null, false, DataWindowServiceBase.COL_TYPE_DECIMAL));

        // A STRING column WITHOUT the empty-is-null flag traces the empty string, not the sentinel.
        Assert.Equal(string.Empty, ScriptedTraceSink.ExpectedValue("", false, DataWindowServiceBase.COL_TYPE_STRING));

        // A non-empty value is never replaced.
        Assert.Equal("12.3", ScriptedTraceSink.ExpectedValue("12.3", true, DataWindowServiceBase.COL_TYPE_DECIMAL));
    }

    /// <summary>
    /// The trace sink records what it is given, in order, and keeps the record even when delivery fails.
    /// </summary>
    [Fact]
    public void ScriptedTraceSink_RecordsEveryPayloadAndKeepsItWhenDeliveryFails()
    {
        ScriptedTraceSink sink = new();

        sink.Emit(TraceRecord(1L, "n1", "outer>n1", "1 + 1", "2"));
        sink.Emit(TraceRecord(2L, "n2", "n2", string.Empty, ScriptedTraceSink.NullValueSentinel));

        Assert.Equal(2, sink.Count);
        Assert.Equal(["outer>n1", "n2"], sink.Stacks);
        Assert.Equal(["1 + 1", string.Empty], sink.Expressions);
        Assert.Equal(["2", ScriptedTraceSink.NullValueSentinel], sink.Values);
        Assert.Equal([1L, 2L], sink.SequenceNumbers);
        Assert.Single(sink.For("n1"));
        Assert.Equal("n2", sink.LastRecord?.ColumnName);

        sink.Fault = new InvalidOperationException("scripted sink failure");
        _ = Assert.Throws<InvalidOperationException>(
            () => sink.Emit(TraceRecord(3L, "n3", "n3", "x", "y")));

        // The record survived the failed delivery - the trace is diagnostics, and losing the evidence that
        // the engine tried to emit would be worse than the failure itself.
        Assert.Equal(3, sink.Count);

        sink.Clear();
        Assert.Equal(0, sink.Count);
        Assert.Null(sink.LastRecord);
    }

    /// <summary>
    /// The handler answers by route, records the credential's PRESENCE only, and observes the cancellation
    /// token.
    /// </summary>
    /// <remarks>
    /// The credential assertion is the C-F and C-G pair in one place: the header WAS attached (so nothing
    /// disabled authentication) and its parameter is NOT in the record (so nothing can leak it).
    /// </remarks>
    [Fact]
    public async Task ScriptedHttpMessageHandler_AnswersByRouteAndRecordsCredentialPresenceOnly()
    {
        // Owned by the test rather than by the client: `disposeHandler: false` below keeps the recorder
        // readable for as long as the assertions need it, so the handler needs an owner of its own.
        using ScriptedHttpMessageHandler handler = new ScriptedHttpMessageHandler()
            .RouteJson(HttpMethod.Post, "/v1/tokens", HttpStatusCode.OK, "{\"ok\":true}")
            .RouteStatus(HttpMethod.Get, "/health", HttpStatusCode.NoContent);

        using HttpClient client = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://dataservices.invalid", UriKind.Absolute),
        };

        DeterministicRandomSource random = new();
        string credential = random.NextPlaceholderCredential();

        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/tokens")
        {
            Content = new StringContent("{\"subject\":\"dataservices\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        using HttpResponseMessage issued =
            await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal(
            "{\"ok\":true}",
            await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using HttpResponseMessage health =
            await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, health.StatusCode);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(["/v1/tokens", "/health"], handler.RequestedPaths);

        HttpExchangeRecord tokenExchange = Assert.Single(handler.ExchangesFor("/v1/tokens"));
        Assert.Equal(HttpMethod.Post, tokenExchange.Method);
        Assert.Equal("{\"subject\":\"dataservices\"}", tokenExchange.Body);
        Assert.Contains("application/json", tokenExchange.ContentType, StringComparison.Ordinal);
        Assert.True(tokenExchange.HasAuthorization);
        Assert.Equal("Bearer", tokenExchange.AuthorizationScheme);
        Assert.True(tokenExchange.HasAuthorizationParameter);
        Assert.Contains("Authorization", tokenExchange.HeaderNames);
        Assert.True(tokenExchange.CancellationTokenCanBeCanceled);

        // C-F: the credential is NOWHERE in the record, including in its rendering.
        Assert.DoesNotContain(credential, tokenExchange.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(credential, tokenExchange.Body, StringComparison.Ordinal);

        HttpExchangeRecord healthExchange = Assert.Single(handler.ExchangesFor("/health"));
        Assert.False(healthExchange.HasAuthorization);
        Assert.Null(healthExchange.AuthorizationScheme);
        Assert.Equal(string.Empty, healthExchange.Body);
        Assert.Equal("/health", handler.LastExchange?.Path);
    }

    /// <summary>
    /// An unscripted route fails loudly, a sequence answers differently per hit, and a fault route throws.
    /// </summary>
    /// <remarks>
    /// Failing loudly is the property that keeps a mis-wired test a FAILURE rather than a timeout: the
    /// alternative - answering a plausible status - would let the client's own error handling produce an
    /// assertion about a request that never reached its intended endpoint.
    /// </remarks>
    [Fact]
    public async Task ScriptedHttpMessageHandler_FailsLoudlyAndScriptsSequencesAndFaults()
    {
        using ScriptedHttpMessageHandler handler = new ScriptedHttpMessageHandler()
            .RouteSequence(
                HttpMethod.Get,
                "/v1/flaky",
                _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => new HttpResponseMessage(HttpStatusCode.OK))
            .RouteFault(null, "/v1/broken", new HttpRequestException("scripted transit failure"));

        using HttpClient client = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://dataservices.invalid", UriKind.Absolute),
        };

        Uri flaky = new("/v1/flaky", UriKind.Relative);

        using HttpResponseMessage first =
            await client.GetAsync(flaky, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);

        using HttpResponseMessage second =
            await client.GetAsync(flaky, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Beyond the sequence the LAST responder keeps answering.
        using HttpResponseMessage third =
            await client.GetAsync(flaky, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        HttpRequestException transit = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("/v1/broken", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Equal("scripted transit failure", transit.Message);

        // The unmatched request names what arrived AND what was scripted, which is everything needed to fix
        // the wiring. It surfaces UNWRAPPED: HttpClient wraps a transport failure in HttpRequestException,
        // but an InvalidOperationException from a handler propagates as itself - which is what makes a
        // mis-wired test read as a wiring failure rather than as a network one.
        InvalidOperationException unmatched = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(new Uri("/v1/absent", UriKind.Relative), TestContext.Current.CancellationToken));
        Assert.Contains("/v1/absent", unmatched.Message, StringComparison.Ordinal);
        Assert.Contains("GET /v1/flaky", unmatched.Message, StringComparison.Ordinal);
        Assert.Contains("* /v1/broken", unmatched.Message, StringComparison.Ordinal);

        // Every attempt was recorded, the unmatched one included.
        Assert.Equal(5, handler.RequestCount);

        _ = Assert.Throws<ArgumentException>(
            () => handler.RouteSequence(HttpMethod.Get, "/v1/empty"));
    }

    /// <summary>
    /// The Persistence factory produces the three scripted shapes contract C-05 and C-06 are judged on.
    /// </summary>
    [Fact]
    public void ScriptedPersistenceResponses_ProducesTheThreeScriptedShapes()
    {
        // SHAPE 1 - a complete server-streamed retrieval of buffer-shaped chunks.
        ImmutableArray<QueryResponse> stream = ScriptedPersistenceResponses.QueryStream(7L, chunkCount: 2);

        Assert.Equal(4, stream.Length);
        Assert.Equal(7L, stream[0].RowCount.RowCount);

        QueryDataChunk firstChunk = stream[1].DataChunk;
        Assert.Equal(1L, firstChunk.ChunkIndex);
        Assert.Equal(2L, firstChunk.ChunkCount);
        Assert.False(firstChunk.FullState);
        Assert.Equal(DwBuffer.Primary, Assert.Single(firstChunk.State.Segments).Buffer);

        QueryDataChunk lastChunk = stream[2].DataChunk;
        Assert.Equal(2L, lastChunk.ChunkIndex);
        Assert.True(lastChunk.FullState);
        Assert.Equal(WireRetCode.Ok, stream[3].Status.RetCode);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ScriptedPersistenceResponses.QueryStream(1L, chunkCount: 0));

        // SHAPE 2 - the chunk-size refusal. The guard is "at or below 1000", so the code is
        // E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L410].
        SetChunkSizeResponse rejection = ScriptedPersistenceResponses.ChunkSizeRejection(1000L);

        Assert.Equal(WireRetCode.EInvalidArgument, rejection.Status.RetCode);
        Assert.Equal((long)WireRetCode.EInvalidArgument, RetCode.E_INVALID_ARGUMENT);
        Assert.Contains("1000", rejection.Status.ErrorText, StringComparison.Ordinal);

        // 1001 is the smallest value the client accepts, which is the other side of the same boundary.
        Assert.Equal(1_001L, PersistenceClient.SmallestValidChunkSize);

        // SHAPE 3 - the aborted conflict, carrying current AND original row state.
        ConflictDetail conflict = ScriptedPersistenceResponses.Conflict(
            rowsExpected: 2L,
            rowsMatched: 1L,
            ScriptedPersistenceResponses.ModifiedConflictRow(1L, "salary", 5L, 21_000d, 20_000d));

        Assert.Equal("COMPANY", conflict.UpdateTable);
        ConflictRow row = Assert.Single(conflict.Rows);
        Assert.Equal(DwBuffer.Primary, row.Buffer);
        Assert.Equal(ItemStatus.DataModified, row.ItemStatus);
        Assert.Equal(21_000d, Assert.Single(row.CurrentValues).Value.DoubleValue);
        Assert.Equal(20_000d, Assert.Single(row.OriginalValues).Value.DoubleValue);

        RichErrorBinding binding = ScriptedPersistenceResponses.UpdateConflictBinding();
        Assert.Equal((int)StatusCode.Aborted, binding.GrpcStatusCode);
        Assert.Equal(RichErrorTrailer.Descriptor.FullName, binding.PayloadType);
        Assert.EndsWith("-bin", binding.TrailerKey, StringComparison.Ordinal);

        RpcException aborted = ScriptedPersistenceResponses.AbortedConflict(conflict);

        Assert.Equal(StatusCode.Aborted, aborted.StatusCode);

        byte[]? payload = aborted.Trailers.GetValueBytes(binding.TrailerKey);
        Assert.NotNull(payload);

        RichErrorTrailer decoded = RichErrorTrailer.Parser.ParseFrom(payload);
        Assert.Equal(RetCode.E_DB_ERROR, decoded.RetCode);
        Assert.Equal(conflict, decoded.Conflict);
    }

    /// <summary>
    /// The token provider issues an opaque placeholder, records every request, and can refuse.
    /// </summary>
    [Fact]
    public async Task ScriptedTokenProvider_IssuesAnOpaquePlaceholderAndRecordsRequests()
    {
        DeterministicTimeProvider clock = new();
        ScriptedTokenProvider provider = new(clock, new DeterministicRandomSource());

        // C-F: the credential is composed at test time behind a prefix that says what it is, and it is
        // structurally not a token of any scheme - no dot-separated segments, no vendor prefix.
        Assert.StartsWith(
            DeterministicRandomSource.PlaceholderCredentialPrefix,
            provider.Credential,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".", provider.Credential, StringComparison.Ordinal);

        ServiceTokenRequest request = new("dataservices", "persistence", ["persistence.query"]);

        ServiceToken token = await provider.GetTokenAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(provider.Credential, token.AccessToken);
        Assert.Equal(ScriptedTokenProvider.BearerTokenType, token.TokenType);
        Assert.Equal(clock.Instant + provider.Lifetime, token.ExpiresAt);
        Assert.Equal(["persistence.query"], token.GrantedScopes);
        Assert.Equal(1, provider.RequestCount);
        Assert.Same(request, provider.LastRequest);

        // Granting less than was asked is a real issuer behaviour a client must cope with.
        provider.GrantedScopes = [];
        ServiceToken narrowed =
            await provider.GetTokenAsync(request, TestContext.Current.CancellationToken);
        Assert.Empty(narrowed.GrantedScopes);

        // A refusal is the honest way to reach a client's unauthenticated path - never a silent absence.
        provider.Fault = new InvalidOperationException("scripted issuer refusal");
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(request, TestContext.Current.CancellationToken).AsTask());

        // Recorded BEFORE the throw, so the refused attempt counts too.
        Assert.Equal(3, provider.RequestCount);
        Assert.Equal(3, provider.Requests.Length);
    }

    /// <summary>
    /// The clock is frozen, moves only when asked, and reports a fixed timestamp frequency.
    /// </summary>
    /// <remarks>
    /// Freezing is what makes a recorded timestamp reproducible, which is the golden-master technique's one
    /// hard prerequisite (AAP 0.6.7). The fixed frequency is what makes a measured INTERVAL reproducible
    /// too: the system provider's frequency is platform-dependent, so an elapsed assertion would otherwise
    /// carry a machine-specific rounding error.
    /// </remarks>
    [Fact]
    public void DeterministicTimeProvider_IsFrozenUntilMovedAndFixesTheFrequency()
    {
        DeterministicTimeProvider clock = new();

        Assert.Equal(DeterministicTimeProvider.DefaultInstant, clock.Instant);
        Assert.Equal(TimeZoneInfo.Utc, clock.LocalTimeZone);
        Assert.Equal(TimeSpan.TicksPerSecond, clock.TimestampFrequency);

        // Reading Instant does not count as a read, so a test can inspect without perturbing.
        Assert.Equal(0, clock.Reads);

        Assert.Equal(DeterministicTimeProvider.DefaultInstant, clock.GetUtcNow());
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, clock.GetUtcNow());
        Assert.Equal(2, clock.Reads);

        // The timestamp derives from the same instant and is NOT advanced by a read, so a pair of
        // timestamps around code that took no clock time measures zero.
        long before = clock.GetTimestamp();
        Assert.Equal(TimeSpan.Zero, clock.GetElapsedTime(before, clock.GetTimestamp()));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(DeterministicTimeProvider.DefaultInstant.AddMinutes(5), clock.Instant);
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            clock.GetElapsedTime(before, clock.GetTimestamp()));

        // Backward motion is permitted, because a corrected host clock is a real condition an expiry cache
        // has to cope with.
        clock.Advance(TimeSpan.FromMinutes(-10));
        Assert.Equal(DeterministicTimeProvider.DefaultInstant.AddMinutes(-5), clock.Instant);

        // A non-zero step is DETERMINISTIC motion: the nth read is the start plus n-1 steps, always.
        DeterministicTimeProvider stepping = new(DeterministicTimeProvider.DefaultInstant)
        {
            Step = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(DeterministicTimeProvider.DefaultInstant, stepping.GetUtcNow());
        Assert.Equal(DeterministicTimeProvider.DefaultInstant.AddSeconds(1), stepping.GetUtcNow());
        Assert.Equal(DeterministicTimeProvider.DefaultInstant.AddSeconds(2), stepping.GetUtcNow());

        stepping.Reset(DeterministicTimeProvider.DefaultInstant);
        Assert.Equal(0, stepping.Reads);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, stepping.Instant);
    }

    /// <summary>
    /// The random source is reproducible from its seed, in every projection it offers.
    /// </summary>
    /// <remarks>
    /// Reproducibility ACROSS RUNTIME VERSIONS is the property a seeded <see cref="Random"/> does not
    /// promise, which is why the generator is written out in this file. The two independent instances below
    /// stand in for two captures taken at different times: they must agree value for value, or a paired
    /// legacy-and-candidate recording could differ for a reason that has nothing to do with the code.
    /// </remarks>
    [Fact]
    public void DeterministicRandomSource_IsReproducibleFromItsSeed()
    {
        DeterministicRandomSource first = new();
        DeterministicRandomSource second = new();

        Assert.Equal(DeterministicRandomSource.DefaultSeed, first.Seed);
        Assert.Equal(0L, first.Draws);

        Assert.Equal(first.NextUInt64(), second.NextUInt64());
        Assert.Equal(first.NextBytes(20), second.NextBytes(20));
        Assert.Equal(first.NextHex(32), second.NextHex(32));
        Assert.Equal(first.NextGuid(), second.NextGuid());
        Assert.Equal(first.NextPlaceholderCredential(), second.NextPlaceholderCredential());
        Assert.Equal(first.Draws, second.Draws);
        Assert.True(first.Draws > 0L);

        // A different seed gives a different sequence, so a suite needing two independent streams can have
        // them without either becoming non-deterministic.
        DeterministicRandomSource other = new(seed: 12_345UL);
        first.Reset();
        Assert.NotEqual(first.NextUInt64(), other.NextUInt64());

        // Reset returns the stream to its start, so a scenario can be replayed.
        first.Reset();
        second.Reset();
        Assert.Equal(0L, first.Draws);
        Assert.Equal(first.NextHex(8), second.NextHex(8));

        // The identifier is well-formed version 4 - a consumer that validates the shape must not reject it.
        Guid identifier = first.NextGuid();
        Assert.Equal(4, identifier.Version);
        Assert.Equal(Guid.Empty, Guid.Empty);
        Assert.NotEqual(Guid.Empty, identifier);

        // Bounded draws stay in range, and a non-positive bound is refused rather than wrapped.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int bounded = first.Next(10);
            Assert.InRange(bounded, 0, 9);
        }

        Assert.Empty(first.NextBytes(0));
        Assert.Equal(string.Empty, first.NextHex(0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => first.Next(0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => first.NextBytes(-1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => first.NextHex(-1));

        // Zero is refused rather than silently degenerating: xorshift produces an all-zero sequence from an
        // all-zero state.
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandomSource(0UL));
    }

    /// <summary>Builds a macro invocation for the sanity suite.</summary>
    /// <param name="column">The column the macro is being calculated for.</param>
    /// <param name="name">The macro name.</param>
    /// <param name="form">The expansion form.</param>
    /// <param name="args">The evaluated arguments, in one-based order.</param>
    /// <returns>The invocation.</returns>
    /// <remarks>
    /// The correlation identity is fixed rather than generated, so the echo assertions can name it. That is
    /// safe here precisely because <see cref="ScriptedMacroChannel"/> never generates one itself - it echoes
    /// or overrides.
    /// </remarks>
    private static MacroInvocation Invocation(
        IDataWindowObject column,
        string name,
        ExpansionMode form,
        params string[] args) =>
        new()
        {
            InvocationId = "inv-1",
            Sequence = 1L,
            Row = 1L,
            Dwo = column,
            Name = name,
            Arguments = MacroArgumentList.FromEvaluated(args),
            Form = form,
        };

    /// <summary>Builds an expression trace record for the sanity suite.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number.</param>
    /// <param name="columnName">The column the expression belongs to.</param>
    /// <param name="stack">The composed call stack.</param>
    /// <param name="expression">The expression text.</param>
    /// <param name="value">The traced value, sentinel included as literal text.</param>
    /// <returns>The record.</returns>
    private static ExpressionTraceRecord TraceRecord(
        long sequenceNumber,
        string columnName,
        string stack,
        string expression,
        string value) =>
        new()
        {
            SequenceNumber = sequenceNumber,
            SessionId = "session-1",
            Handle = DataWindowHandle.From("dw-1"),
            Row = 1L,
            ColumnName = columnName,
            Stack = stack,
            StackFrames = [.. stack.Split(ScriptedTraceSink.StackSeparator)],
            Expression = expression,
            Value = value,
            Depth = 1,
            Timestamp = DeterministicTimeProvider.DefaultInstant,
        };
}
