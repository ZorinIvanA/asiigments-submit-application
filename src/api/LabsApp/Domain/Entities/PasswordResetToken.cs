namespace LabsApp.Domain.Entities;

/// <summary>
/// Доменная сущность PasswordResetToken (v2.2, ADR-008/ASM-002):
/// reset-токен — непрозрачная CSPRNG-строка ≥256 бит, в
/// хранилище живёт ТОЛЬКО её SHA-256-дайджест (<see cref="TokenHash"/>, 64
/// hex-символа lowercase), исходное значение токена не хранится никогда. Запись
/// создаётся при выдаче токена (recovery/confirm, TTL 15 минут), идентификатор
/// записи — сам <see cref="TokenHash"/> (уникальность — ordinal, без коллации);
/// гасится при применении в reset-password (одноразовость); сброс пароля гасит
/// ВСЕ токены пользователя. Метки времени — DateTime Utc
/// (DateTime.Kind = Utc, ADR-004).
/// </summary>
public sealed class PasswordResetToken
{
    /// <summary>
    /// SHA-256-дайджест непрозрачного reset-токена (64 hex-символа lowercase) —
    /// идентификатор записи; поиск при применении токена ведётся по нему (FR-014).
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Владелец (uuid User, чей пароль сбрасывается по токену).</summary>
    public Guid UserId { get; set; }

    /// <summary>Момент истечения токена (TTL 15 минут от выдачи), UTC.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Момент применения токена (одноразовость), UTC; null — токен ещё жив.
    /// Найденный неживой токен гасится: UsedAt проставляется и при отклонении
    /// просроченного/применённого токена (FR-014).
    /// </summary>
    public DateTime? UsedAt { get; set; }
}
