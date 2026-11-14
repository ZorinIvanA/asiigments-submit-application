namespace LabsApp.Domain.Entities;

/// <summary>
/// Доменная сущность RecoveryCode (domain_model): код восстановления пароля
/// (6 цифр, TTL 10 минут, одноразовый). Жизненный цикл: live → used (успешный
/// confirm ИЛИ resend — все живые коды пользователя гасятся), live → annulled
/// (5-я неверная попытка), live → expired (истечение 10 минут).
/// </summary>
public sealed class RecoveryCode
{
    /// <summary>UUID v4; используется как jti reset-токена (FR-006).</summary>
    public Guid Id { get; set; }

    /// <summary>Владелец кода (uuid User); живой код — максимум один.</summary>
    public Guid UserId { get; set; }

    /// <summary>Хэш 6-значного кода; исходный код не хранится.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Момент истечения, UTC: создание + 10 минут.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Момент гашения, UTC; null = живой (не использован и не аннулирован).</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Число неверных попыток подтверждения; при достижении 5 код аннулируется.</summary>
    public int Attempts { get; set; }

    /// <summary>Момент создания, UTC (DateTime.Kind = Utc, ADR-005).</summary>
    public DateTime CreatedAt { get; set; }
}
