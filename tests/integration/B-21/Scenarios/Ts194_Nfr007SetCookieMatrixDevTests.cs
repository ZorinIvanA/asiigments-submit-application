using LabsApp.IntegrationTests.B21.Infrastructure;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-194 (NFR-007, P0): матрица Set-Cookie 7/7 в dev-конфигурации.
///
/// given: Development (B21CookieMatrixFactory.CreateDevelopment — тестовый хост
///        зоны B-21, изоляция зон BL-001 BUG-001); выполнены последовательно
///        register, login, refresh (по refresh-cookie от login), logout (по
///        свежей сессии) — общий поток <see cref="B21CookieFlowHost.RunFourEndpointFlowAsync"/>
///        (ручной cookie-контейнер: Set-Cookie каждого ответа разбирается по
///        сырому ответу, контейнер только переносит значения).
/// when:  поэкземплярный разбор ВСЕХ 7 Set-Cookie
///        (<see cref="B21CookieFlowHost.AssertSetCookieMatrix"/>):
///        register — access_token(Max-Age=900) и refresh_token(604800); login —
///        access_token(900) и refresh_token(604800); refresh — ТОЛЬКО
///        access_token(900), refresh_token не переустанавливается; logout — оба
///        cookie с Max-Age=0.
/// then:  атрибуты HttpOnly, SameSite=Strict, Path=/ — 7 из 7; Max-Age: 900 —
///        у всех 3 выпусков access, 604800 — у обоих выпусков refresh, 0 — у
///        обоих сбросов logout; флаг Secure отсутствует во всех 7
///        (Development) (NFR-007/ISS-010/AR-001, ADR-009).
/// </summary>
public sealed class Ts194_Nfr007SetCookieMatrixDevTests
{
    private const string LoginName = "b194-matrix";
    private const string Email = "b194-matrix@example.com";
    private const string FullName = "Матрица Куки B-21";
    private const string Password = "B194-Matrix-Password1!";

    [Fact]
    public async Task Development_FourEndpointFlow_AllSevenSetCookieInstances_MatchMatrixWithoutSecure()
    {
        using var factory = B21CookieMatrixFactory.CreateDevelopment();

        // given/when: Development; register → login → refresh → logout; разбор
        // всех 7 экземпляров Set-Cookie.
        var (register, login, refresh, logout) = await B21CookieFlowHost.RunFourEndpointFlowAsync(
            factory, LoginName, Email, FullName, Password);

        // then: поэкземплярная матрица NFR-007 без Secure (Development).
        B21CookieFlowHost.AssertSetCookieMatrix(
            register, login, refresh, logout, expectSecure: false);
    }
}
