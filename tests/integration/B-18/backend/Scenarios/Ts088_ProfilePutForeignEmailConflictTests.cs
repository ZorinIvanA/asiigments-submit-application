using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-088 «Профиль: занятый другим пользователем email — 409» (negative, FR-015, P0).
///
/// given: email 'b@x.ru' занят другим пользователем (DI-сид); текущий
///        пользователь приложения — засеянный преподаватель с иным email; клиент
///        анонимный (аутентификации в приложении нет).
/// when:  PUT /api/v1/me/profile с этим email.
/// then:  409; message «Пользователь с таким email уже существует» (FR-015 AC
///        «Занятый email»; IF-013 CONFLICT_EMAIL: дубликат lower(email) среди
///        ДРУГИХ).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts088_ProfilePutForeignEmailConflictTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string ForeignEmail = "b@x.ru";
    private const string ForeignLogin = "ts088-other";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts088_ProfilePutForeignEmailConflictTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_WithForeignOccupiedEmail_Returns409WithDictionaryMessage()
    {
        // given: email 'b@x.ru' занят ДРУГИМ пользователем; текущий пользователь —
        // засеянный преподаватель; клиент анонимный.
        _ = B18ProfileHarness.SeedUser(
            _factory,
            login: ForeignLogin,
            email: ForeignEmail,
            fullName: "Другой Пользователь Восемьдесят Восемь",
            role: UserRoles.Student,
            groupId: null);
        var current = await B18ProfileHarness.CurrentUserAsync(_factory);
        using var client = B18ProfileHarness.Create(_factory);

        // when: PUT с занятым другим пользователем email.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.ProfileEndpoint, new
        {
            fullName = current.FullName,
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
