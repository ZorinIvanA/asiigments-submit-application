using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B04.Infrastructure;

/// <summary>
/// Клиенты тестового хоста кейсов B-04 (FR-017/FR-020): без авто-редиректов
/// (точные статусы) и без cookie-контейнера — сессия МИНТИТСЯ явно (ADR-015):
/// access-JWT через ITokenService тестового хоста передаётся заголовком Cookie
/// с именем по константам Auth.Core (единый источник с ICookieService). POST
/// /auth/login для сквозных сессий доменных кейсов не используется.
/// </summary>
public static class MintedSessions
{
    public static HttpClient Create(B04HostFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Минт сессии (ADR-015): access-JWT (userId, role) в заголовке Cookie access_token.</summary>
    public static void MintAccessCookie(B04HostFactory factory, HttpClient client, Guid userId, string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
    }

    /// <summary>Клиент с сессией сид-преподавателя (учётка создана сидом FR-004).</summary>
    public static HttpClient CreateTeacherClient(B04HostFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «сессия teacher» неисполним.");
        var client = Create(factory);
        MintAccessCookie(factory, client, teacher.Id, UserRoles.Teacher);
        return client;
    }

    /// <summary>Клиент с сессией студента (запись создаётся DI-сидом кейса).</summary>
    public static HttpClient CreateStudentClient(B04HostFactory factory, User student)
    {
        var client = Create(factory);
        MintAccessCookie(factory, client, student.Id, UserRoles.Student);
        return client;
    }
}
