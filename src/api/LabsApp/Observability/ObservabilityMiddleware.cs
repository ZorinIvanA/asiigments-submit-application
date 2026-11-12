using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using LabsApp.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace LabsApp.Observability;

/// <summary>
/// Единый ObservabilityMiddleware (ADR-017, C-011): request-логирование, события
/// 403/429 и метрики на уровне HTTP-конвейера. Регистрируется ПЕРВЫМ middleware в
/// Program.cs (до аутентификации/авторизации и маппинга контроллеров), замеряет
/// длительность (Stopwatch) и после выполнения нижележащего конвейера фиксирует
/// финальный статус ответа:
/// — каждый запрос /api/** и /health → запись Api.Request (Information: method,
///   path — строго HttpContext.Request.Path без query, status, durationMs,
///   traceId) + гистограмма api_request_duration_ms{route, method, status};
///   метка route — шаблон маршрута из context.GetEndpoint() (маршрутизация
///   выполняется внутри _next), без endpoint — конкретный path;
/// — /health → дополнительно гистограмма health_duration_ms;
/// — финальный статус 403/429 → запись Api.Security (Warning: status, path без
///   query, traceId, subject=login аутентифицированного пользователя, если
///   известен; анонимно — субъект опускается) + счётчик
///   security_rejections_total{status};
/// — производные события входа и аудита — по фиксированной таблице
///   «маршрут × метод × финальный статус → событие» (ADR-017):
///   POST /api/v1/auth/login 200/401/429 → auth_login_attempts_total
///   {login_success|login_failure|rate_limited}; POST /api/v1/auth/logout 204 →
///   security_audit_events_total{op=logout}; PUT /api/v1/me/password 204 →
///   {op=password_change}; POST /api/v1/auth/reset-password 204 →
///   {op=password_reset}; POST /api/v1/auth/recovery/request 200 →
///   {op=recovery_code_requested} (отклонённый 429 запрос кода в op не попадает —
///   он учтён в security_rejections_total).
/// В записях нет query, заголовков, паролей, токенов, кодов и PII
/// (NFR-005/NFR-008): path — строго Request.Path; subject — только claim login
/// аутентифицированного HttpContext.User.
/// </summary>
public sealed class ObservabilityMiddleware
{
    /// <summary>Имя Meter метрик наблюдаемости (ADR-017); DI-singleton в Program.cs.</summary>
    public const string MeterName = "labs.api";

    /// <summary>Категория записей request-лога.</summary>
    public const string RequestLogCategory = "Api.Request";

    /// <summary>Категория записей об отказах доступа (403/429).</summary>
    public const string SecurityLogCategory = "Api.Security";

    // Таблица «маршрут × метод × финальный статус → событие» (ADR-017).
    private const string LoginPath = "/api/v1/auth/login";
    private const string LogoutPath = "/api/v1/auth/logout";
    private const string ChangePasswordPath = "/api/v1/me/password";
    private const string ResetPasswordPath = "/api/v1/auth/reset-password";
    private const string RecoveryRequestPath = "/api/v1/auth/recovery/request";

    private const string RouteTag = "route";
    private const string MethodTag = "method";
    private const string StatusTag = "status";
    private const string ResultTag = "result";
    private const string OpTag = "op";

    private const string LoginSuccess = "login_success";
    private const string LoginFailure = "login_failure";
    private const string LoginRateLimited = "rate_limited";

    private readonly RequestDelegate _next;
    private readonly ILogger _requestLogger;
    private readonly ILogger _securityLogger;
    private readonly Histogram<double> _apiRequestDuration;
    private readonly Histogram<double> _healthDuration;
    private readonly Counter<long> _securityRejections;
    private readonly Counter<long> _loginAttempts;
    private readonly Counter<long> _securityAuditEvents;

