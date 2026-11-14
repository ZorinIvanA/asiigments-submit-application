using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-179 «NFR-003: память лимитера ≤10001 ключа при 50000 уникальных»
/// (nfr, NFR-003 + FR-003, P1).
///
/// given: движок лимитера, одна политика (window=60000, limit=5);
///        инжектируемые часы.
/// when:  50000 запросов TryAcquire с уникальными ключами.
/// then:  размер словаря политики ≤10001 (MaxTrackedKeys + overflow); метки
///        вычищаются при каждой проверке (NFR-003 verification: «50000
///        уникальных ключей → размер словаря политики ≤10001»).
///
/// Чистка проверяется переводом часов за границу окна: очередная проверка
/// обязана вычистить все устаревшие метки, удалить опустевшие ключи и
/// освободить индивидуальные слоты. Файл текущей волны батча B-09.
/// </summary>
public sealed class Ts179_Nfr003LimiterMemoryCeilingTests
{
    private const long WindowMs = 60_000;
    private const int Limit = 5;
    private const int UniqueKeysCount = 50_000;

    [Fact]
    public void FiftyThousandUniqueTryAcquires_PolicyHoldsAtMostMaxTrackedKeysPlusOverflow()
    {
        // given: движок лимитера, одна политика (window=60000, limit=5);
        // инжектируемые часы.
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(9_000_000));
        var engine = new SlidingWindowLimiter(time);

        // when: 50000 запросов TryAcquire с уникальными ключами.
        for (var index = 1; index <= UniqueKeysCount; index++)
        {
            _ = engine.TryAcquire($"ts179-key-{index}", WindowMs, Limit);
        }

        // then: размер словаря политики ≤10001 (10000 индивидуальных ключей +
        // overflow-корзина) независимо от 50000 уникальных ключей.
        Assert.True(
            engine.TrackedKeysCount <= SlidingWindowLimiter.MaxTrackedKeys + 1,
            $"Размер словаря политики обязан быть ≤{SlidingWindowLimiter.MaxTrackedKeys + 1} " +
            $"(MaxTrackedKeys + overflow), фактически: {engine.TrackedKeysCount}.");
        Assert.Equal(
            SlidingWindowLimiter.MaxTrackedKeys,
            B09LimiterInspection.IndividualKeysCount(engine));

        // then: метки вычищаются при каждой проверке — часы за границей окна,
        // очередная проверка вычищает устаревшие метки и опустевшие ключи
        // (индивидуальные слоты освобождаются, корзина опустевает).
        time.Advance(TimeSpan.FromMilliseconds(WindowMs + 1));
        Assert.True(
            engine.TryAcquire("ts179-key-after-window", WindowMs, Limit),
            "После вычистки устаревших меток новый ключ обязан получить индивидуальный слот.");
        Assert.Equal(1, engine.TrackedKeysCount);
        Assert.Equal(0, B09LimiterInspection.OverflowMarksCount(engine));
    }
}
