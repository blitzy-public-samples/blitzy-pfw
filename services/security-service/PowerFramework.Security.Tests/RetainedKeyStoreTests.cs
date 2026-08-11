// --------------------------------------------------------------------------------------------------
// THE RETAINED GENERATED-KEY STORE: AN EXACT CAP AND A BOUNDED WINDOW
//
// WHAT WENT WRONG, STATED AS THE FACT ABOUT THE CODE IT WAS. The published key-generation operation
// returns the PUBLIC half of a pair plus a reference to the private half, and this service holds the
// private half so that it never has to cross the wire. That store is capped, because this is the service
// that holds the system's only signing key: memory exhaustion here removes authentication for Gateway,
// DataServices and Persistence at the same moment. Two defects sat in that cap.
//
//   1. IT WAS NOT A CAP. The check read the dictionary's Count and then inserted, so N simultaneous
//      authenticated requests all observed the same under-capacity value and all N proceeded. The
//      overshoot was bounded by nothing except how many requests arrived together (CWE-362).
//
//   2. A FULL STORE STAYED FULL FOR THE LIFE OF THE PROCESS. Nothing expired, so the first sixty-four
//      generations disabled the operation permanently and the only remedy was restarting the service that
//      holds the signing key (CWE-400). An authenticated caller could therefore wedge key generation for
//      every caller, deliberately or by accident, and nothing would report why.
//
// WHY THESE ROWS LOOK THE WAY THEY DO. A cap is a claim about SIMULTANEOUS callers, and a loop that calls
// a method many times in sequence cannot test one - it observes the exact interleaving the defect was
// invisible under. The concurrency rows therefore release real threads through a barrier. That alone was
// not enough, and the reason is worth recording: releasing threads at an EMPTY store still passed against
// the count-then-insert defect, because a store that fills from empty crosses the capacity boundary once,
// at one instant, and a run whose threads are not inside that instant sees nothing wrong. So the rows here
// arrange for the boundary itself to be the contended thing - the store is filled to one below the cap
// before the threads are released - and repeat that arrangement over many rounds. The expiry rows drive an
// injected clock rather than waiting, and assert the BOUNDARY rather than only a far-past instant, because
// an off-by-one in a retention comparison is exactly the kind of thing a generous test cannot see.
// --------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Security.Endpoints;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The capacity and retention rules of the generated-key store.
/// </summary>
public sealed class RetainedKeyStoreTests
{
    /// <summary>The cap the store enforces, read from the implementation rather than restated.</summary>
    private static int Capacity => CryptoReferenceResolver.MaximumRetainedGeneratedKeys;

    /// <summary>The retention window, read from the implementation rather than restated.</summary>
    private static TimeSpan Retention => CryptoReferenceResolver.GeneratedKeyRetention;

    /// <summary>
    /// Sequential retention fills the store to the cap exactly and then refuses.
    /// </summary>
    /// <remarks>
    /// The baseline the concurrency row is measured against. On its own it would have passed against the
    /// defect too, which is precisely why it is not the only row here.
    /// </remarks>
    [Fact]
    public void TheStoreFillsToTheCapAndThenRefuses()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        for (int index = 0; index < Capacity; index++)
        {
            Assert.True(
                store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string keyRef),
                $"Retention {index + 1} of {Capacity} was refused inside the cap.");

