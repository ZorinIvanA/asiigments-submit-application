using System.Diagnostics;
using System.Security.Claims;
using LabsApp.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LabsApp.Observability;

/// <summary>
/// Реализация ISecurityEventLogger (IF-016, аменда CR-002/ADR-034): DI-singleton,
/// категория записей — единая с ObservabilityMiddleware константа «Api.Security»
/// (отдельная константа «Security» упразднена). Каждая запись — Warning с фактом
/// операции, traceId (Activity.Current, иначе TraceIdentifier запроса — как в
/// ObservabilityMiddleware) и subject=login аутентифицированного пользователя,
/// если известен (анонимный logout — без subject). HttpContext берётся из
/// IHttpContextAccessor (регистрация — AddSecurityEventLogging). Состав записей
/// ограничен фактами: пароли, значения/хэши токенов, коды и тела запросов в
/// записи не попадают (маркерная проверка NFR-006).
/// </summary>
public sealed class SecurityEventLogger : ISecurityEventLogger
{
    /// <summary>
    /// Категория записей событий безопасности — ЕДИНАЯ с ObservabilityMiddleware
    /// (ADR-034: одиночная константа «Api.Security»).
    /// </summary>
    public const string LogCategory = ObservabilityMiddleware.SecurityLogCategory;

    private readonly ILogger _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SecurityEventLogger(ILoggerFactory loggerFactory, IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _logger = loggerFactory.CreateLogger(LogCategory);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Отзыв refresh-токена(ов): Warning с reason (logout | password_change |
    /// password_reset), traceId и subject, если известен. Значения/хэши токенов
    /// не передаются и не логируются (NFR-006).
    /// </summary>
    public void LogRefreshTokenRevoked(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var (traceId, subject) = CurrentRequestFacts();
        if (subject is null)
        {
            _logger.LogWarning(
                "Отзыв refresh-токена; reason={Reason}; traceId={TraceId}",
                reason,
                traceId);
        }
        else
        {
            _logger.LogWarning(
                "Отзыв refresh-токена; reason={Reason}; traceId={TraceId}; subject={Subject}",
                reason,
                traceId,
                subject);
        }
    }

    /// <summary>Факт успешной смены пароля (PUT /me/password) — Warning.</summary>
    public void LogPasswordChanged() =>
        LogFact("Смена пароля");

    /// <summary>Факт успешного сброса пароля (POST /auth/reset-password) — Warning.</summary>
    public void LogPasswordReset() =>
        LogFact("Сброс пароля");

    /// <summary>Единый шаблон записи факта — без секретов, subject опционален.</summary>
    private void LogFact(string fact)
    {
        var (traceId, subject) = CurrentRequestFacts();
        if (subject is null)
        {
            _logger.LogWarning("{Fact}; traceId={TraceId}", fact, traceId);
        }
        else
        {
            _logger.LogWarning("{Fact}; traceId={TraceId}; subject={Subject}", fact, traceId, subject);
        }
    }

    /// <summary>
    /// traceId и subject текущего запроса (зеркало ObservabilityMiddleware):
    /// traceId — Activity.Current, иначе TraceIdentifier HttpContext (вне запроса —
    /// пустая строка); subject — claim login аутентифицированного пользователя.
    /// </summary>
    private (string TraceId, string? Subject) CurrentRequestFacts()
    {
        var context = _httpContextAccessor.HttpContext;
        var traceId = Activity.Current?.TraceId.ToString() is { Length: > 0 } activityTraceId
            ? activityTraceId
            : context?.TraceIdentifier ?? string.Empty;
        var user = context?.User;
        var subject = user?.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(AuthCoreDefaults.LoginClaimType)
            : null;
        return (traceId, subject);
    }
}
