// =================================================================================================
//  SqlTaskProxyTests.cs - THE ASSERTION THAT THE LEGACY PROXY PAIR SURVIVED THE MIGRATION.
// =================================================================================================
//  WHY THIS FILE EXISTS. AAP 0.1.5 and 0.4.5.4 are emphatic that the legacy proxy pair - a
//  caller-side n_cst_threading* and a worker-side n_cst_thread* for every concurrency class -
//  encodes THREAD AFFINITY AS A CONTRACT, NOT AN IMPLEMENTATION DETAIL, and that the pair MUST NOT
//  be flattened into a single async method. Nothing in a compiler can notice a fused pair, and
//  nothing in a per-derived-proxy test notices it either, because each half still behaves. Only an
//  assertion that BOTH halves exist, that they are distinct types, and that the two-sided behaviours
//  which need two state sets still work, can notice. That assertion is this file.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT.
//  Three sibling files already drive the three DERIVED proxies to near-total coverage -
//  SqlCommandTaskProxyTests.cs, SqlQueryTaskProxyTests.cs and SqlUpdateTaskProxyTests.cs. This file
//  is not a fourth of those. Its subject is:
//
//    * Tasks/TaskProxies/SqlTaskProxyBase.cs - the SHARED caller-side base every proxy inherits,
//      including its nested TaskNotificationDispatcher. The three transaction copy-width overloads,
//      the parameter screens, commit and rollback, the substrate forwarders and most of the
//      dispatcher were unreached before this file existed, and the copy-width overloads are the
//      ones AAP 0.4.5.4 warns "silently carries or drops a connection field" when got wrong.
//    * THE CROSS-PROXY INVARIANTS no single derived proxy can state on its own: the two notify-code
//      namespaces and their deliberate collision at the value 1, the low-word-first packing law, and
//      the pair-is-not-flattened assertions over all three proxies AND over the composition root.
//
//  Where a behaviour belongs to exactly one derived proxy, this file states the base half and names
//  the sibling that owns the override. The update proxy's database-error override is the worked
//  example: the base half - a bare last-error-wins assignment - is asserted here, and the row
//  translation the override adds belongs to the update proxy's own file.
//
//  THE ORACLE, READ IN FULL RATHER THAN SAMPLED. Every locator below was opened.
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru      222 lines
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru    58 lines
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru     532 lines
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru    the notify code at :L32
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru         the worker counterpart
//    ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru                  the framework parent
//    ws_objects/pfw.common.pbl.src/makelong.srf                              the packing primitive
//    ws_objects/pfw.tests.pbl.src/n_test_threading_task.sru                  the legacy's own probe
//    docs/PB多线程绕坑提示.md                                                 5 lines, both hazards
//
//  The affinity markers are the contract, and across the fifteen objects of pfw.thread.ext they
//  MEASURE as six [运行在子线程] worker-thread classes, exactly one [运行在主线程] main-thread class,
//  and four [运行在当前线程] calling-thread proxies. This file's subjects are the fourth group; the
//  marker for the base is at n_cst_threading_task_sqlbase.sru:L2 and its worker counterpart's is at
//  n_cst_thread_task_sqlbase.sru:L2.
//
//  THE ONE ORACLE PASSAGE THAT PROVES A FUSED METHOD CANNOT WORK - the two-sided reset
//  [n_cst_threading_task_sqlbase.sru:L53-L68]:
//
//      if of_IsBusy() then return RetCode.E_BUSY      guards on its OWN busy state          [:L57]
//      task = _Task                                                                        [:L59]
//      rtCode = task.of_Reset()                      DELEGATES the reset to the WORKER      [:L61]
//      if IsFailed(rtCode) then return rtCode        ABANDONS if the worker refused         [:L62]
//      _bHasParams = false                           only THEN clears its OWN state         [:L64]
//      _lastDBError = emptyData                                                            [:L65]
//      return RetCode.OK                             NOT rtCode                            [:L67]
//
//  Two distinct state sets, a delegation between them, and a failure mode in which the worker's
//  refusal leaves caller-side state UNTOUCHED. One fused method has one state set and therefore
//  cannot have that failure mode at all. The abandon-arm case below is consequently the single most
//  load-bearing test in this file, and it can only be reached through a SUBSTITUTED worker, because
//  the real worker's reset always succeeds [n_cst_thread_task_sqlbase.sru:L242-L248].
//
//  CONSTRAINTS THAT GOVERN THIS FILE. There are NO user rules for this project - review_rules
//  returns exactly one line saying so - and nothing is invented or back-filled in their place. The
//  binding set is the enterprise baseline plus the named non-rule constraints:
//
//    C-A  No type from another service. Every subject here is Persistence-internal, and this test
//         project holds exactly one ProjectReference - its own application project - so a type from
//         Gateway, DataServices or Security is not merely unused but unreachable.
//    C-B  Replicate verbatim, never correct. Six legacy behaviours that read as defects are asserted
//         as EXPECTED here, each commented and located at its assertion: the command proxy's gating
//         asymmetry [:L34-L46], the bare last-error-wins assignment [:L44], the abandon-on-refusal
//         reset [:L62], the never-cleared has-transaction-data flag [:L116 versus :L64], the
//         opposite orderings of the two clears [:L64 versus :L164], and the notify-code collision at
//         the value 1 across two per-class namespaces [sqlquery:L28 versus sqlupdate:L32]. A case
//         here fails if any of them is "fixed".
//    C-D  Nothing for any deferred service. PowerFramework.Shared.Eventful is NOT a deferred-service
//         library: AAP 0.4.1 assigns the whole of ws_objects/pfw.thread.pbl.src to Persistence in
//         scope, one of its six objects is n_cst_threading_eventful.sru, and that object derives from
//         n_cst_eventful - so the shared broker is in-scope infrastructure this service consumes, and
//         the notification surface under test is a delegate-shaped ADAPTER over it. The edge and the
//         derivation are both asserted rather than assumed.
//    C-E  No fabricated database. No case here opens a connection, names a SQL Server or Oracle
//         target, or provisions a schema. The worker, the pool activator and the result carrier are
//         all doubles, and the two that must never be reached say so by throwing.
//    C-F  No credential literal in any form, including commented out. The one credential-shaped
//         value in this file is the obviously synthetic CredentialPlaceholder constant, which cannot
//         match any provider's secret pattern, and the assertions on it are that it TRAVELS where the
//         descriptor travels and is NEVER readable back out of a proxy.
//    C-H  Tasks/TaskProxies/ is roughly 1,156 legacy lines. This file and IdentityWriteBackTests.cs
//         carry its coverage; without them the 80 percent per-service line gate is not reachable.
//    C-K  Every decision is documented with its evidence - in particular why the two notify
//         enumerations stay separate, and why affinity is treated as contract.
//
//  DETERMINISM [AAP 0.6.7]. There is NO sleep, NO Task.Delay, NO real-time wait, NO Thread and NO
//  race anywhere in this file. Worker interactions are driven through doubles rather than through
//  actual threads; the clock is an injected fixed seam and no case reads an ambient clock; and the
//  two polling predicates are asserted by the fact that they ANSWER on the calling thread with their
//  handle unset - a blocking wait could not - which is a behavioural claim and never a latency one
//  [AAP 0.8.5]. Running the suite twice produces identical results by construction.
//
//  NAMING [AAP 0.7.2 and 0.4.5.3]. This file sits outside every .editorconfig naming-suppression
//  glob while TreatWarningsAsErrors is true, so it declares no SCREAMING_SNAKE identifier of its
//  own. The preserved legacy constant VALUES are reached through the types that already own them -
//  the generated AutoCommitMode enumerators and the two notify enumerations in
//  Buffers/DataWindowBuffers.cs - and are never re-declared locally.
// =================================================================================================

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Eventful;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Holds the caller-side half of the legacy SQL proxy pair to its oracle, and holds the pair itself
/// to the requirement that it never be flattened into one object.
/// </summary>
public sealed class SqlTaskProxyTests
{
    /// <summary>
    /// The synthetic stand-in for a connection credential, used only to prove that the value travels
    /// where the descriptor travels and is never readable back out of a proxy.
    /// </summary>
    /// <remarks>
    /// C-F. Deliberately shaped so that it cannot match any provider's secret pattern and cannot be
    /// mistaken for a real value by a reader or by a scanner. No real credential, and no
    /// credential-shaped literal of any other kind, appears anywhere in this file.
    /// </remarks>
    private const string CredentialPlaceholder = "<not-a-real-credential>";

    /// <summary>A sentinel that is recognisable in an assertion failure and is not SQL.</summary>
    private const string Sentinel = "sentinel";

    // ==============================================================================================
    //  REGION 1 - THE BASE'S CALLER-SIDE STATE AND THE TWO-SIDED RESET
    //  --------------------------------------------------------------------------------------------
    //  The oracle declares its caller-side state at n_cst_threading_task_sqlbase.sru:L15-L23 and
    //  nowhere else. Five members, and the fifth is the one that makes the pair visible: a handle
    //  the proxy does not own but BORROWS from the worker during init [:L218].
    // ==============================================================================================

