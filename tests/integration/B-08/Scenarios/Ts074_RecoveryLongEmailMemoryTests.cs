using LabsApp.Auth.RateLimiting;
using LabsApp.IntegrationTests.B08.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-074 «Сверхдлинный email не растит память лимитера» (boundary,
/// FR-017 + NFR-004, P1).
///
/// given: словарь лимитера запросов кода пуст (свежий хост фикстуры);
///        MaxTrackedKeys=10000 (явно, B08LimiterWebAppFactory).
/// when:  10 запросов recovery/request с email длиной 100000 символов;
///        инспекция словаря лимитера.
/// then:  все ответы 200/429 (после исчерпания лимита ключа — 429); ключ
///        лимитера усечён до 254 симв.; число записей словаря не растёт
///        (остаётся 1). FR-017 AC «Сверхдлинный email не растит память»;
///        NFR-004.
///
/// Наблюдаемость «усечён до 254»: SlidingWindowLimiter публикует только
/// TrackedKeysCount (IF-006), поэтому усечение проверяется дифференциально
/// НЕМУТИРУЮЩИМ запросом RetryAfterSeconds для второго 100000-символьного
/// email с тем же 254-символьным префиксом: non-null ⟺ ключ совпал ⟺
/// усечение до 254 выполнено. Счёт записей не меняется (инспекция после).
/// </summary>
public sealed class Ts074_RecoveryLongEmailMemoryTests : IClassFixture<B08LimiterWebAppFactory>
{
    private const int LongEmailLength = 100_000;
    private const int RequestCount = 10;

    private readonly B08LimiterWebAppFactory _factory;

    public Ts074_RecoveryLongEmailMemoryTests(B08LimiterWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SuperlongEmail_DoesNotGrowLimiterDictionary()
    {
        // given: словарь лимитера запросов кода пуст; MaxTrackedKeys=10000.
        using var client = B08Host.CreateClient(_factory);
        var limiter = _factory.Services.GetRequiredService<RecoveryRequestLimiter>();
        Assert.Equal(0, limiter.TrackedKeysCount);

        // when: 10 запросов recovery/request с email длиной 100000 символов
        //       (адрес не зарегистрирован — ветка «всегда 200» FR-017).
        var longEmail = BuildLongEmail();
        var requestBody = System.Text.Json.JsonSerializer.Serialize(new { email = longEmail });
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= RequestCount; attempt++)
        {
            using var response = await client.PostAsync(B08Host.RecoveryRequestEndpoint, requestBody);
            statuses.Add(response.StatusCode);
        }

        // then: все ответы 200/429; после исчерпания лимита ключа (3 в окне) — 429.
        Assert.All(statuses, status =>
            Assert.True(
                status is HttpStatusCode.OK or HttpStatusCode.TooManyRequests,
                $"Ожидался статус 200/429, фактически {(int)status}."));
        Assert.True(
            statuses.Count(status => status == HttpStatusCode.OK) == RecoveryRequestLimiter.RequestLimit
            && statuses.Count(status => status == HttpStatusCode.TooManyRequests)
                == RequestCount - RecoveryRequestLimiter.RequestLimit,
            $"Ожидались первые {RecoveryRequestLimiter.RequestLimit} допуска (200) и остальные 429; фактически: [{string.Join(", ", statuses.Select(s => (int)s))}].");

        // then: число записей словаря не растёт (остаётся 1).
        Assert.Equal(1, limiter.TrackedKeysCount);

        // then: ключ лимитера усечён до 254 симв. (дифференциальная проверка).
        var samePrefixEmail = BuildLongEmail(differentTail: true);
        var retryAfter = limiter.RetryAfterSeconds(samePrefixEmail);
        Assert.True(
            retryAfter is not null,
            "Второй email с тем же 254-символьным префиксом не попал на тот же ключ лимитера — "
            + "усечение ключа до 254 символов не выполняется (FR-017 AC, NFR-004).");
        Assert.Equal(1, limiter.TrackedKeysCount);
    }

    /// <summary>Email ровно 100000 символов; differentTail — последний символ отличен (префикс 254 тот же).</summary>
    private static string BuildLongEmail(bool differentTail = false)
    {
        const string domain = "@lab.local";
        var localLength = LongEmailLength - domain.Length;
        var tail = differentTail ? 'z' : 'a';
        var local = new string('a', localLength - 1) + tail;
        return local + domain;
    }
}
