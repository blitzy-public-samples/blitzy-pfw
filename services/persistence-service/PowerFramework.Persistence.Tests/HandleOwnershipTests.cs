// ==================================================================================================
//  HandleOwnershipTests - A HANDLE IS UNGUESSABLE, WHICH IS NOT THE SAME AS OWNER-BOUND
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//    1. TWO PRINCIPALS, NOT ONE. Every refusal row has a positive twin through the same fixture,
//       because a registry that refused EVERY caller would satisfy a suite asserting only refusals
//       while being broken in the more obvious direction. The owner uses and releases its own handle
//       in the same rows that show a second caller cannot.
//
//    2. THE REFUSAL IS INDISTINGUISHABLE FROM A HANDLE THIS SERVICE NEVER ISSUED. Handles are
//       128-bit-opaque precisely so no caller can learn which ones exist; a distinct "not yours"
//       answer would give that back. Each row therefore compares the foreign answer against the
//       unknown-handle answer rather than merely asserting "it failed", and requires the resolved
//       out-parameter to be null on both.
//
//    3. A FOREIGN ATTEMPT DOES NOT REFRESH ACTIVITY. Resolution stamps the entry, so an ownership
//       check placed after the stamp would refuse the call and still let a leaked handle hold another
//       caller's session - and the transaction inside it - open indefinitely. The rows below prove the
//       idle reclaim still collects an entry a foreign caller kept touching, which no status
//       assertion could establish.
//
//    4. THE MAINTENANCE PATHS ARE DELIBERATELY NOT OWNER-CHECKED. Reclaim, purge and drain run inside
//       no request, so comparing there would test every stored subject against the unattributed
//       sentinel and collect nothing - turning the abandoned-handle ceiling into a leak, which is the
//       denial of service the ceiling exists to prevent.
//
//    5. THE COMPOSITION ROOT ACTUALLY WIRES IT. Each registry's resolver parameter is OPTIONAL, so a
//       host that forgot the accessor registration or the constructor argument would compile, start,
//       serve, and attribute every handle in the deployment to one shared unattributed owner. The
//       final rows boot the real host and require all four registries to hold the container's own
//       resolver, and that resolver to read the container's own accessor.
//
//  WHY THE PRINCIPAL IS DRIVEN THROUGH THE STOCK ACCESSOR
//  ------------------------------------------------------------------------------------------------
//  `HandlePrincipalResolver` reads `IHttpContextAccessor`, which is what `AddPersistencePublishedSurface`
//  registers, so these rows exercise the same path a real request does. Assigning the accessor's context
//  is the whole of what "arriving as a different caller" means to anything below the endpoint layer.
//
//  ORACLE
//  ------------------------------------------------------------------------------------------------
//  None. Handle ownership is NET-NEW: the legacy's tasks and transactions were objects in the caller's
//  own address space, reachable only through a pointer the process already held, so the oracle had no
//  notion of one caller reaching another's. The boundary created the exposure and owns the control
//  (AAP 0.1.4, constraint C-G).
// ==================================================================================================

using System.Reflection;
using System.Security.Claims;

using Grpc.Core;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Kernel;

using Xunit;

// The C-08 ADAPTER, named explicitly because the generated contract declares a service class of the same
// name in its own namespace. An unqualified reference is ambiguous (CS0104), and an alias is clearer here
// than qualifying every mention: the rows below drive the ADAPTER, never the generated base.
using GrpcTransactionService = PowerFramework.Persistence.Grpc.TransactionService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Ownership of a server-held handle: who may use it, who may release it, and what a caller who may not
/// is allowed to learn.
/// </summary>
public sealed class HandleOwnershipTests
{
    /// <summary>The caller that creates the handle in every row.</summary>
    private const string Owner = "powerframework-dataservices";

    /// <summary>A second authenticated caller. Not the owner.</summary>
    private const string Stranger = "powerframework-another-caller";

    /// <summary>A well-formed identifier this service never issued.</summary>
    private const string UnknownId = "0123456789abcdef0123456789abcdef";

