namespace LabsApp.IntegrationTests.B02.Infrastructure;

public sealed record HealthProbe(HttpStatusCode StatusCode, string Body);

/// <summary>
/// HTTP-пробы живого процесса на 127.0.0.1 (процессные кейсы батча: TS-136):
/// отличает «соединение отклонено / не отвечает» от реального ответа приложения.
/// </summary>
public static class HttpProbes
{
    public static async Task<HealthProbe?> ProbeHealthAsync(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(HealthUri(port));
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            return new HealthProbe(response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Признак отказа соединения с /health: возвращает исключение, если соединение
    /// НЕ удалось установить (порт не прослушивается), и null, если сервер ответил
    /// чем угодно (любой статус — порт обслуживается; CR-001: семантика различает
    /// «порт не прослушивается» и «сервер ответил»).
    /// </summary>
    public static async Task<Exception?> ProbeHealthFailureAsync(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // Любой HTTP-ответ (включая не-200) — запрос дошёл до приложения:
            // порт обслуживается, отказа соединения нет.
            using var response = await client.GetAsync(HealthUri(port));
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return exception;
        }
    }

    private static Uri HealthUri(int port) => new($"http://127.0.0.1:{port}/health", UriKind.Absolute);
}
