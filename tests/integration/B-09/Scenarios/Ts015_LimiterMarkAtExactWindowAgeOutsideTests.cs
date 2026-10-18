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
/// then:  true: метка t0 вычищена — учитываются метки строго новее now−windowMs
///        (FR-003 AC «Граница окна»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts015_LimiterMarkAtExactWindowAgeOutsideTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 22_000_000;
    private const string Key = "ts015-wave-window-age-key";

    [Fact]
    public void MarkExactlyWindowMsOld_IsOutsideWindow_TryAcquireAllowed()
    {
        // given: метка в момент t0=T0 и ещё 4 метки в окне (T0+1000..T0+4000) —
        // итого 5, окно ключа заполнено.
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

        // then: true — метка t0 вычищена (учитываются метки СТРОГО новее
        // now−windowMs); в окне остались 4 метки + новая = ровно 5.
        Assert.True(
            allowed,
            "Метка возраста ровно windowMs — вне окна: TryAcquire(t0+60000) должен быть допущен.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));
    }
}
