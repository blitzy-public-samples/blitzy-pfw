// ==================================================================================================
//  HandleLifecycle - THE BOUNDS ON SERVER-HELD WORK HANDLES
//  ------------------------------------------------------------------------------------------------
//  WHY THIS TYPE EXISTS, STATED AS A FACT ABOUT THE CODE
//
//  All four of this service's handle tables - the transaction sessions, the query tasks, the update tasks
//  and the command tasks - are naturally plain `ConcurrentDictionary` singletons with no ceiling, no idle
//  expiry and no shutdown drain. Every entry pins real resources: a session pins a pool reference and
//  therefore an open connection; a command or update task pins a worker task and its synchronisation
//  handles. Left unbounded, a caller that crashes, times out or simply forgets to release keeps all of
//  that alive for the life of the PROCESS, and nothing anywhere notices or reports it.
//
//  ============ WHY THE LEGACY NEEDED NONE OF THIS, AND WHY THE PORT DOES =========================
//  In process a caller held its task and its transaction BY REFERENCE. An abandoned one was collected the
//  moment the last reference left scope, so the oracle has no ceiling, no expiry and no drain to port -
//  there is no locator to cite here because there is nothing to cite. Across a boundary the caller holds
//  a NAME and the server holds the object, so the server has to decide when a name has been abandoned.
//  That decision is the cost of the boundary (constraint C-A) and not a behavioural change to anything
//  the legacy does (constraint C-B): no operation this service serves answers differently because these
//  bounds exist, unless a caller has already leaked past them.
//
//  ============ THE FOUR MECHANISMS, AND WHAT EACH IS FOR =========================================
//  1. A CEILING, per caller identity and in total, enforced at CREATION and answered with
//     `RetCode.E_BUSY` - a code the oracle already uses for "not now", so no new value enters a
//     consumer's branch set. A ceiling never evicts: taking a live handle away from one caller to admit
//     another's is a worse failure than refusing the newcomer.
//  2. AN IDLE WINDOW, refreshed every time a call names the handle, swept periodically. A handle in
//     active use is never near expiry, and a retrieval that is provably in flight is never reclaimed at
//     all.
//  3. A SHUTDOWN DRAIN, tasks before sessions, because a task borrows the transaction its session owns.
//  4. IDEMPOTENT RELEASE, which the tables already had and which is preserved rather than changed: the
//     removal is what decides, so two concurrent releases produce exactly one teardown and the loser is
//     answered `E_INVALID_HANDLE` - the contract's stated behaviour, NOT a silent success.
//
//  ============ THE ONE CLOCK ====================================================================
//  Every time read in this file comes from the injected `TimeProvider`, including the sweep timer's. That
//  is the same seam the transaction pool's idle expiry and the pooled transaction's liveness cache use, so
//  a paired characterization capture and a unit test drive all of them from one substitutable clock. A
//  `DateTime.UtcNow` anywhere here would put one clock outside the seam and silently break both.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Runtime;

/// <summary>
/// Resolves the caller identity a newly created handle is attributed to.
/// </summary>
/// <remarks>
/// <para>
/// THE SUBJECT CLAIM IS THE IDENTITY, AND IT IS NOT A CHOICE. Every contract on this service requires an
/// authenticated principal, and the composition root additionally admits only a rostered caller subject,
/// so the subject is both present and meaningful on every call that can reach a registry. It is preferred
/// over the client identifier because it is what the roster is written in terms of, and over the peer
/// address because a caller behind a proxy shares an address with everything else behind that proxy.
/// </para>
/// <para>
/// AN ABSENT IDENTITY IS ATTRIBUTED, NOT EXEMPTED. A call that reaches a registry with no principal - a
/// unit test constructing a service directly, or a future surface that has not yet been authorized - is
/// attributed to <see cref="Unattributed"/>, so it is counted against a per-caller ceiling like everybody
/// else. Exempting it would make the bound trivially avoidable by the one route that is hardest to audit.
/// </para>
/// <para>
/// THE ACCESSOR IS OPTIONAL SO A REGISTRY REMAINS CONSTRUCTIBLE WITHOUT A HOST, which is what keeps the
/// per-service coverage gate reachable for every branch of the quota (constraint C-H). Without one, every
/// handle is unattributed and only the total ceiling is operative.
/// </para>
/// </remarks>
internal sealed class HandlePrincipalResolver
{
    /// <summary>The identity a handle is attributed to when no principal can be read.</summary>
    /// <remarks>
    /// Parenthesised so it cannot collide with a real subject: a JWT subject is an identifier, and this
    /// value is not a legal one. It is never rendered into a response - only into a diagnostic.
    /// </remarks>
    internal const string Unattributed = "(unattributed)";

