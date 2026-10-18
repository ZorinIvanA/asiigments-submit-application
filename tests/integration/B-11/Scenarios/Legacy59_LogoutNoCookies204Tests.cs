using LabsApp.IntegrationTests.B11.Infrastructure;

namespace LabsApp.IntegrationTests.B11.Scenarios;

/// <summary>
/// Legacy59 (P0, idempotency; FR-010) «Logout: без cookie — 204
/// (идемпотентность)». Кейс СТАРОГО реестра зоны (бывший TS-059), сохранён вне
/// актуального реестра батча (TS-041..TS-050 / TS-056..TS-060) под однозначным
/// ID LegacyNN — logout новым батчем не покрывается (CR-001 раунда 2026-10-10).
/// given: Запрос не содержит cookie access_token и refresh_token.
/// when:  POST /api/v1/auth/logout без cookie.
/// then:  204 без ошибок (FR-010 AC «Выход без cookie»): идемпотентный выход —
///        отсутствие cookie не является отказом.
/// </summary>
public sealed class Legacy59_LogoutNoCookies204Tests(B11WebAppFactory factory)
    : IClassFixture<B11WebAppFactory>
{
    private readonly B11WebAppFactory _factory = factory;

    [Fact]
    public async Task Legacy59_Logout_WithoutCookies_Returns204()
    {
        // given: запрос не содержит cookie access_token и refresh_token.
        using var client = HostClients.Create(_factory);

        // when: POST /api/v1/auth/logout без cookie.
        using var response = await HostClients.PostWithoutBodyAsync(client, HostClients.LogoutPath);

        // then: 204 без ошибок (FR-010 AC «Выход без cookie»).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
