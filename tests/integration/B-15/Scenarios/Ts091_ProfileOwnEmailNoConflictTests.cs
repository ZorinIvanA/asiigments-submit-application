using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-091 «PUT /me/profile со своим email: 200, без конфликта» (idempotency, FR-021, P1).
///
/// given: пользователь role=student с email a@b.ru создан прямым DI-сидом
///        в IUserRepository тестового хоста; других пользователей с этим email нет;
///        сессия — cookie access_token с JWT HS256, минтым харнесом ключом
///        Auth__JwtKey тестового хоста; POST /auth/register и POST /auth/login
///        в предусловиях НЕ вызываются (CR-001, арбитраж a-017).
/// when:  PUT /me/profile {fullName:'Имя', email:'a@b.ru'}.
/// then:  200. FR-021 AC «Свой email»: «Свой неизменённый email конфликтом
///        не считается».
/// </summary>
public sealed class Ts091_ProfileOwnEmailNoConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "ts091";
    private const string Email = "a@b.ru";

    private readonly B15WebAppFactory _factory;

    public Ts091_ProfileOwnEmailNoConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithOwnUnchangedEmail_ReturnsOkWithoutConflict()
    {
        // given: студент с email a@b.ru (DI-сид); других пользователей с этим email нет
        // (свежее хранилище фикстуры); сессия — минтованный access-cookie.
        var seeded = B15Harness.SeedStudent(_factory, fullName: "Текущее Имя", login: Login, email: Email);
        using var client = B15Harness.CreateSessionClient(_factory, seeded.Id);

        // when: отправка своего текущего email без изменения.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = Email,
        });

        // then: 200 — свой email конфликтом не считается.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