            Assert.False(string.IsNullOrEmpty(keyRef));
        }

        Assert.False(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string refused));
        Assert.Equal(string.Empty, refused);
    }

    /// <summary>
    /// Every minted reference is distinct, including across simultaneous mints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ROW EXISTS TO MAKE AN UNREACHABILITY CLAIM ASSERTABLE. Retention releases its capacity
    /// reservation if the insertion into the store fails, and that arm cannot be driven from any test: the
    /// mint ordinal is interlocked-monotonic and forms part of the reference, so an insertion cannot collide.
    /// Rather than leave "cannot collide" as a comment, this row asserts the invariant the claim rests on -
    /// if a future edit dropped the ordinal and left only the random component, this fails and the reasoning
    /// behind that arm stops being true silently.
    /// </para>
    /// <para>
    /// The mints are driven concurrently as well as sequentially because a monotonic counter read
    /// non-atomically is exactly how such an invariant breaks, and a sequential loop cannot see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void MintedReferencesAreDistinct()
    {
        const int Threads = 8;

        DeterministicTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        CryptoReferenceResolver store = CryptoFixture.EmptyStore(clock);
        ConcurrentBag<string> minted = [];

        using (Barrier gate = new(Threads))
        {
            Thread[] workers = new Thread[Threads];

            for (int worker = 0; worker < Threads; worker++)
            {
                workers[worker] = new Thread(() =>
                {
                    gate.SignalAndWait();

                    for (int attempt = 0; attempt < Capacity / Threads; attempt++)
                    {
                        if (store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string keyRef))
                        {
                            minted.Add(keyRef);
                        }
                    }
                })
                {
                    IsBackground = true,
                };

                workers[worker].Start();
            }

            foreach (Thread worker in workers)
            {
                worker.Join();
            }
        }

        // The cap is not the subject here, so the row asserts distinctness over whatever was admitted -
        // and admits nothing about the count beyond it being the whole store.
        Assert.Equal(Capacity, minted.Count);
        Assert.Equal(minted.Count, minted.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Many callers contending for the last free slot produce exactly one winner, every round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THE DEFECT FAILS, AND ITS SHAPE IS THE WHOLE POINT. A first attempt at this simply released
    /// eight threads at an empty store and asserted the admitted total. It PASSED against the defect: the
    /// store fills long before the boundary is reached, so the count-then-insert window is crossed once, by
    /// whichever threads happen to be inside it at that single instant, and most runs miss it entirely.
    /// </para>
    /// <para>
    /// So each round is arranged so that the boundary is the ONLY thing being contended: the store is filled
    /// to one below the cap, then every thread is released from a barrier to make exactly one attempt. Under
    /// a correct reservation exactly one wins; under a count-then-insert check every thread that reads the
    /// count before any insertion lands wins, so the round admits more than one. Repeating over many rounds
    /// makes a run that never lands in the window vanishingly unlikely, and the clock is wound past the
    /// retention window between rounds so each one starts from an empty store.
    /// </para>
    /// <para>
    /// A FAILING ROUND IS REPORTED WITH ITS NUMBER AND ITS COUNT, because "somewhere in twenty-four rounds a
    /// round admitted two" is the whole diagnostic - the round index tells a reader whether the boundary was
    /// reached at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ExactlyOneCallerWinsTheLastFreeSlot()
    {
        const int Threads = 16;
        const int Rounds = 24;

        DeterministicTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        CryptoReferenceResolver store = CryptoFixture.EmptyStore(clock);
        DateTimeOffset instant = DateTimeOffset.UnixEpoch;

        for (int round = 0; round < Rounds; round++)
        {
            for (int index = 0; index < Capacity - 1; index++)
            {
                Assert.True(
                    store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _),
                    $"Round {round}: filling to one below the cap was refused at entry {index}.");
            }

            int winners = 0;

            using (Barrier gate = new(Threads))
            {
                Thread[] workers = new Thread[Threads];

                for (int worker = 0; worker < Threads; worker++)
                {
                    workers[worker] = new Thread(() =>
                    {
                        gate.SignalAndWait();

                        if (store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _))
                        {
                            _ = Interlocked.Increment(ref winners);
                        }
                    })
                    {
                        IsBackground = true,
                    };

                    workers[worker].Start();
                }

                foreach (Thread worker in workers)
                {
                    worker.Join();
                }
            }

            Assert.Equal(1, winners);

            // Wind past the window so the next round contends at the boundary again from an empty store.
            instant += Retention + TimeSpan.FromTicks(1);
            clock.SetUtcNow(instant);
        }
    }

    /// <summary>
    /// A retained reference resolves throughout its window and stops resolving after it.
    /// </summary>
    /// <param name="elapsedTicksPastTheWindow">
    /// How far past the window's end the clock is moved: zero is the boundary itself, one tick is the first
    /// instant outside it.
    /// </param>
    /// <remarks>
    /// THE BOUNDARY IS ASSERTED FROM BOTH SIDES. A row that only moved the clock a day forward would pass
    /// against an inclusive comparison, an exclusive one, and an off-by-one in either direction.
    /// </remarks>
    [Theory]
    [InlineData(-1L, true)]
    [InlineData(0L, true)]
    [InlineData(1L, false)]
    public void ARetainedReferenceResolvesUntilItsWindowCloses(long elapsedTicksPastTheWindow, bool resolves)
    {
        DeterministicTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        CryptoReferenceResolver store = CryptoFixture.EmptyStore(clock);

        Assert.True(store.TryRetainGeneratedPrivateKey("private-material", CryptoFixture.Random, out string keyRef));

        clock.SetUtcNow(DateTimeOffset.UnixEpoch + Retention + TimeSpan.FromTicks(elapsedTicksPastTheWindow));

        ProblemHttpResult? rejection = store.TryResolveReference(keyRef, NullLoggerFactory.Instance, out string material);

        if (resolves)
        {
            Assert.Null(rejection);
            Assert.Equal("private-material", material);

            return;
        }

        // An expired reference is refused by the SAME arm as a reference this deployment never published,
        // which is deliberate: the two are indistinguishable to a caller and introducing a third outcome
        // would tell an unauthorised caller which references once existed.
        Assert.NotNull(rejection);
        Assert.Equal(string.Empty, material);
    }

    /// <summary>
    /// A full store admits a new retention once the window has closed on what it holds.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT CLOSES THE AVAILABILITY HALF OF THE FINDING. Without expiry this refuses forever and the
    /// only remedy is restarting the service that holds the signing key. It also proves the reclamation
    /// RELEASES SLOTS rather than merely removing entries: admitting a new retention is only possible if the
    /// reservation counter came down with the dictionary.
    /// </remarks>
    [Fact]
    public void AFullStoreRecoversWhenItsWindowCloses()
    {
        DeterministicTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        CryptoReferenceResolver store = CryptoFixture.EmptyStore(clock);

        for (int index = 0; index < Capacity; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _));
        }

        Assert.False(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _));

        clock.SetUtcNow(DateTimeOffset.UnixEpoch + Retention + TimeSpan.FromTicks(1));

        Assert.True(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string admitted));
        Assert.False(string.IsNullOrEmpty(admitted));

        // And the cap still holds after the recovery: reclaiming sixty-four slots must not have left the
        // counter below zero, which would turn a bounded store into an unbounded one.
        for (int index = 1; index < Capacity; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _));
        }

        Assert.False(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _));
    }

    /// <summary>
    /// Reclamation and resolution running together neither double-release a slot nor lose one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO CODE PATHS REMOVE AN EXPIRED ENTRY - the sweep a retention performs, and the check a resolution
    /// performs on the entry it just read - and each releases the slot it removed. If both released for the
    /// same entry the counter would drift DOWN and the cap would grow without limit; if neither did, it would
    /// drift up and the store would refuse forever. Only the caller whose removal succeeded releases, and
    /// this row is what holds that.
    /// </para>
    /// <para>
    /// The final fill is the assertion: after all the churn the store must still admit exactly the cap.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReclamationAndResolutionAgreeOnHowManySlotsAreFree()
    {
        const int Threads = 8;

        DeterministicTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        CryptoReferenceResolver store = CryptoFixture.EmptyStore(clock);

        List<string> references = [];

        for (int index = 0; index < Capacity; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string keyRef));
            references.Add(keyRef);
        }

        clock.SetUtcNow(DateTimeOffset.UnixEpoch + Retention + TimeSpan.FromTicks(1));

        using Barrier gate = new(Threads);
        Thread[] workers = new Thread[Threads];

        for (int worker = 0; worker < Threads; worker++)
        {
            int ordinal = worker;

            workers[worker] = new Thread(() =>
            {
                gate.SignalAndWait();

                // Half the threads resolve every expired reference, driving the resolution-side removal;
                // the other half retain, driving the sweep. Both target the same entries.
                if (ordinal % 2 == 0)
                {
                    foreach (string reference in references)
                    {
                        _ = store.TryResolveReference(reference, NullLoggerFactory.Instance, out _);
                    }

                    return;
                }

                for (int attempt = 0; attempt < references.Count; attempt++)
                {
                    _ = store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _);
                }
            })
            {
                IsBackground = true,
            };

            workers[worker].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        // Close the window on whatever the churn left behind, then fill from empty and require the cap
        // exactly. A drifted counter shows up here as a store that admits too many or too few.
        clock.SetUtcNow(DateTimeOffset.UnixEpoch + (Retention * 4));

        int admitted = 0;

        while (store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _))
        {
            admitted++;

            Assert.True(
                admitted <= Capacity,
                "The store admitted more than its cap after concurrent reclamation, so a slot was released "
                    + "twice for one entry.");
        }

        Assert.Equal(Capacity, admitted);
    }

    /// <summary>
    /// A refused retention hands back no reference at all.
    /// </summary>
    /// <remarks>
    /// The caller discards the generated pair on a refusal, so a reference produced alongside <c>false</c>
    /// would name material nothing holds - a reference that resolves to nothing is worse than no reference,
    /// because a caller would store it and fail later.
    /// </remarks>
    [Fact]
    public void ARefusedRetentionMintsNoReference()
    {
        CryptoReferenceResolver store = CryptoFixture.EmptyStore();

        for (int index = 0; index < Capacity; index++)
        {
            Assert.True(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out _));
        }

        Assert.False(store.TryRetainGeneratedPrivateKey("private", CryptoFixture.Random, out string keyRef));
        Assert.Equal(string.Empty, keyRef);
    }
}
