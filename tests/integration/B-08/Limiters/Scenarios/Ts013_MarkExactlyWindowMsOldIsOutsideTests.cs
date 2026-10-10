using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-013 «Лимитер: метка ровно windowMs назад — уже вне окна»
/// (boundary, FR-003, P0). Наблюдение меток — публичный шов
/// IRateLimitStore.TryGetMarks (снимок БЕЗ вычистки; чтение ПОСЛЕ вызова).
///
/// given: метка ключа K поставлена в момент t0; политика window=60000 мс,
///        limit=5; в окне 5 меток, включая t0 (t0, t0+1, …, t0+4 мс — самая
///        ранняя ровно на границе проверяемого вызова).
/// when:  TryAcquire(K, 60000, 5) при now = t0+60000.
/// then:  true — метка t0 вычищена: учитываются метки строго новее границы
///        now−windowMs (AC FR-003 «Граница окна»); реализация с НЕстрогим
///        сравнением вернула бы false (5 меток ≥ limit).
/// </summary>
public sealed class Ts013_MarkExactlyWindowMsOldIsOutsideTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts013";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_OldestMarkExactlyWindowMsOld_PurgedAndAllowed()
    {
        // given: метки ключа K в моменты t0..t0+4 мс (пишутся движком TryAcquire
        // со сдвигом часов, чтобы минимум меток политики был известен движку);
        // в окне 5 меток, включая t0.
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var offsetMs = 0; offsetMs <= 4; offsetMs++)
        {
            time.SetUtcNow(T0.AddMilliseconds(offsetMs));
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка t0+{offsetMs}мс");
        }

        Assert.True(store.TryGetMarks(Policy, "K", out var prepared));
        Assert.Equal(5, prepared.Count);
        Assert.Contains(T0.ToUnixTimeMilliseconds(), prepared);

        // when: TryAcquire(K, 60000, 5) при now = t0+60000 (метка t0 ровно
        // windowMs назад — ровно на границе now−windowMs).
        time.SetUtcNow(T0.AddMilliseconds(WindowMs));
        var allowed = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true — метка t0 вычищена (вне окна); в ключе 5 меток: четыре
        // выживших (t0+1..t0+4) и дописанная метка now.
        Assert.True(allowed, "метка возраста ровно windowMs — вне окна: вызов допускается");
        Assert.True(store.TryGetMarks(Policy, "K", out var marks));
        Assert.Equal(5, marks.Count);
        Assert.DoesNotContain(T0.ToUnixTimeMilliseconds(), marks);
        Assert.Equal(T0.AddMilliseconds(1).ToUnixTimeMilliseconds(), marks.Min());
    }
}
