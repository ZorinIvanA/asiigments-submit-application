using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-029 «Флаги cookie при входе в Development (HttpOnly, SameSite=Strict, Path)»
/// (nfr, P0, FR-011 AC «Флаги cookie», NFR-010).
///
/// given: Development; пользователь teacher с известным паролем (сид FR-006,
///        Seed__TeacherPassword задан фикстурой).
/// when:  POST /auth/login с верными данными; разбор всех заголовков Set-Cookie.
/// then:  обе cookie (access_token, refresh_token) имеют HttpOnly и SameSite=Strict;
///        Secure отсутствует (только Production); обе cookie Path=/ (NFR-007/ADR-009).
///        (Механическая адаптация к контракту ADR-009: прежний Path=/api/v1/auth
///        refresh-cookie заменён спекой на Path=/ — константа AuthCoreDefaults.CookiePath.)
/// </summary>
public sealed class Ts029_CookieFlagsDevelopmentTests : IClassFixture<B06WebAppFactory>
{
    private readonly B06WebAppFactory _factory;

    public Ts029_CookieFlagsDevelopmentTests(B06WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginInDevelopment_SetsBothCookiesWithFlagsPerContract()
    {
        // given: Development; пользователь teacher с известным паролем.
        using var client = HostClients.Create(_factory);

        // when: POST /auth/login с верными данными.
        using var response = await ApiRequests.LoginAsync(
            client,
            SeedOptions.DefaultTeacherLogin,
            B06WebAppFactory.TestTeacherPassword);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход преподавателя должен вернуть 200, фактически {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: обе cookie HttpOnly и SameSite=Strict; Secure отсутствует;
        // обе cookie Path=/ (NFR-007: Path=/ 7 из 7, ADR-009).
        CookieAssertions.AssertAuthCookie(
            response,
            AuthCoreDefaults.AccessTokenCookieName,
            AuthCoreDefaults.CookiePath,
            expectSecure: false);
        CookieAssertions.AssertAuthCookie(
            response,
            AuthCoreDefaults.RefreshTokenCookieName,
            AuthCoreDefaults.CookiePath,
            expectSecure: false);
    }
}
