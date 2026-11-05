using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B06.Infrastructure;

namespace LabsApp.IntegrationTests.B06.Scenarios;

/// <summary>
/// TS-030 «Флаги cookie в Production: дополнительно Secure» (nfr, P0,
/// FR-011 AC «Флаги cookie», NFR-010: «Secure — в Production»).
///
/// given: Production-хост с валидными секретами (FR-030/FR-006 guard); пользователь
///        с известным паролем — сид-преподаватель (Seed__TeacherLogin, DI-сид ADR-010).
/// when:  POST /auth/login; разбор Set-Cookie.
/// then:  обе cookie содержат флаг Secure в дополнение к HttpOnly/SameSite=Strict;
///        обе cookie Path=/ (NFR-007/ADR-009).
///        (Механическая адаптация к контракту ADR-009: прежний Path=/api/v1/auth
///        refresh-cookie заменён спекой на Path=/ — константа AuthCoreDefaults.CookiePath.)
/// </summary>
public sealed class Ts030_CookieFlagsProductionTests : IClassFixture<B06ProductionWebAppFactory>
{
    private readonly B06ProductionWebAppFactory _factory;

    public Ts030_CookieFlagsProductionTests(B06ProductionWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginInProduction_SetsBothCookiesWithSecureFlag()
    {
        // given: Production-хост с валидными секретами; пользователь с известным паролем.
        using var client = HostClients.Create(_factory);

        // when: POST /auth/login с верными данными.
        using var response = await ApiRequests.LoginAsync(
            client,
            SeedOptions.DefaultTeacherLogin,
            B06ProductionWebAppFactory.ProductionTeacherPassword);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Предусловие кейса: вход преподавателя должен вернуть 200, фактически {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: обе cookie содержат Secure в дополнение к HttpOnly/SameSite=Strict;
        // обе cookie Path=/ (NFR-007: Path=/ 7 из 7, ADR-009).
        CookieAssertions.AssertAuthCookie(
            response,
            AuthCoreDefaults.AccessTokenCookieName,
            AuthCoreDefaults.CookiePath,
            expectSecure: true);
        CookieAssertions.AssertAuthCookie(
            response,
            AuthCoreDefaults.RefreshTokenCookieName,
            AuthCoreDefaults.CookiePath,
            expectSecure: true);
    }
}
