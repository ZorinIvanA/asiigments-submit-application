using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-016 «Движок лимитера: потокобезопасность» (concurrency, FR-003, P0).
///
/// given: политика limit=5; ключ K; фиксированное время.
/// when:  32 параллельных вызова TryAcquire(K) в один момент.
/// then:  ровно 5 вызовов вернули true (FR-003 AC «Потокобезопасность»).
/// </summary>
public sealed class Ts016_LimiterThreadSafetyTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts016";
    private const int ParallelCallers = 32;

    [Fact]
    public void TryAcquire_ParallelCallsOnSameKey_ExactlyLimitAllowed()
    {
        // given: чистая политика limit=5, ключ K, фиксированное время.
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);

        // when: 32 параллельных TryAcquire(K) в один момент.
        var results = new bool[ParallelCallers];
        Parallel.For(0, ParallelCallers, i => results[i] = limiter.TryAcquire("K", WindowMs, Limit));

        // then: ровно 5 допусков; в состоянии ключа ровно 5 меток.
        Assert.Equal(Limit, results.Count(static allowed => allowed));
        Assert.True(store.TryGetMarks(Policy, "K", out var marks));
        Assert.Equal(Limit, marks.Count);
    }
}
