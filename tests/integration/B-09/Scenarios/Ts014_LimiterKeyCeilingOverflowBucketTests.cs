using LabsApp.Auth.RateLimiting;
using Microsoft.Extensions.Time.Testing;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-014 «Лимитер: потолок MaxTrackedKeys и overflow-корзина» (boundary, FR-003, NFR-003, P0).
///
/// given: чистая политика limit=5, window=60000 мс; в политике уже 10000
///        индивидуальных ключей с живыми метками (созданы через TryAcquire);
///        потолок — константа движка SlidingWindowLimiter.MaxTrackedKeys=10000.
/// when:  TryAcquire с новыми уникальными ключами K10001 и K10002, затем ещё
///        4 запроса новыми ключами в том же окне.
/// then:  K10001 и K10002 учитываются в ОДНОЙ overflow-корзине «__overflow__»;
///        при limit=5 шестой суммарный запрос корзины за окно отклоняется;
///        размер словаря политики ≤10001 (FR-003 AC «Потолок ключей и overflow»; NFR-003).
/// </summary>
public sealed class Ts014_LimiterKeyCeilingOverflowBucketTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const long T0 = 4_000_000;

    [Fact]
    public void NewKeysBeyondCeiling_ShareSingleOverflowBucket_WithSameLimit()
    {
        // given: 10000 индивидуальных ключей с живыми метками в один момент T0.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);
        for (var index = 1; index <= SlidingWindowLimiter.MaxTrackedKeys; index++)
        {
            Assert.True(
                engine.TryAcquire($"ts014-k{index}", WindowMs, Limit),
                $"Предусловие: ключ ts014-k{index} должен получить индивидуальный слот.");
        }

        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, B09LimiterInspection.IndividualKeysCount(engine));

        // when: новые уникальные ключи при полном потолке (все в момент T0).
        var k10001 = engine.TryAcquire("ts014-k10001", WindowMs, Limit);
        var k10002 = engine.TryAcquire("ts014-k10002", WindowMs, Limit);
        var extra = new bool[4];
        for (var attempt = 0; attempt < 4; attempt++)
        {
            extra[attempt] = engine.TryAcquire($"ts014-new{attempt}", WindowMs, Limit);
        }

        // then: K10001 и K10002 допущены в ОДНУ overflow-корзину: метки корзины
        // общие — K10001, K10002 и первые 3 новых ключа (5 меток), шестой
        // суммарный запрос корзины за окно отклонён.
        Assert.True(k10001, "Первый ключ сверх потолка должен обслуживаться overflow-корзиной (корзина пуста).");
        Assert.True(k10002, "Второй ключ сверх потолка должен обслуживаться той же overflow-корзиной.");
        Assert.True(extra[0] && extra[1] && extra[2], "Запросы 3–5 корзины должны допускаться (меток корзины < limit 5).");
        Assert.False(extra[3], "Шестой суммарный запрос корзины за окно должен быть отклонён.");
        Assert.Equal(5, B09LimiterInspection.OverflowMarksCount(engine));

        // then: новые ключи НЕ получили индивидуальных слотов; размер словаря
        // политики ≤10001 (10000 индивидуальных + корзина).
        Assert.Equal(SlidingWindowLimiter.MaxTrackedKeys, B09LimiterInspection.IndividualKeysCount(engine));
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Число ключей политики (с корзиной) должно быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1}, фактически {engine.TrackedKeysCount}.");
    }
}
