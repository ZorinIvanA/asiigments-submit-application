using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-183 «NFR-007: флаг Secure 7/7 в Production» (nfr, NFR-007 + FR-008, P1).
///
/// given: Production-конфигурация (Auth__JwtKey задан — фиксированный ключ
///        фикстуры; Seed__TeacherPassword НЕстандартный — константа фикстуры
///        ProductionTeacherPassword, зафиксирована тестом).
/// when:  тот же цикл register/login/refresh/logout — разбор всех Set-Cookie
///        по экземплярам.
/// then:  флаг Secure присутствует во всех 7 Set-Cookie (NFR-007:
///        «unit-тест конфигурации cookie для Production (Secure 7/7)»).
///
/// Файл текущей волны батча B-09 (перенумерация кейсов): production-часть
/// матрицы прежней волны (Ts152*) не изменялась.
/// </summary>
public sealed class Ts183_Nfr007SecureOnAllSevenProductionTests
{
    [Fact]
    public async Task ProductionFlow_RegisterLoginRefreshLogout_SecureFlagOnAllSevenSetCookies()
    {
        // given: Production-конфигурация (ключ подписи и НЕстандартный
        // сид-пароль зафиксированы константами фикстуры).
        // when: цикл register → login → refresh → logout; разбор всех
        // Set-Cookie по экземплярам.
        var flow = await B09SetCookieFlow.RunAsync(new B09AuthProductionFactory());

        // then: те же 7 экземпляров (register 2, login 2, refresh 1, logout 2).
        Assert.Equal(2, flow.Register.Count);
        Assert.Equal(2, flow.Login.Count);
        Assert.Single(flow.Refresh);
        Assert.Equal(2, flow.Logout.Count);
        var all = flow.Register.Concat(flow.Login).Concat(flow.Refresh).Concat(flow.Logout).ToList();
        Assert.Equal(7, all.Count);

        // then: флаг Secure присутствует во всех 7 Set-Cookie.
        Assert.Equal(7, all.Count(cookie => cookie.HasFlag("secure")));
        foreach (var cookie in all)
        {
            Assert.True(
                cookie.HasFlag("secure"),
                $"[Production] {cookie.Name}: нет флага Secure (матрица NFR-007 Secure 7/7).");
        }
    }
}
