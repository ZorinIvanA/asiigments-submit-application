using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-017 «Движок лимитера: ShouldBlock не пишет метку» (idempotency, FR-003,
/// P1).
///
/// given: ключ K с 4 метками в окне; limit=5.
/// when:  несколько последовательных ShouldBlock(K); затем TryAcquire(K).
/// then:  все ShouldBlock — false (меток не добавилось, 5-я позиция свободна);
///        TryAcquire — true и стал 5-й меткой (FR-003 п.2: та же проверка без
///        записи).
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

        // when: несколько последовательных ShouldBlock(K).
        var checks = new[] { limiter.ShouldBlock("K", WindowMs, Limit), limiter.ShouldBlock("K", WindowMs, Limit), limiter.ShouldBlock("K", WindowMs, Limit) };

        // then: все false — меток не добавилось.
        Assert.All(checks, blocked => Assert.False(blocked, "ShouldBlock — чистая проверка без записи"));
        Assert.True(store.TryGetMarks(Policy, "K", out var marksBeforeAcquire));
        Assert.Equal(4, marksBeforeAcquire.Count);

        // when: TryAcquire(K).
        var allowed = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true и стала 5-й меткой.
        Assert.True(allowed, "5-я позиция свободна после ShouldBlock-проверок");
        Assert.True(store.TryGetMarks(Policy, "K", out var marksAfterAcquire));
        Assert.Equal(5, marksAfterAcquire.Count);
    }
}
