using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Auth.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Auth.Scenarios;

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
/// </summary>
public sealed class Ts048_LoginSetCookieFormTests
{
    private const string Login = "cookieform";
    private const string Email = "cookieform@example.com";

    [Fact]
    public async Task SuccessfulLogin_IssuesTwoCookies_WithContractAttributes()
    {
        using var factory = new B08AuthDevFactory();
        using var client = B08AuthHost.CreateClient(factory);

        // given: пользователь существует (DI-сид); хост фикстуры — Development.
        B08AuthHost.SeedUser(factory, Login, Email, "Куки Форма", UserRoles.Student);

        // given: выполнен успешный вход.
        using var login = await client.PostAsync(
            B08AuthHost.LoginEndpoint,
            "{\"login\":\"" + Login + "\",\"password\":\"" + B08AuthHost.TestUserPassword + "\"}");
        B08AuthHost.AssertStatus(login, HttpStatusCode.OK, "успешный вход (TS-048)");

        // when: разбор заголовков Set-Cookie ответа.
        var cookies = B08AuthHost.ParseSetCookie(login);

        // then: ровно два cookie: access_token и refresh_token.
        Assert.Equal(2, cookies.Count);
        var access = Assert.Single(cookies, cookie => cookie.Name == AuthCookieNames.AccessToken);
        var refresh = Assert.Single(cookies, cookie => cookie.Name == AuthCookieNames.RefreshToken);

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

    /// <summary>Имена cookie из контракта IF-004 (локальные константы — дословно кейс).</summary>
    private static class AuthCookieNames
    {
        public const string AccessToken = "access_token";
        public const string RefreshToken = "refresh_token";
    }
}
