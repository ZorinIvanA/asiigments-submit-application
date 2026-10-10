using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-017 «Движок лимитера: ShouldBlock не пишет метку» (idempotency, FR-003,
/// P1). FR-003 п.2: ShouldBlock — та же проверка без записи.
///
/// given: движок SlidingWindowLimiter, limit=5; у ключа K 4 метки в окне.
/// when:  несколько последовательных ShouldBlock(K, 60000, 5); затем
///        TryAcquire(K, 60000, 5).
/// then:  все ShouldBlock — false (меток не добавилось, 5-я позиция свободна);
///        TryAcquire — true и стал 5-й меткой.
/// </summary>
public sealed class Ts017_LimiterShouldBlockNoWriteTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts017";

    [Fact]
    public void ShouldBlock_RepeatedChecks_DoesNotWriteMarks()
    {
        // given: ключ K с 4 метками в окне.
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(new FakeTimeProvider(), store, Policy);
        for (var mark = 1; mark <= 4; mark++)
        {
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        // when: несколько последовательных ShouldBlock(K, 60000, 5).
        var checks = new[]
        {
            limiter.ShouldBlock("K", WindowMs, Limit),
            limiter.ShouldBlock("K", WindowMs, Limit),
            limiter.ShouldBlock("K", WindowMs, Limit),
        };

        // then: все false — меток не добавилось (5-я позиция свободна).
        Assert.All(checks, blocked => Assert.False(blocked, "ShouldBlock — чистая проверка без записи"));
        Assert.True(store.TryGetMarks(Policy, "K", out var marksBeforeAcquire));
        Assert.Equal(4, marksBeforeAcquire.Count);

        // when: TryAcquire(K, 60000, 5).
        var allowed = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true и стала 5-й меткой.
        Assert.True(allowed, "5-я позиция свободна после ShouldBlock-проверок");
        Assert.True(store.TryGetMarks(Policy, "K", out var marksAfterAcquire));
        Assert.Equal(5, marksAfterAcquire.Count);
    }
}
