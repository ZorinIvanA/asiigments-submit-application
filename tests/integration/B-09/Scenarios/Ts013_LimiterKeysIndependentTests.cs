using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-013 «Лимитер: ключи независимы» (negative, FR-003, P1).
///
/// given: политика limit=5; ключ A заблокирован (5 меток в окне), ключ B без меток.
/// when:  TryAcquire('test-policy', B, now).
/// then:  true (FR-003 AC «Ключи независимы»).
/// </summary>
public sealed class Ts013_LimiterKeysIndependentTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 3_000_000;
    private const string KeyA = "ts013-key-a";
    private const string KeyB = "ts013-key-b";

    [Fact]
    public void ExhaustedKeyA_DoesNotBlockKeyB()
    {
        // given: ключ A заблокирован — 5 меток в окне.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(engine.TryAcquire(KeyA, WindowMs, Limit), $"Предусловие: метка {attempt + 1} ключа A должна допускаться.");
        }

        Assert.False(
            engine.TryAcquire(KeyA, WindowMs, Limit),
            "Предусловие: ключ A с 5 метками должен быть заблокирован.");

        // when: TryAcquire(B, now) — ключ B без меток.
        var allowed = engine.TryAcquire(KeyB, WindowMs, Limit);

        // then: true — исчерпание ключа A не переносится на ключ B.
        Assert.True(allowed, "Ключ B должен допускаться независимо от заблокированного ключа A.");
        Assert.Equal(1, B09LimiterInspection.MarksCount(engine, KeyB));
    }
}
