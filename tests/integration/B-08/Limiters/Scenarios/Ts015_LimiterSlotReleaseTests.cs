using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// TS-015 «Движок лимитера: освобождение слота после истечения меток
/// (наблюдение через ShouldBlock)» (boundary, FR-003 + NFR-003, P0).
/// Основание стимула — текст спеки: ShouldBlock — «та же проверка без записи»
/// (FR-003 п.2), метки «вычищаются при каждой проверке» (глоссарий
/// RateLimitMark.timestamps; NFR-003) — вычистка истёкших меток и удаление
/// опустевших ключей выполняются и в ShouldBlock (по дереву:
/// SlidingWindowLimiter.ShouldBlock вызывает PurgeStale). TryAcquire для
/// наблюдения освобождения непригоден: допущенный вызов обязан дописать метку
/// (FR-003 п.1). Буквенная формулировка AC «Освобождение слота после окна»
/// внутренне противоречит п.1 FR-003 (дефект спеки зафиксирован для владельца);
/// кейс верифицирует требование п.1+п.3 («удаляются опустевшие ключи»;
/// «освобождение слота — после удаления опустевшего ключа») наблюдаемым образом.
///
/// given: движок SlidingWindowLimiter, limit=5; в политике 10000 индивидуальных
///        ключей; метки ключа K1 истекли (тестовое время FakeTimeProvider
///        переведено за границу окна ИМЕННО K1: метка k1 — в T0, метки
///        остальных 9999 ключей — на 30 с позже и остаются в окне).
/// when:  ShouldBlock(K1, 60000, 5); затем TryAcquire(Knew, 60000, 5) с новым
///        уникальным ключом.
/// then:  ShouldBlock — false; K1 удалён из словаря политики:
///        IRateLimitStore.GetKeys(policy) не содержит K1,
///        TryGetMarks(policy, K1, out _) — false (чтение ПОСЛЕ вызова);
///        TryAcquire(Knew) — true, метки Knew учтены в индивидуальном слоте —
///        в '__overflow__' меток нет; TrackedKeysCount ≤ 10001.
/// </summary>
public sealed class Ts015_LimiterSlotReleaseTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts015";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldBlock_PurgesExpiredMarks_FreesIndividualSlot()
    {
        // given: метка k1 — в момент T0; метки остальных 9999 ключей — в T0+30 с
        // (потолок заполнен, из 10000 слотов живы 9999).
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        Assert.True(limiter.TryAcquire("k1", WindowMs, Limit), "подготовка: метка k1 в T0");
        time.Advance(TimeSpan.FromSeconds(30));
        for (var i = 2; i <= SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(limiter.TryAcquire($"k{i}", WindowMs, Limit), $"подготовка: ключ k{i}");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, limiter.TrackedKeysCount);

        // Тестовое время переведено за границу окна ТОЛЬКО метки k1 (T0+60с+1мс):
        // метки остальных ключей (T0+30с) остаются в окне.
        time.Advance(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1));

        // when: ShouldBlock(K1, 60000, 5) — чистая проверка, вычищающая истёкшие метки.
        var blocked = limiter.ShouldBlock("k1", WindowMs, Limit);

        // then: false; K1 удалён из словаря политики (опустевший ключ удаляется
        // при вычистке); освобождён ровно один индивидуальный слот (9999 живых).
        Assert.False(blocked, "после вычистки истёкших меток ключ k1 не блокируется");
        Assert.DoesNotContain("k1", store.GetKeys(Policy));
        Assert.False(store.TryGetMarks(Policy, "k1", out _), "опустевший ключ k1 удалён из словаря политики");
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys - 1, limiter.TrackedKeysCount);

        // when: TryAcquire(Knew, 60000, 5) — новый уникальный ключ при 9999 живых.
        var allowed = limiter.TryAcquire("k-new", WindowMs, Limit);

        // then: true; метки knew — в индивидуальном слоте (корзина '__overflow__'
        // меток не содержит); суммарно ≤10001 ключа.
        Assert.True(allowed, "новому ключу выделен индивидуальный слот после освобождения");
        Assert.True(store.TryGetMarks(Policy, "k-new", out var knewMarks));
        Assert.Single(knewMarks);
        Assert.False(
            store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out _),
            "корзина '__overflow__' меток не содержит");
        Assert.True(
            limiter.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"число записей политики {limiter.TrackedKeysCount} превышает MaxTrackedKeys+1");
    }
}
