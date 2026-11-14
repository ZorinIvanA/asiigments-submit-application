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
///        SlidingWindowLimiter.MaxTrackedKeys); инжектируемые часы.
/// when:  TryAcquire с новыми уникальными ключами K10001..K10005 (пять
///        суммарных обращений), затем шестой TryAcquire с новым уникальным
///        ключом K10006.
/// then:  первые пять TryAcquire = true — все новые ключи обслужены единой
///        overflow-корзиной '__overflow__'; шестой TryAcquire = false (при
///        limit=5 шестой суммарный запрос в корзине за окно отклоняется);
///        размер словаря политики ≤10001 (FR-003 AC «Потолок ключей и overflow»).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts017_LimiterCeilingNewKeysToOverflowBucketTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 24_000_000;

    [Fact]
    public void NewKeysBeyondCeiling_ServedBySingleOverflowBucket_SixthBucketRequestRejected()
    {
        // given: 10000 индивидуальных ключей с живыми метками в один момент T0.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts017-wave-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts017-wave-k{index} должен получить индивидуальный слот.");
        }

        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B09LimiterInspection.IndividualKeysCount(engine));

        // when: пять суммарных обращений новыми уникальными ключами
        // K10001..K10005 (все сверх потолка, тот же момент T0).
        var overflowKeys = new[]
        {
            "ts017-wave-K10001", "ts017-wave-K10002", "ts017-wave-K10003",
            "ts017-wave-K10004", "ts017-wave-K10005",
        };
        var firstFive = new bool[overflowKeys.Length];
        for (var attempt = 0; attempt < overflowKeys.Length; attempt++)
        {
            firstFive[attempt] = engine.TryAcquire(overflowKeys[attempt], WindowMs, Limit);
        }

        // then: первые пять TryAcquire = true — все новые ключи обслужены ЕДИНОЙ
        // overflow-корзиной '__overflow__' (в ней ровно 5 меток).
        Assert.True(
            firstFive.All(allowed => allowed),
            "Первые пять запросов сверх потолка должны допускаться через overflow-корзину, " +
            $"фактически допущено {firstFive.Count(allowed => allowed)} из 5.");
        Assert.Equal(5, B09LimiterInspection.OverflowMarksCount(engine));

        // when: шестой TryAcquire с новым уникальным ключом K10006 в том же окне.
        var sixth = engine.TryAcquire("ts017-wave-K10006", WindowMs, Limit);

        // then: false — при limit=5 шестой суммарный запрос в корзине за окно
        // отклоняется; меток в корзине по-прежнему 5.
        Assert.False(
            sixth,
            "Шестой суммарный запрос в overflow-корзину за окно должен быть отклонён.");
        Assert.Equal(5, B09LimiterInspection.OverflowMarksCount(engine));

        // then: размер словаря политики ≤10001 (10000 индивидуальных + корзина);
        // новые ключи индивидуальных слотов не получили.
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B09LimiterInspection.IndividualKeysCount(engine));
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Размер словаря политики обязан быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1}, " +
            $"фактически: {engine.TrackedKeysCount}.");
    }
}
