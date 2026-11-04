using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-091 «PUT /me/profile со своим email: 200, без конфликта» (idempotency, FR-021, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», email 'teacher@example.com'); других
///        пользователей с этим email нет (свежее хранилище фикстуры); клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /me/profile {fullName:'Имя', email:'teacher@example.com'}.
/// then:  200. FR-021 AC «Свой email»: «Свой неизменённый email конфликтом
///        не считается».
/// </summary>
public sealed class Ts091_ProfileOwnEmailNoConflictTests : IClassFixture<B15WebAppFactory>
{
    private const string Email = "teacher@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts091_ProfileOwnEmailNoConflictTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_WithOwnUnchangedEmail_ReturnsOkWithoutConflict()
    {
        // given: текущий пользователь — засеянный преподаватель с email
        // teacher@example.com; других пользователей с этим email нет; клиент анонимный.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(Email, current.Email);
        using var client = B15Harness.CreateClient(_factory);

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
