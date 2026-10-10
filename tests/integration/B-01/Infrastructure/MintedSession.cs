using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B01.Infrastructure;

/// <summary>
/// Сеанс авторизованного пользователя без HTTP-входа (ADR-015): пользователь
/// берётся из сида DI-хоста (IUserRepository), access-JWT минтится ITokenService
/// хоста и подставляется cookie access_token. POST /api/v1/auth/login не
/// используется: эндпойнт auth — зона другой задачи, и зависимость конвертных
/// кейсов FR-023 от HTTP-входа сузила бы волну (ADR-015).
/// </summary>
public static class MintedSession
{
    /// <summary>Клиент с валидной access-cookie сеяного учителя (роль teacher, FR-022).</summary>
    public static HttpClient CreateTeacherClient(B01WebAppFactory factory)
    {
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var teacher = users.GetByLogin(SeedOptions.DefaultTeacherLogin);
        Assert.True(
            teacher is not null,
            $"Сеяный учитель «{SeedOptions.DefaultTeacherLogin}» не найден в IUserRepository.");

        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher!.Id, UserRoles.Teacher);

        var client = HostClients.Create(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }
}
