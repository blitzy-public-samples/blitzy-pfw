// ==================================================================================================
//  TransactionPoolIdleSweeper.cs - THE THING THAT ACTUALLY FIRES THE POOL'S IDLE COLLECTION
//  ------------------------------------------------------------------------------------------------
//  WHY THIS EXISTS
//  The transaction pool is REFERENCE COUNTED WITH IDLE EXPIRY: when keep-alive is on, an entry whose
//  last reference is dropped is RETAINED rather than destroyed, and it is collected later once it has
//  been idle for its configured window [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L97,
//  :L174, :L215]. The legacy fires that collection by subscribing the pool to the framework's own idle
//  notification, inside the keep-alive branch [:L80].
//
//  A SERVICE HAS NO IDLE NOTIFICATION TO SUBSCRIBE TO. There is no message pump and no framework tick,
//  so without something in this file the pool's collection entry point is reachable only from a test:
//  a retained connection would then be held for the life of the process, which is a RESOURCE LEAK
//  rather than a slow service. This is the port of that subscription, and it is the whole reason the
//  type exists.
//
//  THREE PROPERTIES THAT ARE LOAD BEARING RATHER THAN INCIDENTAL
//    1. NON-OVERLAPPING. One sweep at a time, always. A sweep disconnects and destroys transactions
//       outside the pool's lock, so two concurrent sweeps could each decide to collect the same entry.
//       The timer is awaited rather than scheduled, which makes overlap structurally impossible instead
//       of merely unlikely.
//    2. CLEAN SHUTDOWN. The loop ends on the host's stopping token and swallows exactly one exception -
//       the cancellation that token raises - so a shutdown is not reported as a fault and a fault is not
//       mistaken for a shutdown.
//    3. A SWEEP FAILURE DOES NOT END THE SWEEPER. An unhandled exception from a background service
//       stops the HOST by default, so a single failed collection would take the whole service down and
//       every subsequent collection with it. That is the wrong trade for a maintenance sweep: the fault
//       is recorded and the next tick still runs. This is deliberately NOT the fail-fast posture, which
//       belongs to a STRUCTURAL fault at startup - a data directory that cannot be created, a
//       configuration that cannot be validated - and an idle collection is neither.
//
//  IT READS THE INJECTED CLOCK AND NOTHING ELSE. The interval comes from the transaction-pool options
//  and the timer is built on the injected TimeProvider, so a deterministic test drives the sweep by
//  advancing its own clock rather than by waiting (AAP 0.6.7).
// ==================================================================================================

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Transactions;

/// <summary>
/// Drives <see cref="TransactionPool.OnIdle"/> on a periodic, non-overlapping schedule.
/// </summary>
internal sealed class TransactionPoolIdleSweeper : BackgroundService
{
    private readonly TransactionPool _pool;

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<TransactionPoolIdleSweeper> _logger;

    private readonly TimeSpan? _interval;

    /// <summary>
    /// Creates the sweeper.
    /// </summary>
    /// <param name="pool">The pool whose idle collection is driven.</param>
    /// <param name="options">The configuration carrying the sweep interval.</param>
    /// <param name="timeProvider">The seamed clock the timer runs on.</param>
    /// <param name="logger">The operator channel.</param>
    internal TransactionPoolIdleSweeper(
        TransactionPool pool,
        IOptions<PersistenceOptions> options,
        TimeProvider timeProvider,
        ILogger<TransactionPoolIdleSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        PersistenceOptions resolved = options.Value
            ?? throw new InvalidOperationException(
                "The persistence options resolved to no value, so the idle sweeper cannot be configured.");

        _interval = (resolved.TransactionPool
            ?? throw new InvalidOperationException(
                "The persistence options carry no transaction-pool section, so the idle sweeper cannot "
                + "be configured."))
            .ResolveIdleSweepInterval();
    }

    /// <summary>
    /// The number of sweeps performed, exposed so a deterministic test can assert the schedule rather
    /// than infer it from a side effect.
    /// </summary>
    internal int SweepCount { get; private set; }

    /// <summary>
    /// Whether a sweep is scheduled at all, which is false when the interval is non-positive.
    /// </summary>
    internal bool IsScheduled => _interval is not null;

    /// <summary>
    /// Performs exactly one sweep, recording rather than propagating a failure.
    /// </summary>
    /// <remarks>
    /// Separated from the loop so the sweep's own error handling is testable without a timer, and so the
    /// loop reads as scheduling rather than as scheduling-plus-policy.
    /// </remarks>
    internal void SweepOnce()
    {
        try
        {
            _pool.OnIdle();

            SweepCount++;
        }
        catch (ObjectDisposedException)
        {
            // The pool was disposed between the tick and the call, which happens on shutdown and is not a
            // fault. Nothing is recorded, because a shutdown race is not information an operator needs.
        }
        catch (Exception exception)
        {
            // See this file's header: a maintenance sweep must not take the host down. NUMERIC AND FIXED
            // TEXT ONLY on the message, with the exception object on the operator channel where it
            // belongs - the pool's own diagnostics are already redacted at their source.
            _logger.LogError(
                exception,
                "An idle sweep of the transaction pool failed; the next scheduled sweep will still run.");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval is not { } interval)
        {
            _logger.LogInformation(
                "The transaction-pool idle sweep is disabled by configuration, so retained transactions "
                + "are collected only when the pool is asked to collect or is disposed.");

            return;
        }

        _logger.LogInformation(
            "The transaction-pool idle sweep runs every {IntervalSeconds}s.",
            interval.TotalSeconds);

        // AWAITED, NOT SCHEDULED. A PeriodicTimer whose tick is awaited cannot overlap its own body, which
        // is the property the header calls structural rather than likely.
        using PeriodicTimer timer = new(interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                SweepOnce();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A CLEAN SHUTDOWN, and the filter is what separates it from a genuine cancellation fault. It
            // is swallowed rather than rethrown so stopping the host is not reported as a failure.
        }
    }
}
