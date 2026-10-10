using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-013 «Движок лимитера: ключи независимы» (data_integrity, FR-003, P1).
///
/// given: ключ A заблокирован (5 меток в окне), ключ B без меток; одна политика
///        limit=5.
/// when:  TryAcquire(B).
/// then:  true — блокировка A не влияет на B (FR-003 AC «Ключи независимы»).
/// </summary>
public sealed class Ts013_LimiterKeyIndependenceTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts013";

    [Fact]
    public void TryAcquire_BlockedKeyA_DoesNotAffectKeyB()
    {
        // given: ключ A заблокирован — 5 меток в окне (6-й отказ).
        var limiter = new SlidingWindowLimiter(new FakeTimeProvider(), new InMemoryRateLimitStore(), Policy);
        for (var mark = 1; mark <= 5; mark++)
        {
            Assert.True(limiter.TryAcquire("A", WindowMs, Limit), $"подготовка: метка {mark}");
        }

        Assert.False(limiter.TryAcquire("A", WindowMs, Limit), "предусловие: ключ A заблокирован");

        // when: TryAcquire(B).
        var allowed = limiter.TryAcquire("B", WindowMs, Limit);

        // then: true — блокировка A не влияет на B.
        Assert.True(allowed, "ключ B обслуживается независимо от заблокированного A");
    }
}
