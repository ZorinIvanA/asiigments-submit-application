using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-012 «Лимитер: граница лимита в окне; отказ не пишет метку»
/// (boundary, FR-003, P0). Движок SlidingWindowLimiter (IF-006): политика
/// фиксируется конструктором, now — из инжектируемых часов
/// (FakeTimeProvider.SetUtcNow); наблюдение меток — публичный шов
/// IRateLimitStore.TryGetMarks (снимок БЕЗ вычистки; чтение ПОСЛЕ вызова —
/// вычистку выполняет сам движок внутри TryAcquire).
///
/// given: движок с политикой window=60000 мс, limit=5; у ключа K уже 4 метки
///        в окне; инжектируемые часы дают now=T0.
/// when:  TryAcquire(K, 60000, 5) дважды подряд при том же now.
/// then:  первый вызов — true (меток стало 5), второй — false; число меток K
///        осталось 5 и все равны T0: отклонённый вызов метку НЕ дописывает и
///        окно НЕ продлевает (AC FR-003 «Граница лимита в окне»).
/// </summary>
public sealed class Ts012_LimitBoundaryRefusalKeepsFiveMarksTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts012";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_TwiceAtLimitBoundary_RefusalWritesNoMark()
    {
        // given: часы на now=T0; у ключа K уже 4 метки в окне.
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var mark = 1; mark <= 4; mark++)
        {
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        Assert.True(store.TryGetMarks(Policy, "K", out var prepared));
        Assert.Equal(4, prepared.Count);

        // when: TryAcquire(K, 60000, 5) дважды подряд при now=T0.
        var first = limiter.TryAcquire("K", WindowMs, Limit);
        var second = limiter.TryAcquire("K", WindowMs, Limit);

        // then: первый вызов — true (меток стало 5), второй — false; число
        // меток K осталось 5, все метки — в T0: отклонённый вызов метку НЕ
        // дописывает и окно НЕ продлевает.
        Assert.True(first, "первый вызов на границе лимита допускается (меток станет 5)");
        Assert.False(second, "второй вызов при исчерпанном лимите отклоняется");
        Assert.True(store.TryGetMarks(Policy, "K", out var marks), "ключ K присутствует в состоянии политики");
        Assert.Equal(5, marks.Count);
        Assert.All(marks, mark => Assert.Equal(T0.ToUnixTimeMilliseconds(), mark));
    }
}
