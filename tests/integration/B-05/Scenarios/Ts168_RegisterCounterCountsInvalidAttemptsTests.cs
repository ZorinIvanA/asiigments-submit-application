using LabsApp.IntegrationTests.B05.Infrastructure;

namespace LabsApp.IntegrationTests.B05.Scenarios;

/// <summary>
/// TS-168 «Счётчик регистраций включает попытки, отброшенные валидацией»
/// (негативный, FR-007, FR-024).
///
/// given: свежий экземпляр (собственная фикстура класса); с IP X (TestServer:
///        один и тот же RemoteIpAddress для всех запросов) выполнено 5 запросов
///        POST /api/v1/auth/register, КАЖДЫЙ с невалидным телом {fullName:''}
///        (остальные поля отсутствуют) — все 5 ответили HTTP 400 «Данные
///        заполнены неверно» (ни одной прошедшей регистрации).
/// when:  6-й POST /api/v1/auth/register с IP X с полностью валидным телом
///        (свободные login/email, пароль по §8).
/// then:  HTTP 429 {message:'Слишком много попыток. Повторите позже'} (не 201):
///        попытки, отброшенные валидацией, инкрементируют счётчик наравне с
///        успешными. FR-024: «счётчики register (5/час на IP) и recovery/request
///        (3/час на ci-email) инкрементируются всеми попытками, включая
///        отброшенные валидацией»; FR-007: «окно скользящее, успешные и
///        неуспешные попытки считаются».
/// </summary>
public sealed class Ts168_RegisterCounterCountsInvalidAttemptsTests : IClassFixture<B05WebAppFactory>
{
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private readonly B05WebAppFactory _factory;

    public Ts168_RegisterCounterCountsInvalidAttemptsTests(B05WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ValidSixthRegisterAfterFiveInvalid_Returns429Not201()
    {
        // given: 5 запросов с невалидным телом — каждый отвечает 400 (проверка
        // предусловия кейса: ни одна попытка не проходит и не даёт иной ошибки).
        using var client = HostClients.Create(_factory);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(RegisterEndpoint, new { fullName = "" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var root = await BodyAssertions.ReadRootObjectAsync(response);
            BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        }

        // when: 6-й запрос с полностью валидным телом (свободные login/email, §8-пароль).
        using var sixth = await client.PostAsJsonAsync(RegisterEndpoint, new
        {
            fullName = "Счётчик Валидных Попыток",
            login = "counteruser",
            email = "counteruser@example.com",
            password = "Passw0rd!",
            repeatPassword = "Passw0rd!",
        });

        // then: HTTP 429 (не 201) с дословным текстом словаря.
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, sixth.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(sixth);
        BodyAssertions.MessageIs(body, "Слишком много попыток. Повторите позже");
    }
}
