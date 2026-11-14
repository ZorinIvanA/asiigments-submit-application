namespace LabsApp.IntegrationTests.B03.Infrastructure;

/// <summary>
/// Ручное управление cookie (ADR-015) для кейсов регистрации: перехват Set-Cookie
/// ответа. Нужен, потому что cookie-контейнер HttpClient поглощает Set-Cookie,
/// а TS-031 обязан проверить «Set-Cookie access_token и refresh_token» (FR-008).
/// </summary>
public static class ManualCookies
{
    /// <summary>Все значения Set-Cookie ответа (по одному элементу на заголовок).</summary>
    public static List<string> GetSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}
