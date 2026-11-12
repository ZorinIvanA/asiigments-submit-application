using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-057 (P1, concurrency; FR-009) «Refresh: параллельные запросы — все 204
/// (идемпотентность без ротации)».
/// given: Валидная refresh-cookie.
/// when:  5 параллельных POST /auth/refresh с этой cookie.
/// then:  Все 5 — 204; каждый выпускает новый access; refresh не ротируется,
///        ошибок и исключений нет. FR-009: «параллельные refresh-запросы
///        допустимы и все получают 204».
/// </summary>
public sealed class Ts057_RefreshParallelRequestsTests(B09TimedWebAppFactory factory)
    : IClassFixture<B09TimedWebAppFactory>
{
    private const string TestIp = "10.0.0.57";
    private const int ParallelRequests = 5;

    private readonly B09TimedWebAppFactory _factory = factory;

    [Fact]
    public async Task TS057_Refresh_FiveParallelRequestsWithSameCookie_AllNoContentWithoutRotation()
    {
        // given: валидная refresh-cookie (вход сид-преподавателя).
        using var client = B09AuthHttp.Create(_factory, TestIp);
        using var login = await B09AuthHttp.LoginAsync(client, "teacher", B09AuthHttp.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshCookie = B09AuthHttp.SetCookieValue(login, AuthCoreDefaults.RefreshTokenCookieName);
        Assert.False(string.IsNullOrEmpty(refreshCookie), "Вход не выдал refresh-cookie — given неисполним.");

        // when: 5 параллельных POST /auth/refresh с этой cookie (отдельное сообщение
        // на запрос с собственным заголовком Cookie; исход исключения любого запроса
        // уронил бы Task.WhenAll — «ошибок и исключений нет» проверяется фактом).
        var requests = Enumerable.Range(0, ParallelRequests)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, B09AuthHttp.RefreshPath);
                B09AuthHttp.SetRequestCookie(request, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);
                return request;
            })
            .ToArray();
        var responses = await Task.WhenAll(requests.Select(request => client.SendAsync(request)));

        // then: все 5 — 204; каждый выпускает новый access (Set-Cookie access_token);
        // refresh не ротируется (Set-Cookie refresh_token нет ни в одном ответе).
        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(
                B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.AccessTokenCookieName),
                "Параллельный refresh не выпустил Set-Cookie access_token.");
            Assert.False(
                B09AuthHttp.HasSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName),
                "Параллельный refresh не должен переустанавливать/ротировать refresh_token.");
        });

        // then: исходная refresh-cookie по-прежнему действительна (не отозвана и не
        // потреблена параллельными запросами).
        B09AuthHttp.SetRequestCookie(client, AuthCoreDefaults.RefreshTokenCookieName, refreshCookie!);
        using var sequential = await B09AuthHttp.PostNoBodyAsync(client, B09AuthHttp.RefreshPath);
        Assert.Equal(HttpStatusCode.NoContent, sequential.StatusCode);
    }
}
