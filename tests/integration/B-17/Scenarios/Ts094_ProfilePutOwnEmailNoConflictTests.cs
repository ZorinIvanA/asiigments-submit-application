using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-094 «Профиль: PUT со своим email — не конфликтует» (boundary, FR-015,
/// P0).
///
/// given: текущий email пользователя 'x@example.com' (DI-сид; сессия —
///        минтованный access-cookie, ADR-015).
/// when:  PUT /me/profile без изменения email (fullName новый):
///        {fullName:'Новое ФИО', email:'x@example.com'}.
/// then:  200 — совпадение email с самим собой не 409 (FR-015 AC «Свой email
///        не конфликтует»; IF-013 CONFLICT_EMAIL: дубликат среди ДРУГИХ).
/// </summary>
public sealed class Ts094_ProfilePutOwnEmailNoConflictTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts094";
    private const string OwnEmail = "x@example.com";
    private const string NewFullName = "Новое ФИО";

    private readonly B17WebAppFactory _factory;

    public Ts094_ProfilePutOwnEmailNoConflictTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithUnchangedOwnEmail_Returns200()
    {
        // given: пользователь с текущим email 'x@example.com', сессия минтована.
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: OwnEmail,
            fullName: "Прежнее ФИО",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT без изменения email (только новый fullName).
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = OwnEmail,
        });

        // then: 200 (не 409) — совпадение с самим собой не конфликт; email не
        // изменился, fullName обновлён.
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Ожидался статус 200, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.StringPropertyIs(root, "login", Login);
        B17BodyAssertions.StringPropertyIs(root, "email", OwnEmail);
        B17BodyAssertions.StringPropertyIs(root, "fullName", NewFullName);
        B17BodyAssertions.StringPropertyIs(root, "role", UserRoles.Student);
    }
}
