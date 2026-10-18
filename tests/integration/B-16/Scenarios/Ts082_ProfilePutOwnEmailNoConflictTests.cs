using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-082 «me/profile PUT: пробельное ФИО и собственный email» (boundary,
/// FR-015, P1) — под-сценарий 2 из 2 (первый — Ts082_ProfilePutBlankFullNameTests
/// в соседнем файле; у каждого под-сценария СВОЯ фикстура, потому что given
/// «текущий email 'x@example.com'» выполняется буквально в обоих).
///
/// given: пользователь авторизован с текущим email 'x@example.com'.
/// when:  PUT {fullName:'Ф И О', email:'x@example.com'} (без изменения).
/// then:  200 — совпадение email с самим собой — не 409 (FR-015 AC «Свой email
///        не конфликтует»; IF-013 CONFLICT_EMAIL: дубликат среди ДРУГИХ).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts082_ProfilePutOwnEmailNoConflictTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts082own";
    private const string CurrentEmail = "x@example.com";

    private readonly B16WebAppFactory _factory;

    public Ts082_ProfilePutOwnEmailNoConflictTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_Returns200WithoutConflict()
    {
        // given: пользователь авторизован с текущим email 'x@example.com'.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: CurrentEmail,
            fullName: "Прежнее ФИО",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {fullName:'Ф И О', email:'x@example.com'} (без изменения).
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = "Ф И О",
            email = CurrentEmail,
        });

        // then: 200 — совпадение email с самим собой не является конфликтом;
        // ProfileDto отражает обновлённые значения.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 (свой email не конфликтует), фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "login", Login);
        B16Assertions.StringPropertyIs(root, "email", CurrentEmail);
        B16Assertions.StringPropertyIs(root, "fullName", "Ф И О");
        B16Assertions.StringPropertyIs(root, "role", UserRoles.Student);
    }
}
