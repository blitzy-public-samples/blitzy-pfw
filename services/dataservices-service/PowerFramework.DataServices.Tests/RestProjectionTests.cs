// =====================================================================================================
//  RestProjectionTests.cs - THE THIN REST PROJECTION, DRIVEN THROUGH THE DEPLOYED PIPELINE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT
//    services/dataservices-service/PowerFramework.DataServices/Endpoints/RestProjectionEndpoints.cs
//    exercised through the in-process host of DataServicesTestHostFactory.cs - so every row below runs
//    the REAL authentication pipeline, the REAL gRPC implementations and the REAL status mapper.
//
//  THE ONE PROPERTY THIS FILE EXISTS FOR
//  ---------------------------------------------------------------------------------------------------
//  gRPC `Aborted` BECOMES HTTP `409`, CARRYING `common.v1.ConflictDetail` UNCHANGED. That is the
//  canonical gRPC-to-HTTP conflict mapping and it is the mechanism behind the whole no-silent-overwrite
//  rule: on an `updatewhereclause` mismatch the caller must be able to decide between retrying and
//  surfacing, and it can only construct a retry if it can see WHICH COLUMN MOVED. The sole updatable
//  DataWindow in the legacy estate carries `updatewhere=1` with ALL SIX columns marked
//  `updatewhereclause=yes` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], so the concurrency check
//  spans all six columns' ORIGINAL values and a payload carrying only current values could not tell a
//  caller anything actionable.
//
//  WHY THAT NEEDED A SUITE OF ITS OWN, MEASURED RATHER THAN ASSUMED
//  Before this file, NOTHING in this assembly drove a conflict through HTTP. The sibling
//  RestProjectionInBandStatusTests drives the IN-BAND map directly with no host at all;
//  RestProjectionStreamBoundTests exercises only the stream collector; ErrorContractTests and
//  ScopeAuthorizationTests reach `/v1/datawindow/update` for its two refusals and never past them. So
//  the 409, its payload, its single upstream attempt, and the correspondence between the route table and
//  the authored contract were all unasserted end to end. Every one of them is asserted here.
//
//  ORACLE ANCHORS - READ ONLY, NEVER EDITED, NEVER BUILT (constraint C-C)
//  ---------------------------------------------------------------------------------------------------
//    ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14
//        The six columns and their declared types - id (number, key and identity), name (char(100)),
//        age (number), address (char(200)), salary (decimal(2)), birth (date) - every one of them
//        `update=yes updatewhereclause=yes`; and :L14 `updatewhere=1 updatekeyinplace=no`.
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208-L210
//        The defensive override that rewrites a CLAIMED SUCCESS into a failure when the transaction's
//        SQL code says otherwise - which is why the conflict outcome code travels VERBATIM here rather
//        than being re-derived from anything.
//    ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs
//        The four-field database-error payload whose statement member is the one field that must not
//        leak (constraint C-F).
//    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L11-L32, :L355, :L357
//        The 22-event chain whose bidirectional projection is deliberately absent, and the validation
//        path's LOCALIZED dialog that this projection must relay unchanged.
//    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//        The 28 hardcoded-Chinese dialog sites that do NOT route through localization, and the
//        byte-measured caret the parse builder renders [:L2402-L2407].
//    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L239
//        The one localized rowselect dialog, counted in the census this file does not duplicate.
//    ws_objects/pfw.shared.pbl.src/retcode.sru
//        The algebra the `retCode` problem member carries. The HTTP status is NEVER derived from a
//        success predicate here, because that algebra is TRI-STATE: `IsSucceeded` tests `>= 0` so
//        PREVENT (1) reads as a success, and `IsFailed` excludes CANCELLED (-2) BY NAME so a
//        cancellation is neither.
//
//  GOVERNING CONSTRAINTS, AND WHERE EACH IS DISCHARGED BELOW
//  ---------------------------------------------------------------------------------------------------
//    AAP 0.1.5 / 0.8.1  Aborted -> 409 with current row state, an explicit retry-or-surface policy and
//                       NO SILENT OVERWRITE.                     -> RestProjectionConflictTests
//    C-A                Consumed only by Gateway; exposes nothing beyond what gateway.v1.yaml declares.
//                                                                -> RestProjectionSurfaceTests
//    C-B                The structured errors that replaced the legacy dialogs survive the projection
//                       unchanged, INCLUDING the localization inconsistency.
//                                                                -> RestProjectionStructuredErrorRelayTests
//    C-D                No REST surrogate for the three bidirectional methods, and no route naming a
//                       deferred capability area.                 -> RestProjectionSurfaceTests
//    C-F                `common.v1.DbError.sqlsyntax` is redacted UPSTREAM and is neither un-redacted,
//                       reconstructed nor logged here; no body carries a credential.
//                                                                -> RestProjectionRedactionTests
//    C-G                Every projected route requires a valid token.
//                                                                -> RestProjectionAuthorizationTests
//    AAP 0.6.7          Table-driven parity matrices as theories with member data.
//                                                                -> the route list and the status matrix
//
//  NAMING CONSTRAINT. This file REFERENCES the preserved SCREAMING_SNAKE constants - `RetCode.E_RETRY`,
//  `RetCode.E_INVALID_HANDLE`, `Categories.CAT_DWSVC` and the rest - and DECLARES none of its own, so it
//  needs no `.editorconfig` section and raises neither CA1707 nor IDE1006.
//
//  NO NETWORK, NO DOCKER, NO DATABASE. The host is in-process, the Persistence edge is a derived double,
//  and no row opens a socket, starts a container or provisions storage. Nothing here waits on a clock.
//
//  NO PERFORMANCE CLAIM IS MADE OR IMPLIED. The repository publishes no service-level agreement, no
//  latency budget, no throughput target and no availability commitment, so none may be asserted
//  (AAP 0.8.5). No row measures elapsed time.
//
// =====================================================================================================

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json;

using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Endpoints;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Localization;
using Xunit;

using KernelRetCode = PowerFramework.Shared.Kernel.RetCode;
using PersistenceBeginSessionRequest = PowerFramework.Contracts.Persistence.V1.BeginSessionRequest;
using PersistenceBeginSessionResponse = PowerFramework.Contracts.Persistence.V1.BeginSessionResponse;
using PersistenceCommandClient =
    PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceClient;
using PersistenceCreateQueryTaskRequest =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskRequest;
using PersistenceCreateQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskResponse;
using PersistenceCreateUpdateTaskRequest =
    PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest;
using PersistenceCreateUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse;
using PersistenceEndSessionRequest = PowerFramework.Contracts.Persistence.V1.EndSessionRequest;
using PersistenceEndSessionResponse = PowerFramework.Contracts.Persistence.V1.EndSessionResponse;
using PersistenceOperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using PersistencePrepareUpdateRequest = PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest;
using PersistencePrepareUpdateResponse = PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse;
using PersistenceQueryClient = PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceClient;
using PersistenceQueryRequest = PowerFramework.Contracts.Persistence.V1.QueryRequest;
using PersistenceQueryResponse = PowerFramework.Contracts.Persistence.V1.QueryResponse;
using PersistenceReleaseQueryTaskRequest =
    PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskRequest;
using PersistenceReleaseQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskResponse;
using PersistenceReleaseUpdateTaskRequest =
    PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest;
using PersistenceReleaseUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskResponse;
using PersistenceTransactionClient =
    PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceClient;
using PersistenceUpdateClient =
    PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceClient;
using PersistenceUpdateCounts = PowerFramework.Contracts.Persistence.V1.UpdateCounts;
using PersistenceUpdateRequest = PowerFramework.Contracts.Persistence.V1.UpdateRequest;
using PersistenceUpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using WireAddExpressionResponse = PowerFramework.Contracts.DataServices.V1.AddExpressionResponse;
using WireExpressionError = PowerFramework.Contracts.DataServices.V1.ExpressionError;
using WireGetRowSelectStateRequest =
    PowerFramework.Contracts.DataServices.V1.GetRowSelectStateRequest;
using WireGetRowSelectStateResponse =
    PowerFramework.Contracts.DataServices.V1.GetRowSelectStateResponse;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireRowValidationError = PowerFramework.Contracts.DataServices.V1.RowValidationError;
using WireSeverity = PowerFramework.Contracts.DataServices.V1.Severity;
using WireStructuredError = PowerFramework.Contracts.DataServices.V1.StructuredError;
using WireUpdateResponse = PowerFramework.Contracts.DataServices.V1.UpdateResponse;

namespace PowerFramework.DataServices.Tests;

// =====================================================================================================
//  SHARED FIXTURE SUPPORT FOR THIS FILE
// =====================================================================================================

/// <summary>
/// The script and the record for contract C-06's <c>PrepareUpdate</c> operation.
/// </summary>
/// <remarks>
/// <para>
/// SEPARATE FROM THE CLIENT BECAUSE OF LIFETIME, not for tidiness. The application registers
/// <c>PersistenceClient</c> per request scope, so a counter held on the client instance would be thrown
/// away with the scope that produced it and every assertion about how many times the upstream was
/// reached would read zero. This object is created by the test, captured by the registration, and shared
/// by every scope - which is exactly how the fixture's own <see cref="ScriptedPersistenceEdge"/> works.
/// </para>
/// <para>
/// THE OUTCOME IS SETTABLE, because a refused prepare is a real arm of the update contract: C-06
/// adjudicates the table descriptor there, and a recorder that could only succeed would make that arm
/// unreachable and blur the difference between "the update contract was rejected" and "the statements
/// were contended".
/// </para>
/// </remarks>
internal sealed class PrepareUpdateRecorder
{
    /// <summary>The outcome the next prepare answers with. <c>OK</c> unless a case states otherwise.</summary>
    internal long Outcome { get; set; } = KernelRetCode.OK;

    /// <summary>
    /// The transport failure the next prepare raises instead of answering, when a case sets one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ONLY SEAM THAT REACHES THE EXCEPTION-SIDE STATUS TABLE, AND WHY IT IS HERE.</b> The
    /// projection has two entirely separate failure paths: an operation that RETURNED a non-success code in
    /// its body, and an operation that THREW. The shared fixture's script drives the first exhaustively and
    /// deliberately offers no raw-failure seam - its own remarks record that a double throwing a bare
    /// transport failure would be modelling the undecodable-trailer path rather than the contract. That is
    /// right for the shared fixture and it leaves the mapper's own arms - what a not-found becomes, what an
    /// internal error becomes, what an unauthenticated call becomes - unreachable, so this seam raises the
    /// failure at the ONE operation this file already owns.
    /// </para>
    /// <para>
    /// IT IS RAISED AFTER THE CALL IS COUNTED, so a case can still assert that the prepare was attempted
    /// exactly once - a mapper row must never be satisfied by an operation that was skipped.
    /// </para>
    /// <para>
    /// AND IT IS RAISED FROM INSIDE THE WORK SCOPE, which is the property that makes these rows worth
    /// running through the host rather than against the table directly: the update task and the session are
    /// acquired before the prepare and released by the enclosing scope on the way out, so every row also
    /// demonstrates that a raised failure leaks neither.
    /// </para>
    /// </remarks>
    internal RpcException? Fault { get; set; }

    /// <summary>How many prepare calls have been answered.</summary>
    internal int Calls { get; private set; }

    /// <summary>The prepare request as it last arrived, so its descriptor can be asserted.</summary>
    internal PersistencePrepareUpdateRequest? LastRequest { get; private set; }

    /// <summary>Records one prepare call and builds its answer, or raises the scripted failure.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <returns>The response carrying <see cref="Outcome"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException"><see cref="Fault"/> is set.</exception>
    internal PersistencePrepareUpdateResponse Record(PersistencePrepareUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Calls++;
        LastRequest = request;

        if (Fault is not null)
        {
            throw Fault;
        }

        return new PersistencePrepareUpdateResponse
        {
            Status = new PersistenceOperationStatus { RetCode = (WireRetCode)(int)Outcome },
        };
    }
}

/// <summary>
/// The Persistence edge this suite needs: the shared scripted double PLUS the one C-06 operation the
/// shared double deliberately leaves to the substituted transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS TYPE HAS TO EXIST, AND WHY IT LIVES HERE RATHER THAN ON THE SHARED FIXTURE.</b>
/// <c>DataServicesTestHostFactory</c> substitutes <c>PersistenceClient</c> with a derived double that
/// scripts C-05's retrieval, C-06's update and the six session-and-task lifecycle operations, and
/// deliberately leaves every other operation to the real client over the substituted transport so that
/// an unscripted call fails LOUDLY. That default is correct - and it means the update path stops one
/// operation short of the upstream: contract C-03's <c>Update</c> sends <c>PrepareUpdate</c> before it
/// sends <c>Update</c>, unconditionally
/// [<c>Grpc/DataWindowService.cs</c>, the prepare block inside the work scope], so with the shared double
/// alone every projected update answers <b>502</b> from the transport and the conflict is unreachable.
/// Measured, not supposed: driving <c>POST /v1/datawindow/update</c> on the unextended fixture answers
/// 502 with <c>UpdateCalls == 0</c>.
/// </para>
/// <para>
/// <b>IT ADDS ONE OPERATION AND CHANGES NOTHING ELSE.</b> Every other override forwards to the SAME
/// <see cref="ScriptedPersistenceEdge"/> the shared fixture owns, by calling the same recording methods,
/// so the call counts, the held-handle sets, the release ordering and the scripted conflict all behave
/// exactly as they do for every sibling suite. The scope helpers - <c>OpenQueryScopeAsync</c> and
/// <c>OpenUpdateScopeAsync</c> - are left to the real implementation for the reason the shared double
/// records: overriding them would have been fewer lines and would have tested nothing.
/// </para>
/// <para>
/// <b>THE PREPARE OUTCOME IS A CONSTRUCTOR ARGUMENT RATHER THAN A FIXED SUCCESS</b>, because a refused
/// prepare is a real arm of the update contract - C-06 adjudicates the table descriptor there - and a
/// double that could only succeed would make that arm unreachable and hide the difference between "the
/// contract was rejected" and "the statements were contended".
/// </para>
/// <para>
/// NO KEY, TOKEN, PASSWORD OR CERTIFICATE APPEARS HERE (constraint C-F). The credential on every
/// outbound call comes from the container's substituted <c>IServiceTokenProvider</c>, which composes an
/// opaque placeholder at run time.
/// </para>
/// </remarks>
internal sealed class PreparingPersistenceEdgeClient : PersistenceClient
{
    private readonly ScriptedPersistenceEdge _edge;
    private readonly PrepareUpdateRecorder _prepare;

    /// <summary>Creates the double over the container's own generated stubs.</summary>
    /// <param name="edge">The script and record this double consults - the fixture's own.</param>
    /// <param name="prepare">
    /// The prepare script and record. Held by the test rather than by this instance, because the
    /// application registers <c>PersistenceClient</c> per request scope and a counter on the instance
    /// would be discarded with the scope that produced it.
    /// </param>
    /// <param name="queryClient">The container's C-05 stub.</param>
    /// <param name="updateClient">The container's C-06 stub.</param>
    /// <param name="commandClient">The container's C-07 stub.</param>
    /// <param name="transactionClient">The container's C-08 stub.</param>
    /// <param name="tokenProvider">The substituted token seam.</param>
    /// <param name="logger">The host's logger for the real client type.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="edge"/> or <paramref name="prepare"/> is <see langword="null"/>.
    /// </exception>
    internal PreparingPersistenceEdgeClient(
        ScriptedPersistenceEdge edge,
        PrepareUpdateRecorder prepare,
        PersistenceQueryClient queryClient,
        PersistenceUpdateClient updateClient,
        PersistenceCommandClient commandClient,
        PersistenceTransactionClient transactionClient,
        IServiceTokenProvider tokenProvider,
        ILogger<PersistenceClient> logger)
        : base(queryClient, updateClient, commandClient, transactionClient, tokenProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(prepare);

        _edge = edge;
        _prepare = prepare;
    }

    /// <summary>Answers the configured prepare outcome. Contract <b>C-06</b>.</summary>
    /// <param name="request">The prepare request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistencePrepareUpdateResponse> PrepareUpdateAsync(
        PersistencePrepareUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_prepare.Record(request));
    }

    /// <summary>Answers the scripted update, or raises the scripted conflict. Contract <b>C-06</b>.</summary>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceUpdateResponse> UpdateAsync(
        PersistenceUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_edge.RecordUpdate(request));
    }

    /// <summary>Answers the scripted retrieval stream. Contract <b>C-05</b>.</summary>
    /// <param name="request">The retrieval request.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The scripted messages, in delivery order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The messages are snapshotted by the edge before the first yield, so a later edit to the script
    /// cannot mutate a stream already in flight - the same guarantee the shared double gives.
    /// </remarks>
    public override async IAsyncEnumerable<PersistenceQueryResponse> QueryAsync(
        PersistenceQueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (PersistenceQueryResponse message in _edge.RecordQuery(request))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return message;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Answers the scripted session begin. Contract <b>C-08</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceBeginSessionResponse> BeginSessionAsync(
        PersistenceBeginSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordBeginSession());
    }

    /// <summary>Answers the scripted session end. Contract <b>C-08</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceEndSessionResponse> EndSessionAsync(
        PersistenceEndSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordEndSession(request));
    }

    /// <summary>Answers the scripted query-task creation. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceCreateQueryTaskResponse> CreateQueryTaskAsync(
        PersistenceCreateQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordCreateQueryTask(request));
    }

    /// <summary>Answers the scripted query-task release. Contract <b>C-05</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceReleaseQueryTaskResponse> ReleaseQueryTaskAsync(
        PersistenceReleaseQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordReleaseQueryTask(request));
    }

    /// <summary>Answers the scripted update-task creation. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceCreateUpdateTaskResponse> CreateUpdateTaskAsync(
        PersistenceCreateUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordCreateUpdateTask(request));
    }

    /// <summary>Answers the scripted update-task release. Contract <b>C-06</b>.</summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public override Task<PersistenceReleaseUpdateTaskResponse> ReleaseUpdateTaskAsync(
        PersistenceReleaseUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_edge.RecordReleaseUpdateTask(request));
    }
}

/// <summary>
/// Captures every operator record the host writes while a projected request is in flight.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY A LOG SINK BELONGS IN A REDACTION SUITE.</b> Constraint C-F and AAP §0.6.6 forbid the
/// relayed <c>sqlsyntax</c> from being echoed OR logged, and the implementation states that obligation
/// in its own words at <c>RenderInBandFailure</c> - "it is never written into <c>detail</c> or into a log
/// record". A body assertion pins only the first half. The half that matters more operationally is the
/// second: a log record is written to a sink this service does not own, is retained far longer than a
/// response, and is read by people who never made the request. Nothing else in this project's test suite
/// pins it for the projection, so it is pinned here.
/// </para>
/// <para>
/// <b>EVERY CATEGORY, NOT JUST THE PROJECTION'S.</b> The sink accepts records from any category, because
/// the property under test is that the statement text is absent from the WHOLE record stream - a leak
/// through the resilience pipeline's category, the client's category or the hosting layer's category
/// would be exactly as damaging as one through the projection's own.
/// </para>
/// <para>
/// <b>REGISTERED AS A PROVIDER, WHICH IS WHY THE FILTER MATTERS.</b> The logging factory applies its
/// configured level filter before a record reaches a provider, so a suite using this sink also raises the
/// minimum level to the most verbose one - see <see cref="RestProjection.RecordOperatorLog"/>. Asserting
/// absence from a stream that was filtered down to nothing would be vacuous, so the consuming test also
/// asserts the stream is NOT empty.
/// </para>
/// </remarks>
internal sealed class ProjectionLogRecorder : ILoggerProvider
{
    /// <summary>The rendered records, newest last.</summary>
    private readonly ConcurrentQueue<string> _records = new();

    /// <summary>Every record rendered as category, level and message text.</summary>
    /// <remarks>
    /// Rendered rather than structured because the assertion is a substring search over everything an
    /// operator could read, and a structured snapshot would force the test to guess which argument slot a
    /// leak arrived in.
    /// </remarks>
    internal ImmutableArray<string> Records => [.. _records];

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to release: the queue is managed and the sink outlives the provider registration by
    /// design, so the captured records remain readable after the host has been disposed.
    /// </remarks>
    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>Records one rendered entry.</summary>
    /// <param name="entry">The rendered entry.</param>
    private void Record(string entry) => _records.Enqueue(entry);

    /// <summary>The per-category logger the factory hands to each writer.</summary>
    /// <param name="owner">The sink the records accumulate in.</param>
    /// <param name="category">The category the factory created this logger for.</param>
    private sealed class CapturingLogger(ProjectionLogRecorder owner, string category) : ILogger
    {
        /// <inheritdoc/>
        /// <remarks>
        /// Scopes are accepted and discarded. A scope's own state is rendered into no record here, so a
        /// value that reached only a scope would escape this sink - which is why the consuming test also
        /// asserts the value is absent from the response body, the only other place it could surface.
        /// </remarks>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoScope.Instance;

        /// <inheritdoc/>
        /// <remarks>
        /// Always enabled, so this sink never narrows what the factory offers it. The factory's own filter
        /// still applies upstream of this call.
        /// </remarks>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="formatter"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The exception is rendered too. A projection that passed the upstream exception object to the
        /// logging abstraction would broadcast every message in its chain, and a driver's text - or a
        /// statement fragment - can be one of them, so the exception's rendering is part of what the
        /// absence assertion covers.
        /// </remarks>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            owner.Record(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{logLevel}] {category} ({eventId.Id}): {formatter(state, exception)} {exception}"));
        }

        /// <summary>The single shared no-op scope.</summary>
        private sealed class NoScope : IDisposable
        {
            /// <summary>The shared instance, since the type carries no state.</summary>
            internal static NoScope Instance { get; } = new();

