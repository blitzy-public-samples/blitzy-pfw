// ==================================================================================================
//  RUNTIME-SEAM READINESS - WHAT THIS FILE ASSERTS THAT NOTHING ELSE DOES
//
//  This service's four published contracts - C-05 retrieval, C-06 update, C-07 command, C-08
//  transaction - are served through eight injected seams. Before this check existed, a composition
//  missing one of them produced an instance that
//
//      started, answered GET /health with 200, satisfied the Compose `depends_on:
//      condition: service_healthy` gate, let Gateway start behind it, and THEN answered an
//      internal error to every single call on the affected contract
//
//  and the readiness surface said nothing at all about it. That is the gap this file's subject closes,
//  and these are the rows that hold it closed.
//
//  WHY THE ARMS ARE ASSERTED WITHOUT A HOST, AND WHY THAT IS NOT A WEAKER TEST. In the shipped
//  composition an unbound seam is a REFUSAL TO START: Program.cs resolves the same eight seams during
//  its structural-precondition gate and terminates when one is missing. A host-level test of the
//  unbound arm is therefore impossible by construction - the host never finishes building, so there is
//  no response to assert on. Driving the check directly is the only way to observe what it reports, and
//  the composed path is covered where it can be: HealthEndpointsTests pins `runtime` as a named entry
//  of the ready body, which is what proves the registration reaches the projection.
//
//  WHAT IS DELIBERATELY NOT ASSERTED HERE: nothing about timing, and nothing about the storage volume.
//  Not one seam is invoked by the subject, because opening a connection would CREATE a database file -
//  and for a given workflow identifier the legacy-side and target-side characterization recordings must
//  be captured against the same persistence-db volume state. A readiness probe that created, seeded or
//  migrated anything would invalidate the comparison the parity model rests on, so the row below that
//  matters most about the transaction engine is the one asserting it was resolved and DISPOSED without
//  ever being asked to open anything.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Persistence.Endpoints;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The readiness check that reports whether every C-05..C-08 runtime seam is bound.
/// </summary>
public sealed class RuntimeSeamReadinessTests
{
    /// <summary>
    /// The eight seam types, each named as a theory row so a removed registration is attributed to the
    /// seam it belongs to rather than to "one of them".
    /// </summary>
    /// <remarks>
    /// THE COUNT IS ITSELF AN AUDIT. Eight rows is eight seams: four for C-05, two for C-06, one for
    /// C-07 and one for C-08. If a future edit adds a seam to the subject's list without adding a row
    /// here, the seam is unasserted; if it removes one, a row fails because the seam it names is no
    /// longer required. Either direction is caught.
    /// </remarks>
    public static TheoryData<string> RequiredSeamNames =>
        new()
        {
            nameof(IDataObjectCatalog),
            nameof(IDataObjectRuntime),
            nameof(IQueryDataWindowRuntime),
            nameof(IQueryTransactionSurface),
            nameof(ISqlUpdateCarrierAdapter),
            nameof(IUpdateTaskFactory),
            nameof(ICommandTaskFactory),
            nameof(ITransactionEngine),
        };

    /// <summary>
    /// A fully bound composition reports ready, and the transaction engine it resolved is disposed
    /// rather than left behind.
    /// </summary>
    /// <remarks>
    /// THE DISPOSAL HALF IS NOT INCIDENTAL. The engine is registered TRANSIENT because the transaction
    /// pool creates one per pooled transaction and owns its lifetime, so a probe that resolved one and
    /// kept it would leak one object per poll - and the orchestration gate polls this route
    /// continuously. Resolving it proves the registration and its dependency graph are satisfiable;
    /// disposing it at once is what keeps proving that free.
    /// </remarks>
    [Fact]
    public async Task A_bound_runtime_reports_ready_and_disposes_the_engine_it_resolved()
    {
        SeamDouble seams = new();
        RuntimeSeamHealthCheck check = Build(seams);

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(
            "Every runtime seam the retrieval, update, command and transaction contracts depend on is "
                + "bound.",
            result.Description);

        // Resolved and released, exactly once.
        Assert.Equal(1, seams.DisposeCount);

        // AND NOT ONE SEAM WAS INVOKED. The probe's whole contract is that resolution IS the assertion:
        // every member of every seam double throws, so any call at all would have surfaced as a fault
        // rather than as a verdict.
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// Removing any one required seam makes the check report not ready, whichever seam it is.
    /// </summary>
    /// <param name="removed">The seam interface removed from the composition.</param>
    [Theory]
    [MemberData(nameof(RequiredSeamNames))]
    public async Task An_unbound_seam_reports_not_ready_whichever_seam_it_is(string removed)
    {
        SeamDouble seams = new();
        RuntimeSeamHealthCheck check = Build(seams, omit: removed);

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "A runtime seam the retrieval, update, command and transaction contracts depend on is not "
                + "bound, so calls on the affected contract cannot be served.",
            result.Description);
    }

