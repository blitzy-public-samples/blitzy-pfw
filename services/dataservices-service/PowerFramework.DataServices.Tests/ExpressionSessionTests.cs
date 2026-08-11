// ==================================================================================================
//  ExpressionSessionTests.cs
//  ------------------------------------------------------------------------------------------------
//  Unit under test: services/dataservices-service/PowerFramework.DataServices/Expressions/
//                   ExpressionSession.cs
//
//  ================================================================================================
//  THIS FILE ENFORCES AAP 0.6.2.3 - "THE ONE HARD LIMIT" - AND OPEN RISK R2
//  ================================================================================================
//
//  THE MECHANISM, read off the oracle rather than paraphrased. The legacy declares, at
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L80-L83:
//
//      type foreignvardata from structure
//          integer                 index
//          n_cst_dwsvc_columnexp   expsvc
//      end type
//
//  The second field is typed AS THE ENGINE CLASS ITSELF. It is a LIVE IN-PROCESS OBJECT POINTER to
//  another DataWindow's expression service, and it is dereferenced as one - synchronously, into a
//  PRIVATE method of that other instance - at two independent sites, so the dereference is structural
//  rather than incidental:
//
//      GlobalVars[nGVarIdx].foreign.expSvc._of_CalcVarExpValue(...)   [:L2199-L2200]  preprocessing
//      GlobalVars[index].foreign.expSvc._of_CalcVarExpValue(...)      [:L2384-L2385]  variable value
//
//  `of_addforeignvar(name, dw)` [:L2097] populates it from a live control and then writes a
//  BACK-POINTER into the peer's own table - `expSvc.GlobalVars[...].links[n].expSvc = this` [:L2141] -
//  so the coupling is bidirectional and BOTH directions are pointers. `globalvardata.links[]` [:L70]
//  is an array of the same structure, so every element carries the same pointer.
//
//  WHY IT CANNOT CROSS THE BOUNDARY. A pointer is an address in one process's address space. It has no
//  serialized form, so there is no wire representation to define, and the call through it is a
//  synchronous read of another object's private state, which no message can stand in for.
//
//  WHAT IS SUPPORTED, AND WHAT IS BLOCKED. A cross-DataWindow variable resolves through a
//  SESSION-SCOPED DATAWINDOW HANDLE, and ONLY when both DataWindows are CO-RESIDENT IN THE SAME
//  EXPRESSION SESSION INSIDE ONE DataServices INSTANCE. A reference that spans sessions or service
//  instances - or whose session has been closed or has expired - is BLOCKED: it returns a DEFINED
//  ERROR that names the unreachable handle and it YIELDS NO VALUE. Never zero, never null, never the
//  empty string treated as data, never a stale reading.
//
//  WHY A DEFINED ERROR RATHER THAN A BEST EFFORT. Approximating a pointer dereference across a network
//  produces a result WRONG IN A WAY NO TEST WOULD OBVIOUSLY CATCH: the expression evaluates, returns a
//  number of the right type in the right range, and is silently stale. A loud failure is worse
//  ergonomics and better engineering, and it is the only outcome a characterization comparison can
//  adjudicate.
//
//  WHEN THIS WAS DECIDED - BEFORE IMPLEMENTATION, NOT DURING IT. The narrowing was surfaced during
//  discovery by reading the structure definition at [:L82], reviewed, and recorded in the plan
//  (AAP 0.6.2.3) and as tracked risk R2 (AAP 0.8.6) BEFORE any code was generated. Three artifacts in
//  this repository already carry it and can be diffed against this file: the published contract
//  (`ForeignVarRef` in shared/PowerFramework.Contracts/Proto/dataservices.v1.proto, whose `resolvable`
//  field exists for exactly this), the error vocabulary
//  (`ExpressionErrorCategory.ForeignReferenceBlocked`, documented there as having NO legacy locator
//  because the legacy cannot produce it), and the structure port
//  (`ForeignVariableReference(int Index, string Handle)`). THIS FILE IS WHAT MAKES THE NARROWING
//  VISIBLE AND ENFORCED: a regression that quietly widened the contract would fail here.
//
//  ================================================================================================
//  THE LEGACY'S OWN RULES ARE PRESERVED, NOT BENT TO FIT THE NARROWING  (C-B / G2)
//  ================================================================================================
//
//  A FOREIGN VARIABLE REQUIRES DYNAMIC EXPANSION. That is the legacy's rule and not a consequence of
//  the port. The authoritative specification introduces the feature with a worked example whose own
//  comment says dynamic expansion must be used [docs/n_cst_dwsvc_columnexp.md:L34], and the parser
//  refuses the static form outright at [:L1426] with `RetCode.E_INVALID_ARGUMENT` and the text
//  "解析变量宏失败!\n外部变量[{1}],不支持静态展开". The sibling rule for a CONTEXT variable sits at
//  [:L1417] with "解析函数宏失败!\n上下文变量[{1}],不支持静态展开" - note the legacy says "function
//  macro" there although the failing construct is a variable, and that wording is reproduced verbatim
//  rather than corrected.
//
//  That rule is EVIDENCE FOR the design rather than an obstacle to it. Static expansion substitutes a
//  value at bind time and never reads it again; the legacy refuses it for a foreign variable precisely
//  because a foreign value must be read AT CALCULATION TIME. The original therefore already insisted
//  on a live read through the pointer at the moment of calculation - which is exactly what a live
//  handle inside one session provides, and exactly what nothing outside one session can provide.
//
//  ================================================================================================
//  AAP 0.4.5.4 - THE CALCULATION STACK IS ONE-BASED, AND ITS ORDERING IS ON THE WIRE
//  ================================================================================================
//
//  One-based-to-zero-based translation is the refactor's most dangerous mechanical hazard, and the
//  calculation stack is one of the places it bites, because the stack's ordering is DIRECTLY OBSERVABLE
//  on contract C-04's expression-trace payload. Three oracle behaviours are asserted below:
//
//    PUSH / POP BY CAPTURED INDEX  [:L296-L297, :L318]
//        _vecCalcStack.Append(colName)
//        k = _vecCalcStack.Count()
//        ... _of_CalcItem(row, n) for each dependent expression, WHICH RECURSES ...
//        _vecCalcStack.RemoveAt(k)
//    The pop names the index captured immediately after the push. It is NOT "remove the last element".
//
//    RECURSION DETECTION  [:L682-L690]
//        if Not ColExpDatas[index].recursive then
//            nCount = _vecCalcStack.Count()
//            for nIndex = 1 to nCount
//                if _vecCalcStack.GetAt(nIndex) = ColExpDatas[index].name then return false
//    A one-based INCLUSIVE forward NAME scan, returning on the FIRST match - and it is SKIPPED
//    ENTIRELY when the expression's `recursive` flag is set. The reservation of 20 [:L2421-L2422] is a
//    capacity hint and imposes NO maximum depth.
//
//    THE TRACE PAYLOAD  [:L751-L758]
//        for nIndex = 1 to nCount
//            sCallStack += _vecCalcStack.GetAt(nIndex) + ">"
//        next
//        sCallStack += ColExpDatas[index].dwo.name
//    Every frame is followed by ">", then the CURRENT column's own name is appended, SO THE FINAL
//    SEGMENT CARRIES NO TRAILING ">". The reported VALUE is
//    `iif(sVal = "" and (emptyStringIsNull or colType <> COL_TYPE_STRING), "(null)", sVal)`.
//
//  NO LOOP BOUND OR INDEX EXPECTATION BELOW WAS "TIDIED" INTO ZERO-BASED FORM. Every one is the
//  oracle's own inclusive `1 .. Count()` shape.
//
//  ================================================================================================
//  WHAT THIS FILE IS NOT
//  ================================================================================================
//
//  NOT `Domain/ValidationSession.cs`. That is a DIFFERENT session type, in a DIFFERENT namespace,
//  holding the four pieces of cross-event item-change state from `se_cst_dw.sru:L89-L96` - the
//  disabled-event mask, the two re-entrancy flags, and the item-changed result stashed for the
//  validation-error event. Two distinct concepts, two distinct types, no shared state. The
//  distinctness is not assumed here, it is ASSERTED (section 1).
//
//  NOT A DUPLICATE OF ITS NEIGHBOURS. Four sibling suites already own adjacent ground and are
//  deliberately not repeated: `SessionAcquisitionTests` owns acquire / expire / trace-delivery
//  accounting, `ExpressionSessionHostFaultTests` owns host-fault sanitisation,
//  `ColumnExpressionServiceContractTests` owns the full C-04 gRPC harness, and
//  `ExpressionVariableEnvironmentTests` owns the DOMAIN-side `ForeignVariableReference` reflection.
//  This file owns the SESSION UNIT: handle registry and lifetime, co-resident resolution driven from
//  the oracle's own two-DataWindow scenario, the BLOCKED matrix, the static-expansion refusals, and
//  the calculation / recursion / trace stack - plus the GENERATED WIRE message reflection, which
//  nothing else covers.
//
//  NOT WALL-CLOCK DEPENDENT (AAP 0.6.7). Every lifetime assertion is driven through the injected
//  `DeterministicTimeProvider` seam from `TestDoubles.cs`. There is no `Thread.Sleep`, no
//  `Task.Delay`, no `DateTime.Now` and no `DateTimeOffset.UtcNow` anywhere below. Repeatability is
//  the Golden-Master technique's one hard prerequisite, so a wall-clock test would not merely be slow,
//  it would be invalid.
//
//  NOT A SECRET HOLDER (C-F). No key, token, password, certificate, connection string or
//  credential-shaped literal appears here. Session identifiers and DataWindow handles are correlation
//  values: they authorise nothing and are meaningless outside one live session.
//
//  NOT A DECLARER OF LEGACY CONSTANTS. The repository-root `.editorconfig` lowers CA1707 and IDE1006
//  to `none` for exactly the handful of constant-catalogue files whose legacy identifier SPELLINGS are
//  themselves recording-visible artifacts. NO TEST FILE IS IN THAT LIST, this one included. So this
//  file REFERENCES preserved constants by their legacy names - `RetCode.E_INVALID_ARGUMENT`,
//  `RetCode.E_VAR_NOT_FOUND`, `ExpressionVariableEnvironment.VAR_FOREIGN`,
//  `DataWindowServiceBase.COL_TYPE_STRING` - and DECLARES not one of its own.
//
//  NO USER RULES GOVERN THIS FILE. `review_rules` returns "No user rules provided." The
//  enterprise-standard baseline of AAP 0.7.2 applies in their place: nullable reference types with
//  warnings as errors, plain xunit `Assert` with no assertion library, hand-written doubles with no
//  mocking library, and every reproduced legacy behaviour carrying its oracle locator (C-K).
//
//  THE LEGACY TREE IS READ-ONLY AND SHARES THIS WORKING DIRECTORY. Every `:Lnnn` locator below points
//  into `ws_objects/` or `docs/`, which are the behavioural oracle: read as specification, never
//  edited, moved or reformatted. Every line number was verified against the file on disk.
// ==================================================================================================

using System.Globalization;
using System.Reflection;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Containers;
using PowerFramework.Shared.Kernel;
using Xunit;

// ALIASED RATHER THAN IMPORTED WHOLESALE, for the reason the sibling suites state: the contracts
// assembly and the application assembly each publish types whose simple names collide - most sharply
// `RetCode`, which names the ported constant catalogue on one side and the generated protobuf wrapper
// message on the other, so a bare use is CS0104. Naming the four generated types this file needs keeps
// every use unambiguous with no `global::` prefix at the point of use.
using WireErrorCategory = PowerFramework.Contracts.DataServices.V1.ExpressionError.Types.Category;
using WireExpressionError = PowerFramework.Contracts.DataServices.V1.ExpressionError;
using WireForeignVarRef = PowerFramework.Contracts.DataServices.V1.ForeignVarRef;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterizes <see cref="ExpressionSession"/> - the session that replaces the legacy's in-process
/// <c>foreignvardata.expsvc</c> pointer with a session-scoped DataWindow handle, and that owns the
/// one-based calculation and recursion stack whose ordering travels on contract C-04.
/// </summary>
/// <remarks>
/// The file banner states the narrowing this suite enforces, the legacy rules it preserves, and the
/// four sibling suites it deliberately does not duplicate.
/// </remarks>
public sealed class ExpressionSessionTests
{
    /// <summary>The oracle source every locator in this file points into.</summary>
    private const string OraclePath = "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru";

    /// <summary>
    /// The oracle's own foreign-variable name, transcribed rather than translated
    /// [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L140].
    /// </summary>
    /// <remarks>
    /// A Latin rename would not exercise the same path: the parser's token scanning is byte oriented,
    /// so a multi-byte identifier is exactly where a length-versus-byte-count confusion would surface.
    /// </remarks>
    private const string Summary = "数据汇总";

    /// <summary>The oracle's expression for that variable [w_test_dwsvc_columnexp.srw:L140].</summary>
    private const string SummaryExp = "SUM(n1)";

    /// <summary>
    /// The idle timeout every lifetime assertion below uses, matching
    /// <see cref="SessionLifetimeOptions.IdleTimeout"/>'s shipped default so the tests exercise the
    /// configured value rather than a value invented for the test.
    /// </summary>
    private static readonly TimeSpan ConfiguredIdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// xUnit1051 is an ERROR under this repository's warnings-as-errors gate, so every awaited call
    /// that accepts a token receives this one.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ==============================================================================================
    //  SECTION 1 - SESSION IDENTITY, THE HANDLE REGISTRY, AND LIFETIME
    //  --------------------------------------------------------------------------------------------
    //  The session is the scope the narrowing is expressed in, so its identity and its handle registry
    //  are load bearing rather than administrative: a handle that resolved outside its own session
    //  would reinstate exactly the cross-instance reference the narrowing forbids.
    // ==============================================================================================

    /// <summary>
    /// A registration mints a handle, and the handle is STAMPED WITH THE SESSION THAT ISSUED IT, so a
    /// handle minted by one session can never be mistaken for one minted by another.
    /// </summary>
    /// <remarks>
    /// The legacy needed no such identity because the pointer WAS the identity [<c>:L82</c>]. Once the
    /// pointer becomes a string, the string has to carry its scope or the scope is unenforceable.
    /// </remarks>
    [Fact]
    public void Register_MintsAHandleStampedWithTheIssuingSessionAndScopedToIt()
    {
        ExpressionSession first = NewSession("session-one");
        ExpressionSession second = NewSession("session-two");

        RecordingExpressionServiceHost host = new();

        DataWindowHandle fromFirst = first.Register(host);
        DataWindowHandle fromSecond = second.Register(host);

        Assert.False(fromFirst.IsEmpty);
        Assert.False(fromSecond.IsEmpty);

        // The same host object registered twice yields two DIFFERENT handles, because a handle names a
        // REGISTRATION inside a scope and not an object.
        Assert.NotEqual(fromFirst, fromSecond);

        Assert.StartsWith("session-one", fromFirst.Value, StringComparison.Ordinal);
        Assert.StartsWith("session-two", fromSecond.Value, StringComparison.Ordinal);

        // Each session sees ONLY its own registration.
        Assert.True(first.IsCoResident(fromFirst));
        Assert.False(first.IsCoResident(fromSecond));
        Assert.True(second.IsCoResident(fromSecond));
        Assert.False(second.IsCoResident(fromFirst));

        Assert.Equal(1, first.HandleCount);
        Assert.Equal(fromFirst, Assert.Single(first.Handles));
    }

