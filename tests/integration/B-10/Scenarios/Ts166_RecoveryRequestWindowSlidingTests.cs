using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-166 «Матрица recovery_request: часовое окно 3600с доказано временем
/// (скольжение)» (boundary, FR-004, FR-012, P1).
///
/// given: хост Development с инжектируемыми часами (B10TimedWebAppFactory —
/// FakeTimeProvider, FR-003/ADR-002); часы на T0 (стартовое время фиктивных
/// часов); на email 'cycle@example.com' (существование учётной записи не
/// значимо для ключа lower(email) лимитера recovery_request) выполнены
/// 3 POST /auth/recovery/request — все 200 с пустым телом.
/// when:  4-й запрос на тот же email при часах T0 (контрольная точка); затем
///        часы переведены на T0+3600001 мс и выполнен 5-й запрос на тот же
///        email.
/// then:  4-й — 429 'Слишком много попыток. Повторите позже'; 5-й (после
///        перевода) — 200 с пустым телом (Content-Length: 0), НЕ 429 — окно
///        3600 с (3/час) доказано временем; при окне 600 с или 86400 с исход
///        пары отличался бы (FR-004 матрица recovery_request: ключ
///        lower(trim(email)), окно 3600000 мс, лимит 3; отказ метку НЕ
///        дописывает — IF-006).
///
/// Примечание размещения: given кейса называет зоной-владельцем
/// tests/integration/B-07, тогда как вход батча B-10 привязывает кейс к
/// test_zone tests/integration/B-10 — файл размещён по test_zone батча
/// (расхождение упоминания зоны передано в scenario_change_requests).
/// </summary>
public sealed class Ts166_RecoveryRequestWindowSlidingTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string CycledEmail = "cycle@example.com";

    /// <summary>Эндпойнт кейса: POST /api/v1/auth/recovery/request (FR-012).</summary>
    private const string RecoveryRequestEndpoint = "/api/v1/auth/recovery/request";

    /// <summary>Перевод часов кейса: T0 + 3600001 мс (окно 3600 с + 1 мс).</summary>
    private static readonly TimeSpan WindowPlusOneMs = TimeSpan.FromMilliseconds(3_600_001);

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS166_FourthRequestAtT0_Is429_FifthAfterT0Plus3600001ms_Is200EmptyBody()
    {
        // given: часы на T0 (стартовое время FakeTimeProvider; время движется
        // ТОЛЬКО явно — Advance); квота ключа 'cycle@example.com' исчерпана —
        // 3 запроса, каждый 200 с пустым телом (оракула существования нет).
        _ = _factory.Time.GetUtcNow();
        using var client = B10AuthRequests.Create(_factory);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var seeded = await client.PostAsJsonAsync(
                RecoveryRequestEndpoint, new { email = CycledEmail });
            await AssertEmptyOkAsync(seeded, $"TS-166: given-запрос recovery/request #{attempt + 1}");
        }

        // when: 4-й запрос на тот же email при часах T0 (контрольная точка —
        // без перевода часов).
        using var fourth = await client.PostAsJsonAsync(
            RecoveryRequestEndpoint, new { email = CycledEmail });

        // then: 4-й — 429 с дословным сообщением (матрица FR-004: 3/3600с).
        await ApiAssert.AssertMessageAsync(
            fourth,
            HttpStatusCode.TooManyRequests,
            "Слишком много попыток. Повторите позже");

        // when: часы переведены на T0+3600001 мс — метки 3 запросов возраста
        // windowMs+1 мс вне скользящего окна 3600000 мс; выполнен 5-й запрос
        // на тот же email.
        _factory.Time.Advance(WindowPlusOneMs);
        using var fifth = await client.PostAsJsonAsync(
            RecoveryRequestEndpoint, new { email = CycledEmail });

        // then: 5-й — 200 с пустым телом (Content-Length: 0), НЕ 429: окно
        // 3600 с доказано временем (при окне 600 с метки давно бы истекли и
        // 4-й не был бы 429; при 86400 с 5-й остался бы 429 — исход пары
        // отличался бы в обоих случаях).
        await AssertEmptyOkAsync(fifth, "TS-166: 5-й recovery/request после перевода часов");
    }

    /// <summary>then-проверка «200 с ПУСТЫМ телом» (FR-012/ISS-014: 0 байт, НЕ '{}').</summary>
    private static async Task AssertEmptyOkAsync(HttpResponseMessage response, string context)
    {
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"{context}: ожидался 200, фактически {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());
        Assert.True(
            response.Content.Headers.ContentLength == 0,
            $"{context}: ожидалось пустое тело (Content-Length: 0), фактически " +
            $"Content-Length: {response.Content.Headers.ContentLength?.ToString() ?? "отсутствует"}.");
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }
}
