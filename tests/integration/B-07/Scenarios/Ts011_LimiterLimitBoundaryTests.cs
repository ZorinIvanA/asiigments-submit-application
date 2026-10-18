using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-011 «Движок лимитера: граница лимита, отказ не пишет метку» (boundary,
/// FR-003, P0). Тестовый шов движка: TryAcquire/ShouldBlock + хранилище
/// состояния (инжектируемые часы — FakeTimeProvider, ADR-002).
///
/// given: политика window=60000 мс, limit=5; у ключа K в окне 4 метки;
///        фиксированное время T0.
/// when:  TryAcquire(K, T0); затем повторный TryAcquire(K, T0).
/// then:  первый вызов — true (меток стало 5), второй — false; число меток K
///        осталось 5 — отказ метку НЕ дописывает, окно не продлевается
///        (FR-003 AC «Граница лимита в окне»).
/// </summary>
public sealed class Ts011_LimiterLimitBoundaryTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts011";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_AtLimitBoundary_RefusalDoesNotWriteMark()
    {
        // given: у ключа K в окне 4 метки (фиксированное время T0).
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var mark = 1; mark <= 4; mark++)
        {
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        // when: TryAcquire(K, T0) и повторный TryAcquire(K, T0).
        var first = limiter.TryAcquire("K", WindowMs, Limit);
        var second = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true (меток стало 5), затем false; меток по-прежнему 5.
        Assert.True(first, "первый вызов на границе лимита допускается");
        Assert.False(second, "второй вызов при исчерпанном лимите отклоняется");
        Assert.True(store.TryGetMarks(Policy, "K", out var marks), "ключ K присутствует в состоянии политики");
        Assert.Equal(5, marks.Count);
    }
}
