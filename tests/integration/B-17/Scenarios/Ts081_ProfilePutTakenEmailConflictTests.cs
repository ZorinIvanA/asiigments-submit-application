using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-081 «me/profile PUT: email, занятый другим пользователем — 409»
/// (negative, FR-015, P0).
///
/// given: email 'student02@example.com' принадлежит другому пользователю
///        (DI-сид); текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher»), его email отличен; клиент анонимный
///        (аутентификации в приложении нет).
/// when:  PUT /me/profile {fullName:'Ф', email:'student02@example.com'}.
/// then:  409 'Пользователь с таким email уже существует' (FR-015 AC «Занятый
///        email»; IF-013 CONFLICT_EMAIL: дубликат lower(email) среди ДРУГИХ).
/// </summary>
public sealed class Ts081_ProfilePutTakenEmailConflictTests : IClassFixture<B17WebAppFactory>
{
    private const string TakenEmail = "student02@example.com";
    private const string ForeignLogin = "student02";

    private readonly B17WebAppFactory _factory;

    public Ts081_ProfilePutTakenEmailConflictTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithEmailOfAnotherUser_Returns409WithDictionaryMessage()
    {
        // given: email 'student02@example.com' занят другим пользователем;
        // текущий пользователь — засеянный преподаватель; клиент анонимный.
        _ = B17ProfileHost.SeedUser(
            _factory,
            login: ForeignLogin,
            email: TakenEmail,
            fullName: "Иванов Иван Иванович 02",
            role: UserRoles.Student,
            groupId: null);
        _ = await B17ProfileHost.CurrentUserAsync(_factory);
        using var client = B17ProfileHost.Create(_factory);

        // when: PUT с email другого пользователя.
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
