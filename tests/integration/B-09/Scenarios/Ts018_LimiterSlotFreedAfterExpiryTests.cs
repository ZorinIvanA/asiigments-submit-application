using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-018 «Движок лимитера: освобождение слота после истечения меток»
/// (boundary, FR-003, P1).
///
/// given: 10000 индивидуальных ключей; метки ключа K1 заведомо истекли (часы
///        переведены за границу окна); инжектируемые часы.
/// when:  TryAcquire(policy, K1, now) — вычищает метки (запись K1 с устаревшими
///        метками удаляется) — затем запрос с новым ключом Knew.
/// then:  устаревшая запись K1 удалена (после допуска у K1 ровно одна свежая
///        метка — дописанная этим допуском); Knew получает индивидуальный слот
///        (не overflow); опустевшие записи K2..K10000 удалены; размер словаря
///        ≤10001 (FR-003 AC «Освобождение слота»).
///
/// Примечание к чтению then: TryAcquire(K1) при 0 живых меток допускается и
/// ДОПИСЫВАЕТ метку (FR-003: «запрос допускается … и тогда метка now
/// дописывается»), поэтому «K1 удалён из словаря» наблюдается как удаление
/// устаревшей записи (у K1 не остаётся ни одной старой метки), а не как
/// отсутствие записи после собственного допуска.
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts015_LimiterSlotFreedAfterWindow) не
/// изменялся.
/// </summary>
public sealed class Ts018_LimiterSlotFreedAfterExpiryTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 15_000_000;
    private const string KeyK1 = "ts018-k1";
    private const string KeyNew = "ts018-knew";

    [Fact]
    public void ExpiredMarksFreeIndividualSlot_NewKeyGetsIndividualSlot()
    {
        // given: 10000 индивидуальных ключей (K1 — ts018-k1) с живыми метками в T0.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts018-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts018-k{index} должен получить индивидуальный слот.");
        }

        // given: часы переведены за границу окна — метки всех ключей истекли.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + WindowMs + 1));

        // when: TryAcquire(policy, K1, now) — вычищает устаревшие метки K1.
        var k1Allowed = engine.TryAcquire(KeyK1, WindowMs, Limit);

        // then: устаревшая запись K1 удалена — после допуска у K1 ровно ОДНА
        // свежая метка (ни одной из пяти старых не осталось).
        Assert.True(k1Allowed, "TryAcquire(K1) после истечения меток должен допускаться.");
        Assert.Equal(1, B09LimiterInspection.MarksCount(engine, KeyK1));

        // when: запрос с новым уникальным ключом Knew.
        var newAllowed = engine.TryAcquire(KeyNew, WindowMs, Limit);

        // then: Knew получил ИНДИВИДУАЛЬНЫЙ слот (не overflow-корзину);
        // опустевшие записи K2..K10000 удалены — индивидуальных записей ровно 2
        // (K1 и Knew); overflow-корзина пуста; размер словаря ≤10001.
        Assert.True(newAllowed, "Новый ключ после освобождения слота должен допускаться.");
        Assert.True(
            B09LimiterInspection.HasIndividualKey(engine, KeyNew),
            "Новый ключ должен получить индивидуальную запись в словаре политики (не overflow).");
        Assert.Equal(0, B09LimiterInspection.OverflowMarksCount(engine));
        Assert.Equal(2, B09LimiterInspection.IndividualKeysCount(engine));
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            "Размер словаря политики обязан быть ≤" +
            $"{SlidingWindowLimiter.MaxTrackedKeys + 1}, фактически: {engine.TrackedKeysCount}.");
    }
}
