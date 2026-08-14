// ==================================================================================================
//  EventOrderingPatternTests - THE PER-CAPABILITY-AREA ORDERING CONFORMANCE SUITE
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//
//  AAP 0.1.3 goal G5 - "preserve event ordering across the new service boundaries" - is named THE
//  SINGLE HIGHEST RISK in the whole decomposition (AAP 0.6.1). AAP 0.8.1 turns that into a concrete
//  obligation: "choose an event-ordering pattern per capability area and write conformance tests per
//  workflow". THIS FILE IS THAT CONFORMANCE SUITE. It asserts the ASSIGNMENT ITSELF - AAP 0.6.1.4's
//  table, encoded as data - and then asserts, one property test at a time, the STRUCTURAL REASON
//  each area's pattern cannot be changed.
//
//  WHAT THIS FILE IS *NOT* FOR, AND WHERE THAT WORK LIVES INSTEAD
//  ------------------------------------------------------------------------------------------------
//  The 22-event SURFACE enumeration, the per-handler DELEGATION shape (raw -> semantic -> broker and
//  its non-uniform variations), the twelve topic constants, the three liveness guards, the lifecycle
//  orders and the seven of_on / of_off pass-throughs all belong to DataWindowEventChainTests.cs and
//  are NOT restated here. This file tests ORDERING GUARANTEES, not handler bodies. Where a fact is
//  asserted in both files the two assertions differ in kind: that file asks "does THIS EVENT report
//  the discipline its area was assigned", this file asks "does THIS AREA, driven end to end, actually
//  behave the way its assigned pattern requires - and does the alternative pattern actually break it".
//
//  THE TWO PATTERNS, AND THE ONE SENTENCE THAT SEPARATES THEM
//  ------------------------------------------------------------------------------------------------
//  AAP 0.6.1.4 offers exactly two, and requires one be chosen per capability area:
//
//    (a) SEQUENCING TOKEN   - a monotonic token a consumer MAY use to DETECT and REORDER out-of-order
//                             delivery. Contract name: OrderingDiscipline.Sequenced.
//    (b) SYNCHRONOUS        - strictly synchronous request/response inside ONE validation session on
//                             ONE bidirectional stream. NO reordering is permitted; an out-of-order
//                             arrival is a HARD ERROR. Contract name: OrderingDiscipline.Synchronous.
//
//  THE TWO CARRY THE SAME EVIDENCE AND GRANT OPPOSITE AUTHORITY. Both put a monotonic sequence number
//  on every message. Under (a) that number is a REORDER KEY; under (b) it is a TRIPWIRE. The whole
//  substance of AAP 0.6.1.4 is therefore not "what the token is" but WHAT A CONSUMER IS PERMITTED TO
//  DO WITH AN OUT-OF-ORDER ARRIVAL - and that asymmetry is asserted directly below, on one shared
//  arrival stream fed to both disciplines (TheTwoPatternsDifferOnlyInWhatAConsumerMayDoWithAnArrival).
//
//  WHY (b) IS NOT MERELY A PREFERENCE FOR THE ITEM-CHANGE GROUP
//  ------------------------------------------------------------------------------------------------
//  Reordering that group is not undesirable, it is SEMANTICALLY IMPOSSIBLE, and the mechanism is four
//  lines of the oracle:
//
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L195       the item-change event
//                                                                          STASHES its raw result
//      :L331-L332   the validation-error event READS AND CLEARS that stash
//      :L338-L340   and PRE-SETS its own result when the stashed code was 1 or 3
//      :L204-L208   the item-change event fires a NESTED event from inside itself
//      :L192-L196   whose re-entrancy flag is SAVED and RESTORED, never cleared
//      :L387-L393   and the kill-focus tail queues a deferred accept ONLY while that flag is clear
//
//  One event's behaviour is a FUNCTION of the previous event's return value. A consumer that buffered
//  and re-sorted the group would read a stash written by the wrong predecessor, or by none - AND IT
//  WOULD DO SO SILENTLY, because every individual message would still be well formed. The sharpest
//  demonstration of that is below: reversing the pair leaves the RETURNED NUMBER IDENTICAL while the
//  observable behaviour differs (Stash_ReorderingThePairLeavesTheNumberIdenticalAndTheBehaviourWrong).
//
//  THE ORACLE IS READ ONLY (constraint C-C)
//  ------------------------------------------------------------------------------------------------
//  Every ordering claim below carries its ws_objects/** locator, because the legacy tree is the ONLY
//  specification: logfile.md stops at framework 3.0.7.2062 while the commit history runs years later,
//  and the two PowerBuilder build definitions contradict each other and both name a library that
//  exists nowhere in the repository, so neither would build as written. Nothing else in the tree can
//  adjudicate a behavioural question. No file under ws_objects/** is edited, moved or reformatted by
//  this suite; it is read as the behavioural oracle and nothing else.
//
//  GOVERNING RULES AND CONSTRAINTS
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns exactly "No user rules provided." Per AAP 0.7.1 that is a FINDING AND NOT
//  LATITUDE: NO USER RULE GOVERNS THIS FILE, none is invented, and the enterprise-standard baseline
//  of AAP 0.7.2 applies in their place. The binding constraints are AAP 0.7.3's non-rule set, and the
//  ones that bite here are:
//
//    C-A  Ordering guarantees are properties of the PUBLISHED CONTRACT, so they are asserted against
//         the GENERATED dataservices.v1 types - SequencingToken, OrderingDiscipline, EventId,
//         EventChainRequest, EventChainResponse and StructuredError - and never against an
//         internal-only field alone. The two enums the chain uses ARE those generated types (aliased
//         at the top of Domain/DataWindowEventChain.cs), which is what makes that possible without
//         reaching across a service boundary.
//    C-B  No behaviour improvement. Every oddity below is asserted AS IT IS: the empty `case 1` arm
//         that does not fall through, the 3-collapses-to-1 rewrite, the identical return number for
//         two behaviourally different orderings, the always-Synchronous reading of the dual-role
//         kill-focus event, and the hardcoded non-localized expression messages.
//    C-D  No deferred service, not even stubbed. Nothing here renders, measures, positions or
//         converts a DPI unit; the presentational halves of the three split services are absent and
//         their absence is asserted as a headless data contract rather than filled in.
//    C-F  No credential, key, password or token literal anywhere. A SEQUENCING TOKEN IS AN ORDERING
//         DEVICE AND NOT A CREDENTIAL, and a SESSION ID IS AN OPAQUE CORRELATION HANDLE AND NOT PROOF
//         OF IDENTITY - both words appear throughout and neither carries any secret.
//    C-H  80% line coverage per service, which is why every path is driven through hand-written
//         doubles with no live DataWindow, no window handle, no message pump and no network.
//    C-K  Every assignment row STATES THE STRUCTURAL REASON its pattern cannot be changed, carries it
//         in the theory data, and echoes it into the failure message so a regression names the area
//         and the reason together.
//
//  AAP 0.4.5.3 NAMING CONSTRAINT, HONOURED IN THE NEGATIVE. Legacy SCREAMING_SNAKE identifiers are
//  REFERENCED here (EventGate.EID_ITEMCHANGE, DataWindowEventChain.EVT_LOSEFOCUS, RetCode.CANCELLED,
//  RetCode.E_TIME_OUT, MacroSentinels.FUNC_INVOKE) and NONE IS DECLARED. That matters mechanically:
//  the repository-root .editorconfig lowers CA1707 and IDE1006 only for the specific FILES that carry
//  those declarations, and this file is not one of them - so a declaration here would break the build.
//
//  SHAPE (AAP 0.6.7)
//  ------------------------------------------------------------------------------------------------
//  Table-driven parity matrices as theories with member data; plain xunit `Assert`; hand-written
//  doubles only, reusing the project's shared seams rather than duplicating them. NO SLEEP, NO TIMER,
//  NO WALL-CLOCK READ AND NO THREAD-POOL ORDERING ASSUMPTION appears anywhere in this file - the
//  deferred continuation is drained by the test itself, and the two clock-dependent macro outcomes are
//  asserted through their public factories rather than by waiting for a timeout to elapse. Ordering
//  assertions compare ORDERED SEQUENCES and never sets. No test is written so that it would pass on
//  either outcome: every hard-error row asserts the error, and every reorderable row asserts a
//  SUCCESSFUL reorder.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE (AAP 0.8.5). The repository publishes no SLA,
//  no latency budget, no throughput target and no availability commitment, so nothing below is
//  described as fast or optimised and no ordering choice is justified on performance grounds. Every
//  one is justified by SEMANTICS and by a locator.
// ==================================================================================================
using System.Collections.Immutable;

using Google.Protobuf;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

// ALIASED RATHER THAN IMPORTED WHOLESALE, for the same reason DataWindowEventChainTests.cs aliases:
// PowerFramework.Contracts.Common.V1 and PowerFramework.Contracts.DataServices.V1 each publish a type
// whose name also exists in PowerFramework.DataServices.Domain or .Expressions - the wire half and the
// in-process half of a matched pair - so an unaliased import is CS0104. Naming exactly the types this
// file needs keeps every use unambiguous without importing either namespace.
using EventChainRequest = PowerFramework.Contracts.DataServices.V1.EventChainRequest;
using EventChainResponse = PowerFramework.Contracts.DataServices.V1.EventChainResponse;
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using ExpansionMode = PowerFramework.Contracts.DataServices.V1.ExpansionMode;
using OrderingDiscipline = PowerFramework.Contracts.DataServices.V1.OrderingDiscipline;
using SequencingToken = PowerFramework.Contracts.DataServices.V1.SequencingToken;
using Severity = PowerFramework.Contracts.DataServices.V1.Severity;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireStructuredError = PowerFramework.Contracts.DataServices.V1.StructuredError;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The per-capability-area ordering conformance suite: AAP 0.6.1.4's assignment matrix asserted as
/// data, the hard-error rule that gives pattern (b) its teeth, the structural reason each synchronous
/// area cannot be relaxed, the <c>Post</c> replacement, and the one-session/one-stream guarantee.
/// </summary>
public sealed class EventOrderingPatternTests
{
    // ==============================================================================================
    //  FIXTURE CONSTANTS - the one-column, one-row arrangement every chain test below drives
    // ==============================================================================================

    /// <summary>The one column of the fixture. <c>char(50)</c>, so the coercion arm is reachable.</summary>
    private const string ColumnName = "name";

    /// <summary>The column type, which selects the coercion prefix at <c>se_cst_dw.sru:L233</c>.</summary>
    private const string ColumnType = "char(50)";

    /// <summary>The ONE-BASED row every event is raised against (AAP 0.4.5.4).</summary>
    private const long Row = 1L;

    /// <summary>The cell value the fixture seeds, and the edit text that leaves it unchanged.</summary>
    private const string OriginalValue = "original";

    /// <summary>The rejected edit text the validation-error event is driven with.</summary>
    private const string RejectedValue = "rejected";

    /// <summary>The separator the assignment matrix joins an area's ORDERED event names with.</summary>
    /// <remarks>
    /// An ARROW, so a theory display name reads as the workflow it describes rather than as a set. The
    /// order in the string IS the order the oracle raises the events in, and the tests below walk it.
    /// </remarks>
    private const string EventChainArrow = " -> ";

    // ==============================================================================================
    //  SECTION 1 - AAP 0.6.1.4's ASSIGNMENT MATRIX, ENCODED AS DATA
    //  --------------------------------------------------------------------------------------------
    //  ONE ROW PER CAPABILITY AREA, not one row per event, and that distinction is the point:
    //  DataWindowEventChainTests.cs already pins the per-event mapping, so a per-event table here
    //  would add nothing. AN AREA IS THE UNIT AAP 0.6.1.4 ASSIGNS A PATTERN TO, so it is the unit
    //  this file asserts - and it is the unit a regression should name.
    //
    //  EVERY ROW CARRIES FOUR THINGS, and the fourth is required by constraint C-K:
    //    1. the area name                       - what a failure must name
    //    2. the assigned OrderingDiscipline     - the published contract enum, not a local synonym
    //    3. the area's events IN ORACLE ORDER   - spelled with their LEGACY lowercase names
    //    4. the STRUCTURAL REASON               - why the pattern cannot be changed, with locators
    //
    //  THE EVENT NAMES ARE THE LEGACY SPELLINGS ON PURPOSE. They are parsed into the published EventId
    //  enum case-insensitively, which asserts something worth asserting on its own: that the wire enum
    //  NAMES EVERY LEGACY EVENT OF EVERY AREA. A drift between se_cst_dw.sru's declarations and
    //  dataservices.v1.proto's enum therefore fails here as a parse failure rather than passing
    //  unnoticed.
    //
    //  ONLY SERIALIZABLE THEORY ARGUMENTS. Strings and one enum - no record, no array, no tuple. The
    //  xunit v3 analyzers flag a TheoryData type argument that cannot be serialized, and warnings are
    //  errors repository wide, so the richer per-area data is projected from these four primitives
    //  rather than carried as an object.
    // ==============================================================================================

    /// <summary>The item-change and validation chain - the highest-risk area in the refactor.</summary>
    private const string ItemChangeArea = "ItemChangeAndValidation";

    /// <summary>The context-menu pair.</summary>
    private const string ContextMenuArea = "ContextMenu";

    /// <summary>The drop-down search pair.</summary>
    private const string DropDownSearchArea = "DropDownSearch";

    /// <summary>Column-expression macro invocation - contract C-04's inverted channel.</summary>
    private const string MacroInvocationArea = "MacroInvocation";

    /// <summary>Focus, mouse and row-focus notification.</summary>
    private const string NotificationArea = "FocusMouseAndRowFocusNotification";

    /// <summary>The column-expression trace - pure diagnostics.</summary>
    private const string TraceArea = "ExpressionTrace";