            /// <inheritdoc/>
            public void Dispose() => GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// One operation the authored contract declares on this service's projected surface.
/// </summary>
/// <param name="Method">The single HTTP method the contract declares, upper-cased.</param>
/// <param name="Path">The route, exactly as the authored contract spells it.</param>
/// <param name="GrpcMethod">
/// The fully-qualified gRPC method it projects, from the operation's <c>x-grpc-method</c> extension. EMPTY
/// on a declaration read from the ROUTE TABLE, because a route carries its address and its verb and does
/// not carry the published extension - the extension is written into the generated document from the
/// operation metadata rather than onto the endpoint, so the two sides are compared by address and verb
/// here and by gRPC method against the generated document separately.
/// </param>
/// <remarks>
/// A record so a theory row is legible in a test-run report as its own route rather than as an index.
/// </remarks>
internal sealed record ProjectedOperationDeclaration(string Method, string Path, string GrpcMethod);

/// <summary>
/// The authored contract, read from disk, and the route table, read from the booted host.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE AUTHORED CONTRACT IS THE AUTHORITY AND IT IS READ RATHER THAN TRANSCRIBED.</b>
/// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> is the public mirror a consumer is
/// handed, and constraint C-A says this projection exposes nothing beyond what it declares. A list of
/// thirty-nine routes copied into this file would be a second source of truth that drifts silently on the
/// first contract change, and the drift would make the correspondence test pass while the product was
/// already broken - so the list is parsed from the document itself.
/// </para>
/// <para>
/// <b>PARSED WITH A LINE SCANNER RATHER THAN A YAML LIBRARY, DELIBERATELY.</b> The dependency inventory
/// carries exactly five test packages and no YAML parser, and adding a sixth would fail restore outright
/// because central package management holds no version for it. The scan is narrow enough to be safe: it
/// reads only the mapping keys of the top-level <c>paths</c> block and the <c>x-grpc-method</c> scalar
/// inside each operation, both at fixed indentation in this authored file, and it REFUSES rather than
/// returning a short list when it finds nothing - so a structural change to the document breaks this
/// suite loudly instead of quietly reducing what it checks.
/// </para>
/// </remarks>
internal static class RestProjectionContract
{
    /// <summary>The route prefix every projected operation on this service sits under.</summary>
    internal const string ProjectedPrefix = "/v1/datawindow";

    /// <summary>The marker that identifies the repository root, as every locator here uses.</summary>
    private const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>The authored contract, relative to the repository root.</summary>
    private const string ContractPath = "shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml";

    /// <summary>The document member that opens the path mapping.</summary>
    private const string PathsKey = "paths:";

    /// <summary>The operation extension that names the gRPC method an operation projects.</summary>
    private const string GrpcMethodExtension = "x-grpc-method:";

    /// <summary>The operation keys an OpenAPI path item may declare, as the document spells them.</summary>
    /// <remarks>
    /// The closed set from the specification. A path item may carry keys that are NOT operations -
    /// <c>parameters</c>, <c>summary</c>, <c>description</c>, <c>servers</c> - at the same indent, so the
    /// scan matches against this set rather than against "any key at operation level".
    /// </remarks>
    private static readonly HashSet<string> HttpMethodKeys = new(StringComparer.Ordinal)
    {
        "get",
        "put",
        "post",
        "delete",
        "options",
        "head",
        "patch",
        "trace",
    };

    /// <summary>
    /// The three gRPC methods this projection deliberately does not carry, spelled as the contract spells
    /// a method.
    /// </summary>
    /// <remarks>
    /// ALL THREE ARE BIDIRECTIONAL, and two of them are INVERTED - the server asks and the client answers
    /// - so neither has a request/response direction to project at all. <c>EventChain</c> carries an
    /// ordered conversation whose validation-error handler READS AND CLEARS the code the preceding
    /// item-change event stashed [<c>se_cst_dw.sru:L89-L96</c>], and part of its ordering is encoded in
    /// the topic STRING itself, so independent REST requests would lose both the order and the tri-valued
    /// veto. A PARTIAL projection would be worse than none: a consumer would receive part of an ordered
    /// chain with no way to know what it had missed.
    /// </remarks>
    internal static readonly string[] UnprojectedGrpcMethods =
    [
        "dataservices.v1.DataWindowService/EventChain",
        "dataservices.v1.ColumnExpressionService/InvokeMethodChannel",
        "dataservices.v1.ColumnExpressionService/TraceChannel",
    ];

    /// <summary>Every projected operation the authored contract declares, in document order.</summary>
    /// <remarks>
    /// Read once and cached: the file cannot change during a run, and the scan would otherwise repeat for
    /// every theory row.
    /// </remarks>
    internal static IReadOnlyList<ProjectedOperationDeclaration> Declared { get; } = ReadDeclared();

    /// <summary>Theory rows over every declared projected operation.</summary>
    /// <returns>One row per operation: its method, its path and the gRPC method it projects.</returns>
    /// <remarks>
    /// ENUMERATED FROM THE CONTRACT RATHER THAN LISTED, so a route added to the projection later CANNOT
    /// be silently unauthenticated: the moment it is declared it acquires a row here.
    /// </remarks>
    public static TheoryData<string, string, string> DeclaredOperations()
    {
        TheoryData<string, string, string> rows = [];

        foreach (ProjectedOperationDeclaration declaration in Declared)
        {
            rows.Add(declaration.Method, declaration.Path, declaration.GrpcMethod);
        }

        return rows;
    }

    /// <summary>Reads the route patterns the booted host actually maps under the projected prefix.</summary>
    /// <param name="host">The booted host.</param>
    /// <returns>Each mapped route as its declared method and its pattern.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE ROUTE TABLE, NOT THE GENERATED DOCUMENT. A declaration is only real once it is on the route:
    /// the generated document is produced FROM this table, so reading the table is what makes the
    /// correspondence with the authored contract a statement about the running service rather than about
    /// its own description of itself.
    /// </remarks>
    internal static IReadOnlyList<ProjectedOperationDeclaration> Mapped(DataServicesTestHostFactory host)
    {
        ArgumentNullException.ThrowIfNull(host);

        List<ProjectedOperationDeclaration> mapped = [];

        foreach (RouteEndpoint endpoint in host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>())
        {
            if (endpoint.RoutePattern.RawText is not string pattern
                || !pattern.StartsWith(ProjectedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                ?? [])
            {
                mapped.Add(new ProjectedOperationDeclaration(
                    method.ToUpperInvariant(),
                    pattern,
                    GrpcMethod: string.Empty));
            }
        }

        return mapped;
    }

    /// <summary>
    /// Reads the generated document's <c>x-grpc-method</c> extension for every projected operation.
    /// </summary>
    /// <param name="document">The generated document, already parsed.</param>
    /// <returns>The gRPC methods, one per projected operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    internal static IReadOnlyList<string> GeneratedGrpcMethods(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<string> methods = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith(ProjectedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Value.TryGetProperty(
                        GrpcMethodExtension.TrimEnd(':'),
                        out JsonElement grpcMethod)
                    && grpcMethod.GetString() is string named)
                {
                    methods.Add(named);
                }
            }
        }

        return methods;
    }

    /// <summary>Resolves a repository-relative path to an absolute one.</summary>
    /// <param name="repositoryRelativePath">The path, with forward slashes.</param>
    /// <returns>The absolute path.</returns>
    internal static string Resolve(string repositoryRelativePath)
    {
        ArgumentNullException.ThrowIfNull(repositoryRelativePath);

        return Path.Combine(
            RepositoryRoot(),
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Scans the authored contract for its projected operations.</summary>
    /// <returns>The declarations, in document order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The document could not be located, carries no path mapping, or declares no projected operation.
    /// </exception>
    private static IReadOnlyList<ProjectedOperationDeclaration> ReadDeclared()
    {
        string contract = Resolve(ContractPath);

        List<ProjectedOperationDeclaration> declared = [];
        bool insidePaths = false;
        string? currentPath = null;
        string? currentMethod = null;

        foreach (string line in File.ReadLines(contract))
        {
            if (line.Length == 0)
            {
                continue;
            }

            // A top-level key. `paths:` opens the mapping and any other one closes it, which is what
            // bounds the scan to the block that carries routes.
            if (!char.IsWhiteSpace(line[0]))
            {
                insidePaths = line.StartsWith(PathsKey, StringComparison.Ordinal);
                currentPath = null;
                currentMethod = null;

                continue;
            }

            if (!insidePaths)
            {
                continue;
            }

            // A path key: exactly two spaces of indent, a leading slash, and a trailing colon.
            if (line.StartsWith("  /", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith(':'))
            {
                currentPath = line.Trim()[..^1];
                currentMethod = null;

                continue;
            }

            if (currentPath is null)
            {
                continue;
            }

            // An operation key: exactly four spaces of indent and one of the HTTP methods an OpenAPI path
            // item may declare. MATCHED AGAINST THE CLOSED SET RATHER THAN AGAINST "any key at this
            // indent", because a path item may also carry a `parameters` list at the same level - six of
            // them do - and treating that as an operation would attribute the following operation's
            // `x-grpc-method` to a method name that does not exist.
            if (line.StartsWith("    ", StringComparison.Ordinal)
                && !line.StartsWith("     ", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith(':')
                && HttpMethodKeys.Contains(line.Trim()[..^1]))
            {
                currentMethod = line.Trim()[..^1].ToUpperInvariant();

                continue;
            }

            if (currentMethod is null
                || line.Trim() is not string trimmed
                || !trimmed.StartsWith(GrpcMethodExtension, StringComparison.Ordinal))
            {
                continue;
            }

            if (currentPath.StartsWith(ProjectedPrefix, StringComparison.Ordinal))
            {
                declared.Add(new ProjectedOperationDeclaration(
                    currentMethod,
                    currentPath,
                    trimmed[GrpcMethodExtension.Length..].Trim()));
            }

            currentMethod = null;
        }

        return declared.Count > 0
            ? declared
            : throw new InvalidOperationException(
                $"'{contract}' declared no operation under '{ProjectedPrefix}'. The authored contract is "
                + "the authority for this projection's surface, so an empty read is a broken locator or a "
                + "restructured document rather than an empty projection - and returning nothing would "
                + "make every correspondence assertion in this suite pass by finding nothing to check.");
    }

    /// <summary>Walks up from the embedded anchor to the repository root.</summary>
    /// <returns>The root.</returns>
    /// <exception cref="InvalidOperationException">No ancestor carries the marker.</exception>
    /// <remarks>
    /// Starts at the anchor the build embeds so the walk still works when the test output sits outside the
    /// checkout, then verifies the marker exactly as every other locator in this project does. See
    /// <c>TestRepositoryRoot</c>.
    /// </remarks>
    private static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, RepositoryRootMarker)))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            $"No ancestor of '{TestRepositoryRoot.SearchStart}' carries {RepositoryRootMarker}, so the "
            + "authored contract cannot be located. See TestRepositoryRoot for the anchor this walk "
            + "starts from.");
    }
}


/// <summary>
/// The host this suite drives, and the reading helpers every case shares.
/// </summary>
/// <remarks>
/// <para>
/// ONE HOST BUILDER, so no case can accidentally exercise a different composition from its neighbours -
/// which is the same reason the projection has one status mapper rather than thirty-nine.
/// </para>
/// <para>
/// EVERY HELPER READS, AND NONE ASSERTS ON BEHALF OF A CASE. A helper that folded an assertion in would
/// make a failure report the helper's line instead of the property that broke.
/// </para>
/// </remarks>
internal static class RestProjection
{
    /// <summary>The projected update, where the concurrency conflict is answered.</summary>
    internal const string UpdateRoute = "/v1/datawindow/update";

    /// <summary>The projected retrieval - a server stream rendered as an ordered collection.</summary>
    internal const string RetrieveRoute = "/v1/datawindow/retrieve";

    /// <summary>The four headless state reads, which share one in-band failure for an unknown handle.</summary>
    internal const string RowSelectStateRoute = "/v1/datawindow/row-select/state";

    /// <summary>The column-sort state read.</summary>
    internal const string ColumnSortStateRoute = "/v1/datawindow/column-sort/state";

    /// <summary>The drop-down-search state read.</summary>
    internal const string DropDownSearchStateRoute = "/v1/datawindow/drop-down-search/state";

    /// <summary>The context-menu state read.</summary>
    internal const string ContextMenuStateRoute = "/v1/datawindow/context-menu/state";

    /// <summary>The C-04 session open.</summary>
    internal const string ExpressionSessionRoute = "/v1/datawindow/expression/sessions";

    /// <summary>The C-04 expression add, where a parse failure is reported.</summary>
    internal const string AddExpressionRoute = "/v1/datawindow/expression/expressions/add";

    /// <summary>The generated contract document.</summary>
    internal const string DocumentRoute = "/openapi/v1.json";

    /// <summary>
    /// A DataWindow handle no definition carries, so an update reaches the upstream without the validator
    /// rejecting its payload first.
    /// </summary>
    /// <remarks>
    /// THE VALIDATOR RUNS AHEAD OF THE WORK SCOPE and only where the handle resolves to a model set, so a
    /// handle outside the shipped catalogue is what lets a concurrency case reach C-06 at all. It is the
    /// same value the sibling lifecycle suite uses, for the same reason.
    /// </remarks>
    internal const string UnresolvedHandle = "dw-1";

    /// <summary>The transcribed primary fixture, whose six columns the validator adjudicates against.</summary>
    /// <remarks>
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> - the ONLY updatable DataWindow in the repository
    /// and therefore the golden master for the retrieval, validation and update triple.
    /// </remarks>
    internal const string PrimaryFixtureHandle = "dw_sqlite";

    /// <summary>A column-expression host handle the shipped catalogue binds.</summary>
    internal const string ExpressionFixtureName = "dw_test_dwsvc";

    /// <summary>The problem member carrying the legacy return code.</summary>
    internal const string RetCodeMember = "retCode";

    /// <summary>The problem member carrying an optimistic-concurrency conflict detail.</summary>
    internal const string ConflictMember = "conflict";

    /// <summary>The problem member carrying a relayed database error.</summary>
    internal const string DbErrorMember = "dbError";

    /// <summary>The problem member carrying the relayed response of an in-band failure.</summary>
    internal const string ResponseMember = "response";

    /// <summary>The problem member naming the upstream a failure demonstrably came from.</summary>
    internal const string UpstreamMember = "upstream";

    /// <summary>The problem member carrying the correlation identifier.</summary>
    internal const string TraceIdMember = "traceId";

    /// <summary>The problem type the concurrency conflict carries.</summary>
    /// <remarks>
    /// ITS OWN TYPE, DISTINCT FROM THE OTHER <c>409</c>. <c>AlreadyExists</c> shares the status and is
    /// emphatically NOT a concurrency conflict, so a caller keying a retry-or-surface policy on the status
    /// alone would conflate them; the type is what tells them apart.
    /// </remarks>
    internal const string ConflictProblemType =
        "urn:powerframework:problem:optimistic-concurrency-conflict";

    /// <summary>The problem title the concurrency conflict carries.</summary>
    internal const string ConflictProblemTitle = "Optimistic-concurrency conflict";

    /// <summary>The problem type the other <c>409</c> carries, which must never appear on a conflict.</summary>
    internal const string AlreadyExistsProblemType = "urn:powerframework:problem:already-exists";

    /// <summary>The default problem type, used where no more specific one applies.</summary>
    internal const string DefaultProblemType = "about:blank";

    /// <summary>The media type every refusal declares and produces.</summary>
    internal const string ProblemMediaType = "application/problem+json";

    /// <summary>The configuration key setting the logging factory's minimum level for every category.</summary>
    /// <remarks>
    /// The host factory writes each additional setting through the web host's own setting channel, which is
    /// the same configuration the logging builder reads its filter rules from - so this is the production
    /// resolution path rather than a test-only door.
    /// </remarks>
    internal const string LogLevelFloorSettingKey = "Logging:LogLevel:Default";

    /// <summary>The most verbose level the logging abstraction defines.</summary>
    /// <remarks>
    /// Read from the enumeration rather than typed as a string, so a rename cannot leave this suite
    /// silently filtering records out and passing an absence assertion vacuously.
    /// </remarks>
    internal static string MostVerboseLogLevel { get; } = nameof(LogLevel.Trace);

    /// <summary>The category the projection writes its own operator records under.</summary>
    /// <remarks>
    /// Read from the implementation TYPE rather than transcribed, so the locator a redaction assertion uses
    /// to prove the sink saw the site under test cannot drift away from the site itself. This is the one
    /// place this suite touches the implementation type at all - every behavioural assertion reaches it over
    /// HTTP, which is the only surface a projection has.
    /// </remarks>
    internal static string ProjectionLoggerCategory { get; } =
        typeof(RestProjectionEndpoints).FullName
        ?? throw new InvalidOperationException(
            "The projection endpoint type has no full name, so its logger category cannot be derived.");

    /// <summary>The category the gRPC service behind the projection writes its own records under.</summary>
    /// <remarks>
    /// Named because a redaction assertion has to cover the WHOLE request path. The projected route and the
    /// gRPC method share one implementation, and that implementation writes an operator record of its own
    /// for the same failure - so a statement leaking there would be exactly as damaging as one leaking from
    /// the projection, and a suite that watched only the projection's category would miss it.
    /// </remarks>
    internal static string UpstreamServiceLoggerCategory { get; } =
        typeof(DataWindowService).FullName
        ?? throw new InvalidOperationException(
            "The DataWindow service type has no full name, so its logger category cannot be derived.");