    /// <summary>
    /// The explicit-handle registration overload reports one distinct legacy return code per outcome,
    /// so a caller can tell "you gave me nothing" from "that handle is already taken" from "this
    /// session has gone".
    /// </summary>
    /// <param name="scenario">Which outcome the row drives.</param>
    /// <param name="expected">The <c>RetCode</c> constant that outcome answers.</param>
    /// <param name="rationale">Why the code differs from its neighbours.</param>
    [Theory]
    [MemberData(nameof(ExplicitRegistrationOutcomes))]
    public void RegisterWithAnExplicitHandle_AnswersADistinctCodePerOutcome(
        ExplicitRegistrationScenario scenario,
        long expected,
        string rationale)
    {
        Assert.False(string.IsNullOrWhiteSpace(rationale));

        ExpressionSession session = NewSession("registration");
        RecordingExpressionServiceHost host = new();

        long actual = scenario switch
        {
            ExplicitRegistrationScenario.EmptyHandle =>
                session.Register(DataWindowHandle.None, host),

            ExplicitRegistrationScenario.FreshHandle =>
                session.Register(DataWindowHandle.From("dw_1"), host),

            ExplicitRegistrationScenario.DuplicateHandle => RegisterTwice(session, host),

            ExplicitRegistrationScenario.ClosedSession => RegisterAfterClose(session, host),

            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Every declared scenario must be driven; an unhandled one would pass vacuously."),
        };

        Assert.Equal(expected, actual);

        static long RegisterTwice(ExpressionSession session, RecordingExpressionServiceHost host)
        {
            Assert.Equal(RetCode.OK, session.Register(DataWindowHandle.From("dw_1"), host));
            return session.Register(DataWindowHandle.From("dw_1"), host);
        }

        static long RegisterAfterClose(ExpressionSession session, RecordingExpressionServiceHost host)
        {
            Assert.True(session.Close());
            return session.Register(DataWindowHandle.From("dw_1"), host);
        }
    }

    /// <summary>
    /// The four explicit-registration outcomes and the code each answers.
    /// </summary>
    /// <returns>One row per outcome, each carrying its rationale.</returns>
    public static TheoryData<ExplicitRegistrationScenario, long, string> ExplicitRegistrationOutcomes() =>
        new()
        {
            // An absent handle is a malformed ARGUMENT - the caller passed nothing.
            {
                ExplicitRegistrationScenario.EmptyHandle,
                RetCode.E_INVALID_ARGUMENT,
                "AAP 0.6.2.3: an empty handle is a malformed argument, distinct from a live handle that cannot be reached."
            },

            {
                ExplicitRegistrationScenario.FreshHandle,
                RetCode.OK,
                OraclePath + ":L2145 - a clean acquisition is the only success arm."
            },

            // A collision is a STATE, not a malformed argument, so it answers the generic failure the
            // oracle's own duplicate-definition arm answers at [:L2122-L2125].
            {
                ExplicitRegistrationScenario.DuplicateHandle,
                RetCode.FAILED,
                "A collision is a state rather than a malformed argument, mirroring the oracle's duplicate arm at " + OraclePath + ":L2122."
            },

            // A closed session does not "fail" the registration, it does not EXIST to register into.
            {
                ExplicitRegistrationScenario.ClosedSession,
                RetCode.E_NOT_EXISTS,
                OraclePath + ":L2425 - a released scope no longer exists to register into, which is why it is not a plain failure."
            },
        };

    /// <summary>
    /// A handle resolves ONLY through the session that registered it. This is the narrowing's
    /// foundation: everything else in this file follows from it.
    /// </summary>
    [Fact]
    public void AHandleResolvesOnlyThroughTheSessionThatRegisteredIt()
    {
        ExpressionSession owner = NewSession("owner");
        ExpressionSession stranger = NewSession("stranger");

        RecordingExpressionServiceHost host = new();
        host.AddVariable(Summary, "41");

        DataWindowHandle handle = owner.Register(host);

        // Through the owner: resolved, with the peer's OWN one-based index.
        ForeignVariableResolution resolved = owner.ResolveForeignVariable(handle, Summary);
        Assert.True(resolved.IsResolved);
        Assert.Equal(CrossDataWindowStatus.Resolved, resolved.Status);
        Assert.Equal(1, resolved.Index);
        Assert.Same(host, resolved.Host);
        Assert.Equal(RetCode.OK, resolved.ReturnCode);
        Assert.Null(resolved.Error);

        // Through any other session: BLOCKED. Same handle, same live host object, different scope.
        ForeignVariableResolution blocked = stranger.ResolveForeignVariable(handle, Summary);
        Assert.True(blocked.IsBlocked);
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, blocked.Status);
        Assert.Null(blocked.Host);
        Assert.Equal(0, blocked.Index);
        Assert.NotNull(blocked.Error);

