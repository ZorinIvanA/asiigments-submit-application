using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-015 «Движок лимитера: метка ровно windowMs назад — вне окна»
/// (boundary, FR-003, P0).
///
/// given: политика window=60000 мс, limit=5; у ключа K метка в момент t0 и ещё
///        4 метки в окне (итого 5); инжектируемые часы.
/// when:  TryAcquire(policy, K, t0+60000).
/// then:  true: метка t0 вычищена — учитываются метки строго новее now−windowMs,
///        метка возраста ровно windowMs уже вне окна (FR-003 AC «Граница окна»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts012_LimiterMarkExactlyWindowOldOutside) не
/// изменялся.
/// </summary>
public sealed class Ts015_LimiterWindowBoundaryMarkOutsideTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 12_000_000;
    private const string Key = "ts015-window-boundary-key";

    [Fact]
    public void MarkExactlyWindowOld_IsPurged_TryAcquireAllowed()
    {
        // given: метка в момент t0=T0 и ещё 4 метки новее (T0+1000..T0+4000) —
        // окно ключа заполнено (5 меток = limit).
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + attempt * 1000));
            Assert.True(
                engine.TryAcquire(Key, WindowMs, Limit),
                $"Предусловие: метка {attempt + 1} из 5 должна допускаться.");
        }

        // when: TryAcquire(policy, K, t0+60000) — метка t0 ровно на границе
        // now−windowMs.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + WindowMs));
        var allowed = engine.TryAcquire(Key, WindowMs, Limit);

        // then: true — метка t0 вычищена (порог «строго новее now−windowMs»),
        // в окне остались 4 живые метки + новая: ровно 5.
        Assert.True(
            allowed,
            "Метка возраста ровно windowMs — вне окна: вызов должен быть допущен.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));
    }
}
