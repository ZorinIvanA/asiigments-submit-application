using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-092 «Профиль: PUT email, занятый другим пользователем — 409»
/// (negative, FR-015, P0).
///
/// given: email 'student02@example.com' принадлежит другому пользователю
///        (второй DI-сид; текущий пользователь — student01 с иным email).
/// when:  PUT /me/profile {fullName:'Ф', email:'student02@example.com'} под
///        student01.
/// then:  409 'Пользователь с таким email уже существует' (FR-015 AC «Занятый
///        email»; IF-013 CONFLICT_EMAIL: дубликат lower(email) среди ДРУГИХ).
/// </summary>
public sealed class Ts092_ProfilePutTakenEmail409Tests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "student01";
    private const string Email = "student01@example.com";
    private const string TakenEmail = "student02@example.com";
    private const string ForeignLogin = "student02";

    private readonly B17WebAppFactory _factory;

    public Ts092_ProfilePutTakenEmail409Tests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithEmailOfAnotherUser_Returns409WithDictionaryMessage()
    {
        // given: email 'student02@example.com' занят другим пользователем;
        // текущий пользователь student01 с иным email.
        _ = B17ProfileHost.SeedUser(
            _factory,
            login: ForeignLogin,
            email: TakenEmail,
            fullName: "Иванов Иван Иванович 02",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Иванов Иван Иванович 01",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с email другого пользователя под student01.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.ProfileEndpoint, new
        {
            fullName = "Ф",
            email = TakenEmail,
        });

        // then: 409 с дословным текстом словаря.
        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict,
            $"Ожидался статус 409, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B17BodyAssertions.ReadRootObjectAsync(response);
        B17BodyAssertions.MessageIs(root, ErrorTexts.DuplicateEmail);
    }
}
