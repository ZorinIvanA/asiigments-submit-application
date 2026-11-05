using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-047 «Недопустимый charset логина» (negative, FR-012, P1).
///
/// given: RemoteIpAddress=10.0.0.47; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан).
/// when:  регистрация с login='пользователь!' (кириллица и спецзнак).
/// then:  400; errors.login = ['Логин может содержать только латинские буквы,
///        цифры, точку, дефис и подчёркивание'] (словарь ошибок валидации).
/// </summary>
public sealed class Ts047_RegisterLoginCharsetTests : IClassFixture<B07AuthWebAppFactory>
{
    private const string ClientIp = "10.0.0.47";

    private readonly B07AuthWebAppFactory _factory;

    public Ts047_RegisterLoginCharsetTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task CyrillicAndSpecialCharLogin_RejectedWithDictionaryText()
    {
        // given: RemoteIpAddress=10.0.0.47; попыток регистрации с этого IP нет.
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);

        // when: регистрация с login='пользователь!' (кириллица и спецзнак).
        using var response = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
        {
            fullName = "Кириллица Логин",
            login = "пользователь!",
            email = "ts047@x.ru",
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
        });

        // then: 400; errors.login дословно из словаря ошибок валидации.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(
            root,
            "login",
            "Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание");
    }
}
