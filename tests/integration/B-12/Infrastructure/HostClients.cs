using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Infrastructure;

/// <summary>
/// Клиенты тестового хоста B-12: без авто-редиректов (точные статусы) и без
/// cookie-контейнера — сессия минтится явно (ADR-022): access-JWT через
/// ITokenService тестового хоста передаётся заголовком Cookie с именем и
/// значением по константам Auth.Core (единый источник с ICookieService).
/// POST /auth/login НЕ используется: минт — задокументированный способ сквозных
/// сессий доменных задач (ADR-022, ISS-002).
/// </summary>
public static class HostClients
{
    public static HttpClient Create(B12WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Минт сессии (ADR-022): access-JWT (userId, role) + cookie access_token.</summary>
    public static void MintAccessCookie(B12WebAppFactory factory, HttpClient client, Guid userId, string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
    }

    /// <summary>Клиент с сессией сид-преподавателя (учётка создана сидом FR-004/SeedOptions).</summary>
    public static HttpClient CreateTeacherClient(B12WebAppFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «сессия teacher» неисполним.");
        var client = Create(factory);
        MintAccessCookie(factory, client, teacher.Id, UserRoles.Teacher);
        return client;
    }

    /// <summary>Клиент с сессией студента (DI-сид) — шаг given «сессия студента».</summary>
    public static HttpClient CreateStudentClient(B12WebAppFactory factory, string login)
    {
        var student = factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
            ?? throw new InvalidOperationException(
                $"Студент «{login}» не найден — шаг given «сессия student» неисполним.");
        var client = Create(factory);
        MintAccessCookie(factory, client, student.Id, UserRoles.Student);
        return client;
    }
}
