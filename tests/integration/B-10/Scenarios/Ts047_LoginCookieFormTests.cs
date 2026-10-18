using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-047 «Форма cookie при входе» (happy_path, FR-008/NFR-007, P0).
///
/// given: Development-стенд; успешный вход (учётные данные сида — учётка
///        teacher создаётся сидом из Seed__*, Seed__TeacherPassword хоста
///        равен умолчанию SeedOptions.DefaultTeacherPassword).
/// when:  разбор всех заголовков Set-Cookie ответа POST /auth/login.
/// then:  ровно два cookie: access_token (HttpOnly, SameSite=Strict, Path=/,
///        Max-Age=900) и refresh_token (HttpOnly, SameSite=Strict, Path=/,
///        Max-Age=604800); в Development флаг Secure отсутствует у обоих
///        (FR-008 AC «Форма cookie при входе»; NFR-007).
/// </summary>
public sealed class Ts047_LoginCookieFormTests : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory;

    public Ts047_LoginCookieFormTests(B10NoDemoWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginIssuesExactlyTwoCookies_WithStrictAttributesAndDevWithoutSecure()
    {
        // given: Development-стенд; клиент без cookie-контейнера (разбор сырых
        // Set-Cookie). Сессия НЕ минтится — кейс проверяет именно вход.
        using var client = HostClients.Create(_factory);

        // when: успешный вход (учётные данные сида) и разбор всех Set-Cookie.
        using var response = await client.PostAsJsonAsync(
            B10CookieFlow.LoginPath,
            new
            {
                login = SeedOptions.DefaultTeacherLogin,
                password = SeedOptions.DefaultTeacherPassword,
            });
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: успешный вход сеяным учителем — ожидался 200, фактически " +
            $"{(int)response.StatusCode} {response.StatusCode}, тело: " +
            $"{await response.Content.ReadAsStringAsync()}.");
        var cookies = B10SetCookieReader.Read(response);

        // then: ровно два cookie с именами access_token и refresh_token.
        Assert.True(
            cookies.Count == 2,
            $"Ожидалось ровно 2 Set-Cookie, фактически {cookies.Count}: " +
            $"{string.Join(" | ", cookies.Select(cookie => cookie.Name))}.");
        Assert.Equal(
            new[] { AuthCoreDefaults.AccessTokenCookieName, AuthCoreDefaults.RefreshTokenCookieName },
            cookies.Select(cookie => cookie.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var access = cookies.Single(cookie => cookie.Name == AuthCoreDefaults.AccessTokenCookieName);
        var refresh = cookies.Single(cookie => cookie.Name == AuthCoreDefaults.RefreshTokenCookieName);

        // then: access_token — HttpOnly, SameSite=Strict, Path=/, Max-Age=900.
        Assert.True(access.HasFlag("httponly"), "access_token: отсутствует флаг HttpOnly.");
        Assert.Equal("Strict", access.Attribute("samesite"), ignoreCase: true);
        Assert.Equal("/", access.Attribute("path"), ignoreCase: true);
        Assert.Equal("900", access.Attribute("max-age"));

        // then: refresh_token — HttpOnly, SameSite=Strict, Path=/, Max-Age=604800.
        Assert.True(refresh.HasFlag("httponly"), "refresh_token: отсутствует флаг HttpOnly.");
        Assert.Equal("Strict", refresh.Attribute("samesite"), ignoreCase: true);
        Assert.Equal("/", refresh.Attribute("path"), ignoreCase: true);
        Assert.Equal("604800", refresh.Attribute("max-age"));

        // then: в Development флаг Secure отсутствует у обоих.
        Assert.False(
            access.HasFlag("secure"),
            "access_token в Development не должен нести флаг Secure (IF-004: Secure вне Development).");
        Assert.False(
            refresh.HasFlag("secure"),
            "refresh_token в Development не должен нести флаг Secure (IF-004: Secure вне Development).");
    }
}
