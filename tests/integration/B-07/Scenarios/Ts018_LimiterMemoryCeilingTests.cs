using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-018 «Память лимитера: 50000 уникальных ключей — ≤10001 в памяти»
/// (nfr, NFR-003 + FR-003, P0). Шов счётчика отслеживаемых ключей
/// (IRateLimitStore.TrackedKeysCount); фиксированное время.
///
/// given: чистая политика лимитера.
/// when:  50000 вызовов TryAcquire с уникальными ключами в одном окне.
/// then:  счётчик ключей политики ≤10001 (MaxTrackedKeys + overflow);
///        исключений нет (NFR-003, методика verification из spec).
/// </summary>
public sealed class Ts018_LimiterMemoryCeilingTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts018";
    private const int UniqueKeys = 50_000;

    [Fact]
    public void TryAcquire_50000UniqueKeys_TrackedKeysBounded()
    {
        // given: чистая политика лимитера (фиксированное время).
        var time = new FakeTimeProvider();
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);

        // when: 50000 TryAcquire с уникальными ключами в одном окне
        // (исключение на любом вызове провалит тест).
        for (var i = 1; i <= UniqueKeys; i++)
        {
            _ = limiter.TryAcquire($"flood-{i}", WindowMs, Limit);
        }

        // then: счётчик ключей политики ≤ MaxTrackedKeys + overflow.
        Assert.InRange(
            store.TrackedKeysCount(Policy),
            0,
            SlidingWindowLimiter.MaxTrackedKeys + 1);
    }
}
