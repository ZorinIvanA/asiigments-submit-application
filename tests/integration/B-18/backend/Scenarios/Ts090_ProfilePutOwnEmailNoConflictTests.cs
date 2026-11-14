using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-090 «Профиль: собственный email не конфликтует» (boundary, FR-015, P1).
///
/// given: текущий email пользователя X — 'x@example.com' (DI-сид).
/// when:  PUT /api/v1/me/profile с тем же email (и валидным новым ФИО).
/// then:  200 — совпадение с самим собой не даёт 409 (FR-015 AC «Свой email не
///        конфликтует»; IF-013 CONFLICT_EMAIL: дубликат среди ДРУГИХ пользователей).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts090_ProfilePutOwnEmailNoConflictTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts090-x";
    private const string OwnEmail = "x@example.com";
    private const string NewFullName = "Икс Собственный Email";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts090_ProfilePutOwnEmailNoConflictTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_Returns200WithoutConflict()
    {
        // given: пользователь X с текущим email 'x@example.com'.
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: OwnEmail,
            fullName: "Пользователь Икс",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с тем же email и валидным новым ФИО.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = OwnEmail,
        });

        // then: 200 (совпадение с самим собой — не 409); ФИО обновлено, email прежний.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 (свой email не конфликтует), фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.StringPropertyIs(root, "email", OwnEmail);
        B18ProfileAssertions.StringPropertyIs(root, "fullName", NewFullName);
    }
}