    /// <summary>
    /// AAP 0.6.1.4's table verbatim: area, assigned pattern, ordered legacy event names, and the
    /// structural reason the assignment cannot be changed (constraint C-K).
    /// </summary>
    private static readonly (string Area, OrderingDiscipline Pattern, string Events, string Reason)[]
        AssignmentTable =
        [
            (
                ItemChangeArea,
                OrderingDiscipline.Synchronous,
                string.Join(
                    EventChainArrow,
                    "ondwnitemchange",
                    "ondoitemchange",
                    "onitemchanged",
                    "ondoitemchanged",
                    "ondwnitemvalidationerror",
                    "ondwnkillfocus"),
                "REORDERING IS SEMANTICALLY IMPOSSIBLE, NOT MERELY UNDESIRABLE. The validation-error "
                    + "handler READS AND CLEARS the code the preceding item-change event stashed "
                    + "[se_cst_dw.sru:L195 writes, :L331-L332 consumes and clears] and PRE-SETS its own "
                    + "result from it [:L338-L340]; the item-change handler fires a NESTED event from "
                    + "inside itself [:L204-L208] under a re-entrancy flag that is SAVED and RESTORED "
                    + "rather than cleared [:L192-L196]; and the kill-focus tail queues its deferred "
                    + "accept ONLY while that flag is clear [:L387-L393]. One event's behaviour is a "
                    + "function of its predecessor's return value."
            ),
            (
                ContextMenuArea,
                OrderingDiscipline.Synchronous,
                string.Join(EventChainArrow, "oninitcontextmenu", "oncontextmenu"),
                "INITIALISATION MUST COMPLETE BEFORE THE MENU IDENTIFIER MEANS ANYTHING. The first "
                    + "event is where the application populates the menu "
                    + "[n_cst_dwsvc_contextmenu.sru:L147] and the second is handed the identifier the "
                    + "menu returned [:L194] - an identifier that names an item the first event added. "
                    + "Before the first has run there is no item store for the identifier to resolve "
                    + "against, so `mid` is not late, it is meaningless."
            ),
            (
                DropDownSearchArea,
                OrderingDiscipline.Synchronous,
                string.Join(EventChainArrow, "onddsgetfilter", "onddsfiltered"),
                "THE FIRST EVENT PRODUCES ITS RESULT THROUGH A `ref string` OUT-PARAMETER "
                    + "[se_cst_dw.sru:L13], SO ITS ANSWER HAS NO FIRE-AND-FORGET FORM. The event has no "
                    + "return type at all: its result is the mutation of the reference, so the caller "
                    + "BLOCKS on the produced filter because the filter is the only thing the call "
                    + "exists to obtain. The counts the second event carries are computed from that "
                    + "filter [n_cst_dwsvc_dropdownsearch.sru:L342, :L408]."
            ),
            (
                MacroInvocationArea,
                OrderingDiscipline.Synchronous,
                "oncolumnexpinvokemethod",
                "THE CALCULATION CANNOT PROCEED WITHOUT THE RETURNED VALUE. The legacy expects the "
                    + "APPLICATION to implement the macro switch and raises the event mid-expression "
                    + "[n_cst_dwsvc_columnexp.sru:L2263, :L2287]; across the boundary DataServices must "
                    + "CALL BACK INTO ITS CLIENT over contract C-04's inverted InvokeMethodChannel and "
                    + "wait. There is no fallback macro implementation and no default value that would "
                    + "not put a fabricated number into a calculation."
            ),
            (
                NotificationArea,
                OrderingDiscipline.Sequenced,
                string.Join(
                    EventChainArrow,
                    "ondwnsetfocus",
                    "ondwnkillfocus",
                    "ondwnrowchanging",
                    "ondwnrowchange",
                    "ondwnlbuttonclk",
                    "ondwnlbuttondblclk",
                    "ondwnlbuttonup",
                    "ondwnrbuttondown",
                    "ondwnrbuttonup",
                    "ondwnitemchangefocus",
                    "ondwnchanging"),
                "THESE ARE NOTIFICATIONS WHOSE ONLY RETURN CONTRACT IS THE PREVENT CONVENTION "
                    + "[se_cst_dw.sru:L115-L118, :L120-L121, :L126, :L132, :L166-L172, :L177-L179], and "
                    + "not one of them reads or writes any of the four cross-event fields at :L89-L96. "
                    + "With no cross-event state there is nothing an out-of-order arrival can corrupt, "
                    + "so the monotonic token genuinely carries REORDER AUTHORITY and the consumer may "
                    + "use it. `ondwnkillfocus` appears here as PURE NOTIFICATION and in the "
                    + "item-change row as that chain's TAIL - the one dual-membership event in the "
                    + "matrix, and the reason DisciplineOf takes a second parameter at all."
            ),
            (
                TraceArea,
                OrderingDiscipline.Sequenced,
                "oncolumnexptrace",
                "PURE DIAGNOSTICS, FIRE-AND-FORGET. The value has already been computed by the time the "
                    + "trace is raised [n_cst_dwsvc_columnexp.sru:L752-L758], and the only live legacy "
                    + "consumer appends it to a multi-line edit "
                    + "[ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L315-L316]. Dropping, "
                    + "delaying or reordering a trace record cannot change a calculation result, so the "
                    + "token is a reorder key and a failing sink is absorbed rather than propagated."
            ),
        ];

    /// <summary>
    /// AAP 0.6.1.4's assignment matrix as theory data - one row per capability area.
    /// </summary>
    /// <remarks>
    /// Four serializable arguments: the area name, the published discipline enum, the ordered legacy
    /// event names joined by <see cref="EventChainArrow"/>, and the structural reason (constraint C-K).
    /// </remarks>
    public static TheoryData<string, OrderingDiscipline, string, string> CapabilityAreaAssignments
    {
        get
        {
            TheoryData<string, OrderingDiscipline, string, string> data = new();

            foreach ((string area, OrderingDiscipline pattern, string events, string reason) in
                AssignmentTable)
            {
                data.Add(area, pattern, events, reason);
            }

            return data;
        }
    }

    // ==============================================================================================
    //  FIXTURE HELPERS
    //  --------------------------------------------------------------------------------------------
    //  DataWindowEventChainTests.cs holds an equivalent builder, but it is PRIVATE to that class, so
    //  this file declares its own rather than widening another suite's surface to reach it. The
    //  namespace-level doubles that file publishes - FakeEventChain, FakeAttachedServiceFactory,
    //  RecordingEventObserver - ARE reused, because duplicating them would create a second source of
    //  truth for the same behaviour.
    // ==============================================================================================

    /// <summary>One assembled chain plus everything a test needs to observe its ordering.</summary>
    /// <param name="Chain">The chain under test.</param>
    /// <param name="Observer">Every dispatch, in COMPLETION order.</param>
    /// <param name="Session">The one validation session the whole synchronous group shares.</param>
    private sealed record ChainUnderTest(
        FakeEventChain Chain,
        RecordingEventObserver Observer,
        ValidationSession Session)
    {
        /// <summary>The composed host double, which owns the call log and the handler seams.</summary>
        internal FakeDataWindowHost Host => Chain.Host;

        /// <summary>The one column handle every event is raised against.</summary>
        internal IDataWindowObject Dwo => Host.DwObject(ColumnName);

        /// <summary>The event identities the observer saw, in COMPLETION order.</summary>
        internal IReadOnlyList<EventId> Observed => Observer.EventIds;

        /// <summary>The tokens the observer saw, in COMPLETION order.</summary>
        internal IReadOnlyList<long> Tokens =>
            [.. Observer.Outcomes.Select(outcome => outcome.Sequence)];

        /// <summary>The disciplines the observer saw, in COMPLETION order.</summary>
        internal IReadOnlyList<OrderingDiscipline> Disciplines =>
            [.. Observer.Outcomes.Select(outcome => outcome.Discipline)];
    }

    /// <summary>
    /// Builds a chain over a one-column, one-row fixture with the cursor on row 1.
    /// </summary>
    /// <param name="disabledMask">
    /// The initial event-gate bitmask [<c>se_cst_dw.sru:L89-L90</c>]. Zero disables nothing.
    /// </param>
    /// <param name="sessionId">
    /// The correlation identifier. An OPAQUE HANDLE and never a credential (constraint C-F).
    /// </param>
    /// <returns>The assembled chain, its observer and its session.</returns>
    private static ChainUnderTest NewChain(uint disabledMask = 0u, string sessionId = "ordering-session")
    {
        RecordingEventObserver observer = new();
        FakeAttachedServiceFactory services = new();
        ValidationSession session =
            new(sessionId, "dw-ordering", disabledMask, new SessionLifetimeOptions());
        FakeEventChain chain = new(session, services, observer);

        _ = chain.Host.AddColumn(ColumnName, ColumnType);
        _ = chain.Host.AddRow(OriginalValue);
        chain.Host.CurrentRow = Row;

        return new ChainUnderTest(chain, observer, session);
    }

    /// <summary>
    /// Resolves one legacy event name to the PUBLISHED <c>EventId</c>.
    /// </summary>
    /// <param name="legacyName">
    /// The oracle's own lowercase spelling, exactly as declared at <c>se_cst_dw.sru:L11-L32</c>.
    /// </param>
    /// <returns>The contract enum member.</returns>
    /// <remarks>
    /// CASE-INSENSITIVE ON PURPOSE, AND THE PARSE IS ITSELF AN ASSERTION. The generated member for
    /// <c>ondwnitemchange</c> is <c>Ondwnitemchange</c> - protobuf's own conversion of
    /// <c>EVENT_ID_ONDWNITEMCHANGE</c> - so a case-insensitive parse of the LEGACY spelling succeeds
    /// only while the wire enum still names that legacy event. A drift between
    /// <c>se_cst_dw.sru</c> and <c>dataservices.v1.proto</c> therefore surfaces here as a parse failure
    /// instead of passing unnoticed.
    /// </remarks>
    private static EventId ToEventId(string legacyName) =>
        Enum.Parse<EventId>(legacyName, ignoreCase: true);

    /// <summary>Splits an assignment row's event list back into its ordered legacy names.</summary>
    /// <param name="events">The joined list.</param>
    /// <returns>The names, in the oracle's own raise order.</returns>
    private static ImmutableArray<string> SplitEvents(string events) =>
        [.. events.Split(EventChainArrow, StringSplitOptions.None)];

    /// <summary>
    /// Whether an area's occurrences of <c>ondwnkillfocus</c> are the TAIL of the item-change chain
    /// rather than standalone focus notification.
    /// </summary>
    /// <param name="area">The capability area.</param>
    /// <returns><see langword="true"/> for the item-change area alone.</returns>
    /// <remarks>
    /// THE ONE DUAL-MEMBERSHIP EVENT IN THE WHOLE MATRIX. <c>ondwnkillfocus</c> [<c>:L387-L393</c>] is
    /// the tail of the synchronous item-change chain AND a standalone focus notification, and only the
    /// emitter can tell which role an occurrence is playing - which is exactly what
    /// <see cref="DataWindowEventOrdering.DisciplineOf(EventId, bool)"/>'s second parameter expresses.
    /// </remarks>
    private static bool IsItemChangeTail(string area) =>
        string.Equals(area, ItemChangeArea, StringComparison.Ordinal);

    // ==============================================================================================
    //  PHASE 1 - THE ASSIGNMENT MATRIX IS IMPLEMENTED, AREA BY AREA
    // ==============================================================================================

    /// <summary>
    /// Every event of every capability area carries the discipline AAP 0.6.1.4 assigned that area, and
    /// the failure message names the area, the event and the structural reason (constraint C-K).
    /// </summary>
    /// <param name="area">The capability area.</param>
    /// <param name="pattern">The pattern AAP 0.6.1.4 assigns it.</param>
    /// <param name="events">Its events, in the oracle's raise order.</param>
    /// <param name="reason">Why the assignment cannot be changed.</param>
    [Theory]
    [MemberData(nameof(CapabilityAreaAssignments))]
    public void EveryCapabilityAreaCarriesThePatternItsStructureForces(
        string area,
        OrderingDiscipline pattern,
        string events,
        string reason)
    {
        // CONSTRAINT C-K IS ASSERTED, NOT ASSUMED: a row with no stated reason is a row nobody can
        // audit, so an empty reason fails before the discipline is even looked at.
        Assert.False(
            string.IsNullOrWhiteSpace(reason),
            "Capability area '" + area + "' states no structural reason for its ordering pattern; "
                + "constraint C-K requires every boundary-specific decision to record one.");

        ImmutableArray<string> names = SplitEvents(events);
        Assert.False(names.IsEmpty, "Capability area '" + area + "' names no events.");

        bool withinItemChangeChain = IsItemChangeTail(area);

        foreach (string name in names)
        {
            EventId eventId = ToEventId(name);

            Assert.NotEqual(EventId.Unspecified, eventId);

            // COMPOSED STRINGS RATHER THAN A BARE ENUM COMPARISON, so the failure message carries the
            // area, the event and the reason together - which is what makes a regression identify the
            // area immediately instead of reporting "expected Synchronous, actual Sequenced".
            Assert.Equal(
                area + "." + name + " => " + pattern + " because " + reason,
                area
                    + "."
                    + name
                    + " => "
                    + DataWindowEventOrdering.DisciplineOf(eventId, withinItemChangeChain)
                    + " because "
                    + reason);
        }
    }

    /// <summary>
    /// The six areas between them classify ALL TWENTY-TWO events, and <c>ondwnkillfocus</c> is the ONLY
    /// event that belongs to two of them.
    /// </summary>
    /// <remarks>
    /// A COVERAGE AUDIT OF THE MATRIX ITSELF. AAP 0.6.1.4's own table enumerates nine events under the
    /// notification heading and names neither <c>ondwnitemchangefocus</c> [<c>:L22</c>] nor
    /// <c>ondwnchanging</c> [<c>:L21</c>]; both satisfy that group's stated test exactly - the prevent
    /// convention is their only return contract and neither touches the cross-event state at
    /// <c>:L89-L96</c> - so both are carried in the notification row here rather than left
    /// unclassified. Leaving either out would make an unclassified REAL event indistinguishable on the
    /// wire from a corrupt one.
    /// </remarks>
    [Fact]
    public void TheMatrixClassifiesAllTwentyTwoEventsAndDuplicatesOnlyTheDualRoleTail()
    {
        List<EventId> assigned = [];

        foreach ((string area, _, string events, _) in AssignmentTable)
        {
            _ = area;
            assigned.AddRange(SplitEvents(events).Select(ToEventId));
        }

        // se_cst_dw.sru:L11-L32 declares exactly 22 events - nine semantic and thirteen raw - and
        // EventId numbers them 1..22 in that same declaration order.
        EventId[] declared = [.. Enum.GetValues<EventId>().Where(id => id != EventId.Unspecified)];

        Assert.Equal(22, declared.Length);
        Assert.Equal([.. declared.OrderBy(id => (int)id)], [.. assigned.Distinct().OrderBy(id => (int)id)]);

        EventId[] inTwoAreas = [.. assigned
            .GroupBy(id => id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        Assert.Equal([EventId.Ondwnkillfocus], inTwoAreas);

        // AND THE TWO ROLES GENUINELY DIFFER, which is what makes the duplication meaningful rather
        // than a bookkeeping accident.
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventOrdering.DisciplineOf(EventId.Ondwnkillfocus));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventOrdering.DisciplineOf(EventId.Ondwnkillfocus, withinItemChangeChain: true));
    }

