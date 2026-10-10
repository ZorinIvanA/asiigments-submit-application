using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-017 «ShouldBlock не пишет метку» (idempotency, FR-003, P0).
///
/// given: политика limit=5; ключ K с 3 метками.
/// when:  ShouldBlock('test-policy', K, now) трижды (в терминах движка —
///        ShouldBlock(key, windowMs, limit): та же проверка без записи).
/// then:  все вызовы = false; число меток K осталось 3 — проверка без записи
///        не меняет состояние (FR-003: «(2) ShouldBlock — та же проверка без записи»).
/// </summary>
public sealed class Ts017_ShouldBlockPureCheckTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 7_000_000;
    private const string Key = "ts017-key";

    [Fact]
    public void RepeatedPureCheck_DoesNotMutateMarks()
    {
        // given: ключ K с 3 метками (T0, T0+1000, T0+2000).
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + attempt * 1000));
            Assert.True(engine.TryAcquire(Key, WindowMs, Limit), $"Предусловие: метка {attempt + 1} из 3 должна допускаться.");
        }

        // when: ShouldBlock трижды в один момент now = T0+3000.
        time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + 3 * 1000));
        var first = engine.ShouldBlock(Key, WindowMs, Limit);
        var second = engine.ShouldBlock(Key, WindowMs, Limit);
        var third = engine.ShouldBlock(Key, WindowMs, Limit);

        // then: все вызовы = false; число меток K осталось 3.
        Assert.False(first, "Проверка при 3 метках < limit 5 должна вернуть false.");
        Assert.False(second, "Повторная проверка должна вернуть false.");
        Assert.False(third, "Третья проверка должна вернуть false.");
        Assert.Equal(3, B09LimiterInspection.MarksCount(engine, Key));
    }
}
