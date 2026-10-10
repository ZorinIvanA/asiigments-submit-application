using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-014 «Движок лимитера: граница лимита в окне и отказ без записи метки»
/// (boundary, FR-003, P0).
///
/// given: движок IRateLimiterEngine с политикой window=60000 мс, limit=5
///        (решающее правило реального движка зоны C-004 SlidingWindowLimiter —
///        TryAcquire(key, windowMs, limit), now из инжектируемых часов
///        FakeTimeProvider); в окне ключа K уже 4 метки.
/// when:  TryAcquire(policy, K, now); затем повторный TryAcquire(policy, K, now).
/// then:  первый вызов true (меток стало 5); второй false; число меток ключа K
///        в окне по-прежнему 5 — отказ метку НЕ дописывает и окно НЕ продлевает
///        (FR-003 AC «Граница лимита в окне»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts014_LimiterRejectNoMarkNoWindowExtensionTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 21_000_000;
    private const string Key = "ts014-wave-boundary-key";

    [Fact]
    public void FifthAcquireTrue_SixthFalse_RejectWritesNoMark_DoesNotExtendWindow()
    {
        // given: в окне ключа K уже 4 метки (T0..T0+3000); инжектируемые часы.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + attempt * 1000));
            Assert.True(
                engine.TryAcquire(Key, WindowMs, Limit),
                $"Предусловие: метка {attempt + 1} из 4 должна допускаться.");
        }

        // when: TryAcquire(policy, K, now) дважды в один и тот же момент now.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + 4 * 1000));
        var first = engine.TryAcquire(Key, WindowMs, Limit);
        var second = engine.TryAcquire(Key, WindowMs, Limit);

        // then: первый вызов true — меток стало 5.
        Assert.True(first, "Первый вызов при 4 метках < limit 5 должен быть допущен.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));

        // then: второй false; число меток ключа K в окне по-прежнему 5 —
        // отказ метку НЕ дописывает.
        Assert.False(second, "Шестой вызов в окне (меток уже 5 = limit) должен быть отклонён.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));

        // then: окно НЕ продлевается: старейшая метка T0 выходит из окна ровно в
        // T0+60000 (возраст ровно windowMs — вне окна), и вызов допускается.
        // Отказ с дописанной меткой now=T0+4000 оставил бы 5 живых меток — false.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + WindowMs));
        Assert.True(
            engine.TryAcquire(Key, WindowMs, Limit),
            "Отказ не должен продлевать окно: после выхода старейшей метки вызов допускается.");
    }
}