    /// <summary>
    /// The published description names no type, interface, assembly or registration - only the
    /// capability - because the route that publishes it is anonymous.
    /// </summary>
    /// <param name="removed">The seam interface removed from the composition.</param>
    /// <remarks>
    /// The seam's identity is exactly what an operator needs and exactly what an unauthenticated caller
    /// must not receive, so it goes to the operator channel and the response carries authored prose. A
    /// description that interpolated the missing type's name would publish this service's internal
    /// structure to anything able to reach the port.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RequiredSeamNames))]
    public async Task The_published_description_names_the_capability_and_never_the_type(string removed)
    {
        SeamDouble seams = new();
        RuntimeSeamHealthCheck check = Build(seams, omit: removed);

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Description);
        Assert.DoesNotContain(removed, result.Description!, StringComparison.Ordinal);

        // No data dictionary either: the framework would carry it onto the report entry, and the entry is
        // what the projection reads.
        Assert.Empty(result.Data);
        Assert.Null(result.Exception);
    }

    /// <summary>
    /// The registration's own failure status is honoured rather than assumed, so a host that registered
    /// this check as degrading gets a degradation.
    /// </summary>
    /// <remarks>
    /// The SHIPPED registration is <c>Unhealthy</c>, deliberately: an unbound seam is a composition
    /// defect rather than a condition that resolves with time, and reporting it as a failure is what
    /// tells an operator to redeploy instead of to wait. Honouring the registration nonetheless is what
    /// keeps that a registration decision rather than something this class hardcodes.
    /// </remarks>
    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task The_registrations_own_failure_status_is_honoured(HealthStatus failureStatus)
    {
        SeamDouble seams = new();
        RuntimeSeamHealthCheck check = Build(seams, omit: nameof(ICommandTaskFactory));

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(failureStatus),
            TestContext.Current.CancellationToken);

        Assert.Equal(failureStatus, result.Status);
    }

    /// <summary>
    /// An engine that is bound but unresolvable is reported rather than allowed to fault the probe.
    /// </summary>
    /// <remarks>
    /// THE ARM EXISTS BECAUSE THE ENGINE IS THE ONE SEAM WITH A DEPENDENCY GRAPH BEHIND IT. It is
    /// registered TRANSIENT over the connection factory and the pool's activator, so a registration that
    /// is present but whose own dependencies are missing resolves to an activation failure rather than to
    /// null - and a probe that let that escape would answer 500 on the one route an unauthenticated
    /// caller can reach, which the readiness gate cannot distinguish from a crash.
    /// </remarks>
    [Fact]
    public async Task An_engine_that_cannot_be_activated_is_reported_rather_than_thrown()
    {
        SeamDouble seams = new();

        ServiceCollection services = new();
        Register(services, seams, omit: nameof(ITransactionEngine));

        // Bound, but to a factory that cannot produce one - the shape a missing transitive dependency
        // takes once the container tries to satisfy it.
        services.AddTransient<ITransactionEngine>(
            static _ => throw new InvalidOperationException("The engine's own dependency is missing."));

        RuntimeSeamHealthCheck check = new(
            services.BuildServiceProvider(),
            NullLogger<RuntimeSeamHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("dependency is missing", result.Description!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cancelled evaluation stops rather than producing a verdict nobody will read.
    /// </summary>
    /// <remarks>
    /// Cancellation of the framework's evaluation token means the CALLER disconnected, so there is no
    /// longer a response to write and a verdict would be work nobody reads. The endpoint's own handler
    /// excludes <see cref="OperationCanceledException"/> from the catch that converts a fault into a
    /// not-ready verdict, precisely so this propagates.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_evaluation_stops_rather_than_answering()
    {
        SeamDouble seams = new();
        RuntimeSeamHealthCheck check = Build(seams);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(Registration(HealthStatus.Unhealthy), cancelled.Token));

        // Nothing was resolved, so nothing was left behind either.
        Assert.Equal(0, seams.DisposeCount);
    }

    // ----------------------------------------------------------------------------------------------
    //  THE HARNESS
    // ----------------------------------------------------------------------------------------------

    /// <summary>Builds the subject over a container carrying every seam except an optional omission.</summary>
    /// <param name="seams">The double every seam resolves to.</param>
    /// <param name="omit">The interface name to leave unregistered, or <see langword="null"/> for none.</param>
    /// <returns>The subject.</returns>
    private static RuntimeSeamHealthCheck Build(SeamDouble seams, string? omit = null)
    {
        ServiceCollection services = new();

        Register(services, seams, omit);

        return new RuntimeSeamHealthCheck(
            services.BuildServiceProvider(),
            NullLogger<RuntimeSeamHealthCheck>.Instance);
    }

    /// <summary>Registers every seam except an optional omission.</summary>
    /// <param name="services">The collection to register into.</param>
    /// <param name="seams">The double every seam resolves to.</param>
    /// <param name="omit">The interface name to leave unregistered, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// ONE DOUBLE SATISFIES ALL EIGHT INTERFACES, which is what keeps the harness proportionate to what
    /// the subject actually does: it resolves each seam and invokes none of them, so eight separate
    /// doubles would be eight copies of the same "never called" body. Registering the same instance
    /// under eight service types is also what makes the omission the ONLY difference between the bound
    /// and unbound cases.
    /// </remarks>
    private static void Register(ServiceCollection services, SeamDouble seams, string? omit)
    {
        if (omit != nameof(IDataObjectCatalog))
        {
            services.AddSingleton<IDataObjectCatalog>(seams);
        }

        if (omit != nameof(IDataObjectRuntime))
        {
            services.AddSingleton<IDataObjectRuntime>(seams);
        }

        if (omit != nameof(IQueryDataWindowRuntime))
        {
            services.AddSingleton<IQueryDataWindowRuntime>(seams);
        }

        if (omit != nameof(IQueryTransactionSurface))
        {
            services.AddSingleton<IQueryTransactionSurface>(seams);
        }

        if (omit != nameof(ISqlUpdateCarrierAdapter))
        {
            services.AddSingleton<ISqlUpdateCarrierAdapter>(seams);
        }

        if (omit != nameof(IUpdateTaskFactory))
        {
            services.AddSingleton<IUpdateTaskFactory>(seams);
        }

        if (omit != nameof(ICommandTaskFactory))
        {
            services.AddSingleton<ICommandTaskFactory>(seams);
        }

        if (omit != nameof(ITransactionEngine))
        {
            // TRANSIENT, matching the shipped lifetime: the pool creates one engine per pooled
            // transaction. The double counts its own disposals so the probe's release is observable.
            services.AddTransient<ITransactionEngine>(_ => seams);
        }
    }

    /// <summary>Builds a registration carrying the given failure status.</summary>
    /// <param name="failureStatus">The status a not-ready result must adopt.</param>
    /// <returns>The evaluation context.</returns>
    private static HealthCheckContext Registration(HealthStatus failureStatus) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                HealthEndpoints.RuntimeCheckName,
                _ => throw new NotSupportedException(
                    "The subject is constructed directly, so the registration's own factory is never "
                    + "invoked; a call here would mean the harness had started driving the framework's "
                    + "evaluator instead of the check."),
                failureStatus,
                ["ready"]),
        };
}

