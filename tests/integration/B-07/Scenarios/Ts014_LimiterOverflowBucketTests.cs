using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-014 «Движок лимитера: потолок 10000 ключей и overflow-корзина» (boundary,
/// FR-003 + NFR-003, P0). Шов состояния: счётчик отслеживаемых ключей
/// (IRateLimitStore.TrackedKeysCount) и запись '__overflow__'.
///
/// given: в политике 10000 индивидуальных ключей с живыми метками; limit=5;
///        фиксированное время.
/// when:  TryAcquire(K10001) и TryAcquire(K10002) (новые уникальные ключи), затем
///        ещё 4 запроса с новыми ключами K10003..K10006 в том же окне.
/// then:  K10001 и K10002 учитываются в одной overflow-корзине '__overflow__':
///        при limit=5 шестой суммарный запрос корзины за окно отклонён
///        (последний false); общее число ключей политики ≤10001 (FR-003 AC
///        «Потолок ключей и overflow»).
///
/// Контракт IF-006/IF-015 не гарантирует, что TryGetMarks отдаёт живое
/// внутреннее представление (а не снимок), поэтому после КАЖДОЙ мутации
/// состояние корзины перечитывается новым TryGetMarks — ассерты опираются
/// только на контрактное поведение хранилища.
/// </summary>
public sealed class Ts014_LimiterOverflowBucketTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const string Policy = "ts014";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_BeyondKeyCeiling_NewKeysShareOverflowBucket()
    {
        // given: 10000 индивидуальных ключей с живыми метками (фиксированное время).
        var time = new FakeTimeProvider(T0);
        var store = new InMemoryRateLimitStore();
        var limiter = new SlidingWindowLimiter(time, store, Policy);
        for (var i = 1; i <= SlidingWindowLimiter.MaxTrackedKeys; i++)
        {
            Assert.True(limiter.TryAcquire($"k{i}", WindowMs, Limit), $"подготовка: ключ k{i}");
        }

        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            store.TrackedKeysCount(Policy));

        // when: два новых уникальных ключа при полном потолке.
        Assert.True(limiter.TryAcquire("k10001", WindowMs, Limit), "K10001 допускается корзиной");
        Assert.True(store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out var bucketAfterFirst),
            "при полном потолке состояние '__overflow__' создано");
        Assert.Single(bucketAfterFirst);
        Assert.False(store.TryGetMarks(Policy, "k10001", out _), "индивидуальный слот K10001 не выделяется");

        Assert.True(limiter.TryAcquire("k10002", WindowMs, Limit), "K10002 допускается той же корзиной");
        Assert.True(store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out var bucketAfterSecond),
            "состояние '__overflow__' перечитано после второй мутации");
        Assert.Equal(2, bucketAfterSecond.Count);

        // when: ещё 4 запроса с новыми ключами в том же окне.
        var third = limiter.TryAcquire("k10003", WindowMs, Limit);
        var fourth = limiter.TryAcquire("k10004", WindowMs, Limit);
        var fifth = limiter.TryAcquire("k10005", WindowMs, Limit);
        var sixth = limiter.TryAcquire("k10006", WindowMs, Limit);

        // then: пятый суммарный запрос корзины ещё допускается, шестой отклонён;
        // корзина не разрослась; суммарно ≤10001 ключа. Метки корзины перечитаны
        // заново после последней мутации (свежий снимок, не удержанная ссылка).
        Assert.True(third);
        Assert.True(fourth);
        Assert.True(fifth);
        Assert.False(sixth, "шестой суммарный запрос корзины за окно отклоняется (limit=5)");
        Assert.True(store.TryGetMarks(Policy, SlidingWindowLimiter.OverflowKeyName, out var bucketAfterSixth),
            "состояние '__overflow__' перечитано после шестой попытки");
        Assert.Equal(5, bucketAfterSixth.Count);
        Assert.True(
            store.TrackedKeysCount(Policy) <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"число записей политики {store.TrackedKeysCount(Policy)} превышает MaxTrackedKeys+1");
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys + 1, store.TrackedKeysCount(Policy));
    }
}
