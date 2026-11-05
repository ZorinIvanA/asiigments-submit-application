using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-020 «NFR-003: память лимитера ограничена при 50000 уникальных ключей»
/// (boundary, NFR-003 + FR-003, P1).
///
/// given: одна политика движка (window=60000, limit=5); тест генерирует 50000
///        уникальных ключей с метками в текущем окне; инжектируемые часы.
/// when:  последовательные TryAcquire по 50000 новым ключам, затем проверка
///        размера хранилища политики.
/// then:  размер словаря политики ≤10001 (MaxTrackedKeys + overflow-корзина),
///        независимо от числа запросов (NFR-003).
///
/// Файл волны батча B-09 (кейс — закон; файлы прежних волн зоны с совпадающим
/// поведением не изменялись).
/// </summary>
public sealed class Ts020_LimiterStorageCeilingUnderFiftyThousandKeysTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const int UniqueKeysCount = 50_000;
    private const long T0 = 27_000_000;

    [Fact]
    public void FiftyThousandUniqueKeys_PolicyStorageStaysWithinCeiling()
    {
        // given: одна политика движка (window=60000, limit=5); инжектируемые
        // часы закреплены — все метки в одном текущем окне.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(T0));
        var engine = new SlidingWindowLimiter(time);

        // when: последовательные TryAcquire по 50000 новым ключам (допуск или
        // отказ корзины — результат попытки на потолок хранения не влияет).
        for (var index = 1; index <= UniqueKeysCount; index++)
        {
            _ = engine.TryAcquire($"ts020-wave-key-{index}", WindowMs, Limit);
        }

        // then: размер словаря политики ≤10001 (MaxTrackedKeys + overflow-
        // корзина) — независимо от числа запросов (NFR-003).
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Размер словаря политики обязан быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1} " +
            $"(MaxTrackedKeys + overflow-корзина), фактически: {engine.TrackedKeysCount}.");
    }
}