    public ObservabilityMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, Meter meter)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(meter);

        _next = next;
        _requestLogger = loggerFactory.CreateLogger(RequestLogCategory);
        _securityLogger = loggerFactory.CreateLogger(SecurityLogCategory);
        _apiRequestDuration = meter.CreateHistogram<double>("api_request_duration_ms");
        _healthDuration = meter.CreateHistogram<double>("health_duration_ms");
        _securityRejections = meter.CreateCounter<long>("security_rejections_total");
        _loginAttempts = meter.CreateCounter<long>("auth_login_attempts_total");
        _securityAuditEvents = meter.CreateCounter<long>("security_audit_events_total");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;
        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();
        RecordCompletedRequest(context, path, method, stopwatch.Elapsed.TotalMilliseconds);
    }

    private void RecordCompletedRequest(HttpContext context, PathString path, string method, double durationMs)
    {
        var status = context.Response.StatusCode;

        if (path.StartsWithSegments("/api") || path == "/health")
        {
            // Журнал — строго конкретный путь без query (контракт Api.Request).
            _requestLogger.LogInformation(
                "HTTP-запрос {Method} {Path} → {Status}; durationMs={DurationMs}; traceId={TraceId}",
                method,
                path.Value ?? string.Empty,
                status,
                durationMs,
                GetTraceId(context));
            // Метка route гистограммы — шаблон маршрута (ADR-017: «route — шаблон
            // пути без query»), а не конкретный путь: иначе каждая уникальная
            // комбинация параметров маршрута порождает новую серию метрики.
            _apiRequestDuration.Record(
                durationMs,
                new(RouteTag, GetRouteTemplateTag(context, path)),
                new(MethodTag, method),
                new(StatusTag, status));

            if (path == "/health")
            {
                _healthDuration.Record(durationMs);
            }
        }

        if (status is StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests)
        {
            RecordSecurityRejection(context, path, status);
        }

        RecordDerivedEvents(method, path, status);
    }

    private void RecordSecurityRejection(HttpContext context, PathString path, int status)
    {
        var route = path.Value ?? string.Empty;
        var traceId = GetTraceId(context);
        var subject = GetSubject(context.User);

        if (subject is null)
        {
            // Анонимный запрос — субъект не указывается (NFR-008: «login, если известен»).
            _securityLogger.LogWarning(
                "Отказ доступа {Status} {Path}; traceId={TraceId}",
                status,
                route,
                traceId);
        }
        else
        {
            _securityLogger.LogWarning(
                "Отказ доступа {Status} {Path}; traceId={TraceId}; subject={Subject}",
                status,
                route,
                traceId,
                subject);
        }

        _securityRejections.Add(1, new KeyValuePair<string, object?>(StatusTag, status));
    }

    private void RecordDerivedEvents(string method, PathString path, int status)
    {
        var route = path.Value ?? string.Empty;
        string? loginResult = null;
        string? auditOp = null;

        if (HttpMethods.IsPost(method))
        {
            if (route == LoginPath)
            {
                loginResult = status switch
                {
                    StatusCodes.Status200OK => LoginSuccess,
                    StatusCodes.Status401Unauthorized => LoginFailure,
                    StatusCodes.Status429TooManyRequests => LoginRateLimited,
                    _ => null,
                };
            }
            else if (route == LogoutPath && status == StatusCodes.Status204NoContent)
            {
                auditOp = "logout";
            }
            else if (route == ResetPasswordPath && status == StatusCodes.Status204NoContent)
            {
                auditOp = "password_reset";
            }
            else if (route == RecoveryRequestPath && status == StatusCodes.Status200OK)
            {
                auditOp = "recovery_code_requested";
            }
        }
        else if (HttpMethods.IsPut(method)
            && route == ChangePasswordPath
            && status == StatusCodes.Status204NoContent)
        {
            auditOp = "password_change";
        }

        if (loginResult is not null)
        {
            _loginAttempts.Add(1, new KeyValuePair<string, object?>(ResultTag, loginResult));
        }

        if (auditOp is not null)
        {
            _securityAuditEvents.Add(1, new KeyValuePair<string, object?>(OpTag, auditOp));
        }
    }

    /// <summary>
    /// Метка route гистограммы api_request_duration_ms (ADR-017: «шаблон пути без
    /// query»). Маршрутизация выполняется внутри _next, поэтому после его
    /// завершения endpoint доступен: для RouteEndpoint берётся шаблон маршрута
    /// (RawText, иначе PatternText), приводимый к единому виду с ведущим слэшем;
    /// для прочих endpoint — DisplayName; без endpoint (путь без маршрута, 404) —
    /// конкретный path запроса.
    /// </summary>
    private static string GetRouteTemplateTag(HttpContext context, PathString path)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is RouteEndpoint routeEndpoint)
        {
            // RawText — исходный текст шаблона (для RoutePattern, разобранного из
            // строки, всегда задан); null-значение трактуется как отсутствие шаблона.
            if (routeEndpoint.RoutePattern.RawText is { Length: > 0 } template)
            {
                return template.StartsWith('/') ? template : "/" + template;
            }
        }

        if (endpoint?.DisplayName is { Length: > 0 } displayName)
        {
            return displayName;
        }

        return path.Value ?? string.Empty;
    }

    private static string? GetSubject(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(AuthCoreDefaults.LoginClaimType)
            : null;

    private static string GetTraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId
            ? traceId
            : context.TraceIdentifier;
}

/// <summary>
/// Регистрация ObservabilityMiddleware первым middleware конвейера (ADR-017):
/// вызывается в Program.cs до аутентификации/авторизации и маппинга контроллеров.
/// </summary>
public static class ObservabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app) =>
        app.UseMiddleware<ObservabilityMiddleware>();
}
