using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Сессия «teacher» для доменных кейсов батча (ADR-015, given TS-088..TS-094):
/// access-JWT минтится ITokenService тестового хоста для сид-преподавателя
/// (учётка создаётся сидом при любом Seed__DemoData) и передаётся заголовком Cookie
/// с именем-константой AuthCoreDefaults — единый источник с ICookieService.
/// POST /api/v1/auth/login не используется: эндпоинты auth-семейства — чужая волна,
/// доменные кейсы не зависят от них (ADR-015).
/// </summary>
public static class B03TeacherSession
{
    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента) и uuid преподавателя.
    /// </summary>
    public static (HttpClient Client, Guid TeacherId) Create(WebApplicationFactory<Program> factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден в DI-хранилище тестового хоста (шаг given «сессия teacher» неисполним).");

        var accessJwt = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher.Id, teacher.Role);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        Assert.True(
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={accessJwt}"),
            "Не удалось добавить access-cookie сессии преподавателя в заголовки тестового клиента.");

        return (client, teacher.Id);
    }
}