    private readonly IHttpContextAccessor? _accessor;

    /// <summary>Creates the resolver.</summary>
    /// <param name="accessor">
    /// The ambient request accessor, or <see langword="null"/> outside a host - in which case every
    /// handle is <see cref="Unattributed"/>.
    /// </param>
    /// <remarks>
    /// PUBLIC ON AN INTERNAL TYPE, so the container's activator - which considers only public constructors -
    /// can build it. The type stays internal, so nothing outside this assembly gains reach.
    /// </remarks>
    public HandlePrincipalResolver(IHttpContextAccessor? accessor = null) => _accessor = accessor;

    /// <summary>
    /// Reads the identity of the caller in whose request this call is running.
    /// </summary>
    /// <returns>The subject, or <see cref="Unattributed"/> when none can be read.</returns>
    internal string Resolve()
    {
        ClaimsPrincipal? user = _accessor?.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return Unattributed;
        }

        string? subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity.Name;

        return string.IsNullOrWhiteSpace(subject) ? Unattributed : subject;
    }

    /// <summary>
    /// Determines whether the caller of the current call is the one a handle was attributed to.
    /// </summary>
    /// <param name="attributedPrincipal">The identity stored on the handle when it was created.</param>
    /// <returns><see langword="true"/> when the two identities are the same caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attributedPrincipal"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>A HANDLE IS HIGH-ENTROPY, WHICH IS NOT THE SAME AS OWNER-BOUND.</b> Every handle this service
    /// mints is an unguessable identifier, so no caller finds another's by search - but unguessable is a
    /// bound on DISCOVERY, not on USE. A handle that leaks through a log, a proxy trace, a crash dump or a
    /// caller's own bug is a bearer credential for the resource behind it: an update task carries another
    /// caller's buffered rows and its conflict detail carries live table values, and a transaction session
    /// carries an open transaction another caller's writes are inside. Comparing the identity closes the
    /// gap between "cannot be found" and "cannot be used" (CWE-639, CWE-862, CWE-863).
    /// </para>
    /// <para>
    /// <b>THE COMPARISON IS AGAINST THE SAME SOURCE THE ATTRIBUTION USED</b> - <see cref="Resolve"/>, and
    /// therefore the subject claim - so the check cannot disagree with the quota about who a caller is.
    /// One resolver, one notion of identity.
    /// </para>
    /// <para>
    /// ORDINAL, BECAUSE A SUBJECT IS MACHINE INPUT. A case-insensitive or culture-aware comparison would
    /// admit a caller whose subject differs from the owner's only in casing, which is a different caller as
    /// far as the roster and the grant matrix are concerned.
    /// </para>
    /// <para>
    /// <b>AN UNATTRIBUTED HANDLE IS OWNED BY UNATTRIBUTED CALLERS, NOT BY EVERYONE.</b> The sentinel
    /// compares equal only to itself, so a host without an accessor - a test constructing a registry
    /// directly - keeps working exactly as before, while an authenticated caller cannot reach a handle
    /// created outside a request and an unauthenticated path cannot reach an authenticated caller's.
    /// </para>
    /// <para>
    /// WHAT THIS MEMBER DELIBERATELY DOES NOT DO IS DECIDE THE OUTCOME. A caller-facing lookup answers a
    /// foreign handle exactly as it answers an unknown one, so the two are indistinguishable and no caller
    /// can use the difference to learn that a handle exists. That collapse belongs at the lookup, which is
    /// where the not-found answer is also produced.
    /// </para>
    /// </remarks>
    internal bool IsCaller(string attributedPrincipal)
    {
        ArgumentNullException.ThrowIfNull(attributedPrincipal);

        return string.Equals(attributedPrincipal, Resolve(), StringComparison.Ordinal);
    }
}

