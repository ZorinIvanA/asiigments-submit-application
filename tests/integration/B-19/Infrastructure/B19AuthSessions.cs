using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B19.Infrastructure;

/// <summary>
/// Сессии тестовых хостов бэкенда батча B-19 (ADR-015/ADR-022): access-JWT
/// минтится ITokenService тестового хоста для сид-преподавателя (учётка создаётся
/// сидом при любом Seed__DemoData) и передаётся заголовком Cookie с именем-константой
/// Auth.Core (единый источник с ICookieService). POST /api/v1/auth/login для сессий
/// не используется (ADR-015: доменные кейсы не зависят от auth-эндпоинтов).
/// Клиенты — без авто-редиректов (точные статусы) и без cookie-контейнера.
/// Копия механики зоны B-21 (чужая зона недоступна для ссылок — BL-001 BUG-001).
/// </summary>
public static class B19AuthSessions
{
    /// <summary>Клиент без сессии.</summary>
    public static HttpClient Create(B19ApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента) и uuid преподавателя.
    /// </summary>
    public static (HttpClient Client, Guid TeacherId) CreateTeacherSession(B19ApiFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
            .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден в DI-хранилище тестового хоста (шаг given «сессия teacher» неисполним).");

        var client = Create(factory);
        SetAccessCookie(factory, client, teacher.Id, teacher.Role);
        return (client, teacher.Id);
    }

    /// <summary>
    /// Минт access-cookie (ADR-022): ITokenService.IssueAccessToken(userId, role) →
    /// заголовок Cookie access_token с именем по константе Auth.Core.
    /// </summary>
    public static void SetAccessCookie(B19ApiFactory factory, HttpClient client, Guid userId, string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        Assert.True(
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}"),
            "Не удалось добавить access-cookie сессии преподавателя в заголовки тестового клиента.");
    }
}
