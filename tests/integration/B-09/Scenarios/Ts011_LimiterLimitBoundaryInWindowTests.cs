using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-011 «Лимитер: граница лимита в окне; отказ не пишет метку» (boundary, FR-003, P0).
///
/// given: движок лимитера (IF-006; вызов кейса TryAcquire('test-policy', K, now)
///        выражен решающим правилом реального движка SlidingWindowLimiter —
///        TryAcquire(K, windowMs=60000, limit=5), now берётся из инжектированных
///        часов FakeTimeProvider); в окне ключа K уже 4 метки.
/// when:  TryAcquire('test-policy', K, now) дважды с одним и тем же now.
/// then:  первый вызов = true (меток стало 5); второй = false; число меток K
///        по-прежнему 5 — отказ метку НЕ дописывает и окно НЕ продлевает
///        (FR-003 AC «Граница лимита в окне»).
/// </summary>
public sealed class Ts011_LimiterLimitBoundaryInWindowTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 1_000_000;
    private const string Key = "ts011-key";

    [Fact]
    public void FifthAcquireAllowed_SixthRejected_WithoutWritingMark()
    {
        // given: движок с политикой window=60000 мс, limit=5; в окне ключа K
        // уже 4 метки (T0, T0+1000, T0+2000, T0+3000).
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + attempt * 1000));
            Assert.True(engine.TryAcquire(Key, WindowMs, Limit), $"Предусловие: метка {attempt + 1} из 4 должна допускаться.");
        }

        // when: TryAcquire дважды с одним и тем же now = T0+4000.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + 4 * 1000));
        var first = engine.TryAcquire(Key, WindowMs, Limit);
        var second = engine.TryAcquire(Key, WindowMs, Limit);

        // then: первый вызов = true — меток стало 5.
        Assert.True(first, "Первый вызов должен быть допущен: 4 метки < limit 5.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));

        // then: второй вызов = false; число меток K по-прежнему 5 — отказ метку
        // НЕ дописывает.
        Assert.False(second, "Второй вызов (6-й в окне) должен быть отклонён.");
        Assert.Equal(5, B09LimiterInspection.MarksCount(engine, Key));

        // then: окно НЕ продлевается. Старейшая метка (T0) выходит из окна ровно в
        // T0+60000 (граница «строго новее», TS-012); слот освобождается и вызов
        // допускается. Если бы отказ дописал метку (now=T0+4000), в окне осталось
        // бы 5 живых меток и вызов был бы отклонён.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + WindowMs));
        Assert.True(engine.TryAcquire(Key, WindowMs, Limit), "Отказ не должен продлевать окно: после выхода старейшей метки слот свободен.");
    }
}
