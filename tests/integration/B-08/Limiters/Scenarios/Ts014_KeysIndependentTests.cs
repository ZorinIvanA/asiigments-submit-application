using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-014 «Лимитер: ключи независимы» (boundary, FR-003, P1).
///
/// given: ключ A заблокирован (5 меток в окне), ключ B без меток; одна
///        политика (window=60000 мс, limit=5).
/// when:  TryAcquire(policy, B, now).
/// then:  true — блокировка A не влияет на B (AC FR-003 «Ключи независимы»).
/// </summary>
public sealed class Ts014_KeysIndependentTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts014";

    [Fact]
    public void TryAcquire_BlockedKeyA_KeyBAllowed()
    {
        // given: ключ A заблокирован — 5 меток в окне (6-й вызов — отказ).
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var mark = 1; mark <= Limit; mark++)
        {
            Assert.True(limiter.TryAcquire("A", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        Assert.False(limiter.TryAcquire("A", WindowMs, Limit), "предусловие: ключ A заблокирован");

        // when: TryAcquire(policy, B, now) — ключ B без меток.
        var allowed = limiter.TryAcquire("B", WindowMs, Limit);

        // then: true — ключи независимы.
        Assert.True(allowed, "ключ B обслуживается независимо от заблокированного ключа A");
    }
}
