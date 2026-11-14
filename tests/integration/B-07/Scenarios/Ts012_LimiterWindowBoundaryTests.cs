using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-012 «Движок лимитера: метка ровно windowMs назад — вне окна» (boundary,
/// FR-003, P0). Тестовый шов движка + инжектируемые часы (ADR-002).
///
/// given: у ключа K метка в момент t0; в окне 5 меток; политика window=60000 мс,
///        limit=5.
/// when:  TryAcquire(K, t0+60000).
/// then:  true — метка t0 вычищена: учитываются метки строго новее границы
///        now−windowMs (FR-003 AC «Граница окна»).
/// </summary>
public sealed class Ts012_LimiterWindowBoundaryTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts012";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_MarkExactlyWindowMsOld_IsOutsideWindow()
    {
        // given: 5 меток ключа K в момент t0 (старейшая — ровно t0).
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var mark = 1; mark <= 5; mark++)
        {
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        // when: TryAcquire(K, t0+60000) — возраст метки t0 ровно windowMs.
        time.Advance(TimeSpan.FromMilliseconds(WindowMs));
        var allowed = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true — метка t0 вычищена (порог строго новее now−windowMs),
        // в окне остаётся только свежая метка.
        Assert.True(allowed, "метка возраста ровно windowMs — вне окна, запрос допускается");
        Assert.True(store.TryGetMarks(Policy, "K", out var marks));
        Assert.Single(marks);
        Assert.Equal(time.GetUtcNow().ToUnixTimeMilliseconds(), marks[0]);
    }
}
