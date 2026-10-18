using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-012 «Движок лимитера: метка ровно windowMs назад — вне окна (заполненное
/// окно, ранняя метка ровно на границе)» (boundary, FR-003, P0). Движок
/// SlidingWindowLimiter (политика фиксируется конструктором; now — из
/// инжектируемых часов FakeTimeProvider через SetUtcNow; windowMs и limit
/// передаются на каждом вызове); наблюдение меток — публичный шов
/// IRateLimitStore.TryGetMarks(policy, key, out marks): снимок существующих
/// меток БЕЗ вычистки — чтение ПОСЛЕ проверочного вызова (вычистку выполняет
/// сам движок внутри TryAcquire/ShouldBlock).
///
/// given (фикстура согласована с when — окно ЗАПОЛНЕНО, ранняя метка ровно на
///        границе): политика window=60000 мс, limit=5; у ключа K ровно 5 меток,
///        записанных в моменты t0, t0+1, t0+2, t0+3, t0+4 (мс): самая ранняя
///        (t0) приходится ровно на границу t_check−60000 проверяемого вызова
///        (t_check = t0+60000), прочие четыре — строго внутри окна.
/// when:  часы переведены на t_check = t0+60000 (SetUtcNow); TryAcquire(K,
///        60000, 5); затем чтение меток швом IRateLimitStore.TryGetMarks.
/// then:  TryAcquire — true: метка t0 вычищена (учитываются метки СТРОГО новее
///        границы now−windowMs; метка возраста ровно windowMs — уже вне окна);
///        TryGetMarks после вызова содержит ровно 5 меток — t0+1, t0+2, t0+3,
///        t0+4 и дописанную t_check; метки t0 в снимке нет. Реализация с
///        НЕстрогим сравнением (оставляющая метку ровно windowMs в окне)
///        вернула бы false (5 меток ≥ limit) — кейс фальсифицирует расширение
///        окна на одну метку (FR-003 AC «Граница окна»; глоссарий «Скользящее
///        окно»).
/// </summary>
public sealed class Ts012_LimiterWindowBoundaryTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts012";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_EarliestMarkExactlyWindowMsOld_PurgedAndRequestAllowed()
    {
        // given: окно ЗАПОЛНЕНО — 5 меток ключа K в моменты t0, t0+1, t0+2,
        // t0+3, t0+4 (мс); метки пишутся движком (TryAcquire со сдвигом часов),
        // чтобы инкрементальный минимум меток политики был известен движку и
        // полная вычистка выполнялась на проверяемом вызове.
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var offsetMs = 0; offsetMs <= 4; offsetMs++)
        {
            time.SetUtcNow(T0.AddMilliseconds(offsetMs));
            Assert.True(limiter.TryAcquire("K", WindowMs, Limit), $"подготовка: метка t0+{offsetMs}мс");
        }

        // Предусловие: у K ровно 5 меток t0..t0+4; самая ранняя (t0) — ровно на
        // границе t_check−60000, прочие четыре — строго внутри окна.
        Assert.True(store.TryGetMarks(Policy, "K", out var prepared));
        Assert.Equal(5, prepared.Count);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), prepared.Min());

        // when: часы переведены на t_check = t0+60000 (SetUtcNow);
        // TryAcquire(K, 60000, 5).
        time.SetUtcNow(T0.AddMilliseconds(WindowMs));
        var allowed = limiter.TryAcquire("K", WindowMs, Limit);

        // then: true — метка t0 (возраст ровно windowMs) вычищена: в окне
        // осталось 4 метки (t0+1..t0+4) < limit, запрос допускается.
        // Реализация с НЕстрогим сравнением оставила бы 5 меток ≥ limit и
        // вернула false.
        Assert.True(allowed, "метка возраста ровно windowMs — вне окна, запрос допускается");

        // then: снимок ПОСЛЕ вызова — ровно 5 меток: t0+1, t0+2, t0+3, t0+4 и
        // дописанная t_check = t0+60000; метки t0 в снимке нет.
        Assert.True(store.TryGetMarks(Policy, "K", out var marks));
        var expected = new long[]
        {
            T0.AddMilliseconds(1).ToUnixTimeMilliseconds(),
            T0.AddMilliseconds(2).ToUnixTimeMilliseconds(),
            T0.AddMilliseconds(3).ToUnixTimeMilliseconds(),
            T0.AddMilliseconds(4).ToUnixTimeMilliseconds(),
            T0.AddMilliseconds(WindowMs).ToUnixTimeMilliseconds(),
        };
        Assert.Equal(5, marks.Count);
        Assert.Equal(
            expected.OrderBy(static mark => mark),
            marks.OrderBy(static mark => mark));
        Assert.DoesNotContain(T0.ToUnixTimeMilliseconds(), marks);
    }
}