/// <summary>
/// The ceiling on live handles in one registry: a total, and a per-caller share of it.
/// </summary>
/// <remarks>
/// <para>
/// THE RESERVATION IS ATOMIC WITH THE TEST, WHICH IS THE WHOLE POINT OF THE TYPE. A count-then-add pair
/// admits one handle past the ceiling for every caller that races another, and a ceiling that can be
/// exceeded is not a ceiling. Both counters therefore move under one lock, and a reservation that fails
/// moves neither.
/// </para>
/// <para>
/// A LOCK RATHER THAN INTERLOCKED ARITHMETIC, deliberately. Two counters have to agree - the total and the
/// caller's - so an interlocked increment on each would leave a window in which the first had been taken
/// and the second refused, and the compensating decrement is exactly the code that gets a rollback wrong.
/// The critical section is a dictionary lookup and two increments, with no I/O and no callback inside it.
/// </para>
/// <para>
/// PER-CALLER IS CHECKED FIRST, so a runaway caller is told it is the one at fault rather than being told
/// the service is full. The order is observable in the diagnostic and is therefore contract.
/// </para>
/// </remarks>
internal sealed class HandleQuota
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _perPrincipal = new(StringComparer.Ordinal);
    private readonly int _maxTotal;
    private readonly int _maxPerPrincipal;
    private int _total;

    /// <summary>Creates a quota over one registry's ceilings.</summary>
    /// <param name="kind">What the registry holds, for diagnostics. Never rendered into a response.</param>
    /// <param name="maxTotal">The total ceiling. At least one.</param>
    /// <param name="maxPerPrincipal">The per-caller ceiling. At least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either ceiling is below one.</exception>
    /// <remarks>
    /// THE CEILINGS ARE VALIDATED AGAIN HERE EVEN THOUGH THE OPTIONS VALIDATOR ALREADY REFUSED A
    /// NON-POSITIVE ONE. This type is constructible without options - a test builds one directly - so the
    /// guarantee has to hold at its own boundary rather than depend on how it was reached.
    /// </remarks>
    internal HandleQuota(string kind, int maxTotal, int maxPerPrincipal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotal, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPerPrincipal, 1);

        Kind = kind;
        _maxTotal = maxTotal;
        _maxPerPrincipal = maxPerPrincipal;
    }

    /// <summary>What the registry behind this quota holds.</summary>
    internal string Kind { get; }

    /// <summary>The number of reservations currently held.</summary>
    internal int Reserved
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    /// <summary>
    /// Takes one reservation for a caller, or refuses.
    /// </summary>
    /// <param name="principal">The caller identity to attribute the handle to.</param>
    /// <param name="diagnostic">
    /// Empty on success; otherwise which ceiling refused and what it is. It names the CEILING and the
    /// caller, and never the handle values behind them.
    /// </param>
    /// <returns><see langword="true"/> when the caller may create a handle.</returns>
    internal bool TryReserve(string principal, out string diagnostic)
    {
        ArgumentException.ThrowIfNullOrEmpty(principal);

        lock (_gate)
        {
            _ = _perPrincipal.TryGetValue(principal, out int held);

            if (held >= _maxPerPrincipal)
            {
                diagnostic = string.Format(
                    CultureInfo.InvariantCulture,
                    "Caller '{0}' already holds {1} live {2} handles, which is its configured ceiling "
                    + "(Handles:MaxPerPrincipal). Release a handle before creating another.",
                    principal,
                    held,
                    Kind);

                return false;
            }

            if (_total >= _maxTotal)
            {
                diagnostic = string.Format(
                    CultureInfo.InvariantCulture,
                    "This service already holds {0} live {1} handles, which is its configured ceiling "
                    + "(Handles:MaxTotalPerRegistry).",
                    _total,
                    Kind);

                return false;
            }

            _perPrincipal[principal] = held + 1;
            _total++;
            diagnostic = string.Empty;

            return true;
        }
    }

    /// <summary>
    /// Returns one reservation.
    /// </summary>
    /// <param name="principal">The identity the handle was attributed to.</param>
    /// <remarks>
    /// A RELEASE THAT DOES NOT MATCH A RESERVATION IS IGNORED RATHER THAN THROWING. It is reached from
    /// teardown paths, including the reclaim pass and the shutdown drain, and a throw there would abandon
    /// the rest of the pass; the counters are a bound, not an audit ledger. The entry is removed at zero so
    /// the dictionary tracks LIVE callers rather than every caller the process has ever served.
    /// </remarks>
    internal void Release(string principal)
    {
        if (string.IsNullOrEmpty(principal))
        {
            return;
        }

        lock (_gate)
        {
            if (!_perPrincipal.TryGetValue(principal, out int held) || held <= 0)
            {
                return;
            }

            if (held == 1)
            {
                _ = _perPrincipal.Remove(principal);
            }
            else
            {
                _perPrincipal[principal] = held - 1;
            }

            if (_total > 0)
            {
                _total--;
            }
        }
    }
}

