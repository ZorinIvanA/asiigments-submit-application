using LabsApp.Auth;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B07.Infrastructure;

/// <summary>
/// Клиенты с явно минтованной сессией для доменных кейсов батча B-07 (TS-209):
/// access-JWT выпускается ITokenService тестового хоста (ADR-015: сквозные сессии
/// доменных кейсов не зависят от POST /auth/login) и передаётся заголовком Cookie
/// с именем из констант Auth.Core (единый источник имён с ICookieService).
/// Учётка преподавателя создаётся сидом хоста (FR-004; Seed__DemoData=false —
/// в хранилище только сид-преподаватель). Клиенты — без cookie-контейнера:
/// сессия задаётся только минтованным access-cookie.
/// </summary>
public static class B07MintedSessions
{
    /// <summary>Клиент без авто-редиректов (точные статусы) и без cookie-контейнера.</summary>
    public static HttpClient Create(B07WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Минт сессии (ADR-015): access-JWT (userId, role) заголовком Cookie access_token.</summary>
    public static void MintAccessCookie(B07WebAppFactory factory, HttpClient client, Guid userId, string role)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(client);

        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
    }

    /// <summary>Клиент с сессией сид-преподавателя (шаг given «сессия teacher», TS-209).</summary>
    public static HttpClient CreateTeacherClient(B07WebAppFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «сессия teacher» неисполним.");
        var client = Create(factory);
        MintAccessCookie(factory, client, teacher.Id, teacher.Role);
        return client;
    }
}
