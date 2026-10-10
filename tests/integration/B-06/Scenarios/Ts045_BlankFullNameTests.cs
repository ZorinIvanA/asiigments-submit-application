using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-045 «Пробельное ФИО: required-ошибка» (negative, P0,
/// FR-012 AC «Пустые и пробельные поля»).
///
/// given: RemoteIpAddress=10.0.0.45; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан).
/// when:  регистрация с fullName='   ' (прочее валидно).
/// then:  400; errors.fullName=['Заполните поле'].
/// </summary>
public sealed class Ts045_BlankFullNameTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts045_BlankFullNameTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithWhitespaceFullName_Returns400WithRequiredError()
    {
        // given: RemoteIpAddress=10.0.0.45, прочие поля валидны.
        using var client = HostClients.Create(_factory);

        // when: регистрация с fullName из одних пробелов.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.45",
            fullName: "   ",
            login: "ts045.user",
            email: "ts045@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 400; errors.fullName=['Заполните поле'] — ровно один элемент.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "fullName", "Заполните поле");
    }
}