/// <summary>
/// Reclaims abandoned work handles periodically, and drains every registry on shutdown.
/// </summary>
/// <remarks>
/// <para>
/// THE FOUR REGISTRIES ARE INJECTED BY NAME RATHER THAN AS A SEQUENCE, and that is deliberate: the drain
/// ORDER matters - tasks before sessions, because a task borrows the transaction its session owns - and a
/// sequence would leave that order to a registration order nothing enforces. Naming them also makes the
/// set auditable: these four are exactly the four handle tables this service holds.
/// </para>
/// <para>
/// A SESSION IS NEVER RECLAIMED WHILE A LIVE TASK NAMES IT. Each task entry records the session it was
/// created against, so the pass collects those identities first and pins them. Without that, an idle
/// session whose task was still working would have its transaction handed back underneath the task - which
/// would turn an abandoned-handle cleanup into data loss.
/// </para>
/// <para>
/// THE BOUNDARY OF THE IDLE TEST IS STATED RATHER THAN HIDDEN. Activity is refreshed whenever a call
/// resolves a handle, so the pass cannot distinguish a handle abandoned fifteen minutes ago from one whose
/// single operation has been running for fifteen minutes without touching it again. The query registry -
/// whose operation is the streaming one, and therefore the only one where that duration is realistic -
/// carries an explicit in-flight latch and is guarded by it. For the two unary contracts the window is
/// simply far longer than any statement they declare can take.
/// </para>
/// <para>
/// A FAULT IN ONE PASS DOES NOT STOP THE SERVICE. The pass is best-effort by nature: it exists to bound a
/// leak, so a failure to reclaim is a logged warning and the next pass tries again. Letting it terminate
/// the host would convert a cleanup problem into an outage, which is the opposite of its purpose - and it
/// is a different case entirely from a STRUCTURAL fault at startup, which does terminate the process.
/// </para>
/// </remarks>
internal sealed class HandleReclaimer : BackgroundService
{
    private readonly TransactionSessionRegistry _sessions;
    private readonly QueryTaskRegistry _queries;
    private readonly UpdateTaskRegistry _updates;
    private readonly CommandTaskRegistry _commands;
    private readonly IOptions<PersistenceOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<HandleReclaimer>? _logger;

