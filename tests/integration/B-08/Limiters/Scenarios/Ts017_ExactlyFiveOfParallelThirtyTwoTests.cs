using System.Collections.Concurrent;
using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-017 «Лимитер: потокобезопасность — ровно 5 допусков из 32
/// параллельных» (concurrency, FR-003, P0).
///
/// given: политика limit=5; один ключ K; 32 потока синхронизированы барьером;
///        фиксированное now (FakeTimeProvider).
/// when:  32 параллельных TryAcquire(policy, K, now), выпущенных одновременно
///        барьером.
/// then:  ровно 5 вызовов вернули true, остальные — false; исключений нет
///        (AC FR-003 «Потокобезопасность»).
/// </summary>
public sealed class Ts017_ExactlyFiveOfParallelThirtyTwoTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts017";
    private const int ParallelCallers = 32;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_ThirtyTwoBarrierSyncedCallers_ExactlyFiveAllowed()
    {
        // given: политика limit=5; один ключ K; фиксированное now; 32 потока,
        // синхронизированные барьером (все TryAcquire стартуют одновременно).
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        var barrier = new Barrier(ParallelCallers);
        var results = new bool[ParallelCallers];
        var exceptions = new ConcurrentQueue<Exception>();
        var threads = new Thread[ParallelCallers];
        for (var index = 0; index < ParallelCallers; index++)
        {
            var caller = index;
            threads[index] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    results[caller] = limiter.TryAcquire("K", WindowMs, Limit);
                }
                catch (Exception exception)
                {
                    exceptions.Enqueue(exception);
                }
            });
        }

        // when: 32 параллельных TryAcquire(policy, K, now) в один момент.
        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // then: ровно 5 вызовов вернули true, остальные — false; исключений
        // нет; в состоянии ключа ровно 5 меток.
        Assert.Empty(exceptions);
        Assert.Equal(Limit, results.Count(static allowed => allowed));
        Assert.True(store.TryGetMarks(Policy, "K", out var marks));
        Assert.Equal(Limit, marks.Count);
    }
}