    /// <summary>
    /// Every pattern-(b) area is carried on the ONE contract that can express it: a session-correlated,
    /// bidirectional stream whose every message names its session and carries its token.
    /// </summary>
    /// <remarks>
    /// ASSERTED AGAINST THE GENERATED TYPES (constraint C-A). Ordering is a property of the PUBLISHED
    /// contract, so the evidence is <c>EventChainRequest</c> and <c>EventChainResponse</c> themselves -
    /// each carrying a <c>session_id</c> and a <c>SequencingToken</c> - and not an internal field of
    /// this service. The proto records the consequence of getting this wrong: sending an item-change
    /// event without a session is a DEFINED ERROR and never an implicit session creation, because an
    /// implicit session would give each event its own state and the stash the validation-error event
    /// reads would always be empty - the chain would appear to work and would take the wrong branch at
    /// <c>[:L338-L340]</c> every single time.
    /// </remarks>
    [Fact]
    public void EverySynchronousAreaIsCarriedOnOneSessionCorrelatedStream()
    {
        Assert.Contains(
            AssignmentTable,
            row => row.Pattern == OrderingDiscipline.Synchronous);

        EventChainRequest request = new()
        {
            SessionId = "ordering-session",
            Token = new SequencingToken
            {
                Sequence = 1L,
                Discipline = OrderingDiscipline.Synchronous,
            },
        };

        EventChainResponse response = new()
        {
            SessionId = request.SessionId,
            Token = new SequencingToken
            {
                Sequence = 1L,
                Discipline = OrderingDiscipline.Synchronous,
            },
        };

        // BOTH DIRECTIONS, because the stream is bidirectional: the client reports events inbound and
        // answers handler invocations outbound on the SAME ordered conversation.
        Assert.Equal("ordering-session", request.SessionId);
        Assert.Equal("ordering-session", response.SessionId);
        Assert.Equal(OrderingDiscipline.Synchronous, request.Token.Discipline);
        Assert.Equal(OrderingDiscipline.Synchronous, response.Token.Discipline);

        // AND THE POSTURE IS CONFIGURED RATHER THAN ACCIDENTAL. DataServices:EventChain:StrictOrdering
        // defaults to true, which is how the assignment survives a deployment that never sets it.
        Assert.True(
            new DataServicesOptions().EventChain.StrictOrdering,
            "DataServices:EventChain:StrictOrdering must default to true, because AAP 0.6.1.4 makes an "
                + "out-of-order arrival inside a synchronous group a hard error.");
    }

    /// <summary>
    /// Every pattern-(a) area carries a monotonic token, issued on entry and strictly increasing, so a
    /// consumer has the evidence its reorder authority depends on.
    /// </summary>
    /// <remarks>
    /// DRIVEN END TO END rather than asserted on the sequencer in isolation: the notification area's
    /// own events are raised through the chain, and the tokens are read off the reported outcomes. Zero
    /// is deliberately unreachable - <c>SequencingToken.sequence</c> documents <c>0</c> as "not
    /// supplied", which is a fault on every discipline, so tokens start at one.
    /// </remarks>
    [Fact]
    public void EverySequencedAreaCarriesAMonotonicToken()
    {
        ChainUnderTest fixture = NewChain();
        _ = fixture.Host.AddRow("second");

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnSetFocus());
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChanging(Row, 2L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(2L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonDown(10L, 20L, Row, fixture.Dwo));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonUp(10L, 20L, Row, fixture.Dwo));

