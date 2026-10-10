using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Помощники auth-сессий и DI-сида батча B-11 (кейсы FR-010/FR-011/FR-022:
/// Legacy58..Legacy61 — logout старого реестра, Ts062/Ts063 — me, TS-126..TS-128,
/// TS-155; шов-копия механики зоны B-10 — чужие
/// зоны недоступны, BL-001 BUG-001). Сессии кейсов создаются DI-сидом пользователя
/// через репозитории из factory.Services + минт access-JWT через ITokenService
/// (ADR-015: POST /auth/login для сессий кейсов не используется — лимитеры не
/// затрагиваются, зависимость от HTTP-входа устранена). Ручная сборка access-JWT
/// HS256 (формат IF-003: sub/role/iat, nbf=iat, exp) с произвольным ключом подписи
/// и сроком — шаг given кейса TS-063 («токен, подписанный другим ключом», «токен
/// с истёкшим exp»). Код реализации не затрагивается — только публичные интерфейсы
/// IF-003/IF-015 (IUserRepository/IGroupRepository/ITokenService).
/// </summary>
public static class B11AuthSessions
{
    /// <summary>
    /// HTTP-клиент с access-cookie пользователя login (минт по записи из
    /// DI-хранилища: сид-учётка teacher или DI-сид-студент SeedStudent).
    /// </summary>
    public static HttpClient CreateSessionClient(WebApplicationFactory<Program> factory, string login)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByLogin(login)
            ?? throw new InvalidOperationException(
                $"DI-сид: пользователь «{login}» не найден в хранилище хоста — given кейса неисполним.");
        var token = factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role, user.Login);

        var client = HostClients.Create(factory);
        HostClients.SetRequestCookie(client, AuthCoreDefaults.AccessTokenCookieName, token);
        return client;
    }

    /// <summary>
    /// DI-сид студента с заданными login/fullName (IUserRepository напрямую,
    /// ADR-010/CR-001 — демо-набор Seed__DemoData=false отключён). При заданном
    /// groupName группа создаётся (IGroupRepository), студент получает её Id —
    /// шаг given кейса TS-062 «student01 состоит в ИК-221» / «student31 без группы».
    /// </summary>
    public static User SeedStudent(
        WebApplicationFactory<Program> factory,
        string login,
        string fullName,
        string? groupName = null)
    {
        var services = factory.Services;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        Guid? groupId = null;
        if (groupName is not null)
        {
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = groupName,
                CreatedAt = now,
            };
            services.GetRequiredService<IGroupRepository>().Add(group);
            groupId = group.Id;
        }

        var student = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            // Сессии кейсов минтятся через ITokenService (ADR-015) — пароль не нужен.
            PasswordHash = "di-seed-mint-only",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = groupId,
            CreatedAt = now,
        };
        services.GetRequiredService<IUserRepository>().Add(student);
        return student;
    }

    /// <summary>
    /// Ручная сборка access-JWT HS256 (формат IF-003: claims sub/role/iat, nbf=iat,
    /// exp) с ЯВНО заданным ключом подписи и сроком — given кейса TS-063. Форма
    /// токена — как у TokenService.IssueAccessToken (JwtSecurityTokenHandler,
    /// MapInboundClaims=false у валидатора).
    /// </summary>
    public static string CraftAccessToken(
        Guid userId,
        string role,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string signingKey)
    {
        var jwt = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D", CultureInfo.InvariantCulture)),
                new Claim(AuthCoreDefaults.RoleClaimType, role),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
