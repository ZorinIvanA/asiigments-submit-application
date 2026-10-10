using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabsApp.IntegrationTests.B22.Infrastructure;

/// <summary>
/// Сессии тестовых хостов батча B-22 (ADR-015, TestSession-хелпер): access-JWT
/// минтится ITokenService тестового хоста для сид-преподавателя (учётка создаётся
/// сидом при любом Seed__DemoData) и передаётся заголовком Cookie с
/// именем-константой AuthCoreDefaults — единый источник с ICookieService.
/// POST /api/v1/auth/login для сессий не используется (ADR-015: авторизованные
/// запросы кейсов TS-184 не зависят от auth-эндпоинтов).
/// Клиенты — без авто-редиректов (точные статусы) и без cookie-контейнера.
/// </summary>
public static class B22Sessions
{
    /// <summary>Клиент без сессии.</summary>
    public static HttpClient Create(B22WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>
    /// Клиент с сессией сид-преподавателя (заголовок Cookie access_token выставлен
    /// во всех запросах клиента) и uuid преподавателя.
    /// </summary>
    public static (HttpClient Client, Guid TeacherId) CreateTeacherSession(B22WebAppFactory factory)
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
    /// Минт access-cookie (ADR-015): ITokenService.IssueAccessToken(userId, role) →
    /// заголовок Cookie access_token с именем по константе AuthCoreDefaults.
    /// </summary>
    public static void SetAccessCookie(B22WebAppFactory factory, HttpClient client, Guid userId, string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        Assert.True(
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie", $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}"),
            "Не удалось добавить access-cookie сессии преподавателя в заголовки тестового клиента.");
    }
}
