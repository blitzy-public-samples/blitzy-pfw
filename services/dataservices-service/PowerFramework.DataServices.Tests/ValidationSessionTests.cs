// ==================================================================================================
//  ValidationSessionTests - Domain/ValidationSession.cs AS A SESSION CONTAINER AND A LIFECYCLE
//  ------------------------------------------------------------------------------------------------
//  BEHAVIOURAL ORACLE   ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                         :L88-L96    the FOUR pieces of cross-event state this suite characterizes
//                         :L41-L43    EID_ROWFOCUSCHANGE / EID_ITEMFOCUSCHANGE / EID_ITEMCHANGE
//                         :L76        EVT_LOSEFOCUS, the topic ondwnkillfocus triggers
//                         :L187       the item-change gate the mask decides
//                         :L195       the STASH write the validation-error event later consumes
//                         :L331-L332  that consumption - a READ AND A CLEAR, which is why the stash
//                                     leaking across sessions would be a wrong ANSWER, not an error
//                         :L387-L393  ondwnkillfocus: the guarded `Post _of_PostAcceptText()`, then
//                                     the broker trigger, then `return Event LoseFocus()`
//                         :L537-L558  _of_postaccepttext, the posted continuation's body (:L553-L557)
//                       ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru
//                         the service base every attached service derives from, which is why the
//                         chain-level assertions below drive a fully assembled chain rather than a
//                         bare session
//                       ws_objects/pfw.shared.pbl.src/retcode.sru
//                         the return-code algebra the boundary-created lifecycle results answer with
//                       docs/PB多线程绕坑提示.md
//                         the legacy's own threading guidance, which is why NOTHING here schedules:
//                         the oracle's deferral rides the Win32 message queue, and a headless Linux
//                         container has no message pump to ride
//
//  The legacy tree is READ ONLY and is never an edit target (constraint C-C). It is the oracle these
//  tests characterize, and it is the ONLY specification: nothing else in the repository can adjudicate
//  a behavioural question here, so every assertion below carries the ws_objects/** locator it came
//  from (constraint C-K).
//
//  WHAT THIS SUITE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  Domain/ValidationSession.cs carries two separable subjects. This file is the FIRST of them:
//
//    * IT OWNS  the four fields as a CONTAINER - that they exist, that they start where a freshly
//               constructed control's do, and that there is no fifth piece of hidden state; the
//               server-held LIFECYCLE the network boundary created - open, use, close, reopen, refuse
//               and expire; the ISOLATION between two correlation identifiers; and the QUEUED
//               CONTINUATION that replaces the oracle's `Post`.
//
//    * IT DOES NOT OWN  the validation-error PROTOCOL at :L322-L385. Its eight steps - the
//               re-entrancy guard, the consume-and-clear, the pre-set from the stash, the ItemError
//               raise and its null coercion, the ValidationMsg strip boundary, the localized
//               fallback, the structured error's text/category/arguments/severity, and the tail
//               restore matrix - are characterized by the sibling validation-error suite in
//               ValidationSessionParityTests.cs. Not one of those assertions is repeated here. The
//               protocol is REACHED in exactly one place below, and only to prove an ISOLATION
//               property that cannot be shown any other way: that session A's stash is invisible to
//               session B, which is observable only by letting B consume its own stash.
//
//  WHY THE FOUR FIELDS ARE A SESSION AT ALL                                          (AAP 0.6.1.3)
//  ------------------------------------------------------------------------------------------------
//  In the oracle all four are private instance fields of ONE visual control, so their lifetime is the
//  control's and no identifier is needed to find them. A STATELESS REQUEST BOUNDARY HAS NOWHERE TO PUT
//  THEM, so they become fields of a server-held session correlated by an identifier and opened and
//  closed by dedicated calls - which is why contract C-03 declares OpenValidationSession and
//  CloseValidationSession and carries ValidationSessionState as a message. Every lifecycle assertion
//  below is therefore characterizing a BOUNDARY-CREATED obligation, and each is marked as such so a
//  reader never mistakes it for a legacy behaviour that could be looked up in the oracle.
//
//  GOVERNING CONSTRAINTS
//  ------------------------------------------------------------------------------------------------
//  NO USER RULES GOVERN THIS FILE. `review_rules` returns "No user rules provided." verbatim, so the
//  enterprise-standard baseline of AAP 0.7.2 applies in their place and no rule is inferred or
//  back-filled. The binding constraints are AAP 0.7.3's, and four bear on this file directly:
//
//    C-B  Legacy behaviour is preserved exactly, defects included. The four initial values, the
//         `if Not _bDoItemChange` guard and the deferral itself are asserted AS THE ORACLE HAS THEM,
//         never as a tidier design would have them.
//    C-A  No shared behaviour crosses a service boundary. Session state is server-held INSIDE
//         DataServices; the only thing that may leave is what dataservices.v1.proto declares, and
//         NothingBeyondTheDeclaredWireFieldsLeavesTheService pins exactly that.
//    C-H  This suite is the coverage of the session container and its lifecycle.
//    C-K  Every assertion cites its locator.
//
//  NAMING. SCREAMING_SNAKE identifiers are REFERENCED here - EventGate.EID_ITEMCHANGE,
//  RetCode.E_INVALID_HANDLE, DataWindowEventChain.EVT_LOSEFOCUS - and NONE is declared. The
//  repository .editorconfig suppresses CA1707 and IDE1006 per FILE, and its list names only the six
//  ported constant carriers; no test file is on it, and `TreatWarningsAsErrors` is on.
//
//  DETERMINISM                                                                         (AAP 0.6.7)
//  ------------------------------------------------------------------------------------------------
//  A characterization suite's one hard prerequisite is repeatability, so a non-deterministic value
//  must be masked from BOTH the master and the candidate. Consequently: every clock read goes through
//  TestDoubles.cs's DeterministicTimeProvider, so expiry is asserted with NO real waiting; the queued
//  continuation is drained WHEN THIS TEST SAYS SO and never by a timer; and the one concurrent test
//  synchronizes on task completion rather than on elapsed time. There is no Thread.Sleep, no
//  Task.Delay and no wall-clock tolerance anywhere in this file.
// ==================================================================================================
using System.Reflection;
using System.Runtime.CompilerServices;

// PowerFramework.Shared.Localization is DELIBERATELY NOT IMPORTED, and its absence is the visible
// consequence of the split at the top of this file. The localization facade reaches ValidationSession
// only on the validation-error protocol's message branch [se_cst_dw.sru:L355, :L357], and that branch
// belongs to the sibling suite. Every session built here is therefore constructed with no provider
// installed - the SILENT PASSTHROUGH state the oracle's own `if IsValid(n_cst_i18n)` guard describes
// [ws_objects/pfw.ui.pbl.src/i18n.srf:L17] - and no test below reads a translated string, so there is
// nothing here for that namespace to supply.
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;

using Xunit;

// The two contract types are ALIASED rather than imported by namespace.
// PowerFramework.Contracts.DataServices.V1 publishes types called EventGate and ItemChangeResult that
// collide by simple name with the two domain types this file uses, so importing that namespace whole
// would make every such reference CS0104. Naming only what is needed keeps the domain names unambiguous.
//
// EventId being a CONTRACT type rather than a domain one is worth noticing rather than working around:
// the event chain reports each dispatch under contract C-03's own event identity, so an assertion that
// names an event here is naming the same value the wire would carry.
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using WireValidationSessionState = PowerFramework.Contracts.DataServices.V1.ValidationSessionState;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterizes <c>Domain/ValidationSession.cs</c> as a session container and a lifecycle: the four
/// cross-event fields of <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L88-L96</c>, the
/// boundary-created open/use/close/expire cycle around them, the isolation between two correlation
/// identifiers, and the queued continuation that replaces the oracle's <c>Post</c> at <c>:L389</c>.
/// </summary>
public sealed class ValidationSessionTests
{
    /// <summary>The single column every fixture in this file carries.</summary>
    private const string ColumnName = "name";

    /// <summary>The value the fixture row holds before anything is edited.</summary>
    private const string OriginalValue = "original";

    /// <summary>The edit text every item-change and validation-error drive uses.</summary>
    private const string EditedValue = "changed";

    /// <summary>
    /// The ONE-BASED row every fixture operates on. Named rather than written as a literal because
    /// PowerBuilder rows are one-based and AAP 0.4.5.4 calls that translation the most dangerous
    /// mechanical hazard in the refactor; <c>1</c> here is a ROW NUMBER and never a .NET index.
    /// </summary>
    private const long FixtureRow = 1L;

    /// <summary>The first correlation identifier the isolation tests use.</summary>
    private const string SessionA = "session-a";

    /// <summary>The second correlation identifier the isolation tests use.</summary>
    private const string SessionB = "session-b";

