using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-120 «Порядок: 429 раньше валидации тела регистрации» (негативный, FR-024, FR-007).
///
/// given: свежий экземпляр (собственная фикстура класса — счётчик регистраций пуст);
///        с IP X за последний час выполнено 5 запросов POST /api/v1/auth/register
///        (любых). Все запросы теста идут через TestServer с одним и тем же
///        RemoteIpAddress — условие «с того же IP» выполняется.
/// when:  6-й запрос с того же IP с заведомо невалидным телом {fullName:''}
///        (остальные поля отсутствуют).
/// then:  HTTP 429 {message:'Слишком много попыток. Повторите позже'} (не 400):
///        проверка лимита выполняется после сессии/роли и ДО валидации тела.
///        FR-024 AC «429 раньше валидации тела».
/// </summary>
public sealed class Ts120_RateLimitBeforeBodyValidationTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts120_RateLimitBeforeBodyValidationTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SixthRegisterWithInvalidBody_Returns429Not400()
    {
        // given: 5 любых запросов регистрации с IP X (свежий экземпляр — счётчик пуст).
        using var client = HostClients.Create(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(RegisterEndpoint, new
            {
                fullName = $"Лимит Регистраций {attempt:00}",
                login = $"ts120user{attempt:00}",
                email = $"ts120user{attempt:00}@example.com",
                password = "Passw0rd!",
                repeatPassword = "Passw0rd!",
            });
            Assert.True(
                (int)response.StatusCode >= 200 && (int)response.StatusCode < 500,
                $"Предусловие кейса: попытка {attempt} не должна прерываться 5xx, фактически {response.StatusCode}.");
        }

        // when: 6-й запрос с того же IP с заведомо невалидным телом {fullName:''}.
        using var sixth = await client.PostAsJsonAsync(RegisterEndpoint, new { fullName = "" });

        // then: HTTP 429 «Слишком много попыток. Повторите позже» (не 400).
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(root, "Слишком много попыток. Повторите позже");
    }
}
