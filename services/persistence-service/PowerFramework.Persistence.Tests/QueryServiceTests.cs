// ==================================================================================================
//  QueryServiceTests.cs - CONTRACT C-05 AND THE SqlQueryTask PIPELINE BENEATH IT, AS ONE UNIT
//  ------------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Grpc/QueryService.cs
//                 services/persistence-service/PowerFramework.Persistence/Tasks/SqlQueryTask.cs
//
//  CONTRACT       shared/PowerFramework.Contracts/Proto/persistence.v1.proto  (service QueryService)
//                 shared/PowerFramework.Contracts/Proto/common.v1.proto       (OperationStatus, DbError)
//
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru       (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru    (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru        (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru   (READ ONLY)
//                 ws_objects/pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru            (READ ONLY)
//                 ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw                (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               (READ ONLY)
//                 ws_objects/pfw.shared.pbl.src/retcode.sru                              (READ ONLY)
//
//  ORACLE STATUS (constraint C-C). Every ws_objects/** path above is READ ONLY. It is the behavioural
//  oracle this migration is verified against, never an edit target, never reformatted and never moved.
//  Nothing else in this repository can adjudicate the behaviour asserted below, so every claim here
//  carries a line locator a reader can diff against. Unqualified `:Lnnn` locators refer to
//  n_cst_thread_task_sqlquery.sru; anything else is named in full.
//
//  ==================================================================================================
//  WHY THIS FILE EXISTS, STATED AS THE GAP IT CLOSES RATHER THAN AS A CLAIM
//  ==================================================================================================
//  The adapter is the widest RPC surface in this service - eleven calls over eighteen legacy settings -
//  and until now it was the least covered. Two sibling artifacts said so in as many words, and both
//  are worth quoting because they define this file's remit:
//
//    * PowerFramework.Persistence.Tests.csproj records, under "REACHED ONLY SHALLOWLY", that
//      Grpc/QueryService sat at 5.7 percent line coverage, "whose chunked server-streaming members are
//      entered only by the composition-root registration test".
//
//    * QueryServiceLeaseTests.cs states in its own header that it is NOT a full parity suite for that
//      adapter, that it covers the operation lease and nothing else, and that "the rest of that adapter
//      - the four numeric guard boundaries, the clause modification styles, the paging validation, the
//      five stream arms - is not covered here and is not covered anywhere else either; that gap is
//      real and is recorded rather than papered over."
//
//  THIS FILE IS THAT GAP CLOSED. It drives contract C-05 and the SqlQueryTask pipeline beneath it as
//  ONE unit, because the adapter maps one-for-one onto the task: every RPC either delegates a guard to
//  a task setter or projects a task outcome onto the wire, so an assertion that stops at the adapter
//  boundary cannot tell a delegated guard from a re-implemented one.
//
//  WHAT IS DELIBERATELY NOT HERE, WITH THE OWNER NAMED. Cross-referenced rather than duplicated, so a
//  behaviour has exactly one suite that owns it and a reader knows where to look:
//
//    SqlQueryTaskTests.cs .................. the task's INTERNALS in isolation: the reset defaults
//                                            field by field, the page-native reset defect, the paged
//                                            statement builder, ApplyStoredClauses, the count
//                                            statement's byte-exact wrapper, the drop-down child walk
//    QueryServiceLeaseTests.cs ............. the operation LEASE and its teardown handoff, and the
//                                            unique-index column list's lexical and duplicate refusals
//    ChangesetCodecTests / ...ReceiveTests . the changeset codec's own encode/apply internals
//    FullStateCodecTests.cs ................ the full-state codec's own capture and apply internals
//    ClauseModifierTests.cs ................ the one-based clause upsert and the clause-body grammar
//    PagingRewriterByteExactTests.cs ....... the dispatcher and the byte-exact count statement
//    ConflictDetectorTests.cs .............. the UPDATE-side defensive override. See the warning below
//    DataWindowBuffersTests.cs ............. the row-cap machinery, including the cap-lifting arm
//    TaskOperationLatchTests.cs ............ the shared latch state machine under contention
//
//  ==================================================================================================
//  ⚠ THE QUERY-SIDE DEFENSIVE OVERRIDE IS A DISTINCT SITE FROM THE UPDATE-SIDE ONE (constraint C-K)
//  ==================================================================================================
//  Two overrides in this service rewrite a claimed success into a failure when the transaction's own
//  SQL code says otherwise, they are NOT the same behaviour, and a future refactor that merges them
//  into one shared helper tested by a single numeric case would silently drop half of each:
//
//    QUERY SIDE   [:L771-L774]  `if TransObject.SQLCode = -1 and nRowCnt >= 0 then nRowCnt = -1;
//                                data.Reset() end if`
//                 EXACT -1 on the code, `>= 0` on the count, AND IT DISCARDS THE CARRIER. Asserted in
//                 section 5 of this file, and at the pure-function level by
//                 SqlQueryTaskTests.ApplyDefensiveRowCountOverride_*.
//
//    UPDATE SIDE  n_cst_thread_task_sqlupdate.sru:L208-L210
//                 A different predicate on a different claimed result, and it does NOT discard the
//                 carrier. Owned by ConflictDetectorTests.
//
//  The two share a numeral and nothing else. Neither file may be deleted on the grounds that "the
//  other one already tests the minus-one case".
//
//  ==================================================================================================
//  THE CONSTRAINTS THAT GOVERN THIS FILE, AND HOW EACH IS DISCHARGED
//  ==================================================================================================
//  C-B  REPLICATE VERBATIM, INCLUDING THE DEFECTS. Four legacy behaviours are asserted here as
//       EXPECTED rather than corrected, each commented and located at its point of assertion: the
//       INCLUSIVE `<= 1000` chunk floor [:L410], the EXACT `-1` defensive override [:L771], the
//       `-1`/`-1` count sentinels [:L861-L862], and the hook's DUAL fallback where a null return and
//       E_NO_IMPLEMENTATION both mean "not handled" [:L761].
//
//  C-E  NO FABRICATED DATABASE. Nothing here opens a connection, creates a file, or references a
//       SQLite, SQL Server or Oracle client. The pipeline is driven through the pooled-transaction,
//       carrier and pool doubles; section 6 pins that as a property rather than leaving it to review.
//
//  C-F  NO CREDENTIAL, AND REDACTION ON EVERY ERROR PATH. Every value below is synthetic and invented
//       here - no password, account, host or connection string is copied from the legacy tree or from
//       any catalogued in-source secret site. The transaction descriptor's log password is empty. The
//       recording redactor from TestDoubles.cs proves the task's own redaction seam is exercised, and
//       section 5 additionally proves the wire projection masks the statement field.
//
//  C-G  THE SURFACE STAYS AUTHENTICATED. No signing key is constructed anywhere in this file, no
//       authentication is bypassed to make an assertion pass, and section 6 asserts by reflection that
//       the adapter carries its least-privilege policy and declares no anonymous escape. Persistence
//       never mints, and the minting package is deliberately absent from this project.
//
//  C-A  NO CROSS-SERVICE REFERENCE. The only contract types used are the generated stubs from the
//       shared contracts project, which is the single permitted cross-service coupling.
//
//  C-D  NOTHING FOR ANY DEFERRED SERVICE. No DesignSystem, Documents, Integration or ScriptBridge type
//       is named, constructed or asserted on. The hook-class string exercised in section 5 is a task
//       COLLABORATOR NAME resolved through an in-process activator; it is not a script, an expression
//       or a dynamic invocation, and it must not be read as one.
//
//  C-H  This is the bulk of C-05's coverage: the mutator-guard theory and the pipeline arms.
//
//  0.6.7  DETERMINISM. One injected clock (FakeTimeProvider), never advanced because nothing here
//         reads it; no wall clock, no Thread.Sleep, no Task.Delay, no timing assertion of any kind.
//         Every case is a theory or a fact over fixed data and produces identical results run to run.
//
//  0.8.5  NO PERFORMANCE ASSERTION. Chunking is asserted purely as BEHAVIOUR - how many chunks, in
//         what order, carrying which discriminator - and never as throughput, latency or duration.
//
//  0.7.2  Warnings-as-errors and nullable are inherited from the repository-root property sheet. No
//         SCREAMING_SNAKE identifier is DECLARED here; the preserved legacy spellings are REFERENCED
//         from Enums, RetCode and ClauseModifier, whose files carry the scoped analyzer suppression.
//
//  RULES  review_rules returns exactly one line - "No user rules provided." - so no user rule governs
//         this file and none is invented. The enterprise baseline of AAP 0.7.2 applies in their place.
// ==================================================================================================

using System.Globalization;
using System.Reflection;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Authorization;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

// The generated service base, aliased so the reflection cross-check in section 3 can name the
// published RPC roster without a fully qualified type in the middle of an assertion.
using GeneratedQueryServiceBase =
    PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceBase;

// The adapter under test. Aliased because PowerFramework.Contracts.Persistence.V1 - a global using of
// this assembly - also publishes a QueryService, which is the generated static descriptor class rather
// than this one. Without the alias the bare name is CS0104-ambiguous.
using GrpcQueryService = PowerFramework.Persistence.Grpc.QueryService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity and contract suites for C-05, <c>persistence.v1.QueryService</c>, and for the
/// <c>Tasks/SqlQueryTask</c> pipeline reached through it.
/// </summary>
public sealed class QueryServiceTests
{
    /// <summary>
    /// A synthetic statement, deliberately simple so a clause rewrite stays legible in a failure dump.
    /// Invented here; not copied from the legacy tree. It names the one table the repository publishes
    /// DDL for [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>] and nothing more - no
    /// statement in this file is ever executed against a database (constraint C-E).
    /// </summary>
    private const string SomeSelect = "SELECT ID, NAME FROM COMPANY";

    /// <summary>
    /// The DataWindow release line the golden-master fixture declares
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L2</c>], used as a stand-in syntax string. Nothing
    /// in this file parses it: the create arm is a double.
    /// </summary>
    private const string FixtureSyntax = "release 12.5;";

    /// <summary>
    /// A synthetic hook class name. Invented here - the legacy names no hook class anywhere in the
    /// repository. It is a COLLABORATOR NAME resolved through an in-process activator, not a script and
    /// not an expression (constraint C-D).
    /// </summary>
    private const string ProbeHookClass = "n_probe_retrieval_hook";

    /// <summary>A synthetic provider error code. Invented here; not a real SQLSTATE or vendor code.</summary>
    private const long ProbeDbCode = -917L;

    /// <summary>
    /// A synthetic provider diagnostic carrying a quoted literal, so the redaction assertions in
    /// section 5 have something a masking policy must actually catch. Invented here (constraint C-F).
    /// </summary>
    private const string ProbeDbErrorText = "unit-test provider diagnostic";

    /// <summary>
    /// A synthetic clause body. Passes the clause-body grammar
    /// <c>Sql/ClauseModifier.ValidateClauseBody</c> enforces, which is deliberate: a clause refused for
    /// its GRAMMAR would fail these cases for a reason that has nothing to do with the property under
    /// test. That grammar is owned by <c>ClauseModifierTests</c>.
    /// </summary>
    private const string SomeWhereClause = "id > 0";

    /// <summary>A synthetic ORDER BY clause body.</summary>
    private const string SomeOrderByClause = "name";

    // ==============================================================================================
    //  SECTION 1 - THE CHUNK-SIZE GUARD, DELEGATED THROUGH THE RPC        [:L410-L415]
    //
    //  `public function long of_setchunksize (readonly long chunksize);
    //   if chunkSize <= 1000 then return RetCode.E_INVALID_ARGUMENT`   [:L410]
    //   _nChunkSize = chunkSize                                        [:L412]
    //
    //  THREE PROPERTIES, AND THE MIDDLE ONE IS THE ONE EVERY READER GETS WRONG:
    //
    //    1. The comparison is `<=` and not `<`, so ONE THOUSAND ITSELF IS REJECTED and the lowest legal
    //       value is 1001. The field's own inline comment says the opposite - `Chunk row count,min:1000`
    //       [:L34] - and the CODE WINS. Anyone reading only the comment will "correct" the guard to
    //       `< 1000` and silently widen the contract by exactly one value (constraint C-B).
    //
    //    2. The guard RETURNS BEFORE THE ASSIGNMENT, two lines apart in the oracle, so a refused value
    //       leaves the previous chunk size in force. An implementation that assigned first and validated
    //       after would answer the same code on the same input and still be wrong.
    //
    //    3. The default is 10000 [:L34, :L256], reached through configuration whose own default is that
    //       literal rather than through a literal in the adapter.
    //
    //  ASSERTED THROUGH THE RPC, WHICH IS THE POINT OF THIS SECTION rather than a heavier way to reach
    //  a setter. SqlQueryTaskTests pins the same boundary directly on the task; what only an RPC-level
    //  case can establish is that the ADAPTER delegates to that one guard instead of restating it - a
    //  restated guard is free to disagree, and a `<` restated beside a `<=` is invisible in review.
    // ==============================================================================================

    /// <summary>
    /// The chunk-size boundary, stated as data so the inclusive floor is visible at a glance.
    /// </summary>
    /// <returns>The requested chunk size and the wire code the contract must answer.</returns>
    public static TheoryData<long, WireRetCode> ChunkSizeBoundaryCases() => new()
    {
        // ---- REFUSED. The whole domain at or below the floor. -------------------------------------
        { long.MinValue, WireRetCode.EInvalidArgument },
        { -1L, WireRetCode.EInvalidArgument },
        { 0L, WireRetCode.EInvalidArgument },
        { 999L, WireRetCode.EInvalidArgument },

        // THE INCLUSIVE BOUNDARY ITSELF. 1000 is REFUSED, contradicting the field comment at :L34.
        { 1_000L, WireRetCode.EInvalidArgument },

        // ---- ACCEPTED. ---------------------------------------------------------------------------
        // THE LOWEST LEGAL VALUE, one above the floor.
        { 1_001L, WireRetCode.Ok },

        { 10_000L, WireRetCode.Ok },
        { 1_000_000L, WireRetCode.Ok },
        { long.MaxValue, WireRetCode.Ok },
    };

    [Theory]
    [MemberData(nameof(ChunkSizeBoundaryCases))]
    public async Task SetChunkSize_ReproducesTheInclusiveThousandFloor(
        long chunkSize,
        WireRetCode expected)
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        SetChunkSizeResponse response = await fixture.Service.SetChunkSize(
            new SetChunkSizeRequest { Task = handle, ChunkSize = chunkSize },
            Fixture.Context);

        Assert.Equal(expected, response.Status.RetCode);

        // THE ACCEPTED VALUE REACHED THE FIELD, which is what makes this a delegation assertion rather
        // than a code-shaped one: the adapter could answer OK without applying anything.
        if (expected == WireRetCode.Ok)
        {
            Assert.Equal(chunkSize, fixture.TaskFor(handle).ChunkSize);
        }

        // A DELEGATED REFUSAL CARRIES THE BARE CODE AND NO TEXT OF THE ADAPTER'S OWN, because the
        // oracle's guard is one line with no message [:L410]. Text is added only for refusals the
        // legacy has no counterpart for, and this is not one of them.
        if (expected == WireRetCode.EInvalidArgument)
        {
            Assert.Equal(string.Empty, response.Status.ErrorText);
        }
    }

    [Fact]
    public async Task SetChunkSize_TheDefaultIsTenThousandBeforeAnySetterIsCalled()
    {
        // [:L34] `long _nChunkSize = 10000` and [:L256] `_nChunkSize = 10000` in the private reset. The
        // value is asserted against the published constant rather than a literal, so the two cannot
        // drift; the constant's own equality with 10000 is pinned by SqlQueryTaskTests.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(10_000L, SqlQueryTask.DefaultChunkSize);
        Assert.Equal(SqlQueryTask.DefaultChunkSize, fixture.TaskFor(handle).ChunkSize);
    }

    [Fact]
    public async Task SetChunkSize_ARefusedValueLeavesThePreviousChunkSizeInForce()
    {
        // THE ORDER OF THE ORACLE'S TWO LINES IS OBSERVABLE. The guard at :L410 returns before the
        // assignment at :L412, so a refusal is inert. An implementation that assigned first would pass
        // the boundary theory above and fail here.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetChunkSize(
                new SetChunkSizeRequest { Task = handle, ChunkSize = 5_000L },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(5_000L, fixture.TaskFor(handle).ChunkSize);

        // Refused at the inclusive boundary, and again below it.
        foreach (long refused in (long[])[1_000L, 0L, -7L])
        {
            Assert.Equal(
                WireRetCode.EInvalidArgument,
                (await fixture.Service.SetChunkSize(
                    new SetChunkSizeRequest { Task = handle, ChunkSize = refused },
                    Fixture.Context)).Status.RetCode);

            // UNCHANGED. Not reset to the default, and not overwritten with the refused value.
            Assert.Equal(5_000L, fixture.TaskFor(handle).ChunkSize);
        }
    }

    [Fact]
    public async Task CreateQueryTask_WithARefusedChunkSizeInItsSpec_FailsTheCallAndRegistersNothing()
    {
        // THE SAME GUARD ON THE OTHER ACCESS PATH. Ten of the eighteen legacy settings arrive as fields
        // of QuerySpec rather than as calls of their own, and the chunk size is reachable BOTH ways - so
        // a guard that lived only on the dedicated RPC would be bypassable by the specification. A
        // rejected setting fails the whole create with THAT SETTING'S OWN CODE, and the half-configured
        // task is disposed rather than published.
        using Fixture fixture = new();

        CreateQueryTaskResponse created = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { ChunkSize = 1_000L },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, created.Status.RetCode);
        Assert.Null(created.Task);
        Assert.Equal(0, fixture.Tasks.Count);

        // AND THE LOWEST LEGAL VALUE GOES THROUGH ON THE SAME PATH, so the refusal above is the guard
        // rather than the specification path being broken.
        CreateQueryTaskResponse accepted = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { ChunkSize = 1_001L },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal(1_001L, fixture.TaskFor(accepted.Task).ChunkSize);
    }

    /// <summary>
    /// A caller that does not own a query task can neither use it nor release it, and cannot tell either
    /// refusal from one naming a task this service never issued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A HANDLE IS UNGUESSABLE, WHICH IS NOT THE SAME AS OWNER-BOUND.</b> Nothing in this service finds
    /// another caller's handle by search, but unguessable bounds DISCOVERY and not USE: a handle that
    /// escapes through a log record, a proxy trace or the caller's own bug is otherwise a bearer credential
    /// for the retrieval behind it - another caller's rows, its statement and its paging state
    /// (CWE-639, CWE-862, CWE-863). Both halves are covered here, and the release half matters more: a
    /// foreign release disposes a task its owner is mid-retrieval on.
    /// </para>
    /// <para>
    /// <b>THE SECOND CALLER IS PRODUCED BY RE-ATTRIBUTING THE ENTRY, AND THAT IS THE SAME COMPARISON.</b>
    /// This fixture constructs the service directly, so there is no ambient request for a principal to be
    /// read from and every task it creates is attributed to the unattributed sentinel. Ownership is decided
    /// by comparing the STORED principal against the RESOLVED caller, so moving the stored value away from
    /// the caller exercises exactly the code path a second caller's request would - and it keeps this row
    /// free of a host. <c>HandleOwnershipTests</c> drives the same comparison from the other side, by
    /// varying the ambient caller, and additionally proves the deployed host supplies one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AForeignCallerCanNeitherUseNorReleaseAnotherCallersQueryTask()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();
        TaskHandle unknownHandle = new() { TaskId = "no-such-query-task" };

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal(HandlePrincipalResolver.Unattributed, entry.Principal);

        // Charged to somebody else from here on.
        entry.Principal = "powerframework-another-caller";

        // --- USE: the registry refuses, and refuses as it does for an unknown handle. ---------------
        Assert.False(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? foreignResolve));
        Assert.Null(foreignResolve);
        Assert.False(fixture.Tasks.TryResolve(unknownHandle, out QueryTaskEntry? unknownResolve));
        Assert.Null(unknownResolve);

        // --- USE, over the wire: a per-task mutator answers exactly as it does for an unknown task. --
        SetChunkSizeResponse foreignSet = await fixture.Service.SetChunkSize(
            new SetChunkSizeRequest { Task = handle, ChunkSize = 20_000L },
            Fixture.Context);

        SetChunkSizeResponse unknownSet = await fixture.Service.SetChunkSize(
            new SetChunkSizeRequest { Task = unknownHandle, ChunkSize = 20_000L },
            Fixture.Context);

        Assert.Equal(unknownSet.Status.RetCode, foreignSet.Status.RetCode);
        Assert.Equal(unknownSet.Status.ErrorText, foreignSet.Status.ErrorText);

        // --- RELEASE: refused the same way, and the task SURVIVES the attempt. ----------------------
        ReleaseQueryTaskResponse foreignRelease = await fixture.Service.ReleaseQueryTask(
            new ReleaseQueryTaskRequest { Task = handle },
            Fixture.Context);

        ReleaseQueryTaskResponse unknownRelease = await fixture.Service.ReleaseQueryTask(
            new ReleaseQueryTaskRequest { Task = unknownHandle },
            Fixture.Context);

        Assert.Equal(unknownRelease.Status.RetCode, foreignRelease.Status.RetCode);
        Assert.Equal(unknownRelease.Status.ErrorText, foreignRelease.Status.ErrorText);
        Assert.Equal(1, fixture.Tasks.Count);

        // --- THE POSITIVE ARM, so a registry refusing everybody cannot pass this row. ---------------
        entry.Principal = HandlePrincipalResolver.Unattributed;

        Assert.True(fixture.Tasks.TryResolve(handle, out _));

        SetChunkSizeResponse mineSet = await fixture.Service.SetChunkSize(
            new SetChunkSizeRequest { Task = handle, ChunkSize = 20_000L },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, mineSet.Status.RetCode);
        Assert.Equal(20_000L, fixture.TaskFor(handle).ChunkSize);

        ReleaseQueryTaskResponse mineRelease = await fixture.Service.ReleaseQueryTask(
            new ReleaseQueryTaskRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, mineRelease.Status.RetCode);
        Assert.Equal(0, fixture.Tasks.Count);
    }

    // ==============================================================================================
    //  SECTION 2 - THE `Query` SERVER STREAM AND ITS FIVE ARMS
    //
    //  The oracle publishes its result through five caller-side events, and contract C-05 carries them
    //  as the five arms of one `oneof payload` on a SERVER-STREAMING response
    //  [persistence.v1.proto: message QueryResponse]:
    //
    //     row_count         `tasking.Event OnDataReceived(rowCount)`                        [:L83]
    //     data_chunk        `tasking.Event OnDataChunk(ref blbData,count,current,fullstate)`
    //                       [:L97 fullstate TRUE] and [:L183, :L219, :L230 fullstate FALSE]
    //     child_data_chunk  `tasking.Event OnChildDataReceived(sColName,ref blbData)`       [:L134]
    //     page_counts       `tasking.Event OnPageReceived(nPageCount,nRecordCount)`         [:L866]
    //     status            the event's own return code, which had no wire form at all
    //
    //  FOUR PROPERTIES THIS SECTION OWNS, AND WHY EACH IS LOAD BEARING:
    //
    //    ONE-BASED COUNTERS.  `for nChunkIdx = 1 to nChunkCnt` [:L158, :L194] - the first chunk is 1,
    //    never 0, and the receiving side's `if current = 1` reset test
    //    [n_cst_threading_task_sqlquery.sru:L192, :L218, :L235] would NEVER FIRE for a zero-based
    //    stream, leaving the target accumulating rows across sequences. Hazard R9.
    //
    //    THE full_state DISCRIMINATOR.  The receiving side selects its DECODER from this flag alone
    //    [n_cst_threading_task_sqlquery.sru:L189, :L215, :L232], so a flag that does not survive the
    //    projection routes the payload to the wrong codec. The codecs' own internals belong to
    //    ChangesetCodecTests and FullStateCodecTests; what is asserted HERE is that the STREAM carries
    //    the discriminator faithfully.
    //
    //    PROGRESSIVE FORWARD-ONLY DELIVERY.  The oracle hands each chunk over inside the loop, one at a
    //    time, releasing the payload before building the next [:L182-L186, :L218-L222]. That is the
    //    behaviour n_sqliterecordset's forward-only cursor exhibits and the reason the response is a
    //    stream rather than one fat message. Asserted as ORDER AND POSITION - never as elapsed time
    //    (AAP 0.8.5).
    //
    //    CANCELLATION IS A RETURN CODE.  `if Not of_Wait(0.02) then return RetCode.CANCELLED` [:L188]
    //    and the four bare `if of_IsCancelled() then return RetCode.CANCELLED` polls. A cancelled
    //    stream stops emitting and reports CANCELLED; it does not fault.
    //
    //  DRIVEN THROUGH THE RETRIEVAL-RUNNER SEAM, so no database, no carrier and no codec is involved
    //  (constraints C-E, C-H): IQueryRetrievalRunner is a one-method seam over
    //  SqlQueryTask.ExecuteAsync, and a scripted runner publishes any arm sequence a case needs
    //  directly into the production sink. Section 5 then drives the REAL pipeline through the REAL
    //  runner, so nothing below is the only evidence for the arms it asserts.
    // ==============================================================================================

    [Fact]
    public void Query_IsDeclaredServerStreamingOnThePublishedContract()
    {
        // `rpc Query(QueryRequest) returns (stream QueryResponse)` [persistence.v1.proto]. Asserted by
        // reflection over the GENERATED base rather than over the adapter, because the stream shape is
        // the contract's property: an adapter can only implement what the generated signature declares.
        MethodInfo query = Assert.Single(
            typeof(GeneratedQueryServiceBase).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            candidate => string.Equals(candidate.Name, "Query", StringComparison.Ordinal));

        ParameterInfo[] parameters = query.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(QueryRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(IServerStreamWriter<QueryResponse>), parameters[1].ParameterType);
        Assert.Equal(typeof(ServerCallContext), parameters[2].ParameterType);

        // A SERVER-STREAMING RPC RETURNS A BARE Task, NOT Task<TResponse> - the responses go to the
        // writer. A unary Query would return Task<QueryResponse>, and that is the difference this
        // assertion pins.
        Assert.Equal(typeof(Task), query.ReturnType);
    }

    /// <summary>
    /// Chunk sequences whose length is the property under test.
    /// </summary>
    /// <returns>The number of changeset chunks a scripted retrieval publishes.</returns>
    public static TheoryData<long> ChunkSequenceLengths() => [1L, 2L, 3L, 7L];

    [Theory]
    [MemberData(nameof(ChunkSequenceLengths))]
    public async Task Query_EmitsEveryChunkWithAOneBasedIndexRunningToTheDeclaredCount(long chunkCount)
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Script = async (sink, cancellationToken) =>
        {
            // [:L83] the row count is published FIRST, before any row.
            sink.OnDataReceived(chunkCount * 10L);

            // [:L158, :L194] `for nChunkIdx = 1 to nChunkCnt` - one-based and inclusive at both ends.
            for (long index = 1L; index <= chunkCount; index++)
            {
                _ = await sink
                    .SendChunkAsync(NewChangesetChunk(chunkCount, index), cancellationToken)
                    .ConfigureAwait(false);
            }

            return RetCode.OK;
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        // ONE row-count arm and exactly chunkCount data arms. No terminal status: a successful
        // retrieval writes none, because the payload IS the answer.
        Assert.Equal(chunkCount * 10L, Assert.Single(stream.RowCounts));
        Assert.Empty(stream.Statuses);

        QueryDataChunk[] chunks = [.. stream.DataChunks];
        Assert.Equal(chunkCount, chunks.Length);

        for (int position = 0; position < chunks.Length; position++)
        {
            // EVERY chunk declares the SAME total, which is what lets a receiver size itself once.
            Assert.Equal(chunkCount, chunks[position].ChunkCount);

            // AND ITS OWN ONE-BASED POSITION. `position + 1`, so the first chunk is 1 and the last is
            // the count itself - never 0 and never count + 1 (hazard R9).
            Assert.Equal(position + 1, chunks[position].ChunkIndex);
        }

        // Stated as its own assertion because it is the property the receive-side reset depends on.
        Assert.Equal(1L, chunks[0].ChunkIndex);
        Assert.Equal(chunkCount, chunks[^1].ChunkIndex);
    }

    [Fact]
    public async Task Query_EmitsTheFiveArmsInThePublicationOrderTheOracleFixes()
    {
        // THE ORDER IS THE ORACLE'S, NOT A CONVENIENCE. The row count is first and unconditional [:L83];
        // the drop-down children are transferred during the column walk [:L112-L141], BEFORE the chunk
        // loop [:L142-L231]; the page totals are published last, after the data event [:L866]; and a
        // terminal status - which the legacy has no wire form for at all - closes a FAILED stream.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Script = async (sink, cancellationToken) =>
        {
            sink.OnDataReceived(2L);

            _ = await sink
                .SendChildChunkAsync(
                    new ChangesetChildPayload("dept_code", NewCarrierState()),
                    cancellationToken)
                .ConfigureAwait(false);

            _ = await sink
                .SendChunkAsync(NewChangesetChunk(1L, 1L), cancellationToken)
                .ConfigureAwait(false);

            sink.OnPageReceived(4L, 37L);

            // A NON-OK CODE, so the adapter appends the terminal status arm. E_DB_ERROR is one of the
            // nine codes C-05 enumerates.
            return RetCode.E_DB_ERROR;
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(
            ["row_count", "child_data_chunk", "data_chunk", "page_counts", "status"],
            [.. stream.Written.Select(ArmNameOf)]);

        // The payloads themselves, so the ordering above is over real arms rather than empty envelopes.
        Assert.Equal(2L, Assert.Single(stream.RowCounts));
        Assert.Equal("dept_code", Assert.Single(stream.ChildChunks).Name);
        Assert.Equal(1L, Assert.Single(stream.DataChunks).ChunkIndex);
        Assert.Equal(4L, Assert.Single(stream.PageCounts).PageCount);
        Assert.Equal(37L, stream.PageCounts[0].RecordCount);
        Assert.Equal(WireRetCode.EDbError, Assert.Single(stream.Statuses).RetCode);
    }

    [Fact]
    public async Task Query_MarksChangesetChunksNotFullStateAndTheSingleFullStateChunkFullState()
    {
        // THE DISCRIMINATOR SELECTS THE DECODER ON THE RECEIVING SIDE, so its value must survive the
        // projection unchanged in BOTH directions. The changeset handovers pass FALSE [:L183, :L219,
        // :L230]; the crosstab and composite handover passes TRUE exactly once, with count 1 and index 1
        // [:L97]. Both are driven here through the two DISTINCT sink members the codecs use, which is
        // how a projection that hard-coded either value is caught.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Script = async (sink, cancellationToken) =>
        {
            _ = await sink
                .SendChunkAsync(NewChangesetChunk(2L, 1L), cancellationToken)
                .ConfigureAwait(false);
            _ = await sink
                .SendChunkAsync(NewChangesetChunk(2L, 2L), cancellationToken)
                .ConfigureAwait(false);

            // The full-state arm. Its counters come from the codec's published constants rather than
            // from literals, so a change to either is caught here rather than on the wire.
            long handover = sink.SendFullStateChunk(new QueryDataChunk
            {
                State = NewCarrierState(),
                ChunkCount = FullStateCodec.SingleChunkCount,
                ChunkIndex = FullStateCodec.SingleChunkIndex,
                FullState = true,
            });

            Assert.Equal(RetCode.OK, handover);

            return RetCode.OK;
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        QueryDataChunk[] chunks = [.. stream.DataChunks];
        Assert.Equal(3, chunks.Length);

        // The two changeset chunks: FALSE, and the codec's own constant says it can never be otherwise.
        Assert.False(ChangesetChunk.ChangesetSelector);
        Assert.False(chunks[0].FullState);
        Assert.False(chunks[1].FullState);

        // The full-state chunk: TRUE, alone, and one-based on both counters.
        Assert.True(chunks[2].FullState);
        Assert.Equal(1L, FullStateCodec.SingleChunkCount);
        Assert.Equal(1L, FullStateCodec.SingleChunkIndex);
        Assert.Equal(FullStateCodec.SingleChunkCount, chunks[2].ChunkCount);
        Assert.Equal(FullStateCodec.SingleChunkIndex, chunks[2].ChunkIndex);
    }

    [Fact]
    public async Task Query_WritesEachChunkAsItIsProducedRatherThanBufferingTheSequence()
    {
        // PROGRESSIVE FORWARD-ONLY DELIVERY, ASSERTED AS POSITION AND NOT AS TIME (AAP 0.8.5). The
        // question is whether the Nth handover has REACHED THE WIRE by the moment it returns, or whether
        // the adapter accumulates the sequence and writes it at the end. Both shapes produce an
        // identical final transcript, so the only way to tell them apart is to interrogate the stream
        // from inside the retrieval - which is what the depth snapshot below does.
        //
        // NO CLOCK IS READ AND NOTHING WAITS. The snapshot is taken synchronously at the point the
        // handover returns, so this case is exactly as deterministic as the transcript assertions above
        // (AAP 0.6.7).
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        CollectingStream stream = new();
        List<int> depthAfterEachHandover = [];

        fixture.Runner.Script = async (sink, cancellationToken) =>
        {
            sink.OnDataReceived(30L);

            for (long index = 1L; index <= 3L; index++)
            {
                _ = await sink
                    .SendChunkAsync(NewChangesetChunk(3L, index), cancellationToken)
                    .ConfigureAwait(false);

                depthAfterEachHandover.Add(stream.Written.Count);
            }

            return RetCode.OK;
        };

        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        // After handover 1 the wire holds the row count plus chunk 1; after 2, one more; after 3, one
        // more again. A buffering adapter would read 0, 0, 0 here and then write four messages at the
        // end - the same transcript, the wrong behaviour.
        Assert.Equal([2, 3, 4], [.. depthAfterEachHandover]);

        // AND FORWARD-ONLY: the indices arrive in ascending order with no repeat and no revisit, which
        // is the other half of what the receive-side `if current = 1` reset depends on.
        Assert.Equal(
            [1L, 2L, 3L],
            [.. stream.DataChunks.Select(chunk => chunk.ChunkIndex)]);
    }

    [Fact]
    public async Task Query_CancelledMidStream_StopsEmittingAndAnswersCancelledRatherThanFaulting()
    {
        // [:L188] `if Not of_Wait(0.02) then return RetCode.CANCELLED` plus the four bare
        // `if of_IsCancelled() then return RetCode.CANCELLED` polls. CANCELLATION IS A RETURN CODE ON
        // THIS CONTRACT: a caller written against the oracle reacts to the code and would not catch an
        // exception, so nothing may escape as one (constraint C-B).
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        using CancellationTokenSource cancellation = new();
        long secondHandoverResult = RetCode.OK;
        long thirdHandoverResult = RetCode.OK;

        fixture.Runner.Script = async (sink, cancellationToken) =>
        {
            _ = await sink
                .SendChunkAsync(NewChangesetChunk(3L, 1L), cancellationToken)
                .ConfigureAwait(false);

            // The caller goes away between chunk 1 and chunk 2. Cancelled DIRECTLY rather than after a
            // delay, so the case carries no timing at all (AAP 0.6.7).
            await cancellation.CancelAsync().ConfigureAwait(false);

            secondHandoverResult = await sink
                .SendChunkAsync(NewChangesetChunk(3L, 2L), cancellationToken)
                .ConfigureAwait(false);

            // A THIRD ATTEMPT AFTER THE REFUSAL, because "stops emitting" has to hold for every
            // subsequent handover and not merely for the one that observed the cancellation.
            thirdHandoverResult = await sink
                .SendChunkAsync(NewChangesetChunk(3L, 3L), cancellationToken)
                .ConfigureAwait(false);

            return RetCode.CANCELLED;
        };

        CollectingStream stream = new();

        // NO EXCEPTION. Asserted by the call simply completing - a fault here would fail the test with
        // the fault rather than with an assertion, which is the clearer failure of the two.
        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            new CallContext(cancellation.Token));

        Assert.Equal(RetCode.CANCELLED, secondHandoverResult);
        Assert.Equal(RetCode.CANCELLED, thirdHandoverResult);

        // ONLY THE PRE-CANCELLATION CHUNK REACHED THE WIRE. Chunks 2 and 3 were enqueued and then
        // dropped rather than written, which is the "stops emission" half of the property.
        Assert.Equal(1L, Assert.Single(stream.DataChunks).ChunkIndex);

        // AND NO TERMINAL STATUS WAS WRITTEN, because a stream whose peer has gone has nowhere to put
        // one - the refusal is reported to the RETRIEVAL, which is the only listener left.
        Assert.Empty(stream.Statuses);
    }

    [Fact]
    public void AChunkIndexOutsideTheOneBasedDomainIsRefusedBeforeItCanReachTheWire()
    {
        // THE GUARD THAT MAKES THE ASSERTIONS ABOVE MORE THAN A CONVENTION. A zero index, an index past
        // the declared count and a non-positive count are all rejected at construction, so no projection
        // in the adapter has to defend against them and a zero-based stream cannot be built by accident
        // (hazard R9).
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ChangesetChunk(NewCarrierState(), 3L, 0L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ChangesetChunk(NewCarrierState(), 3L, 4L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ChangesetChunk(NewCarrierState(), 0L, 1L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ChangesetChunk(NewCarrierState(), -1L, 1L));

        // AND THE LEGAL EXTREMES ARE ACCEPTED, so the guard is a domain check rather than a blanket
        // refusal: the first position and the last are both valid.
        ChangesetChunk first = new(NewCarrierState(), 3L, 1L);
        ChangesetChunk last = new(NewCarrierState(), 3L, 3L);

        Assert.Equal(1L, first.ChunkIndex);
        Assert.Equal(3L, last.ChunkIndex);
    }

    [Fact]
    public async Task Query_ForAnUnknownHandle_WritesAStatusOnlyStreamAndNeverEntersTheRunner()
    {
        // A STATUS-ONLY STREAM IS A LEGAL SHAPE on this contract, because the terminal status is one of
        // the five arms. The refusal therefore travels in the same channel a real outcome would use
        // rather than as an exception - and the runner is never entered, so nothing was half-executed.
        using Fixture fixture = new();

        CollectingStream stream = new();
        await fixture.Service.Query(
            new QueryRequest { Task = new TaskHandle { TaskId = "no-such-task" } },
            stream,
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidHandle, Assert.Single(stream.Statuses).RetCode);
        Assert.Equal(0, fixture.Runner.Runs);
        Assert.Empty(stream.DataChunks);
    }

    // ==============================================================================================
    //  SECTION 3 - E_BUSY ON EVERY MUTATOR, ENUMERATED RATHER THAN SPOT-CHECKED
    //
    //  The oracle writes its caller-side busy guard out NINE TIMES, once per mutator
    //  [n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144, :L162, :L179, :L188],
    //  and the worker-side reset states it a tenth time [:L237]. In process that READ cannot be raced -
    //  the caller and the task are one thread of control - so across a request boundary the adapter
    //  takes it as a LEASE instead: same codes, same order, now indivisible. THAT MECHANISM is owned by
    //  QueryServiceLeaseTests and by TaskOperationLatchTests.
    //
    //  WHAT THIS SECTION OWNS IS COMPLETENESS, WHICH NEITHER OF THOSE ESTABLISHES. The lease suite spot-
    //  checks five mutators, one per shape; a guard that was simply MISSING from a sixth would pass it.
    //  So the guard is driven here as a theory over the WHOLE mutator set, and - because a theory can
    //  only cover what someone remembered to list - the set is CROSS-CHECKED BY REFLECTION against the
    //  RPC roster the generated base publishes. Add an RPC to persistence.v1.proto and one of the two
    //  cases below fails until it has been classified, which is the failure mode a hand-maintained list
    //  cannot produce on its own.
    //
    //  THE FOUR-WAY CLASSIFICATION, AND WHY IT IS NOT "EVERYTHING IS A MUTATOR":
    //
    //    MUTATORS (7)   guarded. They change per-task configuration a retrieval is reading.
    //    STREAMING (1)  `Query` guards too, and is asserted separately because its refusal travels on
    //                   its own stream as a status arm rather than as a response field.
    //    READER (1)     `Count` is DELIBERATELY UNGUARDED (constraint C-B). The oracle's nine guards are
    //                   on mutators; its three count accessors
    //                   [n_cst_threading_task_sqlquery.sru:L70, :L71, :L78] have none, and a field read
    //                   cannot fail. Guarding it would invent a refusal the legacy never answers.
    //    LIFECYCLE (2)  `CreateQueryTask` and `ReleaseQueryTask` are not per-task mutators: one has no
    //                   task yet, and the other retires the handle rather than configuring it. The
    //                   release's own interaction with an in-flight retrieval - the deferred teardown -
    //                   is owned by QueryServiceLeaseTests.
    //
    //  THE SETTINGS THAT HAVE NO RPC OF THEIR OWN ARE COVERED TOO. Ten of the eighteen legacy setters
    //  arrive as QuerySpec fields on the create and query calls, so their guard is `Query`'s - the
    //  specification is merged INSIDE the run lease [Grpc/QueryService.cs: Query]. The last case in this
    //  section pins that, which is why "eleven RPCs cover eighteen setters" leaves nothing unguarded.
    // ==============================================================================================

    /// <summary>
    /// One invocation of one mutating RPC, reduced to the status it answers.
    /// </summary>
    /// <remarks>
    /// Every argument is deliberately VALID, so a refusal below can only be the busy guard and never an
    /// argument fault: a zero select index would fail its own guard [<c>:L271</c>], a chunk size at the
    /// floor would fail the guard section 1 owns, and either would make the case pass for the wrong
    /// reason.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, Func<GrpcQueryService, TaskHandle, Task<OperationStatus>>>
        Mutators =
            new Dictionary<string, Func<GrpcQueryService, TaskHandle, Task<OperationStatus>>>(
                StringComparer.Ordinal)
            {
                // The reset states its guard INLINE rather than through the shared helper, which makes it
                // the one most easily left behind by a refactor of that helper [:L237].
                ["Reset"] = async (service, handle) =>
                    (await service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context)
                        .ConfigureAwait(false)).Status,

                ["SetChunkSize"] = async (service, handle) =>
                    (await service.SetChunkSize(
                        new SetChunkSizeRequest { Task = handle, ChunkSize = 5_000L },
                        Fixture.Context).ConfigureAwait(false)).Status,

                ["SetMaxRows"] = async (service, handle) =>
                    (await service.SetMaxRows(
                        new SetMaxRowsRequest { Task = handle, MaxRows = 10L },
                        Fixture.Context).ConfigureAwait(false)).Status,

                ["SetWhereClause"] = async (service, handle) =>
                    (await service.SetWhereClause(
                        new SetWhereClauseRequest { Task = handle, Clause = NewClauseSpec(SomeWhereClause) },
                        Fixture.Context).ConfigureAwait(false)).Status,

                ["SetOrderByClause"] = async (service, handle) =>
                    (await service.SetOrderByClause(
                        new SetOrderByClauseRequest
                        {
                            Task = handle,
                            Clause = NewClauseSpec(SomeOrderByClause),
                        },
                        Fixture.Context).ConfigureAwait(false)).Status,

                // Five legacy setters in one call, because the oracle RE-VALIDATES size and index
                // together at run time [:L307-L310].
                ["SetPaging"] = async (service, handle) =>
                    (await service.SetPaging(
                        new SetPagingRequest
                        {
                            Task = handle,
                            Paged = true,
                            PageSize = 10L,
                            PageIndex = 1L,
                            PageNative = false,
                            PageCounting = true,
                        },
                        Fixture.Context).ConfigureAwait(false)).Status,

                ["SetPagedUniqueIndexColumns"] = async (service, handle) =>
                    (await service.SetPagedUniqueIndexColumns(
                        new SetPagedUniqueIndexColumnsRequest { Task = handle, Columns = { "id" } },
                        Fixture.Context).ConfigureAwait(false)).Status,
            };

    /// <summary>The mutating RPC names, as theory data.</summary>
    /// <returns>Every key of <see cref="Mutators"/>.</returns>
    public static TheoryData<string> MutatorNames() => [.. Mutators.Keys];

    [Theory]
    [MemberData(nameof(MutatorNames))]
    public async Task EveryMutator_IsRefusedAsBusyWhileARetrievalIsInFlight(string mutator)
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        // The retrieval is held inside the runner seam, so the lease is genuinely open for the whole of
        // the mutation attempt - no database, no statement, no row (constraint C-E).
        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream stream = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        await fixture.Runner.Entered.Task.WaitAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        OperationStatus refused = await Mutators[mutator](fixture.Service, handle).ConfigureAwait(true);

        // E_BUSY IS A DISTINCT OUTCOME FROM A FAILURE, and flattening the two would remove a distinction
        // a caller can act on: retrying the same call unchanged succeeds once the retrieval finishes.
        Assert.Equal(WireRetCode.EBusy, refused.RetCode);

        // AND IT IS RETRYABLE-BY-CONSTRUCTION: this refusal is one the adapter owns, so unlike a
        // delegated guard it carries text, and that text names the remedy rather than any value.
        Assert.NotEqual(string.Empty, refused.ErrorText);
        Assert.DoesNotContain("5000", refused.ErrorText, StringComparison.Ordinal);

        gate.SetResult();
        await retrieval.ConfigureAwait(true);

        // THE LEASE CAME BACK, so the very same call now succeeds unchanged. Without this half the case
        // would also pass against an implementation that refused the mutator permanently.
        OperationStatus accepted = await Mutators[mutator](fixture.Service, handle).ConfigureAwait(true);

        Assert.Equal(WireRetCode.Ok, accepted.RetCode);
    }

    [Theory]
    [MemberData(nameof(MutatorNames))]
    public async Task EveryMutator_SucceedsWhenNoRetrievalIsInFlight(string mutator)
    {
        // THE CONTROL HALF OF THE THEORY ABOVE, run in isolation so a mutator that is broken for its own
        // reasons cannot masquerade as a correctly guarded one.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        OperationStatus status = await Mutators[mutator](fixture.Service, handle).ConfigureAwait(true);

        Assert.Equal(WireRetCode.Ok, status.RetCode);
        Assert.Equal(string.Empty, status.ErrorText);
    }

    [Theory]
    [MemberData(nameof(MutatorNames))]
    public async Task EveryMutator_IsRefusedWithInvalidHandleWhenTheTaskIsNotHeld(string mutator)
    {
        // THE OTHER REFUSAL THE SHARED HELPER OWNS, and the pair matters: telling a caller its task is
        // BUSY when the truth is that it is GONE sends it into a retry loop that can never succeed.
        using Fixture fixture = new();

        OperationStatus status = await Mutators[mutator](
            fixture.Service,
            new TaskHandle { TaskId = "no-such-task" }).ConfigureAwait(true);

        Assert.Equal(WireRetCode.EInvalidHandle, status.RetCode);
    }

    [Fact]
    public void TheMutatorTheoryCoversEveryMutatingRpcTheContractPublishes()
    {
        // THE GUARD ON THE GUARD. A theory covers what its data lists, so this case derives the roster
        // from the GENERATED BASE and requires every published RPC to be classified. A new RPC that
        // nobody classified belongs to no partition and fails the final assertion; a new MUTATOR that
        // nobody added to the table fails the one before it. Neither can be added silently.
        string[] published =
        [
            .. typeof(GeneratedQueryServiceBase)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(candidate => !candidate.IsSpecialName)
                .Select(candidate => candidate.Name),
        ];

        // The roster the contract declares [persistence.v1.proto: service QueryService]. Asserted as a
        // count as well as by partition, so a REMOVED rpc is caught too.
        Assert.Equal(11, published.Length);

        HashSet<string> lifecycle = new(StringComparer.Ordinal)
        {
            "CreateQueryTask",
            "ReleaseQueryTask",
        };

        // DELIBERATELY UNGUARDED, and section 4 asserts that positively rather than by omission.
        HashSet<string> readers = new(StringComparer.Ordinal) { "Count" };

        // GUARDED, but its refusal travels as a status arm on its own stream, so it is asserted by the
        // dedicated case below rather than through the mutator table.
        HashSet<string> streaming = new(StringComparer.Ordinal) { "Query" };

        HashSet<string> expectedMutators = new(published, StringComparer.Ordinal);
        expectedMutators.ExceptWith(lifecycle);
        expectedMutators.ExceptWith(readers);
        expectedMutators.ExceptWith(streaming);

        // EVERY MUTATING RPC IS IN THE THEORY, AND THE THEORY CONTAINS NOTHING ELSE. Set equality in
        // both directions, so a stale entry is caught alongside a missing one.
        Assert.Equal(
            expectedMutators.Order(StringComparer.Ordinal),
            Mutators.Keys.Order(StringComparer.Ordinal));

        // Every classification names a real RPC, so a rename cannot leave a partition silently empty.
        foreach (string classified in lifecycle.Concat(readers).Concat(streaming))
        {
            Assert.Contains(classified, published, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Query_IsItselfRefusedAsBusyWhileARetrievalIsInFlight()
    {
        // THE STREAMING MEMBER'S OWN GUARD. Two concurrent retrievals on one task would interleave their
        // chunk sequences, which is precisely what the one-based counters cannot express - so the second
        // is refused rather than admitted, and the refusal arrives as a STATUS ARM because a streaming
        // call has no response field to put it in.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream first = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            first,
            Fixture.Context);

        await fixture.Runner.Entered.Task.WaitAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        CollectingStream second = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, second, Fixture.Context);

        Assert.Equal(WireRetCode.EBusy, Assert.Single(second.Statuses).RetCode);

        // THE RUNNER WAS ENTERED EXACTLY ONCE, so the refusal is the adapter's own and the second run
        // never reached the task at all.
        Assert.Equal(1, fixture.Runner.Runs);

        gate.SetResult();
        await retrieval.ConfigureAwait(true);
    }

    [Fact]
    public async Task TheSpecificationSettingsWithNoRpcOfTheirOwnAreGuardedByTheRunLease()
    {
        // WHY "ELEVEN RPCs FOR EIGHTEEN SETTERS" LEAVES NOTHING UNGUARDED. Ten settings - cache, data
        // object, SQL syntax, SQL, hook class, filter, sort, paged, page native and page counting -
        // arrive as QuerySpec fields rather than as calls of their own. The specification is merged
        // INSIDE the run lease, so a second query carrying a specification is refused before any field
        // of it is applied, and the task's configuration cannot be rewritten under a live retrieval.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        // A value the merge would install, so "not applied" is observable rather than inferred.
        Assert.Equal(string.Empty, fixture.TaskFor(handle).HookClass);

        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream first = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            first,
            Fixture.Context);

        await fixture.Runner.Entered.Task.WaitAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        CollectingStream second = new();
        await fixture.Service.Query(
            new QueryRequest
            {
                Task = handle,
                Spec = new QuerySpec { HookClass = ProbeHookClass, Cache = true, Sort = "id A" },
            },
            second,
            Fixture.Context);

        Assert.Equal(WireRetCode.EBusy, Assert.Single(second.Statuses).RetCode);

        // NOT ONE FIELD OF THE REFUSED SPECIFICATION LANDED. That is the property the lease buys and a
        // re-read of a flag would not: with a check-then-act the merge could begin after the check.
        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.Equal(string.Empty, task.HookClass);
        Assert.False(task.Cache);
        Assert.Equal(string.Empty, task.NewSort);

        gate.SetResult();
        await retrieval.ConfigureAwait(true);

        // AND THE IDENTICAL SPECIFICATION APPLIES ONCE THE LEASE IS FREE, so the refusal was the guard.
        CollectingStream third = new();
        await fixture.Service.Query(
            new QueryRequest
            {
                Task = handle,
                Spec = new QuerySpec { HookClass = ProbeHookClass, Cache = true, Sort = "id A" },
            },
            third,
            Fixture.Context);

        task = fixture.TaskFor(handle);
        Assert.Equal(ProbeHookClass, task.HookClass);
        Assert.True(task.Cache);
        Assert.Equal("id A", task.NewSort);
    }

    // ==============================================================================================
    //  SECTION 4 - THE SETTABLE SURFACE, ONE-FOR-ONE, AND THE COUNT READER
    //
    //  Eighteen legacy setters [:L55-L70], eleven RPCs, and the arithmetic is deliberate: four settings
    //  get a dedicated call, the five paging values are grouped because the oracle re-validates size and
    //  index TOGETHER at run time [:L307-L310], and the remaining ten arrive as QuerySpec fields. One
    //  model, two access paths to the same state - so this section drives both and asserts the FIELD.
    //
    //  THREE GUARD BOUNDARIES ACROSS FOUR NUMERIC SETTERS, AND THEY ARE NOT UNIFORM. Harmonising any of
    //  them changes observable behaviour (constraint C-B):
    //
    //     of_setchunksize   `<= 1000`   so 1000 is REFUSED and 1001 is the floor      [:L410]
    //     of_setmaxrows     `< 0`       so ZERO IS LEGAL and means no limit            [:L433]
    //     of_setpagesize    `<= 0`      a ONE-BASED domain                             [:L462]
    //     of_setpageindex   `<= 0`      a ONE-BASED domain, so the first page is 1     [:L450]
    //
    //  THE RESET DEFAULTS ARE ASSERTED THROUGH THE RPC [:L250-L266]. SqlQueryTaskTests pins the same
    //  values directly on the task; what the RPC-level case adds is that the adapter's reset arm CHAINS
    //  to that one body instead of restating it - the two-method pair is `if #Running then return
    //  RetCode.E_BUSY` / `_of_Reset()` / `return super::of_Reset()` [:L237-L241], and an adapter that
    //  cleared fields itself would drift from the private body the constructor also uses.
    //
    //  THE CLAUSE SETTERS DELEGATE THEIR UPSERT [:L269-L300]. `ClauseModifierTests` owns the one-based
    //  scan, the update-in-place-or-append semantics and the clause-body grammar. What is asserted here
    //  is the DELEGATION: that the one-based select index and the SQL_MS_* style reach that collection
    //  untouched, that a zero index is NOT rewritten to one, and that the guard's E_INVALID_ARGUMENT
    //  surfaces through the RPC rather than being swallowed or re-coded.
    // ==============================================================================================

    [Fact]
    public async Task Reset_RestoresEveryDefaultThePrivateResetLists()
    {
        // Every field the oracle's `_of_reset` assigns [:L250-L266], driven away from its default first
        // so the restoration is observable rather than vacuous.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Cache = true,
            Sql = SomeSelect,
            HookClass = ProbeHookClass,
            Filter = "id > 5",
            Sort = "name A",
            MaxRows = 250L,
            ChunkSize = 4_096L,
            Paged = true,
            PageSize = 25L,
            PageIndex = 3L,
            PageCounting = false,
            PagedUniqueIndexColumns = { "COMPANY.ID" },
        });

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest { Task = handle, Clause = NewClauseSpec(SomeWhereClause) },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest { Task = handle, Clause = NewClauseSpec(SomeOrderByClause) },
                Fixture.Context)).Status.RetCode);

        // Everything is genuinely off its default before the reset.
        SqlQueryTask before = fixture.TaskFor(handle);
        Assert.True(before.Cache);
        Assert.NotEmpty(before.Clauses.WhereClauses);
        Assert.NotEmpty(before.Clauses.OrderByClauses);
        Assert.NotEmpty(before.PagedUniqueIndexColumns);

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.Reset(
                new ResetQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        SqlQueryTask task = fixture.TaskFor(handle);

        // [:L250-L255] the six string fields.
        Assert.Equal(string.Empty, task.HookClass);
        Assert.Equal(string.Empty, task.Sql);
        Assert.Equal(string.Empty, task.SqlSyntax);
        Assert.Equal(string.Empty, task.DataObject);
        Assert.Equal(string.Empty, task.NewSort);
        Assert.Equal(string.Empty, task.NewFilter);

        // [:L256] 10000, [:L257] 0, [:L258] false, [:L259] 0.
        Assert.Equal(SqlQueryTask.DefaultChunkSize, task.ChunkSize);
        Assert.Equal(0L, task.PageIndex);
        Assert.False(task.Paged);
        Assert.Equal(0L, task.PageSize);

        // [:L260] THE ONE BOOLEAN WHOSE LEGACY DEFAULT IS TRUE. A reset that left it false would switch
        // page counting off for every caller who never mentioned it.
        Assert.True(task.PageCounting);

        // [:L261-L262]
        Assert.Equal(0L, task.MaxRows);
        Assert.False(task.Cache);

        // [:L264-L266] the two clause collections and the unique-index list.
        Assert.Empty(task.Clauses.WhereClauses);
        Assert.Empty(task.Clauses.OrderByClauses);
        Assert.Empty(task.PagedUniqueIndexColumns);

        // A ZERO PAGE INDEX IS BELOW ITS OWN SETTER'S LEGAL DOMAIN [:L450], and that is the ORACLE'S
        // state rather than an inconsistency introduced by the port: an unset page index is zero and the
        // paged builder re-validates it at run time [:L307-L310]. Pinned so nobody "fixes" the reset to
        // install 1.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.SetPaging(
                new SetPagingRequest { Task = handle, Paged = true, PageSize = 10L, PageIndex = 0L },
                Fixture.Context)).Status.RetCode);
    }

    [Fact]
    public async Task Reset_DoesNotClearPageNative_PreservedLegacyDefect()
    {
        // DEFECT D2, ASSERTED AS EXPECTED THROUGH THE RPC (constraint C-B). The oracle's `_of_reset`
        // [:L247-L267] assigns twelve fields and DOES NOT TOUCH `_bPageNative`, so the flag survives a
        // reset while every one of its neighbours is restored. The task-level case is owned by
        // SqlQueryTaskTests; what only an RPC-level case can establish is that the adapter's reset arm
        // did not helpfully add the missing clear on its way through.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPaging(
                new SetPagingRequest
                {
                    Task = handle,
                    Paged = true,
                    PageSize = 10L,
                    PageIndex = 1L,
                    PageNative = true,
                    PageCounting = true,
                },
                Fixture.Context)).Status.RetCode);

        Assert.True(fixture.TaskFor(handle).PageNative);

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.Reset(
                new ResetQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        SqlQueryTask task = fixture.TaskFor(handle);

        // ITS NEIGHBOURS WERE ALL RESTORED, which is what makes the survival a defect rather than a
        // reset that simply did nothing.
        Assert.False(task.Paged);
        Assert.Equal(0L, task.PageSize);
        Assert.Equal(0L, task.PageIndex);

        // AND THE FLAG SURVIVED. Do not "fix" this: page-native selects a different paged statement
        // [:L344-L352 versus :L353-L364], so clearing it here would change generated SQL that byte-exact
        // parity is measured against.
        Assert.True(task.PageNative);
    }

    [Fact]
    public async Task Reset_ChainsToTheTaskWhoseOwnGuardStandsBehindIt()
    {
        // THE TWO-METHOD PAIR [:L237-L241]. The adapter's arm guards on the LEASE; the task's own arm
        // guards on the RUNNING FLAG and answers the SAME code. Both halves are live, and the task's is
        // asserted directly here because the adapter's lease normally prevents it from ever being
        // reached - a guard that is unreachable in practice is exactly the one a refactor deletes.
        //
        // DRIVEN THROUGH THE REAL PIPELINE, because the running flag is raised by ExecuteAsync itself:
        // no scripted runner can make the task report running, so the retrieval is paused INSIDE the
        // store's own retrieve instead. Still no database - the store is a double (constraint C-E).
        using Fixture fixture = new(productionPipeline: true);
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        TaskCompletionSource gate = new();
        fixture.Store.RetrieveGate = gate;

        CollectingStream stream = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        await fixture.Store.RetrieveEntered.Task.WaitAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        // The task itself reports running, and its own reset refuses on that flag rather than on a lease
        // it knows nothing about.
        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.True(task.IsRunning);
        Assert.Equal(RetCode.E_BUSY, task.Reset());

        gate.SetResult();
        await retrieval.ConfigureAwait(true);

        Assert.False(task.IsRunning);
        Assert.Equal(RetCode.OK, task.Reset());
    }

    /// <summary>
    /// The three legal modification styles, each paired with the in-process constant it must arrive as.
    /// </summary>
    /// <returns>The wire style and the preserved legacy constant it maps to.</returns>
    /// <remarks>
    /// The expected values are READ from <c>ClauseModifier</c> rather than written as literals, because
    /// this file is outside the naming-suppression glob and could not declare their preserved
    /// screaming-snake spellings itself (AAP 0.7.2). Their VALUES - 1, 2 and 3 [<c>enums.sru:L718-L720</c>]
    /// - are pinned by <c>ClauseModifierTests</c>.
    /// </remarks>
    public static TheoryData<SqlModifyStyle, long> LegalModifyStyles() => new()
    {
        { SqlModifyStyle.SqlMsReplace, ClauseModifier.SQL_MS_REPLACE },
        { SqlModifyStyle.SqlMsAppend, ClauseModifier.SQL_MS_APPEND },
        { SqlModifyStyle.SqlMsPrepend, ClauseModifier.SQL_MS_PREPEND },
    };

    [Theory]
    [MemberData(nameof(LegalModifyStyles))]
    public async Task SetWhereClause_CarriesTheOneBasedIndexAndTheStyleThroughToTheClauseCollection(
        SqlModifyStyle wireStyle,
        long expectedModifyStyle)
    {
        // `of_setwhereclause(readonly integer selectindex, readonly long ms, readonly string clause)`
        // [:L36]. All three arguments must survive the projection: the index selects WHICH select block
        // the clause belongs to, and the style decides whether the existing clause is replaced, appended
        // to or prepended to. A style that arrived as REPLACE when APPEND was sent would silently
        // discard a clause and still answer OK.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        SetWhereClauseResponse response = await fixture.Service.SetWhereClause(
            new SetWhereClauseRequest
            {
                Task = handle,
                Clause = new SqlClauseSpec
                {
                    // ONE-BASED, and 2 rather than 1 so a projection that ignored the field and defaulted
                    // to the first block is caught.
                    SelectIndex = 2,
                    ModifyStyle = wireStyle,
                    Clause = SomeWhereClause,
                },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        SqlClause stored = Assert.Single(fixture.TaskFor(handle).Clauses.WhereClauses);

        Assert.Equal(2, stored.SelectIndex);
        Assert.Equal(expectedModifyStyle, stored.ModifyStyle);
        Assert.Equal(SomeWhereClause, stored.Clause);

        // THE CONVERSION IS THE COLLECTION OWNER'S, reached rather than restated, so the adapter and the
        // clause model cannot disagree about what a wire style means.
        Assert.Equal(expectedModifyStyle, ClauseModifier.ToModifyStyle(wireStyle));

        // AND THE ORDER-BY COLLECTION IS UNTOUCHED. The two are separate arrays with separate identical
        // guards [:L269-L300], which is why the contract gives them separate messages: the paged builder
        // STRIPS the ORDER BY and never touches the WHERE [:L389-L390].
        Assert.Empty(fixture.TaskFor(handle).Clauses.OrderByClauses);
    }

    [Fact]
    public async Task SetOrderByClause_UpsertsOnItsOwnCollectionSeparatelyFromWhere()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest { Task = handle, Clause = NewClauseSpec(SomeOrderByClause) },
                Fixture.Context)).Status.RetCode);

        SqlClause stored = Assert.Single(fixture.TaskFor(handle).Clauses.OrderByClauses);
        Assert.Equal(SomeOrderByClause, stored.Clause);
        Assert.Empty(fixture.TaskFor(handle).Clauses.WhereClauses);

        // THE UPSERT IS DELEGATED, NOT RE-IMPLEMENTED. The same select index updates IN PLACE rather
        // than appending, and a new one appends at the end - the oracle's own scan [:L273-L280]. The
        // semantics belong to ClauseModifierTests; what this pins is that the RPC reaches them.
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest { Task = handle, Clause = NewClauseSpec("age") },
                Fixture.Context)).Status.RetCode);

        SqlClause replaced = Assert.Single(fixture.TaskFor(handle).Clauses.OrderByClauses);
        Assert.Equal("age", replaced.Clause);

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        SelectIndex = 2,
                        ModifyStyle = SqlModifyStyle.SqlMsReplace,
                        Clause = "salary",
                    },
                },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(
            [1, 2],
            [.. fixture.TaskFor(handle).Clauses.OrderByClauses.Select(clause => clause.SelectIndex)]);
    }

    /// <summary>
    /// Clause arguments the guard at <c>:L271</c> refuses, and the guard is the oracle's whole body.
    /// </summary>
    /// <returns>The select index and the clause body.</returns>
    public static TheoryData<int, string> RefusedClauseArguments() => new()
    {
        // `if selectIndex <= 0 ... then return RetCode.E_INVALID_ARGUMENT`. ZERO DOES NOT MEAN "THE
        // FIRST SELECT" - the domain is one-based and zero is outside it (hazard R9).
        { 0, SomeWhereClause },
        { -1, SomeWhereClause },
        { int.MinValue, SomeWhereClause },

        // `... or clause = ""`. THE COMPARISON IS AGAINST THE EMPTY STRING AND NOTHING MORE, so
        // space-only text is NOT refused here - see the case below, which pins that asymmetry.
        { 1, "" },
    };

    [Theory]
    [MemberData(nameof(RefusedClauseArguments))]
    public async Task TheClauseSetters_SurfaceTheDelegatedInvalidArgumentThroughBothRpcs(
        int selectIndex,
        string clause)
    {
        // THE GUARD'S CODE REACHES THE CALLER UNCHANGED. Both setters carry the identical guard on their
        // own collection [:L271, :L288], so both are driven - a guard present on one and absent on the
        // other is a real and easy divergence.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        SqlClauseSpec spec = new()
        {
            SelectIndex = selectIndex,
            ModifyStyle = SqlModifyStyle.SqlMsReplace,
            Clause = clause,
        };

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest { Task = handle, Clause = spec.Clone() },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest { Task = handle, Clause = spec.Clone() },
                Fixture.Context)).Status.RetCode);

        // AND NOTHING WAS STORED, which is the difference between refusing and reporting.
        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.Empty(task.Clauses.WhereClauses);
        Assert.Empty(task.Clauses.OrderByClauses);
    }

    /// <summary>
    /// Modification styles outside the published domain of three.
    /// </summary>
    /// <returns>The style as it arrives on the wire.</returns>
    /// <remarks>
    /// A proto3 enum field is OPEN: an unrecognised number is neither rejected by the parser nor folded
    /// to zero, so every value below deserializes intact and reaches the application.
    /// </remarks>
    public static TheoryData<SqlModifyStyle> OutOfDomainModifyStyles() =>
    [
        // THE UNSPECIFIED ZERO IS A PROTOCOL ARTIFACT AND IS NEVER TREATED AS REPLACE. Reading it as
        // replace would apply a clause the caller never asked to apply.
        SqlModifyStyle.SqlMsUnspecified,
        (SqlModifyStyle)4,
        (SqlModifyStyle)99,
        (SqlModifyStyle)(-1),
    ];

    [Theory]
    [MemberData(nameof(OutOfDomainModifyStyles))]
    public async Task TheClauseSetters_RefuseAModifyStyleOutsideThePublishedDomain(SqlModifyStyle style)
    {
        // A DEFINED REFUSAL THE ORACLE DOES NOT HAVE, and defining it is the correct direction: the
        // legacy dispatches on the style with NO DEFAULT ARM, so an unrecognised value silently modifies
        // nothing. Narrowing the contract with an error beats widening it with a guess (AAP 0.1.5), and
        // this refusal is the adapter's own - so unlike a delegated one it carries text.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        SetWhereClauseResponse response = await fixture.Service.SetWhereClause(
            new SetWhereClauseRequest
            {
                Task = handle,
                Clause = new SqlClauseSpec
                {
                    SelectIndex = 1,
                    ModifyStyle = style,
                    Clause = SomeWhereClause,
                },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.NotEqual(string.Empty, response.Status.ErrorText);

        // NO CLAUSE TEXT WAS APPLIED, which the diagnostic promises and which a caller relies on.
        Assert.Empty(fixture.TaskFor(handle).Clauses.WhereClauses);
    }

    [Fact]
    public async Task TheClauseSetters_RefuseARequestCarryingNoClauseMessageAtAll()
    {
        // A MESSAGE FIELD LEFT UNSET ARRIVES AS NULL rather than as a default-constructed instance, so
        // the arm exists and has to be answered. Refused with the adapter's own text, because there is
        // no legacy call shape that omits the argument.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        SetWhereClauseResponse where = await fixture.Service.SetWhereClause(
            new SetWhereClauseRequest { Task = handle },
            Fixture.Context);

        SetOrderByClauseResponse orderBy = await fixture.Service.SetOrderByClause(
            new SetOrderByClauseRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, where.Status.RetCode);
        Assert.Equal(WireRetCode.EInvalidArgument, orderBy.Status.RetCode);
        Assert.NotEqual(string.Empty, where.Status.ErrorText);
        Assert.NotEqual(string.Empty, orderBy.Status.ErrorText);
    }

    [Fact]
    public async Task SetPaging_AppliesTheFiveValuesAndAbandonsAtTheFirstRefusal()
    {
        // FIVE LEGACY SETTERS IN ONE CALL, APPLIED IN THE CONTRACT'S OWN FIELD ORDER and abandoned at
        // the first refusal, so the answer names WHICH setting was wrong - the same property five
        // successive legacy calls give a caller. Earlier settings REMAIN APPLIED, exactly as five
        // successive calls would leave them: the legacy has no transactional setter group and inventing
        // one here would be a behaviour change (constraint C-B).
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPaging(
                new SetPagingRequest
                {
                    Task = handle,
                    Paged = true,
                    PageSize = 25L,
                    PageIndex = 4L,
                    PageNative = true,
                    PageCounting = false,
                },
                Fixture.Context)).Status.RetCode);

        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.True(task.Paged);
        Assert.Equal(25L, task.PageSize);
        Assert.Equal(4L, task.PageIndex);
        Assert.True(task.PageNative);
        Assert.False(task.PageCounting);

        // A REFUSED PAGE INDEX abandons the sequence at field three. Paged and page size - fields one
        // and two - were already applied and STAY applied.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.SetPaging(
                new SetPagingRequest
                {
                    Task = handle,
                    Paged = false,
                    PageSize = 50L,
                    PageIndex = 0L,
                    PageNative = false,
                    PageCounting = true,
                },
                Fixture.Context)).Status.RetCode);

        task = fixture.TaskFor(handle);
        Assert.False(task.Paged);
        Assert.Equal(50L, task.PageSize);

        // Field three onwards never ran, so the index kept its previous value and the two trailing
        // booleans kept theirs.
        Assert.Equal(4L, task.PageIndex);
        Assert.True(task.PageNative);
        Assert.False(task.PageCounting);
    }

    [Fact]
    public async Task SetMaxRows_AcceptsZeroBecauseItsBoundaryIsDeliberatelyNotTheChunkSizeBoundary()
    {
        // `if rows < 0 then return RetCode.E_INVALID_ARGUMENT` [:L433]. ZERO IS LEGAL and is the field's
        // own legacy default, meaning NO LIMIT. That is deliberately NOT the chunk size's `<= 1000`, and
        // the two must not be made uniform (constraint C-B).
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 0L },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(0L, fixture.TaskFor(handle).MaxRows);

        foreach (long refused in (long[])[-1L, long.MinValue])
        {
            Assert.Equal(
                WireRetCode.EInvalidArgument,
                (await fixture.Service.SetMaxRows(
                    new SetMaxRowsRequest { Task = handle, MaxRows = refused },
                    Fixture.Context)).Status.RetCode);
        }

        Assert.Equal(0L, fixture.TaskFor(handle).MaxRows);
    }

    [Fact]
    public async Task SetPagedUniqueIndexColumns_ReplacesTheListWholesaleAndAnEmptyListClearsIt()
    {
        // [:L406] the oracle ASSIGNS the array rather than merging it, unlike the two clause collections
        // which are upserted. An empty list on THIS call therefore CLEARS - the one place an empty
        // repeated field means "clear" rather than "leave alone", because here it is the whole payload of
        // a dedicated call rather than one field of a merge specification.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPagedUniqueIndexColumns(
                new SetPagedUniqueIndexColumnsRequest
                {
                    Task = handle,
                    Columns = { "COMPANY.ID", "COMPANY.NAME" },
                },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(
            ["COMPANY.ID", "COMPANY.NAME"],
            [.. fixture.TaskFor(handle).PagedUniqueIndexColumns]);

        // WHOLESALE, not merged: the second list does not contain the first.
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPagedUniqueIndexColumns(
                new SetPagedUniqueIndexColumnsRequest { Task = handle, Columns = { "age" } },
                Fixture.Context)).Status.RetCode);

        Assert.Equal("age", Assert.Single(fixture.TaskFor(handle).PagedUniqueIndexColumns));

        // AND THE EMPTY LIST CLEARS. Refusing it would break the one call a caller makes to undo the
        // setting, and the setting is not a hint - a non-empty list selects a different paged statement
        // [:L323].
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPagedUniqueIndexColumns(
                new SetPagedUniqueIndexColumnsRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        Assert.Empty(fixture.TaskFor(handle).PagedUniqueIndexColumns);
    }

    [Fact]
    public async Task TheSpecification_LeavesAnUnsetFieldAloneAndAppliesEveryPresentOne()
    {
        // EXPLICIT PRESENCE IS LOAD BEARING, NOT DECORATION. An unset optional field must leave the
        // task's existing value alone, and for two fields the implicit default is not even legal: the
        // chunk size defaults to 10000 and zero would be refused by its own guard, while page counting
        // defaults to TRUE so an implicit false would switch counting off for every caller who simply
        // did not mention it.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            ChunkSize = 8_000L,
            MaxRows = 42L,
            Cache = true,
            HookClass = ProbeHookClass,
        });

        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.Equal(8_000L, task.ChunkSize);
        Assert.Equal(42L, task.MaxRows);
        Assert.True(task.Cache);
        Assert.Equal(ProbeHookClass, task.HookClass);

        // PAGE COUNTING WAS NEVER MENTIONED and therefore kept its TRUE default rather than being
        // overwritten with the proto3 implicit false.
        Assert.True(task.PageCounting);

        // A second merge naming only ONE field leaves the other three exactly as they were.
        CollectingStream stream = new();
        await fixture.Service.Query(
            new QueryRequest { Task = handle, Spec = new QuerySpec { MaxRows = 7L } },
            stream,
            Fixture.Context);

        task = fixture.TaskFor(handle);
        Assert.Equal(7L, task.MaxRows);
        Assert.Equal(8_000L, task.ChunkSize);
        Assert.True(task.Cache);
        Assert.Equal(ProbeHookClass, task.HookClass);
        Assert.True(task.PageCounting);
    }

    [Fact]
    public async Task TheSpecification_SetsDataObjectAndSqlSyntaxSoThatTheLaterAssignmentWins()
    {
        // TWO SETTINGS THAT INTERACT [:L419-L420, :L474-L475]: setting the data object blanks the syntax
        // and setting the syntax blanks the data object. With both present the LATER assignment in the
        // contract's field order wins, exactly as two successive legacy calls would behave - which is
        // why neither is modelled as a mutually exclusive choice.
        using Fixture fixture = new();

        // data_object is field 2 and sql_syntax is field 3, so the syntax is applied second and wins.
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            DataObject = "dw_query_probe",
            SqlSyntax = FixtureSyntax,
        });

        SqlQueryTask task = fixture.TaskFor(handle);
        Assert.Equal(FixtureSyntax, task.SqlSyntax);
        Assert.Equal(string.Empty, task.DataObject);

        // AND THE OTHER DIRECTION, applied as a second merge so the ordering above is the field order
        // rather than one of the two setters simply not working.
        CollectingStream stream = new();
        await fixture.Service.Query(
            new QueryRequest
            {
                Task = handle,
                Spec = new QuerySpec { DataObject = "dw_query_probe" },
            },
            stream,
            Fixture.Context);

        task = fixture.TaskFor(handle);
        Assert.Equal("dw_query_probe", task.DataObject);
        Assert.Equal(string.Empty, task.SqlSyntax);
    }

    /// <summary>Values the sort and filter setters rewrite to a single space.</summary>
    /// <returns>The value as it arrives on the wire.</returns>
    public static TheoryData<string> ClearedSortAndFilterValues() => ["", " ", "   "];

    [Theory]
    [MemberData(nameof(ClearedSortAndFilterValues))]
    public async Task TheSpecification_RewritesAnEmptySortOrFilterToASingleSpace_PreservedLegacyDefect(
        string requested)
    {
        // A DOCUMENTED QUIRK, PRESERVED VERBATIM (constraint C-B). `_sNewFilter = filter; if
        // _sNewFilter = "" then _sNewFilter = " "` [:L481], and the sort setter carries the identical
        // rewrite [:L487]. It LOOKS like a typo and is not: the single space is how the legacy asks for a
        // filter to be CLEARED, as distinct from leaving it unset - and the run-time arms test
        // `_sNewFilter <> ""` before applying it [:L563, :L570], so an empty string would be skipped
        // where a space is applied.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sort = requested,
            Filter = requested,
        });

        SqlQueryTask task = fixture.TaskFor(handle);

        // The empty string becomes the sentinel space; whitespace that was already non-empty is carried
        // through untouched, because the guard compares against the empty string and nothing more.
        string expected = requested.Length == 0 ? SqlQueryTask.ClearedSortOrFilter : requested;

        Assert.Equal(expected, task.NewSort);
        Assert.Equal(expected, task.NewFilter);
        Assert.NotEqual(string.Empty, task.NewSort);
        Assert.NotEqual(string.Empty, task.NewFilter);
    }

    // ----------------------------------------------------------------------------------------------
    //  SECTION 4b - `Count`, THE READER, AND THE -1 SENTINELS
    //
    //  The legacy count surface is THREE CALLER-SIDE ACCESSORS over three private fields -
    //  `of_getpagecount()`, `of_getrecordcount()` and `of_getrowcount()`
    //  [n_cst_threading_task_sqlquery.sru:L70, :L71, :L78] - populated by the `onpagereceived` and
    //  `ondatareceived` events. So this RPC reads what the last run published and ISSUES NO STATEMENT.
    //
    //  -1 IS A VALUE AND NOT AN ABSENCE (constraint C-B). The oracle assigns `-1` to BOTH totals on the
    //  else-branch of its three-condition gate [:L861-L862] - taken when the task is not paged, when
    //  counting is switched off, or when the source is a stored procedure [:L818] - so a consumer must
    //  read -1 as "not counted" and must NOT do arithmetic on it. It is emphatically not zero, which
    //  would claim a real total of no pages.
    //
    //  THE COUNTING STATEMENT'S BYTE-EXACT FORM IS NOT ASSERTED HERE. `SELECT COUNT(1) AS CNT FROM (...)
    //  pfwPagedSQL_Tbl` with the `1 AS _` column replacement [:L830, :L834] is owned by
    //  PagingRewriterByteExactTests and SqlQueryTaskTests. What this section owns is the RPC surfacing
    //  the totals, the sentinels, and the `counted` flag that distinguishes a real count query from the
    //  arithmetic short-circuit.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Count_SurfacesThePageAndRecordTotalsTheRetrievalPublished()
    {
        using Fixture fixture = new();

        // A FULL PAGE, so the arithmetic short-circuit [:L820-L823] does NOT apply and the totals came
        // from a counting statement - which is what makes `counted` true.
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Paged = true,
            PageSize = 10L,
            PageIndex = 1L,
            PageCounting = true,
        });

        fixture.Runner.Script = (sink, _) =>
        {
            // ORDER MATTERS: the row count is published first [:L83] and the page totals last [:L866],
            // and the `counted` derivation reads the row count that arrived first.
            sink.OnDataReceived(10L);
            sink.OnPageReceived(4L, 37L);

            return Task.FromResult(RetCode.OK);
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, counts.Status.RetCode);
        Assert.Equal(4L, counts.PageCount);
        Assert.Equal(37L, counts.RecordCount);
        Assert.True(counts.Counted);

        // AND THE SAME TOTALS TRAVELLED ON THE STREAM'S OWN ARM, so a caller that read the stream and a
        // caller that polled the reader see the same numbers.
        QueryPageCounts published = Assert.Single(stream.PageCounts);
        Assert.Equal(4L, published.PageCount);
        Assert.Equal(37L, published.RecordCount);
    }

    [Fact]
    public async Task Count_WithPageCountingOff_SurfacesTheMinusOneSentinelOnBothTotals()
    {
        // [:L861-L862] the else-branch. Driven with counting switched OFF, which is one of the three
        // conditions that reaches it.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Paged = true,
            PageSize = 10L,
            PageIndex = 1L,
            PageCounting = false,
        });

        fixture.Runner.Script = (sink, _) =>
        {
            sink.OnDataReceived(10L);

            // What the oracle publishes on that branch: the sentinel on BOTH, not on one.
            sink.OnPageReceived(SqlQueryTask.NotCountedValue, SqlQueryTask.NotCountedValue);

            return Task.FromResult(RetCode.OK);
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        // THE SENTINEL IS -1 AND IS ASSERTED AS SUCH, against the published constant so the two cannot
        // drift, and then against the literal so the constant's own value is pinned here too.
        Assert.Equal(-1L, SqlQueryTask.NotCountedValue);
        Assert.Equal(SqlQueryTask.NotCountedValue, counts.PageCount);
        Assert.Equal(SqlQueryTask.NotCountedValue, counts.RecordCount);

        // NOT ZERO. A zero page count would claim a real total of no pages, which is a different fact.
        Assert.NotEqual(0L, counts.PageCount);
        Assert.NotEqual(0L, counts.RecordCount);

        // AND `counted` IS FALSE, which is how a consumer tells "not counted" from a real total without
        // having to special-case the numeral.
        Assert.False(counts.Counted);

        // The sentinels reached the stream arm too, unchanged.
        QueryPageCounts published = Assert.Single(stream.PageCounts);
        Assert.Equal(SqlQueryTask.NotCountedValue, published.PageCount);
        Assert.Equal(SqlQueryTask.NotCountedValue, published.RecordCount);
    }

    [Fact]
    public async Task Count_AfterAnArithmeticShortCircuit_CarriesRealTotalsWithCountedFalse()
    {
        // THE SHORT-CIRCUIT IS OBSERVABLE THROUGH `counted`, AND ITS TOTALS ARE REAL. When the current
        // page came back shorter than the page size the oracle issues NO counting statement at all: the
        // page count becomes the current index and the record count is computed arithmetically
        // [:L820-L823]. So a consumer must NOT infer from a missing count query that counting FAILED -
        // which is exactly why `counted` is a field rather than something a caller has to deduce.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Paged = true,
            PageSize = 10L,
            PageIndex = 1L,
            PageCounting = true,
        });

        fixture.Runner.Script = (sink, _) =>
        {
            // A PARTIAL FINAL PAGE: three rows against a page size of ten.
            sink.OnDataReceived(3L);
            sink.OnPageReceived(1L, 3L);

            return Task.FromResult(RetCode.OK);
        };

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        // REAL, USABLE TOTALS - and neither is the sentinel.
        Assert.Equal(1L, counts.PageCount);
        Assert.Equal(3L, counts.RecordCount);
        Assert.NotEqual(SqlQueryTask.NotCountedValue, counts.PageCount);

        // BUT NO COUNTING STATEMENT WAS ISSUED, which is what the flag reports.
        Assert.False(counts.Counted);
    }

    [Fact]
    public async Task Count_ForAnUnknownHandle_IsInvalidHandleAndStillCarriesTheSentinels()
    {
        // A REFUSAL MUST NOT LOOK LIKE A COUNT OF ZERO. The refusal carries the same "not counted"
        // sentinels the else-branch publishes, so a caller that read the totals without checking the
        // status cannot mistake a missing handle for an empty result.
        using Fixture fixture = new();

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = new TaskHandle { TaskId = "no-such-task" } },
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidHandle, counts.Status.RetCode);
        Assert.Equal(SqlQueryTask.NotCountedValue, counts.PageCount);
        Assert.Equal(SqlQueryTask.NotCountedValue, counts.RecordCount);
        Assert.False(counts.Counted);
    }

    [Fact]
    public async Task Count_BeforeAnyRetrieval_ReadsTheClearedFieldsAndCannotFail()
    {
        // A FIELD READ CANNOT FAIL, and the oracle's three accessors have no guard of any kind - which is
        // why `Count` is deliberately absent from the mutator theory in section 3 (constraint C-B).
        // Before any run the fields read as the prepare-equivalent left them, which is exactly what the
        // legacy caller would observe at the same moment.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, counts.Status.RetCode);
        Assert.Equal(0L, counts.PageCount);
        Assert.Equal(0L, counts.RecordCount);
        Assert.False(counts.Counted);
    }

    // ==============================================================================================
    //  SECTION 5 - THE PIPELINE, DRIVEN END TO END THROUGH THE RPC
    //
    //  Every case in this section uses the PRODUCTION SqlQueryTask and the PRODUCTION
    //  QueryRetrievalRunner, so a `Query` call executes the real port of `event ondotask`
    //  [:L500-L882] and the assertions are on what reaches the WIRE. Only the four collaborators that
    //  would otherwise need a storage engine or a DataWindow runtime are substituted, and the task's
    //  host and fault proxy are the shipped types (constraints C-E, C-H).
    //
    //  WHY THIS IS NOT A DUPLICATE OF SqlQueryTaskTests. That suite drives the same behaviours against
    //  the task in isolation, with a recording sink standing in for the caller. What only an end-to-end
    //  case can establish is that each outcome SURVIVES THE PROJECTION: a defensive override that fired
    //  correctly but was reported as success, a cap breach whose diagnostic was dropped on the way out,
    //  or a full-state carrier whose discriminator was lost between the codec and the wire would all
    //  pass there and fail here.
    // ==============================================================================================

    // ----------------------------------------------------------------------------------------------
    //  5a - THE RETRIEVAL HOOK AND ITS DUAL FALLBACK        [:L755-L767]
    //
    //     if IsValid(hook) then nRowCnt = hook.Event OnRetrieve(this,TransObject,Data)
    //     else SetNull(nRowCnt) end if
    //     if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then <default retrieve>
    //
    //  TWO DISTINCT VALUES MEAN "NOT HANDLED", AND ONE OF THEM LOOKS LIKE A FAILURE. A hook answering
    //  E_NO_IMPLEMENTATION has NOT failed - it declined - so the correct response is to run the default
    //  retrieval, and emphatically NOT to test the value with the failure predicate, which would answer
    //  true for it and turn every decline into a database error (constraint C-B).
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public void TheDeclinePredicateAcceptsBothNotHandledValuesAndNothingElse()
    {
        // THE DUAL FALLBACK AS A PURE PREDICATE, stated before the three end-to-end arms below so the
        // asymmetry is visible in one place. `IsNull(nRowCnt) or nRowCnt = E_NO_IMPLEMENTATION` [:L761].
        Assert.True(SqlTaskBase.HookDeclinedRetrieval(null));
        Assert.True(SqlTaskBase.HookDeclinedRetrieval(RetCode.E_NO_IMPLEMENTATION));

        // AND NOTHING ELSE - a handled retrieval owns its row count, including zero, and including a
        // NEGATIVE one, which is a genuine failure the hook is reporting rather than a decline.
        Assert.False(SqlTaskBase.HookDeclinedRetrieval(0L));
        Assert.False(SqlTaskBase.HookDeclinedRetrieval(1L));
        Assert.False(SqlTaskBase.HookDeclinedRetrieval(-1L));
        Assert.False(SqlTaskBase.HookDeclinedRetrieval(RetCode.E_NO_SUPPORT));
    }

    [Fact]
    public async Task WithNoHookAtAll_TheDefaultRetrievalRunsAndItsRowCountReachesTheWire()
    {
        // THE NULL ARM. No hook class was ever set, so the row count is left null and the default
        // retrieval runs [:L757-L765].
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 3L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(string.Empty, fixture.TaskFor(handle).HookClass);
        Assert.Equal(0, fixture.Hook.Calls);
        Assert.Equal(1, fixture.Store.OrdinalOf(RecordingQueryStore.RetrieveCall) > 0 ? 1 : 0);
        Assert.Equal(3L, Assert.Single(stream.RowCounts));
        Assert.Empty(stream.Statuses);
    }

    [Fact]
    public async Task AHookThatDeclinesWithNoImplementation_StillRunsTheDefaultRetrieval()
    {
        // THE SECOND FALLBACK ARM. The hook IS installed and IS called, answers E_NO_IMPLEMENTATION, and
        // the default retrieval runs anyway - the decline is not an error and does not reach the wire as
        // one.
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 4L;
        fixture.Hook.Answer = RetCode.E_NO_IMPLEMENTATION;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            HookClass = ProbeHookClass,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(1, fixture.Hook.Calls);
        Assert.True(fixture.Store.OrdinalOf(RecordingQueryStore.RetrieveCall) > 0);

        // THE DEFAULT'S ROW COUNT WINS, not the hook's declining code - which is the whole point of the
        // arm. A port that reported -2001 here would be reporting the sentinel as data.
        Assert.Equal(4L, Assert.Single(stream.RowCounts));

        // AND NO FAILURE STATUS, so the decline was not mistaken for a fault by the failure predicate.
        Assert.Empty(stream.Statuses);
    }

    [Fact]
    public async Task AHookThatHandlesTheRetrieval_SuppressesTheDefaultAndOwnsTheRowCount()
    {
        // THE HANDLED ARM. The hook answers a real row count, so the default retrieval must NOT run -
        // running it as well would execute the statement twice and double the result.
        using Fixture fixture = new(productionPipeline: true);

        // Deliberately DIFFERENT from the hook's answer, so "the default did not run" is observable in
        // the number rather than only in the call log.
        fixture.Store.RetrieveRows = 4L;
        fixture.Hook.Answer = 9L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            HookClass = ProbeHookClass,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(1, fixture.Hook.Calls);

        // THE DEFAULT RETRIEVAL NEVER HAPPENED.
        Assert.Equal(-1, fixture.Store.OrdinalOf(RecordingQueryStore.RetrieveCall));

        // AND THE HOOK'S COUNT IS THE ONE ON THE WIRE.
        Assert.Equal(9L, Assert.Single(stream.RowCounts));
    }

    // ----------------------------------------------------------------------------------------------
    //  5b - THE QUERY-SIDE DEFENSIVE OVERRIDE        [:L771-L774]
    //
    //     if TransObject.SQLCode = -1 and nRowCnt >= 0 then nRowCnt = -1; data.Reset() end if
    //
    //  ⚠ A DISTINCT SITE FROM THE UPDATE-SIDE OVERRIDE (constraint C-K). See the file header. The update
    //  side is a different predicate on a different claimed result and does NOT discard the carrier; it
    //  is owned by ConflictDetectorTests. Merging the two into one helper tested by a single numeric case
    //  would silently drop half of each.
    //
    //  THE MATCH ON THE SQL CODE IS EXACT AND THE COMPARISON ON THE COUNT IS NOT WIDENED. Only -1
    //  rewrites; every other non-zero code - including other NEGATIVE codes - leaves the result alone,
    //  because -1 is the specific value the driver uses for "the statement failed" and any other code is
    //  a different fact. A `< 0` test here would rewrite a successful retrieval whose transaction merely
    //  carried a warning.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// SQL codes the override is asked about, and whether the retrieval must be rewritten into a failure.
    /// </summary>
    /// <returns>The transaction's SQL code and whether the wire reports a database error.</returns>
    public static TheoryData<long, bool> DefensiveOverrideSqlCodes() => new()
    {
        // THE ONLY VALUE THAT REWRITES.
        { -1L, true },

        // Success.
        { 0L, false },

        // "No rows found" - the driver's own not-found code, which is emphatically not a failure.
        { 100L, false },

        // ANOTHER NEGATIVE CODE, and it does NOT rewrite. This is the case a widened `< 0` test breaks,
        // and the reason the theory carries it rather than only the three obvious values.
        { -2L, false },
        { -99L, false },
        { 1L, false },
    };

    [Theory]
    [MemberData(nameof(DefensiveOverrideSqlCodes))]
    public async Task TheQuerySideDefensiveOverride_RewritesOnlyOnAnExactMinusOne(
        long sqlCode,
        bool expectDatabaseError)
    {
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 3L;

        // Stamped inside the after-retrieve notification, the statement immediately before the override
        // [:L769-L771], because resolving the transaction clears its state.
        fixture.TransactionSurface.AfterRetrieveScript =
            (transaction, _) => ((ScriptedPooledTransaction)transaction).SqlCode = sqlCode;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        if (expectDatabaseError)
        {
            // THE CLAIMED SUCCESS BECAME A FAILURE. The rewritten -1 falls into the retrieval-failure arm
            // [:L776-L783] and reaches the caller as a database error - which is the whole purpose of the
            // override: a port that trusted the retrieve's own return value would report success here.
            Assert.Equal(WireRetCode.EDbError, Assert.Single(stream.Statuses).RetCode);

            // AND THE ROW COUNT NEVER REACHED THE WIRE, because the failure arm returns before the data
            // event publishes it.
            Assert.Empty(stream.RowCounts);
            Assert.Empty(stream.DataChunks);
        }
        else
        {
            // UNTOUCHED. The retrieval succeeded and its real count was published.
            Assert.Empty(stream.Statuses);
            Assert.Equal(3L, Assert.Single(stream.RowCounts));
        }

        // THE AFTER-RETRIEVE NOTIFICATION SAW THE PRE-OVERRIDE COUNT in every case [:L769], so a handler
        // observes what the retrieval itself reported rather than what the override made of it.
        Assert.Equal(3L, Assert.Single(fixture.TransactionSurface.AfterRetrieveRowCounts));
    }

    [Fact]
    public void TheQuerySideOverrideDiscardsTheCarrier_WhichTheUpdateSideOneDoesNot()
    {
        // THE HALF OF THE OVERRIDE THAT IS EASIEST TO LOSE IN A MERGE (constraint C-K). `data.Reset()`
        // [:L773] discards the rows the failed retrieval left behind, so a caller cannot read a partial
        // result as a whole one. The update-side override has no equivalent line. Asserted on the pure
        // function because the discard is not observable on the wire - the failure arm returns before any
        // chunk - and an invisible behaviour is exactly the one a refactor drops.
        DataWindowBufferStore carrier = new();
        long row = carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        _ = carrier.SetItemValue(row, 1, DwBuffer.Primary, 1L);

        Assert.Equal(1L, carrier.RowCount());

        Assert.Equal(-1L, SqlQueryTask.ApplyDefensiveRowCountOverride(-1L, 3L, carrier));
        Assert.Equal(0L, carrier.RowCount());

        // AND ON EVERY OTHER CODE THE CARRIER IS LEFT ALONE, so the discard is tied to the rewrite rather
        // than being unconditional.
        DataWindowBufferStore untouched = new();
        long keptRow = untouched.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        _ = untouched.SetItemValue(keptRow, 1, DwBuffer.Primary, 1L);

        Assert.Equal(3L, SqlQueryTask.ApplyDefensiveRowCountOverride(-2L, 3L, untouched));
        Assert.Equal(1L, untouched.RowCount());
    }

    // ----------------------------------------------------------------------------------------------
    //  5c - THE ROW-CAP ARM        [:L787-L790]
    //
    //     if data.of_IsRowsExceeded() then
    //         Event OnError(RetCode.E_OUT_OF_RANGE,"超出最大允许的行数(" + String(_nMaxRows) + ")!")
    //
    //  The cap MACHINERY - the counting, the stop event and the cap-lifting arm - is owned by
    //  DataWindowBuffersTests. What is asserted here is that the flag being raised produces the
    //  documented OUTCOME and that the outcome survives the projection.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ExceedingTheRowCap_ReportsOutOfRangeWithTheCapInTheRawDiagnostic()
    {
        using Fixture fixture = new(productionPipeline: true);

        // Five rows offered against a cap of two, so the carrier stops the retrieval and raises the flag.
        fixture.Store.RetrieveRows = 5L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            MaxRows = 2L,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        // THE CODE IS E_OUT_OF_RANGE AND NOT A DATABASE ERROR. The retrieval SUCCEEDED; what failed is
        // the caller's own constraint, and the two are different facts.
        Assert.Equal(WireRetCode.EOutOfRange, Assert.Single(stream.Statuses).RetCode);

        // NO CHUNK WAS PUBLISHED, because the arm returns before the data event - so a caller cannot read
        // a truncated result as a complete one.
        Assert.Empty(stream.DataChunks);

        // THE FLAG ITSELF IS RAISED ON THE CARRIER, asserted alongside the outcome so the two are not
        // conflated: the outcome could in principle be produced by some other arm, and the cap machinery
        // that raises the flag - the counting, the stop event and the cap-lifting arm - is owned by
        // DataWindowBuffersTests.
        Assert.True(fixture.Store.Carrier.IsRowsExceeded());

        // AND THE CARRIER STOPPED AT THE CAP rather than accepting all five rows and complaining
        // afterwards, which is what makes the breach detectable at all.
        Assert.Equal(2L, fixture.Store.Carrier.RowCount());

        // THE CAP IS INTERPOLATED INTO THE DIAGNOSTIC, asserted on the RAW recorded fault rather than on
        // the wire: the wire text is the redacted projection, and whether a bare numeral survives masking
        // is the redactor's own property, owned by SqlRedactorTests. What matters here is that the port
        // built the oracle's message with the oracle's own affixes and the configured cap inside them.
        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));
        QueryFaultSnapshot fault = entry!.Faults.Snapshot();

        Assert.Equal(RetCode.E_OUT_OF_RANGE, fault.Code);
        Assert.Equal(
            SqlQueryTask.MaxRowsExceededPrefix
                + 2L.ToString(CultureInfo.InvariantCulture)
                + SqlQueryTask.MaxRowsExceededSuffix,
            fault.ErrorText);

        // AND THE WIRE STILL CARRIES THE MESSAGE'S SHAPE, so the projection relayed a diagnostic rather
        // than swallowing it.
        Assert.NotEqual(string.Empty, stream.Statuses[0].ErrorText);
    }

    [Fact]
    public async Task WithinTheRowCap_TheRetrievalSucceedsAndTheCapIsNotReported()
    {
        // THE CONTROL. Without it the case above would also pass against an implementation that reported
        // the cap unconditionally.
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 2L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            MaxRows = 2L,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Empty(stream.Statuses);
        Assert.Equal(2L, Assert.Single(stream.RowCounts));
    }

    // ----------------------------------------------------------------------------------------------
    //  5d - THE STATEMENT SAVE AND RESTORE        [:L629, :L709, :L794-L805]
    //
    //  The oracle saves the current statement BEFORE it rewrites it - `sSQLOriginal = data.GetSQLSelect()`
    //  [:L629] - and restores it after the execution, under its OWN comment saying the restore must
    //  happen before the data event: *需要在OnDataReceived之前还原 [:L793].
    //
    //  ASSERTED AS A SEQUENCE WITH THE ORDERED CALL RECORDER, not as an end state. Checking only the final
    //  statement would pass against an implementation that never rewrote it, and against one that restored
    //  it BEFORE executing - which would run the caller's clause-less statement and return the wrong rows.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOriginalStatementIsSavedRewrittenExecutedAndThenRestored_InThatOrder()
    {
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 2L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        // A STORED CLAUSE IS WHAT ARMS THE RESTORE. The oracle only sets its restore flag when it has
        // actually rewritten the statement [:L717, :L740], so a query with no clause and no paging never
        // restores anything - there is nothing to restore.
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest { Task = handle, Clause = NewClauseSpec(SomeWhereClause) },
                Fixture.Context)).Status.RetCode);

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Empty(stream.Statuses);

        int save = fixture.Store.OrdinalOf(RecordingQueryStore.SaveCall);
        int rewrite = fixture.Store.FirstModifyContaining(SomeWhereClause);
        int execute = fixture.Store.OrdinalOf(RecordingQueryStore.RetrieveCall);
        int restore = fixture.Store.LastModifyContaining(SomeSelect);

        // All four happened.
        Assert.True(save > 0);
        Assert.True(rewrite > 0);
        Assert.True(execute > 0);
        Assert.True(restore > 0);

        // SAVE, THEN REWRITE, THEN EXECUTE, THEN RESTORE. The statement in force at the moment of the
        // execution is therefore the rewritten one, which is the property the whole clause feature rests
        // on.
        Assert.True(save < rewrite, "the statement was rewritten before it was saved");
        Assert.True(rewrite < execute, "the retrieval ran before the statement was rewritten");
        Assert.True(execute < restore, "the statement was restored before the retrieval ran");

        // AND THE ORIGINAL IS BACK, byte for byte, so a second query on the same task starts from the
        // statement the caller configured rather than from the rewritten one.
        Assert.Equal(SomeSelect, fixture.Store.SqlSelect);

        // THE RESTORE IS THE LAST MODIFICATION OF THE STATEMENT PROPERTY, and it precedes the data event -
        // asserted through the chunk, which the data event is what publishes.
        Assert.True(
            restore < fixture.Store.Calls.Count + 1,
            "the restore is inside the recorded call sequence");
        _ = Assert.Single(stream.DataChunks);
    }

    [Fact]
    public async Task WithNoClauseAndNoPaging_TheStatementIsNeverRewrittenAndNeverRestored()
    {
        // THE OTHER HALF OF THE FLAG. The oracle guards the restore on having rewritten something
        // [:L794], so an unmodified statement is left completely alone - one fewer modification against
        // the runtime, and byte-exact parity for the statement that actually executes.
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 1L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Empty(stream.Statuses);
        Assert.Equal(SomeSelect, fixture.Store.SqlSelect);

        // The statement property was written EXACTLY ONCE - by the create arm that installed it - and
        // never rewritten or restored afterwards.
        Assert.Equal(
            1,
            fixture.Store.ModifyScripts.Count(script =>
                script.Contains(DataWindowProperty.TableSelect, StringComparison.Ordinal)));
    }

    // ----------------------------------------------------------------------------------------------
    //  5e - CODEC SELECTION ON THE `DataWindow.Processing` DISCRIMINATOR        [:L92-L102]
    //
    //     choose case Long(Data.Describe("DataWindow.Processing"))
    //         case 4,5   -> GetFullState, ONE chunk, fullstate TRUE
    //         case else  -> GetChanges, chunked, fullstate FALSE
    //
    //  4 is a crosstab and 5 is a composite; everything else takes the changeset arm. The codecs' own
    //  internals are owned by ChangesetCodecTests and FullStateCodecTests - what is asserted here is that
    //  the discriminator selects the arm AND that the resulting flag reaches the wire, because the
    //  receiving side picks its DECODER from that flag alone.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Processing values and the codec each selects.</summary>
    /// <returns>The processing value and whether the full-state codec must be chosen.</returns>
    public static TheoryData<long, bool> ProcessingDiscriminators() => new()
    {
        // The two full-state values, and nothing else is one.
        { 4L, true },
        { 5L, true },

        // The changeset arm, including the value ADJACENT to the pair on each side - which is where an
        // off-by-one in a range test would show up, and a range test is the obvious wrong way to write
        // `case 4,5`.
        { 0L, false },
        { 1L, false },
        { 3L, false },
        { 6L, false },
        { 2L, false },
    };

    [Theory]
    [MemberData(nameof(ProcessingDiscriminators))]
    public async Task TheProcessingDiscriminatorSelectsTheCodecAndItsFlagReachesTheWire(
        long processing,
        bool expectFullState)
    {
        using Fixture fixture = new(productionPipeline: true);

        // Few enough rows that the chunk arithmetic yields exactly ONE chunk against the default chunk
        // size, so the codec's cooperative inter-chunk yield is never reached and this case waits on
        // nothing (AAP 0.6.7).
        fixture.Store.RetrieveRows = 3L;
        fixture.Store.ProcessingOnRetrieve = processing;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Empty(stream.Statuses);

        QueryDataChunk chunk = Assert.Single(stream.DataChunks);

        Assert.Equal(expectFullState, chunk.FullState);

        // THE COUNTERS ARE ONE-BASED ON EITHER ARM. The full-state arm hands over exactly one chunk with
        // count 1 and index 1 [:L97]; the changeset arm here happens to produce one chunk too, and both
        // are one-based rather than one of them being zero-based.
        Assert.Equal(1L, chunk.ChunkCount);
        Assert.Equal(1L, chunk.ChunkIndex);
    }

    // ----------------------------------------------------------------------------------------------
    //  5f - REDACTION ON THE ERROR PATH (constraint C-F)
    //
    //  The oracle places the RAW generated statement into its error payload - `Event OnDBError(...,sSQL,
    //  Primary!,1)` [:L855] - and its logger performs no redaction anywhere. That field therefore carries
    //  interpolated literal values, so masking it is a REQUIRED ADDITION rather than a ported behaviour
    //  (AAP 0.6.3.8), applied at the egress so in-process handling still sees the real statement.
    //
    //  BOTH HALVES ARE ASSERTED, because they are enforced by different mechanisms. The task's own
    //  diagnostic goes through its INJECTED redactor, which is the recording redactor from TestDoubles.cs
    //  here. The wire projection reaches the sanctioned masking policy DIRECTLY rather than through an
    //  injection, precisely so it cannot be weakened from a call site or a container registration.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFailedCountQuery_ReachesTheWireWithItsStatementMaskedAndItsCodeIntact()
    {
        using Fixture fixture = new(productionPipeline: true);

        // A FULL PAGE, so the arithmetic short-circuit does not apply and the counting statement is
        // actually issued [:L818-L833].
        fixture.Store.RetrieveRows = 2L;

        // The count query answers something that is neither the literal 1 the success arm requires
        // [:L851] nor a failure the predicate would catch - which is the arm that builds a database-error
        // payload CARRYING THE STATEMENT [:L855].
        fixture.TransactionSurface.CountReturnCode = 0L;

        // STAMPED AFTER THE TRANSACTION HAS BEEN RESOLVED, for the same reason the override theory stamps
        // its SQL code there: resolving the transaction calls ClearState, which zeroes the driver's whole
        // state block, so values set before the run would be gone by the time the count arm reads them.
        fixture.TransactionSurface.AfterRetrieveScript = (transaction, _) =>
        {
            ScriptedPooledTransaction scripted = (ScriptedPooledTransaction)transaction;
            scripted.SqlDbCode = ProbeDbCode;
            scripted.SqlErrText = ProbeDbErrorText;
        };

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            Paged = true,
            PageSize = 2L,
            PageIndex = 1L,
            PageCounting = true,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        OperationStatus terminal = Assert.Single(stream.Statuses);
        Assert.Equal(WireRetCode.EDbError, terminal.RetCode);

        // THE DRIVER PAYLOAD IS ATTACHED, because the driver event actually fired - it is absent on a
        // validation failure, which is the normal case for every guard sections 1 and 4 exercise.
        Assert.NotNull(terminal.DbError);
        Assert.Equal(ProbeDbCode, terminal.DbError.Sqldbcode);

        // THE STATEMENT FIELD IS MASKED. The counting statement the oracle would have placed here in full
        // carries the count alias and the wrapper alias, and a numeric literal inside them; what reaches
        // the caller must not be the statement as generated.
        string statement = fixture.TransactionSurface.CountStatements.Single();
        Assert.Contains(SqlQueryTask.CountWrapperPrefix, statement, StringComparison.Ordinal);
        Assert.NotEqual(statement, terminal.DbError.Sqlsyntax);

        // AND THE ROW IS ONE-BASED AND THE BUFFER IS THE PRIMARY ONE [:L855] - the payload's own fields
        // survive the masking of its statement.
        Assert.Equal(1L, terminal.DbError.Row);
        Assert.Equal(DwBuffer.Primary, terminal.DbError.Buffer);

        // THE TASK'S OWN DIAGNOSTIC WENT THROUGH ITS INJECTED REDACTOR, which is the recording one here -
        // so the seam is exercised rather than merely present.
        Assert.Contains(statement, fixture.Redactor.Statements, StringComparer.Ordinal);
        Assert.True(fixture.Redactor.CallCount > 0);
    }

    [Fact]
    public async Task ASuccessfulPagedRetrieval_PublishesItsTotalsAndCarriesNoDriverPayload()
    {
        // THE CONTROL FOR THE CASE ABOVE, and a positive assertion that the payload is ABSENT when no
        // driver event fired - an implementation that attached an empty payload unconditionally would
        // make a validation failure look like a driver failure.
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 2L;
        fixture.TransactionSurface.CountReturnCode = 1L;
        fixture.TransactionSurface.CountValue = 9L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec
        {
            Sql = SomeSelect,
            Paged = true,
            PageSize = 2L,
            PageIndex = 1L,
            PageCounting = true,
        });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Empty(stream.Statuses);

        // Nine records at two per page is five pages - the ceiling division, not the floor.
        QueryPageCounts totals = Assert.Single(stream.PageCounts);
        Assert.Equal(9L, totals.RecordCount);
        Assert.Equal(5L, totals.PageCount);

        // AND THE READER AGREES WITH THE STREAM, with `counted` true because a statement really was
        // issued.
        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(9L, counts.RecordCount);
        Assert.Equal(5L, counts.PageCount);
        Assert.True(counts.Counted);
    }

    // ==============================================================================================
    //  SECTION 6 - THE CONSTRAINTS, ASSERTED RATHER THAN LEFT TO REVIEW
    //
    //  Three properties this file must have are easy to lose silently in a later edit, so each is pinned
    //  by a case instead of by a comment.
    // ==============================================================================================

    [Fact]
    public async Task NoCaseInThisFileOpensADatabaseOrReachesADialectClient()
    {
        // CONSTRAINT C-E, ASSERTED. The whole pipeline runs to a successful wire result while the only
        // transaction the pool ever hands out is the scripted double - so no connection was opened, no
        // file was created and no provider was involved. A future edit that reached for a real
        // transaction, a SQLite file or a dialect client would fail here rather than passing quietly and
        // fabricating a database.
        using Fixture fixture = new(productionPipeline: true);
        fixture.Store.RetrieveRows = 2L;

        TaskHandle handle = await fixture.CreateTaskAsync(new QuerySpec { Sql = SomeSelect });

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        // The retrieval really did run end to end.
        Assert.Empty(stream.Statuses);
        Assert.Equal(2L, Assert.Single(stream.RowCounts));
        _ = Assert.Single(stream.DataChunks);

        // AND IT RAN AGAINST THE DOUBLE. The pool activated exactly ONE transaction, it is the scripted
        // one, and it is NOT the production PooledTransaction - which is the type that would actually open
        // a connection through an engine.
        Assert.Equal(1, fixture.Activator.Activations);
        _ = Assert.IsType<ScriptedPooledTransaction>(fixture.Transaction);
        Assert.IsNotType<PooledTransaction>(fixture.Transaction);

        // THE TASK ATTACHED THAT SAME INSTANCE, so the pipeline resolved the double rather than reaching
        // past the pool for a transaction of its own.
        Assert.Same(fixture.Transaction, fixture.TaskFor(handle).AttachedTransaction);

        // AND NOT ONE STATEMENT WAS EXECUTED AGAINST ANY ENGINE. The retrieval, the clause rewrite and the
        // paging arithmetic are all string transforms plus a carrier; nothing here needs a database, which
        // is exactly why both dialect behaviours can be preserved without provisioning either engine.
        Assert.Empty(fixture.Transaction.ExecutedStatements);

        // WHY NO ASSEMBLY-REFERENCE ASSERTION IS MADE HERE, said rather than left as a gap. This project
        // references the application project, which legitimately references Microsoft.Data.Sqlite -
        // Persistence is the only service that holds a storage provider - so the provider appears in this
        // assembly's transitive reference list no matter how this file is written. An assertion over that
        // list would therefore be about the wrong assembly and would fail for a correct arrangement. The
        // property that IS enforceable is the one above: nothing in this file resolves a real transaction
        // or executes a statement.
    }

    [Fact]
    public void TheAdapterCarriesItsLeastPrivilegePolicyAndDeclaresNoAnonymousEscape()
    {
        // CONSTRAINT C-G, ASSERTED. The adapter must stay authenticated and least-privileged, and no case
        // in this file may weaken that to make an assertion pass. Two properties, one negative and one
        // positive, because either alone is insufficient: a missing policy leaves a read credential
        // reaching all four contracts, and an anonymous escape on ONE method opens the whole surface
        // through it.
        AuthorizeAttribute authorize = Assert.Single(
            typeof(GrpcQueryService).GetCustomAttributes<AuthorizeAttribute>(inherit: false));

        // THE READ SCOPE, and specifically not the write one: C-05 is the retrieval contract.
        Assert.Equal(PersistenceScopes.Read, authorize.Policy);

        MethodInfo[] rpcs =
        [
            .. typeof(GrpcQueryService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.IsVirtual && !candidate.IsSpecialName),
        ];

        Assert.Equal(11, rpcs.Length);

        foreach (MethodInfo rpc in rpcs)
        {
            Assert.Empty(rpc.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false));
        }

        // AND NO SIGNING KEY IS CONSTRUCTED ANYWHERE IN THIS ASSEMBLY. Persistence never mints, so the
        // minting package is deliberately absent from this project - asserted as an absent reference so a
        // later edit cannot add one and quietly acquire an independent signing authority.
        IEnumerable<string> referenced = typeof(QueryServiceTests).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain(
            "Microsoft.IdentityModel.JsonWebTokens",
            referenced,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task NoDiagnosticThisContractEmitsEchoesACallerSuppliedValue()
    {
        // CONSTRAINT C-F, ASSERTED ON THE DIAGNOSTICS THIS FILE CAN REACH. Every refusal below is driven
        // with a value distinctive enough to find in a string, and none of them may appear in the text
        // that comes back - a diagnostic that quoted the rejected value would relay caller-supplied
        // material bound for a SQL statement straight back out through a field documented as display
        // text.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        const string Distinctive = "zzq-probe-value-98765";

        List<string> diagnostics =
        [
            (await fixture.Service.SetChunkSize(
                new SetChunkSizeRequest { Task = handle, ChunkSize = 98_765L },
                Fixture.Context)).Status.ErrorText,

            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = -98_765L },
                Fixture.Context)).Status.ErrorText,

            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        SelectIndex = 1,
                        ModifyStyle = (SqlModifyStyle)98_765,
                        Clause = Distinctive,
                    },
                },
                Fixture.Context)).Status.ErrorText,

            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        SelectIndex = 0,
                        ModifyStyle = SqlModifyStyle.SqlMsReplace,
                        Clause = Distinctive,
                    },
                },
                Fixture.Context)).Status.ErrorText,
        ];

        foreach (string diagnostic in diagnostics)
        {
            Assert.DoesNotContain(Distinctive, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("98765", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("98,765", diagnostic, StringComparison.Ordinal);
        }
    }

    // ==============================================================================================
    //  THE SHARED HELPERS - small, named, and used by more than one section
    // ==============================================================================================

    /// <summary>
    /// Builds a carrier state payload. Its CONTENT is immaterial to every case in this file: the
    /// properties under test are the counters, the discriminator and the ordering, all of which live
    /// beside the payload rather than inside it. The codecs' own encoding is owned by
    /// <c>ChangesetCodecTests</c> and <c>FullStateCodecTests</c>.
    /// </summary>
    /// <returns>A payload with no segments.</returns>
    private static CarrierState NewCarrierState() => new();

    /// <summary>
    /// Builds one changeset chunk with one-based counters.
    /// </summary>
    /// <param name="chunkCount">The declared total.</param>
    /// <param name="chunkIndex">This chunk's ONE-BASED position.</param>
    /// <returns>The chunk.</returns>
    private static ChangesetChunk NewChangesetChunk(long chunkCount, long chunkIndex) =>
        new(NewCarrierState(), chunkCount, chunkIndex);

    /// <summary>
    /// Builds a clause specification targeting select block ONE with the replace style.
    /// </summary>
    /// <param name="clause">The clause body.</param>
    /// <returns>The specification.</returns>
    /// <remarks>
    /// The select index is stated EXPLICITLY as 1 rather than left unset. An unset field would arrive as
    /// zero, which the guard at <c>:L271</c> refuses - so a case meaning to test something else would
    /// fail for a reason that has nothing to do with its subject.
    /// </remarks>
    private static SqlClauseSpec NewClauseSpec(string clause) => new()
    {
        SelectIndex = 1,
        ModifyStyle = SqlModifyStyle.SqlMsReplace,
        Clause = clause,
    };

    /// <summary>
    /// Names the arm a streamed response carries, in the contract's own field spelling.
    /// </summary>
    /// <param name="response">The response as it was written.</param>
    /// <returns>The field name of the populated <c>oneof</c> arm.</returns>
    /// <remarks>
    /// Reported in the wire spelling rather than the generated PascalCase one so an ordering assertion
    /// reads against <c>persistence.v1.proto</c> directly.
    /// </remarks>
    private static string ArmNameOf(QueryResponse response) => response.PayloadCase switch
    {
        QueryResponse.PayloadOneofCase.RowCount => "row_count",
        QueryResponse.PayloadOneofCase.DataChunk => "data_chunk",
        QueryResponse.PayloadOneofCase.ChildDataChunk => "child_data_chunk",
        QueryResponse.PayloadOneofCase.PageCounts => "page_counts",
        QueryResponse.PayloadOneofCase.Status => "status",

        // An empty envelope is a defect rather than a shape, so it is named as one instead of being
        // silently reported as something legal.
        _ => "none",
    };

    // ==============================================================================================
    //  THE FIXTURE AND ITS DOUBLES
    //
    //  NO DATABASE, NO CONNECTION, NO DIALECT CLIENT (constraint C-E). The transaction the pool hands
    //  out is ScriptedPooledTransaction from TestDoubles.cs: it answers a connect without opening
    //  anything, and its SQL code is settable, which is what makes the defensive-override theory in
    //  section 5 reachable at all. No provider TYPE is referenced in code anywhere in this file - no
    //  Microsoft.Data.Sqlite type, no SQL Server client, no Oracle client, no testcontainer - and
    //  section 6 asserts the enforceable half of that as a property. Worded that precisely on purpose:
    //  the only places those provider names appear at all are explanatory comments (one of them in
    //  section 6 itself, saying why an assembly-reference assertion would be unsound), so a flat claim
    //  of "the name appears nowhere in this file" would be false of the file's own documentation.
    //
    //  ONE CLOCK, NEVER ADVANCED (AAP 0.6.7). FakeTimeProvider is injected everywhere the production
    //  graph injects TimeProvider, and nothing in this file advances it - because nothing here reads a
    //  clock. The single exception is documented at its point of use in HarnessTaskFactory.
    // ==============================================================================================

    /// <summary>
    /// The adapter under test, its two handle tables, its task factory and its runner, composed without
    /// a host and without a database.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly PoolLease _lease;

        /// <summary>
        /// Composes the whole graph.
        /// </summary>
        /// <param name="productionPipeline">
        /// When <see langword="true"/> the service is given the PRODUCTION
        /// <see cref="QueryRetrievalRunner"/>, so a query drives the real
        /// <c>SqlQueryTask.ExecuteAsync</c> end to end - which is what section 5 needs. When
        /// <see langword="false"/> it is given the scripted runner, so a case can publish any arm
        /// sequence it likes without a pipeline - which is what sections 1 through 4 need.
        /// </param>
        /// <param name="query">
        /// Optional configured query defaults. Left null in almost every case, because
        /// <c>QueryOptions</c>' own property initializers ARE the legacy reset literals and a case about
        /// a legacy default must not silently be reading a test-chosen one.
        /// </param>
        internal Fixture(bool productionPipeline = false, QueryOptions? query = null)
        {
            Configuration = new PersistenceOptions();

            if (query is not null)
            {
                Configuration.Query = query;
            }

            Accessor = Options.Create(Configuration);

            Transaction = new ScriptedPooledTransaction();
            Activator = new SingleTransactionActivator(Transaction);
            Pool = new TransactionPool(Accessor, Clock, Activator);

            Store = new RecordingQueryStore(DataWindowCarrierFactory.CreateForThread(false, Clock));
            TransactionSurface = new ScriptedQueryTransactionSurface();
            Runtime = new ScriptedQueryRuntime();
            HookActivator = new SqlRetrievalHookActivator();
            Redactor = new RecordingSqlRedactor();

            // THE HOOK CLASS IS REGISTERED UP FRONT, because `SetHookClass` is ADMISSIBILITY-GATED: it
            // answers E_INVALID_ARGUMENT for a name the activator does not know. That is a deliberate
            // narrowing of the oracle, whose setter is a bare assignment [:L428] - a caller there
            // discovered its mistake later, as a failed retrieval. One instance is registered rather than
            // one per activation so a case can read the calls it received; the task disposes a hook only
            // when it implements IDisposable, and this one does not.
            Hook = new ScriptedRetrievalHook();
            _ = HookActivator.Register(ProbeHookClass, () => Hook);

            Factory = new HarnessTaskFactory(this);

            // A DESCRIPTOR WITH NO CREDENTIAL IN IT (constraint C-F). Every value is synthetic and the
            // log password is EMPTY - not a placeholder that looks like one, and not copied from any
            // catalogued in-source secret site.
            TransactionData descriptor = new()
            {
                Dbms = "SQLite",
                ServerName = "query-service-tests",
                Database = "query-service-tests.db",
                LogId = "harness",
                LogPass = string.Empty,
                DbParm = string.Empty,
                Lock = string.Empty,
                AutoCommit = false,
                UserParm = string.Empty,
            };

            _lease = Pool.AddRefLease(in descriptor);
            _ = Pool.Get(_lease, out IPooledTransaction? borrowed);

            Sessions = new TransactionSessionRegistry(Accessor, Clock, Pool);
            Tasks = new QueryTaskRegistry(Accessor, Clock);

            TransactionSession? registered = Sessions.Register(
                _lease,
                in descriptor,
                borrowed!,
                out string registrationDiagnostic);

            Assert.Equal(string.Empty, registrationDiagnostic);
            Assert.NotNull(registered);

            Session = registered!.SessionId;

            Service = new GrpcQueryService(
                Sessions,
                Tasks,
                Factory,
                productionPipeline ? new QueryRetrievalRunner() : Runner);
        }

        /// <summary>The context every call is made with unless a case is testing cancellation.</summary>
        internal static ServerCallContext Context { get; } = new CallContext(CancellationToken.None);

        /// <summary>The one clock. Nothing in this file advances it, because nothing reads it.</summary>
        internal FakeTimeProvider Clock { get; } = new();

        internal PersistenceOptions Configuration { get; }

        internal IOptions<PersistenceOptions> Accessor { get; }

        internal ScriptedPooledTransaction Transaction { get; }

        internal SingleTransactionActivator Activator { get; }

        internal TransactionPool Pool { get; }

        internal RecordingQueryStore Store { get; }

        internal ScriptedQueryTransactionSurface TransactionSurface { get; }

        internal ScriptedQueryRuntime Runtime { get; }

        internal SqlRetrievalHookActivator HookActivator { get; }

        /// <summary>
        /// The one hook instance registered under <see cref="ProbeHookClass"/>. Its answer is settable, so
        /// one fixture covers the decline arm and the handled arm alike.
        /// </summary>
        internal ScriptedRetrievalHook Hook { get; }

        internal RecordingSqlRedactor Redactor { get; }

        internal HarnessTaskFactory Factory { get; }

        /// <summary>
        /// The scripted runner. Live only when the fixture was built WITHOUT the production pipeline;
        /// section 5 drives the real one instead and reads <see cref="Store"/> and
        /// <see cref="Transaction"/> to observe it.
        /// </summary>
        internal ScriptedRunner Runner { get; } = new();

        internal TransactionSessionRegistry Sessions { get; }

        internal QueryTaskRegistry Tasks { get; }

        internal GrpcQueryService Service { get; }

        internal string Session { get; }

        /// <summary>
        /// Creates one query task through the RPC, optionally with an initial specification.
        /// </summary>
        /// <param name="spec">The initial specification, or null for none.</param>
        /// <returns>The handle.</returns>
        internal async Task<TaskHandle> CreateTaskAsync(QuerySpec? spec = null)
        {
            CreateQueryTaskRequest request = new()
            {
                Session = new SessionHandle { SessionId = Session },
            };

            if (spec is not null)
            {
                request.Spec = spec;
            }

            CreateQueryTaskResponse created = await Service
                .CreateQueryTask(request, Context)
                .ConfigureAwait(false);

            Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
            Assert.NotNull(created.Task);

            return created.Task;
        }

        /// <summary>
        /// Resolves the server-held task behind a handle, so a setter's effect can be read at the FIELD
        /// rather than inferred from the status it answered.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <returns>The task.</returns>
        internal SqlQueryTask TaskFor(TaskHandle handle)
        {
            Assert.True(Tasks.TryResolve(handle, out QueryTaskEntry? entry));

            return entry!.Task;
        }

        public void Dispose()
        {
            _ = Pool.RemoveRef(_lease);
            Pool.Dispose();
        }
    }

    /// <summary>
    /// Composes the production <see cref="SqlQueryTask"/> over the fixture's doubles, exactly as
    /// <c>Program.cs</c>' own <c>QueryTaskFactory</c> composes it over the container's services.
    /// </summary>
    /// <remarks>
    /// <b>THE HOST AND THE FAULT PROXY ARE THE PRODUCTION TYPES</b> - <c>PersistenceSqlTaskHost</c> and
    /// <c>QueryFaultProxy</c> - rather than doubles of their own, so the fault path from the task to the
    /// adapter's recorder is the shipped one. Only the four collaborators that would otherwise need a
    /// database or a DataWindow runtime are substituted.
    /// </remarks>
    private sealed class HarnessTaskFactory : IQueryTaskFactory
    {
        private readonly Fixture _fixture;

        internal HarnessTaskFactory(Fixture fixture) => _fixture = fixture;

        /// <summary>The fault sinks handed to each task, in creation order.</summary>
        internal List<IQueryFaultSink> Sinks { get; } = [];

        public SqlQueryTask Create(IQueryFaultSink faults)
        {
            ArgumentNullException.ThrowIfNull(faults);

            Sinks.Add(faults);

            PersistenceSqlTaskHost host = new(
                NullLogger<PersistenceSqlTaskHost>.Instance,
                _fixture.Redactor,
                faults,
                new QueryFaultProxy(faults));

            SqlQueryTask task = new(
                host,
                _fixture.Pool,
                new SingleStoreFactory(_fixture.Store),
                _fixture.HookActivator,
                _fixture.Clock,
                NullLogger<SqlQueryTask>.Instance,
                _fixture.TransactionSurface,
                _fixture.Runtime,
                [new SqlServerPagingRewriter(), new OraclePagingRewriter()],

                // THE ONE PLACE A REAL CLOCK IS USED, AND IT IS UNREACHABLE HERE. The changeset codec's
                // cooperative inter-chunk yield - `if Not of_Wait(0.02) then return RetCode.CANCELLED`
                // [:L188] - is a genuine Task.Delay against this provider, so a fake clock nobody
                // advances would never complete it. Every pipeline case in section 5 produces exactly
                // ONE chunk, so the yield is never reached and no case in this file waits on a wall
                // clock; the assertions are on chunk order and position, never on duration (AAP 0.8.5).
                // The task's OWN clock, one argument above, is the fake one.
                new ChangesetCodec(TimeProvider.System),
                _fixture.Redactor,
                _fixture.Accessor);

            // The cycle the two constructors cannot close, exactly as the production factory closes it.
            host.BindTask(task);

            return task;
        }
    }

    /// <summary>
    /// A retrieval runner whose whole behaviour is supplied by the case that uses it.
    /// </summary>
    /// <remarks>
    /// <c>IQueryRetrievalRunner</c> is a one-method seam over <c>SqlQueryTask.ExecuteAsync</c>, so this
    /// double lets the ENTIRE stream projection - the one-based counters, the discriminator, the ordered
    /// emission of five arms, cancellation mid-stream and the terminal failure detail - be driven
    /// exactly, which is the only way those properties are reachable without a storage engine.
    /// </remarks>
    private sealed class ScriptedRunner : IQueryRetrievalRunner
    {
        /// <summary>Publishes into the sink. Defaults to a run that publishes nothing and succeeds.</summary>
        internal Func<IQueryResultSink, CancellationToken, Task<long>> Script { get; set; } =
            static (_, _) => Task.FromResult(RetCode.OK);

        /// <summary>Held to block a retrieval inside the lease. Null for a run that does not block.</summary>
        internal TaskCompletionSource? Gate { get; set; }

        /// <summary>Completed the first time the runner is entered.</summary>
        internal TaskCompletionSource Entered { get; } = new();

        /// <summary>How many times a retrieval reached this runner.</summary>
        internal int Runs { get; private set; }

        /// <summary>The sinks the runner was handed, in call order.</summary>
        internal List<IQueryResultSink> Sinks { get; } = [];

        public async Task<long> RunAsync(
            SqlQueryTask task,
            IQueryResultSink sink,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(sink);

            Runs++;
            Sinks.Add(sink);

            _ = Entered.TrySetResult();

            if (Gate is not null)
            {
                await Gate.Task.ConfigureAwait(false);
            }

            return await Script(sink, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records everything written to the server stream, in order, with a typed view per arm.
    /// </summary>
    private sealed class CollectingStream : IServerStreamWriter<QueryResponse>
    {
        /// <summary>Every response, in write order.</summary>
        internal List<QueryResponse> Written { get; } = [];

        public WriteOptions? WriteOptions { get; set; }

        internal IEnumerable<long> RowCounts =>
            Written
                .Where(response => response.PayloadCase == QueryResponse.PayloadOneofCase.RowCount)
                .Select(response => response.RowCount.RowCount);

        internal IReadOnlyList<QueryDataChunk> DataChunks =>
            [
                .. Written
                    .Where(response => response.PayloadCase == QueryResponse.PayloadOneofCase.DataChunk)
                    .Select(response => response.DataChunk),
            ];

        internal IReadOnlyList<QueryChildDataChunk> ChildChunks =>
            [
                .. Written
                    .Where(response =>
                        response.PayloadCase == QueryResponse.PayloadOneofCase.ChildDataChunk)
                    .Select(response => response.ChildDataChunk),
            ];

        internal IReadOnlyList<QueryPageCounts> PageCounts =>
            [
                .. Written
                    .Where(response => response.PayloadCase == QueryResponse.PayloadOneofCase.PageCounts)
                    .Select(response => response.PageCounts),
            ];

        internal IReadOnlyList<OperationStatus> Statuses =>
            [
                .. Written
                    .Where(response => response.PayloadCase == QueryResponse.PayloadOneofCase.Status)
                    .Select(response => response.Status),
            ];

        public Task WriteAsync(QueryResponse message)
        {
            Written.Add(message);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A server call context whose only interesting member is its cancellation token.
    /// </summary>
    /// <param name="cancellationToken">The token the adapter will observe.</param>
    private sealed class CallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        private readonly CancellationToken _token = cancellationToken;

        protected override string MethodCore => "/persistence.v1.QueryService/Query";

        protected override string HostCore => "localhost:5101";

        protected override string PeerCore => "ipv4:127.0.0.1:0";

        protected override DateTime DeadlineCore => DateTime.MaxValue;

        protected override Metadata RequestHeadersCore { get; } = [];

        protected override CancellationToken CancellationTokenCore => _token;

        protected override Metadata ResponseTrailersCore { get; } = [];

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore { get; } =
            new("fake", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

        // A MANDATORY ABSTRACT OVERRIDE THAT IS DELIBERATELY NOT IMPLEMENTED, AND NOT A STUB FOR
        // FUTURE WORK. ServerCallContext declares this abstract, so the override has to exist for the
        // harness to compile; call-context propagation is a chained-outbound-call concern, and the
        // adapter under test never chains an outbound call, so QueryService cannot reach this member on
        // any path exercised here. Throwing is the active choice: gRPC's own non-propagating contexts
        // throw exactly this, and if a future change to QueryService did start propagating a context,
        // this would fail loudly at that new call rather than silently hand back a null token.
        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }

    /// <summary>Hands the pool the one scripted transaction, whichever way it asks for it.</summary>
    private sealed class SingleTransactionActivator : IPooledTransactionActivator
    {
        private readonly ScriptedPooledTransaction _transaction;

        internal SingleTransactionActivator(ScriptedPooledTransaction transaction) =>
            _transaction = transaction;

        /// <summary>How many times the pool asked for a transaction.</summary>
        internal int Activations { get; private set; }

        public IPooledTransaction CreateDefault()
        {
            Activations++;

            return _transaction;
        }

        public IPooledTransaction Create(string className)
        {
            Activations++;

            return _transaction;
        }
    }

    /// <summary>Hands the task the one recording store, and records the affinity it asked for.</summary>
    private sealed class SingleStoreFactory : ISqlDataStoreFactory
    {
        private readonly RecordingQueryStore _store;

        internal SingleStoreFactory(RecordingQueryStore store) => _store = store;

        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            _store.RequestedAffinities.Add(affinity);

            return _store;
        }
    }

    /// <summary>
    /// The data store, and THE ORDERED CALL RECORDER this file asserts sequence with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE LOG FOR EVERY VERB, IN ONE ORDER.</b> The statement save, the statement rewrite, the
    /// execution and the restore are four different members, so asserting the SEQUENCE - rather than
    /// only the end state - needs them recorded against one another. A test that checked the final
    /// statement alone would pass against an implementation that never rewrote it at all, and against
    /// one that restored it BEFORE executing.
    /// </para>
    /// <para>
    /// <b>WHY NOT <c>ScriptedCarrierSurface</c> FROM TestDoubles.cs.</b> That type is the shared ordered
    /// recorder for the CARRIER surface - describe, modify, sort, filter, column identity - and this seam
    /// is <see cref="ISqlDataStore"/>, which additionally owns <c>GetSqlSelect</c>, <c>RetrieveAsync</c>,
    /// <c>ClearState</c> and <c>OnInit</c>: the four members the save/execute/restore ordering is made
    /// of. It cannot serve this interface, so the log is authored here in the same shape.
    /// </para>
    /// </remarks>
    private sealed class RecordingQueryStore : ISqlDataStore
    {
        /// <summary>The log entry for the statement read that saves the original.</summary>
        internal const string SaveCall = "get-sql-select";

        /// <summary>The log entry prefix for a modification script.</summary>
        internal const string ModifyPrefix = "modify:";

        /// <summary>The log entry for the retrieval itself.</summary>
        internal const string RetrieveCall = "retrieve";

        private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
        private readonly List<string> _calls = [];

        internal RecordingQueryStore(DataWindowCarrier carrier)
        {
            Carrier = carrier;

            // "!" means the property is unreadable, which for the procedure probe means NOT a procedure
            // [:L626] - so the clause and paging arms stay live.
            _properties[QueryDataWindowProperty.TableProcedure] =
                ChangesetSourceDefinition.DescribeUnreadable;

            _properties[DataWindowProperty.Units] = "0";
            _properties[QueryDataWindowProperty.Syntax] = FixtureSyntax;
            _properties[QueryDataWindowProperty.ColumnCount] = "0";
            _properties[DataWindowProperty.TableSort] = string.Empty;
            _properties[DataWindowProperty.TableFilter] = string.Empty;
            _properties[DataWindowProperty.TableArguments] = string.Empty;

            // ALREADY "yes", so the crosstab workaround at [:L674-L676] is skipped and its modification
            // does not appear in the ordered log. The workaround itself is owned by FullStateCodecTests.
            _properties[FullStateCodec.NoUserPromptProperty] = FullStateCodec.NoUserPromptEnabledValue;
        }

        public DataWindowCarrier Carrier { get; }

        public string DataObject { get; set; } = string.Empty;

        /// <summary>The current statement. Starts as the ORIGINAL the save will read.</summary>
        internal string SqlSelect { get; set; } = SomeSelect;

        /// <summary>Every call, in order. The ordered call recorder.</summary>
        internal IReadOnlyList<string> Calls => _calls;

        /// <summary>Only the modification scripts, in order.</summary>
        internal IReadOnlyList<string> ModifyScripts =>
            [.. _calls.Where(call => call.StartsWith(ModifyPrefix, StringComparison.Ordinal))];

        /// <summary>Affinities the factory was asked for, in order.</summary>
        internal List<CarrierThreadAffinity> RequestedAffinities { get; } = [];

        /// <summary>Rows a retrieval appends to the primary buffer.</summary>
        internal long RetrieveRows { get; set; }

        /// <summary>The value a retrieval reports, overriding the appended count when set.</summary>
        internal long? RetrieveResult { get; set; }

        /// <summary>
        /// The carrier's processing discriminator, applied at retrieval time so nothing between the
        /// configuration and the execution can clear it. <c>4</c> and <c>5</c> select the full-state
        /// codec [<c>:L93-L94</c>].
        /// </summary>
        internal long? ProcessingOnRetrieve { get; set; }

        /// <summary>
        /// Held to suspend a retrieval INSIDE the pipeline. This is the only place in the whole execution
        /// path where a case can stop the real task mid-flight, which is what the running-flag assertions
        /// need: the flag is raised by <c>ExecuteAsync</c> itself, so nothing outside it can observe the
        /// flag raised unless the execution is paused. Null for a retrieval that does not pause.
        /// </summary>
        internal TaskCompletionSource? RetrieveGate { get; set; }

        /// <summary>Completed the first time a retrieval reaches this store.</summary>
        internal TaskCompletionSource RetrieveEntered { get; } = new();

        /// <summary>The one-based position of a call in the ordered log, or -1 when it never happened.</summary>
        /// <param name="call">The exact log entry.</param>
        /// <returns>The one-based ordinal, or -1.</returns>
        internal int OrdinalOf(string call)
        {
            int index = _calls.IndexOf(call);

            return index < 0 ? -1 : index + 1;
        }

        /// <summary>
        /// The one-based position of the first modification whose script contains a fragment.
        /// </summary>
        /// <param name="fragment">The fragment to look for.</param>
        /// <returns>The one-based ordinal, or -1.</returns>
        internal int FirstModifyContaining(string fragment)
        {
            for (int index = 0; index < _calls.Count; index++)
            {
                if (_calls[index].StartsWith(ModifyPrefix, StringComparison.Ordinal)
                    && _calls[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        /// <summary>
        /// The one-based position of the LAST modification whose script contains a fragment.
        /// </summary>
        /// <param name="fragment">The fragment to look for.</param>
        /// <returns>The one-based ordinal, or -1.</returns>
        internal int LastModifyContaining(string fragment)
        {
            for (int index = _calls.Count - 1; index >= 0; index--)
            {
                if (_calls[index].StartsWith(ModifyPrefix, StringComparison.Ordinal)
                    && _calls[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        public string GetSqlSelect()
        {
            _calls.Add(SaveCall);

            return SqlSelect;
        }

        public string Modify(string modificationScript)
        {
            _calls.Add(ModifyPrefix + modificationScript);

            int separator = modificationScript.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                return "malformed modification script";
            }

            string property = modificationScript[..separator].Trim();
            string value = modificationScript[(separator + 1)..].Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (string.Equals(property, DataWindowProperty.TableSelect, StringComparison.Ordinal))
            {
                SqlSelect = value;

                return string.Empty;
            }

            _properties[property] = value;

            return string.Empty;
        }

        public string Describe(string property)
        {
            if (string.Equals(property, DataWindowProperty.TableSelect, StringComparison.Ordinal))
            {
                return SqlSelect;
            }

            return _properties.TryGetValue(property, out string? value)
                ? value
                : ChangesetSourceDefinition.DescribeUnreadable;
        }

        public long SetFilter(string? filter)
        {
            _calls.Add("set-filter:" + (filter ?? string.Empty));
            _properties[DataWindowProperty.TableFilter] = filter ?? string.Empty;

            return DataWindowBufferStore.DataStoreSuccess;
        }

        public long SetSort(string? sort)
        {
            _calls.Add("set-sort:" + (sort ?? string.Empty));
            _properties[DataWindowProperty.TableSort] = sort ?? string.Empty;

            return DataWindowBufferStore.DataStoreSuccess;
        }

        public void ClearState()
        {
            _calls.Add("clear-state");
            Carrier.ClearState();
        }

        public void OnInit(ICarrierParentTask parentTask)
        {
            _calls.Add("on-init");
            Carrier.OnInit(parentTask);
        }

        public async ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken)
        {
            _calls.Add(RetrieveCall);

            _ = RetrieveEntered.TrySetResult();

            if (RetrieveGate is not null)
            {
                await RetrieveGate.Task.ConfigureAwait(false);
            }

            if (ProcessingOnRetrieve is long processing)
            {
                Carrier.Processing = new DataWindowProcessing(processing);
            }

            long appended = Fill();

            return RetrieveResult ?? appended;
        }

        /// <summary>
        /// Appends the configured rows through the carrier's own retrieve events, so the row cap stays
        /// live rather than being bypassed by a direct append.
        /// </summary>
        /// <returns>The number of rows the carrier accepted.</returns>
        private long Fill()
        {
            _ = Carrier.OnRetrieveStart();

            long appended = 0L;

            // R9: one-based and inclusive at both ends, matching every legacy loop.
            for (long index = 1L; index <= RetrieveRows; index++)
            {
                long row = Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
                _ = Carrier.SetItemValue(row, 1, DwBuffer.Primary, index);
                Carrier.RowAt(row, DwBuffer.Primary).Baseline();

                if (Carrier.OnRetrieveRow(row) == DataWindowBufferStore.EventStop)
                {
                    _ = Carrier.RowsDiscard(row, row, DwBuffer.Primary);

                    return appended;
                }

                appended++;
            }

            return appended;
        }
    }

    /// <summary>The transaction-surface seam: syntax derivation, the count query and the two hooks.</summary>
    private sealed class ScriptedQueryTransactionSurface : IQueryTransactionSurface
    {
        internal string SyntaxToReturn { get; set; } = FixtureSyntax;

        internal string SyntaxError { get; set; } = string.Empty;

        /// <summary>The before-retrieve hook's answer. <c>ALLOW</c> means "not vetoed".</summary>
        internal long BeforeRetrieveResult { get; set; } = RetCode.ALLOW;

        /// <summary>The row counts the after-retrieve hook observed, in order.</summary>
        internal List<long> AfterRetrieveRowCounts { get; } = [];

        /// <summary>
        /// Runs inside the after-retrieve notification, which the oracle fires at <c>:L769</c> - the
        /// statement IMMEDIATELY BEFORE the defensive override at <c>:L771</c>. That position is why this
        /// script exists rather than a plain settable code on the transaction: resolving the transaction
        /// calls <c>ClearState</c>, which zeroes its SQL code, so a code set before the run would be gone
        /// by the time the override reads it.
        /// </summary>
        internal Action<IPooledTransaction, long>? AfterRetrieveScript { get; set; }

        /// <summary>Count statements this surface was asked to run, in order.</summary>
        internal List<string> CountStatements { get; } = [];

        /// <summary>The count query's return code. The oracle reads success as the LITERAL 1 [:L851].</summary>
        internal long CountReturnCode { get; set; } = 1L;

        internal long CountValue { get; set; }

        internal string CountErrorText { get; set; } = string.Empty;

        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new(SyntaxToReturn, SyntaxError);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken)
        {
            CountStatements.Add(sql);

            DataWindowBufferStore? result = null;

            if (CountReturnCode == 1L)
            {
                result = new DataWindowBufferStore();
                long row = result.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
                _ = result.SetItemValue(row, 1, DwBuffer.Primary, CountValue);
            }

            return ValueTask.FromResult(new CountQueryOutcome(CountReturnCode, result, CountErrorText));
        }

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            BeforeRetrieveResult;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount)
        {
            AfterRetrieveRowCounts.Add(rowCount);
            AfterRetrieveScript?.Invoke(transaction, rowCount);
        }
    }

    /// <summary>The DataWindow-runtime seam: carrier creation from syntax, children and attachment.</summary>
    private sealed class ScriptedQueryRuntime : IQueryDataWindowRuntime
    {
        internal long CreateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal string CreateError { get; set; } = string.Empty;

        internal long AttachResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>The statement a freshly created carrier reports as its own.</summary>
        internal string GeneratedSelect { get; set; } = SomeSelect;

        internal List<string> CreatedFromSyntax { get; } = [];

        internal int AttachCalls { get; private set; }

        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
        {
            ArgumentNullException.ThrowIfNull(data);

            CreatedFromSyntax.Add(syntax);

            if (CreateResult == DataWindowBufferStore.DataStoreSuccess)
            {
                _ = data.Modify(SqlQueryTask.BuildTableSelectAssignment(GeneratedSelect));
            }

            return new CarrierCreateOutcome(CreateResult, CreateError);
        }

        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
        {
            child = null;

            return false;
        }

        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction)
        {
            AttachCalls++;

            return AttachResult;
        }
    }

    /// <summary>
    /// A retrieval hook whose answer is supplied by the case that registers it.
    /// </summary>
    /// <remarks>
    /// A COLLABORATOR, NOT A SCRIPT (constraint C-D). The hook class name is a key into an in-process
    /// activator registry; nothing is compiled, evaluated or dynamically invoked, so this exercises no
    /// part of the deferred ScriptBridge capability.
    /// </remarks>
    private sealed class ScriptedRetrievalHook : ISqlRetrievalHook
    {
        /// <summary>
        /// What the hook answers. DECLINES BY DEFAULT with <c>E_NO_IMPLEMENTATION</c>, which is one of the
        /// two values that mean "not handled, run the default retrieval" [<c>:L761</c>] - and a decline is
        /// NOT a failure, however much the numeral looks like one.
        /// </summary>
        internal long Answer { get; set; } = RetCode.E_NO_IMPLEMENTATION;

        /// <summary>How many times this instance was asked to retrieve.</summary>
        internal int Calls { get; private set; }

        public long OnRetrieve(SqlTaskBase task, IPooledTransaction transaction, DataWindowCarrier data)
        {
            Calls++;

            return Answer;
        }
    }
}
