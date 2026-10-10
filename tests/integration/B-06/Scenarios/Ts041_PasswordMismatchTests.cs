using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-041 «Несовпадение повтора пароля» (negative, P0, FR-012 AC «Несовпадение повтора»).
///
/// given: RemoteIpAddress=10.0.0.41; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан).
/// when:  регистрация с repeatPassword ≠ password (прочее валидно).
/// then:  400; errors.repeatPassword=['Пароли не совпадают'].
/// </summary>
public sealed class Ts041_PasswordMismatchTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts041_PasswordMismatchTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithMismatchedRepeatPassword_Returns400WithExactRepeatError()
    {
        // given: RemoteIpAddress=10.0.0.41, прочие поля валидны.
        using var client = HostClients.Create(_factory);

        // when: регистрация с repeatPassword ≠ password.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.41",
            fullName: "Несовпадение Повтора",
            login: "ts041.user",
            email: "ts041@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!X");

        // then: 400; errors.repeatPassword = ['Пароли не совпадают'] — ровно один элемент.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message", "errors");
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldIsExactly(root, "repeatPassword", "Пароли не совпадают");
    }
}
