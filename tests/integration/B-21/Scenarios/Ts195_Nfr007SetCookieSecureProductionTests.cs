using LabsApp.IntegrationTests.B21.Infrastructure;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-195 (NFR-007, P1): Secure 7/7 в Production-конфигурации.
///
/// given: конфигурация Production (B21CookieMatrixFactory.CreateProduction —
///        заданы Auth__JwtKey ≥32 символа и НЕстандартный Seed__TeacherPassword,
///        guard'ы FR-006/FR-025 проходят); выполнение register → login → refresh
///        → logout с разбором всех 7 Set-Cookie
///        (<see cref="B21CookieFlowHost.RunFourEndpointFlowAsync"/>).
/// when:  поэкземплярный разбор ВСЕХ 7 Set-Cookie
///        (<see cref="B21CookieFlowHost.AssertSetCookieMatrix"/>).
/// then:  флаг Secure присутствует во всех 7 экземплярах; прочие атрибуты
///        соответствуют dev-матрице (состав «имя → Max-Age» 900/604800/0,
///        HttpOnly, SameSite=Strict, Path=/ — 7 из 7; refresh — ТОЛЬКО
///        access_token) (NFR-007: «Secure присутствует во всех 7 при
///        Environment=Production», ADR-009: Secure iff окружение ≠ Development).
/// </summary>
public sealed class Ts195_Nfr007SetCookieSecureProductionTests
{
    private const string LoginName = "b195-matrix";
    private const string Email = "b195-matrix@example.com";
    private const string FullName = "Матрица Куки Production B-21";
    private const string Password = "B195-Matrix-Password1!";

    [Fact]
    public async Task Production_FourEndpointFlow_AllSevenSetCookieInstances_CarrySecure()
    {
        // given: Production (Auth__JwtKey и нестандартный Seed__TeacherPassword заданы).
        using var factory = B21CookieMatrixFactory.CreateProduction();

        // when: register → login → refresh → logout; разбор всех 7 Set-Cookie.
        var (register, login, refresh, logout) = await B21CookieFlowHost.RunFourEndpointFlowAsync(
            factory, LoginName, Email, FullName, Password);

        // then: Secure на всех 7; прочие атрибуты — по той же матрице NFR-007.
        B21CookieFlowHost.AssertSetCookieMatrix(
            register, login, refresh, logout, expectSecure: true);
    }
}