    /// <summary>
    /// The four fields of <c>se_cst_dw.sru:L88-L96</c>, by their field name in the port, each paired
    /// with the oracle locator it came from.
    /// </summary>
    /// <remarks>
    /// Held as data rather than written into each assertion so that the set is stated ONCE. The
    /// no-fifth-state assertion partitions the type's declared fields against this list, so adding a
    /// field to the port without adding it here fails the build's own test rather than passing quietly.
    /// </remarks>
    private static readonly (string Field, string Locator)[] LegacyFields =
    [
        ("_disabledEvent", "se_cst_dw.sru:L88-L89  long _nDisabledEvent"),
        ("_doItemChange", "se_cst_dw.sru:L91-L92  boolean _bDoItemChange"),
        ("_inItemValidationError", "se_cst_dw.sru:L93-L94  boolean _bDwnItemValidationError"),
        ("_itemChangeRetCode", "se_cst_dw.sru:L95-L96  long _nItemChangeRetCode"),
    ];

    /// <summary>
    /// The fields the NETWORK BOUNDARY created, which have no counterpart in the oracle at all.
    /// </summary>
    /// <remarks>
    /// Enumerated explicitly for the same reason as <see cref="LegacyFields"/>: the point of the
    /// no-fifth-state assertion is that a reader can tell which of the type's fields the legacy
    /// justifies and which the boundary does. A field that is on neither list is state nobody asked
    /// for.
    /// </remarks>
    private static readonly string[] BoundaryCreatedFields =
    [
        "_gate",
        "_timeProvider",
        "_i18n",
        "_lastAccessedAt",
        "_isOpen",
        "_deferredAcceptPending",
    ];

    // ==============================================================================================
    //  FIXTURES
    //  --------------------------------------------------------------------------------------------
    //  Three seams and nothing else: the host, the clock and the options. No live DataWindow, no real
    //  waiting, no ambient state and no shared static anywhere - which is also what makes the
    //  isolation tests below meaningful rather than accidental.
    // ==============================================================================================

    /// <summary>
    /// Builds a one-column, one-row host with the cursor on <see cref="FixtureRow"/>.
    /// </summary>
    /// <returns>The host.</returns>
    private static FakeDataWindowHost NewHost()
    {
        FakeDataWindowHost host = new();
        host.AddColumn(ColumnName, FakeColumnType.CharOf(50));
        host.AddRow(OriginalValue);
        host.CurrentRow = FixtureRow;

        return host;
    }

    /// <summary>
    /// Builds options whose validation-session lifetime is exactly what a test asks for.
    /// </summary>
    /// <param name="idleTimeout">The idle timeout, or <see langword="null"/> for the shipped default.</param>
    /// <param name="maxConcurrentSessions">The ceiling, or <see langword="null"/> for the shipped default.</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// The lifetime is nested under <c>Sessions</c>, matching the configuration path
    /// <c>DataServices:Sessions:ValidationSession</c> the registry binds from. Writing it here rather
    /// than in each test is what lets a test say WHICH bound it is exercising.
    /// </remarks>
    private static DataServicesOptions NewOptions(
        TimeSpan? idleTimeout = null,
        int? maxConcurrentSessions = null)
    {
        SessionLifetimeOptions lifetime = new();

        if (idleTimeout.HasValue)
        {
            lifetime.IdleTimeout = idleTimeout.Value;
        }

        if (maxConcurrentSessions.HasValue)
        {
            lifetime.MaxConcurrentSessions = maxConcurrentSessions.Value;
        }

        DataServicesOptions options = new();
        options.Sessions.ValidationSession = lifetime;

        return options;
    }

    /// <summary>
    /// Builds a registry over a frozen clock - the entry point every lifecycle test uses.
    /// </summary>
    /// <param name="idleTimeout">The configured idle timeout, or <see langword="null"/> for the default.</param>
    /// <param name="maxConcurrentSessions">The configured ceiling, or <see langword="null"/> for the default.</param>
    /// <returns>The registry and the clock driving it.</returns>
    /// <remarks>
    /// The clock is <c>TestDoubles.cs</c>'s <see cref="DeterministicTimeProvider"/>, frozen until a test
    /// advances it. A suite that let real time reach an expiry decision would be asserting within a
    /// tolerance, and a tolerance is precisely what a golden-master comparison cannot express
    /// (AAP 0.6.7).
    /// </remarks>
    private static (ValidationSessionRegistry Registry, DeterministicTimeProvider Clock) NewRegistry(
        TimeSpan? idleTimeout = null,
        int? maxConcurrentSessions = null)
    {
        DeterministicTimeProvider clock = new();
        ValidationSessionRegistry registry =
            new(NewOptions(idleTimeout, maxConcurrentSessions), i18n: null, timeProvider: clock);

        return (registry, clock);
    }

    /// <summary>
    /// Builds a bare session directly, for the assertions that need no registry.
    /// </summary>
    /// <param name="sessionId">The correlation identifier.</param>
    /// <param name="mask">The seeded gate mask. Zero disables nothing, as a fresh control does.</param>
    /// <param name="clock">The clock seam, or <see langword="null"/> for a fresh frozen one.</param>
    /// <returns>The session.</returns>
    private static ValidationSession NewSession(
        string sessionId = SessionA,
        uint mask = 0u,
        TimeProvider? clock = null) =>
        new(
            sessionId,
            "dw-1",
            mask,
            new SessionLifetimeOptions(),
            i18n: null,
            timeProvider: clock ?? new DeterministicTimeProvider());

    /// <summary>
    /// Builds a fully assembled event chain over its own session and its own host.
    /// </summary>
    /// <param name="sessionId">The correlation identifier the chain's session carries.</param>
    /// <param name="mask">The seeded gate mask.</param>
    /// <returns>The chain and the observer recording what it dispatched.</returns>
    /// <remarks>
    /// A CHAIN AND NOT A BARE SESSION, because two of this file's subjects are only observable through
    /// one. The <c>Post</c> at <c>se_cst_dw.sru:L389</c> is issued by <c>ondwnkillfocus</c>, and the
    /// item-change gate at <c>:L187</c> is read on the way into the item-change protocol; asserting
    /// either against a session alone would assert the mechanism rather than the behaviour that uses
    /// it. Column and row setup happens after construction because the chain's constructor reads
    /// neither.
    /// </remarks>
    private static (FakeEventChain Chain, RecordingEventObserver Observer) NewChain(
        string sessionId = SessionA,
        uint mask = 0u)
    {
        RecordingEventObserver observer = new();
        FakeEventChain chain = new(NewSession(sessionId, mask), new FakeAttachedServiceFactory(), observer);

        chain.Host.AddColumn(ColumnName, FakeColumnType.CharOf(50));
        chain.Host.AddRow(OriginalValue);
        chain.Host.CurrentRow = FixtureRow;

        // Focus has LEFT the host, which is the state `if GetFocus() <> this` at :L553 tests for and the
        // only state in which the continuation's body does anything. Seeded here so a kill-focus drive
        // reaches the body rather than the skip.
        chain.Host.FocusedObject = null;

        // Seeding wrote to the call log. Cleared so every assertion below measures only what the drive
        // did, which is what makes "AcceptText has not run yet" a statement about the subject.
        chain.Host.CallLog.Clear();

        return (chain, observer);
    }