        // And the stranger read NOTHING from the host: the resolved call recorded its lookup, the
        // blocked call recorded none. Section 3 proves the stronger form - that the block is decided
        // before the host is contacted at all.
        Assert.Empty(host.Reads);
        Assert.Empty(host.ResolvedIndexes);
    }

    /// <summary>
    /// An unknown handle yields a DEFINED ERROR rather than a null, an empty resolution, or a silently
    /// absent host.
    /// </summary>
    /// <remarks>
    /// Returning null here would push the decision onto every caller and invite a null-coalesce to a
    /// default, which is precisely the silently-wrong-value outcome AAP 0.6.2.3 forbids.
    /// </remarks>
    [Fact]
    public void AnUnknownHandleYieldsADefinedErrorRatherThanNull()
    {
        ExpressionSession session = NewSession("unknown-handle");

        ForeignVariableResolution resolution =
            session.ResolveForeignVariable(DataWindowHandle.From("never-registered"), Summary);

        // The RESOLUTION itself is never null - it is a value with a status.
        Assert.NotNull(resolution);
        Assert.False(resolution.IsResolved);
        Assert.True(resolution.IsBlocked);
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, resolution.Status);
        Assert.Equal(RetCode.E_INVALID_HANDLE, resolution.ReturnCode);

        Assert.NotNull(resolution.Error);
        ExpressionParseError error = resolution.Error;
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);

        // The error NAMES the unreachable handle and the session that could not reach it, so the
        // failure is diagnosable without a debugger attached.
        Assert.Contains("never-registered", error.Text, StringComparison.Ordinal);
        Assert.Contains("unknown-handle", error.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closing a session releases every handle it scoped, and a resolution attempted afterwards fails
    /// with a defined error rather than reaching a host that is still perfectly alive.
    /// </summary>
    /// <remarks>
    /// The legacy released its vector in the DESTRUCTOR [<c>:L2425</c>], a deterministic release tied to
    /// the owning object's lifetime. The boundary equivalent is the explicit close the contract
    /// publishes, and this test is what proves the release actually happens.
    /// </remarks>
    [Fact]
    public void ClosingASessionReleasesItsHandlesAndLaterResolutionFailsWithADefinedError()
    {
        ExpressionSession session = NewSession("closing");
        RecordingExpressionServiceHost host = new();
        host.AddVariable(Summary, "7");

        DataWindowHandle handle = session.Register(host);
        Assert.NotNull(session.CalcStackFor(handle));
        Assert.True(session.ResolveForeignVariable(handle, Summary).IsResolved);

        Assert.True(session.Close());

        // Closing is idempotent in effect and honest about it: the second call reports that it had
        // nothing left to do.
        Assert.False(session.Close());

        Assert.False(session.IsOpen);
        Assert.Equal(0, session.HandleCount);
        Assert.Empty(session.Handles);
        Assert.False(session.IsCoResident(handle));
        Assert.Null(session.CalcStackFor(handle));
        Assert.False(session.TryGetHost(handle, out IExpressionServiceHost? released));
        Assert.Null(released);

        // The host object is untouched and still answers - which is exactly why the BLOCK has to be
        // enforced by the session rather than inferred from the host being gone.
        Assert.Equal(1, host.FindVarIndex(Summary));

        ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, Summary);
        Assert.True(resolution.IsBlocked);
        Assert.Equal(CrossDataWindowStatus.BlockedSessionClosed, resolution.Status);

        // A CLOSED session answers E_NOT_EXISTS rather than E_INVALID_HANDLE: the handle was never the
        // problem, the SCOPE has gone. Both are blocks, and the difference is diagnostically useful.
        Assert.Equal(RetCode.E_NOT_EXISTS, resolution.ReturnCode);
        Assert.NotNull(resolution.Error);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, resolution.Error.Category);
    }

    /// <summary>
    /// The session honours the idle timeout configured under
    /// <c>DataServices:Sessions:ExpressionSession</c>, and it does so through the INJECTED CLOCK - no
    /// wall clock, no sleep.
    /// </summary>
    /// <remarks>
    /// Expiry is a BLOCKING condition for a foreign reference, not merely a housekeeping event, so it
    /// belongs in this suite: an expired session must refuse a cross-DataWindow read for the same
    /// reason a closed one does.
    /// </remarks>
    [Fact]
    public void TheSessionHonoursTheConfiguredIdleTimeoutThroughTheInjectedClock()
    {
        DeterministicTimeProvider clock = new();
        ExpressionSession session = NewSession("expiring", clock: clock);

        Assert.Equal(ConfiguredIdleTimeout, session.IdleTimeout);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, session.CreatedAt);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, session.LastAccessedAt);

        RecordingExpressionServiceHost host = new();
        host.AddVariable(Summary, "13");
        DataWindowHandle handle = session.Register(host);

        // One tick short of the timeout is NOT expired: the comparison is strictly greater-than.
        clock.Advance(ConfiguredIdleTimeout);
        Assert.False(session.HasExpired());
        Assert.True(session.ResolveForeignVariable(handle, Summary).IsResolved);

        // A successful resolution TOUCHES the session, so the clock has to be advanced again from the
        // new baseline rather than from the original one.
        clock.Advance(ConfiguredIdleTimeout + TimeSpan.FromTicks(1));
        Assert.True(session.HasExpired());

        // Expiry is realised by an acquisition, which releases the registrations under the same lock
        // that observed the expiry.
        Assert.Equal(ExpressionSessionAcquisition.Expired, session.Acquire());
        Assert.False(session.IsOpen);
        Assert.Equal(0, session.HandleCount);

        // AND THE FOREIGN READ IS NOW BLOCKED - the point of the test.
        ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, Summary);
        Assert.True(resolution.IsBlocked);
        Assert.Equal(CrossDataWindowStatus.BlockedSessionClosed, resolution.Status);
        Assert.NotNull(resolution.Error);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, resolution.Error.Category);
    }

    /// <summary>
    /// A session in continuous use never expires, because every resolution records activity through
    /// the same clock seam.
    /// </summary>
    [Fact]
    public void ASessionInContinuousUseNeverExpires()
    {
        DeterministicTimeProvider clock = new();
        ExpressionSession session = NewSession("kept-alive", clock: clock);

        RecordingExpressionServiceHost host = new();
        host.AddVariable(Summary, "3");
        DataWindowHandle handle = session.Register(host);

        // Four idle periods pass, but each is interrupted by a real resolution just before the
        // deadline, so the session survives all four.
        for (int cycle = 1; cycle <= 4; cycle++)
        {
            clock.Advance(ConfiguredIdleTimeout);
            Assert.True(
                session.ResolveForeignVariable(handle, Summary).IsResolved,
                "Cycle " + cycle.ToString(CultureInfo.InvariantCulture) + " must still resolve.");
            Assert.False(session.HasExpired());
        }

        Assert.True(session.IsOpen);
        Assert.Equal(
            DeterministicTimeProvider.DefaultInstant + (ConfiguredIdleTimeout * 4),
            session.LastAccessedAt);
    }

    /// <summary>
    /// <see cref="Touch"/> is the explicit activity seam, and it is a no-op once the session has gone -
    /// so a late touch cannot resurrect a closed session's last-accessed stamp.
    /// </summary>
    [Fact]
    public void TouchRecordsActivityWhileOpenAndIsInertOnceClosed()
    {
        DeterministicTimeProvider clock = new();
        ExpressionSession session = NewSession("touch", clock: clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        session.Touch();
        DateTimeOffset touched = session.LastAccessedAt;
        Assert.Equal(DeterministicTimeProvider.DefaultInstant.AddMinutes(1), touched);

        Assert.True(session.Close());

        clock.Advance(TimeSpan.FromMinutes(1));
        session.Touch();
        Assert.Equal(touched, session.LastAccessedAt);

        // A closed session is not "expired" either: it has already gone, and reporting it as expired
        // would invite a second release.
        Assert.False(session.HasExpired());
    }

    /// <summary>
    /// EVERY REGISTERED HANDLE GETS ITS OWN CALCULATION STACK. The legacy holds the vector as an
    /// ENGINE-PRIVATE field [<c>:L110</c>] created per instance [<c>:L2421</c>], so one stack per
    /// session would fuse two DataWindows' recursion state and make a legitimate mutual reference look
    /// like recursion.
    /// </summary>
    [Fact]
    public void EachRegisteredHandleOwnsItsOwnCalculationStack()
    {
        ExpressionSession session = NewSession("per-handle-stacks");

        DataWindowHandle first = session.Register(new RecordingExpressionServiceHost());
        DataWindowHandle second = session.Register(new RecordingExpressionServiceHost());

        ExpressionCalcStack? firstLookup = session.CalcStackFor(first);
        ExpressionCalcStack? secondLookup = session.CalcStackFor(second);
        Assert.NotNull(firstLookup);
        Assert.NotNull(secondLookup);
        ExpressionCalcStack firstStack = firstLookup;
        ExpressionCalcStack secondStack = secondLookup;

        Assert.NotSame(firstStack, secondStack);

        // The same handle always answers the same stack, so a caller that re-reads mid-calculation does
        // not silently start a second one.
        Assert.Same(firstStack, session.CalcStackFor(first));

        Assert.Equal(1, firstStack.Push("n1"));
        Assert.Equal(1, firstStack.Depth);

        // The second stack is untouched: the depth is per DataWindow, exactly as in the oracle.
        Assert.Equal(0, secondStack.Depth);

        // An unregistered handle has no stack at all, and that is reported as an absence rather than as
        // a fresh empty stack a caller could push onto and lose.
        Assert.Null(session.CalcStackFor(DataWindowHandle.From("absent")));
        Assert.Null(session.CalcStackFor(DataWindowHandle.None));
    }

    /// <summary>
    /// THE EXPRESSION SESSION AND THE VALIDATION SESSION ARE DIFFERENT TYPES WITH INDEPENDENT
    /// IDENTITIES AND INDEPENDENT LIFETIMES. Closing one has no effect on the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are easy to conflate - both are server-held, id-correlated, open/close scoped - and
    /// conflating them would be a genuine defect: <c>Domain/ValidationSession</c> carries the four
    /// pieces of cross-event item-change state from <c>se_cst_dw.sru:L89-L96</c>, while this session
    /// scopes cross-DataWindow handles and owns the calculation stack. Neither reads the other's state.
    /// </para>
    /// <para>
    /// The two even take their idle timeout from SEPARATE configuration keys -
    /// <c>Sessions:ValidationSession</c> and <c>Sessions:ExpressionSession</c> - so they can be tuned
    /// independently, which is only meaningful because they are independent.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExpressionSessionIsDistinctFromTheValidationSession()
    {
        DeterministicTimeProvider clock = new();

        // Deliberately given THE SAME correlation id, which is the strongest form of the assertion: even
        // identically named, they share nothing.
        const string SharedId = "correlation-1";

        ExpressionSession expression = NewSession(SharedId, clock: clock);
        ValidationSession validation = new(
            SharedId,
            dataWindowHandle: "dw_1",
            lifetime: new SessionLifetimeOptions { IdleTimeout = ConfiguredIdleTimeout },
            timeProvider: clock);

        Assert.NotSame(expression, validation);
        Assert.NotEqual(typeof(ExpressionSession), typeof(ValidationSession));

        // Different namespaces, which is the structural half of the distinction.
        Assert.Equal("PowerFramework.DataServices.Expressions", typeof(ExpressionSession).Namespace);
        Assert.Equal("PowerFramework.DataServices.Domain", typeof(ValidationSession).Namespace);

        DataWindowHandle handle = expression.Register(new RecordingExpressionServiceHost());
        Assert.True(expression.IsOpen);
        Assert.True(validation.IsOpen);

        // Close the EXPRESSION session. The validation session is unaffected.
        Assert.True(expression.Close());
        Assert.False(expression.IsOpen);
        Assert.True(validation.IsOpen);
        Assert.Equal(ValidationSessionAcquisition.Acquired, validation.Acquire());

        // Close the VALIDATION session. The expression session was already closed, so re-close it to
        // prove the second close is still reported honestly rather than being confused with the first.
        Assert.True(validation.Close());
        Assert.False(validation.IsOpen);
        Assert.False(expression.Close());

        // The expression session's handle registry never appeared on the validation session at all:
        // there is no member to look it up with, and the two ids are the only thing they share.
        Assert.Equal(SharedId, expression.SessionId);
        Assert.Equal(SharedId, validation.SessionId);
        Assert.Equal(0, expression.HandleCount);
        Assert.Equal("dw_1", validation.DataWindowHandle);
        Assert.False(handle.IsEmpty);
    }

    /// <summary>
    /// The registry reads its lifetime and its stack reservation from
    /// <see cref="DataServicesOptions"/> - the <c>ExpressionSession</c> key, never the
    /// <c>ValidationSession</c> one - and hands both to every session it opens.
    /// </summary>
    [Fact]
    public void TheRegistryTakesItsLifetimeFromTheExpressionSessionKeyAndNotTheValidationOne()
    {
        DeterministicTimeProvider clock = new();

        DataServicesOptions options = new();
        options.Sessions.ExpressionSession.IdleTimeout = TimeSpan.FromMinutes(9);
        options.Sessions.ExpressionSession.MaxConcurrentSessions = 3;

        // Deliberately DIFFERENT, so a mis-wired registry that read the validation key would be caught
        // rather than passing by coincidence.
        options.Sessions.ValidationSession.IdleTimeout = TimeSpan.FromMinutes(77);
        options.ColumnExpression.CalcStackInitialCapacity = 20;

        ExpressionSessionRegistry registry = new(options, clock);

        Assert.Equal(TimeSpan.FromMinutes(9), registry.IdleTimeout);
        Assert.Equal(3, registry.MaxConcurrentSessions);

        ExpressionSessionOpenResult opened = registry.Open("from-options");
        Assert.True(opened.IsOpened);
        Assert.Equal(RetCode.OK, opened.ReturnCode);

        Assert.NotNull(opened.Session);
        ExpressionSession session = opened.Session;
        Assert.Equal(TimeSpan.FromMinutes(9), session.IdleTimeout);
        Assert.Equal(20, session.CalcStackInitialCapacity);
        Assert.Equal(1, registry.Count);

        // A handle minted inside a registry-opened session resolves only through THAT session, which is
        // what makes the registry the instance-wide boundary the narrowing needs.
        RecordingExpressionServiceHost host = new();
        host.AddVariable(Summary, "5");
        DataWindowHandle handle = session.Register(host);

        Assert.True(registry.TryGet("from-options", out ExpressionSession? fetched));
        Assert.Same(session, fetched);
        Assert.True(session.ResolveForeignVariable(handle, Summary).IsResolved);

        // Closing through the REGISTRY closes the session, so the handle is blocked afterwards.
        Assert.True(registry.Close("from-options"));
        Assert.Equal(0, registry.Count);
        Assert.False(registry.TryGet("from-options", out ExpressionSession? gone));
        Assert.Null(gone);
        Assert.Equal(
            CrossDataWindowStatus.BlockedSessionClosed,
            session.ResolveForeignVariable(handle, Summary).Status);
    }

    // ==============================================================================================
    //  SECTION 2 - THE CO-RESIDENT FOREIGN-VARIABLE HAPPY PATH
    //  --------------------------------------------------------------------------------------------
    //  Oracle: ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L136-L160, the repository's only
    //  cross-DataWindow fixture, reproduced with TWO ENGINES IN ONE SESSION.
    //
    //      dw_2.ColumnExp.of_SetEnabled(true)                            :L137
    //      dw_2.ColumnExp.of_AddVarExp("数据汇总", "SUM(n1)")             :L140
    //      dw_1.ColumnExp.of_AddForeignVar("数据汇总", dw_2)              :L143
    //      dw_1.ColumnExp.of_SetExp("n1", "...+$$数据汇总")               :L157
    //      dw_1.ColumnExp.of_CalcAll()                                   :L160
    //
    //  INSIDE ONE SESSION THE SUPPORTED CASE LOSES NOTHING AT ALL: the dereference is still a
    //  synchronous in-process call through an interface reference, reached through a handle lookup
    //  rather than through a stored pointer.
    // ==============================================================================================

    /// <summary>
    /// The oracle's cross-DataWindow scenario resolves when both DataWindows are co-resident, and the
    /// foreign value is read AT CALCULATION TIME - so changing the foreign DataWindow's underlying data
    /// changes the referencing DataWindow's calculated result. That propagation is the entire point of a
    /// foreign reference being dynamic.
    /// </summary>
    [Fact]
    public async Task TheOracleCrossDataWindowScenario_ResolvesAndTracksThePeersDataWhenCoResident()
    {
        ExpressionSession session = NewSession("co-resident");

        // TWO ENGINES, ONE SESSION. `dw_2` is the oracle's foreign source and `dw_1` the referencing
        // DataWindow.
        (ColumnExpressionEngine dw2, FakeDataWindowHost hostTwo) = NewEngine(session);
        (ColumnExpressionEngine dw1, FakeDataWindowHost hostOne) = NewEngine(session);

        Assert.Same(session, dw1.Session);
        Assert.Same(session, dw2.Session);
        Assert.False(dw1.OwnsSession);
        Assert.False(dw2.OwnsSession);
        Assert.NotEqual(dw1.Handle, dw2.Handle);
        Assert.Equal(2, session.HandleCount);

        // :L140 - the peer defines the expression variable.
        Assert.Equal(RetCode.OK, dw2.AddVarExp(Summary, SummaryExp));

        // The peer's four fixture rows carry n1 = 0, so SUM(n1) is 0 to begin with. Seed real data.
        hostTwo.SetItem(1L, "n1", 10m);
        hostTwo.SetItem(2L, "n1", 20m);

        // :L143 - the referencing DataWindow acquires the foreign variable. THE CO-RESIDENCY TEST RUNS
        // HERE, and it passes because both engines registered into this one session.
        Assert.Equal(RetCode.OK, dw1.AddForeignVar(Summary, dw2));

        // :L157 - and it uses it DYNAMICALLY, which is the only form the legacy permits for a foreign
        // variable [docs/n_cst_dwsvc_columnexp.md:L34].
        Assert.True(dw1.AddExp("n1", "$$" + Summary) > 0);

        // :L160 - recalculate. The value travels from dw_2's buffer, through dw_2's evaluator, across
        // the handle lookup, into dw_1's column.
        Assert.Equal(RetCode.OK, await dw1.CalcAllAsync(Ct));
        Assert.Equal(30m, hostOne.GetItemDecimal(1L, "n1"));

        // NOW CHANGE THE PEER'S UNDERLYING DATA AND RECALCULATE. A dynamic foreign reference re-reads,
        // so the referencing column MUST move. A static substitution would be frozen at 30.
        hostTwo.SetItem(3L, "n1", 12m);
        Assert.Equal(RetCode.OK, await dw1.CalcAllAsync(Ct));
        Assert.Equal(42m, hostOne.GetItemDecimal(1L, "n1"));

        // No error was raised on either side of the boundary.
        Assert.Equal(0L, dw1.RaisedErrorCount);
        Assert.Equal(0L, dw2.RaisedErrorCount);
    }

    /// <summary>
    /// The local entry is marked FOREIGN and carries the PEER's handle plus the index of the variable
    /// INSIDE THE PEER'S table - not the index it landed at locally [<c>:L2128-L2135</c>].
    /// </summary>
    /// <remarks>
    /// The distinction is easy to get wrong and impossible to notice once wrong, because both numbers
    /// are small positive integers. The peer here is given TWO variables so that the foreign index and
    /// the local index are provably different numbers.
    /// </remarks>
    [Fact]
    public void TheForeignIndexNamesThePeersVariableAndNotTheLocalEntry()
    {
        ExpressionSession session = NewSession("foreign-index");

        (ColumnExpressionEngine dw2, _) = NewEngine(session);
        (ColumnExpressionEngine dw1, _) = NewEngine(session);

        // The peer declares a decoy FIRST, so `Summary` sits at the peer's index 2.
        Assert.Equal(RetCode.OK, dw2.AddVar("decoy", 1L));
        Assert.Equal(RetCode.OK, dw2.AddVarExp(Summary, SummaryExp));
        Assert.Equal(2, dw2.FindVarIndex(Summary));

        // The referencing side declares its own decoy, so the new foreign entry lands at LOCAL index 2
        // as well - and then a third, so the two numbers cannot coincide.
        Assert.Equal(RetCode.OK, dw1.AddVar("localDecoyOne", 1L));
        Assert.Equal(RetCode.OK, dw1.AddVar("localDecoyTwo", 2L));
        Assert.Equal(RetCode.OK, dw1.AddForeignVar(Summary, dw2));

        int localIndex = dw1.FindVarIndex(Summary);
        Assert.Equal(3, localIndex);

        GlobalVariable entry = dw1.Variables[localIndex];
        Assert.Equal(Summary, entry.Name);
        Assert.Equal(ExpressionVariableEnvironment.VAR_FOREIGN, entry.VarType);

        // THE ASSERTION: the foreign index is the PEER's 2, not the local 3.
        Assert.Equal(2, entry.Foreign.Index);
        Assert.NotEqual(localIndex, entry.Foreign.Index);

        // And it is reached by the PEER's handle, which is a session-scoped string.
        Assert.Equal(dw2.Handle.Value, entry.Foreign.Handle);
        Assert.NotEqual(dw1.Handle.Value, entry.Foreign.Handle);

        // The local entry's own `Local` half is untouched - a foreign variable has no local expression.
        Assert.Equal(string.Empty, entry.Local.Exp);
    }

    /// <summary>
    /// The back-link is recorded ON THE PEER, against the peer's own variable, and its index names the
    /// REFERENCING service's entry [<c>:L2139-L2141</c>].
    /// </summary>
    /// <remarks>
    /// <c>globalvardata.links[]</c> [<c>:L70</c>] is how the oracle propagates a change outward: when the
    /// peer's variable changes it walks its links and fires <c>OnVarChanged</c> on each
    /// [<c>:L320-L325</c>]. The direction is therefore load bearing, and the pointer in
    /// <c>links[n].expSvc</c> is replaced by the referencing DataWindow's HANDLE for exactly the same
    /// reason the forward reference is.
    /// </remarks>
    [Fact]
    public void TheBackLinkIsRecordedOnThePeerAndNamesTheReferencingService()
    {
        ExpressionSession session = NewSession("back-link");

        (ColumnExpressionEngine dw2, _) = NewEngine(session);
        (ColumnExpressionEngine dw1, _) = NewEngine(session);

        Assert.Equal(RetCode.OK, dw2.AddVarExp(Summary, SummaryExp));

        // Before the reference exists the peer's variable has no links at all.
        Assert.Empty(dw2.Variables[dw2.FindVarIndex(Summary)].Links);

        Assert.Equal(RetCode.OK, dw1.AddForeignVar(Summary, dw2));

        GlobalVariable peerVariable = dw2.Variables[dw2.FindVarIndex(Summary)];
        ForeignVariableReference link = Assert.Single(peerVariable.Links);

        // :L2140 - `links[n].index = UpperBound(GlobalVars)`, the REFERENCING service's local index.
        Assert.Equal(dw1.FindVarIndex(Summary), link.Index);
        Assert.Equal(dw1.Variables.UpperBound, link.Index);

        // :L2141 - `links[n].expSvc = this`, the back-pointer, ported as the referencing DataWindow's
        // session-scoped handle. THE POINTER IS GONE FROM BOTH DIRECTIONS.
        Assert.Equal(dw1.Handle.Value, link.Handle);
        Assert.NotEqual(dw2.Handle.Value, link.Handle);

        // The referencing side records no link of its own: the link array lives on the DEFINER.
        Assert.Empty(dw1.Variables[dw1.FindVarIndex(Summary)].Links);

        // A second referencing DataWindow appends a second link rather than replacing the first.
        (ColumnExpressionEngine dw3, _) = NewEngine(session);
        Assert.Equal(RetCode.OK, dw3.AddForeignVar(Summary, dw2));

        // Asserted as an ORDERED PROJECTION rather than by positional index: `Links` is a .NET
        // ImmutableArray and therefore zero-based, while every ported container in this service is
        // one-based, and nothing in this file should be readable as an index expectation over either.
        Assert.Equal(
            [dw1.Handle.Value, dw3.Handle.Value],
            dw2.Variables[dw2.FindVarIndex(Summary)]
                .Links
                .Select(static link => link.Handle));
    }

    /// <summary>
    /// At the session level, <see cref="ExpressionSession.CalcForeignVariableValue"/> reads through the
    /// foreign host EVERY TIME IT IS CALLED, so a change on the far side is visible on the next read
    /// without any re-binding.
    /// </summary>
    [Fact]
    public void CalcForeignVariableValue_ReadsThroughTheForeignHostOnEveryCall()
    {
        ExpressionSession session = NewSession("live-read");

        RecordingExpressionServiceHost peer = new();
        peer.AddVariable(Summary, "100");
        DataWindowHandle peerHandle = session.Register(peer);

        RecordingExpressionServiceHost caller = new();
        DataWindowHandle callerHandle = session.Register(caller);
        FakeDataWindowObject callerColumn = new("n1", "decimal(2)", id: 1L);

        ForeignVariableReference reference =
            session.ResolveForeignVariable(peerHandle, Summary).ToReference();
        Assert.Equal(1, reference.Index);
        Assert.Equal(peerHandle.Value, reference.Handle);

        ExpressionValueResult first =
            session.CalcForeignVariableValue(reference, 1L, callerColumn, caller);
        Assert.True(first.Succeeded);
        Assert.Equal("100", first.Value);
        Assert.Equal(RetCode.OK, first.ReturnCode);
        Assert.Null(first.Error);

        // Mutate the far side. NOTHING is re-bound and the reference is not rebuilt.
        peer.Items[Summary] = "250";

        ExpressionValueResult second =
            session.CalcForeignVariableValue(reference, 1L, callerColumn, caller);
        Assert.Equal("250", second.Value);

        // Both reads went through the peer's own one-based index, twice.
        Assert.Equal([1, 1], peer.ResolvedIndexes);

        // Both DataWindows are registered in this one session, which is what made the read legal.
        Assert.Equal(2, session.HandleCount);
        Assert.Contains(callerHandle, session.Handles);
        Assert.Contains(peerHandle, session.Handles);
    }

    /// <summary>
    /// An empty answer from the foreign host is reported as an unsuccessful read rather than as a
    /// successful empty value, so "the peer had nothing" never masquerades as data.
    /// </summary>
    /// <remarks>
    /// This is the boundary case AAP 0.6.2.3 singles out: an empty string TREATED AS DATA is one of the
    /// four substitutions the narrowing forbids, so the empty answer has to be visibly unsuccessful.
    /// </remarks>
    [Fact]
    public void AnEmptyForeignAnswerIsReportedAsUnsuccessfulRatherThanAsData()
    {
        ExpressionSession session = NewSession("empty-answer");

        RecordingExpressionServiceHost peer = new();
        peer.AddVariable(Summary, string.Empty);
        DataWindowHandle peerHandle = session.Register(peer);

        ForeignVariableReference reference = new(1, peerHandle.Value);
        ExpressionValueResult result = session.CalcForeignVariableValue(reference, 1L, null, null);

        // The handle RESOLVED - this is not a blocked reference - but the read did not succeed.
        Assert.Equal(CrossDataWindowStatus.Resolved, result.Status);
        Assert.False(result.IsBlocked);
        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.Value);
        Assert.Equal(RetCode.FAILED, result.ReturnCode);
        Assert.Null(result.Error);
    }

    // ==============================================================================================
    //  SECTION 3 - THE BLOCKED PATHS. THE DECISIVE TESTS OF AAP 0.6.2.3 AND RISK R2.
    //  --------------------------------------------------------------------------------------------
    //  Everything above establishes that the supported case works. THIS SECTION IS WHY THE FILE EXISTS:
    //  it pins the narrowing so that a future change cannot quietly widen the contract back into a
    //  cross-instance pointer, and it pins the SHAPE of the refusal - a defined error, a named handle,
    //  and NO VALUE OF ANY KIND.
    // ==============================================================================================

    /// <summary>
    /// The two BLOCKED outcomes, each reached by its own route, each answering the same defined error
    /// vocabulary - and each producing NO VALUE.
    /// </summary>
    /// <param name="scenario">Which route to the block the row drives.</param>
    /// <param name="expected">The <see cref="CrossDataWindowStatus"/> that route reports.</param>
    /// <param name="expectedReturnCode">
    /// The <c>RetCode</c> constant that route answers. THE TWO BLOCKED ARMS DO NOT SHARE ONE CODE:
    /// "not co-resident" is a bad HANDLE and answers <c>E_INVALID_HANDLE</c>, while "the session has
    /// gone" answers <c>E_NOT_EXISTS</c> because the scope itself no longer exists. Both are blocks and
    /// neither is a success; carrying the code per row keeps that useful distinction asserted rather
    /// than flattened.
    /// </param>
    /// <param name="locator">The oracle or plan locator that makes the route a block.</param>
    [Theory]
    [MemberData(nameof(BlockedRoutes))]
    public void EveryRouteOutsideTheSessionIsBlockedAndProducesNoValue(
        BlockedRouteScenario scenario,
        CrossDataWindowStatus expected,
        long expectedReturnCode,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        BlockedRouteFixture fixture = BuildBlockedRoute(scenario);

        // ---- the RESOLUTION half ----
        ForeignVariableResolution resolution =
            fixture.Resolver.ResolveForeignVariable(fixture.Handle, Summary);

        Assert.Equal(expected, resolution.Status);
        Assert.True(resolution.IsBlocked);
        Assert.False(resolution.IsResolved);

        // NO HOST. A caller cannot reach through a blocked resolution by accident.
        Assert.Null(resolution.Host);

        // NO INDEX either: a blocked reference carries nothing that could be used to read.
        Assert.Equal(0, resolution.Index);

        Assert.Equal(expectedReturnCode, resolution.ReturnCode);
        Assert.NotEqual(RetCode.OK, resolution.ReturnCode);
        Assert.NotNull(resolution.Error);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, resolution.Error.Category);

        // ---- the VALUE half. THIS IS THE ASSERTION THE NARROWING TURNS ON ----
        ExpressionValueResult value = fixture.Resolver.CalcForeignVariableValue(
            new ForeignVariableReference(1, fixture.Handle.Value),
            callerRow: 1L,
            callerDwo: null,
            caller: null);

        Assert.Equal(expected, value.Status);
        Assert.True(value.IsBlocked);

        // NOT a success, NOT zero, NOT the peer's number, and the empty string it carries is a
        // STRUCTURAL absence rather than data - which is why `Succeeded` is false even though `Value`
        // is a non-null string.
        Assert.False(value.Succeeded);
        Assert.Equal(string.Empty, value.Value);
        Assert.NotEqual("0", value.Value);
        Assert.NotEqual(fixture.PeerValue, value.Value);
        Assert.Equal(expectedReturnCode, value.ReturnCode);
        Assert.NotEqual(RetCode.OK, value.ReturnCode);
        Assert.NotNull(value.Error);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, value.Error.Category);
    }

    /// <summary>
    /// The four routes by which a reference leaves the scope that could satisfy it.
    /// </summary>
    /// <returns>One row per route, each carrying the locator that makes it a block.</returns>
    public static TheoryData<BlockedRouteScenario, CrossDataWindowStatus, long, string> BlockedRoutes() =>
        new()
        {
            // CROSS-SESSION. The peer is alive, registered and answering - in ANOTHER session. This is
            // the exact case AAP 0.6.2.3 narrows: co-residency is required, and residency alone is not
            // enough.
            {
                BlockedRouteScenario.PeerInAnotherSession,
                CrossDataWindowStatus.BlockedHandleNotCoResident,
                RetCode.E_INVALID_HANDLE,
                OraclePath + ":L82 (foreignvardata.expsvc is an in-process pointer)"
            },

            // CROSS-INSTANCE, simulated the only way a unit test honestly can: a handle that was never
            // registered in ANY local session is indistinguishable from one minted by another process,
            // because a handle carries no address and no process identity by design.
            {
                BlockedRouteScenario.PeerInAnotherServiceInstance,
                CrossDataWindowStatus.BlockedHandleNotCoResident,
                RetCode.E_INVALID_HANDLE,
                OraclePath + ":L2141 (the back-pointer the wire cannot carry)"
            },

            // THE SESSION WAS CLOSED. The legacy's equivalent release is the destructor at :L2425, after
            // which the pointer would be dangling; here it is refused instead.
            {
                BlockedRouteScenario.SessionClosed,
                CrossDataWindowStatus.BlockedSessionClosed,
                RetCode.E_NOT_EXISTS,
                OraclePath + ":L2425 (the destructor's deterministic release)"
            },

            // THE SESSION EXPIRED. Reached through the injected clock, never a wall clock.
            {
                BlockedRouteScenario.SessionExpired,
                CrossDataWindowStatus.BlockedSessionClosed,
                RetCode.E_NOT_EXISTS,
                OraclePath + ":L2425 (release, reached here by idle expiry)"
            },
        };

    /// <summary>
    /// THE BLOCK IS DECIDED BEFORE THE HOST IS CONTACTED AT ALL. A host that throws on every member is
    /// registered in one session and its handle resolved from another: the outcome is
    /// <see cref="CrossDataWindowStatus.BlockedHandleNotCoResident"/> and NOT
    /// <see cref="CrossDataWindowStatus.HostFaulted"/>, which is only possible if nothing was called.
    /// </summary>
    /// <remarks>
    /// This is the strongest available form of "no code path reaches across the boundary": the two
    /// statuses are produced by different code paths, so the status itself discriminates. A design that
    /// dereferenced first and checked afterwards would report the fault and fail this test.
    /// </remarks>
    [Fact]
    public void TheBlockIsDecidedWithoutEverTouchingTheForeignHost()
    {
        ExpressionSession owner = NewSession("owner-of-throwing-host");
        ExpressionSession stranger = NewSession("stranger");

        ThrowingProbeHost probe = new();
        DataWindowHandle handle = owner.Register(probe);

        ForeignVariableResolution byName = stranger.ResolveForeignVariable(handle, Summary);
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, byName.Status);
        Assert.NotEqual(CrossDataWindowStatus.HostFaulted, byName.Status);

        ForeignVariableResolution byReference =
            stranger.ResolveForeignVariable(new ForeignVariableReference(1, handle.Value));
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, byReference.Status);

        ExpressionValueResult value = stranger.CalcForeignVariableValue(
            new ForeignVariableReference(1, handle.Value),
            callerRow: 1L,
            callerDwo: null,
            caller: null);
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, value.Status);

        // The proof: not one member of the host was invoked.
        Assert.Equal(0, probe.CallCount);

        // And the SAME host, reached through the session that owns it, DOES fault - so the probe really
        // would have thrown had it been touched, and the assertion above is not vacuous.
        Assert.Equal(
            CrossDataWindowStatus.HostFaulted,
            owner.ResolveForeignVariable(handle, Summary).Status);
        Assert.Equal(1, probe.CallCount);
    }

    /// <summary>
    /// BLOCKED AND "UNDEFINED FOREIGN VARIABLE" ARE DIFFERENT CONDITIONS AND A CALLER CAN TELL THEM
    /// APART. "You never defined it" is a legacy failure with a legacy locator [<c>:L2133</c>]; "it
    /// exists but cannot be reached across this boundary" is this refactor's narrowing and has no legacy
    /// locator at all, because the legacy could not produce it.
    /// </summary>
    /// <remarks>
    /// Collapsing the two would be the worst available outcome: a caller told "undefined" would go and
    /// define the variable, which cannot fix a co-residency failure, and a caller told "blocked" would
    /// go and co-locate the DataWindows, which cannot fix a missing definition.
    /// </remarks>
    [Fact]
    public void TheBlockedErrorIsDistinguishableFromAnUndefinedForeignVariable()
    {
        ExpressionSession session = NewSession("distinguish");
        ExpressionSession other = NewSession("elsewhere");

        RecordingExpressionServiceHost peer = new();
        peer.AddVariable(Summary, "41");
        DataWindowHandle coResident = session.Register(peer);

        RecordingExpressionServiceHost remote = new();
        remote.AddVariable(Summary, "41");
        DataWindowHandle notCoResident = other.Register(remote);

        // (a) DEFINED but NOT REACHABLE -> the narrowing.
        ForeignVariableResolution blocked = session.ResolveForeignVariable(notCoResident, Summary);

        // (b) REACHABLE but NOT DEFINED -> the oracle's own failure at :L2133.
        const string UndefinedName = "neverDefinedAnywhere";
        ForeignVariableResolution undefined =
            session.ResolveForeignVariable(coResident, UndefinedName);

        // Neither resolved, and BOTH produced a real error - so the distinction is not "one of them
        // failed silently".
        Assert.False(blocked.IsResolved);
        Assert.False(undefined.IsResolved);
        Assert.NotNull(blocked.Error);
        Assert.NotNull(undefined.Error);

        // 1. DIFFERENT STATUS.
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, blocked.Status);
        Assert.Equal(CrossDataWindowStatus.VariableNotFound, undefined.Status);
        Assert.NotEqual(blocked.Status, undefined.Status);

        // 2. DIFFERENT `IsBlocked` PROJECTION - the single boolean a caller most likely branches on.
        Assert.True(blocked.IsBlocked);
        Assert.False(undefined.IsBlocked);

        // 3. DIFFERENT RETURN CODE. -11 against -21.
        Assert.Equal(RetCode.E_INVALID_HANDLE, blocked.ReturnCode);
        Assert.Equal(RetCode.E_VAR_NOT_FOUND, undefined.ReturnCode);
        Assert.NotEqual(blocked.ReturnCode, undefined.ReturnCode);

        // 4. DIFFERENT ERROR CATEGORY.
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, blocked.Error.Category);
        Assert.Equal(ExpressionErrorCategory.UndefinedName, undefined.Error.Category);

        // 5. DIFFERENT ERROR SITE, and the undefined one carries THE ORACLE'S OWN LINE NUMBER while the
        //    blocked one deliberately carries none - it is not attributed to a legacy line, because no
        //    legacy line produces it.
        Assert.Equal(ExpressionErrorSite.ForeignVariableUndefined, undefined.Error.Site);
        Assert.Equal(2133, undefined.Error.LegacyLine);
        Assert.Equal(ExpressionErrorSite.Unspecified, blocked.Error.Site);

        // 6. DIFFERENT TEXT. The undefined arm reproduces the oracle's message verbatim; the blocked arm
        //    names the unreachable handle and the session that could not reach it.
        Assert.Equal("外部变量[" + UndefinedName + "],未定义", undefined.Error.Text);
        Assert.DoesNotContain(notCoResident.Value, undefined.Error.Text, StringComparison.Ordinal);
        Assert.Contains(notCoResident.Value, blocked.Error.Text, StringComparison.Ordinal);
        Assert.Contains("distinguish", blocked.Error.Text, StringComparison.Ordinal);

        // Both are stop-sign severity, because both are refusals rather than advisories - the ONE
        // property they are allowed to share.
        Assert.Equal(ExpressionErrorSeverity.StopSign, blocked.Error.Severity);
        Assert.Equal(ExpressionErrorSeverity.StopSign, undefined.Error.Severity);
    }

    /// <summary>
    /// <see cref="ExpressionSession.CreateBlockedError"/> REFUSES to dress any other outcome up as this
    /// refactor's narrowing, so the blocked vocabulary cannot be borrowed for a legacy failure.
    /// </summary>
    /// <param name="status">The non-blocked status the row offers.</param>
    /// <param name="why">Why that status must not be reported as a block.</param>
    [Theory]
    [MemberData(nameof(NonBlockedStatuses))]
    public void CreateBlockedError_RefusesEveryStatusThatIsNotOneOfTheTwoBlockedArms(
        CrossDataWindowStatus status,
        string why)
    {
        Assert.False(string.IsNullOrWhiteSpace(why));

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ExpressionSession.CreateBlockedError(
                DataWindowHandle.From("dw_1"),
                "some-session",
                status));

        Assert.Equal("status", thrown.ParamName);
    }

    /// <summary>
    /// Every <see cref="CrossDataWindowStatus"/> that is NOT one of the two blocked arms.
    /// </summary>
    /// <returns>One row per non-blocked status.</returns>
    public static TheoryData<CrossDataWindowStatus, string> NonBlockedStatuses() =>
        new()
        {
            {
                CrossDataWindowStatus.Resolved,
                OraclePath + ":L2145 - a success is not a block, and reporting one as a block would fabricate a failure."
            },
            {
                CrossDataWindowStatus.VariableNotFound,
                "The oracle's own undefined-name failure at " + OraclePath + ":L2133 must keep its own identity."
            },
            {
                CrossDataWindowStatus.HostFaulted,
                OraclePath + ":L2199 - a host that threw is REACHABLE; calling that a block would hide a real fault."
            },
            {
                CrossDataWindowStatus.InvalidReference,
                OraclePath + ":L2119-L2120 - a malformed argument is the caller's error, not a boundary it cannot cross."
            },
        };

    /// <summary>
    /// The blocked outcome surfaces on contract C-04 as a DEFINED STATUS carrying a DEFINED CATEGORY -
    /// never as a successful response with a wrong value.
    /// </summary>
    /// <remarks>
    /// The full gRPC harness lives in <c>ColumnExpressionServiceContractTests</c>; what is asserted here
    /// is the PROJECTION itself, which is the piece the narrowing depends on: if the blocked error
    /// projected onto the wire's success code or onto the oracle's undefined-name category, every remote
    /// caller would be misinformed no matter how correct the session was.
    /// </remarks>
    [Fact]
    public void TheBlockedResultSurfacesOnC04AsADefinedStatusAndNeverAsSuccess()
    {
        ExpressionParseError blocked = ExpressionSession.CreateBlockedError(
            DataWindowHandle.From("dw_2"),
            "session-one",
            CrossDataWindowStatus.BlockedHandleNotCoResident);

        WireExpressionError? projected = ExpressionWireProjection.ToWire(blocked);
        Assert.NotNull(projected);

        // THE CATEGORY IS ITS OWN WIRE VALUE, distinct from the oracle's undefined-name category.
        Assert.Equal(WireErrorCategory.ForeignReferenceBlocked, projected.Category);
        Assert.NotEqual(WireErrorCategory.UndefinedName, projected.Category);
        Assert.NotEqual(WireErrorCategory.Unspecified, projected.Category);

        // THE RETURN CODE IS A FAILURE, and it is not the success value.
        Assert.NotNull(projected.Error);
        Assert.Equal(WireRetCode.EInvalidHandle, projected.Error.RetCode);
        Assert.NotEqual(WireRetCode.Ok, projected.Error.RetCode);

        // The text that reaches the caller NAMES THE UNREACHABLE HANDLE, so a remote operator can act on
        // it without access to this process.
        Assert.Contains("dw_2", projected.Error.Text, StringComparison.Ordinal);
        Assert.Contains("session-one", projected.Error.Text, StringComparison.Ordinal);

        // It is a PLAIN message, so it carries no caret - there is no expression position to point at,
        // and inventing one would imply a parse failure that did not happen.
        Assert.Equal(0L, projected.CaretPosition);
        Assert.Equal(string.Empty, projected.RenderedMarker);
        Assert.Equal(string.Empty, projected.CaughtExceptionText);

        // And the two blocked arms are BOTH categorised as blocked, so a closed session is not silently
        // demoted to some other failure on the wire.
        WireExpressionError? closed = ExpressionWireProjection.ToWire(
            ExpressionSession.CreateBlockedError(
                DataWindowHandle.From("dw_2"),
                "session-one",
                CrossDataWindowStatus.BlockedSessionClosed));
        Assert.NotNull(closed);
        Assert.Equal(WireErrorCategory.ForeignReferenceBlocked, closed.Category);
        Assert.NotNull(closed.Error);

        // SAME CATEGORY, DIFFERENT CODE. A remote caller sees one blocked category to branch on and a
        // code that still says WHICH block it was, so neither the shared handling nor the diagnosis is
        // lost on the wire.
        Assert.Equal(WireRetCode.ENotExists, closed.Error.RetCode);
        Assert.NotEqual(WireRetCode.Ok, closed.Error.RetCode);
        Assert.NotEqual(projected.Error.RetCode, closed.Error.RetCode);

        // The two arms are nevertheless different MESSAGES, so a diagnosis can still tell "wrong
        // session" from "session gone".
        Assert.NotEqual(projected.Error.Text, closed.Error.Text);
    }

    /// <summary>
    /// NO CODE PATH SERIALIZES THE EXPRESSION-SERVICE POINTER. The generated wire message for a foreign
    /// reference carries an INDEX plus a SESSION-SCOPED HANDLE and a resolvability flag - three scalars,
    /// and not one object reference.
    /// </summary>
    /// <remarks>
    /// Asserted by REFLECTION over the generated type rather than by reading the <c>.proto</c>, because
    /// the generated type is what actually crosses the wire. The legacy structure had exactly two
    /// fields, <c>integer index</c> and <c>n_cst_dwsvc_columnexp expsvc</c> [<c>:L80-L83</c>]; the wire
    /// message keeps the first, REPLACES the second with an opaque string, and adds the flag that lets a
    /// receiver see the reference is unreachable before it tries to use it.
    /// </remarks>
    [Fact]
    public void TheForeignReferenceWireMessageCarriesAnIndexAndAHandleAndNoObjectReference()
    {
        PropertyInfo[] declared = [.. typeof(WireForeignVarRef)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)];

        Assert.Equal(
            ["ForeignDatawindowHandle", "Index", "Resolvable"],
            declared.Select(static property => property.Name));

        // EVERY field is a scalar the wire can express. `object`, `dynamic`, a delegate, or any type
        // from this assembly would all be routes back to a pointer.
        Assert.Equal(
            [typeof(string), typeof(int), typeof(bool)],
            declared.Select(static property => property.PropertyType));

        foreach (PropertyInfo property in declared)
        {
            Assert.True(
                property.PropertyType.IsPrimitive || property.PropertyType == typeof(string),
                property.Name + " must be a primitive or a string; anything richer could carry a reference.");

            Assert.NotEqual(typeof(object), property.PropertyType);
            Assert.NotEqual(typeof(IExpressionServiceHost), property.PropertyType);
            Assert.NotEqual(typeof(ExpressionSession), property.PropertyType);
        }

        // NO member anywhere on the generated type mentions the engine, the host interface or the
        // session - so there is no back door through a method either.
        Assert.DoesNotContain(
            typeof(WireForeignVarRef).GetMembers(BindingFlags.Public | BindingFlags.Instance),
            static member => member.Name.Contains("ExpSvc", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)
                || member.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase));

        // The handle really is opaque: a plain string with no structure the receiver is asked to parse,
        // and it round-trips through the message unchanged.
        WireForeignVarRef message = new()
        {
            Index = 2,
            ForeignDatawindowHandle = "session-one/1",
            Resolvable = false,
        };

        Assert.Equal(2, message.Index);
        Assert.Equal("session-one/1", message.ForeignDatawindowHandle);
        Assert.False(message.Resolvable);

        // And the domain resolution projects onto exactly those two load-bearing values, so the wire
        // shape and the in-process shape cannot drift apart.
        ExpressionSession session = NewSession("session-one");
        RecordingExpressionServiceHost peer = new();
        peer.AddVariable(Summary, "9");
        DataWindowHandle handle = session.Register(peer);

        ForeignVariableReference reference =
            session.ResolveForeignVariable(handle, Summary).ToReference();

        WireForeignVarRef fromDomain = new()
        {
            Index = reference.Index,
            ForeignDatawindowHandle = reference.Handle,
            Resolvable = session.IsCoResident(handle),
        };

        Assert.Equal(1, fromDomain.Index);
        Assert.Equal(handle.Value, fromDomain.ForeignDatawindowHandle);
        Assert.True(fromDomain.Resolvable);

        // Close the session and the SAME reference is no longer resolvable - which is precisely what the
        // flag exists to tell a receiver.
        Assert.True(session.Close());
        Assert.False(session.IsCoResident(handle));
    }

    /// <summary>
    /// Driven through the ENGINE rather than the session: acquiring a foreign variable whose peer lives
    /// in another session is refused, and the refusal reaches the caller as a raised error with the
    /// blocked category - so the narrowing holds at the level an application actually calls.
    /// </summary>
    [Fact]
    public async Task AddingAForeignVariableAcrossTwoSessionsIsRefusedAtTheEngineLevel()
    {
        ExpressionSession sessionOne = NewSession("engine-session-one");
        ExpressionSession sessionTwo = NewSession("engine-session-two");

        (ColumnExpressionEngine dw2, FakeDataWindowHost hostTwo) = NewEngine(sessionTwo);
        (ColumnExpressionEngine dw1, FakeDataWindowHost hostOne) = NewEngine(sessionOne);

        Assert.NotSame(dw1.Session, dw2.Session);

        // The peer defines the variable perfectly well and has real data behind it.
        Assert.Equal(RetCode.OK, dw2.AddVarExp(Summary, SummaryExp));
        hostTwo.SetItem(1L, "n1", 10m);
        hostTwo.SetItem(2L, "n1", 20m);
        Assert.Equal(1, dw2.FindVarIndex(Summary));

        long baseline = dw1.RaisedErrorCount;

        // AND THE REFERENCE IS STILL REFUSED, because the two DataWindows are not co-resident.
        long code = dw1.AddForeignVar(Summary, dw2);

        Assert.Equal(RetCode.E_INVALID_HANDLE, code);
        Assert.NotEqual(RetCode.OK, code);
        Assert.Equal(baseline + 1L, dw1.RaisedErrorCount);

        Assert.NotNull(dw1.LastError);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, dw1.LastError.Category);
        Assert.Contains(dw2.Handle.Value, dw1.LastError.Text, StringComparison.Ordinal);

        // NOTHING WAS ADDED. The referencing engine has no such variable, so a later expression cannot
        // accidentally pick up a half-built entry.
        Assert.Equal(0, dw1.FindVarIndex(Summary));
        Assert.Equal(0, dw1.Variables.UpperBound);

        // No back-link was written on the peer either: the write at :L2139-L2141 is downstream of the
        // co-residency test, so a refused reference leaves BOTH sides untouched.
        Assert.Empty(dw2.Variables[dw2.FindVarIndex(Summary)].Links);

        // AN EXPRESSION MAY STILL NAME IT, AND THAT IS LEGACY BEHAVIOUR RATHER THAN A HOLE. A DYNAMIC
        // reference is bound BY NAME and resolved at CALCULATION time [:L2189-L2200], so the parser
        // accepts it - the oracle's undefined-variable check at [:L1422] sits on the STATIC branch only,
        // which is the same asymmetry section 4 pins from the other side. The refusal therefore lands
        // exactly where the cross-boundary read would have happened, which is the right place for it.
        Assert.Equal(RetCode.OK, dw1.AddVarExp("local", "$$" + Summary));
        Assert.Equal("$$" + Summary, dw1.GetVarExp("local"));

        long beforeCalc = dw1.RaisedErrorCount;
        Assert.True(dw1.AddExp("n1", "$$local") > 0);

        // `of_calcall` reports the PASS rather than the per-item outcome, so the aggregate code is not
        // the assertion; the raised error and the column's value are.
        _ = await dw1.CalcAllAsync(Ct);

        Assert.True(dw1.RaisedErrorCount > beforeCalc);
        Assert.NotNull(dw1.LastError);
        Assert.Equal(ExpressionErrorSite.PreprocessVariableUndefined, dw1.LastError.Site);
        Assert.Equal(ExpressionErrorCategory.Preprocess, dw1.LastError.Category);

        // THE DECISIVE ASSERTION: THE COLUMN NEVER RECEIVES THE PEER'S TOTAL. It keeps the fixture's own
        // value, so the refusal produced no data at all - not the peer's 30, and not a substituted
        // reading of it.
        Assert.NotEqual(30m, hostOne.GetItemDecimal(1L, "n1"));
        Assert.Equal(0m, hostOne.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A malformed reference is reported as MALFORMED and never as blocked, so "you passed me nothing"
    /// stays distinct from "you cannot reach that".
    /// </summary>
    /// <param name="index">The index the row offers.</param>
    /// <param name="handle">The handle the row offers.</param>
    /// <param name="expected">The <c>RetCode</c> constant the pair answers.</param>
    /// <param name="why">Why the pair is malformed rather than blocked.</param>
    [Theory]
    [MemberData(nameof(MalformedReferences))]
    public void AMalformedReferenceIsReportedAsMalformedRatherThanBlocked(
        int index,
        string handle,
        long expected,
        string why)
    {
        Assert.False(string.IsNullOrWhiteSpace(why));

        ExpressionSession session = NewSession("malformed");

        ForeignVariableResolution resolution =
            session.ResolveForeignVariable(new ForeignVariableReference(index, handle));

        Assert.Equal(CrossDataWindowStatus.InvalidReference, resolution.Status);
        Assert.False(resolution.IsBlocked);
        Assert.False(resolution.IsResolved);
        Assert.Equal(expected, resolution.ReturnCode);

        // A malformed reference produces NO error object at all - it never reached the boundary, so
        // there is no boundary failure to describe, and manufacturing one would imply a block.
        Assert.Null(resolution.Error);
    }

    /// <summary>
    /// The malformed-reference pairs and the code each answers.
    /// </summary>
    /// <returns>One row per malformed shape.</returns>
    public static TheoryData<int, string, long, string> MalformedReferences() =>
        new()
        {
            // An absent handle is checked FIRST, so it wins even when the index is also wrong.
            {
                0,
                "",
                RetCode.E_INVALID_OBJECT,
                "An absent handle mirrors the oracle's IsValidObject guard at " + OraclePath + ":L2120."
            },
            {
                1,
                "",
                RetCode.E_INVALID_OBJECT,
                "A valid index cannot rescue an absent handle."
            },

            // A present handle with a non-positive index is a bad ARGUMENT: the oracle's indexes are
            // one-based, so zero and negatives are outside the domain entirely.
            {
                0,
                "dw_1",
                RetCode.E_INVALID_ARGUMENT,
                "Zero is outside a one-based index domain (AAP 0.4.5.4)."
            },
            {
                -1,
                "dw_1",
                RetCode.E_INVALID_ARGUMENT,
                "A negative index is outside a one-based index domain (AAP 0.4.5.4)."
            },
        };

    /// <summary>
    /// An empty variable NAME is a malformed argument too, and it is answered before any handle lookup -
    /// matching the oracle's own guard order at <c>:L2119-L2120</c>, where the name is tested first.
    /// </summary>
    [Fact]
    public void AnEmptyForeignVariableNameIsRefusedBeforeAnyHandleLookup()
    {
        ExpressionSession session = NewSession("empty-name");
        RecordingExpressionServiceHost peer = new();
        DataWindowHandle handle = session.Register(peer);

        foreach (string? name in new[] { null, string.Empty })
        {
            ForeignVariableResolution resolution = session.ResolveForeignVariable(handle, name);

            Assert.Equal(CrossDataWindowStatus.InvalidReference, resolution.Status);
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, resolution.ReturnCode);
            Assert.Null(resolution.Error);
            Assert.False(resolution.IsBlocked);
        }

        // The name guard runs BEFORE the handle guard, so an absent name beats an absent handle - the
        // oracle's order, preserved.
        ForeignVariableResolution both =
            session.ResolveForeignVariable(DataWindowHandle.None, string.Empty);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, both.ReturnCode);
        Assert.NotEqual(RetCode.E_INVALID_OBJECT, both.ReturnCode);
    }


    // ==============================================================================================
    //  SECTION 4 - STATIC EXPANSION OF A FOREIGN REFERENCE IS AN ERROR  (C-B / G2)
    //  --------------------------------------------------------------------------------------------
    //  This is THE LEGACY'S OWN RULE, reproduced because it is behaviour - not relaxed because the port
    //  finds it inconvenient, and not tightened because the narrowing would have liked it anyway.
    //
    //      [:L1426]  "解析变量宏失败!\n外部变量[{1}],不支持静态展开"   E_INVALID_ARGUMENT
    //      [:L1417]  "解析函数宏失败!\n上下文变量[{1}],不支持静态展开"  E_INVALID_ARGUMENT
    //      [:L1308]  "解析函数宏失败!\n不支持引用上下文的函数宏"        E_INVALID_ARGUMENT
    //
    //  The three flow through the same expansion-mode validation because they are the same concern:
    //  resolving against a service that is not the local one, at a moment when that service cannot be
    //  read. Each pair of assertions pins the DISTINCTION rather than merely the failure - the dynamic
    //  form of the identical reference is asserted to SUCCEED.
    // ==============================================================================================

    /// <summary>
    /// Each refusal reproduces its oracle line's text, code and severity exactly, and each is attributed
    /// to its own site so a caller can tell the three apart.
    /// </summary>
    /// <param name="refusal">Which refusal the row drives.</param>
    /// <param name="expectedSite">The site - whose numeric value IS the oracle line.</param>
    /// <param name="expectedText">The message, transcribed verbatim from the oracle.</param>
    [Theory]
    [MemberData(nameof(StaticExpansionRefusals))]
    public void EachStaticExpansionRefusalReproducesItsOracleLineVerbatim(
        StaticExpansionRefusal refusal,
        ExpressionErrorSite expectedSite,
        string expectedText)
    {
        // The scanner's positions are indices into the TRAILER-EXTENDED expression `exp + ";"`
        // [:L1251, :L1262], so every syntax below carries the trailer the oracle appends.
        const long CaretPosition = 3L;

        ExpressionParseError error = refusal switch
        {
            StaticExpansionRefusal.ForeignVariable => ExpressionSession.RejectForeignStaticExpansion(
                "$" + Summary + ";",
                CaretPosition,
                Summary),

            StaticExpansionRefusal.ContextVariable =>
                ExpressionSession.RejectContextVariableStaticExpansion("@$" + Summary + ";", CaretPosition, Summary),

            StaticExpansionRefusal.ContextFunctionMacro =>
                ExpressionSession.RejectContextFunctionMacro("@$Fn();", CaretPosition),

            _ => throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "Every declared refusal must be driven; an unhandled one would pass vacuously."),
        };

        // THE SITE IS THE ORACLE LINE. `ExpressionErrorSite`'s numeric values are the line numbers, so
        // the attribution cannot drift from the source it claims.
        Assert.Equal(expectedSite, error.Site);
        Assert.Equal((int)expectedSite, error.LegacyLine);

        // THE MESSAGE, verbatim - and it is the LEADING text, because a caret-bearing error appends the
        // rendered expression and marker after it.
        Assert.StartsWith(expectedText, error.Text, StringComparison.Ordinal);
        Assert.Contains(ParseErrorFormatter.LineSeparator, error.Text, StringComparison.Ordinal);

        // THE CODE. All three are malformed-argument refusals in the oracle, and all three stay so.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);

        // A CARET-BEARING PARSE ERROR, positioned where the caller said.
        Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, error.Family);
        Assert.Equal(ExpressionErrorCategory.Parse, error.Category);
        Assert.Equal(CaretPosition, error.CaretPosition);
        Assert.NotNull(error.RenderedMarker);
        Assert.Contains(ParseErrorFormatter.CaretCharacter, error.RenderedMarker);
        Assert.Contains(ParseErrorFormatter.PaddingCharacter, error.RenderedMarker);

        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, error.Title);

        // PRESERVED INCONSISTENCY (C-B). These messages are hardcoded Chinese and do NOT route through
        // localization, unlike the equivalent messages elsewhere in the DataWindow service layer which
        // do. The formatter reports what the oracle did rather than harmonising it.
        Assert.False(error.Localized);

        // AND THIS IS NOT THIS REFACTOR'S NARROWING. A legacy refusal must never wear the blocked
        // category, or the two would be indistinguishable to a caller.
        Assert.NotEqual(ExpressionErrorCategory.ForeignReferenceBlocked, error.Category);
    }

    /// <summary>
    /// The three static-expansion refusals, each with its oracle site and its verbatim message.
    /// </summary>
    /// <returns>One row per refusal.</returns>
    public static TheoryData<StaticExpansionRefusal, ExpressionErrorSite, string> StaticExpansionRefusals() =>
        new()
        {
            // :L1426 - the one this file exists for. A foreign value lives behind a live pointer, so it
            // cannot be substituted at bind time.
            {
                StaticExpansionRefusal.ForeignVariable,
                ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
                "解析变量宏失败!\n外部变量[" + Summary + "],不支持静态展开"
            },

            // :L1417 - the sibling. Note the oracle says "function macro" although the failing construct
            // is a VARIABLE; that wording is reproduced rather than corrected.
            {
                StaticExpansionRefusal.ContextVariable,
                ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
                "解析函数宏失败!\n上下文变量[" + Summary + "],不支持静态展开"
            },

            // :L1308 - a context-scoped FUNCTION macro is unsupported outright, in either expansion mode.
            {
                StaticExpansionRefusal.ContextFunctionMacro,
                ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
                "解析函数宏失败!\n不支持引用上下文的函数宏"
            },
        };

    /// <summary>
    /// Driven end to end through two co-resident engines: the STATIC form of a foreign reference is
    /// refused at <c>:L1426</c> and the DYNAMIC form of the IDENTICAL reference succeeds. The pair is
    /// what pins the distinction; either assertion alone would be satisfied by an implementation that
    /// simply rejected, or simply accepted, both.
    /// </summary>
    [Fact]
    public async Task StaticExpansionOfAForeignReferenceIsRefusedWhileTheDynamicFormSucceeds()
    {
        ExpressionSession session = NewSession("static-versus-dynamic");

        (ColumnExpressionEngine dw2, FakeDataWindowHost hostTwo) = NewEngine(session);
        (ColumnExpressionEngine dw1, FakeDataWindowHost hostOne) = NewEngine(session);

        Assert.Equal(RetCode.OK, dw2.AddVarExp(Summary, SummaryExp));
        hostTwo.SetItem(1L, "n1", 4m);
        hostTwo.SetItem(2L, "n1", 5m);

        // Co-resident, so the reference itself is legal.
        Assert.Equal(RetCode.OK, dw1.AddForeignVar(Summary, dw2));

        long baseline = dw1.RaisedErrorCount;

        // ---- THE STATIC FORM: a SINGLE sigil. Refused. ----
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, dw1.AddVarExp("staticUse", "$" + Summary));
        Assert.Equal(baseline + 1L, dw1.RaisedErrorCount);

        Assert.NotNull(dw1.LastError);
        ExpressionParseError refused = dw1.LastError;
        Assert.Equal(ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported, refused.Site);
        Assert.Equal(1426, refused.LegacyLine);
        Assert.StartsWith(
            "解析变量宏失败!\n外部变量[" + Summary + "],不支持静态展开",
            refused.Text,
            StringComparison.Ordinal);

        // The caret points INSIDE the trailer-extended expression, and the rendered marker shows it.
        Assert.NotNull(refused.CaretPosition);
        Assert.True(refused.CaretPosition > 0L);
        Assert.Equal("$" + Summary + ColumnExpressionEngine.TRAILER, refused.Expression);

        // Nothing was defined by the refused call, so a later expression cannot pick up a half-built one.
        Assert.Equal(string.Empty, dw1.GetVarExp("staticUse"));

        // ---- THE DYNAMIC FORM: a DOUBLED sigil. Accepted, and the name is RETAINED in the stored text,
        //      which is the mechanism that lets it be re-read at calculation time. ----
        Assert.Equal(RetCode.OK, dw1.AddVarExp("dynamicUse", "$$" + Summary));
        Assert.Equal("$$" + Summary, dw1.GetVarExp("dynamicUse"));
        Assert.Contains(Summary, dw1.GetVarExp("dynamicUse"), StringComparison.Ordinal);

        // No further error was raised by the accepted call.
        Assert.Equal(baseline + 1L, dw1.RaisedErrorCount);

        // And it CALCULATES, reading the peer's live total.
        Assert.True(dw1.AddExp("n1", "$$dynamicUse") > 0);
        Assert.Equal(RetCode.OK, await dw1.CalcAllAsync(Ct));
        Assert.Equal(9m, hostOne.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The CONTEXT sibling rule, driven the same way: <c>@$name</c> is refused at <c>:L1417</c> and
    /// <c>@$$name</c> is accepted - the only context macro form the oracle permits.
    /// </summary>
    /// <remarks>
    /// The context sigil <c>@</c> [<c>:L1253</c>] appears in no specification but is real, is scanned as a
    /// first-class sigil beside <c>$</c> [<c>:L1292-L1300</c>], and sets <c>vardata.isCtx</c>
    /// [<c>:L1413</c>]. It matters here because a context reference resolves against a service that is
    /// not the local one [<c>:L2180-L2187</c>] - the same shape as a foreign reference, hence the same
    /// refusal and the same session scope.
    /// </remarks>
    [Fact]
    public void TheContextVariableSiblingRuleSharesTheSameValidation()
    {
        ExpressionSession session = NewSession("context-sibling");
        (ColumnExpressionEngine engine, _) = NewEngine(session);

        Assert.Equal(RetCode.OK, engine.AddVar("ctxVar", 5L));

        // ---- STATIC context reference: refused at :L1417. ----
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp("ctxStatic", "@$ctxVar"));

        Assert.NotNull(engine.LastError);
        ExpressionParseError refused = engine.LastError;
        Assert.Equal(
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            refused.Site);
        Assert.Equal(1417, refused.LegacyLine);
        Assert.StartsWith(
            "解析函数宏失败!\n上下文变量[ctxVar],不支持静态展开",
            refused.Text,
            StringComparison.Ordinal);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, refused.ReturnCode);

        // ---- DYNAMIC context reference: accepted. ----
        Assert.Equal(RetCode.OK, engine.AddVarExp("ctxDynamic", "@$$ctxVar"));

        // The sigil projection agrees with both outcomes, so the parser and the projection cannot drift.
        Assert.Equal(
            ExpansionRejection.ContextStaticExpansion,
            ExpansionSigils.ContextStaticMacro.ProjectVariableReference().Rejection);
        Assert.Equal(
            ExpansionRejection.None,
            ExpansionSigils.ContextDynamicMacro.ProjectVariableReference().Rejection);
    }

    /// <summary>
    /// A CONTEXT variable is resolved through the SAME session registry as a foreign one, so an
    /// unreachable context host is BLOCKED by the same mechanism and reported with the same vocabulary.
    /// </summary>
    /// <remarks>
    /// Scoping the session around foreign variables alone would leave the context path homeless: it too
    /// calls into a service the local one was merely handed [<c>:L2180-L2187</c>].
    /// </remarks>
    [Fact]
    public void AContextVariableIsScopedByTheSameSessionAndBlockedTheSameWay()
    {
        ExpressionSession owner = NewSession("context-owner");
        ExpressionSession stranger = NewSession("context-stranger");

        RecordingExpressionServiceHost contextHost = new();
        contextHost.AddVariable("ctxVar", "77");
        contextHost.Items["s1"] = "from-context";
        DataWindowHandle contextHandle = owner.Register(contextHost);

        FakeDataWindowObject contextColumn = new("s1", "char(100)", id: 4L);
        ExpressionContext context = new(2L, contextColumn, contextHandle);
        Assert.True(context.IsSpecified);

        // ---- Through the OWNING session: the context host resolves. ----
        ForeignVariableResolution resolvedHost = owner.ResolveContextHost(context);
        Assert.True(resolvedHost.IsResolved);
        Assert.Same(contextHost, resolvedHost.Host);

        // A non-macro context reference reads the COLUMN [:L2185].
        VariableReference columnRead = new(Name: "s1", FullName: "@s1", IsCtx: true, IsMacro: false);
        ExpressionValueResult read =
            owner.CalcContextVariableValue(columnRead, context, 1L, null, null);
        Assert.True(read.Succeeded);
        Assert.Equal("from-context", read.Value);

        // ---- Through ANY OTHER session: BLOCKED, with the same category and the same code. ----
        ForeignVariableResolution blockedHost = stranger.ResolveContextHost(context);
        Assert.Equal(CrossDataWindowStatus.BlockedHandleNotCoResident, blockedHost.Status);
        Assert.True(blockedHost.IsBlocked);
        Assert.Null(blockedHost.Host);
        Assert.NotNull(blockedHost.Error);
        Assert.Equal(ExpressionErrorCategory.ForeignReferenceBlocked, blockedHost.Error.Category);

        ExpressionValueResult blockedRead =
            stranger.CalcContextVariableValue(columnRead, context, 1L, null, null);
        Assert.True(blockedRead.IsBlocked);
        Assert.False(blockedRead.Succeeded);
        Assert.Equal(string.Empty, blockedRead.Value);
        Assert.NotEqual("from-context", blockedRead.Value);
        Assert.Equal(RetCode.E_INVALID_HANDLE, blockedRead.ReturnCode);

        // An UNSPECIFIED context is malformed rather than blocked: nothing was named, so nothing was
        // unreachable.
        ForeignVariableResolution unspecified = owner.ResolveContextHost(ExpressionContext.None);
        Assert.Equal(CrossDataWindowStatus.InvalidReference, unspecified.Status);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, unspecified.ReturnCode);
        Assert.False(unspecified.IsBlocked);

        // And a NON-context reference is refused by the context entry point, because the local table is
        // the engine's business and not the session's [:L2180].
        VariableReference notContext = new(Name: "s1", FullName: "s1", IsCtx: false, IsMacro: false);
        ExpressionValueResult wrongDoor =
            owner.CalcContextVariableValue(notContext, context, 1L, null, null);
        Assert.Equal(CrossDataWindowStatus.InvalidReference, wrongDoor.Status);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, wrongDoor.ReturnCode);
    }


    // ==============================================================================================
    //  SECTION 5 - THE CALCULATION AND RECURSION STACK  (AAP 0.4.5.4, risk R9)
    //  --------------------------------------------------------------------------------------------
    //  The stack is ONE-BASED and its ordering travels on contract C-04's trace payload, so an
    //  off-by-one here does not merely misbehave - IT CHANGES EVERY STORED CHARACTERIZATION RECORDING.
    //  Every index expectation below is the oracle's own inclusive `1 .. Count()` form.
    // ==============================================================================================

    /// <summary>
    /// The stack is created with a reservation, and that reservation comes from
    /// <c>ColumnExpression:CalcStackInitialCapacity</c> - default 20, matching the oracle's
    /// <c>Reserve(20)</c> in the constructor [<c>:L2421-L2422</c>].
    /// </summary>
    /// <remarks>
    /// The reservation is A CAPACITY HINT AND NOT A DEPTH LIMIT: the oracle's recursion guard is a NAME
    /// scan [<c>:L682-L690</c>], never a depth check, so a stack deeper than 20 is legal and is asserted
    /// to be.
    /// </remarks>
    [Fact]
    public void TheCalcStackIsReservedFromConfigurationAndTheReservationIsNotADepthLimit()
    {
        // The shipped default is the oracle's own 20, and it is exposed as a named constant rather than
        // being written out twice.
        Assert.Equal(20, new ColumnExpressionOptions().CalcStackInitialCapacity);
        Assert.Equal(20, ExpressionCalcStack.DefaultInitialCapacity);

        ExpressionSession session = NewSession("reservation");
        Assert.Equal(20, session.CalcStackInitialCapacity);

        DataWindowHandle handle = session.Register(new RecordingExpressionServiceHost());
        ExpressionCalcStack? lookup = session.CalcStackFor(handle);
        Assert.NotNull(lookup);
        ExpressionCalcStack stack = lookup;

        Assert.Equal(20, stack.InitialCapacity);

        // THE RESERVATION REACHED THE UNDERLYING ONE-BASED VECTOR. Asserted against the container
        // itself rather than against a remembered number: a `Vector` reserved the same way reports the
        // same `MaxSize()`, and the `ulong` type of `Capacity` is the container's own signature.
        Vector reserved = new();
        Assert.True(reserved.Reserve(20UL));
        Assert.Equal(reserved.MaxSize(), stack.Capacity);
        Assert.Equal(20UL, stack.Capacity);
        Assert.Equal(0, stack.Depth);

        // AND THE WRAPPER DOES NOT CHANGE THE CONTAINER'S SEMANTICS. The same append / read / remove
        // sequence performed on the raw one-based container and on the stack produces the same ordering,
        // which is what lets the oracle's `1 .. Count()` loops be transcribed literally.
        reserved.Append("n1");
        reserved.Append("n2");
        Assert.Equal(2UL, reserved.Count());
        Assert.Equal("n1", reserved.GetAt(1UL));
        Assert.Equal("n2", reserved.GetAt(2UL));

        Assert.Equal(1, stack.Push("n1"));
        Assert.Equal(2, stack.Push("n2"));
        Assert.Equal(reserved.GetAt(1UL), stack.GetAt(1));
        Assert.Equal(reserved.GetAt(2UL), stack.GetAt(2));

        reserved.RemoveAt(1UL);
        stack.Pop(1);
        Assert.Equal((int)reserved.Count(), stack.Depth);
        Assert.Equal(reserved.GetAt(1UL), stack.GetAt(1));

        stack.Clear();
        Assert.Equal(0, stack.Depth);

        // NOT A DEPTH LIMIT. Twenty-five frames is legal, the depth reports 25, and the container simply
        // grew - which is what makes the oracle's unbounded name scan correct rather than lucky.
        for (int frame = 1; frame <= 25; frame++)
        {
            Assert.Equal(
                frame,
                stack.Push("f" + frame.ToString(CultureInfo.InvariantCulture)));
        }

        Assert.Equal(25, stack.Depth);
        Assert.True(stack.Capacity >= 25UL);

        // A CONFIGURED value is honoured end to end, and a non-positive one falls back to the oracle's
        // 20 rather than producing a zero-capacity stack.
        ExpressionSession configured = NewSession("configured", stackCapacity: 7);
        DataWindowHandle configuredHandle = configured.Register(new RecordingExpressionServiceHost());
        Assert.Equal(7, configured.CalcStackInitialCapacity);
        ExpressionCalcStack? configuredLookup = configured.CalcStackFor(configuredHandle);
        Assert.NotNull(configuredLookup);
        Assert.Equal(7, configuredLookup.InitialCapacity);
        Assert.Equal(7UL, configuredLookup.Capacity);

        Assert.Equal(20, NewSession("fallback", stackCapacity: 0).CalcStackInitialCapacity);
        Assert.Equal(20, NewSession("negative", stackCapacity: -5).CalcStackInitialCapacity);

        // A stack cannot be built with a non-positive reservation directly either: a zero-capacity stack
        // would silently disable recursion detection AND the trace, so it is refused loudly.
        Assert.Throws<ArgumentOutOfRangeException>(static () => new ExpressionCalcStack(0));
        Assert.Throws<ArgumentOutOfRangeException>(static () => new ExpressionCalcStack(-1));
    }

    /// <summary>
    /// PUSH IS AN APPEND THAT RETURNS THE ONE-BASED POSITION, AND POP NAMES THE CAPTURED INDEX - not
    /// "remove the last element" [<c>:L296-L297, :L318</c>].
    /// </summary>
    /// <remarks>
    /// The difference is observable rather than stylistic: the recursion between the push and the pop can
    /// leave the count changed, and a remove-last would then discard the WRONG FRAME. The assertion below
    /// pops the FIRST captured index while two later frames are still on the stack, which no remove-last
    /// implementation can satisfy.
    /// </remarks>
    [Fact]
    public void PushAppendsAtAOneBasedPositionAndPopRemovesTheCapturedIndex()
    {
        ExpressionCalcStack stack = new();

        // The first push is position 1, not 0. This single assertion is the one-based contract.
        int outer = stack.Push("n1");
        Assert.Equal(1, outer);

        int inner = stack.Push("n2");
        Assert.Equal(2, inner);

        int deepest = stack.Push("n3");
        Assert.Equal(3, deepest);

        Assert.Equal(3, stack.Depth);
        Assert.Equal(["n1", "n2", "n3"], stack.Frames);

        // Read back by ONE-BASED index. Index 1 is the outermost frame.
        Assert.Equal("n1", stack.GetAt(1));
        Assert.Equal("n2", stack.GetAt(2));
        Assert.Equal("n3", stack.GetAt(3));

        // Index 0 is NOT the first element - it is outside the domain - and neither is one past the end.
        Assert.Null(stack.GetAt(0));
        Assert.Null(stack.GetAt(-1));
        Assert.Null(stack.GetAt(4));

        // POP THE CAPTURED INDEX OF THE OUTERMOST FRAME while the two later frames remain. A remove-last
        // implementation would drop "n3" here and leave "n1" behind.
        stack.Pop(outer);

        Assert.Equal(2, stack.Depth);
        Assert.Equal(["n2", "n3"], stack.Frames);
        Assert.False(stack.ContainsFrame("n1"));

        // The surviving frames closed up, so the ordering is stable and still one-based.
        Assert.Equal("n2", stack.GetAt(1));
        Assert.Equal("n3", stack.GetAt(2));

        // A non-positive captured index is a NO-OP rather than a removal, because the oracle's `k` is
        // only ever a real position and a guard is cheaper than a corrupted stack.
        stack.Pop(0);
        stack.Pop(-1);
        Assert.Equal(2, stack.Depth);

        // Clearing empties the stack while keeping it usable, which is what the session does on release.
        stack.Clear();
        Assert.Equal(0, stack.Depth);
        Assert.Empty(stack.Frames);
        Assert.Equal(1, stack.Push("n1"));
    }

    /// <summary>
    /// RECURSION DETECTION IS A ONE-BASED FORWARD NAME SCAN THAT RETURNS THE FIRST MATCH
    /// [<c>:L682-L690</c>] - never a depth check, and never a search from the top.
    /// </summary>
    [Fact]
    public void RecursionDetectionIsAOneBasedForwardNameScanReturningTheFirstMatch()
    {
        ExpressionCalcStack stack = new();

        // An empty stack matches nothing, and a miss is reported as 0 - which is outside the one-based
        // domain and therefore unambiguous.
        Assert.Equal(0, stack.IndexOfFrame("n1"));
        Assert.False(stack.ContainsFrame("n1"));
        Assert.Equal(0, stack.IndexOfFrame(null));

        stack.Push("n1");
        stack.Push("n2");
        stack.Push("n1");

        // THE SAME NAME PUSHED TWICE IS DETECTED, which is exactly the condition the oracle refuses.
        Assert.True(stack.ContainsFrame("n1"));

        // FORWARD, so the FIRST occurrence wins - position 1, not the later position 3. Scanning from the
        // top would answer 3 and would change which frame a diagnosis blames.
        Assert.Equal(1, stack.IndexOfFrame("n1"));
        Assert.Equal(2, stack.IndexOfFrame("n2"));

        // The comparison is ORDINAL, so a case difference is a different name. The oracle compares
        // PowerScript strings, whose column names are handed back with the DataWindow's own casing.
        Assert.Equal(0, stack.IndexOfFrame("N1"));
        Assert.False(stack.ContainsFrame("N1"));

        // A miss on a populated stack is still 0, and a name that is a substring of a frame is not a
        // match - the scan compares whole names.
        Assert.Equal(0, stack.IndexOfFrame("n"));
        Assert.Equal(0, stack.IndexOfFrame("n1 "));
    }

    /// <summary>
    /// THE <c>recursive</c> FLAG DECIDES WHETHER A DETECTED RECURSION IS PERMITTED OR REFUSED, and the
    /// engine reads THE SESSION'S OWN STACK to decide [<c>:L682-L690</c>]. Both arms are asserted, driven
    /// through a frame pushed onto the session's stack.
    /// </summary>
    /// <remarks>
    /// This is also the test that proves the ownership move is real: the stack now lives on the session,
    /// and the engine's re-entry guard reads it from there. A separate engine-private stack would make
    /// the pushed frame invisible and both arms would report "permitted".
    /// </remarks>
    /// <param name="recursive">The expression's <c>recursive</c> flag.</param>
    /// <param name="expectedPermitted">Whether the re-entry is permitted with that flag.</param>
    /// <param name="locator">The oracle line that decides it.</param>
    [Theory]
    [MemberData(nameof(RecursionFlagArms))]
    public async Task TheRecursiveFlagDecidesWhetherADetectedRecursionIsPermitted(
        bool recursive,
        bool expectedPermitted,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        // A FRESH session and engine per arm, so no calculation state carries over between them.
        ExpressionSession session = NewSession("recursive-" + (recursive ? "on" : "off"));
        (ColumnExpressionEngine engine, _) = NewEngine(session);

        int index = engine.AddExp("n1", "1");
        Assert.Equal(1, index);

        if (recursive)
        {
            Assert.Equal(RetCode.OK, engine.SetRecursive("n1", true));
        }

        ExpressionCalcStack? lookup = session.CalcStackFor(engine.Handle);
        Assert.NotNull(lookup);
        ExpressionCalcStack stack = lookup;

        // Put the expression's OWN column name on the stack, which is the state the oracle's guard exists
        // to notice: "n1 is already being calculated further up".
        int captured = stack.Push("n1");
        Assert.Equal(1, captured);
        Assert.True(stack.ContainsFrame("n1"));

        bool permitted = await engine._of_calcitem(1L, index, Ct);

        Assert.Equal(expectedPermitted, permitted);

        // The guard neither consumed nor disturbed the frame - it only READ it, so the caller that pushed
        // still owns the pop.
        Assert.Equal(1, stack.Depth);
        Assert.Equal("n1", stack.GetAt(1));

        stack.Pop(captured);
        Assert.Equal(0, stack.Depth);
    }

    /// <summary>
    /// The two arms of the <c>recursive</c> flag, with the oracle line that decides each.
    /// </summary>
    /// <returns>One row per arm.</returns>
    public static TheoryData<bool, bool, string> RecursionFlagArms() =>
        new()
        {
            // `if Not ColExpDatas[index].recursive then` - the flag OFF means the name scan runs, finds
            // the frame, and the oracle answers `return false`.
            { false, false, OraclePath + ":L683-L690" },

            // The flag ON means the scan is SKIPPED ENTIRELY - not "runs and forgives", which would still
            // pay for the walk and would behave differently under a partial match.
            { true, true, OraclePath + ":L683" },
        };

    /// <summary>
    /// THE STACK ORDERING IS OBSERVABLE ON THE C-04 TRACE PAYLOAD: every frame is followed by
    /// <c>"&gt;"</c> and then the current column's own name is appended, SO THE FINAL SEGMENT CARRIES NO
    /// TRAILING DELIMITER [<c>:L751-L758</c>].
    /// </summary>
    /// <param name="frames">The frames to push, outermost first.</param>
    /// <param name="terminal">The current column's name, appended after the loop.</param>
    /// <param name="expectedStack">The exact payload the oracle's loop produces.</param>
    /// <param name="locator">The oracle line that builds it.</param>
    [Theory]
    [MemberData(nameof(CallStackShapes))]
    public void TheTracePayloadJoinsTheOneBasedFramesAndAppendsTheTerminalWithoutADelimiter(
        string[] frames,
        string terminal,
        string expectedStack,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        RecordingTraceSink sink = new();
        DeterministicTimeProvider clock = new();
        ExpressionSession session = NewSession("trace", clock: clock, trace: true, traceSink: sink);
        Assert.True(session.TraceEnabled);

        RecordingExpressionServiceHost host = new();
        DataWindowHandle handle = session.Register(host);

        ExpressionCalcStack? lookup = session.CalcStackFor(handle);
        Assert.NotNull(lookup);
        ExpressionCalcStack stack = lookup;

        foreach (string frame in frames)
        {
            stack.Push(frame);
        }

        // The stack builds the payload directly, and the session builds the same payload through the
        // record - so the two cannot disagree.
        Assert.Equal(expectedStack, stack.BuildCallStack(terminal));

        FakeDataWindowObject column = new(terminal, "decimal(2)", id: 1L);
        ExpressionTraceRecord? emitted = session.EmitTrace(handle, 3L, column, "$$x + 1", "42.00");
        Assert.NotNull(emitted);
        ExpressionTraceRecord record = emitted;

        Assert.Equal(expectedStack, record.Stack);
        Assert.Equal(frames, record.StackFrames);
        Assert.Equal(frames.Length, record.Depth);
        Assert.Equal(terminal, record.ColumnName);

        // THE TERMINAL SEGMENT HAS NO TRAILING DELIMITER, and there is exactly one delimiter per frame.
        Assert.False(record.Stack.EndsWith('>'));
        Assert.Equal(frames.Length, record.Stack.Count(static character => character == '>'));
        Assert.EndsWith(terminal, record.Stack, StringComparison.Ordinal);

        // The rest of the record is carried through unchanged, and the timestamp comes from the injected
        // clock rather than a wall clock.
        Assert.Equal(session.SessionId, record.SessionId);
        Assert.Equal(handle, record.Handle);
        Assert.Equal(3L, record.Row);
        Assert.Equal("$$x + 1", record.Expression);
        Assert.Equal("42.00", record.Value);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, record.Timestamp);

        // The token is monotonic within its session, which is the ordering pattern (a) the plan assigns
        // to the trace: a sequencing token for detection, because the trace is fire-and-forget diagnostics
        // rather than a step the calculation waits on.
        Assert.Equal(1L, record.SequenceNumber);
        ExpressionTraceRecord? second = session.EmitTrace(handle, 3L, column, "$$x + 1", "42.00");
        Assert.NotNull(second);
        Assert.Equal(2L, second.SequenceNumber);

        // The sink received both, in order, and the delivery accounting agrees.
        Assert.Equal(2, sink.Records.Count);
        Assert.Equal([1L, 2L], sink.Records.Select(static entry => entry.SequenceNumber));
        Assert.Equal(2L, session.TraceDelivery.Attempted);
        Assert.Equal(2L, session.TraceDelivery.Delivered);
        Assert.Equal(0L, session.TraceDelivery.Failed);
        Assert.Null(session.TraceDelivery.LastFailureType);
    }

    /// <summary>
    /// The call-stack shapes, from an empty stack up to three levels of nesting.
    /// </summary>
    /// <returns>One row per nesting depth.</returns>
    public static TheoryData<string[], string, string, string> CallStackShapes() =>
        new()
        {
            // ZERO frames: just the terminal, with no delimiter at all - the loop body never runs.
            { [], "n1", "n1", OraclePath + ":L757" },

            // ONE frame.
            { ["n1"], "n2", "n1>n2", OraclePath + ":L753-L757" },

            // TWO levels - the shape the plan cites.
            { ["outer", "inner"], "current", "outer>inner>current", OraclePath + ":L753-L757" },

            // THREE levels.
            {
                ["outer", "middle", "inner"],
                "current",
                "outer>middle>inner>current",
                OraclePath + ":L753-L757"
            },

            // The oracle's own suffixed computed-column names, which is what a real recording contains.
            {
                ["n2_columnexp", "n3_columnexp"],
                "s1_columnexp",
                "n2_columnexp>n3_columnexp>s1_columnexp",
                OraclePath + ":L753-L757"
            },
        };

    /// <summary>
    /// THE <c>"(null)"</c> SENTINEL. The oracle selects it with
    /// <c>iif(sVal = "" and (emptyStringIsNull or colType &lt;&gt; COL_TYPE_STRING), "(null)", sVal)</c>
    /// [<c>:L758</c>], and the session reports whatever was selected VERBATIM - it never re-derives,
    /// normalises or second-guesses the choice.
    /// </summary>
    /// <param name="value">The evaluated value.</param>
    /// <param name="emptyStringIsNull">The expression's <c>emptyStringIsNull</c> flag.</param>
    /// <param name="colType">The column type constant.</param>
    /// <param name="expectedReported">What the oracle's <c>iif</c> reports for that combination.</param>
    /// <param name="locator">The oracle line and the limb of the condition the row exercises.</param>
    [Theory]
    [MemberData(nameof(NullSentinelSelections))]
    public void TheNullSentinelIsSelectedByTheOraclesCompoundConditionAndCarriedVerbatim(
        string value,
        bool emptyStringIsNull,
        long colType,
        string expectedReported,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L758", locator, StringComparison.Ordinal);

        // THE ORACLE'S CONDITION, transcribed through the SAME `Text.Iif` the engine calls at
        // ColumnExpressionEngine.cs:5022 - a FUNCTION and not a conditional expression, because the
        // oracle calls a function and both arms are therefore evaluated before the choice is made.
        string reported = Text.Iif(
            value.Length == 0
                && (emptyStringIsNull || colType != DataWindowServiceBase.COL_TYPE_STRING),
            "(null)",
            value) ?? string.Empty;

        Assert.Equal(expectedReported, reported);

        // AND THE SESSION CARRIES IT UNCHANGED onto the trace payload.
        RecordingTraceSink sink = new();
        ExpressionSession session = NewSession("sentinel", trace: true, traceSink: sink);
        DataWindowHandle handle = session.Register(new RecordingExpressionServiceHost());

        FakeDataWindowObject column = new("n1", "decimal(2)", id: 1L);
        ExpressionTraceRecord? emitted = session.EmitTrace(handle, 1L, column, "$$x", reported);
        Assert.NotNull(emitted);

        Assert.Equal(expectedReported, emitted.Value);
        Assert.Equal(expectedReported, Assert.Single(sink.Records).Value);
    }

    /// <summary>
    /// The <c>"(null)"</c> selection truth table. The condition is a conjunction whose second limb is
    /// itself a disjunction, so all four interesting combinations are covered plus the non-empty case that
    /// short-circuits the whole thing.
    /// </summary>
    /// <returns>One row per combination.</returns>
    public static TheoryData<string, bool, long, string, string> NullSentinelSelections()
    {
        const string Limb = OraclePath + ":L758 ";

        return new()
        {
            // LIMB ONE of the disjunction: a STRING column that treats empty as null.
            { "", true, DataWindowServiceBase.COL_TYPE_STRING, "(null)", Limb + "limb one - emptyStringIsNull" },

            // THE ONE EMPTY CASE THAT IS *NOT* REPORTED AS NULL: a string column WITHOUT the flag. This is
            // the row that proves the condition is a conjunction rather than "empty means null".
            { "", false, DataWindowServiceBase.COL_TYPE_STRING, "", Limb + "neither limb - string column, flag off" },

            // LIMB TWO: any NON-STRING column with an empty value, flag or no flag.
            { "", false, DataWindowServiceBase.COL_TYPE_DECIMAL, "(null)", Limb + "limb two - colType <> COL_TYPE_STRING" },
            { "", true, DataWindowServiceBase.COL_TYPE_DECIMAL, "(null)", Limb + "both limbs" },
            { "", false, DataWindowServiceBase.COL_TYPE_DATE, "(null)", Limb + "limb two - date" },
            { "", false, DataWindowServiceBase.COL_TYPE_DATETIME, "(null)", Limb + "limb two - datetime" },
            { "", false, DataWindowServiceBase.COL_TYPE_TIME, "(null)", Limb + "limb two - time" },
            { "", false, DataWindowServiceBase.COL_TYPE_UNKNOWN, "(null)", Limb + "limb two - unknown type" },

            // A NON-EMPTY value fails the FIRST conjunct and passes through untouched, on every column
            // type and with the flag either way.
            { "42.00", true, DataWindowServiceBase.COL_TYPE_DECIMAL, "42.00", Limb + "first conjunct false - non-empty" },
            { "42.00", false, DataWindowServiceBase.COL_TYPE_STRING, "42.00", Limb + "first conjunct false - non-empty string" },
            { "0", true, DataWindowServiceBase.COL_TYPE_DECIMAL, "0", Limb + "first conjunct false - zero is data, not empty" },

            // A value that merely LOOKS like the sentinel is data, not a sentinel, and is not rewritten.
            { "(null)", false, DataWindowServiceBase.COL_TYPE_STRING, "(null)", Limb + "first conjunct false - sentinel-shaped data" },
        };
    }

    /// <summary>
    /// The trace is GATED, and a gate that is shut produces nothing at all rather than an empty record a
    /// consumer would have to filter.
    /// </summary>
    [Fact]
    public void TheTraceIsGatedAndAShutGateProducesNoRecordAtAll()
    {
        RecordingTraceSink sink = new();

        // Tracing is OFF by default, because `:L98` declares `privatewrite boolean #Trace` with no
        // initializer and PowerScript's default is false.
        ExpressionSession off = NewSession("trace-off", traceSink: sink);
        Assert.False(off.TraceEnabled);

        DataWindowHandle offHandle = off.Register(new RecordingExpressionServiceHost());
        Assert.Null(off.EmitTrace(offHandle, 1L, null, "$$x", "1"));
        Assert.Empty(sink.Records);
        Assert.Equal(0L, off.TraceDelivery.Attempted);

        // Flipping the gate on makes the very next call emit - the flag is the single gate, matching
        // `of_settrace` [:L2409] being callable at any time after construction.
        off.TraceEnabled = true;
        Assert.NotNull(off.EmitTrace(offHandle, 1L, null, "$$x", "1"));
        Assert.Single(sink.Records);

        // An UNREGISTERED handle has no stack, so there is no call stack to build and nothing is emitted -
        // reported as an absence rather than as a record with an empty stack.
        ExpressionSession on = NewSession("trace-on", trace: true, traceSink: sink);
        Assert.Null(on.EmitTrace(DataWindowHandle.From("never-registered"), 1L, null, "$$x", "1"));
        Assert.Null(on.EmitTrace(DataWindowHandle.None, 1L, null, "$$x", "1"));

        // A CLOSED session emits nothing either, because closing released the stacks.
        DataWindowHandle onHandle = on.Register(new RecordingExpressionServiceHost());
        Assert.NotNull(on.EmitTrace(onHandle, 1L, null, "$$x", "1"));
        Assert.True(on.Close());
        Assert.Null(on.EmitTrace(onHandle, 1L, null, "$$x", "1"));

        // A null DataWindow object, expression and value are all tolerated and reported as empty strings
        // rather than faulting the calculation the trace is only observing.
        ExpressionSession nulls = NewSession("trace-nulls", trace: true);
        DataWindowHandle nullHandle = nulls.Register(new RecordingExpressionServiceHost());
        ExpressionTraceRecord? sparse = nulls.EmitTrace(nullHandle, 0L, null, null, null);
        Assert.NotNull(sparse);
        Assert.Equal(string.Empty, sparse.ColumnName);
        Assert.Equal(string.Empty, sparse.Stack);
        Assert.Equal(string.Empty, sparse.Expression);
        Assert.Equal(string.Empty, sparse.Value);
        Assert.Equal(0, sparse.Depth);
    }

    /// <summary>
    /// THE STACK IS EMPTY AFTER BOTH A SUCCESSFUL AND A FAILED CALCULATION, so a failure cannot leak a
    /// frame into the next calculation and make a legitimate expression look recursive.
    /// </summary>
    /// <remarks>
    /// A leaked frame is the worst kind of defect this stack can produce: the NEXT calculation of the same
    /// column would find its own name already present, the recursion guard would refuse it, and the column
    /// would silently stop updating with no error anywhere.
    /// </remarks>
    [Fact]
    public async Task TheStackIsEmptyAfterBothASuccessfulAndAFailedCalculation()
    {
        ExpressionSession session = NewSession("no-leak");
        (ColumnExpressionEngine engine, FakeDataWindowHost host) = NewEngine(session);

        ExpressionCalcStack? lookup = session.CalcStackFor(engine.Handle);
        Assert.NotNull(lookup);
        ExpressionCalcStack stack = lookup;
        Assert.Equal(0, stack.Depth);

        // ---- THE SUCCESS PATH ----
        Assert.True(engine.AddExp("n1", "1 + 1") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAllAsync(Ct));
        Assert.Equal(2m, host.GetItemDecimal(1L, "n1"));

        Assert.Equal(0, stack.Depth);
        Assert.Empty(stack.Frames);
        Assert.Equal(0L, engine.RaisedErrorCount);

        // ---- THE ERROR PATH: an expression naming a column that does not exist. ----
        Assert.True(engine.AddExp("n2", "nosuchcolumn + 1") > 0);
        _ = await engine.CalcAllAsync(Ct);

        // The failure really happened - the assertion below is not vacuous.
        Assert.True(engine.RaisedErrorCount > 0L);
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorCategory.Expression, engine.LastError.Category);

        // AND THE STACK IS STILL EMPTY.
        Assert.Equal(0, stack.Depth);
        Assert.Empty(stack.Frames);
        Assert.False(stack.ContainsFrame("n1"));
        Assert.False(stack.ContainsFrame("n2"));

        // The successful expression still calculates afterwards, which is the property a leaked frame
        // would have destroyed.
        host.SetItem(1L, "n1", 0m);
        Assert.Equal(RetCode.OK, await engine.CalcAllAsync(Ct));
        Assert.Equal(2m, host.GetItemDecimal(1L, "n1"));
        Assert.Equal(0, stack.Depth);

        // Closing the session clears the stack as well, which is the deterministic release the oracle's
        // destructor performed at :L2425.
        Assert.True(session.Close());
        Assert.Null(session.CalcStackFor(engine.Handle));
        Assert.Equal(0, stack.Depth);
    }


    // ==============================================================================================
    //  HELPERS - SMALL, EXPLICIT, AND DELIBERATELY NOT CLEVER
    //  --------------------------------------------------------------------------------------------
    //  Every helper below exists to keep the assertions in the tests rather than in the setup. None
    //  makes a behavioural decision, none has a branch a test relies on for its outcome, and none
    //  duplicates a double that already lives in `TestDoubles.cs`, `FakeDataWindowHost.cs` or a sibling
    //  suite - `RecordingExpressionServiceHost`, `RecordingTraceSink`, `DeterministicTimeProvider`,
    //  `FakeDataWindowObject` and `FakeDataWindowFixtures` are all REUSED as they stand.
    // ==============================================================================================

    /// <summary>
    /// Builds an expression session with the shipped lifetime defaults and an explicit clock, so no test
    /// below ever reads a wall clock.
    /// </summary>
    /// <param name="sessionId">The correlation id, spelled out per test so failures name themselves.</param>
    /// <param name="clock">The clock seam; a frozen <see cref="DeterministicTimeProvider"/> when omitted.</param>
    /// <param name="stackCapacity">The calculation-stack reservation; the shipped 20 when omitted.</param>
    /// <param name="trace">Whether the trace gate starts open; closed when omitted, as PowerScript's own default is.</param>
    /// <param name="traceSink">The sink the session hands records to, if any.</param>
    /// <returns>An open session.</returns>
    private static ExpressionSession NewSession(
        string sessionId,
        DeterministicTimeProvider? clock = null,
        int stackCapacity = 20,
        bool trace = false,
        RecordingTraceSink? traceSink = null) =>
        new(
            sessionId,
            lifetime: new SessionLifetimeOptions { IdleTimeout = ConfiguredIdleTimeout },
            columnExpression: new ColumnExpressionOptions
            {
                Trace = trace,
                CalcStackInitialCapacity = stackCapacity,
            },
            timeProvider: clock ?? new DeterministicTimeProvider(),
            traceSink: traceSink);

    /// <summary>
    /// Builds a column-expression engine over the oracle's own DataWindow fixture and registers it into
    /// the SUPPLIED session, which is what makes two engines co-resident.
    /// </summary>
    /// <param name="session">The session both engines must share for a foreign reference to resolve.</param>
    /// <returns>The engine and the host it was initialised over.</returns>
    /// <remarks>
    /// The fixture is <c>dw_test_dwsvc_columnexp.srd</c>'s shape - n1, n2 and n3 as <c>decimal(2)</c>, s1
    /// and s2 as <c>char(100)</c>, four rows - so the scenario runs against the oracle's own columns rather
    /// than against invented ones. Passing <c>session</c> explicitly is essential: an engine given no
    /// session builds its OWN, and two such engines would never be co-resident.
    /// </remarks>
    private static (ColumnExpressionEngine Engine, FakeDataWindowHost Host) NewEngine(
        ExpressionSession session)
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine engine = new(
            new ColumnExpressionOptions(),
            evaluator: null,
            macroInvoker: null,
            session: session,
            syntaxMutator: null,
            traceSink: null);

        engine.OnInit(host);
        engine.SetEnabled(true);

        return (engine, host);
    }

    /// <summary>
    /// The pieces one blocked-route scenario needs: the session doing the resolving, the handle it cannot
    /// reach, and the value the unreachable peer WOULD have answered.
    /// </summary>
    /// <param name="Resolver">The session the resolution is attempted through.</param>
    /// <param name="Handle">The handle that session cannot reach.</param>
    /// <param name="PeerValue">
    /// What the peer holds. Carried so a test can assert the blocked result is NOT that value, which is
    /// the difference between "refused" and "silently stale".
    /// </param>
    private sealed record BlockedRouteFixture(
        ExpressionSession Resolver,
        DataWindowHandle Handle,
        string PeerValue);

    /// <summary>
    /// Arranges one route out of the resolving session's scope. Every route ends with a handle the
    /// resolver cannot reach and a peer that is perfectly capable of answering.
    /// </summary>
    /// <param name="scenario">Which route to arrange.</param>
    /// <returns>The arranged fixture.</returns>
    private static BlockedRouteFixture BuildBlockedRoute(BlockedRouteScenario scenario)
    {
        const string PeerValue = "4321";

        switch (scenario)
        {
            case BlockedRouteScenario.PeerInAnotherSession:
            {
                ExpressionSession resolver = NewSession("resolver");
                ExpressionSession elsewhere = NewSession("elsewhere");

                RecordingExpressionServiceHost peer = new();
                peer.AddVariable(Summary, PeerValue);

                return new BlockedRouteFixture(resolver, elsewhere.Register(peer), PeerValue);
            }

            case BlockedRouteScenario.PeerInAnotherServiceInstance:
            {
                // A handle carries NO address and NO process identity by design, so a handle minted by
                // another instance is indistinguishable from one that was never minted here. That is not
                // a weakness of the test, it is the property the narrowing relies on: there is nothing in
                // a handle that could let this instance reach out to another one.
                ExpressionSession resolver = NewSession("resolver");
                _ = resolver.Register(new RecordingExpressionServiceHost());

                return new BlockedRouteFixture(
                    resolver,
                    DataWindowHandle.From("another-instance/7"),
                    PeerValue);
            }

            case BlockedRouteScenario.SessionClosed:
            {
                ExpressionSession resolver = NewSession("resolver");

                RecordingExpressionServiceHost peer = new();
                peer.AddVariable(Summary, PeerValue);
                DataWindowHandle handle = resolver.Register(peer);

                Assert.True(resolver.Close());

                return new BlockedRouteFixture(resolver, handle, PeerValue);
            }

            case BlockedRouteScenario.SessionExpired:
            {
                DeterministicTimeProvider clock = new();
                ExpressionSession resolver = NewSession("resolver", clock: clock);

                RecordingExpressionServiceHost peer = new();
                peer.AddVariable(Summary, PeerValue);
                DataWindowHandle handle = resolver.Register(peer);

                // Driven by the clock seam, never by elapsed real time.
                clock.Advance(ConfiguredIdleTimeout + TimeSpan.FromSeconds(1));
                Assert.Equal(ExpressionSessionAcquisition.Expired, resolver.Acquire());

                return new BlockedRouteFixture(resolver, handle, PeerValue);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    "Every declared blocked route must be arranged; an unarranged one would pass "
                        + "vacuously and the narrowing would go unasserted for that route.");
        }
    }

    /// <summary>
    /// A host that THROWS on every member, used to prove a block is decided before the foreign host is
    /// contacted at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately local rather than shared. <c>TestDoubles.cs</c> states that a double used by exactly
    /// one suite belongs beside that suite, and the two throwing hosts in
    /// <c>ExpressionSessionHostFaultTests</c> are private to their own class and scripted for a different
    /// purpose - they carry a message whose SUPPRESSION is the assertion there, whereas this one carries
    /// a call COUNT whose remaining at zero is the assertion here.
    /// </para>
    /// <para>
    /// The count makes the negative assertion positive: "the host was never called" is checked directly
    /// rather than inferred from the absence of a fault status.
    /// </para>
    /// </remarks>
    private sealed class ThrowingProbeHost : IExpressionServiceHost
    {
        private int _callCount;

        /// <summary>How many times any member of this host was invoked.</summary>
        internal int CallCount => Volatile.Read(ref _callCount);

        /// <inheritdoc />
        public int FindVarIndex(string? name) => throw Record();

        /// <inheritdoc />
        public string CalcVarExpValue(
            long row,
            IDataWindowObject? dwo,
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw Record();

        /// <inheritdoc />
        public string CalcVarExpValue(
            int index,
            long ctxRow,
            IDataWindowObject? ctxDwo,
            IExpressionServiceHost? ctxExpSvc) => throw Record();

        /// <inheritdoc />
        public string GetItemExpValue(long row, string? columnName) => throw Record();

        /// <summary>
        /// Counts the invocation and produces the exception to throw.
        /// </summary>
        /// <returns>The exception the caller throws.</returns>
        /// <remarks>
        /// The message is fixed, obviously synthetic, and carries no credential-shaped content - a fault
        /// message is one of the places a secret can leak into a CI log (C-F).
        /// </remarks>
        private InvalidOperationException Record()
        {
            _ = Interlocked.Increment(ref _callCount);

            return new InvalidOperationException(
                "This probe host throws on every member so that a test can prove a cross-session "
                    + "reference was refused WITHOUT the host ever being contacted.");
        }
    }
}