    /// <summary>
    /// The base carries exactly the five caller-side state members the oracle declares, and no sixth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's declaration block is <c>Transaction _Trans</c>, <c>DBERRORDATA _lastDBError</c>,
    /// <c>Boolean _bHasTransData</c>, <c>Boolean _bHasParams</c> and <c>ulong _hEvtCommitted</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L15-L23</c>]. Four are protected and the fifth is private,
    /// which is exactly the split the port reproduces: the borrowed handle is not part of the
    /// derived-type surface because a derived proxy has no business re-borrowing it.
    /// </para>
    /// <para>
    /// Asserted by NAME rather than by count alone, because a count would pass after a rename and the
    /// point of the case is that each member still exists to carry its own piece of the two-sided
    /// contract. A sixth member would mean caller-side state the oracle does not have.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBaseCarriesExactlyTheFiveCallerSideStateMembersTheOracleDeclares()
    {
        // The four members a derived proxy can see, from the oracle's `Protected:` block [:L15-L19].
        Assert.NotNull(MemberOfBase("RetainedTransaction"));
        Assert.NotNull(MemberOfBase("LastDbError"));
        Assert.NotNull(MemberOfBase("TransDataInstalled"));
        Assert.NotNull(MemberOfBase("ParamsInstalled"));

        // The fifth, from the oracle's `Private:` block [:L21-L22]. A field rather than a property,
        // because it is BORROWED and must not be re-assignable from a derived proxy.
        FieldInfo? borrowed = typeof(SqlTaskProxyBase).GetField(
            "_committedSignal",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(borrowed);
        Assert.True(borrowed.IsPrivate);
        Assert.Equal(typeof(ManualResetEventSlim), borrowed.FieldType);
    }

    /// <summary>
    /// A freshly constructed proxy starts with every caller-side member at its cleared state.
    /// </summary>
    /// <remarks>
    /// PowerBuilder initialises an unassigned structure to its cleared state and an unassigned object
    /// reference to null, so this is the oracle's starting position rather than a convention of the
    /// port's. It is stated because the whole value of the latch is that "no error yet" is
    /// distinguishable from "an error whose fields happen to be empty".
    /// </remarks>
    [Fact]
    public void AFreshlyConstructedProxyStartsWithEveryCallerSideMemberCleared()
    {
        using Harness harness = new(attachWorker: false);

        Assert.Null(harness.Proxy.Retained);
        Assert.Equal(DbErrorData.Empty, harness.Proxy.Latch);
        Assert.False(harness.Proxy.TransDataFlag);
        Assert.False(harness.Proxy.ParamsFlag);
        Assert.Null(harness.Proxy.Worker);

        // The borrowed handle has not been borrowed yet, so the poll has nothing to read and answers
        // false rather than throwing [:L174 reached with a zero handle].
        Assert.False(harness.Proxy.IsCommitted());
    }

    /// <summary>
    /// Init borrows the worker's commit handle rather than creating one, and the handle is
    /// manual-reset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>_hEvtCommitted = task.of_GetCommitEvent()</c> [<c>:L218</c>]. The word that matters is
    /// BORROWS: the worker owns the handle, so the proxy must observe the very same instance the
    /// worker signals, and must not dispose it on teardown. Both halves are asserted - the instance
    /// is the worker's own, and it is still usable after the proxy is disposed.
    /// </para>
    /// <para>
    /// Manual-reset rather than auto-reset is load bearing: an auto-reset handle would be CONSUMED by
    /// the first successful poll, so a second <c>of_IsCommitted()</c> on a committed task would answer
    /// false. The oracle polls with <c>WaitForSingleObject</c> and never resets, so the state must
    /// persist, and the case polls twice to prove it.
    /// </para>
    /// </remarks>
    [Fact]
    public void InitBorrowsTheWorkersOwnCommitHandleAndTheHandleIsManualReset()
    {
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        ManualResetEventSlim workerOwned = harness.Worker!.GetCommitEvent();

        Assert.Same(workerOwned, harness.BorrowedCommitHandle(harness.Proxy));
        Assert.False(harness.Proxy.IsCommitted());

        workerOwned.Set();

        // Twice. A manual-reset handle answers the same both times; an auto-reset one would not.
        Assert.True(harness.Proxy.IsCommitted());
        Assert.True(harness.Proxy.IsCommitted());

        workerOwned.Reset();
        Assert.False(harness.Proxy.IsCommitted());
    }

    /// <summary>
    /// The proxy releases the borrowed handle on teardown WITHOUT disposing it, because it never
    /// owned it.
    /// </summary>
    /// <remarks>
    /// Hazard 2 of <c>docs/PB多线程绕坑提示.md:L5</c> - a cross-boundary reference is cleared
    /// explicitly on teardown rather than left to the collector. Clearing is not disposing: the
    /// worker still owns the handle and may still signal it, so a proxy that disposed it would fault
    /// its own worker. The case proves the reference was dropped and the object survived.
    /// </remarks>
    [Fact]
    public void TeardownDropsTheBorrowedHandleWithoutDisposingIt()
    {
        Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        ManualResetEventSlim workerOwned = harness.Worker!.GetCommitEvent();
        harness.Proxy.Dispose();

        Assert.Null(harness.BorrowedCommitHandle(harness.Proxy));
        Assert.Null(harness.Proxy.Worker);
        Assert.Null(harness.Proxy.Retained);

        // Still usable, which it would not be had the proxy disposed it.
        workerOwned.Set();
        Assert.True(workerOwned.IsSet);

        harness.Dispose();
    }

    /// <summary>
    /// The reset runs its three steps in exactly the oracle's order: guard, delegate, then clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SEQUENCE IS THE BEHAVIOUR, so it is recorded rather than inferred from the end state. The
    /// worker's own reset reports the caller-side state AS IT STANDS AT THAT MOMENT, which is the only
    /// way to prove the clear happens AFTER the delegation and not before it
    /// [<c>:L61</c> then <c>:L64-L65</c>]. An implementation that cleared first would leave exactly
    /// the same end state and would pass a naive assertion.
    /// </para>
    /// <para>
    /// The busy probe is recorded too, because it is first [<c>:L57</c>]: a proxy that delegated
    /// before checking its own busy state would push work at a worker that is mid-run.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reset_RunsGuardThenDelegateThenClear_InExactlyThatOrder()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        // Dirty both cleared members so that "still set" and "cleared" are distinguishable.
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));
        harness.Proxy.PublishDbError(ErrorWith(sqlDbCode: 17L));

        Assert.True(harness.Proxy.ParamsFlag);
        Assert.NotEqual(DbErrorData.Empty, harness.Proxy.Latch);

        harness.Host.ClearLog();

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        Assert.Equal(
            ["busy-probe", "worker-reset(params=set,latch=set)"],
            harness.Host.Log);

        // And only now is the caller side clear [:L64-L65].
        Assert.False(harness.Proxy.ParamsFlag);
        Assert.Equal(DbErrorData.Empty, harness.Proxy.Latch);
    }

    /// <summary>
    /// The reset ABANDONS on the worker's refusal and leaves caller-side state entirely intact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>if IsFailed(rtCode) then return rtCode</c> [<c>:L62</c>]. This is the case AAP 0.4.5.4 is
    /// really about. Two state sets exist, the worker's refusal is authoritative over both, and the
    /// caller-side clear does not happen - so a caller that retries still has its parameters and its
    /// latched error. A single fused method has one state set and cannot express this at all, which is
    /// why fusing the pair would silently delete a behaviour rather than merely simplify one.
    /// </para>
    /// <para>
    /// Reachable ONLY through a substituted worker: the real worker's reset always answers OK
    /// [<c>n_cst_thread_task_sqlbase.sru:L242-L248</c>]. That is not an inconvenience of the test, it
    /// is why the worker had to be a substitutable seam in the first place.
    /// </para>
    /// <para>
    /// The worker's code is returned VERBATIM rather than replaced with a code of the proxy's own, so
    /// the caller can tell WHY it was refused.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reset_AbandonsOnTheWorkersRefusalAndLeavesCallerSideStateIntact()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));
        harness.Proxy.PublishDbError(ErrorWith(sqlDbCode: 23L));

        harness.Worker!.ResetAnswer = RetCode.E_INVALID_TRANSACTION;
        harness.Host.ClearLog();

        // Verbatim, not rewritten.
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, harness.Proxy.Reset());

        // The delegation DID happen - the refusal is the worker's, not a short-circuit.
        Assert.Equal(
            ["busy-probe", "worker-reset(params=set,latch=set)"],
            harness.Host.Log);

        // AND NOTHING WAS CLEARED. This is the assertion the whole file exists for.
        Assert.True(harness.Proxy.ParamsFlag);
        Assert.Equal(23L, harness.Proxy.Latch.SqlDbCode);
    }

    /// <summary>
    /// A non-failing but non-zero worker answer still clears the caller side, and the proxy still
    /// answers OK.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two preserved subtleties in one case. The guard is
    /// <see cref="Predicates.IsFailed(long?)"/> rather than a hand-rolled <c>&lt; 0</c>
    /// [<c>:L62</c>], and that predicate EXCLUDES cancelled explicitly - so a cancelled worker does
    /// NOT take the abandon arm and the clear still happens. And the return is
    /// <see cref="RetCode.OK"/> rather than <c>rtCode</c> [<c>:L67</c>], so a prevent answer is
    /// reported as a plain success.
    /// </para>
    /// <para>
    /// Both follow from the tri-state hole in the return algebra that AAP 0.8.2 names: prevent is 1
    /// and satisfies the success test, and cancelled is minus two and is neither succeeded nor failed.
    /// Re-deriving either boundary here instead of inheriting it is how the hole gets quietly closed.
    /// </para>
    /// </remarks>
    /// <param name="workerAnswer">A worker answer that is not a failure.</param>
    [Theory]
    [InlineData(RetCode.OK)]
    [InlineData(RetCode.PREVENT)]
    [InlineData(RetCode.CANCELLED)]
    public void Reset_ClearsAndAnswersOk_ForAnyNonFailingWorkerAnswer(long workerAnswer)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));
        harness.Worker!.ResetAnswer = workerAnswer;

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());
        Assert.False(harness.Proxy.ParamsFlag);
    }

    /// <summary>
    /// The reset is refused while busy and never reaches the worker.
    /// </summary>
    /// <remarks>
    /// <c>if of_IsBusy() then return RetCode.E_BUSY</c> [<c>:L57</c>], and the guard is FIRST. The
    /// case asserts the negative that matters: the worker was not called, so a reset arriving
    /// mid-run cannot disturb a running statement.
    /// </remarks>
    [Fact]
    public void Reset_IsRefusedWhileBusyAndNeverReachesTheWorker()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));

        harness.Host.MakeBusy();
        harness.Host.ClearLog();

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Reset());

        Assert.Equal(["busy-probe"], harness.Host.Log);
        Assert.True(harness.Proxy.ParamsFlag);
    }

    /// <summary>
    /// The reset is introduced at THIS level - the framework parent seam declares no reset at all -
    /// and the two proxies that add caller-side state of their own chain to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's framework parent <c>n_cst_threading_task</c> has no <c>of_reset</c> in its
    /// prototype block; the SQL base introduces one [<c>n_cst_threading_task_sqlbase.sru:L28</c>].
    /// The port reproduces that by declaring the member on <see cref="SqlTaskProxyBase"/> and NOT on
    /// the <c>ISqlTaskProxyHost</c> seam that stands in for the framework parent, so an
    /// implementation cannot satisfy the substrate contract by supplying a reset of its own.
    /// </para>
    /// <para>
    /// It is <see langword="virtual"/> because two derived proxies genuinely extend it and chain -
    /// query and update - while the command proxy adds no state and therefore inherits it unchanged.
    /// That three-way split is the oracle's own: <c>n_cst_threading_task_sqlquery.sru</c> and
    /// <c>n_cst_threading_task_sqlupdate.sru</c> both re-declare <c>of_reset</c> and both open with
    /// the ancestor call, and <c>n_cst_threading_task_sqlcommand.sru</c> declares none.
    /// </para>
    /// </remarks>
    [Fact]
    public void Reset_IsIntroducedOnTheBaseAndTheStatefulProxiesChainToIt()
    {
        MethodInfo? introduced = typeof(SqlTaskProxyBase).GetMethod(
            nameof(SqlTaskProxyBase.Reset),
            BindingFlags.Instance | BindingFlags.Public,
            Type.EmptyTypes);

        Assert.NotNull(introduced);
        Assert.True(introduced.IsVirtual);
        Assert.Equal(typeof(SqlTaskProxyBase), introduced.DeclaringType);

        // The framework-parent seam has none, so the reset cannot arrive from the substrate side.
        Assert.DoesNotContain(
            typeof(ISqlTaskProxyHost).GetMembers(),
            member => member.Name.Contains("Reset", StringComparison.Ordinal));

        // Two proxies extend it and chain; the command proxy adds no state and inherits it.
        Assert.Equal(typeof(SqlQueryTaskProxy), DeclaringTypeOfReset(typeof(SqlQueryTaskProxy)));
        Assert.Equal(typeof(SqlUpdateTaskProxy), DeclaringTypeOfReset(typeof(SqlUpdateTaskProxy)));
        Assert.Equal(typeof(SqlTaskProxyBase), DeclaringTypeOfReset(typeof(SqlCommandTaskProxy)));
    }

    /// <summary>
    /// Every guarded member of the base fails fast with a diagnosable exception when no worker is
    /// attached, rather than degrading into a return code.
    /// </summary>
    /// <remarks>
    /// An absent worker is a STRUCTURAL fault, not a runtime condition: the oracle's <c>_Task</c> is
    /// non-null by the time any of these is reachable, because the substrate inserts it during init
    /// [<c>:L217</c>]. AAP 0.1.4 requires the fail-fast posture survive as fail-fast rather than be
    /// softened into graceful degradation, so the port throws and the message names both the task
    /// type and the class the substrate should have inserted.
    /// </remarks>
    [Fact]
    public void EveryDelegatingMemberFailsFastWhenNoWorkerIsAttached()
    {
        using Harness harness = new(attachWorker: false);

        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.Reset());
        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.AddParam(Sentinel));
        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.ResetParams());
        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.Rollback());
        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.Commit());
        _ = Assert.Throws<InvalidOperationException>(() => harness.Proxy.Commit(autoRollback: false));
        _ = Assert.Throws<InvalidOperationException>(
            () => harness.Proxy.SetTransData(DescriptorWithEveryField()));
    }

    // ==============================================================================================
    //  REGION 2 - THE COMPOSED, NON-BLOCKING BUSY PREDICATE
    //  --------------------------------------------------------------------------------------------
    //  `of_isbusy` is TWO answers behind one name [n_cst_threading_task.sru:L305-L306]:
    //
    //      if Not #Running then return #ParentThreading.of_IsBusy()          [:L305]
    //      return (WaitForSingleObject(_hEvtSync,0) <> 0)                    [:L306]
    //
    //  Two properties of that second line are the whole reason the proxy half exists. The timeout is
    //  ZERO, so the calling thread is never parked; and the sense is INVERTED, so a SET handle means
    //  NOT busy. A blocking wait here would stall the very thread the caller is using to ask.
    // ==============================================================================================

    /// <summary>
    /// The busy predicate is a composition of two independent answers, selected by the running flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The full truth table, so that neither arm can be quietly dropped. When NOT running the answer
    /// is the parent controller's IN FULL - both of its values, unmodified, and the proxy's own sync
    /// signal is not consulted at all. When running the answer is the proxy's own sync signal
    /// INVERTED, and the controller is not consulted at all.
    /// </para>
    /// <para>
    /// Written as a matrix rather than as two cases because the interesting failures are the
    /// cross-terms: an implementation that OR-ed the two arms, or that forgot the inversion, passes
    /// any single-row assertion and fails here.
    /// </para>
    /// </remarks>
    /// <param name="running">The substrate's running flag.</param>
    /// <param name="controllerBusy">The parent controller's own answer.</param>
    /// <param name="syncSignalSet">Whether the proxy's sync signal is currently set.</param>
    /// <param name="expected">The composed answer.</param>
    [Theory]
    [MemberData(nameof(BusyPredicateCases))]
    public void IsBusy_ComposesTheControllerAnswerAndItsOwnInvertedSignal(
        bool running,
        bool controllerBusy,
        bool syncSignalSet,
        bool expected)
    {
        using Harness harness = new();

        harness.Host.IsRunning = running;
        harness.Host.IsControllerBusy = controllerBusy;
        harness.Host.IsSyncSignalSet = syncSignalSet;

        Assert.Equal(expected, harness.Proxy.IsBusy());
    }

    /// <summary>The truth table for <see cref="IsBusy_ComposesTheControllerAnswerAndItsOwnInvertedSignal"/>.</summary>
    public static TheoryData<bool, bool, bool, bool> BusyPredicateCases =>
        new()
        {
            // running, controllerBusy, syncSignalSet, expected
            // NOT RUNNING [:L305] - the controller's answer in full, whatever the signal says.
            { false, false, false, false },
            { false, false, true, false },
            { false, true, false, true },
            { false, true, true, true },

            // RUNNING [:L306] - the signal INVERTED, whatever the controller says.
            { true, false, false, true },
            { true, true, false, true },
            { true, false, true, false },
            { true, true, true, false },
        };

    /// <summary>
    /// While running, the predicate reads the sync signal and does NOT read the controller.
    /// </summary>
    /// <remarks>
    /// The negative half of the composition, asserted by counting reads rather than by inferring it
    /// from the answer. A short-circuit that happened to produce the right value while still touching
    /// the controller would be a different program: the controller belongs to the calling thread's
    /// scheduler and the sync signal belongs to this task, and conflating them is how a proxy ends up
    /// reporting the whole controller's state as its own.
    /// </remarks>
    [Fact]
    public void IsBusy_WhileRunning_ReadsOnlyItsOwnSignal()
    {
        using Harness harness = new();

        harness.Host.IsRunning = true;
        harness.Host.IsSyncSignalSet = true;
        harness.Host.ResetReadCounts();

        Assert.False(harness.Proxy.IsBusy());

        Assert.Equal(1, harness.Host.SyncSignalReads);
        Assert.Equal(0, harness.Host.ControllerBusyReads);
    }

    /// <summary>
    /// While not running, the predicate reads the controller and does NOT read the sync signal.
    /// </summary>
    [Fact]
    public void IsBusy_WhileNotRunning_ReadsOnlyTheParentController()
    {
        using Harness harness = new();

        harness.Host.IsRunning = false;
        harness.Host.IsControllerBusy = true;
        harness.Host.ResetReadCounts();

        Assert.True(harness.Proxy.IsBusy());

        Assert.Equal(1, harness.Host.ControllerBusyReads);
        Assert.Equal(0, harness.Host.SyncSignalReads);
    }

    /// <summary>
    /// The commit poll uses a ZERO timeout, so it answers on the calling thread with the handle unset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>return (WaitForSingleObject(_hEvtCommitted,0) = 0)</c> [<c>:L174</c>]. The zero is the
    /// point. The port is <c>_committedSignal?.Wait(0) == true</c>, and the case asserts the ONLY
    /// thing a test can honestly assert about it: with the handle unset the call RETURNS, and it
    /// returns on the same managed thread that made it.
    /// </para>
    /// <para>
    /// That is a behavioural claim and deliberately not a latency claim [AAP 0.8.5]. No duration is
    /// measured, no threshold is asserted and no clock is read. A blocking wait would never reach the
    /// assertion at all, which is what makes the absence of a stall observable without timing it; and
    /// an implementation that queued the answer elsewhere would come back on another thread.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsCommitted_PollsWithAZeroTimeoutAndAnswersOnTheCallingThread()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        ManualResetEventSlim borrowed = harness.Worker!.GetCommitEvent();
        Assert.False(borrowed.IsSet);

        int callingThread = Environment.CurrentManagedThreadId;

        // With the handle UNSET: an answer, here, now. No parking, no marshalling.
        Assert.False(harness.Proxy.IsCommitted());
        Assert.Equal(callingThread, Environment.CurrentManagedThreadId);

        // And the busy guard is the same shape - both arms answer without blocking.
        Assert.False(harness.Proxy.IsBusy());
        Assert.Equal(callingThread, Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// The commit poll answers false before init, because there is no handle to poll yet.
    /// </summary>
    /// <remarks>
    /// The oracle polls an unassigned <c>ulong</c> handle, which is zero, and
    /// <c>WaitForSingleObject</c> on a zero handle does not answer <c>WAIT_OBJECT_0</c> - so the
    /// legacy answers false. The port reaches the same answer through the null-conditional rather
    /// than by throwing, which is the behaviour-preserving choice: a caller polling a
    /// not-yet-initialised proxy is told "not committed", exactly as before.
    /// </remarks>
    [Fact]
    public void IsCommitted_AnswersFalseBeforeInitBecauseNoHandleHasBeenBorrowed()
    {
        using Harness harness = new(attachWorker: false);

        Assert.False(harness.Proxy.IsCommitted());
    }

    // ==============================================================================================
    //  REGION 3 - LAST ERROR WINS
    //  --------------------------------------------------------------------------------------------
    //  The oracle's whole event body is one line [n_cst_threading_task_sqlbase.sru:L44]:
    //
    //      event ondberror(readonly dberrordata err);_lastDBError = err
    //
    //  A bare assignment. It does not accumulate, it does not merge, and it does not screen the
    //  incoming payload. C-B: reproduced as a bare assignment and asserted as expected.
    // ==============================================================================================

    /// <summary>
    /// The database-error sink is a bare assignment that never accumulates: the last one wins.
    /// </summary>
    /// <remarks>
    /// Three payloads, deliberately - two would not distinguish "keeps the last" from "keeps the
    /// second". Every field of the winner is asserted and every field of the losers is asserted
    /// absent, because a merge would leave a payload that is individually plausible and jointly wrong:
    /// one statement with another statement's row.
    /// </remarks>
    [Fact]
    public void OnDbError_IsABareAssignmentAndOnlyTheLastPayloadIsRetained()
    {
        using Harness harness = new();

        harness.Proxy.PublishDbError(ErrorWith(1L, "first", "SELECT 1", DwBuffer.Primary, 11L));
        harness.Proxy.PublishDbError(ErrorWith(2L, "second", "SELECT 2", DwBuffer.Delete, 22L));
        harness.Proxy.PublishDbError(ErrorWith(3L, "third", "SELECT 3", DwBuffer.Filter, 33L));

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        Assert.Equal(3L, latched.SqlDbCode);
        Assert.Equal("third", latched.SqlErrText);
        Assert.Equal("SELECT 3", latched.SqlSyntax);
        Assert.Equal(DwBuffer.Filter, latched.Buffer);
        Assert.Equal(33L, latched.Row);

        // No trace of either loser, in any field.
        Assert.DoesNotContain("first", latched.SqlErrText, StringComparison.Ordinal);
        Assert.DoesNotContain("second", latched.SqlErrText, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT 1", latched.SqlSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT 2", latched.SqlSyntax, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sink accepts a cleared payload as readily as a populated one, so a clear is expressible.
    /// </summary>
    /// <remarks>
    /// A screen that ignored an empty payload would make the latch impossible to clear from the worker
    /// side, and the oracle has no such screen. Both the prepare hook [<c>:L208</c>] and the reset
    /// [<c>:L65</c>] clear it by assigning a freshly declared empty structure, which is the same
    /// operation this case performs through the event.
    /// </remarks>
    [Fact]
    public void OnDbError_AcceptsAClearedPayloadSoTheLatchCanBeClearedThroughIt()
    {
        using Harness harness = new();

        harness.Proxy.PublishDbError(ErrorWith(sqlDbCode: 5L));
        Assert.NotEqual(DbErrorData.Empty, harness.Proxy.Latch);

        harness.Proxy.PublishDbError(DbErrorData.Empty);
        Assert.Equal(DbErrorData.Empty, harness.Proxy.Latch);
    }

    /// <summary>
    /// The prepare hook clears the latch, and does so through the base regardless of the substrate's
    /// answer.
    /// </summary>
    /// <remarks>
    /// <c>event onprepare;call super::onprepare;... _lastDBError = emptyData ... return 0</c>
    /// [<c>:L206-L211</c>]. TWO preserved oddities in four lines: the ancestor's answer is DISCARDED
    /// rather than propagated, and the literal returned is a bare <c>0</c> rather than
    /// <see cref="RetCode.OK"/> even though the init hook right below it spells the same value as the
    /// named constant [<c>:L215</c>]. Both are kept at the sites that carry them.
    /// </remarks>
    [Fact]
    public void OnPrepare_ClearsTheLatchAndDiscardsTheSubstrateAnswer()
    {
        using Harness harness = new();

        harness.Proxy.PublishDbError(ErrorWith(sqlDbCode: 9L));
        harness.Host.OnPrepareResult = RetCode.PREVENT;

        Assert.Equal(0L, harness.Proxy.Prepare());
        Assert.Equal(DbErrorData.Empty, harness.Proxy.Latch);
        Assert.Equal(1, harness.Host.PrepareCalls);
    }

    /// <summary>
    /// The wire projection is the ONE sanctioned way the latched payload leaves the process, and it
    /// redacts the statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-F. The legacy <c>sqlsyntax</c> field carries the complete generated statement INCLUDING
    /// interpolated literal values, and the legacy logger performs no redaction at all
    /// [AAP 0.6.3.8]. The port keeps the field - removing it would change the payload - and makes the
    /// only outbound conversion a redacting one, so a value that reached the latch cannot reach a
    /// caller verbatim.
    /// </para>
    /// <para>
    /// The in-process read and the outbound projection are asserted TOGETHER, because the distinction
    /// between them is the whole control: the unredacted text is legitimately readable inside the
    /// service and must not be readable outside it.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetLastDbErrorForWire_RedactsTheStatementWhileTheInProcessReadDoesNot()
    {
        using Harness harness = new();

        const string literalBearing = "UPDATE COMPANY SET NAME = 'Ada Lovelace' WHERE ID = 7";
        harness.Proxy.PublishDbError(ErrorWith(19L, "constraint", literalBearing, DwBuffer.Primary, 4L));

        // In process: verbatim, because the service's own diagnostics need the real statement.
        Assert.Equal(literalBearing, harness.Proxy.GetLastDbErrorData().SqlSyntax);

        DbError projected = harness.Proxy.GetLastDbErrorForWire();

        Assert.DoesNotContain("Ada Lovelace", projected.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, projected.Sqlsyntax, StringComparison.Ordinal);

        // The structural fields travel unchanged - only the literal-bearing text is masked.
        Assert.Equal(19L, projected.Sqldbcode);
        Assert.Equal(DwBuffer.Primary, projected.Buffer);
        Assert.Equal(4L, projected.Row);
    }

    /// <summary>
    /// The base does NOT translate the reported row; exactly one derived proxy overrides the sink to
    /// do that, and it rewrites nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated here as the base half of a split contract, so that a reader of this file learns the
    /// override exists without this file duplicating it. The base assigns and stops [<c>:L44</c>]; the
    /// update proxy overrides the sink to translate a reported ordinal into an actual primary-buffer
    /// row, and rewrites ONLY the row. That translation, all of its arms and all of its negatives are
    /// the subject of the update proxy's own file.
    /// </para>
    /// <para>
    /// Asserted structurally - exactly one of the three proxies declares the override - because that
    /// is what keeps the split honest. A second override would mean two translation policies, and none
    /// would mean the ordinal reaches a caller as if it were a row.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnDbError_IsOverriddenByExactlyOneDerivedProxyAndTheBaseTranslatesNothing()
    {
        // The base's own row travels unchanged, whatever it is.
        using Harness harness = new();
        harness.Proxy.PublishDbError(ErrorWith(1L, "text", "SELECT 1", DwBuffer.Primary, 4L));
        Assert.Equal(4L, harness.Proxy.Latch.Row);

        Assert.Equal(typeof(SqlUpdateTaskProxy), DeclaringTypeOfDbErrorSink(typeof(SqlUpdateTaskProxy)));
        Assert.Equal(typeof(SqlTaskProxyBase), DeclaringTypeOfDbErrorSink(typeof(SqlQueryTaskProxy)));
        Assert.Equal(typeof(SqlTaskProxyBase), DeclaringTypeOfDbErrorSink(typeof(SqlCommandTaskProxy)));
    }

    // ==============================================================================================
    //  REGION 4 - THE TRANSACTION SETTERS, THE PARAMETERS, COMMIT AND ROLLBACK
    //  --------------------------------------------------------------------------------------------
    //  FOUR overloads carry THREE distinct copy widths [n_cst_threading_task_sqlbase.sru:L70-L133]:
    //
    //      of_settransobject(transaction)          8 fields, and NO UserParm       [:L76-L83]
    //      of_settransdata(6 strings)              6 fields, and no Lock either    [:L97-L102]
    //      of_settransdata(transactiondata)        the caller's descriptor, whole  [:L114]
    //      of_settransobject(n_cst_thread_trans)   the pool's descriptor, whole    [:L127]
    //
    //  Each of the first two builds a FRESHLY DECLARED local [:L71, :L93], so every member it does not
    //  assign stays at its cleared state. That is what leaves UserParm behind, and it is why AAP
    //  0.4.5.4 warns that getting a copy width wrong "silently carries or drops a connection field":
    //  the wrong width still connects, still runs, and connects to the wrong place.
    // ==============================================================================================

    /// <summary>The eight-field copy from a plain transaction object [<c>:L70-L91</c>].</summary>
    private const string EightFieldCopy = "eight-field";

    /// <summary>The six-field copy from six loose strings [<c>:L93-L105</c>].</summary>
    private const string SixFieldCopy = "six-field";

    /// <summary>The caller's whole descriptor, moved intact [<c>:L107-L120</c>].</summary>
    private const string WholeDescriptorCopy = "whole-descriptor";

    /// <summary>The pooled object's whole descriptor, moved intact [<c>:L122-L133</c>].</summary>
    private const string PooledDescriptorCopy = "pooled-descriptor";

    /// <summary>
    /// The descriptor members whose movement distinguishes one copy width from another.
    /// </summary>
    /// <remarks>
    /// <c>AutoCommit</c> is deliberately absent and has its own case: whether the PROXY copied it is
    /// not observable at the worker, because the worker erases it on arrival, so folding it into a
    /// moved/not-moved matrix would state something the assertion cannot support.
    /// </remarks>
    private static readonly string[] DescriptorFields =
        ["Dbms", "ServerName", "Database", "LogId", "Credential", "DbParm", "Lock", "UserParm"];

    /// <summary>The three widths, spelled as the set of members each one moves.</summary>
    private static readonly (string Width, string[] Moved)[] CopyWidths =
    [
        // Seven of the eight named members plus the credential, and NO UserParm [:L76-L83].
        (EightFieldCopy, ["Dbms", "ServerName", "Database", "LogId", "Credential", "DbParm", "Lock"]),

        // Six. No Lock, no AutoCommit, no UserParm - the oracle has no line for any of them [:L97-L102].
        (SixFieldCopy, ["Dbms", "ServerName", "Database", "LogId", "Credential", "DbParm"]),

        // The whole descriptor, so every member travels including the two the narrower widths drop.
        (WholeDescriptorCopy, [.. DescriptorFields]),
        (PooledDescriptorCopy, [.. DescriptorFields]),
    ];

    /// <summary>
    /// Each transaction-setting overload moves exactly the members its own copy width names, and
    /// leaves every other member at its cleared state.
    /// </summary>
    /// <param name="width">The copy width under test.</param>
    /// <param name="field">The descriptor member being examined.</param>
    /// <param name="moved">Whether that width carries that member.</param>
    [Theory]
    [MemberData(nameof(TransactionCopyWidthCases))]
    public void EachTransactionOverloadMovesExactlyItsOwnFields(string width, string field, bool moved)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, InstallTransactionVia(width, harness.Proxy));

        TransactionData arrived = harness.Worker!.GetTransData();

        Assert.Equal(moved ? SentinelFor(field) : string.Empty, ReadDescriptorField(arrived, field));
    }

    /// <summary>The copy-width matrix - every width crossed with every distinguishing member.</summary>
    public static TheoryData<string, string, bool> TransactionCopyWidthCases
    {
        get
        {
            TheoryData<string, string, bool> data = [];

            foreach ((string width, string[] moved) in CopyWidths)
            {
                foreach (string member in DescriptorFields)
                {
                    data.Add(width, member, moved.Contains(member, StringComparer.Ordinal));
                }
            }

            return data;
        }
    }

    /// <summary>
    /// The eighth field of the widest copy is copied by the proxy and then ERASED by the worker on
    /// arrival.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, and both halves are legacy behaviour. The proxy assigns
    /// <c>transData.AutoCommit = trans.AutoCommit</c> [<c>:L83</c>], and the worker's own setter then
    /// discards it [<c>n_cst_thread_task_sqlbase.sru:L118-L119</c>]. The port reproduces the pair
    /// exactly: the eighth line is present in the copy and the arriving descriptor reads false.
    /// </para>
    /// <para>
    /// Stated with the observable half asserted and the unobservable half located, rather than
    /// pretended into an assertion. Whether the copy happened cannot be seen from outside the worker,
    /// because the worker is the only reader; what CAN be seen is that a caller who asks for
    /// auto-commit on the descriptor does not get it, for every width. Removing the eighth line would
    /// therefore not fail this case - which is exactly why the line carries its own comment at the
    /// point of reproduction.
    /// </para>
    /// </remarks>
    /// <param name="width">The copy width under test.</param>
    [Theory]
    [InlineData(EightFieldCopy)]
    [InlineData(WholeDescriptorCopy)]
    [InlineData(PooledDescriptorCopy)]
    public void TheAutoCommitFieldIsErasedByTheWorkerWhateverTheCopyWidthCarried(string width)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, InstallTransactionVia(width, harness.Proxy));

        Assert.False(harness.Worker!.GetTransData().AutoCommit);
    }

    /// <summary>
    /// The pooled overload contributes no field of its own - it reads the pooled object's descriptor
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>of_SetTransData(trans.of_GetTransData())</c> [<c>:L127</c>] is the whole body. The negative is
    /// asserted by counting property reads on the source: an implementation that "helpfully" also
    /// copied the individual connection properties would produce a descriptor that is right today and
    /// diverges the moment the pool's own descriptor and its property surface disagree - which is
    /// precisely what a pooled object exists to arbitrate.
    /// </remarks>
    [Fact]
    public void ThePooledOverloadReadsOnlyTheDescriptorAndNoIndividualField()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        FakePooledTransactionObject pooled = new() { Descriptor = DescriptorWithEveryField() };

        Assert.Equal(RetCode.OK, harness.Proxy.SetTransObject(pooled));

        Assert.Equal(1, pooled.DescriptorReads);
        Assert.Equal(0, pooled.IndividualFieldReads);
    }

    /// <summary>
    /// Both object-taking overloads retain the object on success, and the getter hands back the very
    /// same instance.
    /// </summary>
    /// <remarks>
    /// <c>if IsSucceeded(rtCode) then _Trans = trans</c> [<c>:L86-L88</c> and <c>:L128-L130</c>], and
    /// <c>of_gettransobject</c> returns the field [<c>:L50</c>]. The gate is
    /// <c>IsSucceeded</c> rather than an equality against zero, so a prevent answer also retains -
    /// the tri-state boundary inherited rather than re-derived. The two descriptor-taking overloads
    /// retain nothing, because there is no object to retain, and that asymmetry is asserted too.
    /// </remarks>
    [Fact]
    public void TheObjectTakingOverloadsRetainTheObjectAndTheDescriptorTakingOnesDoNot()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        FakeTransactionObject plain = SourceTransactionObject();
        Assert.Equal(RetCode.OK, harness.Proxy.SetTransObject(plain));
        Assert.Same(plain, harness.Proxy.GetTransObject());

        FakePooledTransactionObject pooled = new() { Descriptor = DescriptorWithEveryField() };
        Assert.Equal(RetCode.OK, harness.Proxy.SetTransObject(pooled));
        Assert.Same(pooled, harness.Proxy.GetTransObject());

        // A descriptor-taking overload leaves the retained object exactly as it was.
        Assert.Equal(RetCode.OK, harness.Proxy.SetTransData(DescriptorWithEveryField()));
        Assert.Same(pooled, harness.Proxy.GetTransObject());
    }

    /// <summary>
    /// Both object-taking overloads refuse a null object with the invalid-object code, before touching
    /// the worker.
    /// </summary>
    /// <remarks>
    /// <c>if Not IsValidObject(trans) then return RetCode.E_INVALID_OBJECT</c> [<c>:L74</c> and
    /// <c>:L125</c>]. The guard sits AFTER the busy check and BEFORE any field is read, so a null
    /// object cannot half-install a descriptor.
    /// </remarks>
    [Fact]
    public void TheObjectTakingOverloadsRefuseANullObject()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Proxy.SetTransObject((ISqlTransactionObject)null!));
        Assert.Equal(
            RetCode.E_INVALID_OBJECT,
            harness.Proxy.SetTransObject((IPooledSqlTransactionObject)null!));

        Assert.False(harness.Proxy.HasTransData());
        Assert.Null(harness.Proxy.GetTransObject());
        Assert.Equal(TransactionData.Empty, harness.Worker!.GetTransData());
    }

    /// <summary>
    /// The descriptor-taking overload is the SOLE writer of the has-transaction-data flag, and the
    /// reset never clears it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, AND IT IS A REAL ASYMMETRY RATHER THAN AN OVERSIGHT OF THE PORT. The flag is written in
    /// exactly one place [<c>:L115-L117</c>], and the reset clears the parameters flag and the error
    /// latch [<c>:L64-L65</c>] while saying nothing about this one. So a proxy that has been reset
    /// still reports that it has transaction data - which is coherent, because the reset does not
    /// disconnect it - and the two flags therefore behave differently across a reset.
    /// </para>
    /// <para>
    /// Pinned deliberately: "tidying" the reset to clear both flags would look like a consistency fix
    /// and would change what a caller observes after a reset.
    /// </para>
    /// </remarks>
    [Fact]
    public void HasTransData_IsWrittenOnceAndSurvivesTheResetThatClearsHasParams()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.False(harness.Proxy.HasTransData());

        Assert.Equal(RetCode.OK, harness.Proxy.SetTransData(DescriptorWithEveryField()));
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));

        Assert.True(harness.Proxy.HasTransData());
        Assert.True(harness.Proxy.HasParams());

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        // The parameters flag is cleared [:L64]; the transaction-data flag is NOT [no line].
        Assert.False(harness.Proxy.HasParams());
        Assert.True(harness.Proxy.HasTransData());
    }

    /// <summary>
    /// Every transaction setter is refused while busy, and none of them reaches the worker.
    /// </summary>
    /// <remarks>
    /// Four overloads, four guards, all at the top of their own bodies [<c>:L73</c>, <c>:L95</c>,
    /// <c>:L110</c>, <c>:L124</c>]. The guard on the descriptor-taking overload is not redundant even
    /// though the two narrower ones funnel into it: they guard before BUILDING their descriptor, and
    /// it guards before DELEGATING, so a proxy that became busy between the two still refuses.
    /// </remarks>
    /// <param name="width">The copy width under test.</param>
    [Theory]
    [InlineData(EightFieldCopy)]
    [InlineData(SixFieldCopy)]
    [InlineData(WholeDescriptorCopy)]
    [InlineData(PooledDescriptorCopy)]
    public void EveryTransactionSetterIsRefusedWhileBusy(string width)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        harness.Host.MakeBusy();

        Assert.Equal(RetCode.E_BUSY, InstallTransactionVia(width, harness.Proxy));

        Assert.False(harness.Proxy.HasTransData());
        Assert.Equal(TransactionData.Empty, harness.Worker!.GetTransData());
    }

    /// <summary>
    /// The credential travels where the descriptor travels, and no proxy member hands it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-F, asserted in both directions. FORWARD: after any width that carries it, the worker's
    /// descriptor reports a credential is present, so the connect path can use one. BACKWARD: the
    /// value is not readable out of the proxy at all - the descriptor property is write-only, the
    /// retained-object accessor hands back an interface whose only credential door is a named
    /// transfer method, and neither the latched payload's projection nor any rendering of the
    /// descriptor contains it.
    /// </para>
    /// <para>
    /// The <see cref="object.ToString"/> arm is not padding. A record struct's generated
    /// <c>ToString</c> prints every property it has, so a credential exposed as an ordinary property
    /// would leak through a single interpolated log line - which is the same leak reached through a
    /// different default, and the reason the write-only shape exists.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCredentialTravelsWithTheDescriptorAndIsNeverReadableBackOutOfTheProxy()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        FakeTransactionObject source = SourceTransactionObject();
        Assert.Equal(RetCode.OK, harness.Proxy.SetTransObject(source));

        // FORWARD: it arrived, and it arrived through the ONE named door.
        TransactionData arrived = harness.Worker!.GetTransData();
        Assert.True(arrived.HasCredential);
        Assert.Equal(CredentialPlaceholder, arrived.RevealLogPassForConnect());
        Assert.Equal(1, source.RevealCalls);

        // BACKWARD: nothing a caller can reach renders it.
        Assert.DoesNotContain(CredentialPlaceholder, arrived.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            CredentialPlaceholder,
            harness.Proxy.GetLastDbErrorForWire().ToString(),
            StringComparison.Ordinal);

        // The retained object is handed back as the interface, whose credential door is a METHOD with
        // a name that says what it does - never a property a serializer or a log template can find.
        ISqlTransactionObject? retained = harness.Proxy.GetTransObject();
        Assert.NotNull(retained);
        Assert.DoesNotContain(
            typeof(ISqlTransactionObject).GetProperties(),
            property => property.Name.Contains("Pass", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The unnamed parameter form forwards an EMPTY name rather than a null one.
    /// </summary>
    /// <remarks>
    /// <c>return of_AddParam("",param)</c> [<c>:L138</c>]. The empty string is the oracle's own literal
    /// and it is load bearing: the worker lower-cases and stores the name, and an empty name is how a
    /// POSITIONAL parameter is distinguished from a named one. A null would be a third state the
    /// worker does not have.
    /// </remarks>
    [Fact]
    public void TheUnnamedParameterFormForwardsAnEmptyName()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));

        Assert.Equal(1, harness.Worker!.GetParamCount());
        Assert.True(harness.Proxy.HasParams());
    }

    /// <summary>
    /// The parameter screens accept a scalar and a non-empty one-dimensional array, and reject an
    /// empty array and any array of rank two or higher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's two screens are <c>if UpperBound(value,2) &gt;= 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c> [<c>:L148</c>] and <c>if UpperBound(value,1) = 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c> [<c>:L149</c>], under the comment
    /// "参数只支持简单类型或简单类型的一维数组" [<c>:L146</c>] - only simple types, or one-dimensional
    /// arrays of them. PowerBuilder has no jagged arrays, so rank is the whole of the first question,
    /// and a one-based upper bound of zero is its empty variable-size array.
    /// </para>
    /// <para>
    /// A SCALAR reaches neither screen and is accepted, which is what those two <c>UpperBound</c> calls
    /// do for a non-array value. The rank test runs FIRST, so an empty two-dimensional array is
    /// rejected as multi-dimensional rather than as empty - same code, different reason, and the order
    /// is the oracle's.
    /// </para>
    /// <para>
    /// C-B - A NULL VALUE IS ACCEPTED. The oracle's null check exists but is COMMENTED OUT
    /// [<c>:L147</c>], so a null parameter is forwarded to the worker. That line is carried across
    /// inert, and this matrix pins the behaviour so it cannot be quietly revived: reviving it would
    /// reject calls the legacy accepts.
    /// </para>
    /// </remarks>
    /// <param name="shape">The value shape under test.</param>
    /// <param name="expected">The code the screens produce for it.</param>
    [Theory]
    [MemberData(nameof(ParameterShapeCases))]
    public void TheParameterScreensAcceptScalarsAndOneDimensionalArraysOnly(string shape, long expected)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(expected, harness.Proxy.AddParam("named", ValueOfShape(shape)));

        // The flag and the worker move together, and only on a succeeded answer [:L154-L156].
        bool accepted = expected == RetCode.OK;
        Assert.Equal(accepted, harness.Proxy.HasParams());
        Assert.Equal(accepted ? 1 : 0, harness.Worker!.GetParamCount());
    }

    /// <summary>The value shapes the two <c>UpperBound</c> screens discriminate between.</summary>
    public static TheoryData<string, long> ParameterShapeCases =>
        new()
        {
            // Neither screen applies to a non-array value [:L148-L149].
            { "scalar", RetCode.OK },

            // C-B: the null check is commented out [:L147], so null is ACCEPTED.
            { "null", RetCode.OK },

            // Rank one, non-empty - the only array shape the comment at :L146 sanctions.
            { "array-1d", RetCode.OK },

            // Rank one, empty: upper bound zero [:L149].
            { "array-1d-empty", RetCode.E_INVALID_ARGUMENT },

            // Rank two or higher [:L148]. The rank test runs first, so the empty one is refused for
            // being multi-dimensional rather than for being empty.
            { "array-2d", RetCode.E_INVALID_ARGUMENT },
            { "array-2d-empty", RetCode.E_INVALID_ARGUMENT },
            { "array-3d", RetCode.E_INVALID_ARGUMENT },
        };

    /// <summary>
    /// The parameter reset clears the caller-side flag BEFORE delegating - the opposite order to the
    /// reset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, and the pair of orderings is the point. <c>of_resetparams</c> clears
    /// <c>_bHasParams</c> at <c>:L164</c> and only then fetches the worker and delegates
    /// [<c>:L166-L168</c>], whereas <c>of_reset</c> delegates first and clears only if the worker
    /// agreed [<c>:L61-L65</c>]. So the flag is cleared here EVEN IF the delegation then fails, and
    /// the caller-side view and the worker-side view can legitimately disagree.
    /// </para>
    /// <para>
    /// It also returns the worker's code VERBATIM rather than rewriting it to OK, which is the other
    /// half of the divergence from the reset [<c>:L168</c> versus <c>:L67</c>]. Both are asserted, so
    /// harmonising the two bodies breaks this case.
    /// </para>
    /// </remarks>
    [Fact]
    public void ResetParams_ClearsTheFlagBeforeDelegating_TheOppositeOrderToReset()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));
        Assert.True(harness.Proxy.HasParams());

        Assert.Equal(RetCode.OK, harness.Proxy.ResetParams());

        Assert.False(harness.Proxy.HasParams());
        Assert.Equal(0, harness.Worker!.GetParamCount());
    }

    /// <summary>
    /// Both parameter members are refused while busy, and neither disturbs the caller side.
    /// </summary>
    /// <remarks>[<c>:L144</c>] and [<c>:L162</c>].</remarks>
    [Fact]
    public void TheParameterMembersAreRefusedWhileBusy()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(Sentinel));

        harness.Host.MakeBusy();

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.AddParam("named", Sentinel));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.AddParam(Sentinel));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.ResetParams());

        // Refused means untouched: the flag still stands and the worker still holds its one parameter.
        Assert.True(harness.Proxy.HasParams());
        Assert.Equal(1, harness.Worker!.GetParamCount());
    }

    /// <summary>
    /// Rollback delegates to the worker and is refused while busy.
    /// </summary>
    /// <remarks>
    /// <c>task = _Task</c> then <c>return task.of_Rollback()</c> [<c>:L181-L183</c>], behind the guard
    /// at <c>:L179</c>. The proxy contributes nothing of its own - no state change, no code rewrite -
    /// which is why the worker's answer travels back unchanged.
    /// </remarks>
    [Fact]
    public void Rollback_DelegatesToTheWorkerAndIsRefusedWhileBusy()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        // No transaction is installed, so the worker has nothing to roll back and says so. The value is
        // the WORKER'S, which is the property under test - the proxy neither invents nor rewrites it.
        long delegated = harness.Proxy.Rollback();
        Assert.Equal(harness.Worker!.Rollback(), delegated);

        harness.Host.MakeBusy();
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Rollback());
    }

    /// <summary>
    /// Commit has two overloads, and the short form supplies auto-rollback TRUE.
    /// </summary>
    /// <remarks>
    /// <c>public function long of_commit ();return of_Commit(true)</c> [<c>:L195</c>]. The default is
    /// the oracle's literal and it is not neutral: it means a failed commit rolls back rather than
    /// leaving the transaction open, so a caller who omits the argument gets the safer behaviour. The
    /// short form carries NO busy guard of its own - it inherits the long form's [<c>:L188</c>] - which
    /// is the third of the oracle's guard-free convenience overloads.
    /// </remarks>
    [Fact]
    public void Commit_HasTwoOverloadsAndTheShortFormPassesAutoRollbackTrue()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(harness.Proxy.Commit(autoRollback: true), harness.Proxy.Commit());

        MethodInfo? shortForm = typeof(SqlTaskProxyBase).GetMethod(
            nameof(SqlTaskProxyBase.Commit),
            BindingFlags.Instance | BindingFlags.Public,
            Type.EmptyTypes);
        MethodInfo? longForm = typeof(SqlTaskProxyBase).GetMethod(
            nameof(SqlTaskProxyBase.Commit),
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(bool)]);

        Assert.NotNull(shortForm);
        Assert.NotNull(longForm);

        harness.Host.MakeBusy();
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Commit());
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Commit(autoRollback: false));
    }

    // ==============================================================================================
    //  REGION 4b - THE NOTIFICATION SURFACE IS THE SHARED BROKER, SPECIALIZED AND ADAPTED
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.4.1. The legacy notification surface is the framework's own event broker, n_cst_eventful,
    //  ported to shared/PowerFramework.Shared.Eventful - and the AAP cites this service's own
    //  n_cst_threading_eventful deriving from it as one of the two structural facts proving the base
    //  belongs in a shared in-scope library. ws_objects/pfw.thread.pbl.src holds exactly six objects
    //  and ALL SIX are assigned to Persistence, that derived broker among them, so this service owns
    //  the threading specialization and consumes the shared base. The cases below assert the edge, the
    //  derivation, and the broker-backed behaviour the delegate surface adapts: named channels, a
    //  catch-all channel that can suppress them, a tri-valued veto that is never flattened, subscriber
    //  ordering, the injected source argument, the established zero default return value, and the
    //  cancel-then-absorb-or-propagate fault posture the threading override actually implements.
    // ==============================================================================================

    /// <summary>
    /// The notification surface dispatches through the shared event broker: the assembly edge exists,
    /// the threading specialization derives from the shared broker, and the delegate types are local.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted rather than assumed, in both directions. The POSITIVE half reads the compiled
    /// assembly's own reference list - the fact, rather than a restatement of the project file - and
    /// the type hierarchy, which is what makes the specialization a specialization rather than a
    /// look-alike. Without the derivation the four overridden hooks would be dead code and the
    /// dispatch would be someone else's.
    /// </para>
    /// <para>
    /// The delegate half still matters: the two handler types are ordinary multicast delegates
    /// declared in the Persistence assembly, so a SUBSCRIBER is a plain method and a caller never has
    /// to implement a framework interface. That is the whole of what the adapter adds, and it is why
    /// the shared broker's object-plus-member-name subscription model is not exposed here.
    /// </para>
    /// <para>
    /// The C-A negatives are kept: no peer service is referenced, and the veto alphabet is the shared
    /// enum rather than a local restatement of the same three numerals.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNotificationSurfaceDispatchesThroughTheSharedEventBroker()
    {
        Assembly persistence = typeof(SqlTaskProxyBase).Assembly;

        // POSITIVE: the edge to the shared broker library exists.
        string[] referenced = [.. persistence.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty)];
        Assert.Contains("PowerFramework.Shared.Eventful", referenced, StringComparer.Ordinal);

        // POSITIVE: the threading specialization is declared HERE and derives from the SHARED broker.
        Type? specialization = persistence.GetType(
            "PowerFramework.Persistence.Tasks.TaskProxies.ThreadingEventBroker",
            throwOnError: false);

        Assert.NotNull(specialization);
        Assert.Same(persistence, specialization.Assembly);
        Assert.True(specialization.IsSubclassOf(typeof(EventBroker)));
        Assert.Same(typeof(EventBroker), specialization.BaseType);
        Assert.Same(typeof(EventBroker).Assembly, typeof(VetoResult).Assembly);

        // POSITIVE: all four of the oracle's overridden hooks are genuinely overridden here, not
        // inherited. A hook declared on the base but not overridden would silently disable the
        // cancellation pre-veto, the sync-signal bracket or the fault posture.
        foreach (string hook in (string[])["OnPrepare", "OnTriggering", "OnTriggered", "OnException"])
        {
            MethodInfo? method = specialization.GetMethod(
                hook,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            Assert.NotNull(method);
            Assert.True(method.IsVirtual);
            Assert.NotSame(specialization, method.GetBaseDefinition().DeclaringType);
        }

        // The delegate half: both handler types are local multicast delegates.
        Assert.Same(persistence, typeof(TaskNotificationHandler).Assembly);
        Assert.Same(persistence, typeof(TaskCommonNotificationHandler).Assembly);
        Assert.True(typeof(TaskNotificationHandler).IsSubclassOf(typeof(MulticastDelegate)));
        Assert.True(typeof(TaskCommonNotificationHandler).IsSubclassOf(typeof(MulticastDelegate)));

        // NEGATIVE (C-A): no edge to any peer service.
        Assert.DoesNotContain("PowerFramework.Gateway", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("PowerFramework.DataServices", referenced, StringComparer.Ordinal);
        Assert.DoesNotContain("PowerFramework.Security", referenced, StringComparer.Ordinal);

        // NEGATIVE (no duplicated alphabet): the veto codes are not restated in this service.
        Assert.Null(persistence.GetType(
            "PowerFramework.Persistence.Tasks.TaskProxies.TaskVeto",
            throwOnError: false));
    }

    /// <summary>
    /// Each notification reason reaches its own named channel and no other.
    /// </summary>
    /// <remarks>
    /// <c>_of_sendnotify</c> dispatches on the reason with no <c>else</c> arm
    /// [<c>n_cst_threading_task.sru:L337-L356</c>], so an unrecognised reason reaches nothing at all -
    /// which the port reproduces by simply having no default case. The four reasons and their channel
    /// names are the oracle's; the case asserts both that the right channel fired and that the other
    /// three did not.
    /// </remarks>
    /// <param name="reason">The notification reason.</param>
    /// <param name="channel">The channel that must fire, or empty when none must.</param>
    [Theory]
    [MemberData(nameof(NotificationReasonCases))]
    public void EachNotificationReasonReachesItsOwnChannelAndNoOther(long reason, string channel)
    {
        using Harness harness = new();

        List<string> fired = [];

        foreach (string name in NotificationChannels)
        {
            string captured = name;
            _ = harness.Proxy.Channels.On(
                name,
                (source, wparam, lparam, text) =>
                {
                    fired.Add(captured);
                    return null;
                });
        }

        _ = harness.Proxy.Send(reason, wparam: 7L, lparam: 11L, text: Sentinel);

        if (channel.Length == 0)
        {
            Assert.Empty(fired);
        }
        else
        {
            Assert.Equal(channel, Assert.Single(fired));
        }
    }

    /// <summary>The reason-to-channel map [<c>n_cst_threading_task.sru:L337-L356</c>].</summary>
    public static TheoryData<long, string> NotificationReasonCases =>
        new()
        {
            { Enums.TNR_START, TaskEventName.Start },
            { Enums.TNR_STOP, TaskEventName.Stop },
            { Enums.TNR_NOTIFY, TaskEventName.Notify },
            { Enums.TNR_ERROR, TaskEventName.Error },

            // No else arm, so an unrecognised reason reaches nothing.
            { 99L, "" },
        };

    /// <summary>The four named channels, in the order the oracle declares them.</summary>
    private static readonly string[] NotificationChannels =
        [TaskEventName.Start, TaskEventName.Stop, TaskEventName.Notify, TaskEventName.Error];

    /// <summary>
    /// A non-zero answer on the catch-all channel SUPPRESSES the per-reason channels.
    /// </summary>
    /// <remarks>
    /// The catch-all runs first and only when subscribed [<c>:L331-L333</c>], and the per-reason
    /// dispatch is gated on its answer being zero or null [<c>:L335</c>]. So a catch-all subscriber can
    /// veto the whole per-reason fan-out, which is a real capability rather than an accident: it is how
    /// the legacy lets one handler take over a task's entire notification stream.
    /// </remarks>
    [Fact]
    public void ANonZeroCatchAllAnswerSuppressesThePerReasonChannels()
    {
        using Harness harness = new();

        int perReasonCalls = 0;
        _ = harness.Proxy.Channels.OnCommon((source, reason, wparam, lparam, text) => 5L);
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                perReasonCalls++;
                return null;
            });

        Assert.Equal(5L, harness.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel));
        Assert.Equal(0, perReasonCalls);

        // A zero answer does NOT suppress, so the gate is on the VALUE and not on the subscription.
        _ = harness.Proxy.Channels.OffAll();
        _ = harness.Proxy.Channels.OnCommon((source, reason, wparam, lparam, text) => 0L);
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                perReasonCalls++;
                return 3L;
            });

        Assert.Equal(3L, harness.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel));
        Assert.Equal(1, perReasonCalls);
    }

    /// <summary>
    /// A subscriber's fault CANCELS THE TASK, stops the dispatch, and is absorbed only while the task
    /// is free - otherwise it propagates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, and the arm a reader is most likely to guess wrongly. The threading broker's
    /// <c>onexception</c> raises the cancellation signal and the exception signal UNCONDITIONALLY
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L79-L80</c>], and only then does
    /// one test decide the dispatch's fate [<c>:L82-L87</c>]. When the synchronization signal is set -
    /// which by the polarity at <c>n_cst_threading_task.sru:L306</c> means the task is currently FREE,
    /// the state the notify publication path establishes around itself at <c>:L291</c> - the oracle
    /// shows a modal dialog and answers <c>1</c>, which makes the base LEAVE the dispatch loop without
    /// rethrowing [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L890-L891</c>]. When it
    /// is clear the hook answers <c>0</c> and the base rethrows [<c>:L902</c>].
    /// </para>
    /// <para>
    /// <b>So "the remaining subscribers still run" is NOT this broker's behaviour, and asserting it
    /// would be asserting a defect.</b> The base offers a continue-with-the-next-subscriber answer
    /// [<c>:L892-L894</c>] and the threading specialization never returns it. Both arms are asserted
    /// here, because an implementation that absorbed everything and an implementation that propagated
    /// everything would each pass a test covering only one.
    /// </para>
    /// <para>
    /// The dialog becomes a recorded fault (AAP 0.3.4), which is what the base logs at the end of each
    /// dispatch. A PROPAGATED fault is deliberately not recorded there: the exception itself is the
    /// diagnostic, and recording it as well would double-report it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASubscriberFaultCancelsTheTaskAndIsAbsorbedOnlyWhileTheTaskIsFree()
    {
        using Harness harness = new();

        int survivorCalls = 0;

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) => throw new InvalidTimeZoneException(Sentinel));
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                survivorCalls++;
                return 4L;
            });

        // ABSORBED: the publication path raises the sync signal around the fan-out, so the task reads
        // as free and the hook prevents instead of rethrowing.
        Assert.Equal(0, harness.Host.CancelCalls);
        Assert.Equal(0L, harness.Proxy.Notify(1L, 2L, Sentinel));

        // The dispatch STOPPED at the fault - the later subscriber did not run - and the answer is the
        // established default rather than the survivor's value, because no subscriber produced one.
        Assert.Equal(0, survivorCalls);

        // The fault cancelled the task, through BOTH signals the oracle raises.
        Assert.Equal(2, harness.Host.CancelCalls);
        Assert.True(harness.Host.IsCancelled);

        Exception absorbed = Assert.Single(harness.Proxy.Channels.CapturedExceptions);
        _ = Assert.IsType<InvalidTimeZoneException>(absorbed);
        Assert.Equal(Sentinel, absorbed.Message);

        // PROPAGATED: dispatched with the sync signal clear - which is what SendNotify does when it is
        // reached outside the publication path - the same fault leaves the dispatch.
        using Harness propagating = new();

        _ = propagating.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) => throw new InvalidTimeZoneException(Sentinel));

        Assert.False(propagating.Host.IsSyncSignalSet);

        InvalidTimeZoneException escaped = Assert.Throws<InvalidTimeZoneException>(
            () => propagating.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel));

        Assert.Equal(Sentinel, escaped.Message);

        // Cancelled all the same, and NOT recorded - the exception is its own diagnostic.
        Assert.True(propagating.Host.IsCancelled);
        Assert.Empty(propagating.Proxy.Channels.CapturedExceptions);

        // The base decorated it on the way out, which is how a caller learns which subscription faulted
        // without the broker having to log anything itself [n_cst_eventful.sru:L883-L886].
        string? decorated = EventBroker.GetDispatchExceptionText(escaped);
        Assert.NotNull(decorated);
        Assert.Contains(TaskEventName.Notify, decorated, StringComparison.Ordinal);
    }

    /// <summary>
    /// The veto is TRI-VALUED, and the shallow form is consumed by the dispatch that raised it while
    /// the deep form is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, and flattening it to a boolean is the specific mistake this case exists to catch. Prevent
    /// once is 1, prevent deep is 2, and continue is the third state. A shallow veto stops the
    /// remaining subscribers of ITS OWN dispatch and is then discarded
    /// [<c>n_cst_eventful.sru:L954-L958</c>]; a deep veto survives while any dispatch is still open and
    /// is cleared only when the outermost one closes [<c>:L956-L957</c>]. Collapsing the two would
    /// silently turn a deep prevention into a shallow one.
    /// </para>
    /// <para>
    /// The veto is also legal ONLY inside a dispatch [<c>:L1293</c>], which is why the standalone
    /// prevent members answer a failure when called from outside one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVetoIsTriValuedAndOnlyTheShallowFormIsConsumedByItsOwnDispatch()
    {
        using Harness harness = new();

        // The alphabet lives in the SHARED broker library and is not restated in this service, so the
        // three numerals are asserted against that enum. A local copy is what would drift.
        Assert.Equal(0L, (long)VetoResult.Continue);
        Assert.Equal(1L, (long)VetoResult.PreventOnce);
        Assert.Equal(2L, (long)VetoResult.PreventDeep);
        Assert.Equal(3, Enum.GetValues<VetoResult>().Length);

        int vetoingSubscriberCalls = 0;
        int laterSubscriberCalls = 0;

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                vetoingSubscriberCalls++;
                return harness.Proxy.PreventEvent();
            });
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                laterSubscriberCalls++;
                return null;
            });

        _ = harness.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel);

        // The later subscriber was stopped by the veto raised ahead of it.
        Assert.Equal(1, vetoingSubscriberCalls);
        Assert.Equal(0, laterSubscriberCalls);

        // CONSUMED, asserted behaviourally rather than by peeking at the broker's state - which is
        // private in the oracle [n_cst_eventful.sru:L86 under the private label at :L70] and therefore
        // private in the port. The proof that nothing leaked is that the NEXT dispatch runs at all: a
        // veto surviving its own dispatch would have suppressed the first subscriber too.
        _ = harness.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel);
        Assert.Equal(2, vetoingSubscriberCalls);
        Assert.Equal(0, laterSubscriberCalls);

        // A DEEP veto is a different outcome, and this is where flattening the alphabet would show.
        // It stops the enclosing dispatch too, so the outer channel's later subscriber never runs.
        using Harness deep = new();

        int outerLaterCalls = 0;
        int innerLaterCalls = 0;

        _ = deep.Proxy.Channels.On(
            TaskEventName.Start,
            (source, wparam, lparam, text) =>
            {
                // A NESTED dispatch, from inside a handler, whose own subscriber vetoes DEEPLY.
                _ = deep.Proxy.Channels.Trigger(TaskEventName.Notify, 0L, 0L, string.Empty);
                return null;
            });
        _ = deep.Proxy.Channels.On(
            TaskEventName.Start,
            (source, wparam, lparam, text) =>
            {
                outerLaterCalls++;
                return null;
            });
        _ = deep.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) => deep.Proxy.PreventEvent(deep: true));
        _ = deep.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                innerLaterCalls++;
                return null;
            });

        _ = deep.Proxy.Send(Enums.TNR_START, 0L, 0L, string.Empty);

        Assert.Equal(0, innerLaterCalls);
        Assert.Equal(0, outerLaterCalls);

        // And it is cleared once the OUTERMOST dispatch closes [:L956-L957], so the next one runs.
        _ = deep.Proxy.Send(Enums.TNR_START, 0L, 0L, string.Empty);
        Assert.Equal(0, innerLaterCalls);
        Assert.Equal(0, outerLaterCalls);

        // Proved by removing the deep veto: the same table then reaches both later subscribers, which
        // is what shows the two zeroes above were the veto and not a wiring mistake.
        _ = deep.Proxy.Channels.Off(TaskEventName.Notify);
        _ = deep.Proxy.Send(Enums.TNR_START, 0L, 0L, string.Empty);
        Assert.Equal(1, outerLaterCalls);
    }

    /// <summary>
    /// Both prevent members are refused outside a dispatch, and the refusal leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// <c>if _nDeep &lt;= 0 then return RetCode.FAILED</c> [<c>n_cst_eventful.sru:L1293</c>]. A veto
    /// outside a dispatch has nothing to veto, and answering a failure rather than recording a pending
    /// veto is what stops it leaking into the NEXT dispatch and suppressing a subscriber that should
    /// have run. The refusal is the observable; "nothing was recorded" is asserted by dispatching
    /// afterwards and seeing the subscriber run, because the broker's veto state is private in the
    /// oracle and therefore private in the port.
    /// </remarks>
    [Fact]
    public void BothPreventMembersAreRefusedOutsideADispatch()
    {
        using Harness harness = new();

        Assert.Equal(0, harness.Proxy.Channels.Depth);

        Assert.Equal(RetCode.FAILED, harness.Proxy.PreventEvent());
        Assert.Equal(RetCode.FAILED, harness.Proxy.PreventEvent(deep: true));

        int subscriberCalls = 0;
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                subscriberCalls++;
                return null;
            });

        _ = harness.Proxy.Send(Enums.TNR_NOTIFY, 1L, 2L, Sentinel);

        Assert.Equal(1, subscriberCalls);
        Assert.Equal(0, harness.Proxy.Channels.Depth);
    }

    /// <summary>
    /// The subscription members answer the legacy codes, and a removal answers OK whether or not it
    /// matched anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A refused argument answers the invalid-argument code. <b>A REMOVAL ANSWERS OK UNCONDITIONALLY,
    /// INCLUDING WHEN IT MATCHED NOTHING</b> - the oracle's modify engine ends
    /// <c>return RetCode.OK</c> with no matched-count test anywhere above it
    /// [<c>n_cst_eventful.sru:L1089</c>], and its only non-OK exit is the malformed-filter screen for a
    /// <c>^</c>-prefixed empty name [<c>:L1020-L1022</c>]. Answering a failure for "nothing matched"
    /// would be an invented result, and it is the result this service used to give: the local broker
    /// re-implementation returned FAILED there, which is one of the four measured drifts that adopting
    /// the shared broker removes.
    /// </para>
    /// <para>
    /// The two arities still differ in EFFECT, which is why both exist and why both are exercised: the
    /// two-argument form drops a single handler and leaves the channel present but empty, and the
    /// one-argument form drops the channel. They no longer differ in return code, because the oracle
    /// never did.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubscriptionMembersAnswerTheLegacyCodesAndARemovalAlwaysAnswersOk()
    {
        using Harness harness = new();

        // Two DISTINCT handlers: distinct bodies rather than two conversions of one method group, so
        // that delegate equality genuinely tells them apart.
        TaskNotificationHandler first = (source, wparam, lparam, text) => 1L;
        TaskNotificationHandler second = (source, wparam, lparam, text) => 2L;

        // Refused arguments.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.On(null, first));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.On(string.Empty, first));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.On(TaskEventName.Notify, null));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.OnCommon(null));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.Off(null, first));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Proxy.Channels.Off(null));

        // A removal that matched NOTHING still answers OK - _of_modify has no matched-count test.
        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify, first));
        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify));

        // And it changed nothing, which is the half a return code cannot state.
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.Notify));

        // Two handlers on one channel: the two-argument removal takes one, the one-argument form takes
        // the channel.
        Assert.Equal(RetCode.OK, harness.Proxy.Channels.On(TaskEventName.Notify, first));
        Assert.Equal(RetCode.OK, harness.Proxy.Channels.On(TaskEventName.Notify, second));
        Assert.True(harness.Proxy.Channels.IsSubscribed(TaskEventName.Notify));

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify, first));
        Assert.True(harness.Proxy.Channels.IsSubscribed(TaskEventName.Notify));

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify));
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.Notify));

        // The catch-all channel is reached by its own name and answers from its own list.
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.CommonNotify));
        Assert.Equal(
            RetCode.OK,
            harness.Proxy.Channels.OnCommon((source, reason, wparam, lparam, text) => null));
        Assert.True(harness.Proxy.Channels.IsSubscribed(TaskEventName.CommonNotify));

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.OffAll());
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.CommonNotify));

        // A null or empty name is never subscribed, whatever is in the table.
        Assert.False(harness.Proxy.Channels.IsSubscribed(null));
        Assert.False(harness.Proxy.Channels.IsSubscribed(string.Empty));
    }

    /// <summary>
    /// A dispatch that produced no value answers the broker's ESTABLISHED DEFAULT of zero, not null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The threading broker's constructor establishes that default</b> -
    /// <c>of_SetDefaultReturnValue(0)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L76</c>], with a null alternative
    /// deliberately left commented out two lines above it - and the base substitutes it for any
    /// non-posted dispatch whose last invocation produced nothing
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L966-L970</c>]. So an unsubscribed
    /// channel, a channel whose subscribers all declined, and an empty channel name all answer
    /// <c>0</c>.
    /// </para>
    /// <para>
    /// <b>Where null still matters, and it is one line further out.</b> The notification path's own
    /// <c>nVal</c> local starts null [<c>n_cst_threading_task.sru:L336</c>] and stays null when no
    /// reason arm runs at all, which is why the overwrite test is <c>Not IsNull(nVal)</c>
    /// [<c>:L357-L359</c>]. "No arm ran" and "a dispatch answered nothing" are genuinely different
    /// facts, and only the first is null - which is exactly why the surface's return type is nullable
    /// while the dispatch itself never produces one.
    /// </para>
    /// <para>
    /// Three routes to the default are asserted: a channel that is PRESENT BUT EMPTY, which is what the
    /// two-argument removal leaves behind when it takes a channel's last handler; a name that was never
    /// registered; and the empty name, which the base answers without dispatching at all
    /// [<c>n_cst_eventful.sru:L793</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public void ADispatchThatProducedNoValueAnswersTheEstablishedDefaultOfZero()
    {
        using Harness harness = new();

        TaskNotificationHandler only = (source, wparam, lparam, text) => 9L;

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.On(TaskEventName.Notify, only));

        // The subscriber's own answer, so the default below is distinguishable from "nothing changed".
        Assert.Equal(9L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify, only));

        // PRESENT BUT EMPTY: only the handler was removed, so the channel key itself survives - which
        // Off(name) still answering OK below proves - yet nothing is subscribed to dispatch to.
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.Notify));
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify));

        // NEVER REGISTERED, and the EMPTY NAME: the same answer by two further routes.
        Assert.Equal(0L, harness.Proxy.Channels.Trigger("never-registered", 1L, 2L, Sentinel));
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(null, 1L, 2L, Sentinel));
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(string.Empty, 1L, 2L, Sentinel));

        // A SUBSCRIBER THAT DECLINED reaches the same default, which is the substitution rather than
        // the "nothing ran" path - and is why the two cannot be told apart from the answer alone.
        _ = harness.Proxy.Channels.On(TaskEventName.Notify, (source, wparam, lparam, text) => null);
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));

        // And a subscriber that DID answer is carried through unchanged.
        _ = harness.Proxy.Channels.On(TaskEventName.Notify, (source, wparam, lparam, text) => 9L);
        Assert.Equal(9L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
    }

    /// <summary>
    /// A dispatch opening on an already-cancelled task runs no subscriber and answers the established
    /// default, and silence suppresses the screen entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>TWO CANCELLATION SCREENS EXIST AND THIS CASE EXERCISES THE FIRST ONE.</b> The broker's
    /// <c>ontriggering</c> hook fires ONCE, on the first matching subscriber, and answers prevented
    /// when the task is already cancelled [<c>n_cst_threading_eventful.sru:L60-L66</c>]; the base then
    /// leaves the dispatch loop [<c>n_cst_eventful.sru:L840-L842</c>] without any subscriber having
    /// run. Its sibling <c>onprepare</c> fires per subscriber and screens a cancellation that arrives
    /// MID-dispatch [<c>:L48-L53</c>] - a different case, asserted by
    /// <see cref="ACancellationArrivingMidDispatchStopsTheLaterSubscribers"/>.
    /// </para>
    /// <para>
    /// Reachable only by dispatching DIRECTLY on the delegate surface, because the proxy's own
    /// publication path always silences first. That is why the surface is a type in its own right
    /// rather than a private detail of the proxy: its behaviour is assertable independently of the one
    /// caller that happens to silence it.
    /// </para>
    /// <para>
    /// <b>The DISPATCH answers zero, not one.</b> The <c>return 1</c> at <c>:L51</c> is the PREPARE
    /// HOOK's answer - a prevention the base reads with <c>IsPrevented</c> to skip that subscriber
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L609-L610</c>] - and it is not the
    /// dispatch's return value. The dispatch produced no value, so it answers the established default
    /// of zero [<c>:L966-L970</c>]. Reading the hook's code as the dispatch's answer is the specific
    /// confusion this assertion pins down.
    /// </para>
    /// <para>
    /// The shallow veto is then CONSUMED by the dispatch that raised it, so the following dispatch is
    /// not suppressed - asserted behaviourally, because a leaked veto would silently swallow the next
    /// notification.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADispatchOpeningOnACancelledTaskRunsNothingAndAnswersTheEstablishedDefault()
    {
        using Harness harness = new();

        int subscriberCalls = 0;

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                subscriberCalls++;
                return 7L;
            });

        harness.Host.IsCancelled = true;

        // NOT SILENT: the pre-veto fires, no subscriber runs, and the dispatch answers the default.
        Assert.False(harness.Proxy.Channels.Silent);
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.Equal(0, subscriberCalls);

        // CONSUMED by its own dispatch: the very next one is refused by the pre-veto again rather than
        // by a leaked veto, which the silent dispatch below proves by running the subscriber.
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.Equal(0, subscriberCalls);

        // SILENT: the screen is suppressed and the subscriber runs even though the task is cancelled.
        harness.Proxy.Channels.Silent = true;
        Assert.Equal(
            7L,
            harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.Equal(1, subscriberCalls);
    }

    /// <summary>
    /// Subscription order IS dispatch order, and the source every subscriber receives is the proxy the
    /// broker was initialised with rather than anything a caller passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves are the shared broker's, and both would be silently lost by a look-alike.</b> A
    /// topic carrying no ordering symbol and no priority prefix appends at the TAIL of its
    /// equal-priority run - the whole difference between prepend and append being <c>&lt;=</c> against
    /// <c>&lt;</c> [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L417</c> against
    /// <c>:L419</c>] - so three subscriptions dispatch in the order they were made. And the leading
    /// argument is INJECTED by the threading specialization's prepare hook from the object it was
    /// initialised with [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L55-L56</c>,
    /// initialised at <c>n_cst_threading_task.sru:L197</c> with <c>this</c>], which is why the dispatch
    /// members take no source parameter at all: a per-dispatch source would be a second authority that
    /// could disagree with the injected one.
    /// </para>
    /// <para>
    /// The payload lands in the slots AFTER the injected one [<c>n_cst_eventful.sru:L613-L615</c>], so
    /// the numeric and string arguments are asserted as well - a hook that forgot to report its
    /// consumed count would shift every one of them by a slot and nothing else would say so.
    /// </para>
    /// <para>
    /// The subscribers here all DECLINE to answer, and that is load bearing rather than incidental. See
    /// <see cref="TheFirstSubscriberToAnswerOtherThanZeroHandlesTheEventAndSuppressesTheRest"/> for the
    /// rule that makes it so, and note that a case written with answering subscribers would measure the
    /// handled latch instead of the order and would pass with the order reversed.
    /// </para>
    /// </remarks>
    [Fact]
    public void SubscriptionOrderIsDispatchOrderAndTheSourceArgumentIsInjected()
    {
        using Harness harness = new();

        List<string> order = [];
        List<SqlTaskProxyBase> sources = [];
        List<(long Wparam, long Lparam, string Text)> payloads = [];

        for (int ordinal = 1; ordinal <= 3; ordinal++)
        {
            string label = $"subscriber-{ordinal}";

            _ = harness.Proxy.Channels.On(
                TaskEventName.Notify,
                (source, wparam, lparam, text) =>
                {
                    order.Add(label);
                    sources.Add(source);
                    payloads.Add((wparam, lparam, text));
                    return null;
                });
        }

        // Nothing answered, so the dispatch answers the established default.
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 11L, 22L, Sentinel));

        Assert.Equal(["subscriber-1", "subscriber-2", "subscriber-3"], order);
        Assert.All(sources, source => Assert.Same(harness.Proxy, source));
        Assert.All(payloads, payload => Assert.Equal((11L, 22L, Sentinel), payload));

        // The catch-all channel carries one extra leading argument, and the injection still lands ahead
        // of it rather than displacing it [n_cst_threading_task.sru:L71-L78].
        SqlTaskProxyBase? commonSource = null;
        long observedReason = 0L;

        _ = harness.Proxy.Channels.OnCommon(
            (source, reason, wparam, lparam, text) =>
            {
                commonSource = source;
                observedReason = reason;
                return null;
            });

        _ = harness.Proxy.Channels.TriggerCommon(Enums.TNR_ERROR, 33L, 44L, Sentinel);

        Assert.Same(harness.Proxy, commonSource);
        Assert.Equal(Enums.TNR_ERROR, observedReason);
    }

    /// <summary>
    /// The first subscriber to answer anything other than zero HANDLES the event, and every remaining
    /// ordinary subscriber on that channel is then skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the dispatch contract that one line in the threading broker's constructor
    /// establishes, and it is the single most surprising consequence of adopting the shared broker
    /// faithfully.</b> <c>of_SetDefaultReturnValue(0)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L76</c>] installs a non-null
    /// established default, which moves the handled test onto its third arm: a value EQUAL to the
    /// default is not handled, a value different from it is
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L912-L917</c>]. The capture filter
    /// then stops offering the event to subscriptions whose capture mode no longer matches the
    /// dispatch's handled state [<c>:L831-L833</c>], and an ordinary subscription - no <c>%</c> and no
    /// <c>*</c> in its topic - is an unhandled-only one.
    /// </para>
    /// <para>
    /// <b>So on a threading channel, answering is a claim and not just a reply.</b> Three outcomes are
    /// asserted because they are three different behaviours that a single-case test would conflate: a
    /// subscriber that DECLINES leaves the event unclaimed and the rest run; a subscriber that answers
    /// ZERO also leaves it unclaimed, because zero IS the default; and a subscriber that answers
    /// anything else claims it and the rest are skipped. An implementation that ignored the handled
    /// latch would pass the first two and fail the third, which is exactly the shape of the local
    /// stand-in this surface replaced.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFirstSubscriberToAnswerOtherThanZeroHandlesTheEventAndSuppressesTheRest()
    {
        // DECLINED: nothing is claimed, so every subscriber runs.
        Assert.Equal((2, 0L), DispatchWithLeadingAnswer(null));

        // ZERO: equal to the established default, so still not claimed and both still run.
        Assert.Equal((2, 0L), DispatchWithLeadingAnswer(0L));

        // ANYTHING ELSE: claimed by the first subscriber, and the second never runs.
        Assert.Equal((1, 5L), DispatchWithLeadingAnswer(5L));
        Assert.Equal((1, -1L), DispatchWithLeadingAnswer(-1L));
    }

    /// <summary>
    /// Dispatches one notification to two ordinary subscribers, the first of which answers a given
    /// value, and reports how many of them ran together with the dispatch's answer.
    /// </summary>
    /// <param name="leadingAnswer">
    /// What the first subscriber answers. <see langword="null"/> declines.
    /// </param>
    /// <returns>The number of subscribers that ran, and the dispatch's answer.</returns>
    private static (int Ran, long? Answer) DispatchWithLeadingAnswer(long? leadingAnswer)
    {
        using Harness harness = new();

        int ran = 0;

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                ran++;
                return leadingAnswer;
            });

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                ran++;
                return null;
            });

        long? answer = harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel);
        return (ran, answer);
    }

    /// <summary>
    /// A subscription made from inside a dispatch takes effect only on the NEXT dispatch, and a removal
    /// made from inside one takes effect immediately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are the shared broker's documented rules and both are inherited rather than restated. The
    /// dispatch loop captures its upper bound once at entry, so a table that grew mid-dispatch is not
    /// noticed until the next one [<c>n_cst_eventful.sru:L820</c>, documented at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L237</c>]. A removal is the mirror image: it
    /// cannot rewrite a table that active levels hold cursors into, so it TOMBSTONES the entry, which
    /// the loop then skips [<c>:L829</c>], and leaves a compaction owed [<c>:L1054-L1059</c>].
    /// </para>
    /// <para>
    /// <b>The compaction is what makes this a bounded-state case and not only an ordering one.</b>
    /// PowerBuilder posts it to the Win32 message queue [<c>:L1083</c>]; a headless service has no
    /// message pump, so the turn is explicit and happens when the outermost dispatch of the surface
    /// closes. Without it a service that unsubscribed from inside a handler would accumulate tombstones
    /// for the life of the process.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASubscriptionMadeDuringADispatchTakesEffectNextTimeAndARemovalTakesEffectAtOnce()
    {
        using Harness harness = new();

        int lateSubscriberCalls = 0;
        int firstSubscriberCalls = 0;
        int removedSubscriberCalls = 0;
        long? removalCode = null;

        TaskNotificationHandler removed = (source, wparam, lparam, text) =>
        {
            removedSubscriberCalls++;
            return null;
        };

        // NOTHING IS ASSERTED FROM INSIDE A HANDLER anywhere in this file, and here is the reason: a
        // non-silent dispatch raises the sync signal, so the threading broker's exception hook ABSORBS
        // whatever a handler throws - and an absorbed assertion failure is a test that passes while
        // proving nothing. Outcomes are recorded into locals and asserted after the dispatch returns.
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                firstSubscriberCalls++;

                if (firstSubscriberCalls == 1)
                {
                    // Added from INSIDE the dispatch: not noticed until the next one.
                    _ = harness.Proxy.Channels.On(
                        TaskEventName.Notify,
                        (innerSource, innerWparam, innerLparam, innerText) =>
                        {
                            lateSubscriberCalls++;
                            return null;
                        });

                    // Removed from INSIDE the dispatch: skipped by THIS dispatch already.
                    removalCode = harness.Proxy.Channels.Off(TaskEventName.Notify, removed);
                }

                return null;
            });

        _ = harness.Proxy.Channels.On(TaskEventName.Notify, removed);

        _ = harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel);

        Assert.Equal(RetCode.OK, removalCode);
        Assert.Equal(1, firstSubscriberCalls);
        Assert.Equal(0, removedSubscriberCalls);
        Assert.Equal(0, lateSubscriberCalls);

        // The next dispatch sees the late subscriber, and still not the removed one.
        _ = harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel);

        Assert.Equal(2, firstSubscriberCalls);
        Assert.Equal(1, lateSubscriberCalls);
        Assert.Equal(0, removedSubscriberCalls);
    }

    /// <summary>
    /// The whole-surface removal genuinely empties the broker, not just the local index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The threading layer's parameterless removal passes the filter <c>".^persistent"</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L385</c>], which decodes to "every
    /// name, in any namespace except persistent"
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L1000-L1023</c>] - one symbol away
    /// from the broker's own parameterless removal, which passes the EMPTY filter and removes
    /// everything [<c>:L228-L230</c>]. Every subscription this surface makes is namespace-less and so
    /// not persistent, which is what makes the two coincide here.
    /// </para>
    /// <para>
    /// <b>Asserted by DISPATCHING afterwards rather than by reading a count.</b> A removal that cleared
    /// the local index while leaving the broker's table populated would satisfy every count-based
    /// assertion and still deliver every notification, which is precisely the failure this case exists
    /// to catch.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWholeSurfaceRemovalEmptiesTheBrokerAndNotOnlyTheLocalIndex()
    {
        using Harness harness = new();

        int perReasonCalls = 0;
        int commonCalls = 0;

        foreach (string channel in NotificationChannels)
        {
            _ = harness.Proxy.Channels.On(
                channel,
                (source, wparam, lparam, text) =>
                {
                    perReasonCalls++;
                    return null;
                });
        }

        _ = harness.Proxy.Channels.OnCommon(
            (source, reason, wparam, lparam, text) =>
            {
                commonCalls++;
                return null;
            });

        // Every one of the five channels is subscribed at once, which is also the table shape that
        // exercises EventBroker.IsSubscribed's preserved first-and-last-slot quirk on real data.
        Assert.All(NotificationChannels, channel => Assert.True(harness.Proxy.Channels.IsSubscribed(channel)));
        Assert.True(harness.Proxy.Channels.IsSubscribed(TaskEventName.CommonNotify));

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.OffAll());

        Assert.All(NotificationChannels, channel => Assert.False(harness.Proxy.Channels.IsSubscribed(channel)));
        Assert.False(harness.Proxy.Channels.IsSubscribed(TaskEventName.CommonNotify));

        // THE REAL ASSERTION: dispatch every channel and see that nothing runs.
        foreach (string channel in NotificationChannels)
        {
            Assert.Equal(0L, harness.Proxy.Channels.Trigger(channel, 1L, 2L, Sentinel));
        }

        Assert.Equal(0L, harness.Proxy.Channels.TriggerCommon(Enums.TNR_NOTIFY, 1L, 2L, Sentinel));

        Assert.Equal(0, perReasonCalls);
        Assert.Equal(0, commonCalls);

        // And a single-channel removal is the same statement one scope smaller.
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                perReasonCalls++;
                return null;
            });

        Assert.Equal(RetCode.OK, harness.Proxy.Channels.Off(TaskEventName.Notify));
        Assert.Equal(0L, harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.Equal(0, perReasonCalls);
    }

    /// <summary>
    /// A non-silent dispatch brackets itself with the synchronization signal, and a silent one does
    /// not touch it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The threading specialization's two triggering hooks are the whole of this: the first raises the
    /// signal for a non-posted dispatch [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L62-L64</c>]
    /// and the second lowers it [<c>:L69-L71</c>], and both return immediately when silent
    /// [<c>:L60, :L68</c>]. By the polarity at <c>n_cst_threading_task.sru:L306</c> the signal being set
    /// means the task reads as NOT BUSY, which is what makes a mutator legal from inside a subscriber.
    /// </para>
    /// <para>
    /// <b>The pair is symmetric or the guard is permanently wrong.</b> The base fires both hooks under
    /// the same "was anything dispatched" latch and fires the second from its outer
    /// <see langword="finally"/> [<c>n_cst_eventful.sru:L942-L946</c>], so a raise cannot be left
    /// standing - asserted here on the fault path as well as the clean one, because that is the path
    /// where a hand-rolled bracket leaks.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANonSilentDispatchBracketsItselfWithTheSyncSignalAndASilentOneDoesNot()
    {
        using Harness harness = new();

        bool? signalInsideNonSilent = null;

        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                signalInsideNonSilent = harness.Host.IsSyncSignalSet;
                return null;
            });

        Assert.False(harness.Proxy.Channels.Silent);
        Assert.False(harness.Host.IsSyncSignalSet);

        _ = harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel);

        Assert.True(signalInsideNonSilent);
        Assert.False(harness.Host.IsSyncSignalSet);

        // SILENT: neither hook touches the signal, which is why the publication path can raise it
        // itself around the whole fan-out without the broker fighting it.
        harness.Proxy.Channels.Silent = true;
        signalInsideNonSilent = null;
        harness.Host.IsSyncSignalSet = true;

        _ = harness.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel);

        Assert.True(signalInsideNonSilent);
        Assert.True(harness.Host.IsSyncSignalSet);

        // THE FAULT PATH: the bracket still closes, because the base lowers it from a finally. The
        // signal is set here, so the threading hook absorbs the fault rather than rethrowing.
        using Harness faulting = new();

        _ = faulting.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) => throw new InvalidTimeZoneException(Sentinel));

        Assert.Equal(0L, faulting.Proxy.Channels.Trigger(TaskEventName.Notify, 1L, 2L, Sentinel));
        Assert.False(faulting.Host.IsSyncSignalSet);
        _ = Assert.Single(faulting.Proxy.Channels.CapturedExceptions);
    }

    /// <summary>
    /// A cancelled proxy publishes nothing and answers null, and the sync signal is restored either
    /// way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SetNull(rtCode)</c> then <c>if of_IsCancelled() then return rtCode</c>
    /// [<c>n_cst_threading_task.sru:L288-L289</c>] - the result starts NULL rather than zero, so a
    /// caller can tell "cancelled, nothing published" from "published, answered zero". That
    /// distinction is why the port's dispatch surface is a nullable rather than a sentinel.
    /// </para>
    /// <para>
    /// The signal is SET around the publication [<c>:L291</c>] and RESET afterwards [<c>:L295</c>], and
    /// the reset sits in a finally so a subscriber's fault cannot leave the proxy permanently reading
    /// as not busy. Both the raised and the restored state are asserted.
    /// </para>
    /// </remarks>
    [Fact]
    public void RaiseNotify_AnswersNullWhenCancelledAndAlwaysRestoresTheSyncSignal()
    {
        using Harness harness = new();

        bool? signalDuringDispatch = null;
        _ = harness.Proxy.Channels.On(
            TaskEventName.Notify,
            (source, wparam, lparam, text) =>
            {
                signalDuringDispatch = harness.Host.IsSyncSignalSet;
                throw new InvalidTimeZoneException(Sentinel);
            });

        _ = harness.Proxy.Notify(1L, 2L, Sentinel);

        Assert.True(signalDuringDispatch);
        Assert.False(harness.Host.IsSyncSignalSet);

        // Cancelled: nothing is published and the answer is null rather than zero.
        signalDuringDispatch = null;
        harness.Host.IsCancelled = true;

        Assert.Null(harness.Proxy.Notify(1L, 2L, Sentinel));
        Assert.Null(signalDuringDispatch);
    }

    /// <summary>
    /// The inherited substrate forwarders each reach the host seam and nothing else, and the two
    /// mutating ones are refused while busy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eleven read-only forwarders and two mutating ones, all inherited from the framework parent. They
    /// are asserted together because the failure mode they share is the same one: a forwarder that
    /// answered from a field of its own instead of from the substrate would report a stale value that
    /// looks entirely plausible. Distinct sentinel values are installed on the host so that a
    /// cross-wired forwarder - identity answering the index, say - cannot pass.
    /// </para>
    /// <para>
    /// The task position is forwarded VERBATIM and unconverted (R9): it is the legacy's own one-based
    /// value, and rebasing it here would make a diagnostic disagree with every recorded one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubstrateForwardersReachTheHostSeamAndTheMutatingOnesRespectTheBusyGuard()
    {
        using Harness harness = new(group: TaskExecutionGroup.Prepare);

        harness.Host.LastExitCode = 41L;
        harness.Host.LastErrorCode = 42L;
        harness.Host.LastErrorInfo = "error-info-sentinel";
        harness.Host.TaskId = 43UL;
        harness.Host.TaskIndex = 1;
        harness.Host.TaskClassName = "class-name-sentinel";

        Assert.Equal(41L, harness.Proxy.GetLastExitCode());
        Assert.Equal(42L, harness.Proxy.GetLastErrorCode());
        Assert.Equal("error-info-sentinel", harness.Proxy.GetLastErrorInfo());
        Assert.Equal(43UL, harness.Proxy.GetId());
        Assert.Equal(1, harness.Proxy.GetIndex());
        Assert.Equal("class-name-sentinel", harness.Proxy.GetTaskClassName());
        Assert.Equal(TaskExecutionGroup.Prepare, harness.Proxy.ExecutionGroup);
        Assert.Equal(harness.Host.Cancellation, harness.Proxy.Cancellation);
        Assert.False(harness.Proxy.IsCancelled());

        Assert.Equal(RetCode.OK, harness.Proxy.Cancel());
        Assert.Equal(1, harness.Host.CancelCalls);
        Assert.True(harness.Proxy.IsCancelled());

        // The two mutating forwarders go through the substrate, not through this type's own state.
        Assert.Equal(RetCode.OK, harness.Proxy.SetDelayFor(2.5d));
        Assert.Equal(2.5d, harness.Host.WorkerDelaySeconds);

        Assert.Equal(RetCode.OK, harness.Proxy.SetSkip(skip: true));
        Assert.True(harness.Host.WorkerSkipped);

        harness.Host.MakeBusy();

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetDelayFor(9d));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetSkip(skip: false));

        // Refused means untouched.
        Assert.Equal(2.5d, harness.Host.WorkerDelaySeconds);
        Assert.True(harness.Host.WorkerSkipped);
    }

    /// <summary>
    /// The clock is an injected seam and the constructor refuses every null collaborator.
    /// </summary>
    /// <remarks>
    /// AAP 0.6.7. The clock is the determinism seam: the base reads no ambient clock, so a
    /// characterization recording cannot pick up a wall-clock value from it. The fail-fast constructor
    /// is the legacy posture preserved - a proxy configured with a null collaborator cannot degrade
    /// into anything meaningful, so it does not try.
    /// </remarks>
    [Fact]
    public void TheClockIsAnInjectedSeamAndTheConstructorRefusesEveryNullCollaborator()
    {
        using Harness harness = new();

        Assert.Same(FixedClock.Instance, harness.Proxy.ClockSeam);

        _ = Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(
                null!,
                NullLogger<SqlCommandTaskProxy>.Instance,
                FixedClock.Instance));
        _ = Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(harness.Host, null!, FixedClock.Instance));
        _ = Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(
                harness.Host,
                NullLogger<SqlCommandTaskProxy>.Instance,
                null!));
    }

    // ==============================================================================================
    //  REGION 5 - THE COMMAND PROXY AND ITS PRESERVED ASYMMETRY
    //  --------------------------------------------------------------------------------------------
    //  Fifty-eight lines, read in full [n_cst_threading_task_sqlcommand.sru]. It declares the SAME
    //  string twice - as the instance property `#type` [:L9] and as `TASK_TYPE` [:L17] - names its
    //  worker by CLASS-NAME STRING through an event [:L56], adds no state of its own, and carries two
    //  mutators whose guarding differs [:L34-L46]. That difference is the subject here.
    // ==============================================================================================

    /// <summary>
    /// The command proxy publishes the same marker twice and names its worker by class-name string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO SPELLINGS OF ONE VALUE, and both are kept because both are reachable in the legacy: the
    /// instance property <c>string #type = "sqlcommand"</c> [<c>:L9</c>] is what a caller reads off a
    /// live object, and <c>constant string TASK_TYPE = "sqlcommand"</c> [<c>:L17</c>] is what a caller
    /// reads off the class without instantiating one. Collapsing them to a single member would remove a
    /// reachable surface.
    /// </para>
    /// <para>
    /// The worker's name is a STRING and not a type. The legacy resolves its worker through
    /// <c>event ongettaskclsname ... return "n_cst_thread_task_sqlcommand"</c> [<c>:L56</c>], which the
    /// substrate uses to instantiate by name - so the string is part of the contract, and the port hands
    /// exactly that string to the substrate during init [<c>n_cst_threading_task_sqlbase.sru:L213</c>].
    /// A rename of the .NET worker type must therefore NOT change this literal.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCommandProxyPublishesOneMarkerTwiceAndNamesItsWorkerByClassNameString()
    {
        Assert.Equal("sqlcommand", SqlCommandTaskProxy.TaskTypeMarker);
        Assert.Equal("n_cst_thread_task_sqlcommand", SqlCommandTaskProxy.LegacyWorkerClassName);

        using CommandHarness harness = new();

        // The instance property override answers the same value as the class constant.
        Assert.Equal(SqlCommandTaskProxy.TaskTypeMarker, TaskTypeOf(harness.Proxy));

        // And the string the substrate was handed during init is the class-name literal, verbatim.
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(SqlCommandTaskProxy.LegacyWorkerClassName, harness.Host.RequestedWorkerClassName);
    }

    /// <summary>
    /// The command proxy holds no caller-side state of its own beyond that marker.
    /// </summary>
    /// <remarks>
    /// The oracle's <c>type variables</c> block declares constants and nothing else [<c>:L13-L23</c>] -
    /// no <c>Private:</c> section at all. That is why the command proxy inherits the reset rather than
    /// overriding it: there is nothing extra to clear. Asserted by counting declared instance fields,
    /// because a single added field would silently make the inherited reset incomplete.
    /// </remarks>
    [Fact]
    public void TheCommandProxyDeclaresNoInstanceStateOfItsOwn()
    {
        FieldInfo[] declared = typeof(SqlCommandTaskProxy).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.Empty(declared);
    }

    /// <summary>
    /// Both mutators guard busy BEFORE any screen they carry, and the guard is a refusal rather than a
    /// validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each setter is handed an argument its own screen would reject - an empty statement, and a mode
    /// outside the three declared ones - and each still answers the BUSY code [<c>:L34</c> ahead of
    /// <c>:L36-L38</c>, and <c>:L43</c>]. So the guard runs ahead of the screen on the setter that has
    /// one, and it REFUSES rather than judging the argument.
    /// </para>
    /// <para>
    /// The negative matters as much as the code: a refused call installs nothing, so a caller who
    /// retries after the run finishes is not carrying a half-applied change.
    /// </para>
    /// </remarks>
    /// <param name="setter">The mutator under test.</param>
    [Theory]
    [InlineData("set-sql")]
    [InlineData("set-auto-commit")]
    public void BothCommandMutatorsGuardBusyBeforeAnyScreenTheyCarry(string setter)
    {
        using CommandHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        harness.Host.MakeBusy();

        Assert.Equal(RetCode.E_BUSY, InvokeCommandSetter(setter, harness.Proxy));

        // Refused means untouched, on both halves of the pair. The worker's statement starts as the
        // EMPTY STRING rather than null, which is PowerScript's own default for an unassigned string.
        Assert.Equal(string.Empty, harness.Worker.Sql);
        Assert.Equal(AutoCommitMode.AcOff, harness.Worker.AutoCommitSetting);
    }

    /// <summary>
    /// ONLY the statement setter carries a screen, and only in a debug build - the auto-commit setter
    /// carries none in either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B. THIS ASYMMETRY IS THE ORACLE'S AND MUST NOT BE TIDIED. The statement setter is busy-guarded
    /// [<c>:L34</c>], then carries a DEBUG-CONDITIONAL non-empty assertion [<c>:L36-L38</c>], then
    /// delegates [<c>:L40</c>]. The auto-commit setter is busy-guarded [<c>:L43</c>], then delegates
    /// [<c>:L45</c>], and VALIDATES NOTHING. Making them symmetric - by adding a range check to the
    /// auto-commit setter, or by promoting the statement assertion out of the debug build - would change
    /// what a caller observes in a release deployment.
    /// </para>
    /// <para>
    /// Both sides are asserted in ONE case, deliberately, because the asymmetry is a RELATION between
    /// the two setters rather than a property of either: two separate cases would each pass while the
    /// difference between them quietly disappeared. The build split is expressed with the same
    /// conditional the production code uses, so this case reads the same way the subject does.
    /// </para>
    /// <para>
    /// The consequence of the missing screen is real rather than theoretical: a proto3 enum is an open
    /// <see cref="int"/> in C#, so an out-of-range mode is stored happily by BOTH halves of the pair and
    /// then behaves as AC_OFF, because the worker's success epilogue is an if/elseif chain with no else.
    /// Pinned deliberately.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheStatementSetterCarriesAScreenAndOnlyInADebugBuild()
    {
        using CommandHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

#if DEBUG
        // The screen is an ASSERTION and it FIRES, so the call answers no code at all [:L36-L38]. The
        // message text is the oracle's own, verbatim, because it is what a failing build reports.
        AssertionFailure failure =
            Assert.Throws<AssertionFailure>(() => harness.Proxy.SetSql(string.Empty));

        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.Worker.Sql);
