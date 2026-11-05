using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-020 «NFR-003: память лимитера ограничена при 50000 уникальных ключей»
/// (boundary, NFR-003 + FR-003, P1).
///
/// given: одна политика движка (window=60000, limit=5); инжектируемые часы.
/// when:  последовательные TryAcquire по 50000 новым ключам, затем проверка
///        размера хранилища политики.
/// then:  размер словаря политики ≤10001 (MaxTrackedKeys + overflow-корзина),
///        независимо от числа запросов (NFR-003).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// зоны с совпадающим поведением (Ts179_Nfr003LimiterMemoryCeiling) не
/// изменялся.
/// </summary>
public sealed class Ts020_Nfr003FiftyThousandKeysCeilingTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const int UniqueKeysCount = 50_000;
    private const long T0 = 17_000_000;

    [Fact]
    public void FiftyThousandUniqueKeys_PolicyDictionaryStaysWithinCeiling()
    {
        // given: одна политика движка (window=60000, limit=5); инжектируемые часы.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);

        // when: последовательные TryAcquire по 50000 новым ключам (метки в одном
        // текущем окне).
        for (var index = 1; index <= UniqueKeysCount; index++)
        {
            _ = engine.TryAcquire($"ts020-key-{index}", WindowMs, Limit);
        }

        // then: размер словаря политики ≤10001 (10000 индивидуальных ключей +
        // overflow-корзина) независимо от числа запросов.
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Размер словаря политики обязан быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1} " +
            $"(MaxTrackedKeys + overflow-корзина), фактически: {engine.TrackedKeysCount}.");
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B09LimiterInspection.IndividualKeysCount(engine));
    }
}
