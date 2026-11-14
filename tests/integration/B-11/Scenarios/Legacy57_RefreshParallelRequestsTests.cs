using LabsApp.Auth;
using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy57 (P1, concurrency; FR-009) «Refresh: параллельные запросы — все 204
/// (идемпотентность без ротации)». Кейс СТАРОГО реестра зоны (бывший TS-057),
/// сохранён вне актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под
/// однозначным ID LegacyNN — новый батч параллельные refresh-запросы не покрывает
/// (CR-001 раунда 2026-10-10); последовательный повтор с той же cookie покрывается
/// финальной проверкой этого кейса (бывший TS-053 старого реестра удалён как дубль).
/// given: Валидная refresh-cookie.
/// when:  5 параллельных POST /auth/refresh с этой cookie.
/// then:  Все 5 — 204; каждый выпускает новый access; refresh не ротируется,
///        ошибок и исключений нет (FR-009: «параллельные refresh-запросы
///        допустимы и все получают 204»).
/// </summary>
public sealed class Legacy57_RefreshParallelRequestsTests(B11WebAppFactory factory) : IClassFixture<B11WebAppFactory>
{
    private const int ParallelRequests = 5;

    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy57_Refresh_FiveParallelRequestsWithSameCookie_AllNoContentWithoutRotation()
    {
        // given: валидная refresh-cookie (вход сид-преподавателя).
        using var client = HostClients.Create(_factory);
        using var login = await HostClients.LoginAsync(client, "teacher", "teacher123!");
        _ = await ApiAssert.ReadOkJsonAsync(login);

        var refreshValue = HostClients.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName)
            ?? throw new InvalidOperationException(
                "Вход не вернул Set-Cookie refresh_token — given кейса неисполним.");

        // when: 5 параллельных POST /auth/refresh с этой cookie (отдельное сообщение
        // на запрос с собственным заголовком Cookie; исключение любого запроса
        // уронило бы Task.WhenAll — «ошибок и исключений нет» проверяется фактом).
        var requests = Enumerable.Range(0, ParallelRequests)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, HostClients.RefreshPath);
                SetRequestCookie(request, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
                return request;
            })
            .ToArray();
        var responses = await Task.WhenAll(requests.Select(request => client.SendAsync(request)));

        // then: все 5 — 204; каждый выпускает новый access (Set-Cookie access_token);
        // refresh не ротируется (Set-Cookie refresh_token нет ни в одном ответе).
        try
        {
            Assert.All(responses, response =>
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.True(
                    HostClients.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
                    "Параллельный refresh не выпустил Set-Cookie access_token.");
                Assert.False(
                    HostClients.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
                    "Параллельный refresh не должен переустанавливать/ротировать refresh_token.");
            });
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // then: refresh не ротирован — исходная cookie по-прежнему действительна
        // (не отозвана и не потреблена параллельными запросами): последовательный
        // POST /auth/refresh с тем же значением — снова 204.
        HostClients.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshValue);
        using var sequential = await HostClients.PostWithoutBodyAsync(client, HostClients.RefreshPath);
        Assert.Equal(HttpStatusCode.NoContent, sequential.StatusCode);
    }

    /// <summary>Заголовок Cookie запроса = единственная пара name=value.</summary>
    private static void SetRequestCookie(HttpRequestMessage request, string name, string value) =>
        request.Headers.Add("Cookie", $"{name}={value}");
}
