using LabsApp.Domain.Entities;

namespace LabsApp.Infrastructure;

/// <summary>
/// Консолидированный репозиторий токенов безопасности (IF-015, FR-024): refresh-токены,
/// коды восстановления и reset-токены в одном хранилище (v2.2/ADR-008; консолидация
/// задачи T-101 вместо четырёх раздельных хранилищ). Хранятся ТОЛЬКО хэши; живость
/// записей вычисляется лениво (таймеров в хранилище нет, ADR-002): refresh —
/// revokedAt == null && expiresAt &gt; now; код и reset-токен — usedAt == null &&
/// expiresAt &gt; now. Метки времени — TimeProvider (FR-003). Контракт асинхронный
/// (реализации могут выполнять ввод-вывод).
/// </summary>
public interface ISecurityTokenRepository
{
    // ------------------------------------------------------------------
    // Refresh-токены (IF-003, FR-002/FR-009/FR-010)
    // ------------------------------------------------------------------

    /// <summary>
    /// Вставка токена (Id и TokenHash заполняет сервис). Повторный TokenHash —
    /// <see cref="InvalidOperationException"/> (fail-fast: коллизия CSPRNG-токена
    /// невозможна и свидетельствует о дефекте).
    /// </summary>
    Task AddAsync(RefreshToken token);

    /// <summary>
    /// ЖИВЫЙ токен по значению хэша (точное сравнение Ordinal) либо null:
    /// отозванный (revokedAt ≠ null) и истёкший (expiresAt ≤ now) токены не живы.
    /// </summary>
    Task<RefreshToken?> FindLiveByHashAsync(string tokenHash);

    /// <summary>
    /// Отзыв токена: RevokedAt = now (TimeProvider), если ещё не отозван.
    /// Отсутствующий токен — отсутствие операции (идемпотентный logout).
    /// </summary>
    Task RevokeAsync(Guid id);

    /// <summary>
    /// Атомарный отзыв всех токенов пользователя (смена/сброс пароля): каждый
    /// не отозванный токен пользователя получает RevokedAt = now; уже отозванные
    /// сохраняют исходный момент отзыва. Токен с хэшем
    /// <paramref name="exceptTokenHash"/> (текущая сессия, ISS-002) не отзывается;
    /// null — отзываются все.
    /// </summary>
    Task RevokeAllForUserExceptAsync(Guid userId, string? exceptTokenHash);

    // ------------------------------------------------------------------
    // Коды восстановления (FR-006/FR-012)
    // ------------------------------------------------------------------

    /// <summary>
    /// Атомарно: все незагашенные коды пользователя получают UsedAt = now
    /// (resend гасит прежние живые коды — одновременно жив максимум один),
    /// затем вставляется новый код.
    /// </summary>
    Task AddLiveAsync(RecoveryCode code);

    /// <summary>
    /// Живой код пользователя (usedAt == null && expiresAt &gt; now) либо null;
    /// при нескольких живых (вне инварианта) — созданный последним.
    /// </summary>
    Task<RecoveryCode?> FindLiveForUserAsync(Guid userId);

    /// <summary>
    /// Инкремент числа неверных попыток ЖИВОГО кода; пятая неверная попытка
    /// аннулирует код (usedAt = now, domain_model: live → annulled).
    /// Отсутствующий или неживой код — отсутствие операции (попытки не считаются).
    /// </summary>
    Task IncrementAttemptsOnLiveAsync(Guid codeId);

    /// <summary>
    /// Гашение кода: UsedAt = now (успешный confirm). Отсутствующий или уже
    /// погашенный код — отсутствие операции (момент гашения сохраняется).
    /// </summary>
    Task MarkUsedAsync(Guid codeId);

    // ------------------------------------------------------------------
    // Reset-токены (IF-003, FR-013/FR-014, ASM-002)
    // ------------------------------------------------------------------

    /// <summary>
    /// Регистрация дайджеста при выдаче reset-токена (идентификатор записи — сам
    /// TokenHash). Повторный TokenHash — <see cref="InvalidOperationException"/>
    /// (коллизия SHA-256-дайджеста CSPRNG-токена невозможна и свидетельствует о
    /// дефекте).
    /// </summary>
    Task AddAsync(PasswordResetToken token);

    /// <summary>
    /// ЖИВЫЙ reset-токен по значению дайджеста (Ordinal) либо null: применённый
    /// (usedAt ≠ null) и истёкший (expiresAt ≤ now) токены не живы. Имя отличает
    /// поиск reset-токена от одноимённого поиска refresh-токена (C# не перегружает
    /// методы по типу возврата).
    /// </summary>
    Task<PasswordResetToken?> FindLiveResetByHashAsync(string resetTokenHash);

    /// <summary>
    /// Гашение reset-токена по дайджесту (предъявленный найденный неживой токен
    /// гасится — просроченный не может «ожить»): UsedAt = now, если ещё не погашен.
    /// Отсутствующий дайджест — отсутствие операции.
    /// </summary>
    Task MarkUsedAsync(string resetTokenHash);

    /// <summary>
    /// Гасит ВСЕ незагашенные reset-токены пользователя (успешный сброс пароля
    /// отзывает ранее выданные ссылки восстановления); уже погашенные сохраняют
    /// свой момент.
    /// </summary>
    Task ConsumeAllForUserAsync(Guid userId);
}
