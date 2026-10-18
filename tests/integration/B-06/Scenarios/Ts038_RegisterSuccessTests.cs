using System.Text.Json;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B06.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-038 «Успешная регистрация: 201 + MeDto + cookie + трим значений» (happy_path,
/// P0, FR-012 AC «Успешная регистрация»).
///
/// given: RemoteIpAddress=10.0.0.9 (лимит регистраций не исчерпан); логин 'new.user'
///        и email 'new@x.ru' свободны (свежий хост фикстуры, демо-сид отключён).
/// when:  POST /api/v1/auth/register {fullName:' Новая Фамилия ', login:' new.user ',
///        email:' new@x.ru ', password:'Passw0rd!', repeatPassword:'Passw0rd!'}.
/// then:  201; тело MeDto {login:'new.user', fullName:'Новая Фамилия', role:'student',
///        groupName:null}; Set-Cookie access_token и refresh_token; в хранилище
///        пользователь с триммированными значениями.
/// </summary>
public sealed class Ts038_RegisterSuccessTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts038_RegisterSuccessTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RegisterWithPaddedValues_Returns201MeDtoCookiesAndStoresTrimmedUser()
    {
        // given: RemoteIpAddress=10.0.0.9; логин и email свободны.
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/auth/register со значениями в пробелах.
        using var response = await ApiRequests.RegisterFromIpAsync(
            client,
            remoteIp: "10.0.0.9",
            fullName: " Новая Фамилия ",
            login: " new.user ",
            email: " new@x.ru ",
            password: "Passw0rd!",
            repeatPassword: "Passw0rd!");

        // then: 201; тело MeDto — ровно 4 поля, значения триммированы, role=student,
        // groupName=null (null-поле присутствует в JSON всегда, FR-007).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await BodyAssertions.ReadRootObjectAsync(response);
        BodyAssertions.HasExactlyProperties(root, "login", "fullName", "role", "groupName");
        Assert.Equal("new.user", root.GetProperty("login").GetString());
        Assert.Equal("Новая Фамилия", root.GetProperty("fullName").GetString());
        Assert.Equal("student", root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupName").ValueKind);

        // then: Set-Cookie access_token и refresh_token (автоматический вход, FR-012).
        CookieAssertions.RequireCookie(response, AuthCoreDefaults.AccessTokenCookieName);
        CookieAssertions.RequireCookie(response, AuthCoreDefaults.RefreshTokenCookieName);

        // then: в хранилище пользователь с триммированными значениями (IF-015:
        // чтение возвращает копию-снимок).
        var users = _factory.Services.GetRequiredService<IUserRepository>();
        var stored = users.GetByLogin("new.user");
        Assert.NotNull(stored);
        Assert.Equal("new.user", stored.Login);
        Assert.Equal("new@x.ru", stored.Email);
        Assert.Equal("Новая Фамилия", stored.FullName);
        Assert.Equal(UserRoles.Student, stored.Role);
    }
}
