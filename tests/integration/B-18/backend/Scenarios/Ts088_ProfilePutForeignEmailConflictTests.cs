using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-088 «Профиль: занятый другим пользователем email — 409» (negative, FR-015, P0).
///
/// given: email 'b@x.ru' занят другим пользователем (второй DI-сид).
/// when:  PUT /api/v1/me/profile с этим email (текущий email пользователя — иной).
/// then:  409; message «Пользователь с таким email уже существует» (FR-015 AC
///        «Занятый email»; IF-013 CONFLICT_EMAIL: дубликат lower(email) среди
///        ДРУГИХ).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts088_ProfilePutForeignEmailConflictTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts088";
    private const string Email = "ts088@x.ru";
    private const string ForeignEmail = "b@x.ru";
    private const string ForeignLogin = "ts088-other";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts088_ProfilePutForeignEmailConflictTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithForeignOccupiedEmail_Returns409WithDictionaryMessage()
    {
        // given: email 'b@x.ru' занят ДРУГИМ пользователем; текущий пользователь с иным email.
        _ = B18ProfileHarness.SeedUser(
            _factory,
            login: ForeignLogin,
            email: ForeignEmail,
            fullName: "Другой Пользователь Восемьдесят Восемь",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-088",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с занятым другим пользователем email.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = "Студент ТС-088",
            email = ForeignEmail,
        });

        // then: 409 с дословным текстом словаря.
        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict,
            $"Ожидался статус 409, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B18ProfileAssertions.ReadRootObjectAsync(response);
        B18ProfileAssertions.MessageIs(root, ErrorTexts.DuplicateEmail);
    }
}
