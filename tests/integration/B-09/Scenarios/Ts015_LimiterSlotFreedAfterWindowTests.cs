using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-015 «Лимитер: освобождение слота после истечения меток» (boundary, FR-003, P1).
///
/// given: движок — LabsApp.Auth.RateLimiting.SlidingWindowLimiter фактического
///        дерева (конструктор SlidingWindowLimiter(TimeProvider); потолок —
///        константа SlidingWindowLimiter.MaxTrackedKeys=10000; решающее правило
///        — TryAcquire(key, windowMs, limit), now из TimeProvider);
///        10000 индивидуальных ключей; метки всех ключей истекли (инжектированные
///        часы переведены за границу окна).
/// when:  TryAcquire(K1, now) — вычищает метки K1, затем TryAcquire с новым
///        уникальным ключом Knew.
/// then:  K1 пере-создан с одной свежей меткой; Knew получил индивидуальный
///        слот (НЕ overflow); опустевшие записи K2..K10000 удалены —
///        индивидуальных записей ровно 2; размер словаря ≤10001 (FR-003 AC
///        «Освобождение слота после окна»).
/// </summary>
public sealed class Ts015_LimiterSlotFreedAfterWindowTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 5_000_000;
    private const string KeyK1 = "ts015-k1";
    private const string KeyNew = "ts015-knew";

    [Fact]
    public void ExpiredMarksFreeIndividualSlot_ForNewKey()
    {
        // given: 10000 индивидуальных ключей (K1 — ts015-k1) с живыми метками в T0.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts015-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts015-k{index} должен получить индивидуальный слот.");
        }

        // given: часы переведены за границу окна — все метки истекли.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + WindowMs + 1));

        // when: TryAcquire(K1, now) — вычищает истекшие метки K1.
        var k1Allowed = engine.TryAcquire(KeyK1, WindowMs, Limit);

        // then: метки K1 вычищены (осталась единственная свежая метка) — слот K1
        // освобождён от устаревшей записи.
        Assert.True(k1Allowed, "TryAcquire(K1) после истечения меток должен допускаться.");
        Assert.Equal(1, B09LimiterInspection.MarksCount(engine, KeyK1));

        // when: TryAcquire с новым уникальным ключом Knew.
        var newAllowed = engine.TryAcquire(KeyNew, WindowMs, Limit);

        // then: Knew получил ИНДИВИДУАЛЬНЫЙ слот (не overflow-корзину); опустевшие
        // истёкшие записи K2..K10000 удалены — индивидуальных записей ровно 2
        // (K1 и Knew); размер словаря ≤10001.
        Assert.True(newAllowed, "Новый ключ после освобождения слота должен допускаться.");
        Assert.True(
            B09LimiterInspection.HasIndividualKey(engine, KeyNew),
            "Новый ключ должен получить индивидуальную запись в словаре политики.");
        Assert.True(
            B09LimiterInspection.OverflowMarksCount(engine) == 0,
            "Overflow-корзина должна остаться пустой: слот был освобождён, а не расширен корзиной.");
        Assert.True(
            B09LimiterInspection.IndividualKeysCount(engine) == 2,
            "Опустевшие истёкшие записи K2..K10000 должны быть удалены: индивидуальных " +
            $"записей ровно 2 (K1 и Knew), фактически {B09LimiterInspection.IndividualKeysCount(engine)}.");
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            "Число ключей политики (с корзиной) должно быть ≤" +
            $"{SlidingWindowLimiter.MaxTrackedKeys + 1}, фактически {engine.TrackedKeysCount}.");
    }
}
