using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LabsApp.IntegrationTests.B10.Infrastructure;

/// <summary>
/// Помощники auth-сессий батча B-10 (кейсы FR-010/FR-011/FR-022: TS-059..TS-063,
/// TS-126..TS-128, TS-155): минт живого refresh-токена через ITokenService +
/// ISecurityTokenRepository (зеркало IssueSession AuthController — только публичные
/// интерфейсы IF-003/IF-015, код реализации не затрагивается), DI-сид студента
/// с заданными login/fullName и группой (шаг given «student01 состоит в ИК-221»,
/// TS-062) и ручная сборка access-JWT HS256 (формат IF-003) с произвольным ключом
/// подписи и сроком — шаг given кейса TS-063 («токен, подписанный другим ключом»,
/// «токен с истёкшим exp»). Refresh-минт не расходует лимитеры и не зависит от
/// POST /auth/login (ADR-015/ADR-022).
/// </summary>
public static class B10AuthSessions
{
    /// <summary>
    /// Минт живого refresh-токена пользователя: значение возвращается вызывающему,
    /// в хранилище — только SHA-256-хэш и TTL (зеркало AuthController.IssueSession,
    /// IF-003/IF-015; TTL refresh — Auth__RefreshTtlDays по TimeProvider хоста).
    /// </summary>
    public static string MintRefreshToken(B10HostFactory factory, Guid userId)
    {
        var grant = factory.Services.GetRequiredService<ITokenService>().CreateRefreshToken(userId);
        factory.Services.GetRequiredService<ISecurityTokenRepository>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = grant.TokenHash,
            ExpiresAt = grant.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
        });
        return grant.Value;
    }

    /// <summary>
    /// DI-сид студента в группе с заданными login и fullName (IUserRepository/
    /// IGroupRepository напрямую, ADR-010/CR-001 — демо-набор FR-004 отключён).
    /// </summary>
    public static (User Student, Group Group) SeedStudentWithGroup(
        B10HostFactory factory, string login, string fullName, string groupName)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = groupName,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IGroupRepository>().Add(group);

        var student = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Email = string.Create(CultureInfo.InvariantCulture, $"{login}@example.com"),
            // Сессии минтятся через ITokenService (ADR-022) — пароль не используется.
            PasswordHash = "di-seed-mint-only",
            FullName = fullName,
            Role = UserRoles.Student,
            GroupId = group.Id,
            CreatedAt = DateTime.UtcNow,
        };
        factory.Services.GetRequiredService<IUserRepository>().Add(student);
        return (student, group);
    }

    /// <summary>
    /// Ручная сборка access-JWT HS256 (формат IF-003: claims sub/role/iat, nbf=iat,
    /// exp) с ЯВНО заданным ключом подписи и сроком — шаг given «токен, подписанный
    /// другим ключом» и «токен с истёкшим exp» (TS-063). Форма токена — как у
    /// TokenService.IssueAccessToken (JwtSecurityTokenHandler, MapInboundClaims=false).
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
