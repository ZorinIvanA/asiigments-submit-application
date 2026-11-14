using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-210 «Лимитер: истёк только K1, остальные живы — пере-допуск K1 занимает
/// слот, новый ключ обслуживается overflow» (boundary, FR-003/NFR-003, P1).
///
/// given: движок LabsApp.Auth.RateLimiting.SlidingWindowLimiter фактического
///        дерева (конструктор SlidingWindowLimiter(TimeProvider); потолок —
///        константа SlidingWindowLimiter.MaxTrackedKeys=10000, корзина —
///        SlidingWindowLimiter.OverflowKeyName='__overflow__'); решающее правило
///        — TryAcquire(key, windowMs, limit) с windowMs=60000, limit=5;
///        часы FakeTimeProvider. K1 получил метку в момент T0, K2..K10000 — по
///        одной метке в момент T1=T0+59990 (все 10000 — индивидуальные записи);
///        часы переведены в now=T0+60000: метка K1 устарела (T0 не строго новее
///        границы now−windowMs=T0), метки K2..K10000 живы.
/// when:  TryAcquire(K1, 60000, 5); затем TryAcquire(Knew, 60000, 5) с новым
///        уникальным ключом Knew.
/// then:  первый вызов = true: истёкшая метка K1 вычищена, опустевшая запись
///        удалена и немедленно пере-создана с одной свежей меткой — K1 снова
///        занимает индивидуальный слот, словарь полон (10000 живых индивидуальных
///        записей); второй вызов = true, но Knew ОБСЛУЖИВАЕТСЯ OVERFLOW-КОРЗИНОЙ
///        '__overflow__': HasIndividualKey(Knew)=false, OverflowMarksCount=1;
///        TrackedKeysCount=10001 (FR-003(3): новый ключ при полном потолке
///        обслуживается overflow-корзиной; NFR-003: ≤10001; обратный
///        дискриминирующий случай к TS-015, где Knew получил бы ИНДИВИДУАЛЬНЫЙ
///        слот после массового истечения).
/// </summary>
public sealed class Ts210_LimiterOverflowAfterSlotRefillTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 5_000_000;
    private const long T1 = T0 + 59_990;
    private const long Now = T0 + WindowMs;
    private const string KeyK1 = "ts210-k1";
    private const string KeyNew = "ts210-knew";

    [Fact]
    public void ReAcquiredExpiredKeyRefillsCeiling_NewKeyIsServedByOverflowBucket()
    {
        // given: движок фактического дерева с инжектированными часами (T0).
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(clock);

        // given: K1 получил метку в момент T0; K2..K10000 — по одной метке в
        // момент T1=T0+59990 (порядок создания: K1 первым, затем остальные);
        // все 10000 — индивидуальные записи.
        Assert.True(
            engine.TryAcquire(KeyK1, WindowMs, Limit),
            "Предусловие: ключ K1 должен получить индивидуальный слот в момент T0.");
        clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T1));
        for (var index = 2; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts210-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts210-k{index} должен получить индивидуальный слот (T1).");
        }

        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B10LimiterInspection.IndividualKeysCount(engine));

        // given: часы переведены в now=T0+60000 — метка K1 (T0) устарела: порог
        // вычистки now−windowMs=T0, метка должна быть СТРОГО новее порога; метки
        // K2..K10000 (T1>T0) живы.
        clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(Now));

        // when: TryAcquire(K1) — истёкшая метка вычищена, запись пере-создана.
        var k1Allowed = engine.TryAcquire(KeyK1, WindowMs, Limit);

        // then: первый вызов = true: K1 снова занимает индивидуальный слот,
        // словарь полон (10000 живых индивидуальных записей), корзина не нужна.
        Assert.True(
            k1Allowed,
            "Истёкшая метка K1 вычищается — пере-допуск K1 должен состояться (true).");
        Assert.True(
            B10LimiterInspection.HasIndividualKey(engine, KeyK1),
            "Пере-допущенный K1 должен вернуть себе индивидуальную запись словаря политики.");
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B10LimiterInspection.IndividualKeysCount(engine));
        Assert.Equal(
            0,
            B10LimiterInspection.OverflowMarksCount(engine));

        // when: TryAcquire(Knew) с новым уникальным ключом при полном потолке.
        var newAllowed = engine.TryAcquire(KeyNew, WindowMs, Limit);

        // then: второй вызов = true, но Knew обслуживается overflow-корзиной:
        // индивидуальной записи у Knew нет, в корзине ровно одна метка,
        // TrackedKeysCount = 10001 (10000 индивидуальных + '__overflow__').
        Assert.True(
            newAllowed,
            "Запрос нового ключа при полном потолке обслуживается корзиной и допускается.");
        Assert.False(
            B10LimiterInspection.HasIndividualKey(engine, KeyNew),
            "Новый ключ при полном потолке НЕ должен получать индивидуальную запись — " +
            "он обслуживается overflow-корзиной (FR-003(3)).");
        Assert.Equal(
            1,
            B10LimiterInspection.OverflowMarksCount(engine));
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys + 1,
            engine.TrackedKeysCount);
    }
}
