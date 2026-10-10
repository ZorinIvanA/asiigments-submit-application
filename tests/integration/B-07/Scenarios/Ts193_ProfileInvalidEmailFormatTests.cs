using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-193 «PUT /me/profile: невалидный формат email → 400 со словарным текстом»
/// (negative, FR-021, P1).
///
/// given: сессия пользователя с email old@x.ru; fullName валиден (собственная
///        учётка регистрации).
/// when:  PUT /api/v1/me/profile {fullName:'Имя', email:'nope'} (нет @ и домена).
/// then:  400 «Данные заполнены неверно»; errors.email =
///        ['Введите корректный email'] (словарь ошибок валидации, дословно);
///        email пользователя в хранилище не изменился (остался old@x.ru).
///        FR-021 description: «валидация (fullName 1–200, email формат и ≤254;
///        словарь ошибок…) → 400».
/// </summary>
public sealed class Ts193_ProfileInvalidEmailFormatTests : IClassFixture<B07WebAppFactory>
{
    private const string Login = "ts193";
    private const string Email = "old@x.ru";

    private readonly B07WebAppFactory _factory;

    public Ts193_ProfileInvalidEmailFormatTests(B07WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_InvalidEmailFormat_Returns400AndKeepsStoredEmail()
    {
        // given: сессия пользователя с email old@x.ru.
        var (client, userId) = await HostClients.RegisterAndLoginStudentAsync(
            _factory, fullName: "Текущее Имя", login: Login, email: Email);

        // when: email без @ и домена.
        using var response = await client.PutAsJsonAsync(HostClients.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = "nope",
        });

        // then: 400 «Данные заполнены неверно», errors.email дословно из словаря.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(root, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(root, "email", "Введите корректный email");

        // then: email в хранилище не изменился — остался old@x.ru.
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetById(userId);
        Assert.NotNull(stored);
        Assert.Equal(Email, stored.Email);
    }
}
