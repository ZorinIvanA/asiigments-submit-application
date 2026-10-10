using Microsoft.AspNetCore.Http;

namespace LabsApp.Hosting;

/// <summary>
/// Защитные заголовки ответов статики и SPA-fallback (FR-002/SEC-006):
/// X-Content-Type-Options: nosniff и X-Frame-Options: DENY. Применяется
/// в композиция-корне к UseStaticFiles и внутри SPA-fallback; полная
/// Content-Security-Policy — out_of_scope.
/// </summary>
public static class SecurityHeaders
{
    public const string XContentTypeOptions = "X-Content-Type-Options";

    public const string Nosniff = "nosniff";

    public const string XFrameOptions = "X-Frame-Options";

    public const string Deny = "DENY";

    /// <summary>Выставляет nosniff и DENY на ответ (статика и SPA-fallback — FR-002/SEC-006).</summary>
    public static void Apply(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers[XContentTypeOptions] = Nosniff;
        response.Headers[XFrameOptions] = Deny;
    }
}
