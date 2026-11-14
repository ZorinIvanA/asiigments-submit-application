using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-017 «Движок лимитера: потолок MaxTrackedKeys и overflow-корзина»
/// (boundary, FR-003 + NFR-003, P0).
///
/// given: политика window=60000, limit=5; в политике уже 10000 индивидуальных
///        ключей с живыми метками (потолок — константа движка
///        SlidingWindowLimiter.MaxTrackedKeys=10000); инжектируемые часы.
/// when:  TryAcquire с новыми уникальными ключами K10001..K10005 (пять
///        суммарных обращений), затем шестой TryAcquire с новым уникальным
///        ключом K10006.
/// then:  первые пять TryAcquire = true — все новые ключи обслужены единой
///        overflow-корзиной '__overflow__'; шестой TryAcquire = false (при
///        limit=5 шестой суммарный запрос в корзине за окно отклоняется);
///        размер словаря политики ≤10001 (FR-003 AC «Потолок ключей и overflow»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts014_LimiterKeyCeilingOverflowBucket) не
/// изменялся.
/// </summary>
public sealed class Ts017_LimiterCeilingOverflowSingleBucketTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 14_000_000;

    [Fact]
    public void NewKeysBeyondCeiling_ShareOneOverflowBucket_SixthBucketRequestRejected()
    {
        // given: 10000 индивидуальных ключей с живыми метками в один момент T0.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts017-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts017-k{index} должен получить индивидуальный слот.");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, B09LimiterInspection.IndividualKeysCount(engine));

        // when: пять суммарных обращений новыми уникальными ключами
        // (K10001..K10005) — все сверх потолка, в один момент T0.
        var overflowKeys = new[] { "ts017-K10001", "ts017-K10002", "ts017-K10003", "ts017-K10004", "ts017-K10005" };
        var firstFive = new bool[overflowKeys.Length];
        for (var attempt = 0; attempt < overflowKeys.Length; attempt++)
        {
            firstFive[attempt] = engine.TryAcquire(overflowKeys[attempt], WindowMs, Limit);
        }

        // then: первые пять TryAcquire = true — новые ключи обслужены ЕДИНОЙ
        // overflow-корзиной '__overflow__' (в корзине ровно 5 меток).
        Assert.True(
            firstFive.All(allowed => allowed),
            "Первые пять запросов сверх потолка должны обслуживаться overflow-корзиной " +
            $"(фактически допущено: {firstFive.Count(allowed => allowed)} из 5).");
        Assert.Equal(5, B09LimiterInspection.OverflowMarksCount(engine));

        // when: шестой TryAcquire с новым уникальным ключом K10006 в том же окне.
        var sixth = engine.TryAcquire("ts017-K10006", WindowMs, Limit);

        // then: false — при limit=5 шестой суммарный запрос в корзине за окно
        // отклоняется.
        Assert.False(sixth, "Шестой суммарный запрос overflow-корзины за окно должен быть отклонён.");
        Assert.Equal(5, B09LimiterInspection.OverflowMarksCount(engine));

        // then: новые ключи НЕ получили индивидуальных слотов; размер словаря
        // политики ≤10001 (10000 индивидуальных + overflow-корзина).
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, B09LimiterInspection.IndividualKeysCount(engine));
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Размер словаря политики обязан быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1}, " +
            $"фактически: {engine.TrackedKeysCount}.");
    }
}
