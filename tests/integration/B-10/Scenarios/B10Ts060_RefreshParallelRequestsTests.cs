using LabsApp.Auth;
using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-060 (P1, concurrency; FR-009) «Refresh: параллельные запросы с одного токена
/// — все 204» (актуальная нумерация кейсов батча B-10; родственный тест предыдущей
/// нумерации — Ts057_RefreshParallelRequestsTests).
/// given: Один валидный refresh-токен; 3 параллельных клиента.
/// when:  3 одновременных POST /auth/refresh с одним токеном.
/// then:  Все 3 — 204 (идемпотентность без ротации); токен остаётся валиден
///        (AC FR-009: «Параллельные refresh-запросы допустимы и все получают
///        204»).
/// </summary>
public sealed class B10Ts060_RefreshParallelRequestsTests(B10TimedWebAppFactory factory)
    : IClassFixture<B10TimedWebAppFactory>
{
    private const string TestIp = "10.0.20.60";
    private const int ParallelClients = 3;

    private readonly B10TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS060_Refresh_ThreeParallelClientsWithSameToken_AllNoContentAndTokenStaysValid()
    {
        // given: один валидный refresh-токен (вход сид-преподавателя); 3
        // параллельных клиента без cookie-контейнеров.
        using var loginClient = B10AuthRequests.Create(_factory, TestIp);
        using var login = await B10AuthRequests.LoginAsync(loginClient, "teacher", B10AuthRequests.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B10AuthRequests.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");

        var clients = Enumerable.Range(0, ParallelClients)
            .Select(_ => B10AuthRequests.Create(_factory, TestIp))
            .ToArray();

        try
        {
            // when: 3 одновременных POST /auth/refresh с одним токеном (свой
            // HttpRequestMessage с заголовком Cookie у каждого клиента; исход
            // исключения любого запроса уронил бы Task.WhenAll — «допустимы и без
            // ошибок» проверяется фактом).
            var requests = clients
                .Select(client =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, B10CookieFlow.RefreshPath);
                    B10AuthRequests.SetRequestCookie(request, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);
                    return (client, request);
                })
                .ToArray();
            var responses = await Task.WhenAll(
                requests.Select(pair => pair.client.SendAsync(pair.request)));

            // then: все 3 — 204 (идемпотентность без ротации).
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));

            foreach (var pair in requests)
            {
                pair.request.Dispose();
            }

            foreach (var response in responses)
            {
                response.Dispose();
            }

            // then: токен остаётся валиден — последовательный refresh исходным
            // значением снова 204 (токен не потреблён и не отозван гонкой).
            B10AuthRequests.SetRequestCookie(
                loginClient, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);
            using var sequential = await B10AuthRequests.PostNoBodyAsync(loginClient, B10CookieFlow.RefreshPath);
            Assert.Equal(HttpStatusCode.NoContent, sequential.StatusCode);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }
}