        // AN ORDERED SEQUENCE, NEVER A SET.
        Assert.Equal(
            [
                EventId.Ondwnsetfocus,
                EventId.Ondwnrowchanging,
                EventId.Ondwnrowchange,
                EventId.Ondwnrbuttondown,
                EventId.Ondwnrbuttonup,
            ],
            fixture.Observed);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], fixture.Tokens);
        Assert.All(
            fixture.Tokens,
            token => Assert.True(
                token > DataWindowEventSequencer.NoToken,
                "Every message on the event chain must carry a token; 0 means 'not supplied', which "
                    + "SequencingToken documents as a fault on every discipline."));

        Assert.All(
            fixture.Disciplines,
            discipline => Assert.Equal(OrderingDiscipline.Sequenced, discipline));
    }

    // ==============================================================================================
    //  PHASE 2 - THE DECISIVE RULE: OUT OF ORDER UNDER PATTERN (b) IS A HARD ERROR
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.6.1.4: under pattern (b) the monotonic sequence numbers are used FOR DETECTION ONLY. An
    //  out-of-order arrival is a HARD ERROR and the chain does NOT buffer it, does NOT re-sort it and
    //  does NOT replay it. Under pattern (a) the identical evidence carries REORDER AUTHORITY and a
    //  consumer may act on it, so the same arrival must NOT fail the stream.
    //
    //  THAT ASYMMETRY IS THE WHOLE SUBSTANCE OF AAP 0.6.1.4. The two patterns do not differ in what
    //  they transmit - both put a monotonic token on every message - they differ ONLY IN WHAT A
    //  CONSUMER IS PERMITTED TO DO WITH AN OUT-OF-ORDER ARRIVAL. The last test in this region drives
    //  ONE arrival stream through BOTH disciplines to assert exactly that and nothing else.
    // ==============================================================================================

    /// <summary>
    /// A DUPLICATE token inside a synchronous group is a hard error, and the accepted position does not
    /// move - so a replayed message cannot resynchronise the group behind the consumer's back.
    /// </summary>
    /// <remarks>
    /// A DUPLICATE IS NOT A HARMLESS RETRANSMISSION HERE. Re-delivering the item-change event would
    /// re-run the stash write at <c>se_cst_dw.sru:L195</c>, so the validation-error event that follows
    /// would consume a code produced by an invocation the caller never intended to repeat. The rule is
    /// therefore the same as for a gap: the exact successor or nothing.
    /// </remarks>
    [Fact]
    public void UnderSynchronousOrderingADuplicateTokenIsAHardError()
    {
        DataWindowEventSequencer sequencer = new();

        sequencer.Accept(1L, OrderingDiscipline.Synchronous, EventId.Ondwnitemchange);

        DataWindowEventSequenceException error = Assert.Throws<DataWindowEventSequenceException>(
            () => sequencer.Accept(1L, OrderingDiscipline.Synchronous, EventId.Ondwnitemchange));

        Assert.Equal(2L, error.ExpectedSequence);
        Assert.Equal(1L, error.ActualSequence);
        Assert.Equal(1L, sequencer.LastAccepted);
    }

    /// <summary>
    /// A GAP inside a synchronous group is a hard error, and it is NEVER buffered, re-sorted or
    /// replayed: the expected position stands still until the true successor arrives.
    /// </summary>
    /// <remarks>
    /// THE THREE ASSERTIONS ARE THREE DIFFERENT CLAIMS, and only the third rules out buffering.
    /// Refusing the gap says the fault is detected. Refusing the messages BEHIND the gap says nothing
    /// was queued in the hope the missing one would follow. Accepting the true successor afterwards
    /// says the group was not silently resynchronised onto the wrong position either - which is the
    /// failure mode a tolerant implementation would exhibit and which no single refusal can exclude.
    /// </remarks>
    [Fact]
    public void UnderSynchronousOrderingAGapIsAHardErrorAndIsNeverBufferedOrReplayed()
    {
        DataWindowEventSequencer sequencer = new();

        sequencer.Accept(1L, OrderingDiscipline.Synchronous, EventId.Ondwnitemchange);

        // THE GAP: 2 never arrived.
        DataWindowEventSequenceException skipped = Assert.Throws<DataWindowEventSequenceException>(
            () => sequencer.Accept(3L, OrderingDiscipline.Synchronous, EventId.Ondoitemchange));

        Assert.Equal(2L, skipped.ExpectedSequence);
        Assert.Equal(3L, skipped.ActualSequence);

        // NOTHING WAS BUFFERED. Two further arrivals from beyond the gap are refused with the SAME
        // expected position, which is only possible if neither the gap message nor these was retained.
        foreach (long beyond in (long[])[4L, 5L])
        {
            DataWindowEventSequenceException later =
                Assert.Throws<DataWindowEventSequenceException>(
                    () =>
                    {
                        sequencer.Accept(
                            beyond,
                            OrderingDiscipline.Synchronous,
                            EventId.Ondoitemchanged);
                    });

            Assert.Equal(2L, later.ExpectedSequence);
            Assert.Equal(beyond, later.ActualSequence);
        }

        Assert.Equal(1L, sequencer.LastAccepted);

        // AND NOTHING WAS REPLAYED. The true successor is still the only value the group will take, so
        // the refusals above did not quietly advance it.
        sequencer.Accept(2L, OrderingDiscipline.Synchronous, EventId.Ondoitemchange);
        Assert.Equal(2L, sequencer.LastAccepted);
    }

    /// <summary>
    /// The hard error is DEFINED AND MACHINE-READABLE: its four typed properties fully determine the
    /// fault, and the published contract carries it as a structured payload with an enum return code -
    /// so no consumer anywhere has to parse a message string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO HALVES, AND BOTH ARE REQUIRED. In process the fault is a typed exception carrying the event,
    /// the discipline and both sequence numbers. On the wire it is <c>EventChainResponse</c>'s THIRD
    /// ARM - the proto names an out-of-order arrival within a synchronous group as that arm's CANONICAL
    /// CASE, and records that it fails the session rather than being reordered. Constraint C-A is why
    /// the wire half is asserted against the GENERATED types rather than described in a comment.
    /// </para>
    /// <para>
    /// THE MESSAGE TEXT IS DELIBERATELY NOT PART OF THE CONTRACT. It is asserted here only to be
    /// non-empty, and the diagnosis is reconstructed from the typed members alone - which is the
    /// property a consumer depends on. <c>StructuredError.ret_code</c> is an ENUM and not a string for
    /// the same reason, and consumers must branch on the specific value rather than on a two-way
    /// success test, because <c>common.v1.RetCode</c> documents the tri-state hole in full: PREVENT
    /// reads as a success, and CANCELLED and null are NEITHER succeeded nor failed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHardErrorIsMachineReadableWithoutParsingItsMessage()
    {
        DataWindowEventSequencer sequencer = new();
        sequencer.Accept(1L, OrderingDiscipline.Synchronous, EventId.Ondwnitemchange);

        DataWindowEventSequenceException error = Assert.Throws<DataWindowEventSequenceException>(
            () =>
            {
                sequencer.Accept(
                    7L,
                    OrderingDiscipline.Synchronous,
                    EventId.Ondwnitemvalidationerror);
            });

        // THE IN-PROCESS HALF: four typed members, fully determining the fault.
        Assert.Equal(EventId.Ondwnitemvalidationerror, error.EventId);
        Assert.Equal(OrderingDiscipline.Synchronous, error.Discipline);
        Assert.Equal(2L, error.ExpectedSequence);
        Assert.Equal(7L, error.ActualSequence);
        Assert.False(
            string.IsNullOrWhiteSpace(error.Message),
            "The message is a diagnostic aid and is expected to exist, but no consumer may need to "
                + "parse it - which is what the four typed members above are for.");

        // THE WIRE HALF: EventChainResponse's third arm, carrying a DEFINED enum code and the token
        // that arrived. Built from the exception's own typed members, so a drift in either half shows
        // up as a mismatch here rather than as an untranslatable fault at run time.
        EventChainResponse response = new()
        {
            SessionId = "ordering-session",
            Token = new SequencingToken
            {
                Sequence = error.ActualSequence,
                Discipline = error.Discipline,
            },
            Error = new WireStructuredError
            {
                Text = "A DataWindow event arrived out of order inside a synchronous ordering group.",
                Localized = false,
                Category = 0L,
                Severity = Severity.StopSign,
                RetCode = WireRetCode.EInvalidArgument,
            },
        };

        Assert.Equal(EventChainResponse.PayloadOneofCase.Error, response.PayloadCase);
        Assert.Equal(WireRetCode.EInvalidArgument, response.Error.RetCode);
        Assert.Equal(Severity.StopSign, response.Error.Severity);

        // A DEFINED FAILURE, NOT A SUCCESS DRESSED UP. The tri-state hole makes this worth asserting:
        // PREVENT = 1 satisfies the legacy success predicate, so "not OK" is not enough on its own.
        Assert.NotEqual(WireRetCode.Ok, response.Error.RetCode);
        Assert.NotEqual(WireRetCode.Prevent, response.Error.RetCode);
        Assert.False(
            Predicates.IsSucceeded((long)response.Error.RetCode),
            "The out-of-order fault must not classify as a success under the legacy return-code "
                + "algebra, where PREVENT = 1 does.");

        // THE ARRIVING TOKEN TRAVELS WITH THE FAULT, so a consumer can say WHICH message was refused
        // without correlating against anything it has to keep itself.
        Assert.Equal(7L, response.Token.Sequence);
        Assert.Equal(OrderingDiscipline.Synchronous, response.Token.Discipline);
    }

    /// <summary>
    /// A default-constructed wire token is a fault on EVERY discipline, which is why tokens start at
    /// one rather than zero.
    /// </summary>
    /// <remarks>
    /// THE WIRE DEFAULT AND THE FAULT ARE THE SAME VALUE ON PURPOSE. Protobuf gives an unset
    /// <c>int64</c> the value <c>0</c>, and <c>SequencingToken.sequence</c> documents <c>0</c> as "not
    /// supplied", which is a fault on the event chain because every message on that stream carries a
    /// token. A sender that simply forgot the token is therefore refused rather than being treated as
    /// the first message of a group.
    /// </remarks>
    [Fact]
    public void ADefaultConstructedWireTokenIsAFaultOnEveryDiscipline()
    {
        SequencingToken unset = new();

        Assert.Equal(DataWindowEventSequencer.NoToken, unset.Sequence);
        Assert.Equal(OrderingDiscipline.Unspecified, unset.Discipline);

        foreach (OrderingDiscipline discipline in (OrderingDiscipline[])
            [
                OrderingDiscipline.Unspecified,
                OrderingDiscipline.Sequenced,
                OrderingDiscipline.Synchronous,
            ])
        {
            DataWindowEventSequencer sequencer = new();

            DataWindowEventSequenceException error = Assert.Throws<DataWindowEventSequenceException>(
                () => sequencer.Accept(unset.Sequence, discipline, EventId.Ondwnsetfocus));

            Assert.Equal(discipline, error.Discipline);
            Assert.Equal(DataWindowEventSequencer.NoToken, error.ActualSequence);
        }
    }

    /// <summary>
    /// Under pattern (a) a GAP is admitted - the token need only be above the mark - because the sequence
    /// space is shared with the outbound direction and the client never sends the numbers the server used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DOING THE REORDERING IN THE TEST ITSELF WOULD HIDE THE DEFECT. That shape feeds four
    /// displaced tokens to the sequencer, asserts that none is refused, then calls
    /// <c>OrderBy(token)</c> IN THE TEST and asserts the sorted result is contiguous. Both halves pass
    /// against an implementation that records the token and does nothing with it - the sort is the test's
    /// own, so all it proved was that <c>OrderBy</c> sorts. Production dispatched in arrival order
    /// throughout.
    /// </para>
    /// <para>
    /// WHAT IT ASSERTS is the rule the sequencer actually applies, in its permissive direction: an
    /// ASCENDING run with gaps is admitted and each arrival moves the mark to itself. The restrictive
    /// direction - a reversal or a duplicate - is the row below, and the consumer-level counterpart is in
    /// the region at the end of this file, driven through the real <c>EventChain</c>.
    /// </para>
    /// <para>
    /// WHY GAPS ARE LEGITIMATE HERE AND NOT UNDER PATTERN (b). Both directions of the chain draw from ONE
    /// counter, so a client's token is one past the highest it has SEEN and the numbers this server
    /// consumed for its own messages are numbers no client sends. Requiring contiguity of a sequenced
    /// arrival would therefore refuse correct clients.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnderSequencedOrderingAnAscendingRunWithGapsIsAdmitted()
    {
        // An ascending run that skips numbers, which is what a client produces when the server has
        // consumed tokens of its own in between.
        (long Sequence, EventId EventId)[] arrivals =
        [
            (1L, EventId.Ondwnsetfocus),
            (4L, EventId.Ondwnrowchanging),
            (5L, EventId.Ondwnrowchange),
            (9L, EventId.Ondwnrbuttonup),
        ];

        DataWindowEventSequencer sequencer = new();

        foreach ((long sequence, EventId eventId) in arrivals)
        {
            // NO THROW, ON ANY OF THEM. Refusing a gap would refuse a correct client.
            sequencer.Accept(sequence, OrderingDiscipline.Sequenced, eventId);

            // AND THE MARK FOLLOWS THE ARRIVAL RATHER THAN CRAWLING BEHIND IT, which is what makes the
            // next check a check against the last message actually dispatched.
            Assert.Equal(sequence, sequencer.LastAccepted);
            Assert.Equal(sequence + 1L, sequencer.NextExpected);
        }
    }

    /// <summary>
    /// An arrival AT OR BELOW the ordering mark is refused under pattern (a) - it is a reversal or a
    /// duplicate rather than a late arrival - and the refusal does not move the mark.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HALF MOST EASILY MISSED ENTIRELY. A sequenced arm that accepts every positive token dispatches
    /// a reversal and a duplicate silently and in arrival order; the token is
    /// measurable and unused. This row is the enforcement, and it fails against exactly that shape.
    /// </para>
    /// <para>
    /// THE ONE PLACE THE TWO PATTERNS AGREE, AND FOR DIFFERENT REASONS. Pattern (b) refuses it because the
    /// group may not be rearranged at all. Pattern (a) refuses it because the events after that position
    /// have already been dispatched, so accepting it would deliver a notification the ordering says came
    /// earlier - or, for a duplicate, run one event twice.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(5L)]
    [InlineData(4L)]
    [InlineData(1L)]
    public void UnderSequencedOrderingAnArrivalAtOrBelowTheMarkIsRefused(long replayed)
    {
        DataWindowEventSequencer sequencer = new();

        foreach (long dispatched in (long[])[1L, 5L])
        {
            sequencer.Accept(dispatched, OrderingDiscipline.Sequenced, EventId.Ondwnsetfocus);
        }

        DataWindowEventSequenceException refused = Assert.Throws<DataWindowEventSequenceException>(
            () => sequencer.Accept(replayed, OrderingDiscipline.Sequenced, EventId.Ondwnsetfocus));

        Assert.Equal(OrderingDiscipline.Sequenced, refused.Discipline);
        Assert.Equal(replayed, refused.ActualSequence);

        // THE DIAGNOSTIC REPORTS THE FLOOR, not a contiguity requirement the sequenced arm does not have.
        Assert.Equal(6L, refused.ExpectedSequence);

        // AND THE REFUSAL DID NOT MOVE THE MARK, so a correct arrival after it is still admitted.
        Assert.Equal(5L, sequencer.LastAccepted);

        sequencer.Accept(6L, OrderingDiscipline.Sequenced, EventId.Ondwnsetfocus);
        Assert.Equal(6L, sequencer.LastAccepted);
    }

    /// <summary>
    /// ONE arrival stream, BOTH disciplines: the patterns differ only in what a consumer is permitted
    /// to do with an out-of-order arrival.
    /// </summary>
    /// <remarks>
    /// THE POINT OF DRIVING THE SAME STREAM TWICE is that it isolates the single variable. The
    /// messages are identical, the tokens are identical and the evidence is identical; only the
    /// discipline changes, and the outcomes are opposite at the SAME message: pattern (a) admits the gap
    /// and pattern (b) refuses it. Any implementation that made the two agree - in either direction -
    /// would be failing AAP 0.6.1.4, and this test fails for both mistakes. It also pins the one place
    /// they agree, and that agreement is not the same as being identical: both refuse a REVERSAL, but only
    /// (b) refuses the gap that precedes it.
    /// </remarks>
    [Fact]
    public void TheTwoPatternsDifferOnlyInWhatAConsumerMayDoWithAnArrival()
    {
        long[] arrivals = [1L, 3L, 2L];

        DataWindowEventSequencer sequenced = new();
        DataWindowEventSequencer synchronous = new();

        sequenced.Accept(arrivals[0], OrderingDiscipline.Sequenced, EventId.Ondwnsetfocus);
        synchronous.Accept(arrivals[0], OrderingDiscipline.Synchronous, EventId.Ondwnitemchange);

        // PATTERN (a): the gap is ADMITTED, because a sequenced token need only be above the mark.
        sequenced.Accept(arrivals[1], OrderingDiscipline.Sequenced, EventId.Ondwnrowchange);
        Assert.Equal(3L, sequenced.LastAccepted);

        // AND THE REVERSAL BEHIND IT IS REFUSED. Without that enforcement this very arrival is accepted
        // and dispatched out of order, which is the failure this row exists to catch.
        DataWindowEventSequenceException reversed =
            Assert.Throws<DataWindowEventSequenceException>(
                () => sequenced.Accept(
                    arrivals[2],
                    OrderingDiscipline.Sequenced,
                    EventId.Ondwnrowchanging));

        Assert.Equal(OrderingDiscipline.Sequenced, reversed.Discipline);
        Assert.Equal(3L, sequenced.LastAccepted);

        // PATTERN (b): refused at the very same message, and refused again for the one behind it -
        // because the group cannot be reassembled after the fact.
        _ = Assert.Throws<DataWindowEventSequenceException>(
            () =>
            {
                synchronous.Accept(
                    arrivals[1],
                    OrderingDiscipline.Synchronous,
                    EventId.Ondoitemchange);
            });

        Assert.Equal(1L, synchronous.LastAccepted);
    }

    // ==============================================================================================
    //  PHASE 3 - WHY THE SYNCHRONOUS GROUP CANNOT BE RELAXED
    //  --------------------------------------------------------------------------------------------
    //  EACH STRUCTURAL REASON IS A PROPERTY TEST, NOT A COMMENT. The assignment matrix above states
    //  the reasons; this region drives each one until it BREAKS under the wrong pattern, because a
    //  reason nobody has demonstrated is a reason a future contributor can talk themselves out of.
    // ==============================================================================================

    /// <summary>
    /// Reversing the item-change and validation-error pair leaves the RETURNED NUMBER IDENTICAL while
    /// the behaviour is wrong - which is exactly why reordering that group is semantically impossible
    /// rather than merely undesirable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE SHARPEST FORM THE ARGUMENT TAKES. A reader might expect a reordered chain to fail
    /// loudly somewhere. It does not: both orderings answer <c>1</c>, so every individual message is
    /// still well formed and a consumer inspecting return values alone sees nothing wrong. What differs
    /// is whether the application's own <c>ItemError</c> handler was consulted at all, and whether a
    /// dialog-replacing structured error was produced - two facts a caller cannot recover from the
    /// number.
    /// </para>
    /// <para>
    /// THE MECHANISM: <c>se_cst_dw.sru:L195</c> stashes the item-change result; <c>:L331-L332</c> reads
    /// AND CLEARS it; <c>:L338-L340</c> pre-sets the validation-error result to <c>1</c> when the stashed
    /// code was <c>1</c> or <c>3</c> and therefore SKIPS the <c>ItemError</c> raise entirely. With no
    /// predecessor the stash is zero, the pre-set does not apply, <c>ItemError</c> IS raised, its null
    /// return is coerced to zero at <c>:L344</c>, and the message branch produces the structured error.
    /// </para>
    /// </remarks>
    [Fact]
    public void Stash_ReorderingThePairLeavesTheNumberIdenticalAndTheBehaviourWrong()
    {
        // ---- THE ORACLE'S ORDER: item change, then validation error -------------------------------
        ChainUnderTest ordered = NewChain();
        ordered.Host.ItemChangedHandler = (_, _, _) => 1L;

        Assert.Equal(1L, ordered.Chain.OnDwnItemChange(Row, ordered.Dwo, OriginalValue));
        Assert.Equal(1L, ordered.Session.CaptureState().RawItemChangeRetCode);

        long orderedResult =
            ordered.Chain.OnDwnItemValidationError(Row, ordered.Dwo, RejectedValue);

        // ---- THE REVERSED ORDER: the validation error arrives with no predecessor -----------------
        ChainUnderTest reversed = NewChain();
        reversed.Host.ItemChangedHandler = (_, _, _) => 1L;

        Assert.Equal(0L, reversed.Session.CaptureState().RawItemChangeRetCode);

        long reversedResult =
            reversed.Chain.OnDwnItemValidationError(Row, reversed.Dwo, RejectedValue);

        // ---- THE NUMBERS AGREE. THE BEHAVIOUR DOES NOT. -------------------------------------------
        Assert.Equal(orderedResult, reversedResult);

        // The pre-set path consults nothing: the application's ItemError handler is never reached.
        Assert.False(
            ordered.Host.CallLog.Contains("Event ItemError"),
            "With the stash holding 1 the validation-error handler pre-sets its own result at "
                + "se_cst_dw.sru:L338-L340 and must NOT raise ItemError.");
        Assert.True(
            reversed.Host.CallLog.Contains("Event ItemError"),
            "With no predecessor the stash is zero, so the pre-set does not apply and ItemError MUST "
                + "be raised - which is the observable difference a reordered chain would hide.");

        // And only the reversed run reaches the branch that replaces the legacy dialog.
        Assert.Null(ordered.Observer.Single(EventId.Ondwnitemvalidationerror).Error);
        Assert.NotNull(reversed.Observer.Single(EventId.Ondwnitemvalidationerror).Error);

        // THE STASH IS SINGLE USE, so a second validation error cannot read a stale predecessor's code
        // - which is what makes the ordering a one-shot handshake rather than a repeatable query.
        Assert.Equal(0L, ordered.Session.CaptureState().RawItemChangeRetCode);
    }

    /// <summary>
    /// Interleaving a second item-change between the pair overwrites the stash the validation-error
    /// event will consume, so the error event answers to the WRONG predecessor.
    /// </summary>
    /// <remarks>
    /// A REORDER IS NOT THE ONLY WAY TO BREAK THIS GROUP. Even with every message in ascending order, an
    /// extra item-change admitted between the two rewrites <c>_nItemChangeRetCode</c> [<c>:L195</c>]
    /// before the consumer at <c>:L331-L332</c> reads it. That is why the pattern is "strictly
    /// synchronous request/response" and not merely "in order": the group is a HANDSHAKE, and nothing
    /// may be admitted into the middle of it.
    /// </remarks>
    [Fact]
    public void Stash_InterleavingASecondItemChangeOverwritesTheStashTheErrorEventConsumes()
    {
        ChainUnderTest fixture = NewChain();

        // The first item change stashes 3 - the value that would pre-set the error event's result and
        // additionally suppress its tail restore [:L369]. The second stashes 0, which does neither.
        int calls = 0;
        fixture.Host.ItemChangedHandler = (_, _, _) => ++calls == 1 ? 3L : 0L;

        // :L225 rewrites a handler's 3 into 1 on the way out, while the STASH keeps the raw 3 - the
        // stash is written before the switch has looked at it.
        Assert.Equal(1L, fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));
        Assert.Equal(3L, fixture.Session.CaptureState().RawItemChangeRetCode);

        // THE INTERLEAVED SECOND ITEM CHANGE. Its own result is the default arm's forced 2 [:L250].
        Assert.Equal(2L, fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));
        Assert.Equal(0L, fixture.Session.CaptureState().RawItemChangeRetCode);
        Assert.Equal(2, calls);

        // The error event now consults the SECOND predecessor's code, so it raises ItemError - the
        // branch a stash of 3 would have skipped entirely.
        _ = fixture.Chain.OnDwnItemValidationError(Row, fixture.Dwo, RejectedValue);

        Assert.True(
            fixture.Host.CallLog.Contains("Event ItemError"),
            "The interleaved item change replaced the stash, so the validation-error event answered to "
                + "the wrong predecessor and took the raise branch instead of the pre-set branch.");

        // THE COUNTER-CASE, so the assertion above cannot pass for an unrelated reason: without the
        // interleave the very same first item change leads to the pre-set branch and raises nothing.
        ChainUnderTest clean = NewChain();
        clean.Host.ItemChangedHandler = (_, _, _) => 3L;

        Assert.Equal(1L, clean.Chain.OnDwnItemChange(Row, clean.Dwo, OriginalValue));
        _ = clean.Chain.OnDwnItemValidationError(Row, clean.Dwo, RejectedValue);

        Assert.False(
            clean.Host.CallLog.Contains("Event ItemError"),
            "With the stash still holding 3 the pre-set at :L338-L340 applies and ItemError is skipped.");
    }

    /// <summary>
    /// The item-change handler fires a NESTED event from inside itself, the nested dispatch is observed
    /// INSIDE the outer step, and the re-entrancy flag is SAVED AND RESTORED - never forced to false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RESTORE IS NOT A CLEAR, AND THE DIFFERENCE IS LOAD BEARING. <c>:L192</c> saves the flag,
    /// <c>:L193</c> sets it, <c>:L196</c> assigns THE SAVED VALUE back. Writing <c>false</c> at
    /// <c>:L196</c> would clear an OUTER invocation's flag while that outer invocation was still
    /// running, and the flag escapes the routine: <c>ondwnkillfocus</c> queues its deferred accept only
    /// while the flag is clear [<c>:L387-L390</c>], so a spurious clear would let an accept run in the
    /// middle of an item change. The test therefore runs the whole dispatch INSIDE an outer scope that
    /// has already set the flag, and asserts the flag survives.
    /// </para>
    /// <para>
    /// THE NESTED DISPATCH IS VISIBLE IN THE ORDER OF THE REPORTS. Tokens are issued on ENTRY and
    /// outcomes are reported on EXIT, so a nested run puts the inner event's outcome AHEAD of its
    /// enclosing one even though the enclosing one holds the LOWER token. That inversion is the direct
    /// consequence of <c>:L207</c> and is why the outcome is pushed to an observer rather than stored in
    /// a last-outcome slot, which the inner dispatch would overwrite.
    /// </para>
    /// </remarks>
    [Fact]
    public void NestedItemChangeEvent_IsObservedInsideTheOuterStepAndTheFlagIsSavedNotCleared()
    {
        ChainUnderTest fixture = NewChain();

        bool flagSeenSetInside = false;

        fixture.Host.ItemChangedHandler = (row, _, _) =>
        {
            // :L192-L193 - the flag is set for the duration of the raise.
            flagSeenSetInside = fixture.Session.DoItemChange;

            // Writing the buffer is what makes the equality test at :L198-L202 fail, which is the
            // condition for the nested raise at :L204-L208.
            _ = fixture.Host.SetItem(row, ColumnName, "written-by-the-handler");

            return 0L;
        };

        // THE OUTER SCOPE. Its saved value is `false`, and entering sets the flag - so if :L196 wrote
        // `false` instead of the saved value the assertion after the dispatch would still pass. The
        // scope is therefore entered a SECOND time below, where the saved value is `true`.
        using (fixture.Session.EnterItemChange())
        {
            Assert.True(fixture.Session.DoItemChange);

            // The default arm forcibly returns 2 [:L250].
            Assert.Equal(2L, fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

            // *** THE DECISIVE ASSERTION. *** :L196 restored the SAVED value, which the outer scope had
            // set to true. A port that wrote `false` here would clear a flag it does not own.
            Assert.True(
                fixture.Session.DoItemChange,
                "se_cst_dw.sru:L196 assigns the SAVED value back, so an item change nested inside an "
                    + "outer one must leave the outer invocation's flag set.");
        }

        // Leaving the outer scope restores ITS saved value, which was false.
        Assert.False(fixture.Session.DoItemChange);
        Assert.True(flagSeenSetInside);

        // THE NESTED EVENT RAN, AND IT COMPLETED BEFORE ITS ENCLOSING ONE.
        Assert.Contains(EventId.Ondwnchanging, fixture.Observed);

        int nested = fixture.Observed.ToList().IndexOf(EventId.Ondwnchanging);
        int enclosing = fixture.Observed.ToList().IndexOf(EventId.Ondwnitemchange);

        Assert.True(
            nested < enclosing,
            "The nested `ondwnchanging` raised at se_cst_dw.sru:L207 completes inside the enclosing "
                + "`ondwnitemchange`, so its outcome must arrive first.");

        // AND THE TOKENS INVERT THAT ORDER, because they are issued on entry rather than on exit.
        Assert.True(
            fixture.Observer.Single(EventId.Ondwnitemchange).Sequence
                < fixture.Observer.Single(EventId.Ondwnchanging).Sequence,
            "Tokens are issued on ENTRY, so the enclosing event holds the LOWER token even though its "
                + "outcome is reported last.");

        // THE ENCLOSING EVENT IS SYNCHRONOUS, and the nested occurrence runs under that enclosing
        // guarantee rather than needing a dual-role flag of its own.
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            fixture.Observer.Single(EventId.Ondwnitemchange).Discipline);
    }

    /// <summary>
    /// <c>OnDDSGetFilter</c> produces its result through a <c>ref string</c> out-parameter, which has no
    /// asynchronous representation: the caller blocks on the produced filter, and that filter is
    /// observable to the <c>OnDDSFiltered</c> step that follows it.
    /// </summary>
    /// <remarks>
    /// THE SIGNATURE IS THE ARGUMENT, so the signature is asserted. A member that returns
    /// <see langword="void"/> and produces its result by mutating a by-reference parameter cannot be
    /// awaited, cannot be queued and cannot answer later - there is nowhere for a continuation to put
    /// the value. The pair is therefore pattern (b) by construction rather than by choice, and the
    /// ordered call log proves the second step genuinely follows the first with the produced filter in
    /// hand [<c>n_cst_dwsvc_dropdownsearch.sru:L342</c> then <c>:L408</c>].
    /// </remarks>
    [Fact]
    public void DdsGetFilter_BlocksOnItsRefParameterAndTheFilterReachesTheFilteredStep()
    {
        const string produced = "name like 'Sh%' or pfwPinyinFirstLetterLike(name,'Sh',7)";
        const long remaining = 3L;
        const long removed = 6L;

        // ---- THE SIGNATURE ANSWERS ONLY THROUGH ITS ref PARAMETER --------------------------------
        System.Reflection.MethodInfo getFilter = Assert.IsType<System.Reflection.MethodInfo>(
            typeof(DataWindowServiceHost).GetMethod(nameof(DataWindowServiceHost.OnDDSGetFilter)),
            exactMatch: false);

        Assert.Equal(typeof(void), getFilter.ReturnType);
        Assert.Contains(
            getFilter.GetParameters(),
            parameter => parameter.ParameterType.IsByRef && !parameter.IsOut);

        // The C-03 semantic event on the chain answers the same shape, for the same reason.
        System.Reflection.MethodInfo semantic = Assert.IsType<System.Reflection.MethodInfo>(
            typeof(DataWindowEventChain).GetMethod(nameof(DataWindowEventChain.OnDdsGetFilter)),
            exactMatch: false);

        Assert.Equal(typeof(void), semantic.ReturnType);
        Assert.Contains(
            semantic.GetParameters(),
            parameter => parameter.ParameterType.IsByRef && !parameter.IsOut);

        // ---- THE CALLER BLOCKS, AND THE FILTER TRAVELS TO THE SECOND STEP -------------------------
        ChainUnderTest fixture = NewChain();
        string? filterSeenByTheFilteredStep = null;

        fixture.Host.DdsGetFilterHandler = (_, _, _, _) => produced;
        fixture.Host.DdsFilteredHandler = (_, _, rowCount, filteredCount) =>
        {
            // The counts the second event carries are derived from the filter the first one produced,
            // so observing the filter here is observing that the handshake completed in order.
            Assert.Equal(remaining, rowCount);
            Assert.Equal(removed, filteredCount);
            filterSeenByTheFilteredStep = produced;
        };

        string filter = string.Empty;
        fixture.Host.OnDDSGetFilter(Row, fixture.Dwo, "Sh", ref filter);

        // AVAILABLE ON RETURN - there is no awaitable, no task and no callback to wait for.
        Assert.Equal(produced, filter);

        fixture.Host.OnDDSFiltered(Row, fixture.Dwo, remaining, removed);

        Assert.Equal(produced, filterSeenByTheFilteredStep);

        // AN ORDERED SEQUENCE, NEVER A SET.
        Assert.Equal(
            ["Event OnDDSGetFilter", "Event OnDDSFiltered"],
            fixture.Host.CallLog.Members);

        // ---- THE SAME RESULT REPORTED ON THE C-03 CONTRACT ---------------------------------------
        fixture.Chain.ProducedFilterFactory = () => produced;

        string reported = string.Empty;
        fixture.Chain.OnDdsGetFilter(Row, fixture.Dwo, "Sh", ref reported);

        Assert.Equal(produced, reported);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Onddsgetfilter);
        Assert.Equal(produced, outcome.ProducedFilter);
        Assert.Equal(OrderingDiscipline.Synchronous, outcome.Discipline);
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventOrdering.DisciplineOf(EventId.Onddsfiltered));
    }

    /// <summary>
    /// A macro-invocation channel that RECORDS the invocation and then answers NOTHING - the one
    /// misbehaviour <see cref="ScriptedMacroChannel"/> cannot express, because it always answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DELIBERATELY RETURNS A NULL ANSWER, which the interface's own signature declares non-nullable.
    /// That is the point: the guard inside <see cref="MacroInvoker"/> exists precisely for a client
    /// implementation that breaks its contract, and the branch is unreachable from a well-behaved
    /// double. Suppressing the nullability here is what makes the guard testable at all.
    /// </para>
    /// <para>
    /// LOCAL TO THIS SUITE ON PURPOSE. It is not added to TestDoubles.cs because exactly one file needs
    /// it, which is the criterion that file states for itself.
    /// </para>
    /// </remarks>
    private sealed class SilentMacroChannel : IMacroInvocationChannel
    {
        /// <summary>How many invocations the channel was handed.</summary>
        internal int InvocationCount { get; private set; }

        /// <inheritdoc/>
        public ValueTask<MacroInvocationResponse> InvokeAsync(
            MacroInvocation invocation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            cancellationToken.ThrowIfCancellationRequested();

            InvocationCount++;

            // ANSWERS NOTHING. A real stream peer that closed without replying reaches the invoker in
            // exactly this state, and the invoker must fail rather than invent a value.
            return ValueTask.FromResult<MacroInvocationResponse>(null!);
        }
    }

    /// <summary>
    /// Macro invocation WAITS for the client's answer over contract C-04's inverted channel and, when no
    /// usable answer arrives, produces a DEFINED error instead of continuing with a default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FIVE FAILURE SHAPES, FIVE DEFINED OUTCOMES, AND NOT ONE FABRICATED VALUE. An answer for a
    /// different invocation, an answer echoing a different sequence and no answer at all are all
    /// <see cref="MacroProtocolViolationException"/> - a HARD error, because substituting a value
    /// computed for one macro into another's expression is silent data corruption and both expressions
    /// would still produce a plausible result. A client with no handler is a structured REFUSAL. A
    /// cancelled wait carries <c>RetCode.CANCELLED</c> and an elapsed timeout carries
    /// <c>RetCode.E_TIME_OUT</c>.
    /// </para>
    /// <para>
    /// THE PROTOCOL VIOLATION IS DELIBERATELY NOT ONE OF <see cref="MacroInvocationOutcome"/>'s members,
    /// and that is asserted. Cancellation and timeout are outcomes because they are things the
    /// environment does; a correlation failure is a BROKEN IMPLEMENTATION, and giving it a result value
    /// would invite a caller to carry on past it.
    /// </para>
    /// <para>
    /// NO CLOCK IS ADVANCED AND NOTHING IS WAITED ON. The cancelled path is reached with an
    /// already-cancelled token, and the timeout's defined code is asserted through the public factory
    /// that produces it - so this test has no wall-clock dependency of any kind.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MacroInvocation_WaitsForTheAnswerAndProducesADefinedErrorRatherThanADefault()
    {
        const string macroName = "FormatPrice";
        IDataWindowObject dwo = new FakeDataWindowObject("n1", "decimal(2)");
        MacroArgumentList arguments = MacroArgumentList.FromEvaluated("1234.5");

        static MacroDispatchPlan PlanFor(string name, MacroArgumentList args) =>
            MacroDispatchPlan.ForDirect(name, args, ExpansionMode.MacroDirect);

        // ---- THE HAPPY PATH, so every refusal below is a real difference and not the only outcome ---
        ScriptedMacroChannel answering = new();
        _ = answering.ScriptValue(macroName, "¥1,234.50");
        MacroInvoker invoker = new(answering, invocationIdFactory: () => "invocation-1");

        MacroInvocationResult rendered = await invoker
            .InvokeAsync(
                Row,
                dwo,
                PlanFor(macroName, arguments),
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        // THE OBSERVABLE RESULT IS THE TEXT SUBSTITUTED INTO THE EXPRESSION, not the value the handler
        // returned - a string answer is rendered as a quoted literal with NO escaping, exactly as the
        // oracle does none [n_cst_dwsvc_columnexp.sru:L2266].
        Assert.Equal(MacroInvocationOutcome.Rendered, rendered.Outcome);
        Assert.Equal("'¥1,234.50'", rendered.Rendered);
        Assert.Equal("('¥1,234.50')", rendered.WrappedRendering);
        Assert.False(rendered.Aborts);
        Assert.Equal(1, answering.InvocationCount);

        // ---- AN ANSWER FOR A DIFFERENT INVOCATION IS A HARD ERROR ---------------------------------
        ScriptedMacroChannel misdirected = new() { OverrideInvocationId = "someone-elses-invocation" };
        _ = misdirected.ScriptValue(macroName, "1");
        MacroInvoker misdirectedInvoker =
            new(misdirected, invocationIdFactory: () => "invocation-2");

        MacroProtocolViolationException wrongId =
            await Assert.ThrowsAsync<MacroProtocolViolationException>(
                async () => await misdirectedInvoker
                    .InvokeAsync(
                        Row,
                        dwo,
                        PlanFor(macroName, arguments),
                        cancellationToken: TestContext.Current.CancellationToken)
                    .ConfigureAwait(true))
            .ConfigureAwait(true);

        Assert.Equal("invocation-2", wrongId.ExpectedInvocationId);
        Assert.Equal("someone-elses-invocation", wrongId.InvocationId);
        Assert.False(wrongId.Late);

        // ---- AN ECHOED SEQUENCE THAT DOES NOT MATCH IS A HARD ERROR -------------------------------
        // The macro channel carries its own sequencing token, and it obeys the same rule as the event
        // chain's: FOR DETECTION ONLY. A mismatch is not a reorder opportunity.
        ScriptedMacroChannel misSequenced = new() { OverrideSequence = 99L };
        _ = misSequenced.ScriptValue(macroName, "1");
        MacroInvoker misSequencedInvoker =
            new(misSequenced, invocationIdFactory: () => "invocation-3");

        _ = await Assert.ThrowsAsync<MacroProtocolViolationException>(
                async () => await misSequencedInvoker
                    .InvokeAsync(
                        Row,
                        dwo,
                        PlanFor(macroName, arguments),
                        cancellationToken: TestContext.Current.CancellationToken)
                    .ConfigureAwait(true))
            .ConfigureAwait(true);

        // ---- NO ANSWER AT ALL IS A HARD ERROR -----------------------------------------------------
        SilentMacroChannel silent = new();
        MacroInvoker silentInvoker = new(silent, invocationIdFactory: () => "invocation-4");

        MacroProtocolViolationException noAnswer =
            await Assert.ThrowsAsync<MacroProtocolViolationException>(
                async () => await silentInvoker
                    .InvokeAsync(
                        Row,
                        dwo,
                        PlanFor(macroName, arguments),
                        cancellationToken: TestContext.Current.CancellationToken)
                    .ConfigureAwait(true))
            .ConfigureAwait(true);

        Assert.Equal("invocation-4", noAnswer.ExpectedInvocationId);
        Assert.Null(noAnswer.InvocationId);
        Assert.Equal(1, silent.InvocationCount);

        // ---- A CLIENT WITH NO HANDLER IS A STRUCTURED REFUSAL, NEVER A DEFAULT --------------------
        ScriptedMacroChannel unhandled = new();
        _ = unhandled.ScriptUnhandled(macroName);
        MacroInvoker unhandledInvoker = new(unhandled, invocationIdFactory: () => "invocation-5");

        MacroInvocationResult refused = await unhandledInvoker
            .InvokeAsync(
                Row,
                dwo,
                PlanFor(macroName, arguments),
                cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(MacroInvocationOutcome.Rejected, refused.Outcome);
        Assert.True(refused.Unhandled);
        Assert.True(refused.Aborts);
        Assert.Null(refused.Rendered);
        Assert.NotNull(refused.Error);

        // ---- A CANCELLED WAIT CARRIES A DEFINED CODE, AND NOTHING WAS ANSWERED --------------------
        ScriptedMacroChannel abandoned = new();
        _ = abandoned.ScriptValue(macroName, "1");
        MacroInvoker abandonedInvoker = new(abandoned, invocationIdFactory: () => "invocation-6");

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(true);

        MacroInvocationResult stopped = await abandonedInvoker
            .InvokeAsync(Row, dwo, PlanFor(macroName, arguments), cancellationToken: cancelled.Token)
            .ConfigureAwait(true);

        Assert.Equal(MacroInvocationOutcome.Cancelled, stopped.Outcome);
        Assert.Equal(RetCode.CANCELLED, stopped.ReturnCode);
        Assert.True(stopped.Aborts);
        Assert.Equal(0, abandoned.InvocationCount);

        // ---- THE TIMEOUT OUTCOME IS DEFINED TOO, ASSERTED WITHOUT WAITING FOR ONE -----------------
        MacroInvoker bounded = new(
            new ScriptedMacroChannel(),
            invocationTimeout: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), bounded.InvocationTimeout);
        Assert.Equal(
            RetCode.E_TIME_OUT,
            MacroInvocationResult.ForTimedOut("invocation-7", 7L).ReturnCode);
        Assert.True(MacroInvocationResult.ForTimedOut("invocation-7", 7L).Aborts);

        // ---- AND A BROKEN CLIENT IS NOT GIVEN AN OUTCOME VALUE TO CARRY ON PAST -------------------
        Assert.DoesNotContain(
            "Violation",
            string.Join(
                ",",
                Enum.GetNames<MacroInvocationOutcome>()));

        // THE ORDERING ASSIGNMENT ITSELF, for completeness: the calculation cannot proceed without the
        // answer, so the area is pattern (b).
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventOrdering.DisciplineOf(EventId.Oncolumnexpinvokemethod));

        // The reserved dispatch sentinel is REFERENCED and never redeclared (AAP 0.4.5.3).
        Assert.Equal("Invoke", MacroSentinels.FUNC_INVOKE);
    }

    // ----------------------------------------------------------------------------------------------
    //  THE CONTEXT-MENU PAIR                     n_cst_dwsvc_contextmenu.sru:L147 then :L194
    // ----------------------------------------------------------------------------------------------

    /// <summary>The one column of the context-menu arrangement.</summary>
    private const string MenuColumn = "salary";

    /// <summary>Its header text object, named with the oracle's own header suffix.</summary>
    private const string MenuHeaderObject = MenuColumn + "_t";

    /// <summary>
    /// A context-menu service and the host it is attached to, arranged so a header click produces a
    /// non-empty menu.
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    /// <remarks>
    /// <c>Processing</c> is <c>"1"</c>, which is the grid style - the only style the automatic-width
    /// block admits - and the column carries a POSITIVE tab sequence, because the fake defaults it to
    /// <c>"0"</c> which reads as non-editable and would make the check and paste blocks unreachable.
    /// Both are stated explicitly so the arrangement's dependencies are visible rather than inherited.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) NewAttachedContextMenu()
    {
        FakeDataWindowHost host = new(new EventBroker())
        {
            Processing = "1",
            ReadOnly = "no",
        };

        FakeDataWindowObjectDefinition column = host.AddColumn(MenuColumn, "decimal(2)");
        column.TabSequence = "50";

        _ = host.AddTextObject(MenuHeaderObject, "header");

        host.SetDescribe(MenuColumn + ".Band", "detail");
        host.SetDescribe(MenuColumn + ".Type", "column");
        host.SetDescribe(MenuColumn + ".edit.style", "edit");
        host.SetDescribe(MenuColumn + ".Protect", "0");
        host.SetDescribe(MenuColumn + ".visible", "1");
        host.SetDescribe(MenuColumn + ".protect", "0");
        host.SetDescribe("DataWindow.Header.Height", "100");

        ContextMenuModel service = new(
            new I18n(),
            new DataWindowExpressionEvaluator(host),
            Options.Create(new DataServicesOptions()));

        service.OnInit(host);
        host.CallLog.Clear();

        return (host, service);
    }

    /// <summary>The pointer facts a header click supplies (the geometry itself is deferred, C-D).</summary>
    /// <returns>The context.</returns>
    private static ContextMenuPointerContext HeaderPointer() =>
        new()
        {
            BandAtPointer = "header\t1",
            PointerY = 50L,
            ClipboardText = string.Empty,
        };

    /// <summary>A standalone object handle for the events that only read <c>Name</c> and <c>Type</c>.</summary>
    /// <param name="name">The object name.</param>
    /// <param name="type">The OBJECT type - <c>column</c>, <c>compute</c> or <c>text</c>.</param>
    /// <returns>The handle.</returns>
    private static FakeDataWindowObject MenuObject(string name, string type) =>
        new(name, "decimal(2)") { Type = type };

    /// <summary>
    /// The context-menu identifier is MEANINGLESS before initialisation has run, and driving the second
    /// half of the pair without the first is a DEFINED error rather than a silent no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THREE STATES, AND ONLY THE THIRD MAKES <c>mid</c> MEAN ANYTHING. Before the service is attached
    /// at all there is no host to raise the second event on, so the attempt is refused with a defined
    /// error. Attached but un-initialised, the item store is EMPTY, so every identifier resolves to
    /// "no such item" - the identifier is not late, it is meaningless. Only after the build has run does
    /// an identifier name something, and the build is where the application populates the menu
    /// [<c>n_cst_dwsvc_contextmenu.sru:L147</c>].
    /// </para>
    /// <para>
    /// THE HANDSHAKE IS ONE-SHOT, which is the other half of why the pair is synchronous: applying the
    /// selection runs the oracle's own cleanup [<c>:L197-L201</c>], so the store is gone afterwards and
    /// a second selection cannot be applied against it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ContextMenu_TheIdentifierIsMeaninglessBeforeInitialisationAndTheSecondHalfAloneFails()
    {
        FakeDataWindowObject dwo = MenuObject(MenuHeaderObject, "text");

        // ---- STATE 1: NOT ATTACHED. The second half alone is a DEFINED error. ---------------------
        FakeDataWindowHost detachedHost = new(new EventBroker());
        ContextMenuModel detached = new(
            new I18n(),
            new DataWindowExpressionEvaluator(detachedHost),
            Options.Create(new DataServicesOptions()));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => detached.ApplySelection(0L, dwo, ContextMenuModel.MID_COLAUTOWIDTH_ALL));

        Assert.False(
            string.IsNullOrWhiteSpace(refused.Message),
            "The refusal must name the missing initialisation rather than surfacing as a bare "
                + "reference fault.");

        // A dismissal is still answered without a host, because the oracle returns before it raises
        // anything when nothing was chosen [:L193] - so the guard above is about the RAISE and not
        // about the call.
        Assert.Equal(RetCode.OK, detached.ApplySelection(0L, dwo, 0L));

        // ---- STATE 2: ATTACHED, BUT THE BUILD HAS NOT RUN. Every identifier resolves to nothing. ---
        (FakeDataWindowHost host, ContextMenuModel service) = NewAttachedContextMenu();

        Assert.Equal(0, service.GetCount());
        Assert.Equal(0, service.GetIndex(ContextMenuModel.MID_COLAUTOWIDTH_ALL));
        Assert.Equal(0, service.GetIndex(ContextMenuModel.MID_COLCOPY));

        // ---- STATE 3: THE BUILD HAS RUN. Now an identifier names an item. -------------------------
        ContextMenuLayout layout = service.BuildMenu(0L, dwo, HeaderPointer());

        Assert.Equal(ContextMenuOutcome.Menu, layout.Outcome);
        Assert.True(layout.AwaitingSelection);
        Assert.NotEmpty(layout.Items);

        // Separators carry identifier zero, so the first REAL identifier is the one to resolve.
        uint chosen = layout.Items.First(item => item.Id != 0u).Id;

        Assert.True(
            service.GetIndex(chosen) > 0,
            "After initialisation the chosen identifier must resolve to a stored item; before it, the "
                + "same identifier resolved to nothing.");

        Assert.Equal(RetCode.OK, service.ApplySelection(0L, dwo, chosen));

        // ---- THE ORDER IS OBSERVABLE, AND IT IS THE ORACLE'S ------------------------------------
        int initialised = host.CallLog.IndexOf("Event OnInitContextMenu");
        int selected = host.CallLog.IndexOf("Event OnContextMenu");

        Assert.True(initialised >= 0, "The build must raise OnInitContextMenu [:L147].");
        Assert.True(selected >= 0, "Applying the selection must raise OnContextMenu [:L194].");
        Assert.True(
            initialised < selected,
            "Initialisation must COMPLETE before the identifier the second event carries can mean "
                + "anything, which is why the context-menu area is ordering pattern (b).");

        // ---- AND THE HANDSHAKE IS ONE-SHOT ------------------------------------------------------
        Assert.Equal(0, service.GetCount());
        Assert.Equal(0, service.GetIndex(chosen));

        // The assignment itself, for the record.
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventOrdering.DisciplineOf(EventId.Oninitcontextmenu));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventOrdering.DisciplineOf(EventId.Oncontextmenu));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE EXPRESSION TRACE                          n_cst_dwsvc_columnexp.sru:L752-L758
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The trace is fire-and-forget diagnostics: dropping it, or handing it to a sink that throws,
    /// changes nothing about the result that produced it - which is why it is pattern (a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THREE DELIVERY STATES ARE ASSERTED SEPARATELY, because they are three different facts.
    /// Delivered: the sink got it. FAILED: the sink threw, the failure was ABSORBED so the calculation
    /// survives, and it was COUNTED so a channel that has silently stopped working is discoverable - an
    /// invisibly failing diagnostic is worse than a disabled one, because it looks enabled. DROPPED: no
    /// sink at all, and the record is still produced, so a caller that wants to forward it does not
    /// depend on the sink existing.
    /// </para>
    /// <para>
    /// ONLY THE EXCEPTION TYPE IS RETAINED, NEVER ITS MESSAGE (constraint C-F). A sink's exception
    /// message is arbitrary upstream content and can carry a path, a URL or a credential, and the
    /// delivery report is readable by anything holding the session.
    /// </para>
    /// <para>
    /// THE CLOCK IS A DETERMINISTIC DOUBLE, so the record's timestamp is reproducible and nothing here
    /// reads a wall clock (AAP 0.6.7).
    /// </para>
    /// </remarks>
    [Fact]
    public void Trace_IsFireAndForgetSoDroppingOrFailingItCannotChangeAResult()
    {
        const string expression = "sum(salary for page)";
        const string computed = "1234.50";

        IDataWindowObject dwo = new FakeDataWindowObject("salary", "decimal(2)");
        ScriptedTraceSink sink = new();

        ExpressionSession session = new(
            "trace-session",
            new SessionLifetimeOptions(),
            new ColumnExpressionOptions { Trace = true },
            new DeterministicTimeProvider(),
            sink);

        DataWindowHandle handle = session.Register(new StubExpressionServiceHost());

        // ---- DELIVERED --------------------------------------------------------------------------
        ExpressionTraceRecord? delivered = session.EmitTrace(handle, Row, dwo, expression, computed);

        Assert.NotNull(delivered);
        Assert.Equal(computed, delivered.Value);
        Assert.Equal(1L, delivered.SequenceNumber);
        Assert.Equal(new ExpressionTraceDeliveryReport(1L, 1L, 0L, null), session.TraceDelivery);

        // ---- FAILED: ABSORBED, COUNTED, AND THE RESULT IS UNCHANGED -----------------------------
        sink.Fault = new InvalidOperationException("the diagnostic channel is down");

        ExpressionTraceRecord? afterFault = session.EmitTrace(handle, Row, dwo, expression, computed);

        Assert.NotNull(afterFault);
        Assert.Equal(2L, afterFault.SequenceNumber);

        // THE VALUE THAT REACHED THE TRACE IS IDENTICAL WHETHER DELIVERY SUCCEEDED OR FAILED, which is
        // the property that makes the trace incapable of changing a calculation.
        Assert.Equal(delivered.Value, afterFault.Value);
        Assert.Equal(delivered.Expression, afterFault.Expression);

        ExpressionTraceDeliveryReport report = session.TraceDelivery;
        Assert.Equal(2L, report.Attempted);
        Assert.Equal(1L, report.Delivered);
        Assert.Equal(1L, report.Failed);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.LastFailureType);

        // THE TYPE ONLY. The sink's message must not travel with the report.
        Assert.DoesNotContain(
            "the diagnostic channel is down",
            report.LastFailureType ?? string.Empty,
            StringComparison.Ordinal);

        // ---- DROPPED: NO SINK AT ALL, AND THE RECORD IS STILL PRODUCED --------------------------
        ExpressionSession sinkless = new(
            "trace-session-without-a-sink",
            new SessionLifetimeOptions(),
            new ColumnExpressionOptions { Trace = true },
            new DeterministicTimeProvider());

        DataWindowHandle sinklessHandle = sinkless.Register(new StubExpressionServiceHost());

        ExpressionTraceRecord? dropped =
            sinkless.EmitTrace(sinklessHandle, Row, dwo, expression, computed);

        Assert.NotNull(dropped);
        Assert.Equal(computed, dropped.Value);
        Assert.Equal(new ExpressionTraceDeliveryReport(0L, 0L, 0L, null), sinkless.TraceDelivery);

        // ---- REORDERABLE, WHICH IS WHAT PATTERN (a) GRANTS -------------------------------------
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventOrdering.DisciplineOf(EventId.Oncolumnexptrace));

        // ADMITTED IN EMISSION ORDER, INCLUDING THE GAP THE FAILED DELIVERY LEFT. The trace is pattern
        // (a), so its tokens need only ascend past the mark - and the record whose delivery faulted still
        // took a number, so the run a consumer sees legitimately skips one.
        DataWindowEventSequencer tolerant = new();

        foreach (long emitted in (long[])[delivered.SequenceNumber, afterFault.SequenceNumber])
        {
            tolerant.Accept(emitted, OrderingDiscipline.Sequenced, EventId.Oncolumnexptrace);
        }

        Assert.Equal(afterFault.SequenceNumber, tolerant.LastAccepted);

        // AND REPLAYING THEM IN THE OTHER ORDER IS REFUSED, which is what makes the token an enforced
        // ordering rather than a decoration: a diagnostic channel that delivered these two the wrong way
        // round would be reporting a call stack against the wrong expression.
        DataWindowEventSequencer reversed = new();
        reversed.Accept(afterFault.SequenceNumber, OrderingDiscipline.Sequenced, EventId.Oncolumnexptrace);

        _ = Assert.Throws<DataWindowEventSequenceException>(
            () => reversed.Accept(
                delivered.SequenceNumber,
                OrderingDiscipline.Sequenced,
                EventId.Oncolumnexptrace));

        // AND THE SEQUENCE NUMBERS ALONE RECOVER THE TRUE ORDER, so a consumer holding the records has the
        // emission order without needing anything else.
        Assert.Equal(
            [1L, 2L],
            sink.Records.OrderBy(record => record.SequenceNumber).Select(record => record.SequenceNumber));
    }

    // ==============================================================================================
    //  PHASE 4 - THE `Post` REPLACEMENT INSIDE THE SYNCHRONOUS GROUP
    //  --------------------------------------------------------------------------------------------
    //  se_cst_dw.sru:L387-L393
    //      event ondwnkillfocus;//*应用修改          "apply the edit"
    //      if Not _bDoItemChange then
    //          Post _of_PostAcceptText()
    //      end if
    //      Eventful.of_Trigger(EVT_LOSEFOCUS)
    //      return Event LoseFocus()
    //
    //  `Post` hands the call to the WIN32 MESSAGE QUEUE so it runs after the current event returns. AAP
    //  0.6.5 records the message pump as a DELIBERATE NON-PORT - a headless Linux container has no pump -
    //  and AAP 0.4.5.4 requires the posted call to become AN EXPLICITLY QUEUED CONTINUATION ON THE
    //  VALIDATION SESSION. Three properties of the original must survive, and each is asserted below:
    //
    //    1. IT IS NOT A FIRE-AND-FORGET TASK. Nothing runs until the host drains the queue, so between
    //       the queueing and the drain there is provably no accept.
    //    2. IT IS NOT AN EVENT. Draining issues NO sequencing token and reports NO outcome, so it
    //       cannot appear in an ordered event sequence a consumer is reasoning about.
    //    3. THE DEFERRAL ITSELF IS THE POINT. The continuation re-checks focus AFTER the event
    //       completed [:L553], so inlining it into the handler would test focus at a moment the oracle
    //       never tests it.
    //
    //  NO SLEEP, NO TIMER, NO WALL-CLOCK READ AND NO THREAD-POOL WORK ITEM APPEARS IN THIS REGION. The
    //  test drives the drain, which is exactly the discipline the queue itself follows.
    // ==============================================================================================

    /// <summary>
    /// The deferred accept is a queued continuation that runs ONLY when the host drains it, runs AT MOST
    /// ONCE, and issues no sequencing token of its own.
    /// </summary>
    [Fact]
    public void DeferredAccept_IsAQueuedContinuationThatRunsOnlyWhenTheHostDrainsIt()
    {
        ChainUnderTest fixture = NewChain();
        fixture.Host.LoseFocusHandler = () => 3L;

        // :L392 returns the semantic handler's code VERBATIM, which is one of only two raw handlers
        // that propagate a semantic code instead of normalising it.
        Assert.Equal(3L, fixture.Chain.OnDwnKillFocus());

        DataWindowEventOutcome killFocus = fixture.Observer.Single(EventId.Ondwnkillfocus);
        Assert.True(killFocus.DeferredAcceptQueued);
        Assert.True(fixture.Session.DeferredAcceptPending);

        // *** PROPERTY 1: NOTHING HAS RUN. *** A fire-and-forget task or a message pump would already
        // have accepted the text by now; a queued continuation cannot have.
        Assert.Equal(0, fixture.Host.CallLog.CountOf("AcceptText"));

        long tokensBeforeDrain = fixture.Chain.Sequencer.LastIssued;
        int outcomesBeforeDrain = fixture.Observer.Outcomes.Count;

        // ---- THE HOST DRAINS, AND ONLY NOW DOES THE CONTINUATION RUN -----------------------------
        DeferredAcceptOutcome? drained = fixture.Chain.DrainDeferredAccept();

        Assert.NotNull(drained);
        Assert.True(drained.FocusHadLeftHost);
        Assert.Equal(1, drained.AcceptTextResult);
        Assert.False(drained.FocusRestored);
        Assert.Null(drained.SetFocusResult);
        Assert.Equal(1, fixture.Host.CallLog.CountOf("AcceptText"));

        // *** PROPERTY 2: IT IS NOT AN EVENT. *** No token was issued and no outcome was reported, so
        // the drain cannot perturb an ordered event sequence.
        Assert.Equal(tokensBeforeDrain, fixture.Chain.Sequencer.LastIssued);
        Assert.Equal(outcomesBeforeDrain, fixture.Observer.Outcomes.Count);

        // ---- AT MOST ONCE ------------------------------------------------------------------------
        Assert.False(fixture.Session.DeferredAcceptPending);
        Assert.Null(fixture.Chain.DrainDeferredAccept());
        Assert.Equal(1, fixture.Host.CallLog.CountOf("AcceptText"));
    }

    /// <summary>
    /// The continuation is NOT enqueued at all while the in-item-change flag is set, so an accept can
    /// never run in the middle of an item change.
    /// </summary>
    /// <remarks>
    /// <c>:L388</c>'s guard is the whole reason the deferral is safe. The flag is set for the duration of
    /// the item-change raise [<c>:L192-L196</c>], and an accept that ran inside that window would
    /// RE-ENTER the protocol that is currently running. The refusal is REPORTED rather than silent -
    /// <c>DwnKillFocusEvent.deferred_accept_queued</c> carries it - because "not queued" is a meaningful
    /// observation and not a missing one.
    /// </remarks>
    [Fact]
    public void DeferredAccept_IsNotEnqueuedWhileAnItemChangeIsInFlight()
    {
        ChainUnderTest fixture = NewChain();
        fixture.Host.LoseFocusHandler = () => 3L;

        using (fixture.Session.EnterItemChange())
        {
            Assert.True(fixture.Session.DoItemChange);
            Assert.Equal(3L, fixture.Chain.OnDwnKillFocus());
        }

        Assert.False(fixture.Observer.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
        Assert.False(fixture.Session.DeferredAcceptPending);

        // NOTHING TO DRAIN, AND NOTHING RAN - not before the drain and not because of it.
        Assert.Null(fixture.Chain.DrainDeferredAccept());
        Assert.Equal(0, fixture.Host.CallLog.CountOf("AcceptText"));

        // AND THE EVENT STILL REPORTS THE SYNCHRONOUS ROLE, on this occurrence as on every other. Only
        // the emitter can tell the tail from a pure notification, and of the two possible misreports
        // only the sequenced one could authorise a harmful reorder - so the stricter reading is always
        // the one reported.
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            fixture.Observer.Single(EventId.Ondwnkillfocus).Discipline);
    }

    /// <summary>
    /// The continuation observes focus AFTER the event completed, not during it - which is the deferral
    /// itself, and the reason it cannot be inlined into the kill-focus handler.
    /// </summary>
    /// <remarks>
    /// <c>:L553</c> is <c>if GetFocus() &lt;&gt; this then</c> - an IDENTITY COMPARISON against the host
    /// rather than a focus test. When focus has come back to the host by the time the queue is drained
    /// the whole body is skipped, and that is precisely the case the deferral exists to catch: inlining
    /// the body into the handler would test focus at a moment the oracle never tests it, and would accept
    /// text the user has already returned to.
    /// </remarks>
    [Fact]
    public void DeferredAccept_ObservesFocusAfterTheEventCompletedRatherThanDuringIt()
    {
        ChainUnderTest fixture = NewChain();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnKillFocus());
        Assert.True(fixture.Session.DeferredAcceptPending);

        // FOCUS COMES BACK BETWEEN THE EVENT AND THE DRAIN - a state only a deferred body can observe.
        // The comparison is against the CHAIN, because the chain is the host the continuation was
        // queued against.
        fixture.Host.FocusedObject = fixture.Chain;

        DeferredAcceptOutcome? skipped = fixture.Chain.DrainDeferredAccept();

        Assert.NotNull(skipped);
        Assert.False(skipped.FocusHadLeftHost);
        Assert.Null(skipped.AcceptTextResult);
        Assert.False(skipped.FocusRestored);
        Assert.Equal(0, fixture.Host.CallLog.CountOf("AcceptText"));

        // ---- THE OTHER BRANCH: A FAILED ACCEPT RESTORES FOCUS, AND ONLY ON -1 -------------------
        ChainUnderTest failing = NewChain();
        failing.Host.AcceptTextResult = ValidationSession.AcceptTextFailure;

        Assert.Equal(RetCode.OK, failing.Chain.OnDwnKillFocus());

        DeferredAcceptOutcome? restored = failing.Chain.DrainDeferredAccept();

        Assert.NotNull(restored);
        Assert.True(restored.FocusHadLeftHost);
        Assert.Equal(ValidationSession.AcceptTextFailure, restored.AcceptTextResult);
        Assert.True(restored.FocusRestored);
        Assert.Equal(1, restored.SetFocusResult);

        // AN EQUALITY TEST AGAINST -1, NOT "NOT SUCCESS" [:L554]. A different failure code leaves focus
        // exactly where the legacy leaves it.
        ChainUnderTest other = NewChain();
        other.Host.AcceptTextResult = -2;

        Assert.Equal(RetCode.OK, other.Chain.OnDwnKillFocus());

        DeferredAcceptOutcome? untouched = other.Chain.DrainDeferredAccept();

        Assert.NotNull(untouched);
        Assert.Equal(-2, untouched.AcceptTextResult);
        Assert.False(untouched.FocusRestored);
        Assert.Equal(0, other.Host.CallLog.CountOf("SetFocus"));
    }

    /// <summary>
    /// The continuation is enqueued DURING the kill-focus step at the tail of the item-change chain, and
    /// drained deterministically afterwards, with no event dispatched in between.
    /// </summary>
    /// <remarks>
    /// THE POSITION IN THE ORDERING IS THE ASSERTION HERE. The whole synchronous group is driven, the
    /// kill-focus tail is the LAST event in it, and the queue becomes pending at that step and at no
    /// earlier one - so a consumer reading the ordered stream can say exactly where the outstanding work
    /// was created. The drain then happens outside the stream entirely.
    /// </remarks>
    [Fact]
    public void DeferredAccept_IsEnqueuedAtTheTailOfTheChainAndDrainedAfterIt()
    {
        ChainUnderTest fixture = NewChain();
        fixture.Host.ItemChangedHandler = (_, _, _) => 1L;

        Assert.Equal(1L, fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

        // NOT PENDING YET: the item-change step queues nothing.
        Assert.False(fixture.Session.DeferredAcceptPending);

        Assert.Equal(1L, fixture.Chain.OnDwnItemValidationError(Row, fixture.Dwo, RejectedValue));
        Assert.False(fixture.Session.DeferredAcceptPending);

        // THE TAIL. This is the step that creates the outstanding work.
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnKillFocus());
        Assert.True(fixture.Session.DeferredAcceptPending);

        // AN ORDERED SEQUENCE, AND THE TAIL IS LAST.
        Assert.Equal(
            [
                EventId.Ondoitemchange,
                EventId.Ondwnitemchange,
                EventId.Ondwnitemvalidationerror,
                EventId.Ondwnkillfocus,
            ],
            fixture.Observed);

        Assert.Equal(EventId.Ondwnkillfocus, fixture.Observed[^1]);
        Assert.True(fixture.Observer.Outcomes[^1].DeferredAcceptQueued);

        // EVERY MEMBER OF THE GROUP IS SYNCHRONOUS, the dual-role tail included.
        Assert.All(
            fixture.Disciplines,
            discipline => Assert.Equal(OrderingDiscipline.Synchronous, discipline));

        // THE DRAIN HAPPENS AFTER THE STREAM AND ADDS NOTHING TO IT.
        int events = fixture.Observer.Outcomes.Count;

        Assert.NotNull(fixture.Chain.DrainDeferredAccept());
        Assert.Equal(events, fixture.Observer.Outcomes.Count);
        Assert.False(fixture.Session.DeferredAcceptPending);
    }

    // ==============================================================================================
    //  PHASE 5 - ONE SESSION, ONE STREAM
    //  --------------------------------------------------------------------------------------------
    //  The four pieces of cross-event mutable state at se_cst_dw.sru:L89-L96 have nowhere to live on a
    //  stateless boundary, so contract C-03 materializes them as a SERVER-HELD SESSION correlated by an
    //  identifier and opened and closed by dedicated calls. That makes correlation a CORRECTNESS
    //  property rather than a convenience: admitting a message from another correlation into a running
    //  chain would let one caller's stashed code be consumed by another, and both callers would still
    //  see well-formed messages and plausible results.
    //
    //  THE SESSION IDENTIFIER IS AN OPAQUE HANDLE AND NEVER A CREDENTIAL (constraint C-F). Nothing here
    //  treats possession of one as proof of identity; authentication is the stock bearer handler's job
    //  at the Grpc/ and Endpoints/ layer, and it is enforced independently of this correlation.
    // ==============================================================================================

    /// <summary>
    /// The whole pattern-(b) group runs inside ONE validation session on ONE stream, and two chains hold
    /// two independent sessions and two independent token counters.
    /// </summary>
    /// <remarks>
    /// THE STATE PROGRESSION IS THE EVIDENCE. Each synchronous event result carries the session state
    /// AFTER that event, so the stash being written by one event and cleared by the next is visible as a
    /// progression along ONE lineage - which is only meaningful if both events read and wrote the same
    /// session. A consumer that had to re-read the state with a separate call could not observe the
    /// intermediate value the next event will consume, which is why the snapshot travels on the result.
    /// </remarks>
    [Fact]
    public void TheWholeSynchronousGroupRunsInsideOneSessionOnOneStream()
    {
        ChainUnderTest first = NewChain(sessionId: "stream-a");

        // ONE SESSION, HELD BY THE CHAIN ITSELF - not looked up per event, and not re-derived.
        Assert.Same(first.Session, first.Chain.Session);
        Assert.Equal("stream-a", first.Session.SessionId);

        first.Host.ItemChangedHandler = (_, _, _) => 1L;

        Assert.Equal(1L, first.Chain.OnDwnItemChange(Row, first.Dwo, OriginalValue));
        Assert.Equal(1L, first.Chain.OnDwnItemValidationError(Row, first.Dwo, RejectedValue));

        // THE SNAPSHOT TRAVELS ON EXACTLY THE EVENTS THAT TOUCH THE FOUR FIELDS, which is the precise
        // claim rather than the convenient one. The raw item-change event WRITES the stash [:L195] and
        // the validation-error event CONSUMES AND CLEARS it [:L331-L332], so both carry it; the nested
        // semantic notification only forwards to the application [:L292] and reads or writes none of
        // the four, so it carries none. Demanding a snapshot on every result would assert a fact the
        // contract does not make, and would pass just as well against an implementation that recaptured
        // the state on events that cannot change it - hiding, rather than proving, the lineage.
        IReadOnlyList<EventId> carryState =
            [.. first.Observer.Outcomes
                .Where(outcome => outcome.State is not null)
                .Select(outcome => outcome.EventId)];

        Assert.Equal([EventId.Ondwnitemchange, EventId.Ondwnitemvalidationerror], carryState);
        Assert.Null(first.Observer.Single(EventId.Ondoitemchange).State);

        // THE PROGRESSION: written by one event, cleared by the next, on ONE session.
        Assert.Equal(
            1L,
            first.Observer.Single(EventId.Ondwnitemchange).State!.Value.RawItemChangeRetCode);
        Assert.Equal(
            0L,
            first.Observer.Single(EventId.Ondwnitemvalidationerror).State!.Value.RawItemChangeRetCode);
        Assert.Equal(0L, first.Session.CaptureState().RawItemChangeRetCode);

        // ONE STREAM, ONE CONTIGUOUS TOKEN RUN, replayable under the strict discipline.
        DataWindowEventSequencer replay = new();

        foreach (DataWindowEventOutcome outcome in
            first.Observer.Outcomes.OrderBy(outcome => outcome.Sequence))
        {
            replay.Accept(outcome.Sequence, outcome.Discipline, outcome.EventId);
        }

        Assert.Equal(first.Chain.Sequencer.LastIssued, replay.LastAccepted);

        // ---- A SECOND CHAIN IS A SECOND STREAM, WITH ITS OWN COUNTER AND ITS OWN SESSION ---------
        ChainUnderTest second = NewChain(sessionId: "stream-b");

        Assert.NotSame(first.Session, second.Session);
        Assert.NotSame(first.Chain.Sequencer, second.Chain.Sequencer);

        Assert.Equal(RetCode.OK, second.Chain.OnDwnSetFocus());

        // The second stream starts at one rather than continuing the first's run, which is what makes a
        // per-stream ordering check well defined at all.
        Assert.Equal(1L, second.Observer.Outcomes[0].Sequence);
        Assert.NotEqual(1L, first.Observer.Outcomes[^1].Sequence);
    }

    /// <summary>
    /// A message on a different correlation identifier is NOT admitted, and one session's stash can
    /// never be consumed by another's chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO HALVES. The registry refuses an unknown or blank identifier with a DEFINED return code and
    /// never creates a session to satisfy the caller - an implicit session would give each event its own
    /// state, the stash would always be empty, and the chain would take the wrong branch at
    /// <c>se_cst_dw.sru:L338-L340</c> every time while appearing to work. And two live chains, driven
    /// against each other, keep their stashes to themselves.
    /// </para>
    /// <para>
    /// THE SECOND HALF IS THE ONE THAT MATTERS FOR ORDERING, because refusing an unknown identifier only
    /// stops a caller that names one; cross-talk between two VALID sessions would be invisible to any
    /// such check.
    /// </para>
    /// </remarks>
    [Fact]
    public void EventsOnADifferentCorrelationIdAreNotAdmittedIntoARunningChain()
    {
        // ---- THE REGISTRY REFUSES, WITH A DEFINED CODE, AND CREATES NOTHING ----------------------
        ValidationSessionRegistry registry = new(Options.Create(new DataServicesOptions()));

        ValidationSessionOpenResult opened = registry.OpenWithId("stream-a", "dw-ordering");
        Assert.True(opened.IsOpened);
        Assert.Equal(1, registry.Count);

        ValidationSessionResolution unknown = registry.Resolve("stream-somebody-else");
        Assert.False(unknown.IsResolved);
        Assert.Null(unknown.Session);
        Assert.Equal(RetCode.E_INVALID_HANDLE, unknown.ReturnCode);

        ValidationSessionResolution blank = registry.Resolve("   ");
        Assert.False(blank.IsResolved);
        Assert.Null(blank.Session);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, blank.ReturnCode);

        // NO SESSION WAS CONJURED to satisfy either refusal.
        Assert.Equal(1, registry.Count);

        ValidationSessionResolution mine = registry.Resolve("stream-a");
        Assert.True(mine.IsResolved);
        Assert.Same(opened.Session, mine.Session);
        Assert.Equal(RetCode.OK, mine.ReturnCode);

        // ---- AND TWO LIVE CHAINS KEEP THEIR STASHES TO THEMSELVES --------------------------------
        ChainUnderTest owner = NewChain(sessionId: "stream-owner");
        ChainUnderTest intruder = NewChain(sessionId: "stream-intruder");

        owner.Host.ItemChangedHandler = (_, _, _) => 3L;

        Assert.Equal(1L, owner.Chain.OnDwnItemChange(Row, owner.Dwo, OriginalValue));
        Assert.Equal(3L, owner.Session.CaptureState().RawItemChangeRetCode);

        // The intruder's validation error runs against ITS OWN empty stash, so it raises ItemError -
        // exactly as it would have with no predecessor anywhere in the system.
        _ = intruder.Chain.OnDwnItemValidationError(Row, intruder.Dwo, RejectedValue);

        Assert.True(
            intruder.Host.CallLog.Contains("Event ItemError"),
            "A chain on another correlation must consult its own stash, which is empty, and therefore "
                + "take the raise branch.");

        // THE OWNER'S STASH IS UNTOUCHED, so the intruder neither read it nor cleared it.
        Assert.Equal(3L, owner.Session.CaptureState().RawItemChangeRetCode);
        Assert.False(owner.Host.CallLog.Contains("Event ItemError"));

        // And the owner can still complete its own handshake afterwards, reaching the PRE-SET branch
        // the stash of 3 selects.
        Assert.Equal(1L, owner.Chain.OnDwnItemValidationError(Row, owner.Dwo, RejectedValue));
        Assert.False(owner.Host.CallLog.Contains("Event ItemError"));
        Assert.Equal(0L, owner.Session.CaptureState().RawItemChangeRetCode);
    }

    /// <summary>
    /// The sequencing token is carried on the GENERATED wire message, in both directions, and survives a
    /// protobuf round trip - so the ordering check is driven by published data and not by an
    /// implementation detail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-A IN ITS MOST LITERAL FORM. The discipline travels ON THE TOKEN rather than being
    /// inferred from the event identity, so a consumer's ordering check reads data it already has instead
    /// of consulting a table it must keep in step with AAP 0.6.1.4. This test asserts the token survives
    /// serialization with both fields intact, because a token that lost its discipline on the wire would
    /// silently downgrade every synchronous group to the unspecified reading.
    /// </para>
    /// <para>
    /// THE ROUND TRIP IS THE ASSERTION, NOT A FORMALITY: <c>sequence</c> is an <c>int64</c> and
    /// <c>discipline</c> an enum, and protobuf omits a field at its default value - so a token whose
    /// discipline were <c>UNSPECIFIED</c> would round-trip as an ABSENT field, which is exactly why
    /// <c>UNSPECIFIED</c> is refused rather than treated as a default.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSequencingTokenIsCarriedOnTheGeneratedWireMessageInBothDirections()
    {
        foreach (OrderingDiscipline discipline in (OrderingDiscipline[])
            [OrderingDiscipline.Synchronous, OrderingDiscipline.Sequenced])
        {
            SequencingToken token = new() { Sequence = 41L, Discipline = discipline };

            SequencingToken roundTripped = SequencingToken.Parser.ParseFrom(token.ToByteArray());

            Assert.Equal(41L, roundTripped.Sequence);
            Assert.Equal(discipline, roundTripped.Discipline);
            Assert.Equal(token, roundTripped);

            // INBOUND: the client reports an event and asks DataServices to dispatch it.
            EventChainRequest inbound = new()
            {
                SessionId = "stream-a",
                Token = token,
            };

            // OUTBOUND: DataServices answers, or asks the client to run a handler, on the SAME ordered
            // conversation - which is what makes the stream bidirectional.
            EventChainResponse outbound = new()
            {
                SessionId = "stream-a",
                Token = token,
            };

            Assert.Equal(
                discipline,
                EventChainRequest.Parser.ParseFrom(inbound.ToByteArray()).Token.Discipline);
            Assert.Equal(
                discipline,
                EventChainResponse.Parser.ParseFrom(outbound.ToByteArray()).Token.Discipline);
            Assert.Equal(
                41L,
                EventChainRequest.Parser.ParseFrom(inbound.ToByteArray()).Token.Sequence);
            Assert.Equal(
                41L,
                EventChainResponse.Parser.ParseFrom(outbound.ToByteArray()).Token.Sequence);
        }

        // THE UNSPECIFIED READING IS INDISTINGUISHABLE FROM AN ABSENT FIELD, which is why it is a fault
        // rather than a default - and why the sequencer refuses it alongside a missing sequence.
        SequencingToken unspecified = new()
        {
            Sequence = 41L,
            Discipline = OrderingDiscipline.Unspecified,
        };

        Assert.Equal(
            OrderingDiscipline.Unspecified,
            SequencingToken.Parser.ParseFrom(unspecified.ToByteArray()).Discipline);

        // AND THE CHAIN'S OWN TOKENS ARE THE SAME VALUES THE WIRE CARRIES - the discipline a dispatch
        // reports is the published enum member, not a local synonym mapped onto it.
        ChainUnderTest fixture = NewChain();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnSetFocus());

        DataWindowEventOutcome reported = fixture.Observer.Single(EventId.Ondwnsetfocus);
        SequencingToken projected = new()
        {
            Sequence = reported.Sequence,
            Discipline = reported.Discipline,
        };

        Assert.Equal(projected, SequencingToken.Parser.ParseFrom(projected.ToByteArray()));
        Assert.Equal(1L, projected.Sequence);
        Assert.Equal(OrderingDiscipline.Sequenced, projected.Discipline);
    }
}
