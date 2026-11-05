using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-048 «Cookie при входе: форма и атрибуты» (happy_path, FR-008 + NFR-007,
/// P0).
///
/// given: хост Development; выполнен успешный вход.
/// when:  разбор заголовков Set-Cookie ответа.
/// then:  два cookie: access_token и refresh_token; оба HttpOnly,
///        SameSite=Strict, Path=/; Max-Age 900 и 604800 соответственно; флаг
///        Secure отсутствует в Development (FR-008 AC «Форма cookie при
///        входе»; NFR-007).
///
/// Примечание о размещении: given кейса называет зоной-владельцем
/// tests/integration/B-08/Auth (cookie-плитки прежних волн в зонах B-10 и
/// B-06 помечены арбитражем дубликатами). Поведенческая часть кейса исполнима
/// дословно и исполнена в собственной зоне батча B-09 (прецедент c-1052);
/// расхождение размещения зафиксировано в scenario_change_requests.
/// </summary>
public sealed class Ts048_LoginSetCookieFormTests(B09TimedWebAppFactory factory) : IClassFixture<B09TimedWebAppFactory>
{
    /// <summary>Имена cookie из контракта IF-004 (дословно кейс).</summary>
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";

    private const string TestIp = "10.0.0.48";

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task SuccessfulLogin_IssuesTwoCookies_WithContractAttributes()
    {
        // given: хост Development (фикстура зоны); выполнен успешный вход
        // сид-преподавателя teacher/teacher123!.
        _ = _factory.Services;
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        _ = await B09Assertions.ParseObjectAsync(login, HttpStatusCode.OK, "успешный вход (TS-048)");

        // when: разбор заголовков Set-Cookie ответа.
        var cookies = B09AuthSupport.ParseSetCookie(login);

        // then: ровно два cookie: access_token и refresh_token.
        Assert.Equal(2, cookies.Count);
        var access = Assert.Single(cookies, cookie => cookie.Name == AccessTokenCookie);
        var refresh = Assert.Single(cookies, cookie => cookie.Name == RefreshTokenCookie);

        // then: оба HttpOnly, SameSite=Strict, Path=/.
        foreach (var cookie in new[] { access, refresh })
        {
            Assert.True(cookie.HasFlag("httponly"), $"cookie {cookie.Name}: нет флага HttpOnly.");
            Assert.Equal("strict", cookie.Attribute("samesite"));
            Assert.Equal("/", cookie.Attribute("path"));
        }

        // then: Max-Age 900 и 604800 соответственно.
        Assert.Equal("900", access.Attribute("max-age"));
        Assert.Equal("604800", refresh.Attribute("max-age"));

        // then: флаг Secure отсутствует в Development.
        Assert.False(access.HasFlag("secure"), "access_token: флаг Secure присутствует в Development.");
        Assert.False(refresh.HasFlag("secure"), "refresh_token: флаг Secure присутствует в Development.");
    }
}
