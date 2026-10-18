using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B16.Infrastructure;

/// <summary>
/// Сессия «teacher» доменных кейсов групп батча B-16 (ADR-015, given «сессия
/// teacher» кейсов TS-102, TS-103, TS-104, TS-105, TS-106, TS-160, TS-169):
/// access-JWT минтится ITokenService тестового хоста для сид-преподавателя
/// (учётка сидится всегда, независимо от Seed__DemoData) и передаётся заголовком
/// Cookie с именем-константой AuthCoreDefaults — единый источник с ICookieService.
/// POST /api/v1/auth/login в предусловиях не используется (ADR-015: сквозные
/// сессии доменных кейсов не зависят от auth-эндпоинтов).
/// </summary>
public static class B16GroupsSession
{
    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента; без авто-редиректов и cookie-контейнера — точные
    /// статусы, CR-002).
    /// </summary>
    public static HttpClient CreateTeacher(WebApplicationFactory<Program> factory)
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
            "Не удалось добавить access-cookie сессии в заголовки тестового клиента.");

        return client;
    }
}
