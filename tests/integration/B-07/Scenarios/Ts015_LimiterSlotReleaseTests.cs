using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-015 «Движок лимитера: освобождение слота после истечения меток» (boundary,
/// FR-003 + NFR-003, P1). Шов состояния движка доступен; вычистка — вызовом
/// БЕЗ записи (ShouldBlock), затем допуск нового ключа TryAcquire.
///
/// given: 10000 индивидуальных ключей; метки ключа K1 истекли (тестовое время
///        переведено за границу окна ИМЕННО K1: метка k1 — в T0, метки остальных
///        9999 ключей — на 30 с позже и остаются в окне; потолок заполнен на
///        9999 живых ключей); limit=5.
/// when:  ShouldBlock(K1, now) — вычистка истёкших меток; затем
///        TryAcquire(Knew, now) с новым уникальным ключом Knew.
/// then:  ShouldBlock — false; K1 удалён из словаря политики (опустевший ключ
///        удаляется при вычистке; освобождён РОВНО ОДИН слот); TryAcquire(Knew) —
///        true, метки Knew в индивидуальном слоте при 9999 живых ключах — корзина
///        '__overflow__' меток не содержит; общее число ключей политики ≤10001
///        (FR-003 п.1 + п.3).
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
        // given: метка k1 — в момент T0; метки остальных 9999 ключей — в T0+30 с.
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        Assert.True(limiter.TryAcquire("k1", WindowMs, Limit), "подготовка: метка k1 в T0");
        time.Advance(TimeSpan.FromSeconds(30));
        for (var i = 2; i <= SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(limiter.TryAcquire($"k{i}", WindowMs, Limit), $"подготовка: ключ k{i}");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, store.TrackedKeysCount(Policy));

        // Тестовое время переведено за границу окна ТОЛЬКО метки k1 (T0+60с+1мс):
        // метки остальных ключей (T0+30с) остаются в окне — из 10000 слотов живы 9999.
        time.Advance(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1));

        // when: ShouldBlock(k1, now) — чистая проверка, вычищающая истёкшие метки.
        var blocked = limiter.ShouldBlock("k1", WindowMs, Limit);

        // then: false; k1 удалён из словаря политики (опустевший ключ удаляется);
        // освобождён ровно один индивидуальный слот (9999 выживших ключей).
        Assert.False(blocked, "после вычистки истёкших меток ключ k1 не блокируется");
        Assert.False(store.TryGetMarks(Policy, "k1", out _), "опустевший ключ k1 удалён из словаря политики");
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys - 1,
            store.TrackedKeysCount(Policy));

        // when: TryAcquire(knew, now) — новый уникальный ключ при 9999 живых ключах.
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
            store.TrackedKeysCount(Policy) <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"число записей политики {store.TrackedKeysCount(Policy)} превышает MaxTrackedKeys+1");
    }
}
