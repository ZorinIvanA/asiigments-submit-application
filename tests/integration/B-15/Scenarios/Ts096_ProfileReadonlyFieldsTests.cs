using System.Text.Json;
using LabsApp.IntegrationTests.B15.Infrastructure;

namespace LabsApp.IntegrationTests.B15.Scenarios;

/// <summary>
/// TS-096 «login/role/groupId через PUT /me/profile не редактируются» (negative, FR-021, P1).
///
/// given: текущий пользователь приложения — засеянный преподаватель
///        (Seed__TeacherLogin «teacher», role='teacher', groupId=null, email
///        'teacher@example.com'); клиент анонимный (аутентификации в приложении нет).
/// when:  PUT /me/profile {fullName:'Имя', email:свой, login:'hacker',
///        role:'student', groupId:'<uuid>'}.
/// then:  200; в ответе и хранилище login='teacher', role='teacher', groupId=null
///        (лишние поля игнорируются). FR-021: «login/role/groupId не редактируются,
///        лишние поля игнорируются».
/// </summary>
public sealed class Ts096_ProfileReadonlyFieldsTests : IClassFixture<B15WebAppFactory>
{
    private const string Login = "teacher";
    private const string Email = "teacher@example.com";

    private readonly B15WebAppFactory _factory;

    public Ts096_ProfileReadonlyFieldsTests(B15WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProfileUpdate_IgnoresReadonlyFields()
    {
        // given: текущий пользователь — засеянный преподаватель (без группы); клиент анонимный.
        var current = await B15Harness.CurrentUserAsync(_factory);
        Assert.Equal(Login, current.Login);
        Assert.Null(current.GroupId);
        using var client = B15Harness.CreateClient(_factory);

        // when: попытка отредактировать login/role/groupId лишними полями.
        using var response = await client.PutAsJsonAsync(B15Harness.ProfileEndpoint, new
        {
            fullName = "Имя",
            email = Email,
            login = "hacker",
            role = "student",
            groupId = Guid.NewGuid(),
        });

        // then: 200; в ответе login/role не изменились, группы нет
        // (groupName=null — транспортная форма groupId в ProfileDto).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(root, "login", Login);
        BodyAssertions.StringPropertyIs(root, "role", "teacher");
        Assert.True(
            root.TryGetProperty("groupName", out var groupName) && groupName.ValueKind == JsonValueKind.Null,
            $"Ожидался groupName=null, фактически: {root.GetRawText()}");

        // then: и в хранилище login='teacher', role='teacher', groupId=null.
        var stored = await B15Harness.UserByIdAsync(_factory, current.Id);
        Assert.Equal(Login, stored.Login);
        Assert.Equal("teacher", stored.Role);
        Assert.Null(stored.GroupId);
    }
}
