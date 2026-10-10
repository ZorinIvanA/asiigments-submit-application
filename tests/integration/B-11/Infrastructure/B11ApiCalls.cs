using System.Text;

namespace LabsApp.IntegrationTests.B11.Infrastructure;

/// <summary>
/// Универсальный отправитель HTTP-запросов матрицы ролей (TS-126..TS-128, TS-155):
/// произвольный метод × путь × опциональное JSON-тело. Клиенты — без
/// авто-редиректов и cookie-контейнера (HostClients), сессия — заголовок Cookie.
/// </summary>
public static class B11ApiCalls
{
    /// <summary>Отправить запрос с методом method на path; при json ≠ null — JSON-тело.</summary>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? json = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }
}