/// <summary>
/// One double satisfying every seam the readiness check requires, whose only real behaviour is counting
/// its disposals.
/// </summary>
/// <remarks>
/// EVERY OTHER MEMBER THROWS, AND THAT IS THE ASSERTION. The subject's contract is that resolution alone
/// establishes readiness: it must not open a connection, execute a statement, resolve a definition or
/// touch the storage volume, because a readiness probe that created or seeded anything would invalidate
/// the paired characterization captures the parity model depends on. A double that answered plausible
/// values would let a future edit start invoking a seam without any row noticing.
/// </remarks>
internal sealed class SeamDouble
    : IDataObjectCatalog,
      IDataObjectRuntime,
      IQueryDataWindowRuntime,
      IQueryTransactionSurface,
      ISqlUpdateCarrierAdapter,
      IUpdateTaskFactory,
      ICommandTaskFactory,
      ITransactionEngine
{
    /// <summary>Why a call to anything but <see cref="Dispose"/> is a defect rather than a behaviour.</summary>
    private const string Reason =
        "The runtime-seam readiness check must RESOLVE every seam and INVOKE none of them, so a call here "
        + "means the subject started exercising a seam - which on the storage seams would create or alter "
        + "the very volume the paired characterization captures require to be untouched.";

    /// <summary>How many times the resolved engine was released.</summary>
    /// <remarks>
    /// The engine is registered transient because the pool owns one per pooled transaction, so a probe
    /// that resolved one without releasing it would leak one object per poll - and this route is polled
    /// continuously by the orchestration health condition.
    /// </remarks>
    internal int DisposeCount { get; private set; }

    /// <inheritdoc/>
    public int Count => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public bool TryResolve(
        string dataObject,
        [NotNullWhen(true)] out DataObjectDefinition? definition) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public bool TryResolveUpdateContract(
        string dataObject,
        [NotNullWhen(true)] out DataObjectUpdateContract? contract) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public bool TryResolveDefinition(
        string dataObject,
        [NotNullWhen(true)] out DataObjectDefinition? definition) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public ValueTask<long> RetrieveAsync(
        ISqlDataStore data,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Connect(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public ValueTask<CountQueryOutcome> Query(
        IPooledTransaction transaction,
        string sql,
        CancellationToken cancellationToken) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public void RaiseAfterRetrieve(
        IPooledTransaction transaction,
        DataWindowCarrier data,
        long rowCount) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public ISqlUpdateCarrier Adapt(ISqlDataStore store) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public long TryCreate(string sessionId, out IUpdateTaskSurface? task) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public long Create(out CommandTaskComponents? components) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public int DbHandle => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public string Dbms => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public bool AutoCommit
    {
        get => throw new NotSupportedException(Reason);
        set => throw new NotSupportedException(Reason);
    }

    /// <inheritdoc/>
    public void ApplyConnectionFields(in TransactionData descriptor) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Connect() => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Disconnect() => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Commit() => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Rollback() => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public SqlState Execute(string sqlCommand) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public void Dispose() => DisposeCount++;
}
