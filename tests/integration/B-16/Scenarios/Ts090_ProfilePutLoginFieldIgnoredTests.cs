using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-090 «Профиль: поле login во входе игнорируется» (negative, FR-015, P1;
/// нумерация текущего батча B-16).
///
/// given: пользователь 'victim' авторизован; email свободен (свежая фикстура —
///        пустое хранилище, кроме сид-преподавателя).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'} (лишнее поле).
/// then:  200; login пользователя остался 'victim' — поле login во входе
///        игнорируется (FR-015: «поле login во входе игнорируется»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts090_ProfilePutLoginFieldIgnoredTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "victim";
    private const string Email = "victim@example.com";
    private const string IgnoredLoginField = "hacker";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B16WebAppFactory _factory;

    public Ts090_ProfilePutLoginFieldIgnoredTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithExtraLoginField_Returns200_AndKeepsVictimLogin()
    {
        // given: пользователь 'victim' авторизован; email 'new@example.com' свободен.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Жертва Атаки Игнорирования",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с лишним полем login:'hacker'.
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IgnoredLoginField,
        });

        // then: 200; login пользователя остался 'victim' — поле во входе
        // игнорируется (и в ответе, и в хранилище).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", Login);

        var stored = B16Harness.StoredUser(_factory, user.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}» (поле login во входе должно игнорироваться).");
        Assert.False(
            string.Equals(stored.Login, IgnoredLoginField, StringComparison.Ordinal),
            "login пользователя не должен перезаписываться значением лишнего поля входа.");
    }
}
