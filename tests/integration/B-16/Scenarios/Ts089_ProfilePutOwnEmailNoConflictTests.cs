using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-089 «Профиль: свой email не конфликтует» (boundary, FR-015, P1;
/// нумерация текущего батча B-16).
///
/// given: текущий email пользователя — 'x@example.com'.
/// when:  PUT /api/v1/me/profile {fullName:'Новое ФИО', email:'x@example.com'}
///        (без изменения email).
/// then:  200 — совпадение с самим собой не 409 (AC FR-015 «Свой email не
///        конфликтует»; IF-013 CONFLICT_EMAIL: дубликат среди ДРУГИХ).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts089_ProfilePutOwnEmailNoConflictTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts089own";
    private const string OwnEmail = "x@example.com";
    private const string NewFullName = "Новое ФИО";

    private readonly B16WebAppFactory _factory;

    public Ts089_ProfilePutOwnEmailNoConflictTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithOwnUnchangedEmail_Returns200_Not409()
    {
        // given: текущий email пользователя — 'x@example.com'.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: OwnEmail,
            fullName: "Прежнее ФИО",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT {fullName:'Новое ФИО', email:'x@example.com'} (без изменения email).
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = OwnEmail,
        });

        // then: 200 — совпадение с самим собой не 409; обновлённый ProfileDto.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200 (свой email не конфликтует), фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.StringPropertyIs(root, "fullName", NewFullName);
        B16Assertions.StringPropertyIs(root, "email", OwnEmail);
    }
}
