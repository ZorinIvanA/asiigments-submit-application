using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-090 «Профиль: поле login во входе игнорируется» (negative, FR-015, P1;
/// нумерация текущего батча B-16).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher»); email 'new@example.com' свободен
///        (свежая фикстура — пустое хранилище, кроме сид-преподавателя); клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'} (лишнее поле).
/// then:  200; login пользователя остался 'teacher' — поле login во входе
///        игнорируется (FR-015: «поле login во входе игнорируется»).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts090_ProfilePutLoginFieldIgnoredTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "teacher";
    private const string IgnoredLoginField = "hacker";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";

    private readonly B16WebAppFactory _factory;

    public Ts090_ProfilePutLoginFieldIgnoredTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithExtraLoginField_Returns200_AndKeepsLogin()
    {
        // given: текущий пользователь — засеянный преподаватель; email
        // 'new@example.com' свободен; клиент анонимный.
        var current = await B16Harness.CurrentUserAsync(_factory);
        Assert.Equal(Login, current.Login);
        using var client = B16Harness.Create(_factory);

        // when: PUT с лишним полем login:'hacker'.
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IgnoredLoginField,
        });

        // then: 200; login пользователя остался 'teacher' — поле во входе
        // игнорируется (и в ответе, и в хранилище).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", Login);

        var stored = await B16Harness.StoredUserAsync(_factory, current.Id);
        Assert.True(
            string.Equals(stored.Login, Login, StringComparison.Ordinal),
            $"Ожидался неизменный login «{Login}», фактически «{stored.Login}» (поле login во входе должно игнорироваться).");
        Assert.False(
            string.Equals(stored.Login, IgnoredLoginField, StringComparison.Ordinal),
            "login пользователя не должен перезаписываться значением лишнего поля входа.");
    }
}
