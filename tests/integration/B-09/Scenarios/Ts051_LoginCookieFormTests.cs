using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-051 «Токены: форма cookie при входе» (happy_path, FR-008 + NFR-007, P0).
///
/// given: выполнен успешный вход (ответ POST /auth/login).
/// when:  разбор Set-Cookie ответа.
/// then:  два cookie: access_token (Max-Age=900) и refresh_token
///        (Max-Age=604800); оба HttpOnly; SameSite=Strict; Path=/
///        (AC FR-008 «Форма cookie при входе»; полная поэкземплярная матрица
///        7 Set-Cookie — TS-182).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// с совпадающим поведением (Ts048_LoginSetCookieForm) не изменялся.
/// </summary>
public sealed class Ts051_LoginCookieFormTests(B09TimedWebAppFactory factory) : IClassFixture<B09TimedWebAppFactory>
{
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";
    private const string TestIp = "10.0.0.51";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task SuccessfulLogin_SetCookieForm_Access900Refresh604800_WithContractAttributes()
    {
        // given: выполнен успешный вход (сид-преподаватель teacher/teacher123!).
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(login, HttpStatusCode.OK, "успешный вход (TS-051)");

        // when: разбор Set-Cookie ответа.
        var cookies = B09AuthSupport.ParseSetCookie(login);

        // then: ровно два cookie: access_token и refresh_token.
        Assert.Equal(2, cookies.Count);
        var access = Assert.Single(cookies, cookie => cookie.Name == AccessTokenCookie);
        var refresh = Assert.Single(cookies, cookie => cookie.Name == RefreshTokenCookie);

        // then: оба HttpOnly; SameSite=Strict; Path=/.
        foreach (var cookie in new[] { access, refresh })
        {
            Assert.True(cookie.HasFlag("httponly"), $"cookie {cookie.Name}: нет флага HttpOnly.");
            Assert.Equal("strict", cookie.Attribute("samesite"));
            Assert.Equal("/", cookie.Attribute("path"));
        }

        // then: Max-Age=900 у access_token и Max-Age=604800 у refresh_token.
        Assert.Equal("900", access.Attribute("max-age"));
        Assert.Equal("604800", refresh.Attribute("max-age"));
    }
}
