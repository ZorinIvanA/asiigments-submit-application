using LabsApp.Auth;
using LabsApp.Hosting.Configuration;

namespace LabsApp.IntegrationTests.B02.Infrastructure;

/// <summary>
/// HTTP-помощники входа кейсов сида: POST /api/v1/auth/login и извлечение access_token
/// из Set-Cookie ответа (сессия — в access-cookie, FR-008; CR-002: создание клиентов
/// — единое место, фабрика B02WebAppFactory).
/// </summary>
public static class HostClients
{
    /// <summary>POST /api/v1/auth/login — вход по кейсам сида TS-137/TS-138 (FR-025).</summary>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { login, password });

    public static Task<HttpResponseMessage> LoginAsDefaultTeacherAsync(HttpClient client) =>
        LoginAsync(client, SeedOptions.DefaultTeacherLogin, SeedOptions.DefaultTeacherPassword);

    /// <summary>
    /// Извлекает значение access_token из заголовка Set-Cookie ответа входа
    /// (cookie HttpOnly, поэтому чтение — именно из заголовков ответа, FR-008).
    /// </summary>
    public static string? ExtractAccessToken(HttpResponseMessage loginResponse)
    {
        if (!loginResponse.Headers.Contains("Set-Cookie"))
        {
            return null;
        }

        foreach (var setCookie in loginResponse.Headers.GetValues("Set-Cookie"))
        {
            var prefix = AuthCoreDefaults.AccessTokenCookieName + "=";
            if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var firstSegment = setCookie.Split(';', 2)[0];
            return firstSegment[prefix.Length..];
        }

        return null;
    }
}