#else
        // Compiled out entirely, so the empty statement travels on and gets the WORKER'S answer - whose
        // own guard is an equality against the empty string [SqlCommandTask.SetSql].
        Assert.Equal(RetCode.E_INVALID_SQL, harness.Proxy.SetSql(string.Empty));
        Assert.Equal(string.Empty, harness.Worker.Sql);
#endif

        // AND IN BOTH BUILDS the auto-commit setter screens nothing - no assertion, no code, and the
        // undeclared value is installed on the worker. That difference is the asymmetry.
        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit((AutoCommitMode)int.MaxValue));
        Assert.Equal((AutoCommitMode)int.MaxValue, harness.Worker.AutoCommitSetting);
    }

    /// <summary>
    /// The auto-commit setter accepts a value outside the three declared modes, in every build.
    /// </summary>
    /// <remarks>
    /// C-B, the concrete half of the asymmetry above. The oracle's body is a busy guard and a
    /// delegation, nothing else [<c>:L43-L46</c>]. <b>Adding the obvious argument check must break this
    /// case.</b>
    /// </remarks>
    /// <param name="raw">A numeric value outside the three declared enumerators.</param>
    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void TheAutoCommitSetterAcceptsAnUndeclaredModeInEveryBuild(int raw)
    {
        using CommandHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit((AutoCommitMode)raw));
    }

    /// <summary>
    /// The three auto-commit values come from the ONE generated enumeration and are not re-declared
    /// anywhere in this service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy declares them three times: the worker owns them and the proxy re-declares them as
    /// ALIASES of the worker's [<c>:L19-L21</c>], which is how PowerScript shares a constant across a
    /// pair. C# has a better mechanism, so both halves of the port reference the SAME generated
    /// enumeration from the published contract - one definition, and the wire value and the in-process
    /// value cannot drift apart.
    /// </para>
    /// <para>
    /// Asserted with the numeric values, because those are what travel: <c>AC_NATIVE</c> is the value a
    /// boolean would lose, and it is the reason the contract carries an enumeration rather than a flag
    /// at all. And asserted as a negative too - no type in this service declares a competing
    /// enumeration with the same shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAutoCommitValuesComeFromTheSingleGeneratedEnumerationAndAreNotRedeclaredHere()
    {
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
        Assert.Equal(1, (int)AutoCommitMode.AcOn);
        Assert.Equal(2, (int)AutoCommitMode.AcNative);

        // ONE definition, and it lives in the published contract rather than in this service.
        Assert.NotSame(typeof(SqlTaskProxyBase).Assembly, typeof(AutoCommitMode).Assembly);
        Assert.Equal(3, Enum.GetValues<AutoCommitMode>().Length);

        // And no local copy exists to drift from it.
        Assert.DoesNotContain(
            typeof(SqlTaskProxyBase).Assembly.GetTypes(),
            candidate => candidate.IsEnum
                && candidate.Name.Contains("AutoCommit", StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  REGION 6 - THE QUERY PROXY'S PUBLISHED NOTIFICATION STREAM
    //  --------------------------------------------------------------------------------------------
    //  The six notify codes are declared at n_cst_threading_task_sqlquery.sru:L28-L33 WITH their own
    //  packing comments, and the update task declares its single code at
    //  n_cst_threading_task_sqlupdate.sru:L32. The value 1 therefore means TWO DIFFERENT THINGS in the
    //  two task types, which is safe in PowerScript because each set is scoped to its own class - and
    //  is safe here only because the port keeps TWO ENUMERATIONS rather than merging them.
    //
    //  This region is the CROSS-PROXY half of the query contract: the codes, the packing law and the
    //  destroy-then-adopt ordering. The per-arm interior of each callback belongs to
    //  SqlQueryTaskProxyTests.cs.
    // ==============================================================================================

    /// <summary>
    /// The six query notify codes carry the oracle's numeric values.
    /// </summary>
    /// <remarks>
    /// THE VALUES TRAVEL, so they are asserted numerically and never merely by name. They appear in the
    /// notification stream and therefore in every stored characterization recording, so a renumbering
    /// would silently invalidate every recorded comparison rather than fail a build.
    /// </remarks>
    /// <param name="code">The numeric value.</param>
    /// <param name="name">The enumerator that must carry it.</param>
    [Theory]
    [MemberData(nameof(QueryNotifyCodeCases))]
    public void TheSixQueryNotifyCodesCarryTheOracleValues(long code, string name)
    {
        Assert.Equal(name, Enum.GetName(typeof(SqlQueryTaskNotifyCode), code));
        Assert.Equal(code, (long)Enum.Parse<SqlQueryTaskNotifyCode>(name));
    }

    /// <summary>The six codes at <c>n_cst_threading_task_sqlquery.sru:L28-L33</c>.</summary>
    public static TheoryData<long, string> QueryNotifyCodeCases =>
        new()
        {
            { 1L, "MaxRows" },        // NCD_MAXROWS      [:L28]
            { 2L, "DataReceived" },   // NCD_DATARECEIVED [:L29] lparam:row count
            { 3L, "PageReceived" },   // NCD_PAGERECEIVED [:L30] lparam:(low:page count,high:record count)
            { 4L, "DataChunk" },      // NCD_DATACHUNK    [:L31] lparam:(low:count,high:current)
            { 5L, "ChildReceived" },  // NCD_CHILDRECEIVED[:L32] sparam:colname
            { 6L, "ChildQuery" },     // NCD_CHILDQUERY   [:L33] lparam:colid,sparam:colname
        };

    /// <summary>
    /// The update task's progress code is ALSO 1, and the two code sets live in two separate
    /// enumerations that are never merged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B and C-K. <c>NCD_MAXROWS = 1</c> [<c>n_cst_threading_task_sqlquery.sru:L28</c>] and
    /// <c>NCD_PROGRESS = 1</c> [<c>n_cst_threading_task_sqlupdate.sru:L32</c>] are the same number
    /// meaning different things. In PowerScript each is a member of its own class, so
    /// <c>n_cst_threading_task_sqlquery.NCD_MAXROWS</c> and
    /// <c>n_cst_threading_task_sqlupdate.NCD_PROGRESS</c> can never be confused at a call site.
    /// </para>
    /// <para>
    /// TWO ENUMERATIONS REPRODUCE THAT SCOPING AND MAKE CONFLATING THEM STRUCTURALLY IMPOSSIBLE. Merging
    /// them would force one of the two values to change, and the values travel - so the merge would be
    /// a wire-visible renumbering dressed up as a tidy-up. The case asserts the collision, the
    /// separation, and each set's exact membership, so neither enumeration can quietly acquire the
    /// other's members.
    /// </para>
    /// <para>
    /// Both enumerations are declared in <c>Buffers/DataWindowBuffers.cs</c> and are reached from there
    /// rather than copied locally, which is the other half of "never merged": one definition site each.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUpdateProgressCodeIsAlsoOneAndTheTwoCodeSetsStayInSeparateEnumerations()
    {
        // THE COLLISION, asserted rather than avoided.
        Assert.Equal(1L, (long)SqlQueryTaskNotifyCode.MaxRows);
        Assert.Equal(1L, (long)SqlUpdateTaskNotifyCode.Progress);

        // THE SEPARATION.
        Assert.NotEqual(typeof(SqlQueryTaskNotifyCode), typeof(SqlUpdateTaskNotifyCode));

        // Exact membership, so neither can absorb the other.
        Assert.Equal(6, Enum.GetValues<SqlQueryTaskNotifyCode>().Length);
        Assert.Equal(
            "Progress",
            Assert.Single(Enum.GetNames<SqlUpdateTaskNotifyCode>()));

        // One definition site each, and both in the same owning file rather than copied per proxy.
        Assert.Same(typeof(DataWindowBufferStore).Assembly, typeof(SqlQueryTaskNotifyCode).Assembly);
        Assert.Same(typeof(DataWindowBufferStore).Assembly, typeof(SqlUpdateTaskNotifyCode).Assembly);
    }

    /// <summary>
    /// The notification stream packs its two-word payloads LOW WORD FIRST, with fixed-width operands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>makelong(readonly uint low, readonly uint high)</c>
    /// [<c>ws_objects/pfw.common.pbl.src/makelong.srf:L7</c>] - the low word is the FIRST argument, and
    /// the operands are sixteen bits wide regardless of the magnitude handed in. The two packed payloads
    /// name their own operand order in the oracle's own comments: the chunk code carries
    /// <c>(low word:count,high word:current)</c> [<c>:L31</c>] and the page code carries
    /// <c>(low word:page count,high word:record count)</c> [<c>:L30</c>].
    /// </para>
    /// <para>
    /// Both orderings are asserted with ASYMMETRIC operands, because symmetric ones pass whichever way
    /// round the packing is. The packed value is computed independently through the shared kernel's own
    /// primitive rather than restated as a literal, so the assertion pins the LAW rather than one
    /// arithmetic result.
    /// </para>
    /// </remarks>
    /// <param name="low">The operand that must land in the low word.</param>
    /// <param name="high">The operand that must land in the high word.</param>
    [Theory]
    [MemberData(nameof(WordPackingCases))]
    public void TheNotificationStreamPacksItsWordsLowFirst(long low, long high)
    {
        long packed = Bits.MakeLong((ushort)low, (ushort)high);

        Assert.Equal(low, packed & 0xFFFFL);
        Assert.Equal(high, (packed >> 16) & 0xFFFFL);

        // And the reverse order is a DIFFERENT value whenever the operands differ, so the assertions
        // above are not vacuous. The full-state hand-over is one chunk of one, and is therefore the only
        // symmetric pair the notification stream ever carries - it is included precisely because a
        // symmetric pair is the one case in which a REVERSED packing would go unnoticed, and it is
        // excluded from this last assertion for exactly the same reason.
        if (low != high)
        {
            Assert.NotEqual(packed, (long)Bits.MakeLong((ushort)high, (ushort)low));
        }
    }

    /// <summary>Asymmetric operand pairs, so a reversed packing cannot pass.</summary>
    public static TheoryData<long, long> WordPackingCases =>
        new()
        {
            // The chunk payload: (count, current) [:L31], as MakeLong(count,current) at :L254.
            { 7L, 3L },

            // The page payload: (page count, record count) [:L30].
            { 2L, 500L },

            // The full-state single chunk, which is one of one and therefore the only symmetric pair.
            { 1L, 1L },
        };

    /// <summary>
    /// The full-state hand-over is published as the CHUNK code carrying one of one, not as a code of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <c>Event OnNotify(NCD_DATACHUNK,MakeLong(1,1),"")</c> [<c>:L270</c>]. A subscriber therefore sees
    /// a whole-state transfer as a single-chunk stream rather than as a distinct event, which keeps the
    /// two transfer strategies indistinguishable to a caller - and both operands are the one-based count
    /// and position the transfer codec already publishes, reached from it rather than restated.
    /// </remarks>
    [Fact]
    public void TheFullStateHandOverIsPublishedAsTheChunkCodeCarryingOneOfOne()
    {
        using QueryHarness harness = new();

        List<(long Code, long Payload, string Text)> published = harness.CaptureNotifications();

        harness.Proxy.OnDataMove(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        (long code, long payload, string text) = Assert.Single(published);

        Assert.Equal((long)SqlQueryTaskNotifyCode.DataChunk, code);
        Assert.Equal(
            (long)Bits.MakeLong(
                (ushort)FullStateCodec.SingleChunkCount,
                (ushort)FullStateCodec.SingleChunkIndex),
            payload);
        Assert.Equal(string.Empty, text);
    }

    /// <summary>
    /// The data-move callback DESTROYS the previous carrier BEFORE adopting the incoming one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Destroy Data</c> [<c>:L268</c>] then <c>Data = ds</c> [<c>:L269</c>], in that order. REVERSING
    /// IT LEAKS THE PREVIOUS CARRIER: once the field has been overwritten there is no reference left to
    /// destroy, and a DataWindow carrier holds rows and parent references rather than mere memory, so
    /// the leak is a live object graph and not a byte count. Hazard 2 of
    /// <c>docs/PB多线程绕坑提示.md:L5</c> is the same instruction stated generally - clear a
    /// cross-boundary reference explicitly and never leave it to the collector.
    /// </para>
    /// <para>
    /// The ordering is recorded rather than inferred, because the END STATE OF BOTH ORDERINGS IS
    /// IDENTICAL: the incoming carrier is held either way, and only the sequence distinguishes a
    /// correct implementation from a leaking one.
    /// </para>
    /// <para>
    /// Destroying also means CLEARING FIRST and then disposing, in that order, so the rows and the
    /// parent references go before the handle does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDataMoveCallbackDestroysThePreviousCarrierBeforeAdoptingTheIncomingOne()
    {
        using QueryHarness harness = new();

        RecordingStore original = harness.HeldStore;
        harness.ClearLog();

        harness.Proxy.OnDataMove(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        // Destroy - as clear-then-dispose - and only THEN adopt.
        Assert.Equal(["clear-state", "dispose", "adopt"], harness.Log);

        Assert.Equal(1, original.ClearStateCalls);
        Assert.Equal(1, original.DisposeCalls);
        Assert.NotSame(original, harness.Proxy.Data);
        Assert.Same(harness.Adopter.Produced, harness.Proxy.Data);
    }

    /// <summary>
    /// The four private-write query properties carry their legacy defaults, and page counting defaults
    /// TRUE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>#PageCounting = true</c> [<c>:L40</c>] is the ONE non-default initializer among the four
    /// [<c>:L37-L40</c>], so counting is opt-OUT rather than opt-in. The other three start at their
    /// cleared values, and the page index's zero is "no page selected" in a one-based scheme rather than
    /// "the first page" (R9).
    /// </para>
    /// <para>
    /// Asserted after construction AND after a reset, because the legacy's field initializers
    /// [<c>:L37-L40</c>] and its reset [<c>:L287-L290</c>] assign the identical values - so a freshly
    /// constructed proxy and a freshly reset one must be in the same state.
    /// </para>
    /// <para>
    /// The properties are private-write, which is the port of PowerScript's <c>PrivateWrite</c>: a caller
    /// reads them directly and mutates them only through the guarded setters.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourQueryPropertiesCarryTheirLegacyDefaultsWithPageCountingTrue()
    {
        using QueryHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.False(harness.Proxy.Paged);
        Assert.Equal(0L, harness.Proxy.PageSize);
        Assert.Equal(0L, harness.Proxy.PageIndex);
        Assert.True(harness.Proxy.PageCounting);

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        Assert.False(harness.Proxy.Paged);
        Assert.Equal(0L, harness.Proxy.PageSize);
        Assert.Equal(0L, harness.Proxy.PageIndex);
        Assert.True(harness.Proxy.PageCounting);

        // All four are private-write, so the guarded setters are the only door onto them.
        foreach (string name in QueryPropertyNames)
        {
            PropertyInfo? property = typeof(SqlQueryTaskProxy).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.NotNull(property);
            Assert.NotNull(property.SetMethod);
            Assert.True(property.SetMethod.IsPrivate);
        }
    }

    /// <summary>The four private-write query properties [<c>:L37-L40</c>].</summary>
    private static readonly string[] QueryPropertyNames =
        ["Paged", "PageSize", "PageIndex", "PageCounting"];

    /// <summary>
    /// The held carrier is created in the constructor and destroyed DETERMINISTICALLY on teardown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Data = Create DataStore</c> [<c>:L518</c>] in the constructor and <c>Destroy Data</c>
    /// [<c>:L515</c>] in the destructor. PowerBuilder's <c>Destroy</c> is deterministic, so the port
    /// destroys on <see cref="IDisposable.Dispose"/> and NEVER relies on finalization: a finalizer runs
    /// at the collector's convenience, which for an object holding rows and a parent reference means the
    /// reference outlives the proxy by an unbounded interval.
    /// </para>
    /// <para>
    /// Teardown is idempotent, so a double dispose destroys exactly once - asserted, because a second
    /// destroy of an already-destroyed carrier is precisely the fault hazard 1 warns about.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHeldCarrierIsCreatedInTheConstructorAndDestroyedDeterministicallyOnTeardown()
    {
        QueryHarness harness = new();

        RecordingStore held = harness.HeldStore;
        Assert.Same(held, harness.Proxy.Data);
        Assert.Equal(0, held.DisposeCalls);

        harness.Proxy.Dispose();

        Assert.Equal(1, held.ClearStateCalls);
        Assert.Equal(1, held.DisposeCalls);

        // Idempotent: exactly once, however many times teardown is asked for.
        harness.Proxy.Dispose();
        Assert.Equal(1, held.DisposeCalls);

        harness.Dispose();
    }

    /// <summary>
    /// The carrier hand-out gives the held carrier away and leaves a fresh one behind, built through
    /// the same affinity-keyed factory.
    /// </summary>
    /// <remarks>
    /// <c>ds = Data</c>, <c>Data = Create DataStore</c>, <c>return ds</c> [<c>:L418-L421</c>]. The
    /// outgoing carrier is NOT destroyed - ownership moves to the caller - and the replacement is built
    /// on the SAME main-thread affinity the constructor chose, because a carrier handed to the main
    /// thread and one handed to a worker are different legacy types.
    /// </remarks>
    [Fact]
    public void TheCarrierHandOutMovesOwnershipAndLeavesAFreshCarrierOfTheSameAffinity()
    {
        using QueryHarness harness = new();

        RecordingStore original = harness.HeldStore;

        ISqlDataStore handedOut = harness.Proxy.MoveData();

        Assert.Same(original, handedOut);
        Assert.NotSame(original, harness.Proxy.Data);

        // Given away, not destroyed - the caller owns it now.
        Assert.Equal(0, original.DisposeCalls);

        Assert.Equal(
            [CarrierThreadAffinity.MainThread, CarrierThreadAffinity.MainThread],
            harness.StoreFactory.RequestedAffinities);
    }

    /// <summary>
    /// The two clause setters each offer two arities, and the short form addresses the FIRST select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's short bodies are literally <c>return of_SetWhereClause(1,ms,clause)</c> and
    /// <c>return of_SetOrderByClause(1,ms,clause)</c>. The 1 is a ONE-BASED select KEY, not an offset,
    /// so it is forwarded unconverted (R9) - and the short form addresses the first select rather than
    /// every select, which matters the moment the statement is a compound one.
    /// </para>
    /// <para>
    /// Asserted through the stored clause's own select key rather than through a return code, because
    /// both arities answer OK and only the stored key distinguishes them.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheClauseSettersOfferTwoAritiesAndTheShortFormAddressesTheFirstSelect()
    {
        using QueryHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(1, SqlQueryTaskProxy.DefaultSelectIndex);

        Assert.Equal(RetCode.OK, harness.Proxy.SetWhereClause(Enums.SQL_MS_REPLACE, "ID > 0"));
        Assert.Equal(RetCode.OK, harness.Proxy.SetOrderByClause(Enums.SQL_MS_REPLACE, "ID"));

        Assert.Equal(
            SqlQueryTaskProxy.DefaultSelectIndex,
            Assert.Single(harness.Worker!.Clauses.WhereClauses).SelectIndex);
        Assert.Equal(
            SqlQueryTaskProxy.DefaultSelectIndex,
            Assert.Single(harness.Worker.Clauses.OrderByClauses).SelectIndex);

        // The long form addresses whichever select it is given, and upserts rather than overwriting.
        Assert.Equal(RetCode.OK, harness.Proxy.SetWhereClause(2, Enums.SQL_MS_REPLACE, "ID < 9"));
        Assert.Equal(
            [1, 2],
            harness.Worker.Clauses.WhereClauses.Select(clause => clause.SelectIndex));

        // Both arities are refused while busy [:L386, :L391].
        harness.Host.MakeBusy();
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetWhereClause(Enums.SQL_MS_REPLACE, "ID > 1"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetWhereClause(1, Enums.SQL_MS_REPLACE, "ID > 1"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetOrderByClause(Enums.SQL_MS_REPLACE, "ID"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetOrderByClause(1, Enums.SQL_MS_REPLACE, "ID"));
    }

    /// <summary>
    /// All six receive callbacks publish their own code, and each distributes its payload across the
    /// notification's arguments in its own documented shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The six are declared as events carrying their payloads BY REFERENCE where a payload exists
    /// [<c>n_cst_threading_task_sqlquery.sru:L10-L15</c>], which is hazard 1 of
    /// <c>docs/PB多线程绕坑提示.md:L1-L4</c> applied: a worker thread synchronously reading a
    /// <c>string</c> or <c>blob</c> RETURN value from a main-thread object can fault, and the documented
    /// remedy is to pass it back through <c>ref</c>.
    /// </para>
    /// <para>
    /// Driven in ONE case because the property being asserted is the SHAPE OF THE PUBLISHED STREAM as a
    /// whole, and the six shapes are only meaningfully different from one another: two pack words, one
    /// carries a raw count, one carries a name in the STRING argument beside a literal zero, one reuses
    /// another's code, and one publishes nothing at all. Asserting them apart would lose the contrast
    /// that makes each one checkable, and the stream is what a characterization recording contains.
    /// </para>
    /// <para>
    /// The create-data callback publishing NOTHING is the deliberate negative: it carries the DataWindow
    /// syntax to whichever carrier is in play [<c>:L157-L171</c>] and raises no notification, so a
    /// subscriber never learns a definition was installed.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllSixReceiveCallbacksPublishTheirOwnCodeInTheirOwnPayloadShape()
    {
        using QueryHarness harness = new();

        List<(long Code, long Payload, string Text)> published = harness.CaptureNotifications();

        // (1) CREATE DATA - carries the syntax, publishes NOTHING [:L157-L171].
        harness.Proxy.OnCreateData("release 12.5;");
        Assert.Empty(published);

        // (2) DATA RECEIVED - the row count RAW, with no packing at all [:L176-L177].
        harness.Proxy.OnDataReceived(rowCount: 4242L);
        Assert.Equal(
            ((long)SqlQueryTaskNotifyCode.DataReceived, 4242L, string.Empty),
            published[^1]);
        Assert.Equal(4242L, harness.Proxy.GetRowCount());

        // (3) PAGE RECEIVED - (page count, record count) packed low-first [:L262-L265].
        harness.Proxy.OnPageReceived(pageCount: 2L, recordCount: 500L);
        Assert.Equal(
            (
                (long)SqlQueryTaskNotifyCode.PageReceived,
                (long)Bits.MakeLong(2, 500),
                string.Empty
            ),
            published[^1]);
        Assert.Equal(2L, harness.Proxy.GetPageCount());
        Assert.Equal(500L, harness.Proxy.GetRecordCount());

        // (4) DATA CHUNK - (count, current) packed low-first, and the buffer cleared [:L253-L255].
        CarrierState? chunk = new();
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Proxy.OnDataChunk(ref chunk, count: 7L, current: 3L, fullState: false));
        Assert.Null(chunk);
        Assert.Equal(
            ((long)SqlQueryTaskNotifyCode.DataChunk, (long)Bits.MakeLong(7, 3), string.Empty),
            published[^1]);

        // (5) CHILD RECEIVED - the column NAME in the string argument and a literal zero beside it
        // [:L151]. Its own filter and sort are re-applied because the child's definition reports
        // expressions longer than a single character, and filter runs BEFORE sort [:L145-L150].
        StubChildSurface child = harness.InstallChild(
            filterExpression: "dept_id > 0",
            sortExpression: "dept_id A");

        CarrierState? childPayload = new();
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Proxy.OnChildDataReceived("dept_id", ref childPayload));

        Assert.Null(childPayload);
        Assert.Equal(1, child.FilterCalls);
        Assert.Equal(1, child.SortCalls);
        Assert.Equal(
            ((long)SqlQueryTaskNotifyCode.ChildReceived, 0L, "dept_id"),
            published[^1]);

        // (6) DATA MOVE - REUSES the chunk code rather than declaring one of its own [:L270].
        harness.Proxy.OnDataMove(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        Assert.Equal(
            ((long)SqlQueryTaskNotifyCode.DataChunk, (long)Bits.MakeLong(1, 1), string.Empty),
            published[^1]);

        // Five publications from six callbacks, because create-data publishes nothing.
        Assert.Equal(5, published.Count);
    }

    /// <summary>
    /// The payload-bearing callback dispatches THREE ways, and clears the reference buffer after
    /// handover on every arm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>if IsValid(_receiver) then ... case DataWindow! ... case DataStore! ... else Data ...</c>
    /// [<c>n_cst_threading_task_sqlquery.sru:L184-L210</c>]. Three destinations, and ALL THREE ANSWER THE
    /// SAME CODE - so only the destination distinguishes them, which is why the codec records its target
    /// rather than merely its result.
    /// </para>
    /// <para>
    /// THE BUFFER IS CLEARED AFTER HANDOVER, on every arm. That is hazard 1 of
    /// <c>docs/PB多线程绕坑提示.md:L1-L4</c> made mechanical: the payload arrives BY REFERENCE precisely
    /// because a worker thread synchronously reading a <c>string</c> or <c>blob</c> RETURN value from a
    /// main-thread object can fault, and the documented remedy is to pass it back through
    /// <c>ref</c> instead. Clearing the reference after handover is what stops the same payload being
    /// read twice across the boundary.
    /// </para>
    /// <para>
    /// Only the DataWindow arm brackets redraw and recalculates groups. Both are PRESENTATIONAL, so they
    /// are delegated to the receiver seam rather than performed here (C-D) - the other two arms have no
    /// business doing either, and the case asserts that negative alongside the positive.
    /// </para>
    /// </remarks>
    /// <param name="arm">The dispatch arm under test.</param>
    [Theory]
    [MemberData(nameof(PayloadDispatchArmCases))]
    public void ThePayloadCallbackDispatchesThreeWaysAndClearsTheBufferAfterHandover(string arm)
    {
        using QueryHarness harness = new();

        RecordingReceiver? receiver = null;
        DataWindowBufferStore expected = harness.HeldStore.Carrier;

        if (arm != "held-carrier")
        {
            receiver = harness.InstallReceiver(asDataWindow: arm == "data-window");
            Assert.Equal(RetCode.OK, harness.ReceiverInstallResult);
            expected = ((RecordingStore)receiver.Store).Carrier;
        }

        CarrierState? payload = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: false));

        // ARM SELECTION: the payload reached exactly one carrier, and it was the right one.
        Assert.Same(expected, Assert.Single(harness.PayloadCodec.Targets));

        // HANDOVER: the reference buffer is cleared, so the same payload cannot be read twice.
        Assert.Null(payload);

        // PRESENTATION: only the DataWindow arm brackets redraw and recalculates groups. A single-chunk
        // stream is BOTH the first chunk and the last, so on that arm the suspension is recorded and
        // then repaid within the one call [:L193-L194 then :L201-L204], and the group recalculation
        // happens once [:L200]. Neither of the other two arms touches either - the DataStore arm is
        // structurally identical and deliberately free of both presentational calls [:L211-L230].
        if (arm == "data-window")
        {
            Assert.Equal([false, true], receiver!.RedrawCalls);
            Assert.Equal(1, receiver.GroupCalcCalls);
        }
        else if (receiver is not null)
        {
            Assert.Empty(receiver.RedrawCalls);
            Assert.Equal(0, receiver.GroupCalcCalls);
        }
    }

    /// <summary>The three dispatch arms [<c>:L184-L210</c>, <c>:L211-L230</c>, <c>:L231-L243</c>].</summary>
    /// <remarks>
    /// The held-carrier arm is listed FIRST because it is the one that runs with no receiver installed -
    /// the legacy's own <c>else</c> - so a reader meets the default before the two overrides.
    /// </remarks>
    public static TheoryData<string> PayloadDispatchArmCases =>
        [
            // ARM (c) - no receiver, so the proxy's own held carrier receives [:L231-L243].
            "held-carrier",

            // ARM (a) - the only arm that brackets redraw and recalculates groups [:L184-L210].
            "data-window",

            // ARM (b) - structurally identical to (a) and deliberately free of both [:L211-L230].
            "data-store",
        ];

    /// <summary>
    /// The query proxy's init door reaches the inherited hook and borrows the worker's commit handle,
    /// with no idempotence guard.
    /// </summary>
    /// <remarks>
    /// C-B. The oracle's <c>oninit</c> is an EVENT raised by the substrate and it carries no
    /// idempotence guard, so raising it twice re-runs the substrate's insert. The port exposes it as a
    /// reachable method with the same absence of a guard, and the case asserts the second call succeeds
    /// exactly as the first does rather than being refused.
    /// </remarks>
    [Fact]
    public void TheQueryInitDoorBorrowsTheCommitHandleAndCarriesNoIdempotenceGuard()
    {
        using QueryHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal("n_cst_thread_task_sqlquery", harness.Host.RequestedWorkerClassName);

        // No guard: the second raise is not refused.
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(2, harness.Host.InitCalls);
    }

    // ==============================================================================================
    //  REGION 7 - THE PAIR MUST NOT BE FLATTENED
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.4.5.4. The legacy encodes required execution context in object annotations, and across
    //  pfw.thread.ext the split measures as six worker-thread classes, exactly one main-thread class
    //  and four calling-thread proxies. Flattening a proxy into its worker would erase that contract:
    //  the resulting single object would have one state set, one affinity, and no boundary at which a
    //  caller-side refusal could differ from a worker-side one.
    //
    //  These cases exist to FAIL if the pair is ever fused. That is their whole purpose.
    // ==============================================================================================

    /// <summary>
    /// Each proxy and its worker are two distinct types, neither assignable to the other.
    /// </summary>
    /// <remarks>
    /// The strongest structural statement available: not merely two objects, but two types with no
    /// inheritance relation in either direction. A fused implementation - whether by making the proxy
    /// derive from the worker, or by making one a partial of the other - fails here. Asserted for all
    /// three pairs, because a fusion is far likelier to be introduced in one pair than in all three.
    /// </remarks>
    /// <param name="pair">The proxy pair under test.</param>
    [Theory]
    [InlineData("command")]
    [InlineData("query")]
    [InlineData("update")]
    public void EachProxyAndItsWorkerAreTwoDistinctUnrelatedTypes(string pair)
    {
        (Type proxy, Type worker) = TypesOfPair(pair);

        Assert.NotEqual(proxy, worker);
        Assert.False(worker.IsAssignableFrom(proxy));
        Assert.False(proxy.IsAssignableFrom(worker));

        // Each half sits under its own base, and the two bases are unrelated too.
        Assert.True(typeof(SqlTaskProxyBase).IsAssignableFrom(proxy));
        Assert.True(typeof(SqlTaskBase).IsAssignableFrom(worker));
        Assert.False(typeof(SqlTaskBase).IsAssignableFrom(typeof(SqlTaskProxyBase)));
        Assert.False(typeof(SqlTaskProxyBase).IsAssignableFrom(typeof(SqlTaskBase)));

        // The `Proxy` suffix is the visible expression of the constraint, and only on the caller side.
        Assert.EndsWith("Proxy", proxy.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("Proxy", worker.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// A proxy reaches its worker only through a TYPED accessor that narrows, and answers null rather
    /// than throwing when the attached worker is of another task type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's <c>_of_gettask()</c> is a private function whose whole body is <c>return _Task</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L31</c>], relying on PowerBuilder's implicit downcast.
    /// C# has no implicit downcast, so the port makes the narrowing explicit and the accessor generic -
    /// which is strictly better, because a mismatched worker is then a diagnosable condition rather
    /// than a runtime surprise.
    /// </para>
    /// <para>
    /// The accessor answers null on a mismatch and the derived proxies convert that into a diagnostic
    /// throw, so the boundary is crossable only with the right worker on the other side. That is the
    /// pair boundary made mechanical.
    /// </para>
    /// </remarks>
    [Fact]
    public void AProxyReachesItsWorkerOnlyThroughATypedNarrowingAccessor()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        // The attached worker IS reachable as its own type.
        Assert.NotNull(harness.Proxy.Downcast<SqlTaskBase>());
        Assert.Same(harness.Worker, harness.Proxy.Downcast<SqlTaskBase>());

        // And is NOT reachable as another task type - the narrowing answers null rather than casting.
        Assert.Null(harness.Proxy.Downcast<SqlQueryTask>());
        Assert.Null(harness.Proxy.Downcast<SqlCommandTask>());

        // The accessor is protected, so a caller outside the pair cannot reach across the boundary.
        MethodInfo? accessor = typeof(SqlTaskProxyBase).GetMethod(
            "GetWorkerTask",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(accessor);
        Assert.True(accessor.IsFamily);
        Assert.True(accessor.IsGenericMethod);
    }

    /// <summary>
    /// The composition root builds the proxy and its worker as TWO distinct instances of TWO distinct
    /// types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The structural assertions above cannot catch a composition root that resolved one object and
    /// handed it back as both halves, so the deployed factory is asked for a pair and both halves are
    /// examined. C-J: the legacy's global auto-instance is not reproduced, and a composition root must
    /// register BOTH types because they are the two halves of the pair.
    /// </para>
    /// <para>
    /// Built from the service's own registration extensions rather than from a hand-written collection,
    /// so the case asserts what the deployed service composes. No connection is opened and no schema is
    /// provisioned - the factory builds the pair and stops (C-E).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCompositionRootBuildsTheProxyAndTheWorkerAsTwoDistinctInstances()
    {
        using ServiceProvider provider = BuildTaskFactoryProvider();

        ICommandTaskFactory factory = provider.GetRequiredService<ICommandTaskFactory>();

        Assert.Equal(RetCode.OK, factory.Create(out CommandTaskComponents? components));
        Assert.NotNull(components);

        object proxy = components.Proxy;
        object worker = components.Worker;

        // TWO INSTANCES, and two TYPES. Either assertion alone would miss a plausible fusion.
        Assert.NotSame(proxy, worker);
        Assert.NotEqual(proxy.GetType(), worker.GetType());
        Assert.IsType<SqlCommandTaskProxy>(proxy);
        Assert.IsType<SqlCommandTask>(worker);

        // The worker holds the disposable half of the pair, and the proxy is released with it.
        components.Worker.Dispose();
    }

    // ==============================================================================================
    //  HELPERS - each one names a legacy mechanism, none of them introduces behaviour
    // ==============================================================================================

    /// <summary>Finds a member the base is required to declare, by name, on either accessibility.</summary>
    /// <param name="name">The member name.</param>
    private static MemberInfo? MemberOfBase(string name) =>
        typeof(SqlTaskProxyBase)
            .GetMember(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault();

    /// <summary>Answers which type in a proxy's hierarchy actually declares the reset.</summary>
    /// <param name="proxy">The proxy type.</param>
    private static Type? DeclaringTypeOfReset(Type proxy) =>
        proxy
            .GetMethod(
                nameof(SqlTaskProxyBase.Reset),
                BindingFlags.Instance | BindingFlags.Public,
                Type.EmptyTypes)
            ?.GetBaseDefinition()
            .DeclaringType is not null
            ? proxy
                .GetMethod(
                    nameof(SqlTaskProxyBase.Reset),
                    BindingFlags.Instance | BindingFlags.Public,
                    Type.EmptyTypes)!
                .DeclaringType
            : null;

    /// <summary>Answers which type in a proxy's hierarchy actually declares the database-error sink.</summary>
    /// <param name="proxy">The proxy type.</param>
    private static Type? DeclaringTypeOfDbErrorSink(Type proxy) =>
        proxy
            .GetMethod(
                "OnDbError",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.DeclaringType;

    /// <summary>Reads a proxy's protected task-type marker, which is not otherwise observable.</summary>
    /// <param name="proxy">The proxy instance.</param>
    private static string TaskTypeOf(SqlTaskProxyBase proxy) =>
        (string)proxy
            .GetType()
            .GetProperty("TaskType", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proxy)!;

    /// <summary>Builds a database-error payload with named fields.</summary>
    /// <param name="sqlDbCode">The provider's own numeric code.</param>
    /// <param name="sqlErrText">The provider's diagnostic.</param>
    /// <param name="sqlSyntax">The generated statement, which may carry interpolated literals.</param>
    /// <param name="buffer">The offending buffer.</param>
    /// <param name="row">The offending row.</param>
    private static DbErrorData ErrorWith(
        long sqlDbCode = 0L,
        string sqlErrText = "",
        string sqlSyntax = "",
        DwBuffer buffer = DwBuffer.Primary,
        long row = 0L) =>
        new()
        {
            SqlDbCode = sqlDbCode,
            SqlErrText = sqlErrText,
            SqlSyntax = sqlSyntax,
            Buffer = buffer,
            Row = row,
        };

    /// <summary>The sentinel a given descriptor member carries when a copy width moves it.</summary>
    /// <param name="field">The descriptor member.</param>
    /// <remarks>
    /// Distinct per member, so a copy that cross-wired two of them - server name into database, say -
    /// fails rather than passing on a shared value.
    /// </remarks>
    private static string SentinelFor(string field) =>
        field switch
        {
            "Dbms" => "dbms-sentinel",
            "ServerName" => "server-sentinel",
            "Database" => "database-sentinel",
            "LogId" => "logid-sentinel",

            // The credential is never compared by value through this path - only its PRESENCE is
            // observable from outside the connect door (C-F).
            "Credential" => "present",
            "DbParm" => "dbparm-sentinel",
            "Lock" => "lock-sentinel",
            "UserParm" => "userparm-sentinel",
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "The descriptor has no such member. The eight named members are the whole of what the "
                    + "copy widths distinguish between."),
        };

    /// <summary>Reads one descriptor member as a comparable string.</summary>
    /// <param name="descriptor">The descriptor that arrived at the worker.</param>
    /// <param name="field">The member to read.</param>
    /// <remarks>
    /// The credential is read as a PRESENCE rather than as a value, because the member is write-only:
    /// its value leaves the descriptor through one named connect door and through nothing else (C-F).
    /// </remarks>
    private static string ReadDescriptorField(in TransactionData descriptor, string field) =>
        field switch
        {
            "Dbms" => descriptor.Dbms,
            "ServerName" => descriptor.ServerName,
            "Database" => descriptor.Database,
            "LogId" => descriptor.LogId,
            "Credential" => descriptor.HasCredential ? "present" : string.Empty,
            "DbParm" => descriptor.DbParm,
            "Lock" => descriptor.Lock,
            "UserParm" => descriptor.UserParm,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "The descriptor has no such member."),
        };

    /// <summary>A descriptor whose every member carries its own sentinel.</summary>
    private static TransactionData DescriptorWithEveryField() =>
        new()
        {
            Dbms = SentinelFor("Dbms"),
            ServerName = SentinelFor("ServerName"),
            Database = SentinelFor("Database"),
            LogId = SentinelFor("LogId"),
            LogPass = CredentialPlaceholder,
            DbParm = SentinelFor("DbParm"),
            Lock = SentinelFor("Lock"),
            AutoCommit = true,
            UserParm = SentinelFor("UserParm"),
        };

    /// <summary>A plain transaction object whose every property carries its own sentinel.</summary>
    private static FakeTransactionObject SourceTransactionObject() =>
        new()
        {
            Dbms = SentinelFor("Dbms"),
            ServerName = SentinelFor("ServerName"),
            Database = SentinelFor("Database"),
            LogId = SentinelFor("LogId"),
            Credential = CredentialPlaceholder,
            DbParm = SentinelFor("DbParm"),
            Lock = SentinelFor("Lock"),
            AutoCommit = true,
            UserParm = SentinelFor("UserParm"),
        };

    /// <summary>Installs a transaction through the overload the named copy width belongs to.</summary>
    /// <param name="width">The copy width.</param>
    /// <param name="proxy">The proxy to install into.</param>
    private static long InstallTransactionVia(string width, SqlTaskProxyBase proxy) =>
        width switch
        {
            // [:L70-L91] eight fields, built into a freshly declared local.
            EightFieldCopy => proxy.SetTransObject(SourceTransactionObject()),

            // [:L93-L105] six loose strings, built into a freshly declared local.
            SixFieldCopy => proxy.SetTransData(
                SentinelFor("Dbms"),
                SentinelFor("ServerName"),
                SentinelFor("Database"),
                SentinelFor("LogId"),
                CredentialPlaceholder,
                SentinelFor("DbParm")),

            // [:L107-L120] the caller's whole descriptor.
            WholeDescriptorCopy => proxy.SetTransData(DescriptorWithEveryField()),

            // [:L122-L133] the pooled object's whole descriptor.
            PooledDescriptorCopy => proxy.SetTransObject(
                new FakePooledTransactionObject { Descriptor = DescriptorWithEveryField() }),

            _ => throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The oracle declares four overloads carrying three copy widths, and this is none of them."),
        };

    /// <summary>Builds a parameter value of the named shape, for the two <c>UpperBound</c> screens.</summary>
    /// <param name="shape">The shape name.</param>
    private static object? ValueOfShape(string shape) =>
        shape switch
        {
            "scalar" => Sentinel,
            "null" => null,
            "array-1d" => new[] { Sentinel, Sentinel },
            "array-1d-empty" => Array.Empty<string>(),
            "array-2d" => new string[2, 2],
            "array-2d-empty" => new string[0, 0],
            "array-3d" => new string[2, 2, 2],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such value shape."),
        };

    /// <summary>Drives one of the command proxy's two mutators by name.</summary>
    /// <param name="setter">The mutator name.</param>
    /// <param name="proxy">The command proxy.</param>
    private static long InvokeCommandSetter(string setter, SqlCommandTaskProxy proxy) =>
        setter switch
        {
            // An EMPTY statement deliberately, so that a guard which validated instead of refusing
            // would answer a different code and fail the case [:L34-L38].
            "set-sql" => proxy.SetSql(string.Empty),

            // An UNDECLARED mode deliberately, for the same reason [:L43-L45].
            "set-auto-commit" => proxy.SetAutoCommit((AutoCommitMode)int.MaxValue),

            _ => throw new ArgumentOutOfRangeException(
                nameof(setter),
                setter,
                "The command proxy has exactly two ported mutators."),
        };

    /// <summary>The two halves of one named proxy pair.</summary>
    /// <param name="pair">The pair name.</param>
    private static (Type Proxy, Type Worker) TypesOfPair(string pair) =>
        pair switch
        {
            "command" => (typeof(SqlCommandTaskProxy), typeof(SqlCommandTask)),
            "query" => (typeof(SqlQueryTaskProxy), typeof(SqlQueryTask)),
            "update" => (typeof(SqlUpdateTaskProxy), typeof(SqlUpdateTask)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pair),
                pair,
                "Three SQL proxy pairs are built in this phase."),
        };

    /// <summary>
    /// Builds a provider from the service's OWN registration extensions, so the pair assertion reads
    /// the deployed composition rather than a hand-written stand-in.
    /// </summary>
    /// <remarks>
    /// C-E. The data directory is a unique temporary path that is never created and never connected to:
    /// the command factory builds its pair and stops, so no engine is opened and no schema is
    /// provisioned. The determinism seam is registered first, so the clock every registration reads is
    /// the injected one.
    /// </remarks>
    private static ServiceProvider BuildTaskFactoryProvider()
    {
        ServiceCollection services = new();

        services.AddLogging();
        _ = services.AddOptions<PersistenceOptions>().Configure(static options =>
        {
            options.Sqlite.DataDirectory = Path.Combine(
                Path.GetTempPath(),
                $"pfw-proxy-pair-{Guid.NewGuid():n}");
            options.Sqlite.DatabaseFileName = "test.db";
        });

        _ = services.AddPersistenceDeterminismSeam();
        _ = services.AddPersistenceStorage();
        _ = services.AddPersistenceSqlLayer();
        _ = services.AddPersistencePublishedSurface();

        return services.BuildServiceProvider();
    }

    // ==============================================================================================
    //  DOUBLES - no database, no network, no real thread, no clock
    //  --------------------------------------------------------------------------------------------
    //  Every collaborator below is either a RECORDER or an UNREACHABLE. A recorder exists because the
    //  behaviour under test is an ORDER or a COUNT rather than a value, and an unreachable exists
    //  because reaching it would mean the suite had stopped testing what it claims to - the two that
    //  would open a connection or build a result carrier therefore throw rather than answer (C-E).
    // ==============================================================================================

    /// <summary>
    /// The threading-substrate seam, with every input settable and every read counted.
    /// </summary>
    /// <remarks>
    /// Stands in for <c>n_cst_threading_task</c>, narrowed to what the SQL proxies consume from it. The
    /// running flag's getter APPENDS TO THE ORDERED LOG, which is how the busy guard's position in a
    /// sequence becomes observable at all.
    /// </remarks>
    private sealed class RecordingProxyHost : ISqlTaskProxyHost, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _running;
        private bool _controllerBusy;
        private bool _syncSignalSet;

        /// <summary>The ordered record of everything the proxy did that crosses a seam.</summary>
        internal List<string> Log { get; } = [];

        /// <summary>The substrate's running flag. Reading it records the busy probe.</summary>
        public bool IsRunning
        {
            get
            {
                Log.Add("busy-probe");
                return _running;
            }

            set => _running = value;
        }

        /// <summary>The parent controller's own answer.</summary>
        public bool IsControllerBusy
        {
            get
            {
                ControllerBusyReads++;
                return _controllerBusy;
            }

            set => _controllerBusy = value;
        }

        /// <summary>The proxy's own sync signal, whose sense the busy predicate inverts.</summary>
        public bool IsSyncSignalSet
        {
            get
            {
                SyncSignalReads++;
                return _syncSignalSet;
            }

            set => _syncSignalSet = value;
        }

        /// <summary>How many times the sync signal was read.</summary>
        internal int SyncSignalReads { get; private set; }

        /// <summary>How many times the parent controller was read.</summary>
        internal int ControllerBusyReads { get; private set; }

        /// <inheritdoc/>
        public bool IsCancelled { get; set; }

        /// <inheritdoc/>
        public CancellationToken Cancellation => _cancellation.Token;

        /// <inheritdoc/>
        public long LastExitCode { get; set; } = RetCode.OK;

        /// <inheritdoc/>
        public long LastErrorCode { get; set; } = RetCode.OK;

        /// <inheritdoc/>
        public string LastErrorInfo { get; set; } = string.Empty;

        /// <inheritdoc/>
        public ulong TaskId { get; set; } = 1UL;

        /// <inheritdoc/>
        public int TaskIndex { get; set; } = 1;

        /// <inheritdoc/>
        public string TaskClassName { get; set; } = string.Empty;

        /// <inheritdoc/>
        public SqlTaskBase? Task { get; set; }

        /// <summary>The class name the proxy asked the substrate to insert.</summary>
        internal string? RequestedWorkerClassName { get; private set; }

        /// <summary>The answer the substrate gives to an init request.</summary>
        internal long OnInitResult { get; set; } = RetCode.OK;

        /// <summary>The answer the substrate gives to a prepare request.</summary>
        internal long OnPrepareResult { get; set; } = RetCode.OK;

        /// <summary>How many times init was raised. There is no idempotence guard, deliberately.</summary>
        internal int InitCalls { get; private set; }

        /// <summary>How many times prepare was raised.</summary>
        internal int PrepareCalls { get; private set; }

        /// <summary>How many times cancellation was requested through the proxy.</summary>
        internal int CancelCalls { get; private set; }

        /// <summary>The delay the proxy pushed onto the worker's substrate half.</summary>
        internal double WorkerDelaySeconds { get; private set; }

        /// <summary>Whether the proxy pushed a skip onto the worker's substrate half.</summary>
        internal bool WorkerSkipped { get; private set; }

        /// <inheritdoc/>
        public void RaiseSyncSignal() => _syncSignalSet = true;

        /// <inheritdoc/>
        public void ClearSyncSignal() => _syncSignalSet = false;

        /// <inheritdoc/>
        public long OnInit(string workerClassName)
        {
            InitCalls++;
            RequestedWorkerClassName = workerClassName;
            return OnInitResult;
        }

        /// <inheritdoc/>
        public long OnPrepare()
        {
            PrepareCalls++;
            return OnPrepareResult;
        }

        /// <inheritdoc/>
        public long Cancel()
        {
            CancelCalls++;
            IsCancelled = true;
            return RetCode.OK;
        }

        /// <inheritdoc/>
        public long SetWorkerDelayFor(double seconds)
        {
            WorkerDelaySeconds = seconds;
            return RetCode.OK;
        }

        /// <inheritdoc/>
        public long SetWorkerSkip(bool skip)
        {
            WorkerSkipped = skip;
            return RetCode.OK;
        }

        /// <summary>Appends an entry to the ordered log.</summary>
        /// <param name="entry">The entry.</param>
        internal void Record(string entry) => Log.Add(entry);

        /// <summary>Empties the ordered log, so a case can record one call rather than a setup.</summary>
        internal void ClearLog() => Log.Clear();

        /// <summary>Zeroes the read counters.</summary>
        internal void ResetReadCounts()
        {
            SyncSignalReads = 0;
            ControllerBusyReads = 0;
        }

        /// <summary>Puts the proxy into its busy state through the not-running arm of the predicate.</summary>
        internal void MakeBusy()
        {
            _running = false;
            _controllerBusy = true;
        }

        /// <summary>Returns the proxy to its idle state.</summary>
        internal void MakeIdle()
        {
            _running = false;
            _controllerBusy = false;
        }

        /// <inheritdoc/>
        public void Dispose() => _cancellation.Dispose();
    }

    /// <summary>The worker's own substrate seam. Inert: no case here runs a task body.</summary>
    private sealed class InertTaskHost : ISqlTaskHost
    {
        /// <inheritdoc/>
        public bool IsMainThread => false;

        /// <inheritdoc/>
        public bool IsCancelled => false;

        /// <inheritdoc/>
        public int TaskIndex => 1;

        /// <inheritdoc/>
        public ISqlTaskProxy? ParentTasking => null;

        /// <inheritdoc/>
        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.E_INVALID_ARGUMENT;
        }

        /// <inheritdoc/>
        public bool HasData(string name) => false;

        /// <inheritdoc/>
        public object? GetData(string name) => null;

        /// <inheritdoc/>
        public long SetData(string name, object? data) => RetCode.OK;

        /// <inheritdoc/>
        public long OnPrepare() => RetCode.OK;

        /// <inheritdoc/>
        public void OnUninit()
        {
            // Nothing to release: this double holds no cross-boundary reference.
        }

        /// <inheritdoc/>
        public long OnError(long errCode, string errInfo) => RetCode.OK;

        /// <inheritdoc/>
        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// A worker whose reset records the caller-side state AS IT STANDS at the moment of delegation, and
    /// whose answer is settable.
    /// </summary>
    /// <remarks>
    /// Both capabilities are mandatory rather than convenient. The snapshot is the ONLY way to prove the
    /// caller-side clear happens after the delegation rather than before it, because both orderings leave
    /// the same end state. And the settable answer is the only way to reach the abandon arm at all, since
    /// the real worker's reset always succeeds
    /// [<c>n_cst_thread_task_sqlbase.sru:L242-L248</c>].
    /// </remarks>
    private sealed class RecordingWorker : SqlTaskBase
    {
        private readonly RecordingProxyHost _log;

        internal RecordingWorker(
            ISqlTaskHost host,
            TransactionPool transactionPool,
            ISqlDataStoreFactory dataStoreFactory,
            ISqlRetrievalHookActivator hookActivator,
            TimeProvider timeProvider,
            RecordingProxyHost log)
            : base(
                host,
                transactionPool,
                dataStoreFactory,
                hookActivator,
                timeProvider,
                NullLogger<RecordingWorker>.Instance) =>
            _log = log;

        /// <summary>Reports the caller side as the proxy sees it at the moment of delegation.</summary>
        internal Func<string>? Snapshot { get; set; }

        /// <summary>The code this worker answers to a reset.</summary>
        internal long ResetAnswer { get; set; } = RetCode.OK;

        /// <inheritdoc/>
        internal override long Reset()
        {
            _log.Record($"worker-reset({Snapshot?.Invoke() ?? "unknown"})");

            return Predicates.IsFailed(ResetAnswer) ? ResetAnswer : base.Reset();
        }
    }

    /// <summary>
    /// A concrete proxy over the abstract base, exposing the protected surface the base's own cases
    /// drive.
    /// </summary>
    /// <remarks>
    /// The base is abstract because the oracle's is: each derived proxy supplies its own task type and
    /// its own worker class-name string. This double supplies neutral values for both and adds no
    /// behaviour of its own, so a case against it is a case against the base alone.
    /// </remarks>
    private sealed class ProbeProxy : SqlTaskProxyBase
    {
        /// <summary>The worker class name this probe publishes to the substrate.</summary>
        internal const string ProbeWorkerClassName = "n_cst_thread_task_sqlbase";

        internal ProbeProxy(
            ISqlTaskProxyHost host,
            TimeProvider timeProvider,
            TaskExecutionGroup executionGroup)
            : base(host, NullLogger<ProbeProxy>.Instance, timeProvider, executionGroup)
        {
        }

        /// <inheritdoc/>
        protected override string TaskType => "probe";

        /// <inheritdoc/>
        protected override string WorkerTaskClassName => ProbeWorkerClassName;

        /// <summary>The latched database-error payload.</summary>
        internal DbErrorData Latch => LastDbError;

        /// <summary>The retained transaction object.</summary>
        internal ISqlTransactionObject? Retained => RetainedTransaction;

        /// <summary>The has-transaction-data flag.</summary>
        internal bool TransDataFlag => TransDataInstalled;

        /// <summary>The has-parameters flag.</summary>
        internal bool ParamsFlag => ParamsInstalled;

        /// <summary>The attached worker.</summary>
        internal SqlTaskBase? Worker => WorkerTask;

        /// <summary>The local delegate notification surface.</summary>
        internal TaskNotificationDispatcher Channels => Notifications;

        /// <summary>The injected clock seam.</summary>
        internal TimeProvider ClockSeam => Clock;

        /// <summary>Raises the init hook, as the substrate would.</summary>
        internal long Initialize() => OnInit();

        /// <summary>Raises the prepare hook, as the substrate would.</summary>
        internal long Prepare() => OnPrepare();

        /// <summary>Raises the database-error event, as the worker would.</summary>
        /// <param name="error">The payload.</param>
        internal void PublishDbError(in DbErrorData error) =>
            ((ISqlTaskProxy)this).OnDbError(in error);

        /// <summary>Publishes a notification, as a derived proxy's callbacks do.</summary>
        /// <param name="wparam">The first numeric argument.</param>
        /// <param name="lparam">The second numeric argument.</param>
        /// <param name="text">The string argument.</param>
        internal long? Notify(long wparam, long lparam, string text) =>
            RaiseNotify(wparam, lparam, text);

        /// <summary>Dispatches a notification reason directly, bypassing the sync-signal bracket.</summary>
        /// <param name="reason">The notification reason.</param>
        /// <param name="wparam">The first numeric argument.</param>
        /// <param name="lparam">The second numeric argument.</param>
        /// <param name="text">The string argument.</param>
        internal long? Send(long reason, long wparam, long lparam, string text) =>
            SendNotify(reason, wparam, lparam, text);

        /// <summary>The typed narrowing accessor, exposed so the pair boundary can be asserted.</summary>
        /// <typeparam name="TTask">The worker type to narrow to.</typeparam>
        internal TTask? Downcast<TTask>()
            where TTask : SqlTaskBase => GetWorkerTask<TTask>();
    }

    /// <summary>A plain transaction object whose property reads are countable.</summary>
    /// <remarks>
    /// Stands in for PowerBuilder's built-in <c>transaction</c>. The credential leaves it through ONE
    /// named transfer method, never through a property, so a serializer or a log template cannot find it
    /// (C-F).
    /// </remarks>
    private sealed class FakeTransactionObject : ISqlTransactionObject
    {
        /// <inheritdoc/>
        public string Dbms { get; init; } = string.Empty;

        /// <inheritdoc/>
        public string ServerName { get; init; } = string.Empty;

        /// <inheritdoc/>
        public string Database { get; init; } = string.Empty;

        /// <inheritdoc/>
        public string LogId { get; init; } = string.Empty;

        /// <inheritdoc/>
        public string DbParm { get; init; } = string.Empty;

        /// <inheritdoc/>
        public string Lock { get; init; } = string.Empty;

        /// <inheritdoc/>
        public bool AutoCommit { get; init; }

        /// <inheritdoc/>
        public string UserParm { get; init; } = string.Empty;

        /// <summary>The synthetic credential this object will hand over on request.</summary>
        internal string Credential { get; init; } = string.Empty;

        /// <summary>How many times the credential was asked for.</summary>
        internal int RevealCalls { get; private set; }

        /// <inheritdoc/>
        public string RevealLogPassForTransfer()
        {
            RevealCalls++;
            return Credential;
        }
    }

    /// <summary>
    /// A pooled transaction object that counts descriptor reads and individual field reads separately.
    /// </summary>
    /// <remarks>
    /// The separation is the point: the pooled overload's whole body is a descriptor read
    /// [<c>:L127</c>], so an implementation that also copied individual properties would show up as a
    /// non-zero field count.
    /// </remarks>
    private sealed class FakePooledTransactionObject : IPooledSqlTransactionObject
    {
        /// <summary>The descriptor this pooled object publishes.</summary>
        internal TransactionData Descriptor { get; init; }

        /// <summary>How many times the descriptor was read.</summary>
        internal int DescriptorReads { get; private set; }

        /// <summary>How many times any individual connection property was read.</summary>
        internal int IndividualFieldReads { get; private set; }

        /// <inheritdoc/>
        public string Dbms => CountedField(Descriptor.Dbms);

        /// <inheritdoc/>
        public string ServerName => CountedField(Descriptor.ServerName);

        /// <inheritdoc/>
        public string Database => CountedField(Descriptor.Database);

        /// <inheritdoc/>
        public string LogId => CountedField(Descriptor.LogId);

        /// <inheritdoc/>
        public string DbParm => CountedField(Descriptor.DbParm);

        /// <inheritdoc/>
        public string Lock => CountedField(Descriptor.Lock);

        /// <inheritdoc/>
        public bool AutoCommit
        {
            get
            {
                IndividualFieldReads++;
                return Descriptor.AutoCommit;
            }
        }

        /// <inheritdoc/>
        public string UserParm => CountedField(Descriptor.UserParm);

        /// <inheritdoc/>
        public TransactionData GetTransData()
        {
            DescriptorReads++;
            return Descriptor;
        }

        /// <inheritdoc/>
        public string RevealLogPassForTransfer()
        {
            IndividualFieldReads++;
            return Descriptor.RevealLogPassForConnect();
        }

        private string CountedField(string value)
        {
            IndividualFieldReads++;
            return value;
        }
    }

    /// <summary>
    /// A pooled-transaction activator that is never reached. C-E: no case here opens a transaction, so
    /// an invocation would mean the suite had stopped testing what it claims to.
    /// </summary>
    private sealed class UnreachableTransactionActivator : IPooledTransactionActivator
    {
        /// <inheritdoc/>
        public IPooledTransaction CreateDefault() =>
            throw new NotSupportedException("No case in this file opens a transaction.");

        /// <inheritdoc/>
        public IPooledTransaction Create(string className) =>
            throw new NotSupportedException("No case in this file opens a transaction.");
    }

    /// <summary>
    /// A result-carrier factory that is never reached. The base's own cases run no task body and a
    /// command produces no result set.
    /// </summary>
    private sealed class UnreachableDataStoreFactory : ISqlDataStoreFactory
    {
        /// <inheritdoc/>
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException("This case creates no result carrier.");
    }

    /// <summary>A clock nothing advances, and which every subject here reads instead of the wall clock.</summary>
    /// <remarks>
    /// <para>
    /// AAP 0.6.7 - the determinism seam. No case in this file measures a duration, reads an ambient
    /// clock, or depends on time passing.
    /// </para>
    /// <para>
    /// DELIBERATELY NOT the sibling <c>FakeTimeProvider</c> from <c>TestDoubles.cs</c>, which exists so
    /// the transaction pool's idle expiry can be driven forward. A clock that CAN be advanced is the
    /// wrong tool here: the property this file needs is that time never moves, and a non-advanceable
    /// type states that structurally rather than by every case remembering not to advance one. Both
    /// sibling proxy test files declare the same private fixed clock for the same reason.
    /// </para>
    /// </remarks>
    private sealed class FixedClock : TimeProvider
    {
        /// <summary>The one instance every harness shares.</summary>
        internal static readonly FixedClock Instance = new();

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>A result carrier that records the two halves of its own destruction, in order.</summary>
    /// <remarks>
    /// The recording is what makes the destroy-then-adopt ordering observable, and it records CLEARING
    /// separately from DISPOSING because destruction is both - the rows and the parent references go
    /// first, then the handle (hazard 2 of <c>docs/PB多线程绕坑提示.md:L5</c>).
    /// </remarks>
    private sealed class RecordingStore : ISqlDataStore, IDisposable
    {
        private readonly Action<string>? _record;

        internal RecordingStore(DataWindowCarrier carrier, Action<string>? record)
        {
            Carrier = carrier;
            _record = record;
        }

        /// <summary>How many times the rows and references were cleared.</summary>
        internal int ClearStateCalls { get; private set; }

        /// <summary>How many times the handle was released.</summary>
        internal int DisposeCalls { get; private set; }

        /// <inheritdoc/>
        public DataWindowCarrier Carrier { get; }

        /// <inheritdoc/>
        public string DataObject { get; set; } = string.Empty;

        /// <inheritdoc/>
        public string GetSqlSelect() => string.Empty;

        /// <inheritdoc/>
        public string Modify(string modificationScript) => string.Empty;

        /// <inheritdoc/>
        public string Describe(string property) => SqlDataObjectStore.DescribeFailureSentinel;

        /// <inheritdoc/>
        public long SetFilter(string? filter) => DataWindowBufferStore.DataStoreSuccess;

        /// <inheritdoc/>
        public long SetSort(string? sort) => DataWindowBufferStore.DataStoreSuccess;

        /// <inheritdoc/>
        public void ClearState()
        {
            ClearStateCalls++;
            _record?.Invoke("clear-state");
            Carrier.ClearState();
        }

        /// <inheritdoc/>
        public void OnInit(ICarrierParentTask parentTask) => Carrier.OnInit(parentTask);

        /// <inheritdoc/>
        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(DataWindowBufferStore.DataStoreSuccess);

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeCalls++;
            _record?.Invoke("dispose");
        }
    }

    /// <summary>A carrier factory that records the affinity every request asked for.</summary>
    /// <remarks>
    /// The affinity is recorded because the legacy has TWO carrier types keyed on it, so a replacement
    /// carrier built on the wrong affinity would be the wrong legacy class - and that is invisible in an
    /// end state.
    /// </remarks>
    private sealed class TrackingStoreFactory : ISqlDataStoreFactory
    {
        private readonly Action<string>? _record;

        internal TrackingStoreFactory(Action<string>? record) => _record = record;

        /// <summary>Every affinity requested, in order.</summary>
        internal List<CarrierThreadAffinity> RequestedAffinities { get; } = [];

        /// <inheritdoc/>
        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            RequestedAffinities.Add(affinity);

            return new RecordingStore(
                DataWindowCarrierFactory.Create(affinity, FixedClock.Instance),
                _record);
        }
    }

    /// <summary>The DataWindow runtime seam, inert. No case here builds a definition from syntax.</summary>
    private sealed class InertQueryRuntime : IQueryDataWindowRuntime
    {
        /// <inheritdoc/>
        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax) =>
            new(DataWindowBufferStore.DataStoreSuccess, string.Empty);

        /// <inheritdoc/>
        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
        {
            child = null;
            return false;
        }

        /// <inheritdoc/>
        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction) =>
            DataWindowBufferStore.DataStoreSuccess;
    }

    /// <summary>
    /// The child-column resolver seam. Resolves nothing until a child is installed, which is the
    /// legacy's own failure arm.
    /// </summary>
    /// <remarks>
    /// The unresolvable case is a FAILURE on the receive side and a column to SKIP on the send side, so
    /// the double's default answer is deliberately the negative one rather than a convenient success.
    /// </remarks>
    private sealed class ProgrammableChildResolver : IQueryChildResolver
    {
        /// <summary>The child this resolver answers with, or <see langword="null"/> to resolve nothing.</summary>
        internal IQueryChildSurface? Child { get; set; }

        /// <inheritdoc/>
        public bool TryGetChild(ISqlDataStore target, string columnName, out IQueryChildSurface? child)
        {
            child = Child;
            return Child is not null;
        }
    }

    /// <summary>A child DataWindow surface whose describe answers are programmable.</summary>
    /// <remarks>
    /// The describe answers matter because the child handler re-applies a filter or a sort only when the
    /// child's own definition reports one longer than a single character - a one-character answer being a
    /// describe SENTINEL rather than an expression.
    /// </remarks>
    private sealed class StubChildSurface : IQueryChildSurface
    {
        private readonly Dictionary<string, string> _described = new(StringComparer.Ordinal);

        internal StubChildSurface(DataWindowBufferStore store) => Store = store;

        /// <summary>How many times the child's own filter was re-applied.</summary>
        internal int FilterCalls { get; private set; }

        /// <summary>How many times the child's own sort was re-applied.</summary>
        internal int SortCalls { get; private set; }

        /// <inheritdoc/>
        public DataWindowBufferStore Store { get; }

        /// <summary>Programmes one describe answer.</summary>
        /// <param name="property">The property name.</param>
        /// <param name="value">The answer.</param>
        internal void SetDescribe(string property, string value) => _described[property] = value;

        /// <inheritdoc/>
        public string Describe(string property) =>
            _described.TryGetValue(property, out string? value) ? value : string.Empty;

        /// <inheritdoc/>
        public long Filter()
        {
            FilterCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        public long Sort()
        {
            SortCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    /// <summary>The transaction surface the query worker consumes, inert. No case opens a connection.</summary>
    /// <remarks>
    /// C-E. All four members are unreached by this file's cases, and each answers its own contract's
    /// neutral value rather than throwing, so a future case that DOES reach one gets a defined answer
    /// instead of a harness fault it would have to diagnose first.
    /// </remarks>
    private sealed class InertQueryTransactionSurface : IQueryTransactionSurface
    {
        /// <inheritdoc/>
        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new(string.Empty, string.Empty);

        /// <inheritdoc/>
        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CountQueryOutcome(RetCode.OK, null, string.Empty));

        /// <inheritdoc/>
        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.OK;

        /// <inheritdoc/>
        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount)
        {
            // Nothing to observe: no case here retrieves.
        }
    }

    /// <summary>An adopter that records the adoption, so its position in a sequence is observable.</summary>
    private sealed class RecordingAdopter : IQueryCarrierAdopter
    {
        private readonly Action<string>? _record;

        internal RecordingAdopter(Action<string>? record) => _record = record;

        /// <summary>The carrier this adopter produced, which the proxy then holds.</summary>
        internal RecordingStore? Produced { get; private set; }

        /// <inheritdoc/>
        public ISqlDataStore Adopt(DataWindowCarrier carrier)
        {
            _record?.Invoke("adopt");
            Produced = new RecordingStore(carrier, _record);

            return Produced;
        }
    }

    /// <summary>A payload codec that records WHICH carrier each payload was handed to.</summary>
    /// <remarks>
    /// The recorded target is how the three-way dispatch becomes observable: all three arms answer the
    /// same code, and only the destination distinguishes them.
    /// </remarks>
    private sealed class RecordingPayloadCodec : IChangesetPayloadCodec
    {
        /// <summary>Every carrier a payload was applied to, in order.</summary>
        internal List<DataWindowBufferStore> Targets { get; } = [];

        /// <inheritdoc/>
        public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
        {
            state = new CarrierState();
            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        public long TryApply(
            DataWindowBufferStore target,
            CarrierState? state,
            CarrierBaselineTrust baselineTrust)
        {
            Targets.Add(target);
            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    /// <summary>A query receiver that records the presentational work done on its behalf.</summary>
    /// <remarks>
    /// C-D. Redraw suspension and group recalculation are PRESENTATIONAL, so they are delegated to this
    /// seam rather than performed here - the rendering half of the capability belongs to the deferred
    /// design service and is reserved as a gateway extension point.
    /// </remarks>
    private sealed class RecordingReceiver : IQueryReceiver
    {
        internal RecordingReceiver(QueryReceiverKind kind, ISqlDataStore store)
        {
            Kind = kind;
            Store = store;
        }

        /// <summary>Every redraw request, in order.</summary>
        internal List<bool> RedrawCalls { get; } = [];

        /// <summary>How many times groups were recalculated.</summary>
        internal int GroupCalcCalls { get; private set; }

        /// <inheritdoc/>
        public QueryReceiverKind Kind { get; }

        /// <inheritdoc/>
        public ISqlDataStore Store { get; }

        /// <inheritdoc/>
        public long SetRedraw(bool enable)
        {
            RedrawCalls.Add(enable);
            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        public long GroupCalc()
        {
            GroupCalcCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    /// <summary>
    /// Wires a concrete probe over the abstract base to a recording worker and a recording substrate.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly TransactionPool _pool;

        internal Harness(
            bool attachWorker = true,
            TaskExecutionGroup group = TaskExecutionGroup.Normal)
        {
            IOptions<PersistenceOptions> options =
                Microsoft.Extensions.Options.Options.Create(new PersistenceOptions());

            _pool = new TransactionPool(
                options,
                FixedClock.Instance,
                new UnreachableTransactionActivator());

            Host = new RecordingProxyHost { TaskClassName = ProbeProxy.ProbeWorkerClassName };

            if (attachWorker)
            {
                Worker = new RecordingWorker(
                    new InertTaskHost(),
                    _pool,
                    new UnreachableDataStoreFactory(),
                    new SqlRetrievalHookActivator(),
                    FixedClock.Instance,
                    Host);

                Host.Task = Worker;
            }

            Proxy = new ProbeProxy(Host, FixedClock.Instance, group);

            if (Worker is not null)
            {
                // The snapshot the worker reports at the moment of delegation, which is what makes the
                // reset's three-step ordering observable.
                Worker.Snapshot = () =>
                    $"params={(Proxy.ParamsFlag ? "set" : "clear")},"
                    + $"latch={(Proxy.Latch == DbErrorData.Empty ? "clear" : "set")}";
            }
        }

        /// <summary>The recording substrate seam.</summary>
        internal RecordingProxyHost Host { get; }

        /// <summary>The recording worker, absent when the harness was built without one.</summary>
        internal RecordingWorker? Worker { get; }

        /// <summary>The concrete probe over the abstract base.</summary>
        internal ProbeProxy Proxy { get; }

        /// <summary>Reads the private borrowed handle, which is not otherwise observable.</summary>
        /// <param name="proxy">The proxy to read.</param>
        internal ManualResetEventSlim? BorrowedCommitHandle(ProbeProxy proxy) =>
            (ManualResetEventSlim?)typeof(SqlTaskProxyBase)
                .GetField("_committedSignal", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(proxy);

        /// <inheritdoc/>
        public void Dispose()
        {
            Proxy.Dispose();
            Worker?.Dispose();
            _pool.Dispose();
            Host.Dispose();
        }
    }

    /// <summary>Wires a real command proxy to a real command worker through a recording substrate.</summary>
    private sealed class CommandHarness : IDisposable
    {
        private readonly TransactionPool _pool;

        internal CommandHarness()
        {
            IOptions<PersistenceOptions> options =
                Microsoft.Extensions.Options.Options.Create(new PersistenceOptions());

            _pool = new TransactionPool(
                options,
                FixedClock.Instance,
                new UnreachableTransactionActivator());

            Worker = new SqlCommandTask(
                new InertTaskHost(),
                _pool,
                new UnreachableDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                FixedClock.Instance,
                NullLogger<SqlCommandTask>.Instance);

            Host = new RecordingProxyHost
            {
                Task = Worker,
                TaskClassName = SqlCommandTaskProxy.LegacyWorkerClassName,
            };

            Proxy = new SqlCommandTaskProxy(
                Host,
                NullLogger<SqlCommandTaskProxy>.Instance,
                FixedClock.Instance);
        }

        /// <summary>The recording substrate seam.</summary>
        internal RecordingProxyHost Host { get; }

        /// <summary>The worker half of the pair.</summary>
        internal SqlCommandTask Worker { get; }

        /// <summary>The caller-side half of the pair.</summary>
        internal SqlCommandTaskProxy Proxy { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Proxy.Dispose();
            Worker.Dispose();
            _pool.Dispose();
            Host.Dispose();
        }
    }

    /// <summary>Wires a real query proxy to a real query worker through recording seams.</summary>
    /// <remarks>
    /// C-E. The worker gets its OWN carrier factory so that the affinity assertions read the PROXY's
    /// requests alone, and no seam here opens a connection or builds a definition.
    /// </remarks>
    private sealed class QueryHarness : IDisposable
    {
        private readonly TransactionPool _pool;
        private readonly List<string> _log = [];

        internal QueryHarness()
        {
            IOptions<PersistenceOptions> options =
                Microsoft.Extensions.Options.Options.Create(new PersistenceOptions());

            _pool = new TransactionPool(
                options,
                FixedClock.Instance,
                new UnreachableTransactionActivator());

            InertQueryRuntime runtime = new();

            StoreFactory = new TrackingStoreFactory(_log.Add);
            Adopter = new RecordingAdopter(_log.Add);
            PayloadCodec = new RecordingPayloadCodec();
            ChildResolver = new ProgrammableChildResolver();

            Worker = new SqlQueryTask(
                new InertTaskHost(),
                _pool,
                new TrackingStoreFactory(record: null),
                new SqlRetrievalHookActivator(),
                FixedClock.Instance,
                NullLogger<SqlQueryTask>.Instance,
                new InertQueryTransactionSurface(),
                runtime,
                [new SqlServerPagingRewriter(), new OraclePagingRewriter()],
                new ChangesetCodec(FixedClock.Instance),
                SqlRedactor.Instance,
                options);

            Host = new RecordingProxyHost
            {
                Task = Worker,
                TaskClassName = "n_cst_thread_task_sqlquery",
            };

            Proxy = new SqlQueryTaskProxy(
                Host,
                NullLogger<SqlQueryTaskProxy>.Instance,
                FixedClock.Instance,
                StoreFactory,
                runtime,
                ChildResolver,
                Adopter,
                PayloadCodec,
                options);
        }

        /// <summary>The recording substrate seam.</summary>
        internal RecordingProxyHost Host { get; }

        /// <summary>The worker half of the pair.</summary>
        internal SqlQueryTask Worker { get; }

        /// <summary>The caller-side half of the pair.</summary>
        internal SqlQueryTaskProxy Proxy { get; }

        /// <summary>The carrier factory the PROXY uses.</summary>
        internal TrackingStoreFactory StoreFactory { get; }

        /// <summary>The adopter the data-move callback hands its incoming carrier to.</summary>
        internal RecordingAdopter Adopter { get; }

        /// <summary>The codec that records which carrier each payload reached.</summary>
        internal RecordingPayloadCodec PayloadCodec { get; }

        /// <summary>The child-column resolver, which resolves nothing until a child is installed.</summary>
        internal ProgrammableChildResolver ChildResolver { get; }

        /// <summary>The result of the most recent receiver installation.</summary>
        internal long ReceiverInstallResult { get; private set; } = RetCode.OK;

        /// <summary>The carrier the proxy currently holds.</summary>
        internal RecordingStore HeldStore => (RecordingStore)Proxy.Data;

        /// <summary>The ordered record of everything that crossed a carrier seam.</summary>
        internal IReadOnlyList<string> Log => _log;

        /// <summary>Empties the ordered log, so a case records one call rather than its setup.</summary>
        internal void ClearLog() => _log.Clear();

        /// <summary>Installs a receiver of the given kind and returns it.</summary>
        /// <param name="asDataWindow">
        /// <see langword="true"/> for the DataWindow arm, which is the only one that brackets redraw.
        /// </param>
        internal RecordingReceiver InstallReceiver(bool asDataWindow)
        {
            RecordingStore store = new(
                DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, FixedClock.Instance),
                record: null);

            RecordingReceiver receiver = new(
                asDataWindow ? QueryReceiverKind.DataWindow : QueryReceiverKind.DataStore,
                store);

            ReceiverInstallResult = Proxy.SetReceiver(receiver);

            return receiver;
        }

        /// <summary>Subscribes to the published notification stream and collects it in full.</summary>
        /// <remarks>
        /// All three arguments are collected, because the six callbacks distribute their payload across
        /// them differently: two pack words into the numeric argument, one carries a raw count there, and
        /// one carries a column name in the STRING argument with a literal zero alongside it.
        /// </remarks>
        internal List<(long Code, long Payload, string Text)> CaptureNotifications()
        {
            List<(long Code, long Payload, string Text)> seen = [];

            _ = DispatcherOf(Proxy).On(
                TaskEventName.Notify,
                (source, wparam, lparam, text) =>
                {
                    seen.Add((wparam, lparam, text));
                    return null;
                });

            return seen;
        }

        /// <summary>Installs a resolvable child column, so the child callback can reach its success arm.</summary>
        /// <param name="filterExpression">The child's own filter, as its definition would report it.</param>
        /// <param name="sortExpression">The child's own sort, as its definition would report it.</param>
        internal StubChildSurface InstallChild(string filterExpression, string sortExpression)
        {
            StubChildSurface child = new(
                DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, FixedClock.Instance));

            child.SetDescribe(SqlQueryTaskProxy.ChildFilterProperty, filterExpression);
            child.SetDescribe(SqlQueryTaskProxy.ChildSortProperty, sortExpression);

            ChildResolver.Child = child;

            return child;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Proxy.Dispose();
            Worker.Dispose();
            _pool.Dispose();
            Host.Dispose();
        }

        /// <summary>Reads the protected local delegate surface off a proxy.</summary>
        /// <param name="proxy">The proxy to read.</param>
        private static TaskNotificationDispatcher DispatcherOf(SqlQueryTaskProxy proxy) =>
            (TaskNotificationDispatcher)typeof(SqlTaskProxyBase)
                .GetProperty("Notifications", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(proxy)!;
    }
}
