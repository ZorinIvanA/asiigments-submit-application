using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B15.Groups.Infrastructure;

/// <summary>
/// Сессия «teacher» доменных кейсов групп батча B-15 (ADR-015, given «сессия
/// teacher» TS-102..TS-106, TS-160): access-JWT минтится ITokenService тестового
/// хоста для сид-преподавателя (учётка сидится всегда, независимо от
/// Seed__DemoData) и передаётся заголовком Cookie с именем-константой
/// AuthCoreDefaults — единый источник с ICookieService. POST /api/v1/auth/login
/// не используется: эндпоинты auth-семейства — чужая волна, доменные кейсы групп
/// от них не зависят (ADR-015).
/// </summary>
public static class B15GroupsSession
{
    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента).
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