    /// <summary>Builds a host whose Persistence edge can carry an update through to C-06.</summary>
    /// <param name="prepare">Receives the prepare script and record the registration captured.</param>
    /// <returns>The host, not yet started.</returns>
    /// <remarks>
    /// The registration is appended AFTER the fixture's own substitutions run, so it replaces the shared
    /// double rather than racing it; the four generated stubs, the token seam and the logger are all
    /// resolved from the container so the deployed client registrations stay genuinely in the graph.
    /// </remarks>
    internal static DataServicesTestHostFactory Host(out PrepareUpdateRecorder prepare)
    {
        DataServicesTestHostFactory host = new();
        PrepareUpdateRecorder recorder = new();

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<PersistenceClient>();
            services.AddScoped<PersistenceClient>(provider => new PreparingPersistenceEdgeClient(
                host.PersistenceEdge,
                recorder,
                provider.GetRequiredService<PersistenceQueryClient>(),
                provider.GetRequiredService<PersistenceUpdateClient>(),
                provider.GetRequiredService<PersistenceCommandClient>(),
                provider.GetRequiredService<PersistenceTransactionClient>(),
                provider.GetRequiredService<IServiceTokenProvider>(),
                provider.GetRequiredService<ILogger<PersistenceClient>>()));
        });

        prepare = recorder;

        return host;
    }

    /// <summary>Attaches an operator-record sink to a host that has not started yet.</summary>
    /// <param name="host">The host to attach to.</param>
    /// <returns>The sink, readable after the request under test has been answered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>THE VERBOSITY IS RAISED DELIBERATELY, AND IT IS THE POINT OF THE HELPER.</b> The logging factory
    /// filters by level BEFORE a record reaches a provider, and the host's default minimum would drop every
    /// debug and trace record - so an absence assertion taken at the default level would be weaker than it
    /// looks. Raising the floor to the most verbose level means the assertion covers every record the
    /// implementation is capable of writing, not merely the ones an operator would see in production.
    /// </remarks>
    internal static ProjectionLogRecorder RecordOperatorLog(DataServicesTestHostFactory host)
    {
        ArgumentNullException.ThrowIfNull(host);

        ProjectionLogRecorder recorder = new();

        host.AdditionalServiceConfiguration.Add(
            services => services.AddSingleton<ILoggerProvider>(recorder));

        host.AdditionalSettings[LogLevelFloorSettingKey] = MostVerboseLogLevel;

        return recorder;
    }

    /// <summary>Relative on purpose, so no row can leave the process.</summary>
    /// <param name="route">The route.</param>
    /// <returns>The relative address.</returns>
    internal static Uri Relative(string route) => new(route, UriKind.Relative);

    /// <summary>Reads a response body as text.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    internal static Task<string> BodyAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads a response body as a parsed document.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The document, which the caller disposes.</returns>
    internal static async Task<JsonDocument> DocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await BodyAsync(response).ConfigureAwait(false));

    /// <summary>
    /// Binds one member of a problem body back onto the generated protobuf message it carries.
    /// </summary>
    /// <typeparam name="TMessage">The generated message the member is declared as.</typeparam>
    /// <param name="body">The problem body.</param>
    /// <param name="member">The member to bind.</param>
    /// <returns>The bound message.</returns>
    /// <remarks>
    /// <b>BOUND TO THE GENERATED TYPE RATHER THAN READ AS LOOSE JSON, DELIBERATELY.</b> Comparing member
    /// by member against hand-written names would assert a shape this file invented; parsing through the
    /// generated parser asserts the CONTRACT's shape, and it fails on an unknown member - so a projection
    /// that renamed, added or dropped a field cannot slip past.
    /// </remarks>
    internal static TMessage Bind<TMessage>(JsonDocument body, string member)
        where TMessage : IMessage<TMessage>, new()
    {
        ArgumentNullException.ThrowIfNull(body);

        return JsonParser.Default.Parse<TMessage>(
            body.RootElement.GetProperty(member).GetRawText());
    }

    /// <summary>Posts a payload to a projected route.</summary>
    /// <param name="client">The client, in whichever authentication posture the case chose.</param>
    /// <param name="route">The route.</param>
    /// <param name="payload">The request body, in the canonical protobuf JSON mapping.</param>
    /// <returns>The response, which the caller disposes.</returns>
    internal static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string route,
        object payload)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.PostAsJsonAsync(
            Relative(route),
            payload,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Opens a column-expression session and reports its identifier and its handle.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <returns>The session identifier and the session-qualified DataWindow handle.</returns>
    /// <remarks>
    /// The handle is session-qualified BY CONSTRUCTION, which is what makes a handle carried in from
    /// another session miss this session's registry and be blocked rather than silently resolve.
    /// </remarks>
    internal static async Task<(string SessionId, string Handle)> OpenExpressionSessionAsync(
        HttpClient client)
    {
        using HttpResponseMessage opened = await PostAsync(
            client,
            ExpressionSessionRoute,
            new { datawindowHandles = new[] { ExpressionFixtureName } }).ConfigureAwait(false);

        using JsonDocument body = await DocumentAsync(opened).ConfigureAwait(false);

        return (
            body.RootElement.GetProperty("sessionId").GetString() ?? string.Empty,
            body.RootElement.GetProperty("datawindowHandles")[0].GetString() ?? string.Empty);
    }

    /// <summary>Builds the conflicting row the primary fixture's concurrency contract implies.</summary>
    /// <param name="row">The one-based row ordinal.</param>
    /// <returns>The row, carrying BOTH value sets for all six marked columns.</returns>
    /// <remarks>
    /// <para>
    /// <b>ALL SIX COLUMNS, WITH BOTH VALUE SETS, BECAUSE THE FIXTURE SAYS SO.</b>
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c> marks every one of id, name, age, address,
    /// salary and birth <c>update=yes updatewhereclause=yes</c> and declares <c>updatewhere=1</c>, so the
    /// generated statement's where-clause compared the ORIGINAL value of all six. A conflict payload
    /// carrying fewer than six, or carrying current values only, could not tell a caller which column
    /// moved - which is the difference between a caller that can rebase and one that can only fail.
    /// </para>
    /// <para>
    /// EACH VALUE USES THE UNION ARM ITS DECLARED TYPE IMPLIES rather than a uniform string: id and age
    /// are <c>number</c>, salary is <c>decimal(2)</c> and therefore NOT a double, birth is a
    /// <c>date</c>, and name and address are character columns. A payload that rendered all six as
    /// strings would still round-trip and would have lost the type information the storage engine
    /// compares on.
    /// </para>
    /// <para>
    /// ONLY <c>age</c> MOVES. A conflict in which everything differs is the easy case; a conflict in which
    /// exactly one column moved is the one a caller has to be able to read, and it is what makes the
    /// per-column comparison in the assertions meaningful.
    /// </para>
    /// </remarks>
    internal static ConflictRow ConflictingRow(long row)
    {
        ConflictRow conflicting = new()
        {
            Buffer = DwBuffer.Primary,
            Row = row,
            ItemStatus = ItemStatus.DataModified,
        };

        // id - number, and the fixture's key AND identity column.
        Add(1L, "id", new AnyValue { Int64Value = 7L }, new AnyValue { Int64Value = 7L });

        // name - char(100).
        Add(2L, "name", new AnyValue { StringValue = "Ada" }, new AnyValue { StringValue = "Ada" });

        // age - number, and THE ONE COLUMN THAT MOVED.
        Add(3L, "age", new AnyValue { Int64Value = 41L }, new AnyValue { Int64Value = 28L });

        // address - char(200). Note the DataWindow declares 200 against a 50-character storage column;
        // that mismatch is a preserved legacy defect and is not this payload's business.
        Add(
            4L,
            "address",
            new AnyValue { StringValue = "Marischal College" },
            new AnyValue { StringValue = "Marischal College" });

        // salary - decimal(2), carried as a decimal and never as a double.
        Add(
            5L,
            "salary",
            new AnyValue { DecimalValue = new DecimalValue { Value = "1200.00" } },
            new AnyValue { DecimalValue = new DecimalValue { Value = "1200.00" } });

        // birth - date.
        Add(
            6L,
            "birth",
            new AnyValue { DateValue = new DateValue { Value = "1815-12-10" } },
            new AnyValue { DateValue = new DateValue { Value = "1815-12-10" } });

        return conflicting;

        void Add(long columnId, string columnName, AnyValue current, AnyValue original)
        {
            conflicting.CurrentValues.Add(new ColumnValue
            {
                ColumnName = columnName,
                ColumnId = columnId,
                Value = current,
            });

            conflicting.OriginalValues.Add(new ColumnValue
            {
                ColumnName = columnName,
                ColumnId = columnId,
                Value = original,
            });
        }
    }

    /// <summary>An update payload the validator does not adjudicate, addressed at an unresolved handle.</summary>
    /// <returns>The payload.</returns>
    internal static object UpdatePayload() => new { datawindowHandle = UnresolvedHandle };

    /// <summary>An update payload carrying one value the declared column type cannot represent.</summary>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// <para>
    /// Addressed at the TRANSCRIBED FIXTURE, because the validator resolves columns by asking the host and
    /// only runs where the handle resolves. <c>age</c> is declared <c>number</c>
    /// [<c>dw_sqlite.srd:L10</c>], so a non-numeric value is exactly what the ported <c>dwnvlnumber</c>
    /// validator refuses - and the refusal is the LOCALIZED dialog of
    /// <c>se_cst_dw.sru:L355</c> and <c>:L357</c>, re-expressed.
    /// </para>
    /// <para>
    /// IT CARRIES THE COLUMN'S ORIGINAL TOO, AND THAT IS NOT DECORATION. A <c>DataModified!</c> row
    /// generates an <c>UPDATE ... WHERE</c> whose predicate is built from the ORIGINAL value of every
    /// column it carries (AAP 0.6.3.2), so a row that states none is refused for a MISSING BASELINE before
    /// its values are the subject at all [<c>Validators/UpdateRowValidator</c>]. That refusal is a
    /// different fault with a different code, and a payload carrying both would make the cases that use
    /// this one assert about whichever fault happened to be reported first. Stating the baseline leaves
    /// exactly ONE fault in the payload - the unrepresentable value - which is what every case here is
    /// about.
    /// </para>
    /// </remarks>
    internal static object RejectedUpdatePayload() => new
    {
        datawindowHandle = PrimaryFixtureHandle,
        rows = new[]
        {
            new
            {
                buffer = "DW_BUFFER_PRIMARY",
                row = 1,
                itemStatus = "ITEM_STATUS_DATA_MODIFIED",
                columns = new[]
                {
                    new
                    {
                        columnName = "age",
                        columnId = 3,
                        value = new { stringValue = "not-a-number" },
                    },
                },
                originalValues = new[]
                {
                    new
                    {
                        columnName = "age",
                        columnId = 3,
                        value = new { doubleValue = 32d },
                    },
                },
            },
        },
    };
}


// =====================================================================================================
//  1. THE DECISIVE PROPERTY - gRPC `Aborted` BECOMES HTTP 409, CARRYING THE CONFLICT UNCHANGED
// =====================================================================================================

/// <summary>
/// The optimistic-concurrency conflict as a caller receives it: status, payload, attempt count, and the
/// absence of every alternative the projection could have chosen instead.
/// </summary>
/// <remarks>
/// <para>
/// EACH CASE OWNS ITS HOST. A conflict is scripted per scenario and two cases sharing a fixture would let
/// one case's script decide another's answer, which is exactly the class of coupling that makes a
/// concurrency suite unreliable.
/// </para>
/// <para>
/// THE WHOLE COMPOSITION IS EXERCISED, not a stub of it: the request travels through the real
/// authentication pipeline, the real <c>DataWindowService.Update</c>, the real work-scope acquisition and
/// release, the real typed client's conflict decoding, and the real status mapper. Only the far side of
/// the single outbound edge is a double.
/// </para>
/// </remarks>
public sealed class RestProjectionConflictTests
{
    /// <summary>
    /// 🔴 AN <c>updatewhereclause</c> MISMATCH IS HTTP <b>409</b>, AND THE BODY CARRIES THE UPSTREAM'S OWN
    /// CONFLICT DETAIL FIELD FOR FIELD.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS AND THE PAYLOAD ARE TWO INDEPENDENT CLAIMS AND BOTH ARE LOAD-BEARING. A projection that
    /// answered 409 with an empty body would tell a caller that something was contended and leave it
    /// unable to act; one that answered 200 with the detail attached would be read as an applied update by
    /// every HTTP client, because every HTTP client branches on the status line.
    /// </para>
    /// <para>
    /// THE DETAIL IS COMPARED AGAINST THE GENERATED TYPE, NOT AGAINST A HAND-WRITTEN SHAPE. The scripted
    /// <c>common.v1.ConflictDetail</c> and the one parsed back out of the body are compared as messages,
    /// so a renamed member, a dropped repeated element or a re-ordered value set all fail - and the parse
    /// itself fails on any member the contract does not declare.
    /// </para>
    /// <para>
    /// EXACTLY ONE UPSTREAM ATTEMPT. Transient-fault resilience belongs to the typed clients and a
    /// concurrency conflict is not a transient fault, so a second attempt here would be a second overwrite
    /// attempt against a row the caller has already been told it may not write.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUpdateWhereClauseMismatchIsAConflictCarryingTheUpstreamPayloadUnchangedAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out PrepareUpdateRecorder prepare);

        host.PersistenceEdge.Reset();

        ConflictDetail scripted = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        // 1 - EXACTLY 409. Written as the enumeration member AND as the number, because the two are
        //     different mistakes: a wrong member is a typo and a wrong number is a mapping change.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(409, (int)response.StatusCode);

        // 2 - and it is machine-readable, which is what makes the payload below reachable by a client.
        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        // 3 - THE SAME DETAIL, FIELD FOR FIELD, bound through the generated parser.
        ConflictDetail relayed = RestProjection.Bind<ConflictDetail>(
            body,
            RestProjection.ConflictMember);

        Assert.Equal(scripted, relayed);

        // 4 - and the current row state a caller has to read is genuinely there: the evidenced table, the
        //     expected-versus-matched counts, and per row BOTH value sets over all six marked columns.
        Assert.Equal(ScriptedPersistenceResponses.EvidencedUpdateTable, relayed.UpdateTable);
        Assert.Equal(1L, relayed.RowsExpected);
        Assert.Equal(0L, relayed.RowsMatched);

        ConflictRow conflicting = Assert.Single(relayed.Rows);

        Assert.Equal(DwBuffer.Primary, conflicting.Buffer);
        Assert.Equal(1L, conflicting.Row);
        Assert.Equal(ItemStatus.DataModified, conflicting.ItemStatus);
        Assert.Equal(6, conflicting.CurrentValues.Count);
        Assert.Equal(6, conflicting.OriginalValues.Count);

        // 5 - the upstream was asked ONCE, through the whole composition: prepare and then update.
        Assert.Equal(1, prepare.Calls);
        Assert.Equal(1, host.PersistenceEdge.UpdateCalls);

        // 6 - and the conflict did not leak a work handle. A conflict is the likeliest non-success outcome
        //     of an update, so it is exactly the path on which a leaked task would hold a worker task and a
        //     pooled-transaction reference for the life of the process.
        Assert.True(
            host.PersistenceEdge.NothingIsStillHeld,
            "A projected conflict left a Persistence handle behind. Held sessions: "
            + string.Join(", ", host.PersistenceEdge.HeldSessionIds)
            + "; held update tasks: "
            + string.Join(", ", host.PersistenceEdge.HeldUpdateTaskIds)
            + ".");
    }

    /// <summary>
    /// The conflict names the column that moved, so a caller can rebase rather than only fail.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE PROPERTY THAT MAKES A RETRY-OR-SURFACE POLICY WRITABLE AT ALL. Five of the six marked
    /// columns are unchanged and exactly one moved; a caller reads the pairs and learns which. A payload
    /// carrying only current values would leave it re-sending the same stale originals for ever, and a
    /// payload carrying only the changed column would not let it tell "unchanged" from "not reported".
    /// </para>
    /// <para>
    /// THE PAIRING IS BY COLUMN NAME AND ORDINAL TOGETHER. The legacy addresses a column both ways and the
    /// contract carries both, so a comparison keyed on only one of them would pass against a payload whose
    /// ordinals had drifted from its names.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheConflictNamesTheColumnThatMovedAndReportsTheRestUnchangedAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        _ = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        ConflictRow conflicting = Assert.Single(
            RestProjection.Bind<ConflictDetail>(body, RestProjection.ConflictMember).Rows);

        List<string> moved = [];

        foreach (ColumnValue current in conflicting.CurrentValues)
        {
            ColumnValue original = Assert.Single(
                conflicting.OriginalValues,
                candidate => string.Equals(
                                 candidate.ColumnName,
                                 current.ColumnName,
                                 StringComparison.Ordinal)
                             && candidate.ColumnId == current.ColumnId);

            if (!Equals(current.Value, original.Value))
            {
                moved.Add(current.ColumnName);
            }
        }

        Assert.Equal(["age"], moved);
    }

    /// <summary>
    /// The conflict is never swallowed: no success status, no empty body, no success-with-a-warning.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>THE NEGATIVE IS THE ASSERTION, AND IT IS SPELLED OUT RATHER THAN IMPLIED BY THE POSITIVE ONE.</b>
    /// "The status is 409" and "the status is not a success" are the same claim only while the mapping is
    /// correct; separating them is what makes a regression that answered 200 with a conflict member
    /// attached - the single most dangerous shape available here, because a rejected update would read as
    /// an applied one - fail on a row that says exactly that.
    /// </remarks>
    [Fact]
    public async Task TheConflictIsNeverSwallowedIntoASuccessAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        _ = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.False(
            response.IsSuccessStatusCode,
            $"A concurrency conflict answered {(int)response.StatusCode}. A success status would make a "
            + "rejected update indistinguishable from an applied one to every client that branches on the "
            + "status line.");

        string body = await RestProjection.BodyAsync(response);

        Assert.NotEmpty(body);

        // A 409 with no conflict member is a DIFFERENT and legitimate answer - it means no detail could be
        // decoded - so the assertion is that THIS conflict, which carried a decodable detail, reports it.
        Assert.Contains($"\"{RestProjection.ConflictMember}\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A conflict is neither degraded to a server fault nor re-blamed on the caller.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// FOUR WRONG ANSWERS, NAMED. <b>500</b> would say this service faulted and send an operator to the
    /// wrong logs; <b>400</b> would blame the caller for a request that was well-formed and invite a retry
    /// with different input that can never help; <b>412</b> is the status for a failed precondition the
    /// caller itself stated in a header, which is not what happened; and <b>409</b> under the
    /// already-exists problem type would report a duplicate rather than a contention.
    /// </para>
    /// <para>
    /// THE PROBLEM TYPE IS CHECKED AS WELL AS THE STATUS, because two distinct conditions share <c>409</c>
    /// here and only one of them is a concurrency conflict.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AConflictIsNotDegradedToAServerFaultOrReBlamedOnTheCallerAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        _ = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            RestProjection.ConflictProblemType,
            body.RootElement.GetProperty("type").GetString());

        Assert.Equal(
            RestProjection.ConflictProblemTitle,
            body.RootElement.GetProperty("title").GetString());

        Assert.NotEqual(
            RestProjection.AlreadyExistsProblemType,
            body.RootElement.GetProperty("type").GetString());

        // The body's own status agrees with the status line. A body disagreeing with its own status would
        // be worse than no body at all, because a client reading either would be right and they would
        // disagree.
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());

        // And the request that provoked it is identified, so a caller correlating several in-flight
        // updates knows which one was refused.
        Assert.Equal(
            RestProjection.UpdateRoute,
            body.RootElement.GetProperty("instance").GetString());
    }

    /// <summary>
    /// 🔴 RE-SENDING THE SAME PAYLOAD PRODUCES THE SAME 409, AND NOTHING IS OVERWRITTEN ON THE WAY.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE RETRY-OR-SURFACE POLICY IS THE CALLER'S, AND THIS ROW IS WHAT MAKES THAT STATEMENT TRUE RATHER
    /// THAN ASPIRATIONAL. Two identical requests produce two identical refusals and EXACTLY TWO upstream
    /// attempts - one per caller request. Three attempts would mean the projection retried on the caller's
    /// behalf; one would mean the second request never reached the upstream at all and the answer was
    /// cached, which would be just as wrong in the other direction.
    /// </para>
    /// <para>
    /// AND THE UPSTREAM SAW WHAT THE CALLER SENT. The DATA the second attempt carried is compared against
    /// the first, so a projection that rebased the request itself - stripping the stale originals, or
    /// substituting the current values it had just been handed - fails here. That is the silent overwrite
    /// this rule exists to forbid: it would turn a refused write into an applied one while looking, from
    /// the outside, like a well-behaved retry.
    /// </para>
    /// <para>
    /// THE DATA IS COMPARED AND THE TASK HANDLE IS NOT, DELIBERATELY. Each request acquires its own work
    /// scope and therefore its own upstream task, so the two handles MUST differ - comparing whole requests
    /// would assert the opposite of the contract and would fail on correct behaviour. What must be
    /// identical is the buffer payload and the transaction setting travelling on it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReSendingTheSamePayloadIsRefusedAgainAndNothingIsOverwrittenAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out PrepareUpdateRecorder prepare);

        host.PersistenceEdge.Reset();

        _ = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage first = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);

        PersistenceUpdateRequest? firstAttempt = host.PersistenceEdge.LastUpdateRequest;

        Assert.NotNull(firstAttempt);
        Assert.Equal(1, host.PersistenceEdge.UpdateCalls);

        using HttpResponseMessage second = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        // The same refusal, not a different one and not a success.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // ONE ATTEMPT PER CALLER REQUEST. Not one, and emphatically not three.
        Assert.Equal(2, host.PersistenceEdge.UpdateCalls);
        Assert.Equal(2, prepare.Calls);

        PersistenceUpdateRequest? secondAttempt = host.PersistenceEdge.LastUpdateRequest;

        Assert.NotNull(secondAttempt);

        // And the payload was relayed rather than rewritten between the two.
        Assert.Equal(firstAttempt.UpdateData, secondAttempt.UpdateData);
        Assert.Equal(firstAttempt.Autocommit, secondAttempt.Autocommit);

        // The two work handles DIFFER, because each request acquired its own scope. Asserted so the
        // equality above cannot be satisfied by a projection that reused one request object.
        Assert.NotEqual(firstAttempt.Task?.TaskId, secondAttempt.Task?.TaskId);

        // Both bodies carry the same detail, so a caller polling the same refusal reads a stable answer.
        using JsonDocument firstBody = await RestProjection.DocumentAsync(first);
        using JsonDocument secondBody = await RestProjection.DocumentAsync(second);

        Assert.Equal(
            RestProjection.Bind<ConflictDetail>(firstBody, RestProjection.ConflictMember),
            RestProjection.Bind<ConflictDetail>(secondBody, RestProjection.ConflictMember));

        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// The outcome code on a conflict is the upstream's own, carried verbatim.
    /// </summary>
    /// <param name="scripted">The outcome code the upstream's trailer carries.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>VERBATIM, NEVER RE-DERIVED.</b> The legacy's own defensive override rewrites a claimed success
    /// into a failure when the transaction's SQL code says otherwise
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208-L210</c>], so the code
    /// that reaches a caller is the reconciled one and a projection that substituted a code of its own
    /// would erase that reconciliation. The status stays 409 either way, which is the point: the HTTP
    /// class says "contended" and the numeric code says which legacy outcome produced it.
    /// </para>
    /// <para>
    /// BOTH ROWS MATTER SEPARATELY. <c>E_DB_ERROR</c> is the code the legacy override produces and is the
    /// default a conflict carries; <c>E_RETRY</c> is the code the published status table names for the
    /// conflict arm. A projection that hard-coded either one would pass one row and fail the other.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(KernelRetCode.E_DB_ERROR)]
    [InlineData(KernelRetCode.E_RETRY)]
    public async Task TheConflictOutcomeCodeIsTheUpstreamsOwnAsync(long scripted)
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        host.PersistenceEdge.ScriptUpdateConflict(
            ScriptedPersistenceResponses.Conflict(
                rowsExpected: 1L,
                rowsMatched: 0L,
                RestProjection.ConflictingRow(row: 1L)),
            scripted);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            scripted,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());
    }

    /// <summary>
    /// A refused update contract is NOT a conflict, so it must not answer 409.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE COMPLEMENT THAT KEEPS THE POSITIVE ROWS HONEST. A projection that answered 409 for every
    /// unsuccessful update would satisfy every conflict row above and would be badly wrong: a rejected
    /// update CONTRACT generated no statement at all, so nothing was contended, and telling a caller to
    /// re-read and rebase would send it round a loop that can never terminate.
    /// </para>
    /// <para>
    /// The upstream was still reached exactly once for the prepare and NOT AT ALL for the update, which is
    /// the other half of the claim: a refused contract must not go on to submit statements.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusedUpdateContractIsNotReportedAsAConflictAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out PrepareUpdateRecorder prepare);

        host.PersistenceEdge.Reset();

        prepare.Outcome = KernelRetCode.E_INVALID_ARGUMENT;

        _ = host.PersistenceEdge.ScriptUpdateSuccess();

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.NotEqual(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.False(
            body.RootElement.TryGetProperty(RestProjection.ConflictMember, out _),
            "A refused update contract carried a conflict member. No statement was generated, so nothing "
            + "was contended, and reporting a concurrency conflict would send the caller to re-read and "
            + "re-send a payload that can never apply.");

        Assert.Equal(1, prepare.Calls);
        Assert.Equal(0, host.PersistenceEdge.UpdateCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }
}


// =====================================================================================================
//  2. ONE SHARED STATUS MAPPING, NOT THIRTY-NINE COPIES OF IT
// =====================================================================================================

