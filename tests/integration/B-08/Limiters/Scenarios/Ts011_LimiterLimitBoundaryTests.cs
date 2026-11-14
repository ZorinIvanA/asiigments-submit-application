using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-011 «Движок лимитера: граница лимита, отказ не пишет метку» (boundary,
/// FR-003, P0). Тестовый шов — фактический движок
/// src/api/LabsApp/Auth/RateLimiting/SlidingWindowLimiter: политика фиксируется
/// конструктором SlidingWindowLimiter(TimeProvider, IRateLimitStore, policy),
/// now берётся из инжектируемых часов (FakeTimeProvider.SetUtcNow), windowMs и
/// limit передаются на каждом вызове. Наблюдение меток — публичный шов
/// IRateLimitStore.TryGetMarks(policy, key, out marks): снимок существующих
/// меток БЕЗ вычистки — чтение выполняется ПОСЛЕ проверочного вызова
/// (вычистку выполняет сам движок внутри TryAcquire).
///
/// given: экземпляр с window=60000 мс, limit=5; у ключа K в окне 4 метки;
///        фиксированное время T0.
/// when:  TryAcquire(K, 60000, 5) при now=T0; затем повторный TryAcquire(K,
///        60000, 5) с тем же now.
/// then:  первый вызов = true (меток стало 5), второй = false; число меток K
///        осталось 5 — отказ метку НЕ дописывает и окно НЕ продлевает
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
        // given: инжектируемые часы на фиксированном времени T0; у ключа K
        // в окне 4 метки.
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

        // when: TryAcquire(K, 60000, 5) при now=T0 и повторный вызов с тем же now.
        var first = limiter.TryAcquire("K", WindowMs, Limit);
        var second = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true (меток стало 5), затем false; меток по-прежнему 5 —
        // отказ метку НЕ дописывает и окно НЕ продлевает.
        Assert.True(first, "первый вызов на границе лимита допускается");
        Assert.False(second, "второй вызов при исчерпанном лимите отклоняется");
        Assert.True(store.TryGetMarks(Policy, "K", out var marks), "ключ K присутствует в состоянии политики");
        Assert.Equal(5, marks.Count);
        Assert.All(marks, mark => Assert.Equal(T0.ToUnixTimeMilliseconds(), mark));
    }
}
