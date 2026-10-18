using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B15.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-080 «me/profile PUT: обновляются только fullName/email, groupName
/// пересчитывается» (happy_path, FR-015, P0).
///
/// given: пользователь student01 авторизован (сессия харнеса, ADR-015/CR-001);
///        новые значения валидны и email свободен; поле login во входе передано
///        (игнорируется); после обновления группа пользователя переименована
///        в 'ИК-999' (DI-правка IGroupRepository — precondition-шаг кейса).
/// when:  PUT /me/profile {fullName:'Новое ФИО', email:'new@example.com',
///        login:'hacker'}; затем GET /me/profile.
/// then:  PUT — 200 ProfileDto с новыми значениями, login и роль неизменны
///        ('student01'/'student', поле login во входе проигнорировано); GET
///        после переименования — groupName пересчитан по текущему состоянию
///        групп — 'ИК-999' (FR-015 AC «Успешное редактирование»).
/// </summary>
public sealed class Ts080_MeProfilePutKeepsLoginRecalcsGroupTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "student01";
    private const string OldEmail = "student01@example.com";
    private const string OldFullName = "Старое ФИО";
    private const string NewFullName = "Новое ФИО";
    private const string NewEmail = "new@example.com";
    private const string IgnoredLoginField = "hacker";
    private const string OriginalGroupName = "ИК-221";
    private const string RenamedGroupName = "ИК-999";

    private readonly B15WebAppFactory _factory;

    public Ts080_MeProfilePutKeepsLoginRecalcsGroupTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutProfile_UpdatesOnlyNameEmail_And_RecalculatesGroupName()
    {
        // given: студент в группе ИК-221 со свободным целевым email.
        var group = B15Harness.SeedGroup(_factory, OriginalGroupName);
        var user = B15Harness.SeedStudent(_factory, fullName: OldFullName, login: Login, email: OldEmail, groupId: group.Id);
        using var client = B15Harness.CreateSessionClient(_factory, user.Id);

        // when: PUT с полем login во входе (обязан быть проигнорирован).
        using var putResponse = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = NewFullName,
            email = NewEmail,
            login = IgnoredLoginField,
        });

        // then (PUT): 200 ProfileDto с новыми fullName/email; login и роль
        // неизменны — поле login во входе игнорируется; группа не редактируется
        // (в момент PUT она ещё 'ИК-221' — переименование произойдёт позже).
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putBody = await BodyAssertions.ReadRootObjectAsync(putResponse);
        BodyAssertions.StringPropertyIs(putBody, "fullName", NewFullName);
        BodyAssertions.StringPropertyIs(putBody, "email", NewEmail);
        BodyAssertions.StringPropertyIs(putBody, "login", Login);
        BodyAssertions.StringPropertyIs(putBody, "role", "student");
        BodyAssertions.StringPropertyIs(putBody, "groupName", OriginalGroupName);

        // given (продолжение): после обновления группа пользователя переименована
        // в 'ИК-999' — DI-правка состояния групп (запись заменяется целиком).
        var groups = _factory.Services.GetRequiredService<IGroupRepository>();
        var stored = groups.GetById(group.Id);
        Assert.NotNull(stored);
        groups.Update(new Group { Id = stored.Id, Name = RenamedGroupName, CreatedAt = stored.CreatedAt });

        // when (продолжение): GET профиля после переименования.
        using var getResponse = await client.GetAsync(B15Harness.ProfileEndpoint);

        // then (GET): groupName пересчитан по ТЕКУЩЕМУ состоянию групп;
        // остальные поля профиля — новые значения, login/роль не тронуты.
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await BodyAssertions.ReadRootObjectAsync(getResponse);
        BodyAssertions.StringPropertyIs(getBody, "login", Login);
        BodyAssertions.StringPropertyIs(getBody, "email", NewEmail);
        BodyAssertions.StringPropertyIs(getBody, "fullName", NewFullName);
        BodyAssertions.StringPropertyIs(getBody, "role", "student");
        BodyAssertions.StringPropertyIs(getBody, "groupName", RenamedGroupName);
    }
}