/// <summary>
/// The status translation is implemented once and reached from every route, and each arm the
/// implementation defines answers what it defines.
/// </summary>
/// <param name="host">
/// The shared in-process host. A CLASS fixture because booting the composition root is the expensive
/// part and no case here scripts the Persistence edge - every row below is answered by the service
/// itself, before or without an upstream call.
/// </param>
/// <remarks>
/// <para>
/// <b>WHY "SHARED" IS THE PROPERTY RATHER THAN "CORRECT".</b> Thirty-nine copies of a status table would each
/// be correct on the day it was written and would drift apart on the first edit, and the drift would be
/// invisible: a caller would receive one status for a condition on one route and a different status for
/// the same condition on another, and its retry-or-surface policy would become unwritable. So the rows
/// below drive the SAME condition through SEVERAL routes and require the SAME answer and the SAME body
/// shape, which is a claim about the implementation's structure and not only about its table.
/// </para>
/// <para>
/// <b>AND NOTHING HERE IS INVENTED.</b> Every expected status is read out of
/// <c>Endpoints/RestProjectionEndpoints.cs</c>. Where the implementation does not classify a condition,
/// the row asserts what it actually answers rather than what a reader might prefer.
/// </para>
/// </remarks>
public sealed class RestProjectionStatusMappingTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// The four headless state reads, which share one in-band failure for a handle nothing binds.
    /// </summary>
    /// <returns>One row per route.</returns>
    /// <remarks>
    /// FOUR ROUTES ON THREE DIFFERENT MODELS plus the context menu, all reaching the SAME in-band arm.
    /// That is what makes the "one mapping" claim testable without a live upstream: the condition is
    /// produced by the service, identically, on four independent operations.
    /// </remarks>
    public static TheoryData<string> HeadlessStateRoutes() =>
    [
        RestProjection.RowSelectStateRoute,
        RestProjection.ColumnSortStateRoute,
        RestProjection.DropDownSearchStateRoute,
        RestProjection.ContextMenuStateRoute,
    ];

    /// <summary>
    /// 🔴 THE SAME CONDITION ON FOUR DIFFERENT ROUTES PRODUCES THE SAME STATUS AND THE SAME BODY SHAPE.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// A handle nothing binds answers <c>RetCode.E_INVALID_HANDLE</c> IN BAND on all four operations, and
    /// the published in-band table sends that to <b>404</b> - a true statement about a handle that names
    /// nothing. Every row must also carry the SAME members: the numeric code, the relayed response, the
    /// instance and the correlation identifier.
    /// </para>
    /// <para>
    /// THE RELAYED RESPONSE IS PART OF THE SHAPE, not a decoration. An in-band failure is a gRPC call that
    /// SUCCEEDED and answered a failure in its body, so the contract's own answer - the code, the
    /// diagnostic and the structured error - is what a caller needs; a problem body that only paraphrased
    /// it would force a caller to choose between the status line and the contract.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(HeadlessStateRoutes))]
    public async Task OneSharedMappingAnswersTheSameConditionIdenticallyOnEveryRouteAsync(string route)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            route,
            new { datawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(RestProjection.DefaultProblemType, body.RootElement.GetProperty("type").GetString());

        Assert.Equal(
            KernelRetCode.E_INVALID_HANDLE,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.Equal(route, body.RootElement.GetProperty("instance").GetString());

        Assert.True(
            body.RootElement.TryGetProperty(RestProjection.ResponseMember, out _),
            $"{route} answered an in-band failure without relaying the contract's own response. The "
            + "numeric code, the diagnostic and the structured error all travel on it, so omitting it "
            + "would leave a caller with the status line alone.");

        Assert.False(
            string.IsNullOrWhiteSpace(
                body.RootElement.GetProperty(RestProjection.TraceIdMember).GetString()));
    }

    /// <summary>
    /// The exception-side translations the implementation defines, pinned arm by arm.
    /// </summary>
    /// <param name="acquisitionOutcome">
    /// The outcome the upstream answers a work-handle acquisition with, which is the lever that selects
    /// the gRPC status the service raises.
    /// </param>
    /// <param name="expected">The HTTP status the shared mapper answers with.</param>
    /// <param name="expectedRetCode">The numeric code the problem body carries.</param>
    /// <param name="namesUpstream">
    /// Whether the body names an upstream, which is licensed only where the failure demonstrably
    /// originated on the single outbound edge these operations traverse.
    /// </param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>READ OUT OF THE IMPLEMENTATION, NOT PREFERRED BY THIS FILE.</b> Each row is the acquisition
    /// map's own arm composed with the shared status mapper's own arm:
    /// <c>E_INVALID_ARGUMENT</c> is an <c>InvalidArgument</c> and therefore <b>400</b>;
    /// <c>E_ACCESS_DENIED</c> is a <c>PermissionDenied</c> and therefore <b>403</b> - a valid-but-
    /// insufficient credential, distinct from none at all; <c>E_INVALID_HANDLE</c> is a <c>NotFound</c> and
    /// therefore <b>404</b>, which is the row the published table declares for it and the answer the unary
    /// outcome map and both of DataWindowService's upstream maps give it. Answering
    /// <c>FailedPrecondition</c> instead is the tempting choice, and since the published table declares no
    /// such row that arm falls to the canonical mapping and reaches the caller as <b>400</b> carrying
    /// <c>E_INVALID_ARGUMENT</c>, replacing the originating code and blaming a malformed argument for a
    /// handle the upstream no longer holds;
    /// <c>E_BUSY</c> is a <c>ResourceExhausted</c> and therefore <b>429</b>, the status
    /// that carries a retry hint, and NOT 503, because a busy resource is a ceiling that clears rather
    /// than a service that is down; and anything the acquisition map does not name reaches its default arm
    /// as <c>Unavailable</c> and therefore <b>503</b>.
    /// </para>
    /// <para>
    /// THE UPSTREAM MEMBER IS PART OF THE ARM AND IS ASSERTED WITH IT. Naming an upstream is a claim about
    /// where a failure came from; on the caller-fault arms nothing may be named, because attributing a
    /// rejected argument to Persistence would send an operator to the wrong service.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(KernelRetCode.E_INVALID_ARGUMENT, 400, KernelRetCode.E_INVALID_ARGUMENT, false)]
    [InlineData(KernelRetCode.E_INVALID_SQL, 400, KernelRetCode.E_INVALID_ARGUMENT, false)]
    [InlineData(KernelRetCode.E_ACCESS_DENIED, 403, KernelRetCode.E_ACCESS_DENIED, false)]
    [InlineData(KernelRetCode.E_INVALID_HANDLE, 404, KernelRetCode.E_OBJECT_NOT_FOUND, false)]
    [InlineData(KernelRetCode.E_BUSY, 429, KernelRetCode.E_BUSY, false)]
    [InlineData(KernelRetCode.E_DB_ERROR, 503, KernelRetCode.E_RETRY, true)]
    [InlineData(KernelRetCode.FAILED, 503, KernelRetCode.E_RETRY, true)]
    public async Task TheExceptionSideTranslationsAreTheOnesTheImplementationDefinesAsync(
        long acquisitionOutcome,
        int expected,
        long expectedRetCode,
        bool namesUpstream)
    {
        await using DataServicesTestHostFactory scoped = RestProjection.Host(out _);

        scoped.PersistenceEdge.Reset();
        scoped.PersistenceEdge.ScriptQuery(rowCount: 1L);
        scoped.PersistenceEdge.BeginSessionCode = acquisitionOutcome;

        using HttpClient client = scoped.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RetrieveRoute,
            new { datawindowHandle = RestProjection.UnresolvedHandle });

        Assert.Equal(expected, (int)response.StatusCode);

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(expected, body.RootElement.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedRetCode,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.Equal(
            namesUpstream,
            body.RootElement.TryGetProperty(RestProjection.UpstreamMember, out _));

        // Refused before any row was asked for, on every arm. An implementation that ignored the
        // acquisition outcome and sent the run call anyway would still fail the retrieval, but it would
        // fail it with the upstream's own handle refusal - naming the wrong problem and doing work for a
        // request that could never have succeeded.
        Assert.Equal(0, scoped.PersistenceEdge.QueryCalls);
        Assert.True(scoped.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// Every named arm of the exception-side status table, driven through the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE FOUR THE BRIEF NAMES, PLUS THE ROWS THAT PROVE THEY ARE DISTINCT ARMS RATHER THAN ONE
    /// DEFAULT.</b> An invalid argument, a not-found, an internal error and an unauthenticated call are
    /// each pinned; so are the two that share a status with something else, because a table whose arms
    /// collapse is exactly the drift this suite exists to catch. <c>AlreadyExists</c> shares <b>409</b>
    /// with the concurrency conflict and MUST carry a different problem type and a different code;
    /// <c>Cancelled</c> shares <b>502</b> with the transport arm and carries the legacy
    /// <c>CANCELLED</c> rather than a retry code.
    /// </para>
    /// <para>
    /// <b>THE TRANSPORT DISCRIMINATION IS A ROW OF ITS OWN.</b> <c>Internal</c> appears twice, once with a
    /// transport exception attached and once without, because the implementation tells those two events
    /// apart deliberately: a status the client SYNTHESIZED from a failed handshake carries a transport
    /// exception and is a bad gateway that names the upstream, while a status the server genuinely ANSWERED
    /// carries none and is this service's own 500. Reading only one of them would let the discrimination be
    /// removed without a failing test.
    /// </para>
    /// <para>
    /// NO ROW ASSERTS A CONFLICT HERE. <c>Aborted</c> is deliberately absent from this table: it is the
    /// decisive property and it is driven end to end through the real conflict path in the first suite,
    /// where the payload is asserted too. Duplicating it as a bare status row would weaken it.
    /// </para>
    /// </remarks>
    /// <returns>The rows, each naming the raised status and the answer the table defines for it.</returns>
    public static TheoryData<StatusCode, bool, int, long, bool, string> MapperArms() =>
        new()
        {
            // The four the brief names.
            { StatusCode.InvalidArgument, false, 400, KernelRetCode.E_INVALID_ARGUMENT, false, RestProjection.DefaultProblemType },
            { StatusCode.NotFound, false, 404, KernelRetCode.E_OBJECT_NOT_FOUND, false, RestProjection.DefaultProblemType },
            { StatusCode.Internal, false, 500, KernelRetCode.E_INTERNAL_ERROR, false, RestProjection.DefaultProblemType },
            { StatusCode.Unauthenticated, false, 401, KernelRetCode.E_ACCESS_DENIED, false, RestProjection.DefaultProblemType },

            // The transport discrimination, on the same status as the row above it.
            { StatusCode.Internal, true, 502, KernelRetCode.E_RETRY, true, RestProjection.DefaultProblemType },
            { StatusCode.Unavailable, true, 502, KernelRetCode.E_RETRY, true, RestProjection.DefaultProblemType },
            { StatusCode.Unavailable, false, 503, KernelRetCode.E_RETRY, true, RestProjection.DefaultProblemType },

            // The remaining named arms, so none of them can quietly become the default.
            { StatusCode.PermissionDenied, false, 403, KernelRetCode.E_ACCESS_DENIED, false, RestProjection.DefaultProblemType },
            // 🔴 500 AND NOT 501. Every operation this projection publishes is implemented, and 501 is
            // reserved system-wide for Gateway's four deferred-capability routes (AAP 0.4.4, C-D) - so the
            // status this row used to assert was both undeclared on this document and a claim that an
            // implemented surface was a placeholder. The legacy code still names the condition exactly.
            { StatusCode.Unimplemented, false, 500, KernelRetCode.E_NO_IMPLEMENTATION, false, RestProjection.DefaultProblemType },
            { StatusCode.DeadlineExceeded, false, 504, KernelRetCode.E_TIME_OUT, false, RestProjection.DefaultProblemType },
            { StatusCode.FailedPrecondition, false, 400, KernelRetCode.E_INVALID_ARGUMENT, false, RestProjection.DefaultProblemType },
            { StatusCode.OutOfRange, false, 400, KernelRetCode.E_OUT_OF_RANGE, false, RestProjection.DefaultProblemType },
            { StatusCode.ResourceExhausted, false, 429, KernelRetCode.E_BUSY, false, RestProjection.DefaultProblemType },
            { StatusCode.Cancelled, false, 502, KernelRetCode.CANCELLED, false, RestProjection.DefaultProblemType },
            { StatusCode.DataLoss, false, 500, KernelRetCode.E_DB_ERROR, false, RestProjection.DefaultProblemType },
            { StatusCode.Unknown, false, 500, KernelRetCode.UNKNOWN, false, RestProjection.DefaultProblemType },

            // The other 409, which must never be mistaken for the conflict.
            { StatusCode.AlreadyExists, false, 409, KernelRetCode.E_INVALID_ARGUMENT, false, RestProjection.AlreadyExistsProblemType },
        };

    /// <summary>
    /// Each raised gRPC status becomes exactly the HTTP status, legacy code and problem type the
    /// implementation's own table names for it.
    /// </summary>
    /// <param name="raised">The status the projected operation raises.</param>
    /// <param name="carriesTransportException">
    /// Whether the raised status carries a transport exception, which is how a client-synthesized failure is
    /// told apart from a server-answered one.
    /// </param>
    /// <param name="expected">The HTTP status the table defines.</param>
    /// <param name="expectedRetCode">The legacy outcome code the table defines.</param>
    /// <param name="namesUpstream">Whether the body names the upstream the failure demonstrably came from.</param>
    /// <param name="expectedType">The problem type the table defines.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// DRIVEN THROUGH THE HOST RATHER THAN AGAINST THE TABLE, so each row also demonstrates the properties
    /// a direct call could not: the media type is the problem one, the correlation identifier is present,
    /// the operation really was attempted, and the work scope released on the way out of a raised failure.
    /// </para>
    /// <para>
    /// AND NO ROW LEAKS THE GRPC VOCABULARY. The status NAME never appears in the body on any arm - a
    /// projection that stringified the status it caught would satisfy every status assertion above and
    /// still hand an HTTP caller a vocabulary it cannot act on.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MapperArms))]
    public async Task EachRaisedStatusBecomesTheAnswerTheTableNamesAsync(
        StatusCode raised,
        bool carriesTransportException,
        int expected,
        long expectedRetCode,
        bool namesUpstream,
        string expectedType)
    {
        await using DataServicesTestHostFactory scoped =
            RestProjection.Host(out PrepareUpdateRecorder prepare);

        scoped.PersistenceEdge.Reset();

        _ = scoped.PersistenceEdge.ScriptUpdateSuccess();

        // The detail string is this service's own text, so it can never be mistaken for something a caller
        // or a driver supplied; the transport exception, where a row asks for one, carries no message that
        // could be echoed either.
        string raisedDetail = string.Create(
            CultureInfo.InvariantCulture,
            $"upstream diagnostic for {raised} that no caller may read");

        prepare.Fault = carriesTransportException
            ? new RpcException(new Status(raised, raisedDetail, new IOException(raisedDetail)))
            : new RpcException(new Status(raised, raisedDetail));

        using HttpClient client = scoped.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.Equal(expected, (int)response.StatusCode);

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(expected, body.RootElement.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedRetCode,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.Equal(expectedType, body.RootElement.GetProperty("type").GetString());

        Assert.Equal(
            namesUpstream,
            body.RootElement.TryGetProperty(RestProjection.UpstreamMember, out _));

        Assert.Equal(
            RestProjection.UpdateRoute,
            body.RootElement.GetProperty("instance").GetString());

        Assert.False(
            string.IsNullOrWhiteSpace(
                body.RootElement.GetProperty(RestProjection.TraceIdMember).GetString()));

        // A raised failure is not a conflict, whatever status it wears. The two 409 arms are told apart by
        // the problem type asserted above and by the ABSENCE of the conflict payload here.
        Assert.False(body.RootElement.TryGetProperty(RestProjection.ConflictMember, out _));

        string raw = await RestProjection.BodyAsync(response);

        // THE RAISED DIAGNOSTIC NEVER REACHES THE CALLER. This is the strongest single assertion in the
        // row: the message the upstream attached to its status - and the message on the transport exception
        // where a row supplied one - is absent from the whole body, so `detail` is this service's own fixed
        // prose on every arm rather than a relayed diagnostic that could quote a statement fragment.
        Assert.DoesNotContain(raisedDetail, raw, StringComparison.Ordinal);

        // AND THE GRPC VOCABULARY NEVER REACHES IT EITHER. Asserted as the transport's own type and member
        // names rather than as the status NAME, because two status names - `Internal` and `Unavailable` -
        // are also words in the standard HTTP reason phrases the problem title carries, so asserting the
        // bare name would fail on a body that is entirely correct. The status name is instead excluded from
        // `detail`, which is the one member the projection composes itself.
        foreach (string vocabulary in new[]
        {
            nameof(RpcException),
            nameof(StatusCode),
            nameof(IOException),
            "Grpc.",
        })
        {
            Assert.DoesNotContain(vocabulary, raw, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            raised.ToString(),
            body.RootElement.GetProperty("detail").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:line", raw, StringComparison.Ordinal);

        // Attempted exactly once - a mapper row satisfied by a skipped operation would prove nothing - and
        // the update itself never ran, because the contract was never prepared.
        Assert.Equal(1, prepare.Calls);
        Assert.Equal(0, scoped.PersistenceEdge.UpdateCalls);

        // The work scope released even though the operation left by a throw.
        Assert.True(scoped.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// A caller fault raised by the service itself is a 400 carrying the legacy code.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// The blank-handle guard is the one place a projected method raises <c>InvalidArgument</c> before any
    /// upstream exists, so it isolates the mapper's <c>InvalidArgument</c> arm from the acquisition map
    /// entirely - and it proves the arm is reached on a SERVER STREAM as well as on a unary call, which is
    /// a different code path through the projection.
    /// </remarks>
    [Fact]
    public async Task ACallerFaultRaisedBeforeAnyUpstreamIsABadRequestAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RetrieveRoute,
            new { datawindowHandle = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            KernelRetCode.E_INVALID_ARGUMENT,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.False(
            body.RootElement.TryGetProperty(RestProjection.UpstreamMember, out _),
            "A caller fault named an upstream. Nothing was sent anywhere, so naming one would "
            + "misattribute the rejection.");
    }

    /// <summary>
    /// A body this service rejects before invoking anything is a 400 from the same shared renderer.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE BINDING REJECTION IS SHARED TOO, and it is worth a theory of its own because it is the arm every
    /// route can reach without any collaborator at all: a missing body. Four routes give the same status,
    /// the same code and the same media type, which is the same structural claim as the in-band theory
    /// above made from the other side of the projection.
    /// </para>
    /// <para>
    /// AND THE PARSER'S OWN DIAGNOSTIC MUST NOT REACH THE BODY. It quotes the offending payload, so
    /// relaying it would echo caller-supplied content back out of a service that must not echo payloads;
    /// <c>detail</c> is fixed prose on every arm precisely so that cannot happen.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(HeadlessStateRoutes))]
    public async Task AMalformedBodyIsRejectedIdenticallyOnEveryRouteAsync(string route)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using StringContent malformed = new(
            "{\"datawindowHandle\": ",
            System.Text.Encoding.UTF8,
            MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            RestProjection.Relative(route),
            malformed,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            KernelRetCode.E_INVALID_ARGUMENT,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.DoesNotContain(
            "datawindowHandle",
            body.RootElement.GetProperty("detail").GetString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A refused payload is a 400 that leaks nothing about how the refusal was produced.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ASSERTING 500 HERE WOULD PIN A DEFECT RATHER THAN A PROPERTY.</b> The reasoning that leads
    /// there says so out loud: it reaches the map's DEFAULT arm because "<c>E_INVALID_DATA</c> is such
    /// an outcome", meaning this projection has not been taught a code the ingress has - so the SAME
    /// refusal would answer 400 through the gateway and 500 here, on two surfaces documented as equivalent. A
    /// 500 told the caller that this service had failed and invited it to retry an identical payload that
    /// can never succeed. <c>E_INVALID_DATA</c> is an explicit arm and this row asserts 400, which is
    /// the honest answer: the request's own DATA is what was refused.
    /// </para>
    /// <para>
    /// THE DEFAULT ARM'S BLAME DIRECTION IS STILL ASSERTED, AND IN A PLACE THAT CANNOT ROT INTO A DEFECT
    /// AGAIN. It moved to <c>RestProjectionStatusEquivalenceTests</c>, which calls the mapping directly
    /// with codes no deployed operation can be provoked into answering - the only way to exercise an arm
    /// for an outcome the map has NOT been taught, and the reason this row could only ever reach it by
    /// accident.
    /// </para>
    /// <para>
    /// WHAT THIS ROW IS ACTUALLY FOR IS THE LEAK, AND THAT PROPERTY IS UNCHANGED BY THE STATUS. No gRPC
    /// status name, no exception type, no stack frame and no source path reaches the response - all four
    /// are how a translated failure betrays its internals - and the correlation identifier is what a
    /// caller is given instead (CWE-209).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusedPayloadLeaksNothingAboutHowItWasProducedAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.RejectedUpdatePayload());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string raw = await RestProjection.BodyAsync(response);

        // The gRPC vocabulary. A caller of an HTTP surface has no use for it and its presence would mean
        // the projection had stopped translating and started forwarding.
        foreach (string leaked in new[]
        {
            nameof(StatusCode),
            nameof(RpcException),
            nameof(StatusCode.Aborted),
            nameof(StatusCode.FailedPrecondition),
            nameof(StatusCode.Unavailable),
        })
        {
            Assert.DoesNotContain(leaked, raw, StringComparison.Ordinal);
        }

        // A stack trace, in either of the two shapes it arrives in.
        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:line", raw, StringComparison.Ordinal);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        // The numeric code is surfaced alongside the status, so the specific legacy outcome stays
        // identifiable rather than being collapsed into the HTTP status that classifies it.
        Assert.Equal(
            KernelRetCode.E_INVALID_DATA,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        Assert.False(
            string.IsNullOrWhiteSpace(
                body.RootElement.GetProperty(RestProjection.TraceIdMember).GetString()));
    }

    /// <summary>
    /// A successful projection is the canonical protobuf JSON of the response message, and nothing else.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// The complement that keeps every failure row above honest: a projection that answered a problem body
    /// for everything would satisfy them all. A success carries <c>application/json</c> - NOT the problem
    /// media type - and none of the problem members.
    /// </remarks>
    [Fact]
    public async Task ASuccessIsTheCanonicalMessageAndCarriesNoProblemMembersAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RowSelectStateRoute,
            new { datawindowHandle = RestProjection.PrimaryFixtureHandle });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        WireGetRowSelectStateResponse projected =
            JsonParser.Default.Parse<WireGetRowSelectStateResponse>(
                await RestProjection.BodyAsync(response));

        Assert.Equal(WireRetCode.Ok, projected.RetCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        // THE MEMBERS CHECKED ARE THE ONES NO PROJECTED MESSAGE DECLARES, which is what makes their absence
        // diagnostic. `retCode` is deliberately NOT among them: it is a FIELD ON THE CONTRACT - most
        // response messages carry `ret_code`, and this one does - so finding it here says nothing about
        // whether a problem body leaked. The problem-only members are what a success must not carry.
        foreach (string problemMember in new[]
        {
            "type",
            "status",
            "detail",
            "instance",
            RestProjection.TraceIdMember,
            RestProjection.ResponseMember,
            RestProjection.ConflictMember,
            RestProjection.UpstreamMember,
        })
        {
            Assert.False(
                body.RootElement.TryGetProperty(problemMember, out _),
                $"A successful projection carried the problem member '{problemMember}'. A success body is "
                + "the contract's own message and nothing else, so a consumer binds it to the generated "
                + "type without first stripping members the contract does not declare.");
        }
    }
}


// =====================================================================================================
//  3. THE SURFACE - WHAT IS PROJECTED, WHAT IS NOT, AND HOW THIN THE PROJECTION IS
// =====================================================================================================

/// <summary>
/// The projected surface corresponds to the authored contract exactly, carries none of the three
/// bidirectional methods, and performs no business decision of its own.
/// </summary>
/// <param name="host">The shared in-process host; no case here scripts anything.</param>
/// <remarks>
/// <para>
/// <b>CONSTRAINT C-A IS THE SUBJECT.</b> This projection is consumed only by Gateway and couples through
/// the published contract, so it must expose nothing the contract does not declare AND declare nothing it
/// does not expose. Both directions are asserted, because each fails differently: an undeclared route is
/// a surface a consumer cannot discover, and a declared-but-absent one is a surface a generated client
/// will call and receive 404 from.
/// </para>
/// <para>
/// <b>CONSTRAINT C-D IS THE OTHER SUBJECT.</b> No route may name a capability area Phase 1 does not
/// implement, and no REST surrogate may exist for the three bidirectional methods. On this listener such a
/// path matches nothing and answers 404, which is the auditable proof that nothing was stubbed here.
/// </para>
/// </remarks>
public sealed class RestProjectionSurfaceTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// Plausible REST spellings of the three bidirectional methods - every one of which must match nothing.
    /// </summary>
    /// <returns>One row per candidate spelling.</returns>
    /// <remarks>
    /// SEVERAL SPELLINGS PER METHOD, DELIBERATELY. A single candidate would prove only that one particular
    /// name is absent; the point is that the CAPABILITY is absent, so the obvious names a later
    /// implementer might reach for are each checked. Note that <c>/expression/trace</c> is deliberately NOT
    /// among them: that route exists and projects <c>SetTrace</c>, which is a unary switch and not the
    /// <c>TraceChannel</c> stream - conflating the two is exactly the mistake this row set guards against.
    /// </remarks>
    public static TheoryData<string> UnprojectableRouteCandidates() =>
    [
        "/v1/datawindow/event-chain",
        "/v1/datawindow/events",
        "/v1/datawindow/event-stream",
        "/v1/datawindow/expression/invoke-method",
        "/v1/datawindow/expression/invoke-method-channel",
        "/v1/datawindow/expression/macros",
        "/v1/datawindow/expression/trace-channel",
        "/v1/datawindow/expression/traces",
    ];

    /// <summary>
    /// The four deferred capability areas, which have no presence on this listener at all.
    /// </summary>
    /// <returns>One row per area.</returns>
    /// <remarks>
    /// The four reserved boundary declarations belong to GATEWAY's own reserved-route endpoint file, which
    /// together with the deferral roster is the only place in this refactor where they may be named. On
    /// THIS listener they match nothing.
    /// </remarks>
    public static TheoryData<string> DeferredAreaRoutes() =>
    [
        "/v1/design/theme",
        "/v1/documents/json",
        "/v1/integration/http",
        "/v1/scripting/evaluate",
    ];

    /// <summary>
    /// 🔴 NO REST REPRESENTATION EXISTS FOR ANY OF THE THREE BIDIRECTIONAL METHODS.
    /// </summary>
    /// <param name="candidate">A plausible REST spelling of one of them.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>404, NOT A DEGRADED FORM.</b> A REST surrogate for an ordered, vetoable conversation would
    /// silently change its semantics: independent requests lose the ordering that
    /// <c>se_cst_dw.sru</c> partly encodes in the topic STRING itself, and flattening the tri-valued veto
    /// to a boolean would convert a deep prevention into a shallow one and let the events the caller meant
    /// to stop fire anyway. For the two inverted channels there is no request/response direction to
    /// project at all - the server asks and the client answers.
    /// </para>
    /// <para>
    /// A PARTIAL PROJECTION WOULD BE WORSE THAN NONE, because a consumer would receive part of an ordered
    /// chain with no way to know what it had missed. Consumers needing any of the three use this service's
    /// gRPC surface directly; that is a documented gap, deliberately preferred over an invented
    /// representation.
    /// </para>
    /// <para>
    /// ASSERTED UNDER AN AUTHENTICATED CLIENT, so a 404 cannot be a 401 wearing a different number - the
    /// absence of the route is what is being proved, not the presence of the credential requirement.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnprojectableRouteCandidates))]
    public async Task NoRestRouteExistsForABidirectionalMethodAsync(string candidate)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            candidate,
            new { sessionId = "unused" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The three bidirectional methods appear nowhere in the served contract description either.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>SPELLING-INDEPENDENT, WHICH IS WHY IT COMPLEMENTS THE ROUTE ROWS ABOVE.</b> Those rows check the
    /// names a later implementer might choose; this one checks the METHOD each operation actually projects,
    /// read from its own <c>x-grpc-method</c> extension. A surrogate under any name at all would appear
    /// here, because a projected operation cannot exist without declaring the method it projects.
    /// </remarks>
    [Fact]
    public async Task NoProjectedOperationNamesABidirectionalMethodAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = await RestProjection.DocumentAsync(served);

        IReadOnlyList<string> projected = RestProjectionContract.GeneratedGrpcMethods(document);

        // Asserted so a document that served nothing cannot make the exclusion pass by finding nothing.
        Assert.NotEmpty(projected);

        foreach (string unprojected in RestProjectionContract.UnprojectedGrpcMethods)
        {
            Assert.DoesNotContain(unprojected, projected);
        }
    }

    /// <summary>
    /// No route on this listener names a capability area Phase 1 does not implement.
    /// </summary>
    /// <param name="deferred">A route under one of the four deferred areas.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// The auditable form of constraint C-D on this service: there is no project, no container, no test and
    /// no exception-throwing placeholder behind any of the four, and on this listener their paths match
    /// nothing. The reserved declarations that DO exist are Gateway's, on Gateway's own listener.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeferredAreaRoutes))]
    public async Task NoRouteOnThisListenerNamesADeferredCapabilityAreaAsync(string deferred)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            deferred,
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// 🔴 EVERY ROUTE THE SERVICE MAPS IS DECLARED BY THE AUTHORED CONTRACT, AND EVERY DECLARATION IS
    /// MAPPED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH DIRECTIONS, AS TWO SEPARATE FAILURES. An undeclared route is a surface a consumer cannot
    /// discover and that no reviewer approved; a declared-but-absent one is a surface a generated client
    /// will call and receive 404 from. Comparing only one direction would let either through.
    /// </para>
    /// <para>
    /// COMPARED BY METHOD AND PATH TOGETHER, because the projection maps the two session closes as
    /// <c>DELETE</c> and the gate read as <c>GET</c> while everything else is <c>POST</c> - a comparison on
    /// paths alone would accept a route that had silently changed verb, which a generated client would
    /// receive <c>405</c> from.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMappedRoutesAndTheAuthoredContractCorrespondExactly()
    {
        IReadOnlyList<ProjectedOperationDeclaration> declared = RestProjectionContract.Declared;
        IReadOnlyList<ProjectedOperationDeclaration> mapped = RestProjectionContract.Mapped(host);

        HashSet<string> declaredKeys = new(
            declared.Select(static declaration => declaration.Method + " " + declaration.Path),
            StringComparer.Ordinal);

        HashSet<string> mappedKeys = new(
            mapped.Select(static operation => operation.Method + " " + operation.Path),
            StringComparer.Ordinal);

        // Neither side may be empty, or the two comparisons below would agree about nothing.
        Assert.NotEmpty(declaredKeys);
        Assert.NotEmpty(mappedKeys);

        List<string> undeclared = [.. mappedKeys.Except(declaredKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.Empty(undeclared);

        List<string> unmapped = [.. declaredKeys.Except(mappedKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.Empty(unmapped);

        // And the counts agree, which catches a duplicate on either side that the two set differences
        // above would both have accepted.
        Assert.Equal(declaredKeys.Count, mappedKeys.Count);
    }

    /// <summary>
    /// The gRPC methods the served document names are exactly the ones the authored contract names.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// The route correspondence above compares ADDRESSES; this compares what each address projects. Both
    /// are needed: a route mapped at the right address to the wrong gRPC method would pass the first and
    /// fail this one, and it is the second mistake that would silently answer a caller from a different
    /// operation than the contract promised.
    /// </remarks>
    [Fact]
    public async Task TheProjectedGrpcMethodsAreExactlyTheOnesTheContractDeclaresAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = await RestProjection.DocumentAsync(served);

        HashSet<string> generated = new(
            RestProjectionContract.GeneratedGrpcMethods(document),
            StringComparer.Ordinal);

        HashSet<string> declared = new(
            RestProjectionContract.Declared.Select(static declaration => declaration.GrpcMethod),
            StringComparer.Ordinal);

        Assert.NotEmpty(generated);
        Assert.Equal(declared, generated);
    }

    /// <summary>
    /// 🔴 THE PROJECTION IS THIN: THE SAME OPERATION ANSWERS IDENTICALLY OVER gRPC AND OVER REST.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE MESSAGES ARE COMPARED, NOT THEIR RENDERINGS.</b> One operation is driven twice - once through
    /// the generated gRPC client over the real channel, once through the REST projection - and the two
    /// answers are bound to the SAME generated type and compared as messages. Any business decision the
    /// projection made of its own would show up as a difference: a defaulted field, a normalised value, a
    /// re-worded diagnostic, a dropped structured error.
    /// </para>
    /// <para>
    /// AN OPERATION WITH NO SIDE EFFECT IS CHOSEN, so driving it twice is genuinely the same question asked
    /// twice. A mutating operation would make the second answer a function of the first and the comparison
    /// would prove nothing.
    /// </para>
    /// <para>
    /// AND THE FAILING ARM IS THE ONE COMPARED, because that is where a projection is most tempted to add
    /// something of its own. The in-band failure travels on the problem body's relayed-response member,
    /// which is where a REST caller reads the contract's own answer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheProjectionIsThinAndAnswersWhatTheGrpcSurfaceAnswersAsync()
    {
        WireGetRowSelectStateRequest request = new()
        {
            DatawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName,
        };

        WireGetRowSelectStateResponse overGrpc = await host
            .CreateDataWindowClient()
            .GetRowSelectStateAsync(
                request,
                cancellationToken: TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RowSelectStateRoute,
            new { datawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName });

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        WireGetRowSelectStateResponse overRest =
            RestProjection.Bind<WireGetRowSelectStateResponse>(body, RestProjection.ResponseMember);

        Assert.Equal(overGrpc, overRest);

        // The structured error is part of that equality and is named here so a failure says which half
        // moved rather than only that the messages differ.
        Assert.NotNull(overRest.Error);
        Assert.Equal(overGrpc.Error.Text, overRest.Error.Text);
        Assert.Equal(overGrpc.Error.Localized, overRest.Error.Localized);
        Assert.Equal(overGrpc.Error.Category, overRest.Error.Category);
        Assert.Equal(overGrpc.Error.Severity, overRest.Error.Severity);
        Assert.Equal(overGrpc.RetCode, overRest.RetCode);
    }

    /// <summary>
    /// A projected server stream is the ordered sequence itself, with its chunking contract intact.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// A BARE ARRAY AND NOT AN ENVELOPE, because the protocol definition declares no envelope message for a
    /// stream: wrapping one here would add a member the gRPC contract does not have and the two halves of
    /// the boundary would stop corresponding.
    /// </para>
    /// <para>
    /// AND THE CHUNKING TRAVELS UNCHANGED. Each element keeps its own one-based chunk index, its final
    /// flag, its own row count and the running cumulative count, so the LAST element is identifiable AS the
    /// last rather than inferred from the collection ending - which is what makes a truncated array
    /// distinguishable from a complete one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AProjectedServerStreamIsTheOrderedSequenceWithItsChunkingIntactAsync()
    {
        await using DataServicesTestHostFactory scoped = RestProjection.Host(out _);

        scoped.PersistenceEdge.Reset();
        scoped.PersistenceEdge.ScriptQuery(rowCount: 2L);

        using HttpClient client = scoped.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RetrieveRoute,
            new { datawindowHandle = RestProjection.UnresolvedHandle });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.NotEqual(0, body.RootElement.GetArrayLength());

        long expectedIndex = 1L;
        bool sawFinal = false;

        foreach (JsonElement element in body.RootElement.EnumerateArray())
        {
            Assert.Equal(
                expectedIndex,
                long.Parse(
                    element.GetProperty("chunkIndex").GetString() ?? "0",
                    CultureInfo.InvariantCulture));

            expectedIndex++;

            sawFinal = element.TryGetProperty("final", out JsonElement final) && final.GetBoolean();

            Assert.True(element.TryGetProperty("cumulativeRowCount", out _));
        }

        Assert.True(
            sawFinal,
            "The last element of a projected stream must carry its own final flag, so the last chunk is "
            + "identifiable as the last rather than inferred from the array ending - which is what makes a "
            + "truncated array distinguishable from a complete one.");
    }

    /// <summary>
    /// Every projected row carries its <c>originalValues</c> member, with one entry per column on a
    /// retrieved row and an EMPTY array on an insert-shaped one.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE BASELINE MEMBER THAT WAS DESCRIBED AS NORMALLY ABSENT, ASSERTED WHERE IT ACTUALLY
    /// REACHES A CALLER.</b> A revision described <c>originalValues</c> as normally ABSENT on a retrieved
    /// row, on the reasoning that a consumer could infer the baseline from the current value. AAP 0.6.3.2 states
    /// the obligation with no exemption - per row, both the current and the original value of every marked
    /// column - and the fixture is why: all six columns are marked <c>updatewhereclause=yes</c> under
    /// <c>updatewhere=1</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so all six
    /// originals go into the generated WHERE clause. Absence had two legal readings and one of them drops
    /// the predicate entirely.
    /// </para>
    /// <para>
    /// <b>THE INSERT ROW IS THE HALF THAT PROVES THE GUARANTEE IS SATISFIABLE AT ALL.</b> A repeated
    /// protobuf field has no explicit presence, so an EMPTY one is a default value - which the canonical
    /// JSON mapping omits. If this projection used that mapping it could not emit the member for the one
    /// row that legitimately has no prior state. The projection formats default values precisely so the
    /// member appears as <c>[]</c>, and this row is where that is checked rather than asserted in a
    /// comment.
    /// </para>
    /// <para>
    /// <b>WHY THIS IS A TEST RATHER THAN A <c>required</c> LIST.</b> <c>DataWindowRow</c> travels in a
    /// REQUEST as well as a response, and the projection's strict canonical parser reads an absent member
    /// as its default - so declaring the member required on the published schema would advertise a check
    /// nothing performs. The obligation on a caller is conditional on the row and is enforced at run time
    /// by <c>Validators/UpdateRowValidator</c>; the obligation on the SERVER is unconditional and is what
    /// this test pins. <c>ConflictRow</c>, which travels only outbound, does declare both value sets
    /// required.
    /// </para>
    /// <para>
    /// READ THROUGH THE HTTP BOUNDARY, not off a message object, because the claim is about what a caller
    /// receives. A message-level assertion would pass while the formatter dropped the member.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProjectedRowCarriesItsOriginalBaselineMemberAsync()
    {
        await using DataServicesTestHostFactory scoped = RestProjection.Host(out _);

        scoped.PersistenceEdge.Reset();
        scoped.PersistenceEdge.ScriptQueryMessages(
            ScriptedPersistenceResponses.QueryStreamCarryingBaselinedAndInsertedRows());

        using HttpClient client = scoped.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RetrieveRoute,
            new { datawindowHandle = RestProjection.UnresolvedHandle });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        JsonElement rows = body.RootElement[0].GetProperty("rows");

        Assert.Equal(2, rows.GetArrayLength());

        // ---- The baselined row: the member is present, and it covers every column ----

        JsonElement baselined = rows[0];

        Assert.True(
            baselined.TryGetProperty("originalValues", out JsonElement baselinedOriginals),
            "A retrieved row must carry its originalValues member. It is the baseline the updatewhere=1 "
            + "predicate compares against, and a caller that received none could not construct a correct "
            + "update at all.");

        JsonElement baselinedColumns = baselined.GetProperty("columns");

        Assert.Equal(6, baselinedColumns.GetArrayLength());
        Assert.Equal(baselinedColumns.GetArrayLength(), baselinedOriginals.GetArrayLength());

        // ONE BASELINE PER COLUMN, MATCHED BY IDENTIFIER RATHER THAN BY POSITION. Equal lengths alone
        // would be satisfied by six originals for one column, which is the shape a partial producer
        // produces.
        foreach (JsonElement column in baselinedColumns.EnumerateArray())
        {
            string columnId = column.GetProperty("columnId").GetString() ?? string.Empty;

            Assert.Contains(
                baselinedOriginals.EnumerateArray(),
                original => string.Equals(
                    original.GetProperty("columnId").GetString(),
                    columnId,
                    StringComparison.Ordinal));
        }

        // ---- The insert row: the member is present and EMPTY, which is a different claim ----

        JsonElement inserted = rows[1];

        Assert.True(
            inserted.TryGetProperty("originalValues", out JsonElement insertedOriginals),
            "An insert-shaped row must still carry the member, as an empty array. `required` constrains "
            + "PRESENCE and not length, and only formatting default values keeps an empty repeated field "
            + "expressible - without it the projection could not satisfy its own schema for the one row "
            + "that legitimately has no prior state.");

        Assert.Equal(JsonValueKind.Array, insertedOriginals.ValueKind);
        Assert.Equal(0, insertedOriginals.GetArrayLength());
    }
}


// =====================================================================================================
//  4. AUTHENTICATION ON EVERY ROUTE, AND THE ONE FIELD THAT MUST NOT LEAK
// =====================================================================================================

/// <summary>
/// A booted host whose Persistence edge is scripted for both halves of the triple, so a route sweep can
/// reach every operation without any of them refusing for want of a script.
/// </summary>
/// <remarks>
/// <para>
/// A CLASS FIXTURE BECAUSE A SWEEP IS THIRTY-NINE ROWS. Booting the composition root per row would multiply
/// one boot by thirty-nine for a property - "this route is not anonymous" - that has nothing to do with which host
/// answers it. Nothing in the sweep mutates host-wide state: the scripts are set once here and read, never
/// rewritten.
/// </para>
/// <para>
/// SCRIPTED RATHER THAN LEFT BARE, DELIBERATELY. The shared double refuses an unscripted operation loudly,
/// which is the right default and the wrong thing for a sweep: a refusal for want of a script would be
/// indistinguishable, in a row asserting "not 401", from a refusal for want of a credential.
/// </para>
/// </remarks>
public sealed class PreparedProjectionHostFixture : IAsyncDisposable
{
    /// <summary>Boots the host and scripts both halves of the retrieval-and-update pair.</summary>
    public PreparedProjectionHostFixture()
    {
        Host = RestProjection.Host(out PrepareUpdateRecorder prepare);
        Prepare = prepare;

        Host.PersistenceEdge.ScriptQuery(rowCount: 1L);

        _ = Host.PersistenceEdge.ScriptUpdateSuccess(rowsUpdated: 1L);
    }

    /// <summary>The booted host.</summary>
    internal DataServicesTestHostFactory Host { get; }

    /// <summary>The prepare script and record the registration captured.</summary>
    internal PrepareUpdateRecorder Prepare { get; }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        return Host.DisposeAsync();
    }
}

/// <summary>
/// Every projected route requires a credential, and none of them is anonymous by omission.
/// </summary>
/// <param name="fixture">The shared, scripted host.</param>
/// <remarks>
/// <para>
/// <b>CONSTRAINT C-G, ENUMERATED FROM THE CONTRACT RATHER THAN FROM A LIST.</b> The rows come from
/// <c>gateway.v1.yaml</c>, so a route added to this projection later cannot be silently unauthenticated:
/// the moment it is declared it acquires a row here. Authorization is required once at the group level in
/// the implementation, which is what makes that structurally true - and this sweep is what proves the
/// structure held.
/// </para>
/// <para>
/// <b>BOTH POSTURES, BECAUSE THE REFUSAL ROW ALONE IS SATISFIABLE BY A BROKEN ROUTE.</b> Posture A - no
/// credential - must be 401. Posture B - a principal the service accepts - must NOT be refused. A route
/// protected by breaking it would pass the first and fail the second.
/// </para>
/// <para>
/// POSTURE A's 401 COMES FROM THE STOCK BEARER HANDLER, not from anything in the fixture: with the test
/// principal header absent the scheme selector forwards to the real handler, so an anonymous request is
/// authenticated, challenged and refused by the deployed pipeline exactly as it would be in a container.
/// </para>
/// </remarks>
public sealed class RestProjectionAuthorizationTests(PreparedProjectionHostFixture fixture)
    : IClassFixture<PreparedProjectionHostFixture>
{
    /// <summary>A session identifier no registry holds, for the two session-scoped routes.</summary>
    /// <remarks>
    /// It has to be SOMETHING, because the identifier is a required route or query argument; it must name
    /// nothing, because a sweep asserting an authorization property must not depend on server-side state.
    /// </remarks>
    private const string UnknownSession = "no-such-session";

    /// <summary>Every projected operation the authored contract declares.</summary>
    /// <returns>One row per operation.</returns>
    public static TheoryData<string, string, string> Operations() =>
        RestProjectionContract.DeclaredOperations();

    /// <summary>
    /// 🔴 EVERY PROJECTED ROUTE IS REFUSED WITH 401 WHEN NO CREDENTIAL IS PRESENTED.
    /// </summary>
    /// <param name="method">The HTTP method the contract declares.</param>
    /// <param name="path">The route.</param>
    /// <param name="grpcMethod">The gRPC method it projects, named so a failure reads as its operation.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// 401 SPECIFICALLY AND NOT MERELY "NOT 200". A 403 here would mean an unauthenticated caller had been
    /// told its PERMISSIONS were the problem, and a 404 would mean the route was missing rather than
    /// protected - both would satisfy a weaker assertion while describing a different service.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryProjectedRouteRefusesAnUncredentialedCallerAsync(
        string method,
        string path,
        string grpcMethod)
    {
        using HttpClient client = fixture.Host.CreateAnonymousClient();

        using HttpRequestMessage request = Build(method, path);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.False(
            string.IsNullOrEmpty(grpcMethod),
            $"{method} {path} declares no gRPC method, so the contract and the projection have drifted.");
    }

    /// <summary>
    /// Every projected route accepts a credentialed caller rather than refusing it.
    /// </summary>
    /// <param name="method">The HTTP method the contract declares.</param>
    /// <param name="path">The route.</param>
    /// <param name="grpcMethod">The gRPC method it projects.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE ASSERTION IS THE ABSENCE OF A REFUSAL, NOT A SUCCESS, AND THAT IS THE PRECISE PROPERTY.</b>
    /// Most of these operations need a session, a handle or a payload this sweep deliberately does not
    /// supply, so a success would be the wrong claim; what matters is that authentication and authorization
    /// stopped refusing and the request reached the handler. Requiring 200 here would force the sweep to
    /// arrange thirty-nine different scenarios and would test something else entirely.
    /// </para>
    /// <para>
    /// AND 403 IS EXCLUDED ALONGSIDE 401, because the fixture's principal carries BOTH published scopes -
    /// which is what Gateway's own credential carries, since a single credential is attached to every call
    /// it makes. A 403 here would mean a route required a scope the contract does not publish.
    /// </para>
    /// <para>
    /// <b>404 IS NOT EXCLUDED, AND THAT IS A CORRECTION RATHER THAN A CONCESSION.</b> Eight of these
    /// operations answer <c>404</c> for a legitimate reason on the empty payload this sweep sends: a blank
    /// DataWindow handle names nothing, which is <c>RetCode.E_INVALID_HANDLE</c> IN BAND and the published
    /// in-band table sends that to 404. Excluding it would make the sweep fail on correct behaviour. Route
    /// EXISTENCE is a different property and is owned by
    /// <c>RestProjectionSurfaceTests.TheMappedRoutesAndTheAuthoredContractCorrespondExactly</c>, which
    /// compares the whole route table against the whole contract and is the right place for it.
    /// </para>
    /// <para>
    /// 405 IS EXCLUDED, HOWEVER, because it is reachable here and means something this sweep can see: the
    /// route exists at this address under a DIFFERENT verb from the one the contract declares, which a
    /// generated client would receive as a hard refusal.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryProjectedRouteAcceptsACredentialedCallerAsync(
        string method,
        string path,
        string grpcMethod)
    {
        using HttpClient client = fixture.Host.CreateAuthenticatedClient();

        using HttpRequestMessage request = Build(method, path);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        // The verb the contract declares is the verb the route answers.
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        Assert.False(string.IsNullOrEmpty(grpcMethod));
    }

    /// <summary>
    /// A credential that authenticates but carries the wrong scope is refused with 403, not 401.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// THE THIRD POSTURE, AND IT IS WHAT MAKES THE FIRST TWO MEAN SOMETHING. Without it, a route that
    /// accepted any authenticated principal at all would pass both sweeps above. The distinction between a
    /// MISSING credential and an INSUFFICIENT one is the distinction the two statuses exist to draw.
    /// </remarks>
    [Fact]
    public async Task AnInsufficientlyScopedCredentialIsForbiddenRatherThanUnauthorizedAsync()
    {
        using HttpClient client = fixture.Host.CreateScopedClient(["dataservices.not-a-published-scope"]);

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Builds the request for one declared operation.</summary>
    /// <param name="method">The declared HTTP method.</param>
    /// <param name="path">The declared route, possibly carrying a session template.</param>
    /// <returns>The request.</returns>
    /// <remarks>
    /// A BODY ONLY WHERE THE CONTRACT DECLARES ONE. The two session closes and the gate read accept no
    /// request body, and sending one would exercise a shape the contract does not have.
    /// </remarks>
    private static HttpRequestMessage Build(string method, string path)
    {
        string address = path.Replace("{sessionId}", UnknownSession, StringComparison.Ordinal);

        HttpRequestMessage request = new(
            new HttpMethod(method),
            RestProjection.Relative(address));

        if (string.Equals(method, HttpMethod.Post.Method, StringComparison.Ordinal))
        {
            request.Content = new StringContent(
                "{}",
                System.Text.Encoding.UTF8,
                MediaTypeNames.Application.Json);
        }

        return request;
    }
}

/// <summary>
/// The statement field is relayed as it arrived, never reconstructed and never recorded, and no response
/// body carries anything credential-shaped.
/// </summary>
/// <remarks>
/// <para>
/// <b>CONSTRAINT C-F, AND THE OBLIGATION HERE IS NON-LEAKAGE RATHER THAN RE-REDACTION.</b>
/// <c>common.v1.DbError.sqlsyntax</c> carries statement text. In the legacy it carried the COMPLETE
/// generated statement INCLUDING interpolated literal values - the mechanical root being a connection
/// parameter that disables bind variables
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>] - and the legacy
/// logger performed no redaction at all. Redaction is Persistence's responsibility and happens BEFORE the
/// value reaches this service, so what these rows assert is that the projection relays it untouched,
/// builds no second redactor, and writes it nowhere.
/// </para>
/// <para>
/// <b>NO SECOND REDACTOR IS THE POINT OF THE FIRST ROW.</b> Two redactors would be two rules free to
/// diverge, and a caller could not tell which had run. An unredacted statement ARRIVING would be a defect
/// in Persistence to report, not something to paper over here.
/// </para>
/// </remarks>
public sealed class RestProjectionRedactionTests
{
    /// <summary>
    /// A relayed database error keeps its statement field exactly as the upstream sent it.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE SCRIPTED STATEMENT CARRIES PLACEHOLDERS ONLY, which is the shape Persistence's own redactor
    /// produces, and the assertion is that it arrives byte for byte. A projection that re-interpolated it,
    /// enriched it, or merged parameter values back in would fail on the equality; one that dropped it
    /// would fail on the presence.
    /// </para>
    /// <para>
    /// AND NO LITERAL VALUE APPEARS ANYWHERE IN THE BODY. The row values the request carried are absent
    /// from the statement and absent from the problem prose, so a caller cannot reconstruct the statement
    /// from what it received either.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARelayedDatabaseErrorKeepsItsStatementFieldExactlyAsItArrivedAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        // Placeholders only - the shape Persistence's own redactor emits. Nothing here is a credential and
        // nothing here is a literal value.
        DbError redacted = new()
        {
            Sqldbcode = 19L,
            Sqlerrtext = "UNIQUE constraint failed",
            Sqlsyntax = "UPDATE COMPANY SET age = ? WHERE id = ? AND age = ?",
            Buffer = DwBuffer.Primary,
            Row = 1L,
        };

        host.PersistenceEdge.UpdateResponse = new PersistenceUpdateResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = (WireRetCode)(int)KernelRetCode.E_DB_ERROR,
                ErrorText = "更新失败",
                DbError = redacted,
            },
            Counts = new PersistenceUpdateCounts(),
        };

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        WireUpdateResponse relayed =
            RestProjection.Bind<WireUpdateResponse>(body, RestProjection.ResponseMember);

        Assert.NotNull(relayed.Error);

        // Byte for byte: not re-redacted, not enriched, not reconstructed.
        Assert.Equal(redacted, relayed.Error);
        Assert.Equal(redacted.Sqlsyntax, relayed.Error.Sqlsyntax);

        string raw = await RestProjection.BodyAsync(response);

        // And no row value the caller sent, nor any value a statement could have interpolated, appears in
        // the body - so the statement cannot be reassembled from what was returned.
        foreach (string literal in new[] { "not-a-number", "Marischal College", "1815-12-10", "1200.00" })
        {
            Assert.DoesNotContain(literal, raw, StringComparison.Ordinal);
        }

        // THE LEGACY DIAGNOSTIC REACHES THE CALLER, AND IT REACHES IT ON THE FIELD THE CONTRACT DECLARES
        // FOR IT (constraint C-B). C-03's update response carries no free-text outcome field of its own -
        // only the outcome code and the database error - so the database error's own `sqlerrtext` IS the
        // diagnostic channel here, and `detail` is the projection's fixed prose. That division is what keeps
        // caller-supplied and driver-supplied text out of the one member that ends up in log records and
        // support tickets, while losing none of it.
        Assert.Equal(redacted.Sqlerrtext, relayed.Error.Sqlerrtext);

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("detail").GetString()));

        Assert.DoesNotContain(
            redacted.Sqlsyntax,
            body.RootElement.GetProperty("detail").GetString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The relayed statement is not written into the problem prose either.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>THE MEMBER IS WHERE A STATEMENT MAY TRAVEL, AND THE PROSE IS NOT.</b> A caller reads the error
    /// member deliberately; the <c>detail</c> string ends up in log records, error pages and support
    /// tickets, so copying the statement there would broadcast it. Asserted separately from the equality
    /// above because a projection that appended it to the prose "for convenience" would satisfy that row
    /// and fail this one.
    /// </remarks>
    [Fact]
    public async Task TheRelayedStatementIsNotCopiedIntoTheProblemProseAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();

        const string statement = "DELETE FROM COMPANY WHERE id = ? AND salary = ?";

        host.PersistenceEdge.UpdateResponse = new PersistenceUpdateResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = (WireRetCode)(int)KernelRetCode.E_DB_ERROR,
                DbError = new DbError
                {
                    Sqldbcode = 1L,
                    Sqlerrtext = "constraint",
                    Sqlsyntax = statement,
                    Buffer = DwBuffer.Delete,
                    Row = 2L,
                },
            },
            Counts = new PersistenceUpdateCounts(),
        };

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.DoesNotContain(
            statement,
            body.RootElement.GetProperty("detail").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            statement,
            body.RootElement.GetProperty("title").GetString() ?? string.Empty,
            StringComparison.Ordinal);

        // It IS on the member the contract declares for it, so the assertion above is about placement
        // rather than about the value having been dropped.
        Assert.Equal(
            statement,
            RestProjection.Bind<WireUpdateResponse>(body, RestProjection.ResponseMember).Error.Sqlsyntax);
    }

    /// <summary>
    /// The relayed statement is never written to an operator record either.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE THIRD OBLIGATION, AND THE ONE A BODY ASSERTION CANNOT REACH.</b> Constraint C-F and AAP
    /// §0.6.6 require that the relayed statement is neither un-redacted, nor reconstructed, NOR WRITTEN TO
    /// A LOG. The two rows above discharge the first two. This one discharges the third, and it is the one
    /// that matters most operationally: a response is read once by the caller that asked for it, while a
    /// log record lands in a sink this service does not own, is retained far longer, and is read by people
    /// who never made the request. The implementation asserts the property in its own prose at
    /// <c>RenderInBandFailure</c>; a test is what keeps that prose true.
    /// </para>
    /// <para>
    /// THE STREAM IS ASSERTED NON-EMPTY FIRST. An absence assertion over an empty stream proves nothing, so
    /// the record count is checked before the absence, and the projection's own record - which carries the
    /// numeric outcome code and the route PATTERN, and nothing a caller supplied - is located by name to
    /// prove the sink saw the site under test rather than merely some unrelated startup chatter.
    /// </para>
    /// <para>
    /// EVERY DRIVER-SUPPLIED AND CALLER-SUPPLIED STRING IS COVERED, not only the statement: the driver's
    /// own error text, the upstream outcome prose, the identifier of the DataWindow the caller named and the
    /// row values the caller sent. Each is a value that a well-meant "log the failure with context" change
    /// would pull into a record, and each would be a C-F regression.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRelayedStatementIsNeverWrittenToAnOperatorRecordAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        ProjectionLogRecorder operatorLog = RestProjection.RecordOperatorLog(host);

        host.PersistenceEdge.Reset();

        const string statement = "UPDATE COMPANY SET salary = ? WHERE id = ? AND salary = ?";
        const string driverText = "UNIQUE constraint failed: COMPANY.id";
        const string upstreamProse = "更新失败";

        host.PersistenceEdge.UpdateResponse = new PersistenceUpdateResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = (WireRetCode)(int)KernelRetCode.E_DB_ERROR,
                ErrorText = upstreamProse,
                DbError = new DbError
                {
                    Sqldbcode = 19L,
                    Sqlerrtext = driverText,
                    Sqlsyntax = statement,
                    Buffer = DwBuffer.Primary,
                    Row = 3L,
                },
            },
            Counts = new PersistenceUpdateCounts(),
        };

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.UpdatePayload());

        // The statement did reach the caller on its declared member, so this scenario genuinely exercised
        // the relay rather than a path where there was nothing to leak.
        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            statement,
            RestProjection.Bind<WireUpdateResponse>(body, RestProjection.ResponseMember).Error.Sqlsyntax);

        ImmutableArray<string> records = operatorLog.Records;

        // Not vacuous: the sink is live and it saw the site under test.
        Assert.NotEmpty(records);

        Assert.Contains(
            records,
            record => record.Contains(RestProjection.ProjectionLoggerCategory, StringComparison.Ordinal));

        // And the record the projection wrote names the route PATTERN and the numeric code, both of which
        // are this service's own text.
        Assert.Contains(
            records,
            record => record.Contains(RestProjection.ProjectionLoggerCategory, StringComparison.Ordinal)
                && record.Contains(
                    ((long)KernelRetCode.E_DB_ERROR).ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal));

        // THE WHOLE REQUEST PATH, NOT ONLY THE PROJECTION'S OWN RECORD. The gRPC service writes its own
        // operator record for the same failure, so a leak there would be exactly as damaging; the sink
        // accepts every category precisely so this row covers both sites at once.
        foreach (string forbidden in new[] { statement, driverText, upstreamProse })
        {
            Assert.DoesNotContain(
                records,
                record => record.Contains(forbidden, StringComparison.Ordinal));
        }

        // AND THE DIVISION IS DELIBERATE RATHER THAN ACCIDENTAL. The record that exists carries this
        // service's OWN identifiers - the caller's DataWindow handle, the numeric outcome and the database
        // code - which is what makes a failure traceable at all; what it withholds is the statement. An
        // implementation that logged nothing would satisfy the absence above while leaving an operator
        // unable to correlate anything, so the presence of the traceable half is asserted too.
        string upstreamRecord = Assert.Single(
            records,
            record => record.Contains(
                RestProjection.UpstreamServiceLoggerCategory,
                StringComparison.Ordinal));

        Assert.Contains(RestProjection.UnresolvedHandle, upstreamRecord, StringComparison.Ordinal);
        Assert.DoesNotContain(statement, upstreamRecord, StringComparison.Ordinal);
        Assert.DoesNotContain(driverText, upstreamRecord, StringComparison.Ordinal);
    }

    /// <summary>
    /// A caller-supplied handle carrying line breaks cannot forge a second operator record, and an
    /// oversize one cannot flood the sink.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>WRITING THE HANDLE VERBATIM IS THE OBVIOUS IMPLEMENTATION, AND THE ATTACK ON IT NEEDS NO
    /// SOPHISTICATION.</b> The record above is the one that carries the caller's DataWindow handle -
    /// deliberately, because it is what makes a failure traceable. Every console, file and syslog
    /// provider renders a structured record to a LINE, so a handle containing a line break appends a
    /// COMPLETE fabricated record after the real one: same shape, same channel, carrying whatever severity,
    /// service name and outcome the caller chose to write into it. Nothing downstream can tell the two
    /// apart, which makes it a forgery rather than merely noise.
    /// </para>
    /// <para>
    /// <b>AND THE SECOND HALF IS SIZE.</b> The handle is read from a request body, so a caller chooses its
    /// length; one request carrying a multi-megabyte handle produces a multi-megabyte record, and a loop of
    /// them fills whatever the records are written to - taking the service down by way of its diagnostics
    /// rather than by way of its endpoints.
    /// </para>
    /// <para>
    /// BOTH HALVES ARE ASSERTED ON THE SAME REQUEST, because one rendering has to answer both and a fix
    /// that escaped without bounding, or bounded without escaping, would satisfy half of this row. The
    /// escaped form is asserted PRESENT as well as the raw form absent: a rendering that simply dropped the
    /// offending characters would pass an absence assertion while making two distinct handles render alike,
    /// so an operator could no longer tell which handle a failure involved.
    /// </para>
    /// <para>
    /// THE PAYLOAD TEXT IS ASSERTED PRESENT TOO. Escaping is not censorship: the caller's value stays
    /// readable and stays on one line, which is what keeps the record worth reading at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACallerSuppliedHandleCannotForgeOrFloodAnOperatorRecordAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        ProjectionLogRecorder operatorLog = RestProjection.RecordOperatorLog(host);

        host.PersistenceEdge.Reset();

        host.PersistenceEdge.UpdateResponse = new PersistenceUpdateResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = (WireRetCode)(int)KernelRetCode.E_DB_ERROR,
                DbError = new DbError { Sqldbcode = 19L, Buffer = DwBuffer.Primary, Row = 3L },
            },
            Counts = new PersistenceUpdateCounts(),
        };

        // The forged tail is shaped like a real record so that a rendering which let it through would
        // produce something a reader would act on rather than something obviously wrong. The oversize tail
        // is appended to the SAME value so one request exercises both bounds.
        const string ForgedTail = "fatal: the COMPANY table was dropped by dw-1";
        string handle = "dw-forged\r\n" + ForgedTail + "\u2028" + new string('h', 4096);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            new { datawindowHandle = handle });

        ImmutableArray<string> records = operatorLog.Records;

        // Not vacuous: the sink is live and it saw the site under test.
        Assert.NotEmpty(records);

        string upstreamRecord = Assert.Single(
            records,
            record => record.Contains(
                RestProjection.UpstreamServiceLoggerCategory,
                StringComparison.Ordinal));

        // 1. NO RAW LINE TERMINATOR SURVIVES, so the record cannot have been split. All three families are
        //    checked, including U+2028, which is NOT a control character by char.IsControl and is therefore
        //    exactly what a hand-written filter lets through.
        Assert.DoesNotContain("\r", upstreamRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", upstreamRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", upstreamRecord, StringComparison.Ordinal);

        // 2. THE ESCAPED FORM IS PRESENT, which is what keeps the rendering injective: two distinct handles
        //    still render distinctly, so a failure remains attributable to the handle that caused it.
        Assert.Contains("\\u000D\\u000A", upstreamRecord, StringComparison.Ordinal);

        // 3. THE VALUE IS BOUNDED AND THE ORIGINAL LENGTH IS RECORDED, so an operator can tell an unusual
        //    name from a payload aimed at the log.
        Assert.Contains(
            LogSafeText.TruncationPrefix
                + handle.Length.ToString(CultureInfo.InvariantCulture)
                + LogSafeText.TruncationSuffix,
            upstreamRecord,
            StringComparison.Ordinal);

        // 4. THE FORGED TAIL IS STILL THERE AND IS HARMLESS, which is the difference between escaping and
        //    censorship: the caller's value remains readable so an operator can see what was sent, and it
        //    is unambiguously PART OF the handle rather than a record of its own. Asserting its
        //    presence is what stops a future "just strip the newline and everything after it" from passing.
        Assert.Contains(ForgedTail, upstreamRecord, StringComparison.Ordinal);

        // 5. AND THE WHOLE RECORD IS ONE LINE, which is the property the forgery depended on breaking. This
        //    is asserted over the ENTIRE stream rather than the one record, because a leak through the
        //    resilience pipeline's category or the hosting layer's would be exactly as damaging.
        Assert.All(
            records,
            record => Assert.Single(record.Split('\n')));

        _ = response;
    }

    /// <summary>
    /// No response body on any exercised path carries anything credential-shaped.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// A SWEEP RATHER THAN A ROW PER PATH, because the property is uniform: no key, password, connection
    /// string, host credential or bearer token may appear in ANY body this projection produces, on a success
    /// or on a refusal. The patterns are the provider prefixes the secrets sweep names plus the
    /// credential-shaped words a hand-written diagnostic is most likely to reach for.
    /// </para>
    /// <para>
    /// THE FIXTURE'S OWN PRINCIPAL MARKER IS CHECKED TOO. It is not a credential - it authenticates
    /// nothing and has no dot-separated segments - but a body echoing the identity it was handed would be
    /// the mechanism by which a real credential would leak once one existed, so its absence is the
    /// property worth pinning.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoResponseBodyCarriesAnythingCredentialShapedAsync()
    {
        await using DataServicesTestHostFactory host = RestProjection.Host(out _);

        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 1L);

        _ = host.PersistenceEdge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            RestProjection.ConflictingRow(row: 1L));

        using HttpClient client = host.CreateAuthenticatedClient();

        List<string> bodies = [];

        foreach ((string Route, object Payload) exercised in new (string, object)[]
        {
            (RestProjection.UpdateRoute, RestProjection.UpdatePayload()),
            (RestProjection.RetrieveRoute, new { datawindowHandle = RestProjection.UnresolvedHandle }),
            (RestProjection.RowSelectStateRoute, new { datawindowHandle = RestProjection.PrimaryFixtureHandle }),
            (
                RestProjection.ColumnSortStateRoute,
                new { datawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName }),
        })
        {
            using HttpResponseMessage response = await RestProjection.PostAsync(
                client,
                exercised.Route,
                exercised.Payload);

            bodies.Add(await RestProjection.BodyAsync(response));
        }

        Assert.Equal(4, bodies.Count);

        foreach (string body in bodies)
        {
            Assert.NotEmpty(body);

            foreach (string pattern in new[]
            {
                "-----BEGIN",
                "Bearer ",
                "Authorization",
                "password",
                "Password",
                "signingKey",
                "SigningKey",
                "ConnectionString",
                "Data Source=",
                DataServicesTestHostFactory.TestPrincipalHeaderValue,
                DataServicesTestHostFactory.TestPrincipalSubject,
            })
            {
                Assert.DoesNotContain(pattern, body, StringComparison.Ordinal);
            }
        }
    }
}


