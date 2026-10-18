using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Минт сессий доменных кейсов батча B-05 (FR-020/FR-021) по ADR-015: access-JWT
/// выпускается ITokenService тестового хоста и передаётся заголовком Cookie с
/// именем по константам Auth.Core (единый источник с ICookieService). POST
/// /auth/login и GET /auth/me — эндпойнты другой волны контроллеров: доменные
/// кейсы от них не зависят; сид-преподаватель берётся из IUserRepository (тот же
/// uuid, который вернул бы /auth/me для сидовой учётки).
/// </summary>
public static class B05MintedSessions
{
    public static HttpClient Create(B05WebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Минт сессии (ADR-015): access-JWT (userId, role) в заголовке Cookie access_token.</summary>
    public static void MintAccessCookie(B05WebAppFactory factory, HttpClient client, Guid userId, string role)
    {
        var jwt = factory.Services.GetRequiredService<ITokenService>().IssueAccessToken(userId, role);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={jwt}");
    }

    /// <summary>Клиент с сессией сид-преподавателя (учётка создана сидом FR-004).</summary>
    public static HttpClient CreateTeacherClient(B05WebAppFactory factory)
    {
        var teacher = factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException(
                "Сид-преподаватель не найден — шаг given «сессия teacher» неисполним.");
        var client = Create(factory);
        MintAccessCookie(factory, client, teacher.Id, UserRoles.Teacher);
        return client;
    }
}
