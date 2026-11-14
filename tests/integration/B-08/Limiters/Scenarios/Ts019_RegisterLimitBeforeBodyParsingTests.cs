using System.Text;
using LabsApp.IntegrationTests.B08.Limiters.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Limiters.Scenarios;

/// <summary>
/// B-08/TS-019 «Register: лимит проверяется до разбора и валидации тела»
/// (negative, FR-004 + FR-006, P0). Все 6 запросов — с одного IP (один
/// тестовый клиент: RemoteIpAddress соединения TestServer постоянен).
///
/// given: с одного IP выполнены 5 запросов POST /api/v1/auth/register в
///        текущем окне (успешных, с уникальными валидными данными).
/// when:  6-й POST /auth/register с того же IP с заведомо невалидным телом
///        {fullName:''} (валидный JSON — без лимитера был бы 400 VALIDATION).
/// then:  429 RATE_LIMITED, message 'Слишком много попыток. Повторите позже';
///        ошибок полей (errors) в ответе нет — тело не разбирается и не
///        валидируется (AC FR-004 «Register: лимит до валидации»).
/// </summary>
public sealed class Ts019_RegisterLimitBeforeBodyParsingTests : IClassFixture<B08LimitersWebAppFactory>
{
    private const string RateLimitedMessage = "Слишком много попыток. Повторите позже";

    private readonly B08LimitersWebAppFactory _factory;

    public Ts019_RegisterLimitBeforeBodyParsingTests(B08LimitersWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_SixthRequestWithInvalidBody_429WithoutFieldErrors()
    {
        // given: 5 успешных регистраций (201) с одного IP — уникальные валидные
        // данные; регистрационный лимит ключа 'IP' исчерпан.
        using var client = B08LimitersClients.CreateClient(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var created = await client.PostAsJsonAsync(B08LimitersClients.RegisterEndpoint, new
            {
                fullName = $"Limit Before Body {attempt:D2}",
                login = $"b08ts019user{attempt:D2}",
                email = $"b08ts019user{attempt:D2}@example.com",
                password = B08LimitersClients.TestUserPassword,
                repeatPassword = B08LimitersClients.TestUserPassword,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // when: 6-й POST с того же IP с заведомо невалидным телом {fullName:''}
        // (валидный JSON: без исчерпанного лимита был бы 400 с errors.fullName).
        using var sixth = await client.PostAsync(
            B08LimitersClients.RegisterEndpoint,
            new StringContent("{\"fullName\":\"\"}", Encoding.UTF8, "application/json"));

        // then: 429 RATE_LIMITED 'Слишком много попыток. Повторите позже';
        // ключа errors в ответе нет — тело не разбирается и не валидируется.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var envelope = await B08LimitersBodyAssertions.ReadRootObjectAsync(sixth);
        B08LimitersBodyAssertions.MessageIs(envelope, RateLimitedMessage);
        Assert.False(
            envelope.TryGetProperty("errors", out _),
            "В ответе 429 нет ошибок полей (errors): тело не валидировалось");
    }
}