    // ----------------------------------------------------------------------------------------------
    //  TRANSACTION SESSIONS - the handle with an OPEN TRANSACTION behind it.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Only the caller that began a session resolves it, on both lookup shapes, and the refusal is the
    /// unknown-session answer.
    /// </summary>
    /// <remarks>
    /// THE MOST CONSEQUENTIAL OF THE FOUR. A session carries an OPEN TRANSACTION, so a foreign caller
    /// holding its handle could commit or roll back writes that are not its own and create query, update
    /// and command tasks inside them (CWE-639, CWE-863). Both overloads are covered because both are
    /// reachable from the wire - the handle shape from C-08 and the identifier shape from the task
    /// services' own session lookups.
    /// </remarks>
    [Fact]
    public void OnlyTheOpeningCallerResolvesItsTransactionSession()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);
        TransactionSession session = harness.RegisterSession();

        harness.ArriveAs(Owner);
        Assert.True(harness.Sessions.TryResolve(session.SessionId, out TransactionSession? mine));
        Assert.Same(session, mine);
        Assert.True(harness.Sessions.TryResolve(
            new SessionHandle { SessionId = session.SessionId },
            out TransactionSession? mineByHandle));
        Assert.Same(session, mineByHandle);

        harness.ArriveAs(Stranger);
        Assert.False(harness.Sessions.TryResolve(session.SessionId, out TransactionSession? theirs));
        Assert.Null(theirs);
        Assert.False(harness.Sessions.TryResolve(
            new SessionHandle { SessionId = session.SessionId },
            out TransactionSession? theirsByHandle));
        Assert.Null(theirsByHandle);

        // Indistinguishable from a session this service never issued.
        Assert.False(harness.Sessions.TryResolve(UnknownId, out TransactionSession? unknown));
        Assert.Null(unknown);

        // And the refusals changed nothing.
        harness.ArriveAs(Owner);
        Assert.True(harness.Sessions.TryResolve(session.SessionId, out _));
        Assert.Equal(1, harness.Sessions.Count);
    }

    /// <summary>
    /// A foreign lookup does not refresh the owner's session, so the idle reclaim still collects it.
    /// </summary>
    /// <remarks>
    /// THE ORDERING PROOF, and the reason the ownership comparison sits ABOVE the activity stamp. The row
    /// is written so it would FAIL against the other ordering: the foreign attempts land inside the
    /// window, so a stamp taken before the refusal would leave the session outside the reclaim's reach
    /// and pinned for as long as an unauthorised holder kept trying.
    /// </remarks>
    [Fact]
    public void AForeignLookupDoesNotKeepAnotherCallersSessionAlive()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);
        TransactionSession session = harness.RegisterSession();

        DateTimeOffset opened = harness.Clock.GetUtcNow();
        TimeSpan window = TimeSpan.FromMinutes(10);

        // Three foreign attempts spread across the window.
        harness.ArriveAs(Stranger);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False(harness.Sessions.TryResolve(session.SessionId, out _));
        }

        // Now step just past the window measured from the OPEN.
        harness.Clock.Advance(window - (harness.Clock.GetUtcNow() - opened) + TimeSpan.FromSeconds(1));

        Assert.Equal(
            1,
            harness.Sessions.ReclaimIdle(harness.Clock.GetUtcNow(), window, pinnedSessionIds: new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal(0, harness.Sessions.Count);
    }

    /// <summary>
    /// The reclaim pass and the drain both collect sessions they do not own.
    /// </summary>
    /// <remarks>
    /// THE ROWS THAT WOULD FAIL IF THE MAINTENANCE PATHS WERE OWNER-CHECKED. Both run with no request in
    /// flight, which is exactly the state a background pass and a shutdown drain run in.
    /// </remarks>
    [Fact]
    public void MaintenanceCollectsSessionsItDoesNotOwn()
    {
        using OwnershipHarness reclaiming = new();

        reclaiming.ArriveAs(Owner);
        _ = reclaiming.RegisterSession();

        reclaiming.ArriveAs(Stranger);
        _ = reclaiming.RegisterSession();

        Assert.Equal(2, reclaiming.Sessions.Count);

        reclaiming.ArriveAsNobody();
        reclaiming.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(
            2,
            reclaiming.Sessions.ReclaimIdle(
                reclaiming.Clock.GetUtcNow(),
                TimeSpan.FromMinutes(10),
                pinnedSessionIds: new HashSet<string>(StringComparer.Ordinal)));

        using OwnershipHarness draining = new();

        draining.ArriveAs(Owner);
        _ = draining.RegisterSession();

        draining.ArriveAs(Stranger);
        _ = draining.RegisterSession();

        draining.ArriveAsNobody();

        Assert.Equal(2, draining.Sessions.Drain());
        Assert.Equal(0, draining.Sessions.Count);
    }

    /// <summary>
    /// Through C-08's own adapter: ending a foreign session answers exactly as ending an unknown one, and
    /// leaves the session open for its owner.
    /// </summary>
    /// <remarks>
    /// THE RELEASE PATH IS THE DAMAGING ONE. A foreign end-session would roll the transaction back to the
    /// pool, purge every task created inside it and break the owner's work in flight - a denial of
    /// service rather than a disclosure (CWE-862). Driven through the adapter rather than the registry so
    /// the row covers the code an actual caller reaches.
    /// </remarks>
    [Fact]
    public async Task EndSessionRefusesAForeignSessionExactlyAsAnUnknownOne()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);
        TransactionSession session = harness.RegisterSession();

        harness.ArriveAs(Stranger);

        EndSessionResponse foreign = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = new SessionHandle { SessionId = session.SessionId } },
            OwnershipHarness.CallContext);

        EndSessionResponse unknown = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = new SessionHandle { SessionId = UnknownId } },
            OwnershipHarness.CallContext);

        Assert.Equal(unknown.Status.RetCode, foreign.Status.RetCode);
        Assert.Equal(unknown.Status.ErrorText, foreign.Status.ErrorText);

        // Untouched: still open, still the owner's.
        Assert.Equal(1, harness.Sessions.Count);

        harness.ArriveAs(Owner);

        EndSessionResponse mine = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = new SessionHandle { SessionId = session.SessionId } },
            OwnershipHarness.CallContext);

        Assert.NotEqual(unknown.Status.RetCode, mine.Status.RetCode);
        Assert.Equal(0, harness.Sessions.Count);
    }

    // ----------------------------------------------------------------------------------------------
    //  UPDATE TASKS - the handle whose conflict detail carries live table values.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Only the caller that created an update task resolves it, and the refusal is the unknown-task
    /// answer.
    /// </summary>
    /// <remarks>
    /// AN UPDATE TASK IS THE PII-BEARING ONE. It holds another caller's buffered rows, and its conflict
    /// detail carries the CURRENT values of every marked column of the row that moved - which for the
    /// only evidenced schema means live name, age, address, salary and birth values. A leaked handle
    /// without an ownership comparison is a read of that row.
    /// </remarks>
    [Fact]
    public void OnlyTheCreatingCallerResolvesItsUpdateTask()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            harness.Updates.Register("session-1", new StubUpdateSurface(), out string diagnostic));

        Assert.Equal(string.Empty, diagnostic);

        TaskHandle handle = new() { TaskId = entry.TaskId };

        harness.ArriveAs(Owner);
        Assert.True(harness.Updates.TryResolve(handle, out UpdateTaskEntry? mine));
        Assert.Same(entry, mine);

        harness.ArriveAs(Stranger);
        Assert.False(harness.Updates.TryResolve(handle, out UpdateTaskEntry? theirs));
        Assert.Null(theirs);

        Assert.False(harness.Updates.TryResolve(new TaskHandle { TaskId = UnknownId }, out UpdateTaskEntry? unknown));
        Assert.Null(unknown);

        harness.ArriveAs(Owner);
        Assert.True(harness.Updates.TryResolve(handle, out _));
        Assert.Equal(1, harness.Updates.Count);
    }

    /// <summary>
    /// A foreign lookup does not keep another caller's update task alive.
    /// </summary>
    [Fact]
    public void AForeignLookupDoesNotKeepAnotherCallersUpdateTaskAlive()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            harness.Updates.Register("session-1", new StubUpdateSurface(), out _));

        TaskHandle handle = new() { TaskId = entry.TaskId };
        DateTimeOffset created = harness.Clock.GetUtcNow();
        TimeSpan window = TimeSpan.FromMinutes(10);

        harness.ArriveAs(Stranger);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False(harness.Updates.TryResolve(handle, out _));
        }

        harness.Clock.Advance(window - (harness.Clock.GetUtcNow() - created) + TimeSpan.FromSeconds(1));

        Assert.Equal(1, harness.Updates.ReclaimIdle(harness.Clock.GetUtcNow(), window));
        Assert.Equal(0, harness.Updates.Count);
    }

    /// <summary>
    /// The reclaim pass, the session purge and the drain all collect update tasks they do not own.
    /// </summary>
    [Fact]
    public void MaintenanceCollectsUpdateTasksItDoesNotOwn()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAs(Owner);
        _ = harness.Updates.Register("session-1", new StubUpdateSurface(), out _);

        harness.ArriveAs(Stranger);
        _ = harness.Updates.Register("session-1", new StubUpdateSurface(), out _);

        Assert.Equal(2, harness.Updates.Count);

        // The purge runs when a SESSION ends, which is the service's own bookkeeping and not a caller's
        // request against a task - so it must retire every task of that session whoever created it.
        harness.ArriveAsNobody();

        Assert.Equal(2, harness.Updates.PurgeSession("session-1"));
        Assert.Equal(0, harness.Updates.Count);

        harness.ArriveAs(Owner);
        _ = harness.Updates.Register("session-2", new StubUpdateSurface(), out _);

        harness.ArriveAs(Stranger);
        _ = harness.Updates.Register("session-2", new StubUpdateSurface(), out _);

        harness.ArriveAsNobody();
        harness.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(2, harness.Updates.ReclaimIdle(harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(10)));

        harness.ArriveAs(Owner);
        _ = harness.Updates.Register("session-3", new StubUpdateSurface(), out _);

        harness.ArriveAs(Stranger);
        _ = harness.Updates.Register("session-3", new StubUpdateSurface(), out _);

        harness.ArriveAsNobody();

        Assert.Equal(2, harness.Updates.Drain());
        Assert.Equal(0, harness.Updates.Count);
    }

    /// <summary>
    /// A handle created with no principal belongs to unattributed callers only.
    /// </summary>
    /// <remarks>
    /// AN ABSENT IDENTITY IS ATTRIBUTED, NOT EXEMPTED. Reading "no principal" as a wildcard would make the
    /// control avoidable by the one route hardest to audit, and would also open every unattributed handle
    /// to any authenticated caller. Both directions are asserted.
    /// </remarks>
    [Fact]
    public void AnUnattributedHandleBelongsToUnattributedCallersOnly()
    {
        using OwnershipHarness harness = new();

        harness.ArriveAsNobody();

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            harness.Updates.Register("session-1", new StubUpdateSurface(), out _));

        Assert.Equal(HandlePrincipalResolver.Unattributed, entry.Principal);

        TaskHandle handle = new() { TaskId = entry.TaskId };

        harness.ArriveAs(Owner);
        Assert.False(harness.Updates.TryResolve(handle, out _));

        harness.ArriveAsNobody();
        Assert.True(harness.Updates.TryResolve(handle, out _));
    }

    /// <summary>
    /// The resolver compares the subject ordinally, and reports the sentinel outside a request.
    /// </summary>
    /// <param name="stored">The identity a handle was attributed to.</param>
    /// <param name="arriving">The subject the current caller presents, or null for no request.</param>
    /// <param name="same">Whether the two are the same caller.</param>
    /// <remarks>
    /// CASE IS SIGNIFICANT ON PURPOSE. A subject is machine input; a case-insensitive comparison would
    /// make two distinct issuer subjects the same caller - an authorization decision varying by casing
    /// rules rather than by identity.
    /// </remarks>
    [Theory]
    [InlineData(Owner, Owner, true)]
    [InlineData(Owner, Stranger, false)]
    [InlineData(Owner, "POWERFRAMEWORK-DATASERVICES", false)]
    [InlineData(Owner, " powerframework-dataservices", false)]
    [InlineData(HandlePrincipalResolver.Unattributed, null, true)]
    [InlineData(Owner, null, false)]
    public void TheResolverComparesTheSubjectOrdinally(string stored, string? arriving, bool same)
    {
        HttpContextAccessor callers = new();
        HandlePrincipalResolver resolver = new(callers);

        if (arriving is null)
        {
            callers.HttpContext = null;

            Assert.Equal(HandlePrincipalResolver.Unattributed, resolver.Resolve());
        }
        else
        {
            callers.HttpContext = ContextFor(arriving);

            Assert.Equal(arriving, resolver.Resolve());
        }

        Assert.Equal(same, resolver.IsCaller(stored));
    }

    /// <summary>A resolver with no accessor attributes everything to the sentinel.</summary>
    [Fact]
    public void AResolverWithNoAccessorIsAlwaysUnattributed()
    {
        HandlePrincipalResolver resolver = new();

        Assert.Equal(HandlePrincipalResolver.Unattributed, resolver.Resolve());
        Assert.True(resolver.IsCaller(HandlePrincipalResolver.Unattributed));
        Assert.False(resolver.IsCaller(Owner));
        Assert.Throws<ArgumentNullException>(() => resolver.IsCaller(null!));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE COMPOSITION ROOT ACTUALLY WIRES IT.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// All four registries the deployed host builds hold the container's own resolver, and that resolver
    /// reads the container's own accessor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT CATCHES THE DEFECT THE UNIT ROWS CANNOT. Every registry's resolver parameter is
    /// optional and falls back to a resolver with NO accessor, so a composition root missing either
    /// <c>AddHttpContextAccessor</c> or the registry registration would start, serve, pass every row
    /// above, and attribute every handle in the deployment to one shared unattributed owner - leaving the
    /// ownership comparison permanently satisfied for everybody. Asserting the identity of the resolver
    /// instance is what makes that unmissable.
    /// </para>
    /// <para>
    /// READ THROUGH REFLECTION DELIBERATELY. The alternative - exposing the resolver as a property purely
    /// so a test could read it - would widen the production surface to make an assertion convenient,
    /// which is a worse trade than a private-field read confined to one row.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRegistryTheHostBuildsCarriesTheContainersPrincipalResolver()
    {
        using OwnershipHost host = new();

        IServiceProvider services = host.Services;

        HandlePrincipalResolver resolver = services.GetRequiredService<HandlePrincipalResolver>();
        IHttpContextAccessor accessor = services.GetRequiredService<IHttpContextAccessor>();

        foreach (object registry in new object[]
        {
            services.GetRequiredService<TransactionSessionRegistry>(),
            services.GetRequiredService<QueryTaskRegistry>(),
            services.GetRequiredService<UpdateTaskRegistry>(),
            services.GetRequiredService<CommandTaskRegistry>(),
        })
        {
            FieldInfo? field = registry.GetType().GetField(
                "_principals",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            Assert.Same(resolver, field.GetValue(registry));
        }

        // And the container's resolver reads the container's accessor, so a request's principal genuinely
        // reaches a registry rather than resolving to the sentinel for every caller.
        accessor.HttpContext = ContextFor(Owner);

        Assert.Equal(Owner, resolver.Resolve());
        Assert.True(resolver.IsCaller(Owner));
        Assert.False(resolver.IsCaller(Stranger));

        accessor.HttpContext = null;

        Assert.Equal(HandlePrincipalResolver.Unattributed, resolver.Resolve());
    }

    /// <summary>
    /// Builds a request context belonging to a named caller, exactly as authentication would.
    /// </summary>
    /// <param name="subject">The subject claim to present.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// BOTH SPELLINGS OF THE SUBJECT are supplied - the protocol's <c>sub</c> and the framework's mapped
    /// name identifier - because a host with inbound claim mapping on renames one to the other. A fixture
    /// naming only one would silently depend on which mapping the host happens to use.
    /// </remarks>
    private static DefaultHttpContext ContextFor(string subject) =>
        new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim("sub", subject),
                ],
                authenticationType: "PowerFramework.Persistence.Tests")),
        };

    /// <summary>
    /// Real registries, a real pool over the fake engine, and a settable ambient caller.
    /// </summary>
    /// <remarks>
    /// The doubles are the ones <c>HandleLifecycleTests</c> already owns, so this suite adds an identity
    /// seam rather than a second set of collaborators.
    /// </remarks>
    private sealed class OwnershipHarness : IDisposable
    {
        private readonly HttpContextAccessor _callers = new();

        internal OwnershipHarness()
        {
            PersistenceOptions options = new();
            IOptions<PersistenceOptions> bound = Options.Create(options);

            Clock = new LifecycleClock();
            Engine = new LifecycleEngine();
            Pool = new TransactionPool(
                bound,
                Clock,
                new PooledTransactionActivator(() => Engine, Clock));

            HandlePrincipalResolver principals = new(_callers);

            Sessions = new TransactionSessionRegistry(bound, Clock, Pool, principals);
            Queries = new QueryTaskRegistry(bound, Clock, principals);
            Updates = new UpdateTaskRegistry(bound, Clock, principals);
            Commands = new CommandTaskRegistry(bound, Clock, principals);

            Transactions = new GrpcTransactionService(
                Pool,
                Sessions,
                new UnreachedQuerySurface(),
                Queries,
                Updates,
                Commands);
        }

        /// <summary>A call context carrying no principal; identity arrives through the accessor.</summary>
        internal static ServerCallContext CallContext { get; } = new LifecycleCallContext();

        internal LifecycleClock Clock { get; }

        internal LifecycleEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal TransactionSessionRegistry Sessions { get; }

        internal QueryTaskRegistry Queries { get; }

        internal UpdateTaskRegistry Updates { get; }

        internal CommandTaskRegistry Commands { get; }

        internal GrpcTransactionService Transactions { get; }

        /// <summary>Makes the ambient request belong to a named caller.</summary>
        /// <param name="subject">The subject claim to present.</param>
        internal void ArriveAs(string subject) => _callers.HttpContext = ContextFor(subject);

        /// <summary>Removes the ambient request, which is how a background pass runs.</summary>
        internal void ArriveAsNobody() => _callers.HttpContext = null;

        /// <summary>Takes a pool reference and registers a session over it, as BeginSession does.</summary>
        /// <returns>The registered session.</returns>
        internal TransactionSession RegisterSession()
        {
            TransactionData descriptor = new() { Dbms = "SQLITE" };
            PoolLease lease = Pool.AddRefLease(descriptor);

            _ = Pool.Get(lease, out IPooledTransaction? borrowed);

            if (!borrowed!.IsConnected())
            {
                _ = borrowed.Connect();
            }

            return Assert.IsType<TransactionSession>(
                Sessions.Register(lease, descriptor, borrowed, out _));
        }

        public void Dispose() => _callers.HttpContext = null;
    }

    /// <summary>
    /// The real host, booted only for its container. No request is ever sent over HTTP.
    /// </summary>
    /// <remarks>
    /// The three settings are the ones this service's startup gate requires; the issuer is a reserved
    /// name so no metadata is ever fetched, and the data directory is unique so two hosts in one run
    /// cannot contend. Nothing here opens a connection.
    /// </remarks>
    private sealed class OwnershipHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:Authority", "https://authority.invalid");
            builder.UseSetting("Jwt:Audience", "powerframework-persistence-handle-ownership");
            builder.UseSetting("Jwt:RequireHttpsMetadata", "true");
            builder.UseSetting(
                "Sqlite:DataDirectory",
                Path.Combine(Path.GetTempPath(), "pfw-handle-ownership-" + Guid.NewGuid().ToString("N")));
        }
    }
}
