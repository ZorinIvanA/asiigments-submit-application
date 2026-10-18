using LabsApp.IntegrationTests.B10.Infrastructure;

namespace LabsApp.IntegrationTests.B10.Scenarios;

/// <summary>
/// TS-059 (P0, idempotency; FR-010) «Logout: без cookie — 204 (идемпотентность)».
/// given: Запрос не содержит cookie access_token и refresh_token.
/// when:  POST /auth/logout без cookie.
/// then:  204 без ошибок (FR-010 AC «Выход без cookie»): эндпойнт анонимен,
///        отсутствие refresh-cookie — отсутствие операции отзыва, ответ всегда 204.
/// </summary>
public sealed class Ts059_LogoutWithoutCookieTests(B10NoDemoWebAppFactory factory)
    : IClassFixture<B10NoDemoWebAppFactory>
{
    private readonly B10NoDemoWebAppFactory _factory = factory;

    [Fact]
    public async Task TS059_Logout_WithoutAnyCookie_Returns204NoContent()
    {
        // given: клиент без cookie-контейнера и без Cookie-заголовка.
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/auth/logout без cookie.
        using var response = await client.PostAsync(B10CookieFlow.LogoutPath, content: null);

        // then: 204 без ошибок.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
