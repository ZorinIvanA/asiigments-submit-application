using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-016 «Лимитер: освобождение слота после истечения меток ключа»
/// (boundary, FR-003 + NFR-003, P1). Буква AC FR-003 «Освобождение слота
/// после окна» требует TryAcquire(K1) и затем удаление K1, но TryAcquire по
/// FR-003 обязан дописать новую метку — слот при этом освобождён быть не
/// может; кейс проверяет намерение требования через непишущую проверку:
/// ShouldBlock (FR-003 п.2 «та же проверка без записи») вычищает истёкшие
/// метки и удаляет опустевшие ключи без записи (NFR-003 «метки вычищаются
/// при каждой проверке»).
///
/// given: в политике (window=60000, limit=5) 10000 индивидуальных ключей; все
///        метки ключа K1 истекли (инжектируемые часы переведены за границу
///        окна: T0+60000 — метка возраста ровно windowMs уже вне окна).
/// when:  ShouldBlock(policy, K1, now) — очистка без записи; затем
///        TryAcquire(policy, K_new, now) с новым уникальным ключом.
/// then:  K1 удалён из словаря (слот освобождён после истечения всех его
///        меток); K_new обслужен индивидуальным слотом, а не overflow-
///        корзиной; размер словаря ≤ 10001 (FR-003 п.3).
/// </summary>
public sealed class Ts016_SlotFreedAfterKeyMarksExpireTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts016";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldBlock_PurgesExpiredKey_NewKeyServedByIndividualSlot()
    {
        // given: 10000 индивидуальных ключей с метками в T0 (каждый — 1 метка);
        // часы переведены за границу окна — метки возраста windowMs уже вне окна.
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(limiter.TryAcquire($"k{index}", WindowMs, Limit), $"подготовка: ключ k{index}");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, limiter.TrackedKeysCount);
        time.SetUtcNow(T0.AddMilliseconds(WindowMs));

        // when: ShouldBlock(policy, K1, now) — очистка без записи.
        var blocked = limiter.ShouldBlock("k1", WindowMs, Limit);

        // then: K1 не блокируется и УДАЛЁН из словаря — слот освобождён после
        // истечения всех его меток (опустевший ключ удаляется при вычистке).
        Assert.False(blocked, "после истечения меток ключ K1 не блокируется");
        Assert.False(store.TryGetMarks(Policy, "k1", out _), "опустевший ключ K1 удалён из словаря политики");
        Assert.DoesNotContain("k1", store.GetKeys(Policy));

        // when: TryAcquire(policy, K_new, now) с новым уникальным ключом.
        var allowed = limiter.TryAcquire("k-new", WindowMs, Limit);

        // then: K_new обслужен индивидуальным слотом, а не overflow-корзиной;
        // размер словаря политики ≤ 10001.
        Assert.True(allowed, "новому ключу выделен индивидуальный слот после освобождения");
        Assert.True(store.TryGetMarks(Policy, "k-new", out var newMarks));
        Assert.Single(newMarks);
        Assert.False(
            store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out _),
            "новый ключ не обслуживается overflow-корзиной");
        Assert.True(
            limiter.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"размер словаря политики {limiter.TrackedKeysCount} превышает 10001");
    }
}
