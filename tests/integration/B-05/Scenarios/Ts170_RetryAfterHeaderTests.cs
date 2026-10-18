using System.Globalization;
using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-170 (P2, nfr; FR-080) «429 содержит Retry-After (SHOULD)».
///
/// given: исчерпан лимит неудач входа (5 неуспешных входов подряд).
/// when:  6-я неуспешная попытка.
/// then:  429; если заголовок Retry-After присутствует — его значение целое
///        число секунд > 0. FR-080: «SHOULD добавляться заголовок Retry-After
///        (секунды до освобождения окна)» — мягкая проверка (SHOULD).
/// </summary>
public sealed class Ts170_RetryAfterHeaderTests(B05SecurityWebAppFactory factory)
    : IClassFixture<B05SecurityWebAppFactory>
{
    private readonly B05SecurityWebAppFactory _factory = factory;

    [Fact]
    public async Task TS170_RateLimitedLogin_429WithOptionalRetryAfterAsPositiveSeconds()
    {
        // given: 5 неуспешных входов подряд (неизвестный логин → по 401).
        using var client = B05SecurityClients.CreateClient(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await B05SecurityClients.PostLoginAsync(
                client, "ts170-unknown-user", "ts170-wrong-password!");
            Assert.True(
                failed.StatusCode == HttpStatusCode.Unauthorized,
                $"Предусловие кейса: неуспешный вход {attempt} должен дать 401, " +
                $"фактически {(int)failed.StatusCode}: {await failed.Content.ReadAsStringAsync()}");
        }

        // when: 6-я неуспешная попытка.
        using var sixth = await B05SecurityClients.PostLoginAsync(
            client, "ts170-unknown-user", "ts170-wrong-password!");

        // then: 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);

        // then (SHOULD): если Retry-After присутствует — целое число секунд > 0.
        if (sixth.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            Assert.True(
                raw is not null,
                "Заголовок Retry-After присутствует, но пуст.");
            Assert.True(
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0,
                $"Retry-After, если присутствует, обязан быть целым числом секунд > 0, фактически «{raw}».");
        }
    }
}
