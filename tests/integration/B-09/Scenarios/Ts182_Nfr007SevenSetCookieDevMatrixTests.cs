using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-182 «NFR-007: поэкземплярная матрица 7 Set-Cookie в dev» (nfr,
/// NFR-007 + FR-008 + ISS-010 + AR-001, P0).
///
/// given: Development-конфигурация (фикстура зоны); тестовый клиент;
///        свободные логин/email для регистрации.
/// when:  цикл: POST /auth/register; POST /auth/login; POST /auth/refresh;
///        POST /auth/logout — разбор всех Set-Cookie по экземплярам.
/// then:  ровно 7 экземпляров Set-Cookie: register — access_token(Max-Age=900)
///        и refresh_token(604800); login — access_token(900) и
///        refresh_token(604800); refresh — ТОЛЬКО access_token(900),
///        refresh_token не переустанавливается; logout — оба cookie с
///        Max-Age=0; атрибуты HttpOnly, SameSite=Strict, Path=/ — 7 из 7;
///        флаг Secure отсутствует во всех 7 (Development).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): файл прежней волны
/// с dev-частью совпадающей матрицы (Ts152_Nfr007SetCookieMatrix) не изменялся.
/// </summary>
public sealed class Ts182_Nfr007SevenSetCookieDevMatrixTests
{
    [Fact]
    public async Task DevFlow_RegisterLoginRefreshLogout_SevenSetCookieInstances_WithoutSecure()
    {
        // given/when: цикл register → login → refresh → logout в Development;
        // разбор всех Set-Cookie по экземплярам.
        var flow = await B09SetCookieFlow.RunAsync(new B09AuthDevFactory());

        // then: ровно 7 экземпляров: register (2), login (2), refresh (1),
        // logout (2).
        Assert.Equal(2, flow.Register.Count);
        Assert.Equal(2, flow.Login.Count);
        Assert.Single(flow.Refresh);
        Assert.Equal(2, flow.Logout.Count);

        // then: состав пар «имя → Max-Age» по эндпойнтам: register и login —
        // access_token(900) + refresh_token(604800); refresh — ТОЛЬКО
        // access_token(900) (refresh_token не переустанавливается); logout —
        // оба сброса с Max-Age=0.
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            flow.Register.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900"), ("refresh_token", "604800") },
            flow.Login.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "900") },
            flow.Refresh.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());
        Assert.Equal(
            new (string, string?)[] { ("access_token", "0"), ("refresh_token", "0") },
            flow.Logout.Select(cookie => (cookie.Name, cookie.Attribute("max-age"))).ToArray());

        // then: атрибуты HttpOnly, SameSite=Strict, Path=/ — 7 из 7.
        var all = flow.Register.Concat(flow.Login).Concat(flow.Refresh).Concat(flow.Logout).ToList();
        Assert.Equal(7, all.Count);
        foreach (var cookie in all)
        {
            Assert.True(cookie.HasFlag("httponly"), $"[Development] {cookie.Name}: нет HttpOnly.");
            Assert.Equal("strict", cookie.Attribute("samesite"));
            Assert.Equal("/", cookie.Attribute("path"));
        }

        // then: флаг Secure отсутствует во всех 7 (Development).
        Assert.Equal(0, all.Count(cookie => cookie.HasFlag("secure")));
    }
}
