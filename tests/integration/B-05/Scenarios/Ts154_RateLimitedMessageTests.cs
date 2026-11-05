using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-154 (триггер 429, выделенный класс с собственной фикстурой — счётчик
/// регистраций этого экземпляра не должен пересекаться с триггерами 400/409
/// регистрации общего класса Ts154_DictionaryMessageTests).
///
/// given: свежий экземпляр приложения (Development, демо-сид); счётчик регистраций
///        пуст.
/// when:  5 запросов POST /api/v1/auth/register (свободные login/email), затем
///        6-й запрос.
/// then:  HTTP 429, message «Слишком много попыток. Повторите позже» — дословно
///        (NFR-007, глоссарий «Словарь ошибок API (§8)»).
/// </summary>
public sealed class Ts154_RateLimitedMessageTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts154_RateLimitedMessageTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task TooManyAttempts_SixthRegister_ReturnsDictionaryMessage()
    {
        // given: 5 запросов регистрации с одного IP (свежий экземпляр).
        using var client = HostClients.Create(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(RegisterEndpoint, new
            {
                fullName = $"Словарь Лимит {attempt:00}",
                login = $"ts154rate{attempt:00}",
                email = $"ts154rate{attempt:00}@example.com",
                password = "Passw0rd!",
                repeatPassword = "Passw0rd!",
            });
            Assert.True(
                (int)response.StatusCode >= 200 && (int)response.StatusCode < 500,
                $"Предусловие кейса: попытка {attempt} не должна прерываться 5xx, фактически {response.StatusCode}.");
        }

        // when: 6-й запрос регистрации.
        using var sixth = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Словарь Лимит Шестой",
            login = "ts154rate06",
            email = "ts154rate06@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 429, message «Слишком много попыток. Повторите позже» дословно.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, "Слишком много попыток. Повторите позже");
    }
}
