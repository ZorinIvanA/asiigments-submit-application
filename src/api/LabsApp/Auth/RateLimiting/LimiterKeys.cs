namespace LabsApp.Auth.RateLimiting;

/// <summary>
/// Ключи прикладных лимитеров (IF-006): нормализация по решающему правилу —
/// нестрока/отсутствие → '', затем trim, нижний регистр (ci-правило глоссария),
/// усечение до границы поля (login 100, email 254). IP не усекается (адрес
/// соединения уже нормализован IClientIpResolver). Нормализация идемпотентна:
/// limiter применяет её сам, повторное применение значения не меняет.
/// </summary>
public static class LimiterKeys
{
    /// <summary>Ключ по IP: trim (нестрока/отсутствие → '').</summary>
    public static string FromIp(string? ip) => (ip ?? string.Empty).Trim();

    /// <summary>Ключ по логину: trim + lower + усечение до 100 символов.</summary>
    public static string FromLogin(string? login) => Normalize(login, AuthCoreDefaults.LoginKeyMaxLength);

    /// <summary>Ключ по email: trim + lower + усечение до 254 символов.</summary>
    public static string FromEmail(string? email) => Normalize(email, AuthCoreDefaults.EmailKeyMaxLength);

    private static string Normalize(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
