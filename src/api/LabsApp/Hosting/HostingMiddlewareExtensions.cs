using LabsApp.Hosting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LabsApp.Hosting;

/// <summary>
/// Middleware-части hosting-конвейера (C-001): 404-конверт для /api/** без маршрута,
/// блокировка /swagger* вне Development и SPA-fallback с защитными заголовками.
/// </summary>
public static class HostingMiddlewareExtensions
{
    /// <summary>
    /// FR-001/FR-023: запрос к /api/**, для которого не нашёлся маршрут (любой метод),
    /// получает 404 с Content-Type application/json и телом
    /// {'message':'Не найдено'} вместо SPA-fallback.
    /// Регистрируется после MapControllers; 404 от обработчиков эндпойнтов
    /// (endpoint != null) не перезаписывается — их тело формирует контроллер.
    /// </summary>
    public static IApplicationBuilder UseApiRouteNotFoundHandler(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next();

            if (context.Response.HasStarted
                || context.Response.StatusCode != StatusCodes.Status404NotFound
                || !context.Request.Path.StartsWithSegments("/api")
                || context.GetEndpoint() is not null)
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ErrorEnvelope { Message = ErrorTexts.NotFound });
        });

    /// <summary>
    /// FR-001/FR-002 (ISS-008): вне Development запросы /swagger/** получают
    /// 404-конверт {'message':'Не найдено'} до SPA-fallback — тело ответа
    /// никогда не является index.html. Регистрируется после обработки маршрутов
    /// (MapControllers) и до статики/SPA-fallback.
    /// </summary>
    public static IApplicationBuilder UseSwaggerBlockedOutsideDevelopment(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/swagger"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new ErrorEnvelope { Message = ErrorTexts.NotFound });
                return;
            }

            await next();
        });

    /// <summary>
    /// FR-002: SPA-fallback — GET-запрос по пути, не начинающемуся с /api, не равному
    /// /health, не начинающемуся с /swagger и не совпадающему с существующим файлом
    /// статики, возвращает index.html с HTTP 200 (deep-link и refresh SPA работают) и
    /// защитными заголовками SEC-006 (nosniff, DENY). Если wwwroot/index.html
    /// отсутствует (бэкенд без сборки клиента) — 404-конверт {'message':'Не найдено'}.
    /// Регистрируется после UseDefaultFiles/UseStaticFiles.
    /// </summary>
    public static IApplicationBuilder UseSpaFallback(this IApplicationBuilder app)
    {
        var environment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var isApiOrSystemServicePath = path.StartsWithSegments("/api")
                || path.StartsWithSegments("/swagger")
                || path == "/health";

            if (HttpMethods.IsGet(context.Request.Method)
                && !isApiOrSystemServicePath
                && !environment.WebRootFileProvider.GetFileInfo(path.Value?.TrimStart('/') ?? string.Empty).Exists)
            {
                var indexPage = environment.WebRootFileProvider.GetFileInfo("index.html");
                if (indexPage.Exists)
                {
                    SecurityHeaders.Apply(context.Response);
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.SendFileAsync(indexPage);
                    return;
                }

                // FR-002: маршрут, претендовавший на SPA-fallback, без index.html —
                // единый конверт вместо пустого 404 конца конвейера.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new ErrorEnvelope { Message = ErrorTexts.NotFound });
                return;
            }

            await next();
        });
    }
}
