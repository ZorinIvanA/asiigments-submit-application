using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace LabsApp.Observability;

/// <summary>
/// Единый ObservabilityMiddleware (ADR-017, C-011): request-логирование и метрики
/// на уровне HTTP-конвейера. Регистрируется ПЕРВЫМ middleware в Program.cs (до
/// маппинга контроллеров), замеряет длительность (Stopwatch) и после выполнения
/// нижележащего конвейера фиксирует финальный статус ответа:
/// — каждый запрос /api/** и /health → запись Api.Request (Information: method,
///   path — строго HttpContext.Request.Path без query, status, durationMs,
///   traceId) + гистограмма api_request_duration_ms{route, method, status};
///   метка route — шаблон маршрута из context.GetEndpoint() (маршрутизация
///   выполняется внутри _next), без endpoint — конкретный path;
/// — /health → дополнительно гистограмма health_duration_ms.
/// В записях нет query, заголовков, паролей, токенов, кодов и PII
/// (NFR-005/NFR-008): path — строго Request.Path.
/// </summary>
public sealed class ObservabilityMiddleware
{
    /// <summary>Имя Meter метрик наблюдаемости (ADR-017); DI-singleton в Program.cs.</summary>
    public const string MeterName = "labs.api";

    /// <summary>Категория записей request-лога.</summary>
    public const string RequestLogCategory = "Api.Request";

    private const string RouteTag = "route";
    private const string MethodTag = "method";
    private const string StatusTag = "status";

    private readonly RequestDelegate _next;
    private readonly ILogger _requestLogger;
    private readonly Histogram<double> _apiRequestDuration;
    private readonly Histogram<double> _healthDuration;

    public ObservabilityMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, Meter meter)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(meter);

        _next = next;
        _requestLogger = loggerFactory.CreateLogger(RequestLogCategory);
        _apiRequestDuration = meter.CreateHistogram<double>("api_request_duration_ms");
        _healthDuration = meter.CreateHistogram<double>("health_duration_ms");
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

    private static string GetTraceId(HttpContext context) =>
        Activity.Current?.TraceId.ToString() is { Length: > 0 } traceId
            ? traceId
            : context.TraceIdentifier;
}

/// <summary>
/// Регистрация ObservabilityMiddleware первым middleware конвейера (ADR-017):
/// вызывается в Program.cs до маппинга контроллеров.
/// </summary>
public static class ObservabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app) =>
        app.UseMiddleware<ObservabilityMiddleware>();
}