/// <summary>
/// The four outcomes of registering an explicit DataWindow handle into an expression session.
/// </summary>
/// <remarks>
/// A named scenario rather than a tuple of flags, so a failing theory row identifies itself in the test
/// output without the reader having to decode three booleans. Public because it appears in the signature
/// of a public <c>MemberData</c> source.
/// </remarks>
public enum ExplicitRegistrationScenario
{
    /// <summary>No handle was supplied at all.</summary>
    EmptyHandle = 0,

    /// <summary>A handle that is not yet taken, in an open session.</summary>
    FreshHandle = 1,

    /// <summary>A handle that is already registered in this session.</summary>
    DuplicateHandle = 2,

    /// <summary>A session that has already been closed.</summary>
    ClosedSession = 3,
}

/// <summary>
/// The four routes by which a cross-DataWindow reference leaves the scope that could satisfy it, each of
/// which AAP 0.6.2.3 requires to be BLOCKED with a defined error rather than approximated.
/// </summary>
public enum BlockedRouteScenario
{
    /// <summary>
    /// The peer is registered, alive and answering - IN ANOTHER SESSION. Residency is not co-residency.
    /// </summary>
    PeerInAnotherSession = 0,

    /// <summary>
    /// The peer is not resident in this service instance at all, which a handle cannot distinguish from
    /// never having been minted here - by design, because a handle carries no address.
    /// </summary>
    PeerInAnotherServiceInstance = 1,

    /// <summary>The session that scoped the handle has been closed.</summary>
    SessionClosed = 2,

    /// <summary>The session that scoped the handle has expired, reached through the clock seam.</summary>
    SessionExpired = 3,
}

/// <summary>
/// The three static-expansion refusals the legacy raises, all reproduced verbatim because they are
/// behaviour rather than limitations (C-B / G2).
/// </summary>
public enum StaticExpansionRefusal
{
    /// <summary>
    /// A FOREIGN variable reached by the single-sigil static form
    /// [n_cst_dwsvc_columnexp.sru:L1426]. The rule the narrowing runs with rather than against.
    /// </summary>
    ForeignVariable = 0,

    /// <summary>
    /// A CONTEXT variable reached by the single-sigil static form [:L1417]. The sibling rule; note the
    /// oracle's message says "function macro" although the failing construct is a variable.
    /// </summary>
    ContextVariable = 1,

    /// <summary>
    /// A context-scoped FUNCTION macro, which is unsupported outright in either expansion mode
    /// [:L1308].
    /// </summary>
    ContextFunctionMacro = 2,
}
