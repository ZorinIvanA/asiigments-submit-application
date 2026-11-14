using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-015 «Лимитер: потолок MaxTrackedKeys=10000 и overflow-корзина»
/// (boundary, FR-003 + NFR-003, P0). Швы состояния:
/// SlidingWindowLimiter.TrackedKeysCount (= IRateLimitStore.TrackedKeysCount)
/// и служебная запись <see cref="SlidingWindowLimiter.OverflowKeyName"/>.
///
/// given: в политике (window=60000, limit=5) уже 10000 индивидуальных ключей
///        с живыми метками (= SlidingWindowLimiter.MaxTrackedKeys).
/// when:  последовательные TryAcquire с 6 новыми уникальными ключами
///        K10001..K10006.
/// then:  все новые ключи учитываются в одной overflow-корзине '__overflow__'
///        с теми же окном и лимитом: первые 5 из 6 суммарных запросов — true,
///        6-й — false; индивидуальные слоты новым ключам не выделяются;
///        размер словаря политики ≤ 10001 (AC FR-003 «Потолок ключей и
///        overflow»; NFR-003).
/// </summary>
public sealed class Ts015_KeyCeilingOverflowBucketTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "b08-ts015";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_SixNewKeysAtCeiling_AllShareOverflowBucket_FifthTrueSixthFalse()
    {
        // given: 10000 индивидуальных ключей с живыми метками (фиксированное
        // время); инспекция TrackedKeysCount = MaxTrackedKeys = 10000.
        var time = new FakeTimeProvider();
        time.SetUtcNow(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(limiter.TryAcquire($"k{index}", WindowMs, Limit), $"подготовка: ключ k{index}");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, limiter.TrackedKeysCount);

        // when: последовательные TryAcquire с 6 новыми уникальными ключами
        // K10001..K10006 при полном потолке индивидуальных слотов.
        var outcomes = new List<bool>(6);
        for (var index = 1; index <= 6; index++)
        {
            outcomes.Add(limiter.TryAcquire($"K1000{index}", WindowMs, Limit));
        }

        // then: первые 5 из 6 суммарных запросов — true, 6-й — false (лимит
        // корзины те же 5); все новые ключи — в одной корзине '__overflow__',
        // индивидуальные слоты им не выделяются; размер словаря ≤ 10001.
        Assert.Equal(5, outcomes.Count(static allowed => allowed));
        Assert.False(outcomes[5], "6-й суммарный запрос корзины за окно отклоняется (limit=5)");
        Assert.True(
            store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out var bucket),
            "при полном потолке создана overflow-корзина '__overflow__'");
        Assert.Equal(5, bucket.Count);
        for (var index = 1; index <= 6; index++)
        {
            Assert.False(
                store.TryGetMarks(Policy, $"K1000{index}", out _),
                $"ключу K1000{index} не выделяется индивидуальный слот");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, limiter.TrackedKeysCount);
        Assert.True(
            limiter.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"размер словаря политики {limiter.TrackedKeysCount} превышает 10001");
    }
}
