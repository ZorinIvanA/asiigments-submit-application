using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-081 «me/profile PUT: email, занятый другим пользователем — 409»
/// (negative, FR-015, P0).
///
/// given: email 'other@example.com' занят другим пользователем (второй DI-сид).
/// when:  PUT /api/v1/me/profile с этим email (fullName валиден).
/// then:  409 'Пользователь с таким email уже существует' (FR-015 AC «Занятый
///        email»; IF-013 CONFLICT_EMAIL: дубликат lower(email) среди ДРУГИХ).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts081_ProfilePutTakenEmailConflictTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts081";
    private const string Email = "ts081@x.ru";
    private const string TakenEmail = "other@example.com";
    private const string ForeignLogin = "ts081-other";

    private readonly B16WebAppFactory _factory;

    public Ts081_ProfilePutTakenEmailConflictTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithForeignOccupiedEmail_Returns409WithDictionaryMessage()
    {
        // given: email 'other@example.com' занят ДРУГИМ пользователем; текущий
        // пользователь с иным email.
        _ = B16Harness.SeedUser(
            _factory,
            login: ForeignLogin,
            email: TakenEmail,
            fullName: "Другой Пользователь Восемьдесят Один",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-081",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с занятым другим пользователем email (fullName валиден).
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = "Студент ТС-081",
            email = TakenEmail,
        });

        // then: 409 с дословным текстом словаря.
        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict,
            $"Ожидался статус 409, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.DuplicateEmail);
    }
}
