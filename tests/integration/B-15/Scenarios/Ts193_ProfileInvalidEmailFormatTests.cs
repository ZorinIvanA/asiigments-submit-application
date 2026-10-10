using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-193 «PUT /me/profile: невалидный формат email → 400 со словарным текстом»
/// (negative, FR-021, P1).
///
/// given: пользователь role=student с email old@x.ru создан прямым DI-сидом
///        в IUserRepository тестового хоста (NFR-001: публичный API для сида
///        не используется); сессия — cookie access_token с JWT HS256, минтым
///        харнесом ключом Auth__JwtKey тестового хоста; POST /auth/register и
///        POST /auth/login в предусловиях НЕ вызываются (CR-001, арбитраж a-017);
///        fullName валиден.
/// when:  PUT /api/v1/me/profile {fullName:'Имя', email:'nope'} (нет @ и домена).
/// then:  400 «Данные заполнены неверно»; errors.email=['Введите корректный email'];
///        email пользователя в хранилище не изменился (остался old@x.ru).
///        FR-021 description: «валидация (fullName 1–200, email формат и ≤254;
///        словарь ошибок…) → 400»; «Словарь ошибок валидации».
/// </summary>
public sealed class Ts193_ProfileInvalidEmailFormatTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts193";
    private const string OldEmail = "old@x.ru";
    private const string InvalidEmail = "nope";

    private readonly B15WebAppFactory _factory;

    public Ts193_ProfileInvalidEmailFormatTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithInvalidEmailFormat_Returns400AndKeepsStoredEmail()
    {
        // given: студент old@x.ru (DI-сид) и сессия.
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: Login, email: OldEmail);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

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
        var stored = B15Harness.UserById(_factory, seeded.Id);
        Assert.Equal(OldEmail, stored.Email);
    }
}
