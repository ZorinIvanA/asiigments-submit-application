using LabsApp.IntegrationTests.B10.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-192 «NFR-007: Secure 7/7 в Production-конфигурации» (nfr, NFR-007/FR-008, P0).
///
/// given: Production-стенд (Auth__JwtKey и нестандартный Seed__TeacherPassword
///        заданы — guard'ы FR-006 пройдены, старт возможен).
/// when:  прогон register/login/refresh/logout с разбором Set-Cookie.
/// then:  флаг Secure присутствует во всех 7 Set-Cookie экземплярах
///        (NFR-007: «Secure присутствует во всех 7 при Environment=Production»).
/// </summary>
public sealed class Ts192_SetCookieSecureProductionTests
{
    private const string FlowLogin = "b10ts192.user";
    private const string FlowPassword = "Passw0rd!";
    private const string FlowFullName = "Матрица Secure Б10";

    [Fact]
    public async Task AuthFlow_InProduction_AllSevenSetCookiesCarrySecureFlag()
    {
        // given: Production-стенд с валидными секретами (ключ подписи + нестандартный
        // сид-пароль); HttpClient без cookie-контейнера — разбор сырых Set-Cookie.
        using var factory = new B10ScenarioHostFactory.ProductionWithSecrets();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // when: register → login → refresh → logout; разбор каждого Set-Cookie.
        var steps = await B10CookieFlow.RunAsync(client, FlowLogin, FlowPassword, FlowFullName);

        // then: шаги выполнены; экземпляров ровно 7 (2+2+1+2).
        Assert.All(steps, step => Assert.True(
            (int)step.Status is >= 200 and < 300,
            $"{step.Endpoint}: HTTP {(int)step.Status} — шаг матрицы Set-Cookie не выполнен."));
        var all = steps.SelectMany(step => step.Cookies).ToList();
        Assert.True(
            all.Count == 7,
            $"Ожидалось ровно 7 Set-Cookie экземпляров (2+2+1+2), фактически {all.Count}: " +
            $"{string.Join(" | ", all.Select(cookie => $"{cookie.Name}={cookie.Attribute("max-age")}"))}.");

        // then: флаг Secure присутствует во всех 7.
        foreach (var cookie in all)
        {
            Assert.True(
                cookie.HasFlag("secure"),
                $"{cookie.Name}: нет флага Secure в Production (NFR-007: Secure 7/7).");
        }
    }
}
