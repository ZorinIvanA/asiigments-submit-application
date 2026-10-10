using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B07.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-048 «Лишние поля (включая role) игнорируются: роль всегда student»
/// (negative, FR-012, P1).
///
/// given: RemoteIpAddress=10.0.0.48; с этого IP выполнено 0 попыток регистрации
///        (лимит 5/час на IP не исчерпан).
/// when:  регистрация валидного тела с дополнительным полем role='teacher'.
/// then:  201; MeDto role='student'; в хранилище role='student' (преподаватель
///        через регистрацию не создаётся). FR-012: «лишние поля, включая role,
///        игнорируются; role всегда student».
/// </summary>
public sealed class Ts048_RegisterRoleIgnoredTests : IClassFixture<B07AuthWebAppFactory>
{
    private const string ClientIp = "10.0.0.48";
    private const string Login = "ts048";
    private const string Email = "ts048@x.ru";

    private readonly B07AuthWebAppFactory _factory;

    public Ts048_RegisterRoleIgnoredTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegistrationWithExtraRoleField_CreatesStudent()
    {
        // given: RemoteIpAddress=10.0.0.48; попыток регистрации с этого IP нет.
        using var client = B07AuthClients.CreateClientWithIp(_factory, ClientIp);

        // when: регистрация валидного тела с дополнительным полем role='teacher'.
        using var response = await client.PostAsJsonAsync(B07AuthClients.RegisterEndpoint, new
        {
            fullName = "Тест Роль",
            login = Login,
            email = Email,
            password = HostClients.TestUserPassword,
            repeatPassword = HostClients.TestUserPassword,
            role = UserRoles.Teacher,
        });

        // then: 201; MeDto role='student'.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.StringPropertyIs(body, "login", Login);
        BodyAssertions.StringPropertyIs(body, "role", UserRoles.Student);

        // then: в хранилище role='student' — преподаватель через регистрацию не создаётся.
        var stored = _factory.Services.GetRequiredService<IUserRepository>().GetByLogin(Login);
        Assert.NotNull(stored);
        Assert.Equal(UserRoles.Student, stored.Role);
    }
}
