using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-193 «PUT /me/profile: невалидный формат email → 400 со словарным текстом»
/// (negative, FR-021, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); клиент
///        анонимный (аутентификации в приложении нет); fullName валиден.
/// when:  PUT /api/v1/me/profile {fullName:'Имя', email:'nope'} (нет @ и домена).
/// then:  400 «Данные заполнены неверно»; errors.email=['Введите корректный email'];
///        email пользователя в хранилище не изменился (остался teacher@example.com).
///        FR-021 description: «валидация (fullName 1–200, email формат и ≤254;
///        словарь ошибок…) → 400»; «Словарь ошибок валидации».
/// </summary>
public sealed class Ts193_ProfileInvalidEmailFormatTests : IClassFixture<B15WebAppFactory>
{
    private const string OldEmail = "teacher@example.com";
    private const string InvalidEmail = "nope";

    private readonly B15WebAppFactory _factory;

    public Ts193_ProfileInvalidEmailFormatTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithInvalidEmailFormat_Returns400AndKeepsStoredEmail()
    {
        // given: текущий пользователь — засеянный преподаватель; клиент анонимный.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(OldEmail, current.Email);
        using var client = B15Harness.CreateClient(_factory);

        // when: email без @ и домена, fullName валиден.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = InvalidEmail,
        });

        // then: 400 с дословным message и словарной ошибкой поля email.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.MessageIs(envelope, "Данные заполнены неверно");
        BodyAssertions.ErrorFieldEquals(envelope, "email", "Введите корректный email");

        // then: email пользователя в хранилище не изменился.
        var stored = await B15Harness.UserByIdAsync(_factory, current.Id);
        Assert.Equal(OldEmail, stored.Email);
    }
}
