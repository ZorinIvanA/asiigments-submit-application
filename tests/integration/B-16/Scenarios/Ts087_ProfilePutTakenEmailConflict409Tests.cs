using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-087 «Профиль: занятый другим пользователем email — 409» (negative,
/// FR-015, P0; нумерация текущего батча B-16).
///
/// given: email 'other@example.com' занят другим пользователем (DI-сид второго
///        пользователя с этим email).
/// when:  PUT /api/v1/me/profile с этим email (fullName валиден).
/// then:  409 'Пользователь с таким email уже существует' (AC FR-015 «Занятый
///        email»; IF-013 CONFLICT_EMAIL).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts087_ProfilePutTakenEmailConflict409Tests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts087profile";
    private const string Email = "ts087profile@x.ru";
    private const string TakenEmail = "other@example.com";
    private const string ValidFullName = "Валидное ФИО";
    private const string ForeignLogin = "ts087other";

    private readonly B16WebAppFactory _factory;

    public Ts087_ProfilePutTakenEmailConflict409Tests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithEmailTakenByOtherUser_Returns409WithDictionaryText()
    {
        // given: email 'other@example.com' занят другим пользователем.
        _ = B16Harness.SeedUser(
            _factory,
            login: ForeignLogin,
            email: TakenEmail,
            fullName: "Другой Пользователь Восемьдесят Семь",
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: ValidFullName,
            role: UserRoles.Student,
            groupId: null,
            password: B16Harness.TestUserPassword);
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с этим email (fullName валиден).
        using var response = await client.PutAsJsonAsync(B16Harness.ProfileEndpoint, new
        {
            fullName = ValidFullName,
            email = TakenEmail,
        });

        // then: 409 'Пользователь с таким email уже существует' — дословно.
        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict,
            $"Ожидался статус 409, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var root = await B16Assertions.ReadRootObjectAsync(response);
        B16Assertions.MessageIs(root, ErrorTexts.DuplicateEmail);
    }
}