    /// <summary>
    /// Every instance field <see cref="ValidationSession"/> genuinely declares.
    /// </summary>
    /// <returns>The fields, ordered by name so a failure message is stable.</returns>
    /// <remarks>
    /// Auto-property backing fields are excluded by their <see cref="CompilerGeneratedAttribute"/>
    /// rather than by a name pattern, because the four read-only identity properties - the identifier,
    /// the DataWindow handle, the timeout and the creation instant - are not STATE the event chain
    /// mutates and are not what the oracle's four fields are.
    /// </remarks>
    private static IReadOnlyList<FieldInfo> DeclaredInstanceFields() =>
        [.. typeof(ValidationSession)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .OrderBy(field => field.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Asserts that a session's four legacy fields are all at the values a freshly constructed control
    /// would hold.
    /// </summary>
    /// <param name="session">The session to inspect.</param>
    /// <remarks>
    /// PowerScript gives a fresh instance <c>0</c>, <c>false</c>, <c>false</c> and <c>0</c> for the four
    /// declarations at <c>se_cst_dw.sru:L88-L96</c>, and the mask value <c>0</c> DISABLES NOTHING
    /// [<c>:L124</c>, <c>:L187</c> both test bits that are clear]. Asserted through every published
    /// route - the three accessors, the gate predicate and the snapshot - because a container that
    /// agreed with itself on one route and not another would be reporting state that does not exist.
    /// </remarks>
    private static void AssertFreshLegacyState(ValidationSession session)
    {
        // :L88-L89  long _nDisabledEvent - and zero disables nothing.
        Assert.Equal(0u, session.DisabledEvent);
        Assert.False(session.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
        Assert.False(session.IsEventDisabled(EventGate.EID_ITEMFOCUSCHANGE));
        Assert.False(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));

        // :L91-L92  boolean _bDoItemChange
        Assert.False(session.DoItemChange);

        // :L93-L94  boolean _bDwnItemValidationError
        Assert.False(session.InItemValidationError);

        // :L95-L96  long _nItemChangeRetCode - the stash, empty until :L195 writes it.
        Assert.Equal(0L, session.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, session.StashedItemChangeResult);

        // The one boundary-created piece of reportable state, for completeness: no continuation is
        // outstanding on a session nothing has used yet (decision D-6 in the subject file).
        Assert.False(session.DeferredAcceptPending);

        // And the same four, through the snapshot the boundary reports rather than through the
        // accessors, so the two routes are pinned to agree.
        ValidationSessionSnapshot snapshot = session.CaptureState();

        Assert.Equal(0L, snapshot.DisabledEventMask);
        Assert.False(snapshot.InItemChange);
        Assert.False(snapshot.InItemValidationError);
        Assert.Equal(ItemChangeResult.Default, snapshot.ItemChangeRetCode);
        Assert.Equal(0L, snapshot.RawItemChangeRetCode);
        Assert.False(snapshot.DeferredAcceptPending);
    }

    // ==============================================================================================
    //  PHASE 1 - THE FOUR FIELDS EXIST AND START AT THEIR LEGACY VALUES
    //  --------------------------------------------------------------------------------------------
    //  se_cst_dw.sru:L86-L97, verbatim and with the oracle's own comments:
    //
    //      /* 实现 */                                                  "implementation"
    //      private:
    //      //禁用的事件                                                "the disabled events"
    //      long _nDisabledEvent                                                         [:L88-L89]
    //
    //      //标志当前正在调用OnDoItemChange              "currently inside OnDoItemChange"
    //      boolean _bDoItemChange                                                       [:L91-L92]
    //      //标志当前正在调用OnDwnItemValidationError    "currently inside OnDwnItemValidationError"
    //      boolean _bDwnItemValidationError                                             [:L93-L94]
    //      //当ItemError事件由ItemChanged触发时使用的ItemChanged返回值
    //                    "the ItemChanged return value used when ItemError was triggered BY
    //                     ItemChanged"
    //      long _nItemChangeRetCode                                                     [:L95-L96]
    //
    //  These four exist as SESSION fields precisely because a stateless request boundary has nowhere
    //  else to put them (AAP 0.6.1.3). In the oracle they are instance fields of one visual control, so
    //  their lifetime is the control's and nothing has to name them; across a boundary they need both a
    //  home and a correlation identifier, and this is that home.
    // ==============================================================================================

    [Fact]
    public void TheFourLegacyFieldsAreDeclaredWithTheOraclesOwnTypesAndOrder()
    {
        IReadOnlyList<FieldInfo> declared = DeclaredInstanceFields();

        // The oracle declares `long`, `boolean`, `boolean`, `long`. PowerBuilder `long` is 32-bit, so
        // the mask ports as `uint` - the width Domain/EventGate.cs's bit primitives operate on - while
        // the stash ports as `long` because the wire carries it as int64 and because the oracle assigns
        // an UNCLASSIFIED handler return value to it at :L195, which may be any number at all.
        (string Field, Type Type)[] expected =
        [
            ("_disabledEvent", typeof(uint)),
            ("_doItemChange", typeof(bool)),
            ("_inItemValidationError", typeof(bool)),
            ("_itemChangeRetCode", typeof(long)),
        ];

        foreach ((string field, Type type) in expected)
        {
            FieldInfo? found = declared.FirstOrDefault(
                candidate => string.Equals(candidate.Name, field, StringComparison.Ordinal));

            Assert.NotNull(found);
            Assert.Equal(type, found.FieldType);

            // PRIVATE, exactly as the oracle's `private:` section at :L87 makes them. The two the
            // item-change protocol has to write reach it through IItemChangeSessionState rather than
            // through a public field, so the encapsulation survives the port.
            Assert.True(found.IsPrivate);
        }

        // Four, and the same four this suite's shared list names - so the list and the type cannot drift.
        Assert.Equal(4, expected.Length);
        Assert.Equal(
            LegacyFields.Select(entry => entry.Field).OrderBy(name => name, StringComparer.Ordinal),
            expected.Select(entry => entry.Field).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void AFreshlyConstructedSessionHoldsTheFourFieldsWhereAFreshControlHoldsThem()
    {
        // The oracle's control comes into existence with all four at PowerScript's defaults: :L88-L96
        // declares them with no initializer, so the mask is 0, both flags are false and the stash is 0.
        AssertFreshLegacyState(NewSession());
    }

    [Fact]
    public void EveryLegacyFieldIsReachableThroughAPublishedAccessorAndSurvivesARoundTrip()
    {
        ValidationSession session = NewSession();

        // ------------------------------------------------------------------------------------------
        // :L88-L89 - reached through the gate operations, which are the oracle's own of_disableevent
        //            [:L110] and of_enableevent [:L111]. The mask has no setter by design: the
        //            item-change protocol only ever TESTS it [:L187], and a setter would invite it to
        //            gate its own events, which the oracle never does.
        // ------------------------------------------------------------------------------------------
        Assert.Equal(RetCode.OK, session.DisableEvent(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, session.DisabledEvent);
        Assert.True(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal((int)RetCode.OK, session.EnableEvent(EventGate.EID_ITEMCHANGE));
        Assert.Equal(0u, session.DisabledEvent);

        // ------------------------------------------------------------------------------------------
        // :L91-L92 - read/write, because the oracle writes it at :L193 and restores it at :L196 and
        //            ondwnkillfocus READS it at :L388.
        // ------------------------------------------------------------------------------------------
        session.DoItemChange = true;
        Assert.True(session.DoItemChange);
        session.DoItemChange = false;
        Assert.False(session.DoItemChange);

        // ------------------------------------------------------------------------------------------
        // :L95-L96 - read/write, because :L195 stashes into it and :L331-L332 consumes and clears it.
        //            The typed view is COMPUTED from the raw value rather than stored beside it, so the
        //            two can never disagree.
        // ------------------------------------------------------------------------------------------
        session.ItemChangeRetCode = 3L;
        Assert.Equal(3L, session.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, session.StashedItemChangeResult);
        session.ItemChangeRetCode = 0L;
        Assert.Equal(ItemChangeResult.Default, session.StashedItemChangeResult);

        // ------------------------------------------------------------------------------------------
        // :L93-L94 - READ-ONLY from outside, and that is the point rather than an omission. The oracle
        //            writes it at exactly three sites, all inside `ondwnitemvalidationerror` [:L329,
        //            :L361, :L382], so the port gives it no setter and its re-entrant branch is
        //            reachable only the way the oracle reaches it. The flag is therefore observed here,
        //            not driven; what it does is the sibling validation-error suite's subject.
        // ------------------------------------------------------------------------------------------
        Assert.False(session.InItemValidationError);
        Assert.Null(typeof(ValidationSession)
            .GetProperty(
                nameof(ValidationSession.InItemValidationError),
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetMethod);
    }

    [Fact]
    public void TheSessionCarriesNoFifthPieceOfHiddenState()
    {
        // EXTRA STATE WOULD BE BEHAVIOUR THE LEGACY DOES NOT HAVE. The oracle's control has exactly four
        // cross-event fields [:L88-L96]; a fifth in the port would be a value the event chain could read
        // and write with no oracle to adjudicate what it should contain, which is the one thing a
        // characterization suite cannot check. So the type's declared fields are partitioned against two
        // explicit lists and anything on neither fails here.
        IReadOnlyList<string> declared =
            [.. DeclaredInstanceFields().Select(field => field.Name)];

        IReadOnlyList<string> permitted =
            [.. LegacyFields.Select(entry => entry.Field)
                .Concat(BoundaryCreatedFields)
                .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(permitted, declared);

        // Stated as two counts as well as one set, so a failure says WHICH half grew. Four is the
        // oracle's number and is fixed; six is the boundary's, and every one of them is justified in the
        // subject file's decisions D-6, D-7 and D-8 - the guard, the clock, the localization facade, the
        // last-accessed stamp, the open flag and the pending-continuation flag. None has a legacy
        // counterpart, and none is cross-event state the event chain reads.
        Assert.Equal(4, LegacyFields.Length);
        Assert.Equal(6, BoundaryCreatedFields.Length);
        Assert.Equal(10, declared.Count);
    }

    [Fact]
    public void NothingBeyondTheDeclaredWireFieldsLeavesTheService()
    {
        // CONSTRAINT C-A. Session state is server-held INSIDE DataServices, and the only part of it that
        // may cross the boundary is what shared/PowerFramework.Contracts/Proto/dataservices.v1.proto
        // declares on ValidationSessionState. The generated descriptor is the authority, so this reads
        // it rather than restating it.
        IReadOnlyList<string> wireFields =
            [.. WireValidationSessionState.Descriptor.Fields.InDeclarationOrder()
                .Select(field => field.Name)];

        Assert.Equal(
            new[]
            {
                "disabled_event_mask",
                "in_item_change",
                "in_item_validation_error",
                "item_change_ret_code",
                "deferred_accept_pending",
            },
            wireFields);

        // THE RAW STASH IS THE ONE PIECE OF REPORTABLE STATE WITH NO WIRE FIELD, AND THAT IS DELIBERATE.
        // The in-process snapshot carries both the CLASSIFIED stash and the raw number, because :L195
        // stashes a handler's return value before any dispatch has looked at it and a handler answering
        // -1 or 42 stashes -1 or 42. The wire carries only the classified value, so the raw number stays
        // inside the service. This asserts that asymmetry on purpose: a sixth wire field appearing here
        // would mean internal state had started to leak.
        Assert.Equal(5, wireFields.Count);
        Assert.DoesNotContain("raw_item_change_ret_code", wireFields);

        // The snapshot has one more member than the wire has fields, and the extra one is the raw stash.
        IReadOnlyList<string> snapshotMembers =
            [.. typeof(ValidationSessionSnapshot)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(wireFields.Count + 1, snapshotMembers.Count);
        Assert.Contains(nameof(ValidationSessionSnapshot.RawItemChangeRetCode), snapshotMembers);
    }

    // ==============================================================================================
    //  PHASE 2 - LIFECYCLE: OPEN, USE, CLOSE
    //  --------------------------------------------------------------------------------------------
    //  EVERY ASSERTION IN THIS SECTION CHARACTERIZES A BOUNDARY-CREATED OBLIGATION, NOT A LEGACY ONE,
    //  and saying so is not a disclaimer - it is what tells a reader why no ws_objects/** locator is
    //  cited for the lifecycle itself. In the oracle the four fields are born with the control and die
    //  with it: there is no open, no close, no identifier, no expiry and no refusal, because there is
    //  nothing to look one up FROM. A server-held session has to have all five, so contract C-03
    //  declares OpenValidationSession and CloseValidationSession and this suite pins what they do.
    //
    //  The oracle still governs ONE thing here, and it is the thing that matters most: WHAT A FRESHLY
    //  OPENED SESSION CONTAINS. That is se_cst_dw.sru:L88-L96's four defaults, and every open and
    //  reopen below is checked against them.
    // ==============================================================================================

    [Fact]
    public void OpeningASessionYieldsACorrelationIdentifierAndTheFourFieldsAtTheirInitialValues()
    {
        (ValidationSessionRegistry registry, DeterministicTimeProvider clock) = NewRegistry();

        ValidationSessionOpenResult opened = registry.Open("dw-1");

        Assert.True(opened.IsOpened);
        Assert.Equal(RetCode.OK, opened.ReturnCode);

        ValidationSession session = Assert.IsType<ValidationSession>(opened.Session);

        // A CORRELATION IDENTIFIER, which every EventChain message must then carry. It is an opaque
        // handle and never a credential: nothing in the session treats possession of one as proof of
        // identity, and authentication is enforced at the transport layer by the stock bearer handler
        // (constraint C-G).
        Assert.NotEmpty(session.SessionId);
        Assert.Equal(ValidationSessionRegistry.GeneratedSessionIdLength, session.SessionId.Length);
        Assert.Equal("dw-1", session.DataWindowHandle);

        // AND THE FOUR FIELDS WHERE THE ORACLE'S FRESH CONTROL HAS THEM [se_cst_dw.sru:L88-L96]. This is
        // the whole point of the open: a caller receives a chain whose cross-event state has not been
        // touched, so the first item change it drives behaves exactly as the first one on a new control.
        AssertFreshLegacyState(session);

        Assert.True(session.IsOpen);
        Assert.Equal(1, registry.Count);

        // The creation instant came from the injected clock and not from the machine's, so a recording
        // taken here is reproducible (AAP 0.6.7).
        Assert.Equal(clock.Instant, session.CreatedAt);
        Assert.Equal(session.CreatedAt, session.LastAccessedAt);
    }

    [Fact]
    public void WritesThroughASessionAreVisibleToEveryLaterReadOfTheSameCorrelationIdentifier()
    {
        (ValidationSessionRegistry registry, _) = NewRegistry();
        Assert.True(registry.OpenWithId(SessionA).IsOpened);

        // Write through one resolution: the gate mask [:L88-L89], the in-item-change flag [:L91-L92] and
        // the stash [:L95-L96]. All three are values a LATER event has to see - :L388 reads the flag and
        // :L331 reads the stash - so a resolution that handed out a copy would silently lose them.
        ValidationSessionResolution first = registry.Resolve(SessionA);

        Assert.True(first.IsResolved);
        ValidationSession writable = Assert.IsType<ValidationSession>(first.Session);

        Assert.Equal(RetCode.OK, writable.DisableEvent(EventGate.EID_ITEMCHANGE));
        writable.DoItemChange = true;
        writable.ItemChangeRetCode = 3L;
        Assert.True(writable.TryQueueDeferredAccept() is false);

        // Read through a SECOND, independent resolution of the same identifier.
        ValidationSessionResolution second = registry.Resolve(SessionA);

        Assert.True(second.IsResolved);
        ValidationSession readable = Assert.IsType<ValidationSession>(second.Session);

        // THE SAME SESSION, not an equal one. Identity is the mechanism that makes the four fields
        // cross-event state at all; a resolution returning a snapshot-shaped copy would satisfy every
        // value assertion below on the first read and none of them on the next event.
        Assert.Same(writable, readable);

        Assert.True(readable.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, readable.DisabledEvent);
        Assert.True(readable.DoItemChange);
        Assert.Equal(3L, readable.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, readable.StashedItemChangeResult);

        // And through the snapshot the boundary reports, which must agree with the accessors.
        ValidationSessionSnapshot snapshot = readable.CaptureState();

        Assert.Equal((long)EventGate.EID_ITEMCHANGE, snapshot.DisabledEventMask);
        Assert.True(snapshot.InItemChange);
        Assert.Equal(3L, snapshot.RawItemChangeRetCode);
    }

    [Fact]
    public void ClosingASessionDiscardsItsStateAndReopeningTheSameIdentifierStartsFresh()
    {
        (ValidationSessionRegistry registry, _) = NewRegistry();
        Assert.True(registry.OpenWithId(SessionA).IsOpened);

        ValidationSession before = Assert.IsType<ValidationSession>(registry.Resolve(SessionA).Session);

        Assert.Equal(RetCode.OK, before.DisableEvent(EventGate.EID_ROWFOCUSCHANGE));
        before.DoItemChange = true;
        before.ItemChangeRetCode = 1L;
        Assert.True(before.TryQueueDeferredAccept() is false);

        ValidationSessionCloseResult closed = registry.Close(SessionA);

        Assert.Equal(RetCode.OK, closed.ReturnCode);
        Assert.True(closed.WasOpen);
        Assert.Equal(0, registry.Count);
        Assert.False(before.IsOpen);

        // THE CLOSE REPORTS THE STATE AT THE MOMENT OF CLOSURE RATHER THAN ZEROING IT, so a caller can
        // still see that an item change was in flight when the session went away. Zeroing would destroy
        // the evidence the response exists to deliver.
        Assert.Equal((long)EventGate.EID_ROWFOCUSCHANGE, closed.FinalState.DisabledEventMask);
        Assert.True(closed.FinalState.InItemChange);
        Assert.Equal(1L, closed.FinalState.RawItemChangeRetCode);

        // Reopening the SAME identifier yields a DIFFERENT session at the oracle's four defaults. The
        // identifier is reusable precisely because closure released it; what must not survive is the
        // STATE, because a stash carried over would let the next chain's validation-error event take a
        // branch decided by an edit that is no longer on screen [:L331-L332, :L338].
        ValidationSessionOpenResult reopened = registry.OpenWithId(SessionA);

        Assert.True(reopened.IsOpened);
        ValidationSession after = Assert.IsType<ValidationSession>(reopened.Session);

        Assert.NotSame(before, after);
        Assert.Equal(SessionA, after.SessionId);
        AssertFreshLegacyState(after);

        // The values written before the close are unreachable: not through the new session, and not
        // through the old handle either, which is closed and refuses to be used or drained.
        Assert.False(after.DoItemChange);
        Assert.Equal(ValidationSessionAcquisition.Closed, before.Acquire());
        Assert.Null(before.DrainDeferredAccept(NewHost()));
    }

    /// <summary>
    /// The five shapes of correlation identifier that cannot be used, each with the DEFINED code the
    /// implementation answers with.
    /// </summary>
    /// <returns>The scenario label and its expected return code.</returns>
    /// <remarks>
    /// THE THREE CODES ARE DISTINGUISHED ON PURPOSE. A malformed request is not the same fault as a
    /// handle that was never valid, and neither is the same as one that was valid and has since gone -
    /// only the last is worth retrying with a fresh open. Which of the two refusal codes a closed
    /// session produces depends on HOW it was closed, and that is a real distinction rather than
    /// looseness: a registry close REMOVES the entry, so a later resolution finds nothing at all,
    /// whereas a close performed on the session itself leaves the entry in place to be refused by the
    /// acquisition.
    /// </remarks>
    public static TheoryData<string, long> UnusableCorrelationIdentifiers() => new()
    {
        { "blank", RetCode.E_INVALID_ARGUMENT },
        { "whitespace", RetCode.E_INVALID_ARGUMENT },
        { "never-registered", RetCode.E_INVALID_HANDLE },
        { "closed-through-the-registry", RetCode.E_INVALID_HANDLE },
        { "closed-on-the-session", RetCode.E_NOT_EXISTS },
    };

    [Theory]
    [MemberData(nameof(UnusableCorrelationIdentifiers))]
    public void AnUnusableCorrelationIdentifierIsRefusedWithADefinedCodeAndNeverCreatesASession(
        string scenario,
        long expectedReturnCode)
    {
        (ValidationSessionRegistry registry, _) = NewRegistry();

        string identifier = scenario switch
        {
            "blank" => string.Empty,
            "whitespace" => "   ",
            "never-registered" => "no-such-session",
            _ => SessionA,
        };

        if (scenario is "closed-through-the-registry")
        {
            Assert.True(registry.OpenWithId(identifier).IsOpened);
            Assert.True(registry.Close(identifier).WasOpen);
        }
        else if (scenario is "closed-on-the-session")
        {
            ValidationSessionOpenResult opened = registry.OpenWithId(identifier);
            Assert.True(opened.IsOpened);

            // Closed on the SESSION, so the registry still holds the entry and the refusal has to come
            // from the acquisition rather than from a failed lookup.
            Assert.True(Assert.IsType<ValidationSession>(opened.Session).Close());
        }

        int countBeforeUse = registry.Count;

        ValidationSessionResolution resolution = registry.Resolve(identifier);

        // A DEFINED ERROR. Compared against the exact constant rather than through a success predicate,
        // because RetCode has a tri-state hole in which PREVENT reads as a success and CANCELLED is
        // neither [ws_objects/pfw.shared.pbl.src/retcode.sru; issucceeded.srf:L11-L13].
        Assert.Equal(expectedReturnCode, resolution.ReturnCode);
        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.Session);

        // NEVER A SILENT CREATE, which is the property that actually matters. A registry that answered a
        // stranger's identifier by minting a session would hand it four fields at their defaults while
        // the chain that identifier belongs to had already moved on: the stash would read 0, so
        // :L338's `if nItemChangeRetCode = 1 or nItemChangeRetCode = 3` would be false and the
        // validation-error event would raise ItemError where the oracle would have pre-set 1. That is a
        // WRONG ANSWER rather than an error, and this is the invariant that forbids it - THE POPULATION
        // NEVER GROWS ON A REFUSAL.
        Assert.True(
            registry.Count <= countBeforeUse,
            "A refused identifier must never add a session to the registry.");

        // AND IT MAY SHRINK, WHICH IS NOT A CONTRADICTION BUT THE SECOND HALF OF THE SAME DESIGN. Only
        // the closed-on-the-session scenario still had an entry when the refusal arrived, because closing
        // through the registry REMOVES the entry while closing on the session does not. A refusal that
        // finds a registered-but-dead session closes and releases it on the way out, so a caller cannot
        // revive one by holding its identifier and an abandoned entry cannot accumulate. Either way the
        // population afterwards is zero: nothing was created, and nothing dead was left behind.
        Assert.Equal(0, registry.Count);

        // The try-shaped route has to agree with the resolving one, or a call site could be refused by
        // one and served by the other.
        Assert.False(registry.TryGet(identifier, out ValidationSession? viaTryGet));
        Assert.Null(viaTryGet);
        Assert.Equal(0, registry.Count);

        // Nothing was left behind: a well-formed identifier is still available to be opened afterwards,
        // and what it opens is a session at the oracle's four defaults [se_cst_dw.sru:L88-L96] rather
        // than anything the refused attempt might have half-built.
        if (scenario is not ("blank" or "whitespace"))
        {
            ValidationSessionOpenResult reopened = registry.OpenWithId(identifier);

            Assert.True(reopened.IsOpened);
            Assert.Equal(1, registry.Count);
            AssertFreshLegacyState(Assert.IsType<ValidationSession>(reopened.Session));
        }
    }

    [Fact]
    public void ClosingTwiceIsIdempotentRatherThanAnError()
    {
        // NO ws_objects/** LOCATOR EXISTS FOR THIS, AND SAYING SO IS THE CITATION (constraint C-K).
        // se_cst_dw.sru:L88-L96 declares the four fields with no lifetime of their own - they are born
        // with the control and die with it - so the oracle has no close to reproduce and cannot adjudicate
        // what closing twice should do. The authority is therefore contract C-03's
        // CloseValidationSessionResponse in shared/PowerFramework.Contracts/Proto/dataservices.v1.proto,
        // which declares `was_open` precisely so the answer can report WHICH case it was without changing
        // `ret_code`.
        //
        // PINNED, NOT ACCEPTED EITHER WAY. Both designs are defensible and the implementation chose
        // idempotence, for a reason worth restating: a caller that could not safely retry a close would
        // leak a session on any transport hiccup. This test exists so that flipping to a defined error
        // becomes a deliberate contract change instead of a quiet one.
        (ValidationSessionRegistry registry, _) = NewRegistry();
        Assert.True(registry.OpenWithId(SessionA).IsOpened);

        ValidationSessionCloseResult first = registry.Close(SessionA);
        ValidationSessionCloseResult second = registry.Close(SessionA);

        Assert.Equal(RetCode.OK, first.ReturnCode);
        Assert.Equal(RetCode.OK, second.ReturnCode);

        // The code is the SAME both times; only `was_open` differs, and a false value there is
        // informational rather than a failure.
        Assert.True(first.WasOpen);
        Assert.False(second.WasOpen);

        // A second close reports no final state, because there was no session to take one from.
        Assert.Equal(default, second.FinalState);

        // The SESSION's own close is idempotent too, and reports it differently: a boolean rather than a
        // code. Pinned alongside the registry's so the two answers cannot be confused.
        ValidationSession session = Assert.IsType<ValidationSession>(registry.OpenWithId(SessionB).Session);

        Assert.True(session.Close());
        Assert.False(session.Close());
        Assert.False(session.IsOpen);

        // E_INVALID_ARGUMENT is reserved for a MALFORMED request and is never how an already-closed
        // session is reported - that distinction is the whole reason closing twice is not an error.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Close("   ").ReturnCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Close(null).ReturnCode);
    }

    [Fact]
    public void TheConfiguredSessionLifetimeReachesEverySessionTheRegistryOpens()
    {
        // CONFIGURATION AND NEVER A LITERAL (constraint C-F). The lifetime lives at
        // DataServices:Sessions:ValidationSession, and there is NO ws_objects/** LOCATOR TO CITE because
        // there is no legacy number to reproduce: se_cst_dw.sru:L88-L96 declares the four fields as plain
        // instance fields of a control, so they expire when it is destroyed and never on a timeout. Both
        // bounds are consequently boundary-created (AAP 0.6.1.3), and the only thing to assert is that the
        // CONFIGURED value is the one actually in force, end to end - a compiled-in default silently
        // winning here would make every expiry assertion in this suite measure the wrong bound.
        TimeSpan configured = TimeSpan.FromMinutes(7);
        (ValidationSessionRegistry registry, _) = NewRegistry(configured, maxConcurrentSessions: 3);

        Assert.Equal(configured, registry.IdleTimeout);
        Assert.Equal(3, registry.MaxConcurrentSessions);

        ValidationSession session = Assert.IsType<ValidationSession>(registry.OpenWithId(SessionA).Session);

        // The session carries the SAME value the registry was configured with. A default leaking in here
        // would make every expiry assertion below measure the wrong bound.
        Assert.Equal(configured, session.IdleTimeout);
        Assert.NotEqual(new SessionLifetimeOptions().IdleTimeout, session.IdleTimeout);
    }

    /// <summary>
    /// The idle-expiry boundary, in ticks either side of the configured timeout.
    /// </summary>
    /// <returns>The tick offset from the timeout, and whether the session has expired at it.</returns>
    /// <remarks>
    /// THE COMPARISON IS STRICTLY GREATER THAN, so a session idle for EXACTLY its timeout is still
    /// usable and one tick more is not. Asserted at the boundary rather than well past it because an
    /// inclusive comparison would pass a coarse test and reclaim live sessions in production. The offsets
    /// are ticks - the finest unit the clock has - so nothing here depends on resolution.
    /// </remarks>
    public static TheoryData<long, bool> IdleExpiryBoundary() => new()
    {
        { -1L, false },
        { 0L, false },
        { 1L, true },
    };

    [Theory]
    [MemberData(nameof(IdleExpiryBoundary))]
    public void IdleExpiryIsDecidedEntirelyByTheInjectedClock(long tickOffset, bool expectExpired)
    {
        TimeSpan configured = TimeSpan.FromMinutes(2);
        (ValidationSessionRegistry registry, DeterministicTimeProvider clock) = NewRegistry(configured);

        ValidationSession session = Assert.IsType<ValidationSession>(registry.OpenWithId(SessionA).Session);

        // NO REAL WAITING ANYWHERE. Two minutes of idleness is expressed as one call on the clock double,
        // which is what makes the assertion reproducible rather than merely likely (AAP 0.6.7).
        clock.Advance(configured + TimeSpan.FromTicks(tickOffset));

        Assert.Equal(expectExpired, session.HasExpired());

        // The sweep and the point of use have to reach the same verdict from the same clock reading, or a
        // session could be reclaimed by one and served by the other.
        Assert.Equal(expectExpired ? 1 : 0, registry.SweepExpired());
        Assert.Equal(expectExpired ? 0 : 1, registry.Count);

        ValidationSessionResolution resolution = registry.Resolve(SessionA);

        if (expectExpired)
        {
            Assert.False(resolution.IsResolved);
            Assert.Equal(RetCode.E_INVALID_HANDLE, resolution.ReturnCode);
            Assert.False(session.IsOpen);
        }
        else
        {
            Assert.True(resolution.IsResolved);
            Assert.Same(session, resolution.Session);

            // A resolution RECORDS ACTIVITY, so the session that was one tick from expiry is now a full
            // timeout away from it. Reclamation therefore tracks idleness rather than age, which is the
            // difference between expiring an abandoned session and expiring a busy one.
            Assert.Equal(clock.Instant, session.LastAccessedAt);
            Assert.False(session.HasExpired());
        }
    }

    // ==============================================================================================
    //  PHASE 3 - ISOLATION BETWEEN SESSIONS
    //  --------------------------------------------------------------------------------------------
    //  In the oracle the four fields are instance fields of ONE control, so two DataWindows on one
    //  window each have their own copy and no mechanism exists by which either could read the other's.
    //  A server-held store has to reproduce that, and it is the property most easily lost: a single
    //  static, a shared default, a cached snapshot or a registry keyed on something coarser than the
    //  correlation identifier would all compile, pass every single-session test in this file, and then
    //  attribute one caller's half-finished edit to another.
    //
    //  THE STASH MAKES THIS THE MOST CONSEQUENTIAL SECTION IN THE SUITE. `ondwnitemvalidationerror`
    //  does not merely read it - it READS AND CLEARS it [:L331-L332] and then PRE-SETS its own result
    //  from it [:L338-L340]. A leaked stash therefore does not surface as an exception or a wrong
    //  field; it surfaces as the validation-error event taking the wrong BRANCH, which produces a
    //  plausible answer that is simply not the oracle's.
    // ==============================================================================================

    [Fact]
    public void DisablingAnEventInOneSessionLeavesTheOthersMaskAloneAndItsHandlersRunning()
    {
        (FakeEventChain chainA, RecordingEventObserver observerA) = NewChain(SessionA);
        (FakeEventChain chainB, RecordingEventObserver observerB) = NewChain(SessionB);

        long ranA = 0L;
        long ranB = 0L;
        chainA.Host.ItemChangedHandler = (_, _, _) =>
        {
            ranA++;
            return RetCode.PREVENT;
        };
        chainB.Host.ItemChangedHandler = (_, _, _) =>
        {
            ranB++;
            return RetCode.PREVENT;
        };

        // Disable the item-change event on A ONLY - the oracle's of_disableevent [:L110, body :L506-L510]
        // with EID_ITEMCHANGE [:L43], whose own comment notes that disabling it also stops column
        // expressions from calculating.
        Assert.Equal(RetCode.OK, chainA.Session.DisableEvent(EventGate.EID_ITEMCHANGE));

        // THE MASKS ARE PER SESSION. This is the assertion a shared static would fail first.
        Assert.True(chainA.Session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.False(chainB.Session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, chainA.Session.DisabledEvent);
        Assert.Equal(0u, chainB.Session.DisabledEvent);

        long resultA = chainA.OnDwnItemChange(FixtureRow, chainA.Host.DwObject(ColumnName), EditedValue);
        long resultB = chainB.OnDwnItemChange(FixtureRow, chainB.Host.DwObject(ColumnName), EditedValue);

        // A IS GATED OUT AT :L187 - `if BitTest(_nDisabledEvent,EID_ITEMCHANGE) then return 0` - so its
        // semantic handler never runs and its stash is never written [:L195 is past the early return].
        Assert.Equal((long)ItemChangeResult.Default, resultA);
        Assert.Equal(0L, ranA);
        Assert.Equal(0L, chainA.Session.ItemChangeRetCode);
        Assert.True(observerA.Single(EventId.Ondwnitemchange).Dispatch.GatedOut);

        // B'S HANDLERS STILL RUN, which is the other half of isolation and the half a naive "one mask for
        // the process" implementation would break silently.
        Assert.Equal(1L, ranB);
        Assert.False(observerB.Single(EventId.Ondwnitemchange).Dispatch.GatedOut);
        Assert.Equal(RetCode.PREVENT, resultB);

        // B stashed its own handler's code [:L195] while A stashed nothing at all.
        Assert.Equal(RetCode.PREVENT, chainB.Session.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.TriggerValidationError, chainB.Session.StashedItemChangeResult);
        Assert.Equal(ItemChangeResult.Default, chainA.Session.StashedItemChangeResult);

        // Enabling on B leaves A's mask exactly where A left it - isolation holds in both directions.
        Assert.Equal((int)RetCode.OK, chainB.Session.EnableEvent(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, chainA.Session.DisabledEvent);
    }

    [Fact]
    public void TheStashedItemChangeCodeInOneSessionIsInvisibleToAnother()
    {
        FakeDataWindowHost hostA = NewHost();
        FakeDataWindowHost hostB = NewHost();
        ValidationSession sessionA = NewSession(SessionA);
        ValidationSession sessionB = NewSession(SessionB);

        // ItemError answers a NON-ZERO code on both hosts, so step 7's message branch is skipped entirely
        // [:L347 `if rtCode = 0 then`]. That keeps this test on the isolation property and off the
        // validation-error protocol, which the sibling suite owns.
        hostA.ItemErrorHandler = (_, _, _) => RetCode.PREVENT;
        hostB.ItemErrorHandler = (_, _, _) => RetCode.PREVENT;

        // A stashes, exactly as :L195 would after its item-change handler returned 1.
        sessionA.ItemChangeRetCode = RetCode.PREVENT;

        // B RUNS ITS OWN VALIDATION-ERROR FLOW AND CONSUMES ZERO, NOT A'S ONE. This is the single most
        // consequential isolation property in the suite: :L331-L332 reads and clears the stash and
        // :L338-L340 pre-sets the result from it, so a leaked value would make B pre-set 1 and skip its
        // ItemError raise - the oracle's behaviour for a DIFFERENT DataWindow's edit.
        ValidationErrorOutcome outcomeB =
            sessionB.OnDwnItemValidationError(hostB, FixtureRow, hostB.DwObject(ColumnName), EditedValue);

        Assert.Equal(0L, outcomeB.StashedRawItemChangeRetCode);
        Assert.False(outcomeB.PreSetFromStash);
        Assert.True(outcomeB.ItemErrorRaised);

        // B's consume-and-clear did not reach into A: A still holds its own stash, unread and uncleared.
        Assert.Equal(RetCode.PREVENT, sessionA.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.TriggerValidationError, sessionA.StashedItemChangeResult);
        Assert.Equal(0L, sessionB.ItemChangeRetCode);

        // And when A finally runs its own flow it consumes ITS value - so the stash was not merely absent
        // from B, it was present in A the whole time.
        ValidationErrorOutcome outcomeA =
            sessionA.OnDwnItemValidationError(hostA, FixtureRow, hostA.DwObject(ColumnName), EditedValue);

        Assert.Equal(RetCode.PREVENT, outcomeA.StashedRawItemChangeRetCode);
        Assert.True(outcomeA.PreSetFromStash);
        Assert.False(outcomeA.ItemErrorRaised);

        // Both stashes are now clear, each cleared by its own consumer and neither by the other's.
        Assert.Equal(0L, sessionA.ItemChangeRetCode);
        Assert.Equal(0L, sessionB.ItemChangeRetCode);
    }

    [Fact]
    public void TheTwoReEntrancyFlagsArePerSessionSoOneSessionCannotSuppressAnothersContinuation()
    {
        (FakeEventChain chainA, RecordingEventObserver observerA) = NewChain(SessionA);
        (FakeEventChain chainB, RecordingEventObserver observerB) = NewChain(SessionB);

        // A enters its item-change handler - the save-set-restore dance of :L192-L196. While that scope is
        // open, A's :L388 guard is closed and B's is not.
        using (chainA.Session.EnterItemChange())
        {
            Assert.True(chainA.Session.DoItemChange);
            Assert.False(chainB.Session.DoItemChange);

            // :L387-L390 on BOTH chains, inside A's scope.
            _ = chainA.OnDwnKillFocus();
            _ = chainB.OnDwnKillFocus();
        }

        // A QUEUED NOTHING, because `if Not _bDoItemChange` [:L388] was false for A.
        Assert.False(observerA.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
        Assert.False(chainA.Session.DeferredAcceptPending);

        // B QUEUED, because B's own flag was clear. A nested item change in one session must not make
        // another session's kill-focus path skip its continuation - that would silently drop an edit
        // belonging to a DataWindow the first session has never heard of.
        Assert.True(observerB.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
        Assert.True(chainB.Session.DeferredAcceptPending);

        // The restore at :L196 put A's flag back to false, so A can queue on its NEXT kill focus - the
        // suppression was scoped to the item change and not to the session.
        Assert.False(chainA.Session.DoItemChange);

        // Draining reaches only the host whose session queued. A's host never sees an accept at all.
        Assert.NotNull(chainB.DrainDeferredAccept());
        Assert.Equal(1, chainB.Host.CallLog.CountOf("AcceptText"));
        Assert.Null(chainA.DrainDeferredAccept());
        Assert.Equal(0, chainA.Host.CallLog.CountOf("AcceptText"));

        // The validation-error re-entrancy flag is likewise per session, and both are clear here because
        // nothing in this test entered that handler.
        Assert.False(chainA.Session.InItemValidationError);
        Assert.False(chainB.Session.InItemValidationError);
    }

    [Fact]
    public async Task TwoCorrelationIdentifiersUsedConcurrentlyShareNoState()
    {
        // CONCURRENCY IS HERE TO CATCH SHARED STATIC STATE, not to measure anything. Every assertion is a
        // value comparison against the writer's own session, so the outcome does not depend on which task
        // is scheduled first, and there is no sleep, no Task.Delay and no elapsed-time tolerance anywhere
        // - the test synchronizes on task COMPLETION (AAP 0.6.7).
        (ValidationSessionRegistry registry, _) = NewRegistry(maxConcurrentSessions: 4);

        Assert.True(registry.OpenWithId(SessionA).IsOpened);
        Assert.True(registry.OpenWithId(SessionB).IsOpened);

        const int iterations = 500;

        Task<int> writerA = Task.Run(
            () => DriveSession(registry, SessionA, EventGate.EID_ROWFOCUSCHANGE, stash: 1L, iterations),
            TestContext.Current.CancellationToken);

        Task<int> writerB = Task.Run(
            () => DriveSession(registry, SessionB, EventGate.EID_ITEMCHANGE, stash: 3L, iterations),
            TestContext.Current.CancellationToken);

        int[] violations = await Task.WhenAll(writerA, writerB);

        // Each task read back only what it itself wrote, on every one of its iterations.
        Assert.Equal(0, violations[0]);
        Assert.Equal(0, violations[1]);

        ValidationSession finalA = Assert.IsType<ValidationSession>(registry.Resolve(SessionA).Session);
        ValidationSession finalB = Assert.IsType<ValidationSession>(registry.Resolve(SessionB).Session);

        Assert.NotSame(finalA, finalB);

        // Each session ended holding ITS OWN bit and ITS OWN stash - the two never merged, and neither
        // adopted the other's.
        Assert.Equal(EventGate.EID_ROWFOCUSCHANGE, finalA.DisabledEvent);
        Assert.Equal(EventGate.EID_ITEMCHANGE, finalB.DisabledEvent);
        Assert.Equal(1L, finalA.ItemChangeRetCode);
        Assert.Equal(3L, finalB.ItemChangeRetCode);
        Assert.Equal(ItemChangeResult.TriggerValidationError, finalA.StashedItemChangeResult);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, finalB.StashedItemChangeResult);
        Assert.Equal(2, registry.Count);
    }

    /// <summary>
    /// Resolves one correlation identifier repeatedly, writes its own gate bit and its own stash, and
    /// reads both back.
    /// </summary>
    /// <param name="registry">The shared registry.</param>
    /// <param name="sessionId">The identifier this driver owns.</param>
    /// <param name="bit">The gate bit this driver owns.</param>
    /// <param name="stash">The stash value this driver owns.</param>
    /// <param name="iterations">How many times to write and read back.</param>
    /// <returns>How many times a read did not match what this driver had just written.</returns>
    /// <remarks>
    /// VIOLATIONS ARE COUNTED AND RETURNED RATHER THAN ASSERTED IN PLACE, so the failure surfaces on the
    /// test's own thread with a plain number instead of inside a faulted task. The driver touches ONLY
    /// its own session, so a non-zero count can mean only one thing: two identifiers reached the same
    /// state.
    /// </remarks>
    private static int DriveSession(
        ValidationSessionRegistry registry,
        string sessionId,
        uint bit,
        long stash,
        int iterations)
    {
        int violations = 0;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            ValidationSessionResolution resolution = registry.Resolve(sessionId);

            if (resolution.Session is not ValidationSession session)
            {
                violations++;
                continue;
            }

            if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            {
                violations++;
                continue;
            }

            // :L506-L510 then :L195, on this driver's own session.
            if (session.DisableEvent(bit) != RetCode.OK)
            {
                violations++;
            }

            session.ItemChangeRetCode = stash;

            if (session.DisabledEvent != bit
                || !session.IsEventDisabled(bit)
                || session.ItemChangeRetCode != stash)
            {
                violations++;
            }
        }

        return violations;
    }

    // ==============================================================================================
    //  PHASE 4 - THE QUEUED CONTINUATION THAT REPLACES `Post`
    //  --------------------------------------------------------------------------------------------
    //  se_cst_dw.sru:L387-L393, verbatim:
    //
    //      event ondwnkillfocus;//*应用修改              "apply the modification"
    //      if Not _bDoItemChange then                                                       [:L388]
    //          Post _of_PostAcceptText()                                                    [:L389]
    //      end if                                                                           [:L390]
    //      Eventful.of_Trigger(EVT_LOSEFOCUS)                                               [:L391]
    //      return Event LoseFocus()                                                         [:L392]
    //      end event
    //
    //  and the body it defers, at :L537-L558 with the four executable lines at :L553-L557:
    //
    //      if GetFocus() <> this then                                                        [:L553]
    //          if AcceptText() = -1 then                                                     [:L554]
    //              SetFocus()                                                                [:L555]
    //          end if                                                                        [:L556]
    //      end if                                                                            [:L557]
    //
    //  `Post` hands the call to the WIN32 MESSAGE QUEUE so that it runs AFTER the current event returns.
    //  A headless Linux container has no message pump, so AAP 0.6.5 records the pump as a deliberate
    //  non-port and AAP 0.4.5.4 requires the posted call to become an explicitly queued continuation.
    //
    //  THE DEFERRAL IS THE BEHAVIOUR, NOT AN IMPLEMENTATION DETAIL, and :L553 is why. That line re-tests
    //  where focus is, and it is meant to be evaluated once the event has finished and focus has settled;
    //  running the body inline would test focus at a moment the oracle never tests it, and the answer
    //  would differ precisely in the case the deferral exists to catch - focus coming back to the
    //  DataWindow before the accept happens. So "enqueued rather than executed" is asserted here on the
    //  HOST's call log, which is the only place the difference is visible.
    // ==============================================================================================

    [Fact]
    public void KillFocusEnqueuesTheAcceptRatherThanRunningItInline()
    {
        (FakeEventChain chain, RecordingEventObserver observer) = NewChain();

        long killFocusResult = chain.OnDwnKillFocus();

        // QUEUED: :L389 ran, because :L388's guard was open on a session nothing has touched.
        Assert.True(observer.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
        Assert.True(chain.Session.DeferredAcceptPending);

        // AND NOT EXECUTED. This is the assertion the whole section exists for: the accept-text work has
        // NOT happened at the moment kill focus returns. A `Post` that had been flattened into a direct
        // call would satisfy every other assertion in this file and fail exactly here.
        Assert.Equal(0, chain.Host.CallLog.CountOf("AcceptText"));
        Assert.Equal(0, chain.Host.CallLog.CountOf("SetFocus"));

        // THE HOST WAS ASKED FOR EXACTLY ONE THING INLINE, AND IT IS :L392's SEMANTIC EVENT. Stated as the
        // complete member sequence rather than as two absences, because the log is the one place the two
        // halves of :L387-L392 can be seen in time order: what the event did, and what it merely queued.
        Assert.Equal(new[] { "Event LoseFocus" }, chain.Host.CallLog.Members);

        // The event itself has already answered - :L392 returns the semantic handler's code - so the
        // caller is not waiting on the deferred work either.
        Assert.Equal(RetCode.OK, killFocusResult);

        // NOW the host drains, and only now does the work happen. Draining is the port of the message pump
        // dispatching the posted message, and the host owns it for the same reason the pump did: the event
        // has to have returned first.
        DeferredAcceptOutcome outcome =
            Assert.IsType<DeferredAcceptOutcome>(chain.DrainDeferredAccept());

        Assert.True(outcome.FocusHadLeftHost);
        Assert.Equal(1, chain.Host.CallLog.CountOf("AcceptText"));
        Assert.False(chain.Session.DeferredAcceptPending);

        // THE COMPLETE SEQUENCE, AND IT IS THE ORACLE'S. :L389 is written FIRST in the source and happens
        // LAST in time, because `Post` defers it past the end of the event; the log shows the semantic
        // event of :L392 landing before the accept it queued.
        Assert.Equal(new[] { "Event LoseFocus", "AcceptText" }, chain.Host.CallLog.Members);

        // Draining is not repeatable: the queue is cleared before the body runs, so a host that drained
        // twice applies the edit once. A message queue delivers a posted message once too.
        Assert.Null(chain.DrainDeferredAccept());
        Assert.Equal(1, chain.Host.CallLog.CountOf("AcceptText"));
    }

    [Fact]
    public void KillFocusEnqueuesNothingAtAllWhileTheItemChangeFlagIsSet()
    {
        (FakeEventChain chain, RecordingEventObserver observer) = NewChain();

        // :L388's guard is `if Not _bDoItemChange`, so a kill focus arriving mid item change must queue
        // NOTHING - not a continuation that later declines, and not a continuation at all. The oracle has
        // no way to express a posted-but-cancelled message, so neither does the port.
        using (chain.Session.EnterItemChange())
        {
            Assert.True(chain.Session.DoItemChange);

            long killFocusResult = chain.OnDwnKillFocus();

            Assert.Equal(RetCode.OK, killFocusResult);
            Assert.False(observer.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
            Assert.False(chain.Session.DeferredAcceptPending);
        }

        // Nothing to drain, and nothing was applied - the edit is left for whatever the item change itself
        // decides to do with it.
        Assert.Null(chain.DrainDeferredAccept());
        Assert.Equal(0, chain.Host.CallLog.CountOf("AcceptText"));
        Assert.Equal(0, chain.Host.CallLog.CountOf("SetFocus"));

        // THE SUPPRESSION IS SCOPED TO THE ITEM CHANGE, NOT TO THE SESSION. :L196 restores the saved value
        // rather than clearing the flag, so once the scope closes the guard is open again and the next
        // kill focus queues normally. A port that wrote `false` at :L196 would look identical here and
        // would silently suppress this second queue if an OUTER item change were still running.
        Assert.False(chain.Session.DoItemChange);

        long second = chain.OnDwnKillFocus();

        Assert.Equal(RetCode.OK, second);
        Assert.True(chain.Session.DeferredAcceptPending);
        Assert.NotNull(chain.DrainDeferredAccept());
        Assert.Equal(1, chain.Host.CallLog.CountOf("AcceptText"));
    }

    [Fact]
    public void KillFocusCompletesItsBrokerAndSemanticHalvesInlineWhileTheAcceptStaysDeferred()
    {
        (FakeEventChain chain, RecordingEventObserver observer) = NewChain();

        // A subscriber on :L391's topic. The three-parameter handler is used because the broker injects the
        // source into the first slot even for a trigger that passes no arguments of its own.
        RecordingSubscriber subscriber = new(DataWindowEventChain.EVT_LOSEFOCUS);

        Assert.Equal(
            RetCode.OK,
            chain.On(
                DataWindowEventChain.EVT_LOSEFOCUS,
                subscriber,
                nameof(RecordingSubscriber.OnThreeArguments)));

        // Observed FROM INSIDE the broker dispatch: at the instant :L391 is running, the accept-text work
        // has not happened. That is what pins the ORDER rather than merely the outcome - :L389 comes first
        // in the source but its effect comes last in time.
        bool acceptSeenDuringBrokerDispatch = true;
        subscriber.InHandler = () =>
            acceptSeenDuringBrokerDispatch = chain.Host.CallLog.Contains("AcceptText");

        // :L392's semantic handler answers a distinctive code so the return value can be traced to it.
        chain.Host.LoseFocusHandler = () => 3L;

        long killFocusResult = chain.OnDwnKillFocus();

        // :L392 - THE SEMANTIC CODE IS RETURNED VERBATIM, in the same call. Kill focus is one of only two
        // raw handlers that propagate a semantic code instead of normalising it to 0 or 1.
        Assert.Equal(3L, killFocusResult);

        // :L391 - the broker topic was triggered, in the same call, and its result is DISCARDED: the
        // subscriber's veto cannot stop anything here, which is why the returned code is still the
        // semantic handler's.
        Assert.Equal(1, subscriber.Count);
        Assert.True(observer.Single(EventId.Ondwnkillfocus).Dispatch.BrokerTriggerRan);
        Assert.True(observer.Single(EventId.Ondwnkillfocus).Dispatch.SemanticHandlerRan);

        // :L389 - AND THE ACCEPT WAS STILL OUTSTANDING WHILE BOTH OF THOSE RAN.
        Assert.False(acceptSeenDuringBrokerDispatch);
        Assert.Equal(0, chain.Host.CallLog.CountOf("AcceptText"));
        Assert.True(chain.Session.DeferredAcceptPending);

        // The state the boundary reports alongside the event says the same thing, so a remote consumer sees
        // the outstanding continuation rather than having to infer it.
        ValidationSessionSnapshot state =
            Assert.IsType<ValidationSessionSnapshot>(observer.Single(EventId.Ondwnkillfocus).State);

        Assert.True(state.DeferredAcceptPending);

        Assert.NotNull(chain.DrainDeferredAccept());
        Assert.Equal(1, chain.Host.CallLog.CountOf("AcceptText"));
    }

    /// <summary>
    /// The two outcomes of the continuation's body, by what <c>AcceptText()</c> answers.
    /// </summary>
    /// <returns>The code <c>AcceptText()</c> returns, and whether focus is restored after it.</returns>
    /// <remarks>
    /// <c>:L554</c> tests EQUALITY against <c>-1</c>, not "not success" and not "less than or equal to
    /// zero", so <c>0</c> is carried here as the row that would pass a broadened test and must not: the
    /// oracle leaves focus alone for it. The exhaustive value matrix at the session level belongs to the
    /// sibling validation-error suite; what these rows add is the CALL ORDER through a fully assembled
    /// chain.
    /// </remarks>
    public static TheoryData<int, bool> AcceptTextOutcomes() => new()
    {
        { -1, true },
        { 0, false },
        { 1, false },
    };

    [Theory]
    [MemberData(nameof(AcceptTextOutcomes))]
    public void TheContinuationAppliesThePendingTextAndRestoresFocusOnlyWhenTheAcceptFails(
        int acceptTextResult,
        bool expectFocusRestored)
    {
        (FakeEventChain chain, _) = NewChain();

        chain.Host.AcceptTextResult = acceptTextResult;

        _ = chain.OnDwnKillFocus();

        DeferredAcceptOutcome outcome =
            Assert.IsType<DeferredAcceptOutcome>(chain.DrainDeferredAccept());

        // :L553 - focus had left the host, so the body ran.
        Assert.True(outcome.FocusHadLeftHost);

        // :L554 - the pending editor text is applied, unconditionally, on every run of the body, and it is
        // applied AFTER the event that queued it has already answered through :L392.
        Assert.Equal(acceptTextResult, outcome.AcceptTextResult);
        Assert.True(
            chain.Host.CallLog.IndexOf("AcceptText") > chain.Host.CallLog.IndexOf("Event LoseFocus"),
            "The queued accept must land after the kill-focus event returned (se_cst_dw.sru:L389, :L392).");

        // :L555 - and focus is restored ONLY on the failure code, so the invalid entry can be corrected.
        Assert.Equal(expectFocusRestored, outcome.FocusRestored);
        Assert.Equal(expectFocusRestored, chain.Host.CallLog.Contains("SetFocus"));

        // THE ORDER IS ACCEPT-THEN-RESTORE AND IT IS NESTED, NOT SEQUENTIAL. :L555 sits inside :L554's
        // `if`, so a restore can only ever follow an accept that has already failed; restoring first, or
        // restoring without accepting, would move focus back before the edit was ever offered.
        Assert.Equal(
            expectFocusRestored
                ? new[] { "Event LoseFocus", "AcceptText", "SetFocus" }
                : ["Event LoseFocus", "AcceptText"],
            chain.Host.CallLog.Members);

        Assert.Equal(
            expectFocusRestored ? chain.Host.SetFocusResult : null,
            outcome.SetFocusResult);

        // The failure code is the named constant rather than a literal at the call site, and it is -1.
        Assert.Equal(-1, ValidationSession.AcceptTextFailure);
        Assert.Equal(expectFocusRestored, acceptTextResult == ValidationSession.AcceptTextFailure);
    }

    [Fact]
    public async Task TheContinuationIsObservableWithNoTimerNoTaskDelayAndNoSleep()
    {
        (FakeEventChain chain, _) = NewChain();

        _ = chain.OnDwnKillFocus();

        Assert.True(chain.Session.DeferredAcceptPending);

        // NOTHING SELF-SCHEDULES. Yielding to the scheduler repeatedly gives any background timer,
        // continuation or thread-pool work item every opportunity to run; the continuation stays exactly
        // where kill focus left it. Task.Yield is used rather than a delay on purpose - it hands control
        // over WITHOUT consulting a clock, so this assertion cannot become flaky and cannot become slow.
        for (int yields = 0; yields < 64; yields++)
        {
            await Task.Yield();
        }

        Assert.True(chain.Session.DeferredAcceptPending);
        Assert.Equal(0, chain.Host.CallLog.CountOf("AcceptText"));

        // The work happens when, and only when, the test says so.
        Assert.NotNull(chain.DrainDeferredAccept());
        Assert.Equal(1, chain.Host.CallLog.CountOf("AcceptText"));

        // AND THE ABSENCE OF SCHEDULING IS STRUCTURAL, NOT MERELY OBSERVED. The behavioural check above
        // shows nothing fired during this test; the field inventory below shows there is nothing that
        // COULD fire, in this test or any other. Both are asserted because the first alone would be
        // satisfied by a timer whose interval simply had not elapsed yet.
        foreach (FieldInfo field in DeclaredInstanceFields())
        {
            Assert.False(
                typeof(System.Threading.Timer).IsAssignableFrom(field.FieldType)
                    || typeof(System.Timers.Timer).IsAssignableFrom(field.FieldType)
                    || typeof(ITimer).IsAssignableFrom(field.FieldType)
                    || typeof(Task).IsAssignableFrom(field.FieldType)
                    || typeof(Thread).IsAssignableFrom(field.FieldType)
                    || typeof(CancellationTokenSource).IsAssignableFrom(field.FieldType)
                    || typeof(WaitHandle).IsAssignableFrom(field.FieldType),
                $"{field.Name} is a scheduling primitive; the queued continuation must be drained by the "
                    + "host and never by a timer, a task or a thread (se_cst_dw.sru:L389).");
        }

        // The clock seam is present and is the ONLY time-related member. It is read to decide idle expiry
        // and is never used to schedule anything, which is what keeps expiry assertable without waiting.
        Assert.Contains(
            "_timeProvider",
            DeclaredInstanceFields().Select(field => field.Name));
    }
}
