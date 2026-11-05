namespace LabsApp.Domain.Entities;

/// <summary>
/// Доменная сущность RefreshToken (domain_model): refresh-токен, хранится хэшем.
/// Жизненный цикл: active → revoked (logout / смена пароля / сброс пароля),
/// active → expired (истечение ExpiresAt).
/// </summary>
public sealed class RefreshToken
{
    /// <summary>UUID v4, генерируется сервером.</summary>
    public Guid Id { get; set; }

    /// <summary>Владелец токена (uuid User).</summary>
    public Guid UserId { get; set; }

    /// <summary>Хэш refresh-токена; исходное значение (256 бит, ADR-012) не хранится.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Момент истечения, UTC: создание + Auth__RefreshTtlDays.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Момент отзыва, UTC; null = токен действителен.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Момент создания, UTC (DateTime.Kind = Utc, ADR-005).</summary>
    public DateTime CreatedAt { get; set; }
}
