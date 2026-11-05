using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-042 «Дубликат логина ci» (negative, P0, FR-012 AC «Дубликат логина ci»).
///
/// given: существует пользователь login='teacher' (сид преподавателя FR-006 —
///        фикстура с Seed__TeacherLogin по умолчанию «teacher»); email регистрируемого
///        свободен; RemoteIpAddress=10.0.0.42 (лимит регистраций на IP не исчерпан).
/// when:  POST /api/v1/auth/register {login:'TEACHER', прочее валидно}.
/// then:  409 «Пользователь с таким логином уже существует».
/// </summary>
public sealed class Ts042_DuplicateLoginCiTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts042_DuplicateLoginCiTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithCaseVariantOfExistingLogin_Returns409DuplicateLogin()
    {
        // given: существует пользователь login='teacher'; email свободен.
        using var client = HostClients.Create(_factory);

        // when: регистрация с login='TEACHER' (совпадение без учёта регистра).
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.42",
            fullName: "Дубль Логина",
            login: "TEACHER",
            email: "ts042@example.com",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 409 с текстом словаря (errors у 409 нет — только message, IF-001).
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "message");
        BodyAssertions.MessageIs(root, "Пользователь с таким логином уже существует");
    }
}
