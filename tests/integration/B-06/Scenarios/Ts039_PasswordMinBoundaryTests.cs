using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-039 «Пароль 7 символов: нижняя граница» (boundary, P0,
/// FR-012 AC «Неверный пароль (граница)»).
///
/// given: RemoteIpAddress не исчерпавшая лимит (10.0.0.39, свежая фикстура);
///        прочие поля валидны.
/// when:  POST /auth/register с password='Passw0r' (7 симв.).
/// then:  400 «Данные заполнены неверно»; errors.password содержит
///        «Пароль должен содержать не менее 8 символов».
/// </summary>
public sealed class Ts039_PasswordMinBoundaryTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts039_PasswordMinBoundaryTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithSevenCharacterPassword_Returns400WithMinLengthError()
    {
        // given: RemoteIpAddress=10.0.0.39, прочие поля валидны.
        using var client = HostClients.Create(_factory);

        // when: регистрация с password из 7 символов.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.39",
            fullName: "Короткий Пароль",
            login: "ts039.user",
            email: "ts039@example.com",
            password: "Passw0r",
            repeatPassword: "Passw0r");

        // then: 400 «Данные заполнены неверно», errors.password содержит
        // «Пароль должен содержать не менее 8 символов» (пароль не триммится,
        // правило нижней границы — словарь ошибок §8).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldContains(root, "password", "Пароль должен содержать не менее 8 символов");
    }
}
