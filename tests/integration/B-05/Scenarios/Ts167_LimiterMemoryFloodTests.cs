using LabsApp.Auth.RateLimiting;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B05.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-167 (P1, nfr; FR-080, NFR-004) «Флуд 100000 уникальных ключей: память
/// лимитера ограничена».
///
/// Форма сценария — прикладная (NFR-004: «Unit-тест: флуд уникальными ключами +
/// инспекция размера внутреннего хранилища лимитера»; тот же класс причин, что
/// и для TS-166 — ADR-027/SI-001: эндпоинт-форма флуда стоила бы ~10003 KDF).
///
/// given: MaxTrackedKeys=10000; словарь пуст; все адреса форматно-валидны и
///        незарегистрированы (демо-сид выключен, адреса уникальны).
/// when:  100000 попыток recovery/request с уникальными email (по одному на
///        ключ); периодическая инспекция словаря.
/// then:  после заполнения корзины все новые ключи → отказ (в эндпоинт-форме
///        429); суммарное число отслеживаемых записей словаря в любой момент
///        ≤ MaxTrackedKeys+1 = 10001. NFR-004: «при 100000 уникальных ключей
///        … ≤ MaxTrackedKeys+1 (10001)».
/// </summary>
public sealed class Ts167_LimiterMemoryFloodTests(B05CeilingWebAppFactory factory)
    : IClassFixture<B05CeilingWebAppFactory>
{
    /// <summary>Число уникальных ключей флуда given кейса.</summary>
    private const int FloodKeys = 100_000;

    /// <summary>Период инспекции словаря (каждые 10000 попыток).</summary>
    private const int InspectionPeriod = 10_000;

    private readonly B05CeilingWebAppFactory _factory = factory;

    [Fact]
    public void TS167_FloodOfUniqueKeys_DictionaryNeverExceedsMaxTrackedKeysPlusBucket()
    {
        // given: MaxTrackedKeys=10000; словарь пуст.
        var maxTrackedKeys = _factory.Services
            .GetRequiredService<IOptions<RateLimitsOptions>>().Value.MaxTrackedKeys;
        Assert.Equal(10000, maxTrackedKeys);
        var limiter = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();
        Assert.Equal(0, limiter.TrackedKeysCount);

        // when: 100000 попыток с уникальными email (по одному на ключ);
        //       периодическая инспекция словаря.
        var admitted = 0;
        var bucketFilled = false;
        for (var index = 0; index < FloodKeys; index++)
        {
            var admittedNow = limiter.TryConsume(FloodEmail(index));
            if (admittedNow)
            {
                admitted++;
                // then: после заполнения корзины новые ключи не допускаются.
                Assert.False(
                    bucketFilled,
                    $"Попытка {index + 1} допущена после заполнения overflow-корзины — лимит обойдён новыми ключами.");
            }
            else
            {
                bucketFilled = true;
            }

            if ((index + 1) % InspectionPeriod == 0)
            {
                Assert.True(
                    limiter.TrackedKeysCount <= maxTrackedKeys + 1,
                    $"После {index + 1} попыток словарь обязан быть ≤ {maxTrackedKeys + 1}, " +
                    $"фактически {limiter.TrackedKeysCount}.");
            }
        }

        // then: суммарный размер словаря ≤ MaxTrackedKeys+1 в любой момент (в т.ч.
        //       в конце); после заполнения корзины все новые ключи отклонены.
        Assert.True(
            limiter.TrackedKeysCount <= maxTrackedKeys + 1,
            $"Итоговый размер словаря обязан быть ≤ {maxTrackedKeys + 1}, фактически {limiter.TrackedKeysCount}.");
        Assert.True(
            bucketFilled,
            "Флуд 100000 уникальных ключей обязан заполнить overflow-корзину (появиться отказы 429).");
        Assert.Equal(
            maxTrackedKeys + RecoveryRequestLimiter.RequestLimit,
            admitted);
    }

    /// <summary>Уникальный форматно-валидный незарегистрированный адрес ключа флуда.</summary>
    private static string FloodEmail(int index) => $"ts167-{index:D6}@example.invalid";
}