    /// <summary>Creates the reclaimer over the four registries it bounds.</summary>
    /// <param name="sessions">The C-08 session table.</param>
    /// <param name="queries">The C-05 task table.</param>
    /// <param name="updates">The C-06 task table.</param>
    /// <param name="commands">The C-07 task table.</param>
    /// <param name="options">The bound settings, read per pass so a reload takes effect.</param>
    /// <param name="time">The one clock. Drives both the timer and the idle comparison.</param>
    /// <param name="logger">Optional structured logger.</param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    public HandleReclaimer(
        TransactionSessionRegistry sessions,
        QueryTaskRegistry queries,
        UpdateTaskRegistry updates,
        CommandTaskRegistry commands,
        IOptions<PersistenceOptions> options,
        TimeProvider time,
        ILogger<HandleReclaimer>? logger = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger;
    }

    /// <summary>
    /// Runs one reclaim pass over all four registries.
    /// </summary>
    /// <returns>How many handles were reclaimed.</returns>
    /// <remarks>
    /// INTERNAL AND CALLABLE DIRECTLY, so a test drives one pass against a fake clock instead of waiting
    /// for a timer. The pass is the unit of behaviour; the timer only decides when it runs.
    /// </remarks>
    internal int ReclaimOnce()
    {
        TimeSpan window = _options.Value.Handles.IdleExpiry;
        DateTimeOffset now = _time.GetUtcNow();

        int reclaimed = _queries.ReclaimIdle(now, window)
            + _updates.ReclaimIdle(now, window)
            + _commands.ReclaimIdle(now, window);

        // The session pass runs LAST and is told which sessions are still spoken for, so a session whose
        // task survived this pass survives with it.
        HashSet<string> pinned =
        [
            .. _queries.LiveSessionIds,
            .. _updates.LiveSessionIds,
            .. _commands.LiveSessionIds,
        ];

        reclaimed += _sessions.ReclaimIdle(now, window, pinned);

        if (reclaimed > 0)
        {
            _logger?.LogWarning(
                "Reclaimed {ReclaimedCount} abandoned work handles that had been idle for longer than "
                + "{IdleWindow}. A reclaimed handle means a caller took one and never released it; the "
                + "handle values are deliberately not recorded.",
                reclaimed,
                window);
        }

        return reclaimed;
    }

    /// <summary>
    /// Drains every registry, tasks before sessions.
    /// </summary>
    /// <returns>How many handles were released.</returns>
    /// <remarks>
    /// THE ORDER IS LOAD-BEARING AND IS THE REVERSE OF ACQUISITION. A task borrows the transaction its
    /// session owns, so draining sessions first would leave a task holding a returned reference. No pinning
    /// is needed here precisely because the tasks are gone by the time the sessions are reached.
    /// </remarks>
    internal int DrainAll()
    {
        int drained = _queries.Drain() + _updates.Drain() + _commands.Drain() + _sessions.Drain();

        if (drained > 0)
        {
            _logger?.LogInformation(
                "Released {DrainedCount} live work handles during shutdown. Each was a handle a caller "
                + "still held: shutting down without releasing them would leave connections and worker "
                + "tasks to the operating system to clean up.",
                drained);
        }

        return drained;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>PeriodicTimer</c> OVER THE INJECTED CLOCK, so a fake time provider advances the schedule as well
    /// as the comparison. A <c>Task.Delay</c> loop would put the schedule on the real clock and make any
    /// test of expiry a test of patience.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_options.Value.Handles.SweepInterval, _time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    _ = ReclaimOnce();
                }
                catch (Exception failure) when (failure is InvalidOperationException
                    or ObjectDisposedException)
                {
                    // Best-effort by design: see this type's remarks. The next pass tries again, and the
                    // exception type alone is recorded because the message could carry a statement or a
                    // path.
                    _logger?.LogWarning(
                        "A handle reclaim pass failed with {FailureType} and was abandoned. The next pass "
                        + "will retry; no handle was left in a partially released state, because each is "
                        + "removed from its table before it is torn down.",
                        failure.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping. This is the ordinary exit and is not a fault.
        }

        _ = DrainAll();
    }
}
