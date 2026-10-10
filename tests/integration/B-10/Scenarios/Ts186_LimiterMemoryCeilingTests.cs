using System.Globalization;
using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-186 «NFR-003: память лимитера ограничена 10001 ключом» (nfr, NFR-003/FR-003, P0).
///
/// given: чистая политика с окном и лимитом (собственный движок IF-006 — свой
///        словарь и своя overflow-корзина); инжектированные часы фиксированы
///        (одна и та же метка времени — окно не истекает, вычистки нет).
/// when:  50000 запросов TryAcquire с уникальными ключами (решающее правило
///        движка — TryAcquire(key, windowMs, limit); потолок MaxTrackedKeys —
///        константа движка, конструктор принимает только часы).
/// then:  Размер словаря политики ≤10001 (10000 индивидуальных + '__overflow__':
///        публичный шов TrackedKeysCount ≤ 10001; инспекция — ровно 10000
///        индивидуальных, корзина непуста); исключений нет (NFR-003 verification:
///        unit-тест 50000 ключей).
/// </summary>
public sealed class Ts186_LimiterMemoryCeilingTests
{
    /// <summary>Потолок индивидуальных ключей на политику (константа, FR-003/ADR-005).</summary>
    private const int MaxTrackedKeys = 10_000;

    /// <summary>Число запросов верификации NFR-003 (50000 уникальных ключей).</summary>
    private const int TotalRequests = 50_000;

    private const long WindowMs = 60_000;
    private const int Limit = 5;

    [Fact]
    public void FiftyThousandUniqueKeys_KeepPolicyDictionaryAtMostTenThousandAndOne()
    {
        // given: чистая политика с окном и лимитом; часы зафиксированы.
        var engine = new SlidingWindowLimiter(new FixedTimeProvider());

        // when: 50000 запросов TryAcquire с уникальными ключами — исключений нет.
        try
        {
            for (var attempt = 0; attempt < TotalRequests; attempt++)
            {
                _ = engine.TryAcquire(
                    string.Create(CultureInfo.InvariantCulture, $"ts186-key-{attempt}"),
                    WindowMs,
                    Limit);
            }
        }
        catch (Exception exception)
        {
            Assert.Fail(
                "Движок лимитера бросил исключение на потоке из 50000 уникальных ключей: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        // then: размер словаря политики ≤10001 (10000 индивидуальных + '__overflow__').
        Assert.True(
            engine.TrackedKeysCount <= MaxTrackedKeys + 1,
            $"Размер словаря политики должен быть ≤ {MaxTrackedKeys + 1} " +
            $"(потолок {MaxTrackedKeys} + overflow-корзина), фактически {engine.TrackedKeysCount}.");
        Assert.Equal(
            MaxTrackedKeys,
            B10LimiterInspection.IndividualKeysCount(engine));
        Assert.True(
            B10LimiterInspection.OverflowMarksCount(engine) > 0,
            "Overflow-корзина '__overflow__' пуста: запросы сверх потолка не обслужены корзиной.");
    }

    /// <summary>Инжектируемые часы, зафиксированные на одной метке (окно не истекает).</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => FixedNow;
    }
}