// =====================================================================================================
//  5. THE STRUCTURED ERRORS THAT REPLACED THE LEGACY DIALOGS SURVIVE THE PROJECTION UNCHANGED
// =====================================================================================================

/// <summary>
/// A legacy dialog, re-expressed as a structured result, reaches a REST caller with everything it carried
/// - including the legacy's own localization inconsistency.
/// </summary>
/// <param name="host">The shared in-process host; nothing here needs the Persistence edge.</param>
/// <remarks>
/// <para>
/// <b>THE CENSUS IS NOT THIS FILE'S SUBJECT.</b> <c>StructuredErrorParityTests</c> owns the forty live
/// dialog sites, their locators, their exact texts and their substitution arguments. What is asserted here
/// is narrower and cannot be asserted there: that the PROJECTION does not alter any of it on the way out.
/// </para>
/// <para>
/// <b>THE LOCALIZATION SPLIT IS THE HARDEST PART TO GET RIGHT AND THE EASIEST TO "FIX" BY ACCIDENT.</b>
/// All 28 column-expression messages are hardcoded Chinese that do NOT route through the localization
/// layer, while the validation path's message DOES -
/// <c>MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"), sErrMsg, StopSign!)</c> at
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L357</c>]. That is an accident of where the
/// code was written rather than a pattern, and harmonizing it would change observable output, which is
/// exactly the silent correction the mandate forbids. So one localized site and one non-localized site are
/// driven through the projection and the difference between them is required to survive.
/// </para>
/// <para>
/// AND ONLY THE DELIVERY CHANNEL CHANGED. A dialog became a machine-readable result; the text, the
/// localization category, the substitution arguments, the severity and - for a parse failure - the
/// expression and the NUMERIC caret position are all still there.
/// </para>
/// </remarks>
public sealed class RestProjectionStructuredErrorRelayTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// 🔴 A LOCALIZED SITE SURVIVES THE PROJECTION WITH ITS CATEGORY, SEVERITY AND FLAG INTACT.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// The validation refusal reproduces the dialog at <c>se_cst_dw.sru:L355</c> and <c>:L357</c>, which
    /// localizes its BODY and its TITLE through two separate lookups against <c>CAT_DWSVC</c>. Every one of
    /// those properties is asserted, and each fails differently: a dropped flag would make translatable
    /// text look untranslatable, a zeroed category would lose which table translated it, a downgraded
    /// severity would change how a consumer presents it, and an empty title would lose a translated string
    /// that the oracle looks up on its own.
    /// </para>
    /// <para>
    /// THE <c>Sprintf</c> ARGUMENT CHANNEL IS ASSERTED TOO, AND ASSERTED EMPTY. The oracle's validation
    /// message takes no substitution, so an empty argument list is the correct answer and pinning it is what
    /// stops the projection filling the channel with something the legacy never substituted. The
    /// complementary non-empty case lives on the column-expression row, so the channel is exercised in both
    /// states.
    /// </para>
    /// <para>
    /// AND IT REACHES THE CALLER THROUGH THE PROBLEM BODY'S RELAYED RESPONSE, bound back onto the generated
    /// type - so the assertion is about the CONTRACT's shape rather than about names this file chose.
    /// </para>
    /// <para>
    /// THE REFUSAL TOUCHED NO UPSTREAM, which is asserted here as well because it is observable from the
    /// same response: the validator runs ahead of the work scope, so a refused payload opens no session,
    /// creates no task and takes no pooled-transaction reference.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ALocalizedStructuredErrorSurvivesTheProjectionAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.RejectedUpdatePayload());

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        WireUpdateResponse relayed =
            RestProjection.Bind<WireUpdateResponse>(body, RestProjection.ResponseMember);

        Assert.Equal(WireRetCode.EInvalidData, relayed.RetCode);

        WireRowValidationError rejected = Assert.Single(relayed.ValidationErrors);

        // The address travels beside the message, so a consumer reads which column was refused without
        // parsing prose - the oracle substitutes no argument into this particular message.
        Assert.Equal("age", rejected.ColumnName);
        Assert.Equal(3L, rejected.ColumnId);
        Assert.Equal(DwBuffer.Primary, rejected.Buffer);
        Assert.Equal(1L, rejected.Row);

        WireStructuredError error = rejected.Error;

        Assert.NotNull(error);

        // 1 - LOCALIZED, and 2 - through the category the oracle names.
        Assert.True(
            error.Localized,
            "The validation path's message routes through I18N in the oracle [se_cst_dw.sru:L355, :L357], "
            + "so its localized flag must be true. A false flag here would tell a consumer that "
            + "translatable text is untranslatable.");

        Assert.Equal(Categories.CAT_DWSVC, error.Category);

        // 3 - the severity the oracle raises the dialog with.
        Assert.Equal(WireSeverity.StopSign, error.Severity);

        // 4 - and both strings are present. The title is a SEPARATE lookup in the oracle, so an empty one
        //     would mean a translated string had been lost rather than merely not shown.
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        Assert.False(string.IsNullOrWhiteSpace(error.Title));

        // 5 - the outcome code the refusal carries, on the error itself as well as on the response.
        Assert.Equal(WireRetCode.EInvalidData, error.RetCode);

        // 6 - AND THE SUBSTITUTION CHANNEL IS PRESENT AND EMPTY, WHICH IS ITS OWN ASSERTION.
        //
        //     The oracle's validation message is a fixed string with no `Sprintf` placeholder, so the
        //     argument list must be empty - and asserting THAT is what stops the projection filling the
        //     channel with something the legacy never substituted. The complementary non-empty case is
        //     asserted on the column-expression row below, which does carry a substituted variable name, so
        //     the channel is exercised in both states rather than only in the state that happens to be
        //     easier to satisfy.
        Assert.Empty(error.FormatArgs);

        // AND NEITHER TEXT NOR ARGUMENTS CARRIES A .NET EXCEPTION'S OWN DETAIL (CWE-209).
        foreach (string runtimeDetail in new[] { "System.", "Exception", "   at " })
        {
            Assert.DoesNotContain(runtimeDetail, error.Text, StringComparison.Ordinal);

            Assert.DoesNotContain(
                error.FormatArgs,
                argument => argument.Contains(runtimeDetail, StringComparison.Ordinal));
        }

        // The refusal reached no upstream. The validator sits ahead of the work scope deliberately, so a
        // malformed payload costs no round trip and holds no upstream resource.
        Assert.Equal(0, host.PersistenceEdge.UpdateCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// 🔴 A NON-LOCALIZED COLUMN-EXPRESSION SITE SURVIVES WITH ITS EXPRESSION AND NUMERIC CARET INTACT.
    /// </summary>
    /// <param name="expression">The expression as authored, sigils intact.</param>
    /// <param name="expectedCaret">The one-based caret position the legacy builder computes.</param>
    /// <param name="expectedSubstitution">
    /// The single value the legacy substitutes into the message through <c>Sprintf</c>, or an empty string
    /// on the row whose message takes no argument. Empty rather than <see langword="null"/> so the row reads
    /// as "no substitution" rather than as "not asserted".
    /// </param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE CARET IS A NUMBER ON THE WIRE AND MUST STAY ONE.</b> The legacy builder renders a marker by
    /// sizing an underscore run with <c>LenA</c> - the BYTE length, so a Chinese character advances the
    /// caret by two [<c>n_cst_dwsvc_columnexp.sru:L2402-L2407</c>]. A consumer that recomputed the marker
    /// from a CHARACTER count would place it wrongly, which is why both forms travel: the rendered marker
    /// for byte-exact comparison and the numeric position for a consumer's own rendering. The projection
    /// must carry both and re-render neither.
    /// </para>
    /// <para>
    /// <b>AND THE FLAG MUST BE FALSE.</b> This engine's messages are hardcoded Chinese that do not route
    /// through localization, in deliberate contrast with the validation path's message which does. The
    /// difference is preserved rather than harmonized, and the flag is what carries it WITHOUT any message
    /// changing.
    /// </para>
    /// <para>
    /// TWO ROWS, AND THE SECOND IS THE ONE WITH ARGUMENTS. A parse failure naming an undefined variable
    /// substitutes that name, so its argument list is non-empty and its caret sits past the sigil - which
    /// exercises the substitution channel as well as the position.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("$", 1L, "")]
    [InlineData("$missing + 1", 5L, "missing")]
    public async Task ANonLocalizedExpressionErrorSurvivesWithItsCaretIntactAsync(
        string expression,
        long expectedCaret,
        string expectedSubstitution)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        (string sessionId, string handle) = await RestProjection.OpenExpressionSessionAsync(client);

        Assert.False(string.IsNullOrEmpty(sessionId));
        Assert.False(string.IsNullOrEmpty(handle));

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.AddExpressionRoute,
            new
            {
                sessionId,
                datawindowHandle = handle,
                columnName = "n3",
                exp = expression,
            });

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        WireAddExpressionResponse relayed =
            RestProjection.Bind<WireAddExpressionResponse>(body, RestProjection.ResponseMember);

        WireExpressionError failure = relayed.Error;

        Assert.NotNull(failure);

        // 1 - THE EXPRESSION TEXT TRAVELS, so a consumer can render its own marker against the same string
        //     the engine parsed.
        Assert.Contains(expression, failure.Expression, StringComparison.Ordinal);

        // 2 - AND THE CARET IS THE NUMBER THE LEGACY COMPUTES, one-based.
        Assert.Equal(expectedCaret, failure.CaretPosition);

        // 3 - and the legacy's own rendered marker travels beside it, carrying the position in parentheses
        //     exactly as the builder writes it - which is what a byte-exact characterization compares.
        Assert.Contains(
            "(" + expectedCaret.ToString(CultureInfo.InvariantCulture) + ")",
            failure.RenderedMarker,
            StringComparison.Ordinal);

        // 4 - it is classified as a parse failure rather than left unspecified.
        Assert.Equal(WireExpressionError.Types.Category.Parse, failure.Category);

        WireStructuredError error = failure.Error;

        Assert.NotNull(error);

        // 5 - NOT LOCALIZED, and carrying no category - the preserved inconsistency.
        Assert.False(
            error.Localized,
            "A column-expression message must NOT be marked localized: all 28 sites in that object are "
            + "hardcoded Chinese that do not route through I18N, in deliberate contrast with the "
            + "validation path's message which does. Harmonizing them would change observable output.");

        Assert.Equal(0L, error.Category);

        // 6 - with the oracle's own severity and its own non-empty text and title.
        Assert.Equal(WireSeverity.StopSign, error.Severity);

        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        Assert.False(string.IsNullOrWhiteSpace(error.Title));

        // 7 - AND THE SUBSTITUTION CHANNEL SURVIVES SEPARATELY FROM THE RENDERED STRING.
        //
        //     The legacy composes these messages through `Sprintf`, so the substituted VALUES are a second
        //     channel beside the rendered text - and the contract carries them as their own repeated field
        //     precisely so a consumer can re-render the message in another locale or another format without
        //     parsing the rendered string back apart. A projection that dropped the arguments would leave
        //     every one of these messages permanently frozen in the oracle's own wording.
        if (expectedSubstitution.Length == 0)
        {
            Assert.Empty(error.FormatArgs);
        }
        else
        {
            Assert.Equal(expectedSubstitution, Assert.Single(error.FormatArgs));

            // The two channels AGREE: the value carried as an argument is the value rendered into the
            // text. A payload whose argument list had drifted from its own message would satisfy the
            // presence check above and still mislead a consumer that re-rendered from the arguments.
            Assert.Contains(expectedSubstitution, error.Text, StringComparison.Ordinal);
        }

        // 8 - AND NEITHER CHANNEL CARRIES A .NET EXCEPTION'S OWN DETAIL (CWE-209). The contract states this
        //     obligation over BOTH `text` and `format_args`, because an argument list is the easier of the
        //     two to fill from a caught exception by accident - a defined error carries the fixed text the
        //     legacy showed plus the values the legacy substituted, and nothing the runtime invented.
        foreach (string runtimeDetail in new[]
        {
            nameof(InvalidOperationException),
            nameof(ArgumentException),
            nameof(NullReferenceException),
            "System.",
            "   at ",
        })
        {
            Assert.DoesNotContain(runtimeDetail, error.Text, StringComparison.Ordinal);

            Assert.DoesNotContain(
                error.FormatArgs,
                argument => argument.Contains(runtimeDetail, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The two sites differ in exactly the way the oracle differs, read from one run.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>THE SPLIT ASSERTED AS A COMPARISON RATHER THAN AS TWO SEPARATE FACTS.</b> The rows above each pin
    /// one site; this one pins the RELATIONSHIP, which is what a harmonizing change would break: it would
    /// make both sites agree, and two independent rows can each be updated in isolation without anyone
    /// noticing that the distinction had gone. Driving both in one case makes the distinction itself the
    /// subject.
    /// </remarks>
    [Fact]
    public async Task TheLocalizationSplitBetweenTheTwoSitesIsPreservedEndToEndAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage localized = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.RejectedUpdatePayload());

        using JsonDocument localizedBody = await RestProjection.DocumentAsync(localized);

        WireStructuredError fromValidation =
            RestProjection.Bind<WireUpdateResponse>(localizedBody, RestProjection.ResponseMember)
                .ValidationErrors[0]
                .Error;

        (string sessionId, string handle) = await RestProjection.OpenExpressionSessionAsync(client);

        using HttpResponseMessage nonLocalized = await RestProjection.PostAsync(
            client,
            RestProjection.AddExpressionRoute,
            new { sessionId, datawindowHandle = handle, columnName = "n3", exp = "$" });

        using JsonDocument nonLocalizedBody = await RestProjection.DocumentAsync(nonLocalized);

        WireStructuredError fromEngine =
            RestProjection.Bind<WireAddExpressionResponse>(
                    nonLocalizedBody,
                    RestProjection.ResponseMember)
                .Error
                .Error;

        // The two flags DISAGREE, and that disagreement is the legacy's.
        Assert.True(fromValidation.Localized);
        Assert.False(fromEngine.Localized);
        Assert.NotEqual(fromValidation.Localized, fromEngine.Localized);

        // The category is meaningful only where the flag is set.
        Assert.Equal(Categories.CAT_DWSVC, fromValidation.Category);
        Assert.Equal(0L, fromEngine.Category);

        // And the severity does NOT differ: both are the oracle's stop sign, so the split is about
        // localization alone and not about how urgently either is presented.
        Assert.Equal(fromValidation.Severity, fromEngine.Severity);
    }

    /// <summary>
    /// No error body names a user-interface type, a dialog, or a stack frame.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>ONLY THE DELIVERY CHANNEL CHANGED, AND THIS IS THE ROW THAT SAYS SO FROM THE OTHER SIDE.</b> The
    /// legacy raised these through <c>MessageBox</c> and <c>MessageBoxEx</c>; the port carries their content
    /// and none of their mechanism. A body naming a dialog, a window, a message box or a severity
    /// enumeration spelled in PowerBuilder's own syntax would mean a presentation dependency had crossed a
    /// boundary that is API and service-level only - and the capability area that would own one is
    /// deferred, so naming it would breach constraint C-D as well.
    /// </para>
    /// <para>
    /// A STACK FRAME IS CHECKED IN THE SAME PLACE because it is the other thing an error body must not
    /// carry: the correlation identifier is what redirects a caller to the operator record, and that
    /// redirection is only defensible while the body itself discloses nothing (CWE-209).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoErrorBodyNamesAUserInterfaceTypeOrAStackFrameAsync()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        List<string> bodies = [];

        using HttpResponseMessage validation = await RestProjection.PostAsync(
            client,
            RestProjection.UpdateRoute,
            RestProjection.RejectedUpdatePayload());

        bodies.Add(await RestProjection.BodyAsync(validation));

        (string sessionId, string handle) = await RestProjection.OpenExpressionSessionAsync(client);

        using HttpResponseMessage parse = await RestProjection.PostAsync(
            client,
            RestProjection.AddExpressionRoute,
            new { sessionId, datawindowHandle = handle, columnName = "n3", exp = "$" });

        bodies.Add(await RestProjection.BodyAsync(parse));

        using HttpResponseMessage unknownHandle = await RestProjection.PostAsync(
            client,
            RestProjection.ContextMenuStateRoute,
            new { datawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName });

        bodies.Add(await RestProjection.BodyAsync(unknownHandle));

        Assert.Equal(3, bodies.Count);

        foreach (string body in bodies)
        {
            Assert.NotEmpty(body);

            foreach (string forbidden in new[]
            {
                "MessageBox",
                "MessageBoxEx",
                "StopSign!",
                "Exclamation!",
                "PowerObject",
                "DataWindowChild",
                "n_cst_popupmenu",
                "n_cst_font",
                "Win32",
                "   at ",
                ".cs:line",
                "System.InvalidOperationException",
            })
            {
                Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Every error body is machine-readable and carries a return-code-derived number rather than an ad-hoc
    /// one.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <param name="expected">The legacy outcome the body must carry.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE NUMBER IS THE POINT.</b> An HTTP status names a CLASS of problem and several legacy outcomes
    /// share each class, so the numeric code is what keeps the originating legacy validation identifiable.
    /// An ad-hoc number - an index, a sequence, an internal error identifier - would be unmappable back to
    /// the algebra a consumer already implements.
    /// </para>
    /// <para>
    /// AND THE MEDIA TYPE IS ASSERTED WITH IT, because a machine-readable code inside a body a client
    /// cannot recognise as a problem document is not machine-readable in practice: a client keys its
    /// parsing on the content type.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/datawindow/row-select/state", KernelRetCode.E_INVALID_HANDLE)]
    [InlineData("/v1/datawindow/context-menu/state", KernelRetCode.E_INVALID_HANDLE)]
    [InlineData("/v1/datawindow/drop-down-search/state", KernelRetCode.E_INVALID_HANDLE)]
    public async Task EveryErrorBodyCarriesAReturnCodeDerivedNumberAsync(string route, long expected)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            route,
            new { datawindowHandle = DataServicesTestHostFactory.UnboundDataWindowName });

        Assert.Equal(
            RestProjection.ProblemMediaType,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(
            expected,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

        // The four members a consumer branches on are all present, so the body is a problem document rather
        // than a bare number wearing the right media type.
        foreach (string member in new[] { "type", "title", "status", "detail" })
        {
            Assert.True(
                body.RootElement.TryGetProperty(member, out _),
                $"The error body for {route} omits the problem member '{member}'.");
        }
    }
}


// =====================================================================================================
//  THE PUBLISHED IN-BAND STATUS MAPPING, PINNED AS A TABLE
// =====================================================================================================

/// <summary>
/// Every arm of the published in-band <c>RetCode</c>-to-HTTP mapping, asserted directly.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>WHY THIS SUITE EXISTS, STATED AS THE DEFECT IT CLOSES.</b> This service's REST projection and the
/// ingress's proxy of the same operations are documented as EQUIVALENT: the same refusal must carry the
/// same HTTP status whichever surface a caller reached it through. Six codes broke that and nothing
/// noticed - <c>E_INVALID_DATA</c> and <c>E_INVALID_DATAOBJECT</c> were 400 at the ingress and 500 here,
/// <c>E_NOT_EXISTS</c>, <c>E_VAR_NOT_FOUND</c> and <c>E_MEMBER_NOT_FOUND</c> were 404 there and 500 here,
/// and <c>FAILED</c> - the oracle's own unspecific failure, which the projected methods really answer -
/// was 502 there and 500 here. Every one of them fell into a DEFAULT arm, so the divergence was invisible
/// from either side.
/// </para>
/// <para>
/// <b>THE TABLE IS WHY IT CANNOT DIVERGE AGAIN, AND DRIVING THE MAP DIRECTLY IS WHY THE TABLE CAN BE
/// COMPLETE.</b> Reaching the mapping through a deployed host exercises only the handful of outcomes a
/// real operation can be provoked into answering, which is exactly how six arms went unexercised. Calling
/// <c>InBandStatus.Project</c> makes every arm assertable, including the codes no operation here answers
/// and the unrecognised code that reaches the default.
/// </para>
/// <para>
/// <b>THE SAME TABLE IS DUPLICATED IN THE GATEWAY'S SUITE ON PURPOSE, AND HOISTING IT INTO THE CONTRACTS
/// PROJECT WOULD BE WRONG.</b> Constraint C-A permits exactly one thing to cross a service boundary - the
/// published contract definitions - and that project carries NO behaviour. A shared mapping table would
/// be behaviour, and a service reading another service's table would be the coupling the decomposition
/// exists to remove. So the table is stated twice, once per surface, and each copy names the other:
/// <c>PowerFramework.Gateway.Tests.ProxyStatusEquivalenceTests</c> is the twin. Two identical tables that
/// each fail loudly are the correct shape for an equivalence between two independently deployable
/// services.
/// </para>
/// <para>
/// THE PROSE IS DELIBERATELY NOT COMPARED. Each surface's fallback sentence names the surface a caller is
/// talking to - the ingress says "upstream" where this one does not - and either way the sentence is
/// replaced by the contract's own diagnostic whenever one was supplied, which is the case behaviour
/// preservation cares about (constraint C-B). The STATUS is the contract; the sentence is the courtesy.
/// </para>
/// </remarks>
public sealed class RestProjectionStatusEquivalenceTests
{
    /// <summary>
    /// The complete published mapping: every classified outcome and the status it must answer.
    /// </summary>
    /// <remarks>
    /// HELD AS A TUPLE ARRAY RATHER THAN ONLY AS THEORY DATA so that
    /// <see cref="TheTableCoversEveryClassifiedArm"/> can walk the same entries the theory runs.
    /// Projecting both from one declaration is what makes it impossible for the per-row assertion and the
    /// completeness guard to describe different sets and both pass. Each entry whose status is not the
    /// obvious one carries the reason it sits where it does.
    /// </remarks>
    private static readonly (long RetCode, int HttpStatus)[] PublishedMappingDeclarations =
    [
        // 400 - the caller's request is at fault and can be corrected.
        (KernelRetCode.E_INVALID_ARGUMENT, StatusCodes.Status400BadRequest),
        (KernelRetCode.E_INVALID_SQL, StatusCodes.Status400BadRequest),
        (KernelRetCode.E_OUT_OF_RANGE, StatusCodes.Status400BadRequest),
        (KernelRetCode.E_OUT_OF_BOUND, StatusCodes.Status400BadRequest),

        // The payload could not be applied - the caller's DATA, not this service.
        (KernelRetCode.E_INVALID_DATA, StatusCodes.Status400BadRequest),

        // A DataWindow name in the request BODY that resolves to nothing. 400 and not 404,
        // because the retrieval side answers the same mistake with E_INVALID_ARGUMENT.
        (KernelRetCode.E_INVALID_DATAOBJECT, StatusCodes.Status400BadRequest),

        // 403 - authenticated and refused.
        (KernelRetCode.E_ACCESS_DENIED, StatusCodes.Status403Forbidden),

        // 404 - the request named something that is not there.
        (KernelRetCode.E_INVALID_HANDLE, StatusCodes.Status404NotFound),
        (KernelRetCode.E_OBJECT_NOT_FOUND, StatusCodes.Status404NotFound),

        // All three are the same situation as the two above: a name with nothing behind it.
        (KernelRetCode.E_NOT_EXISTS, StatusCodes.Status404NotFound),
        (KernelRetCode.E_VAR_NOT_FOUND, StatusCodes.Status404NotFound),
        (KernelRetCode.E_MEMBER_NOT_FOUND, StatusCodes.Status404NotFound),

        // 409 - a definitive answer that may be retried. NEVER a silent overwrite.
        (KernelRetCode.E_RETRY, StatusCodes.Status409Conflict),

        // 429 - capacity, not correctness.
        (KernelRetCode.E_BUSY, StatusCodes.Status429TooManyRequests),

        // 504 - the operation ran out of budget.
        (KernelRetCode.E_TIME_OUT, StatusCodes.Status504GatewayTimeout),

        // 🔴 500 - an implemented operation with no implementation for the cell that was asked for. The two
        // rows asserted 501, and 501 is reserved system-wide for Gateway's four deferred-capability routes
        // (AAP 0.4.4, C-D): it says an entire capability area is unbuilt, which is false of every operation
        // this projection publishes. The retCode member is what names the absent cell.
        (KernelRetCode.E_NO_SUPPORT, StatusCodes.Status500InternalServerError),
        (KernelRetCode.E_NO_IMPLEMENTATION, StatusCodes.Status500InternalServerError),

        // 502 - the path behind this surface answered badly.
        (KernelRetCode.E_DB_ERROR, StatusCodes.Status502BadGateway),
        (KernelRetCode.E_INVALID_TRANSACTION, StatusCodes.Status502BadGateway),

        // The oracle's own unspecific failure, answered by an operation that COMPLETED. 502 and
        // not 500, because nothing on this side faulted - and it is what the ingress answers for it.
        (KernelRetCode.FAILED, StatusCodes.Status502BadGateway),
    ];

    /// <summary>Every classified outcome, projected onto theory rows.</summary>
    /// <returns>One row per classified outcome.</returns>
    public static TheoryData<long, int> PublishedMapping()
    {
        TheoryData<long, int> rows = [];

        foreach ((long retCode, int httpStatus) in PublishedMappingDeclarations)
        {
            rows.Add(retCode, httpStatus);
        }

        return rows;
    }

    /// <summary>
    /// Outcomes the map has NOT been taught, each of which must reach the default arm.
    /// </summary>
    /// <returns>One row per unclassified outcome.</returns>
    /// <remarks>
    /// THE FIRST FOUR ARE CODES THIS SERVICE'S OWN IMPLEMENTATIONS REALLY ANSWER and the map still does not
    /// classify - recorded here as a measured fact rather than fixed, because inventing a status for each
    /// would be this suite choosing a contract the finding did not ask for and the ingress does not
    /// publish either, which would REINTRODUCE the divergence in the opposite direction. The fifth is a
    /// number no <c>RetCode</c> declares, which is the case the default arm exists for.
    /// </remarks>
    public static TheoryData<long> UnclassifiedOutcomes() =>
    [
        KernelRetCode.E_INTERNAL_ERROR,
        KernelRetCode.E_OUT_OF_MEMORY,
        KernelRetCode.E_EVENT_NOT_FOUND,
        KernelRetCode.E_INVALID_OBJECT,
        -987_654L,
    ];


    /// <summary>Each classified outcome maps to exactly the published status.</summary>
    /// <param name="retCode">The failing in-band outcome.</param>
    /// <param name="expectedStatus">The status the published mapping declares for it.</param>
    [Theory]
    [MemberData(nameof(PublishedMapping))]
    public void AClassifiedOutcomeMapsToItsPublishedStatus(long retCode, int expectedStatus)
    {
        RestProjectionEndpoints.StatusProjection projected =
            RestProjectionEndpoints.InBandStatus.Project(retCode, errorText: null);

        Assert.Equal(expectedStatus, projected.HttpStatus);

        // The numeric outcome is carried through unchanged, so the specific legacy code stays identifiable
        // rather than being collapsed into the status that classifies it.
        Assert.Equal(retCode, projected.RetCode);

        // 🔴 NOT ATTRIBUTED TO AN UPSTREAM, AND THAT IS THE ONE PLACE THE TWO SURFACES CORRECTLY DIFFER.
        // The gateway marks an in-band refusal as coming from upstream because for the gateway it did: it
        // relayed an answer another service produced. This projection IS that service - the outcome was
        // produced in-process by the very implementation the route invokes - so claiming an upstream would
        // point an operator at Persistence for a refusal DataServices decided. The attribution differs; the
        // STATUS, which is what the equivalence claim covers, does not.
        Assert.False(projected.FromUpstream);
    }

    /// <summary>
    /// An outcome the map has not been taught reaches the default arm, and the default blames this side.
    /// </summary>
    /// <param name="retCode">The unclassified outcome.</param>
    /// <remarks>
    /// <b>THE DIRECTION OF BLAME IS THE ASSERTION.</b> An outcome the map has not been taught is a contract
    /// this projection does not yet understand, which is a fault on THIS side of the boundary; answering
    /// 400 would blame the caller and invite a retry with different input that can never succeed. This is
    /// the property a host-driven row could only reach by accident - and one did, by using a code that
    /// SHOULD have been classified, which is how the divergence this suite exists for stayed hidden.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnclassifiedOutcomes))]
    public void AnUnclassifiedOutcomeReachesTheDefaultArm(long retCode)
    {
        RestProjectionEndpoints.StatusProjection projected =
            RestProjectionEndpoints.InBandStatus.Project(retCode, errorText: null);

        Assert.Equal(StatusCodes.Status500InternalServerError, projected.HttpStatus);
        Assert.Equal(retCode, projected.RetCode);
    }

    /// <summary>
    /// The table above covers every arm the map declares, so an arm added later cannot go unasserted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE COMPLEMENT THAT MAKES THE TABLE TOTAL RATHER THAN A SAMPLE.</b> Every row above proves one
    /// arm answers what it should; none of them notices an arm the table forgot, which is precisely the
    /// failure mode that let six codes diverge. This walks every negative <c>RetCode</c> constant the
    /// kernel declares, asks the map for it, and requires that anything the map CLASSIFIES - anything not
    /// answering the default - appears in the table.
    /// </para>
    /// <para>
    /// IT WALKS THE KERNEL'S CONSTANTS BY REFLECTION rather than a list kept here, so a new failure code
    /// that acquires a status is caught by this row on the day it is classified.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTableCoversEveryClassifiedArm()
    {
        HashSet<long> tabled = [.. PublishedMappingDeclarations.Select(row => row.RetCode)];

        List<string> unasserted = [];

        foreach (FieldInfo field in typeof(KernelRetCode).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not long value || value >= 0L || tabled.Contains(value))
            {
                continue;
            }

            RestProjectionEndpoints.StatusProjection projected =
                RestProjectionEndpoints.InBandStatus.Project(value, errorText: null);

            if (projected.HttpStatus != StatusCodes.Status500InternalServerError)
            {
                unasserted.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{field.Name} ({value}) maps to {projected.HttpStatus} but is not in the table"));
            }
        }

        Assert.Empty(unasserted);
    }
}


// =====================================================================================================
//  THE COLLECTION WINDOW ON THE ONE PROJECTED STREAM THAT NEVER ENDS
// =====================================================================================================

/// <summary>
/// The projected expression event stream answers within a finite window rather than never answering.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>WHY THIS SUITE EXISTS, STATED AS THE DEFECT IT CLOSES.</b> <c>EventStream</c> is a SUBSCRIPTION:
/// the engine ends it only when the client goes away. The projection collected every stream to completion
/// before answering - correct for a retrieval, which ends with its final-marked chunk, and fatal for this
/// one. A normal HTTP request to <c>/v1/datawindow/expression/event-stream</c> therefore received neither
/// the events nor a success status, and held a request thread, a relay subscription and a connection for
/// as long as the caller was willing to wait. The route was published, documented and unusable.
/// </para>
/// <para>
/// <b>THE ASSERTION THAT MATTERS IS THAT THE REQUEST COMPLETES AT ALL.</b> Every row here would have hung
/// until the test framework's own cancellation fired, so a passing row is itself the evidence: the window
/// closes, the collected sequence is rendered, and the status is 200 even when nothing was emitted.
/// </para>
/// <para>
/// THE WINDOW IS SHORTENED FOR THE SUITE rather than waiting the shipped two seconds per row, through the
/// host factory's per-test settings opt-in. That is a duration, not a behaviour: the property under test is
/// that a window bounds the collection, and the shipped value is asserted separately by the options suite.
/// </para>
/// </remarks>
public sealed class RestProjectionStreamWindowTests
{
    /// <summary>The projected subscription - the one route the window applies to.</summary>
    private const string EventStreamRoute = "/v1/datawindow/expression/event-stream";

    /// <summary>The configuration key the window binds from.</summary>
    private const string WindowKey = "DataServices:RestProjection:StreamCollectionWindow";

    /// <summary>A window short enough that a row costs a fraction of a second.</summary>
    private const string ShortWindow = "00:00:00.250";

    /// <summary>
    /// A poll of the projected subscription completes with 200 and an empty ordered collection.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// <b>AN EMPTY ARRAY IS THE COMPLETE ANSWER, NOT A TRUNCATION AND NOT A FAULT.</b> Nothing is emitting
    /// events in this host, so the honest answer to "the records available now" is none - and the contract
    /// says so in both the projected operation's description and the authored gateway document. A 204, a
    /// 504 or a problem body would each describe something that did not happen.
    /// </para>
    /// <para>
    /// THE MEDIA TYPE IS ASSERTED because a problem body would also deserialize as JSON: checking the
    /// status alone would pass for a refusal that happened to carry an array-shaped member.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APollOfTheProjectedSubscriptionCompletesWithinTheWindowAsync()
    {
        await using DataServicesTestHostFactory host = new();

        host.AdditionalSettings[WindowKey] = ShortWindow;

        using HttpClient client = host.CreateAuthenticatedClient();

        (string sessionId, string handle) = await RestProjection.OpenExpressionSessionAsync(client);

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            EventStreamRoute,
            new { sessionId, datawindowHandle = handle });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = await RestProjection.DocumentAsync(response);

        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    /// <summary>
    /// A second poll behaves identically, so the window releases everything it took.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>THE ROW THAT CATCHES A LEAK RATHER THAN A HANG.</b> The window cancels a linked source and the
    /// projected method disposes a relay subscription on its way out; if either were left behind, the
    /// second poll would either observe a cancelled context immediately or accumulate a subscription per
    /// request. Two polls against ONE host is the smallest arrangement in which that is observable at all -
    /// a fresh host per row would hide it.
    /// </remarks>
    [Fact]
    public async Task ASecondPollIsUnaffectedByTheFirstAsync()
    {
        await using DataServicesTestHostFactory host = new();

        host.AdditionalSettings[WindowKey] = ShortWindow;

        using HttpClient client = host.CreateAuthenticatedClient();

        (string sessionId, string handle) = await RestProjection.OpenExpressionSessionAsync(client);

        for (int poll = 0; poll < 2; poll++)
        {
            using HttpResponseMessage response = await RestProjection.PostAsync(
                client,
                EventStreamRoute,
                new { sessionId, datawindowHandle = handle });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(response);

            Assert.Equal(0, body.RootElement.GetArrayLength());
        }
    }

    /// <summary>
    /// The window applies to the subscription alone: a self-terminating stream is not truncated by it.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE ROW THAT KEEPS THE FIX FROM BECOMING A WORSE DEFECT THAN THE ONE IT CLOSED.</b> A window
    /// applied to streaming IN GENERAL would silently truncate a retrieval - which ends with its
    /// final-marked chunk and can legitimately take longer than any window - and a truncated array cannot
    /// be told apart from a complete one, so the caller would receive the wrong answer under a success
    /// status. The window is therefore opt-in per operation, and this row is what proves the opt-in is
    /// actually selective rather than nominal.
    /// </para>
    /// <para>
    /// IT USES A DELIBERATELY IMPOSSIBLE WINDOW - the shortest a TimeSpan can express - so the row fails if
    /// the retrieval is windowed at all. With the flag off, that value has no effect on this route
    /// whatsoever; with the flag wrongly on, the collection would be cut before the first chunk arrived.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFiniteStreamIsNotTruncatedByTheWindowAsync()
    {
        await using DataServicesTestHostFactory host = new();

        host.AdditionalSettings[WindowKey] = "00:00:00.001";

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await RestProjection.PostAsync(
            client,
            RestProjection.RetrieveRoute,
            new { datawindowHandle = RestProjection.UnresolvedHandle });

        // The retrieval reaches the scripted upstream and answers its scripted chunks. What matters is that
        // the answer is NOT an empty collection produced by a window that should not have applied - the
        // route either succeeds with content or fails on its own merits, and neither outcome is a window.
        using JsonDocument body = await RestProjection.DocumentAsync(response);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
            Assert.True(
                body.RootElement.GetArrayLength() > 0,
                "The retrieval answered an EMPTY collection under a one-millisecond window, which is what "
                    + "a window wrongly applied to a self-terminating stream looks like.");
        }
        else
        {
            // A refusal is a legitimate outcome for this payload and says nothing about windowing - but a
            // 504 or an empty success would, so the refusal is required to be the projection's own.
            Assert.NotEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
            Assert.Equal(
                RestProjection.ProblemMediaType,
                response.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>
    /// Exactly one route opts into the collection window, asserted from the source that declares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE STRUCTURAL HALF OF THE ROW ABOVE, AND IT GUARDS THE WORSE DEFECT RATHER THAN THE ONE THAT
    /// WAS FIXED.</b> A window applied to streaming in general would truncate a retrieval, and a truncated
    /// JSON array is indistinguishable from a complete one - so the caller would receive the wrong answer
    /// under a success status, which is a quieter failure than the non-terminating request the window
    /// exists to close. The behavioural row proves a retrieval is not windowed TODAY; this one proves the
    /// opt-in is still an opt-in, so a second route acquiring it has to be a deliberate edit that fails
    /// here first.
    /// </para>
    /// <para>
    /// READ FROM THE REGISTRATION SOURCE rather than from the route table at run time, because "which
    /// routes passed the flag" is not observable from a running host at all: the flag is a registration-time
    /// argument that leaves no trace on the endpoint metadata. The file is located through the same
    /// repository-root resolution this suite already uses to read the authored contract.
    /// </para>
    /// </remarks>
    [Fact]
    public void ExactlyOneRouteOptsIntoTheCollectionWindow()
    {
        string source = File.ReadAllText(
            RestProjectionContract.Resolve(
                "services/dataservices-service/PowerFramework.DataServices/Endpoints/"
                + "RestProjectionEndpoints.cs"));

        const string optIn = "collectWithinWindow: true";

        int occurrences = 0;

        for (int at = source.IndexOf(optIn, StringComparison.Ordinal);
            at >= 0;
            at = source.IndexOf(optIn, at + optIn.Length, StringComparison.Ordinal))
        {
            occurrences++;
        }

        Assert.Equal(1, occurrences);

        // AND IT IS THE SUBSCRIPTION ROUTE. The count alone would be satisfied by the flag moving to the
        // retrieval, which is the exact mistake this row exists to prevent - so the registration nearest
        // the opt-in is required to be the event stream's.
        int flag = source.IndexOf(optIn, StringComparison.Ordinal);
        int registration = source.LastIndexOf("MapServerStream<", flag, StringComparison.Ordinal);

        Assert.True(registration >= 0, "The opt-in is not inside a server-stream registration.");

        Assert.Contains(
            "/event-stream",
            source[registration..flag],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The projected operation's published description states the poll semantics.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <b>A BOUNDED POLL THAT DOES NOT SAY SO IS A DIFFERENT DEFECT FROM THE ONE THAT WAS FIXED, NOT A
    /// SMALLER ONE.</b> A consumer generating a client from the published document would read the response
    /// as the whole event sequence, treat an empty array as "the subscription ended", and never poll again.
    /// The remedy for the non-terminating route was explicitly "apply a finite poll window AND update the
    /// document", so the document is asserted here rather than taken on trust.
    /// </remarks>
    [Fact]
    public async Task TheGeneratedDocumentPublishesThePollSemanticsAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage document = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        string text = await RestProjection.BodyAsync(document);

        // The description says it is a poll, names the setting that bounds it, and says what an empty
        // collection means - the three things a consumer cannot infer from the schema.
        foreach (string published in (string[])
        [
            "BOUNDED POLL",
            "StreamCollectionWindow",
            "never 'the subscription ended'",
        ])
        {
            Assert.Contains(published, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every projected operation publishes exactly the status surface its mapping can produce, in both
    /// directions.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>TWO STATUSES WERE PRODUCED AND NOT DECLARED, AND BOTH SUPPRESSIONS WERE DELIBERATE.</b>
    /// <c>404</c> was gated off the two session opens, on the reasoning that they carry no prior session
    /// identifier to fail to resolve - true of the identifier and false of the status, because
    /// <c>OpenValidationSession</c> answers the in-band <c>E_INVALID_HANDLE</c> for a
    /// <c>datawindowHandle</c> in its BODY that no DataWindow resolves, and <c>OpenExpressionSession</c>
    /// answers <c>E_OBJECT_NOT_FOUND</c> for a name no host binds. <c>409</c> was gated onto the update
    /// alone, which is right about the conflict DETAIL and wrong about the conflict STATUS: the shared
    /// failure map answers it for an <c>Aborted</c> whose detail did not decode and the shared in-band map
    /// answers it for <c>E_RETRY</c>, from any operation at all.
    /// </para>
    /// <para>
    /// The converse is asserted too, because a declared status that cannot occur hides which responses are
    /// real - and <c>501</c> in particular is reserved system-wide for Gateway's four deferred-capability
    /// route families (AAP 0.4.4, C-D), so an operation this service publishes as implemented must never
    /// declare one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProjectedOperationPublishesExactlyItsProducibleStatusSurfaceAsync()
    {
        string[] reachable =
        [
            "200", "400", "401", "403", "404", "409", "429", "500", "502", "503", "504",
        ];

        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = await RestProjection.DocumentAsync(served);

        int checkedOperations = 0;

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith(RestProjectionContract.ProjectedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("x-grpc-method", out _))
                {
                    continue;
                }

                string[] declared =
                [
                    .. operation.Value.GetProperty("responses")
                        .EnumerateObject()
                        .Select(static status => status.Name)
                        .Order(StringComparer.Ordinal),
                ];

                foreach (string required in (string[])["404", "409"])
                {
                    Assert.True(
                        declared.Contains(required, StringComparer.Ordinal),
                        $"{operation.Name.ToUpperInvariant()} {path.Name} can produce {required} and "
                            + "declares it nowhere. An undeclared status is one no generated client has a "
                            + "branch for.");
                }

                string[] surplus =
                    [.. declared.Where(status => !reachable.Contains(status, StringComparer.Ordinal))];

                Assert.True(
                    surplus.Length == 0,
                    $"{operation.Name.ToUpperInvariant()} {path.Name} declares "
                        + $"{string.Join(", ", surplus)}, which its mapping cannot produce.");

                checkedOperations++;
            }
        }

        // Guards against a silently empty loop: the projection declares thirty-nine operations, so a
        // document that served none would otherwise satisfy every assertion above by finding nothing.
        Assert.Equal(39, checkedOperations);
    }
}
