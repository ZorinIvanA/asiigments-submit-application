namespace LabsApp.Observability;

/// <summary>
/// Журнал событий безопасности (IF-016, аменда CR-002/ADR-034): фиксирует факты
/// безопасности, которые статус HTTP-ответа не различает, — отзыв refresh-токенов
/// при logout/смене/сбросе пароля и факты смены/сброса пароля. Записи — Warning,
/// категория <see cref="SecurityEventLogger.LogCategory"/> — ЕДИНАЯ с
/// <see cref="ObservabilityMiddleware.SecurityLogCategory"/> константа «Api.Security»
/// (отказы 403/429 пишет сам <see cref="ObservabilityMiddleware"/> в ту же
/// категорию). Вызов — ТОЛЬКО при фактическом выполнении операции: идемпотентный
/// 204 без отзыва ничего не пишет. Состав записи — факт/reason, traceId,
/// subject=login аутентифицированного пользователя, если известен (logout допускает
/// отсутствие subject) — БЕЗ идентификаторов секретов: пароли, значения/хэши
/// refresh- и reset-токенов и коды в записях не появляются (маркерная проверка
/// NFR-006). Реализация — <see cref="SecurityEventLogger"/> (DI-singleton);
/// контроллеры зависят только от интерфейса.
/// </summary>
public interface ISecurityEventLogger
{
    /// <summary>
    /// Отзыв refresh-токена(ов): <paramref name="reason"/> — причина из
    /// <see cref="SecurityEventReasons"/> (logout | password_change |
    /// password_reset). Значения и хэши токенов не передаются и не логируются.
    /// </summary>
    void LogRefreshTokenRevoked(string reason);

    /// <summary>Факт успешной смены пароля (PUT /me/password, 204).</summary>
    void LogPasswordChanged();

    /// <summary>Факт успешного сброса пароля (POST /auth/reset-password, 204).</summary>
    void LogPasswordReset();
}

/// <summary>
/// Причины отзыва refresh-токенов (IF-016): значение попадает в запись
/// «Api.Security» — только константы, никакой интерполяции секретов.
/// </summary>
public static class SecurityEventReasons
{
    /// <summary>Выход пользователя (POST /auth/logout, фактический отзыв живого токена).</summary>
    public const string Logout = "logout";

    /// <summary>Смена пароля текущим пользователем (PUT /me/password).</summary>
    public const string PasswordChange = "password_change";

    /// <summary>Сброс пароля по reset-токену (POST /auth/reset-password).</summary>
    public const string PasswordReset = "password_reset";
}
