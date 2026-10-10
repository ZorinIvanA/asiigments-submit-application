using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-065 «Повторный logout с прежними значениями cookie: 204 (идемпотентность)»
/// (idempotency, FR-015, P2).
///
/// given: logout уже выполнен; клиент повторно отправляет прежние (уже
///        отозванные) значения обеих cookie.
/// when:  POST /api/v1/auth/logout с теми же cookie.
/// then:  204; cookie снова очищаются (обе Set-Cookie с Max-Age=0).
///        FR-015: «при наличии любой из cookie … эндпоинт выполняется» —
///        повторный выход не ломается.
/// </summary>
public sealed class Ts065_LogoutRepeatedIdempotentTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts065-student";
    private const string Email = "ts065@lab.local";
    private const string FullName = "Студент ШестьдесятПять";

    private readonly B08WebAppFactory _factory;

    public Ts065_LogoutRepeatedIdempotentTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatedLogout_WithSameAlreadyRevokedCookies_Returns204AndClearsAgain()
    {
        // given: сессия; прежние (ещё не отозванные) значения обеих cookie зафиксированы.
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        B08Host.EstablishSession(_factory, client, user.Id, UserRoles.Student);

        var previousAccess = client.Cookies.GetValue(AuthCoreDefaults.AccessTokenCookieName);
        var previousRefresh = client.Cookies.GetValue(AuthCoreDefaults.RefreshTokenCookieName);
        Assert.NotNull(previousAccess);
        Assert.NotNull(previousRefresh);

        // given: logout уже выполнен (204).
        using var first = await client.PostAsync(B08Host.LogoutEndpoint, json: null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // when: POST /auth/logout с теми же (уже отозванными) значениями cookie —
        // значения вписываются в запрос напрямую, контейнер после первого logout пуст
        // (Set-Cookie Max-Age=0 удалил cookie по механике ручного контейнера).
        var repeatRequest = new HttpRequestMessage(HttpMethod.Post, B08Host.LogoutEndpoint);
        repeatRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={previousAccess}; {AuthCoreDefaults.RefreshTokenCookieName}={previousRefresh}");
        using var second = await client.SendAsync(repeatRequest);

        // then: 204; cookie снова очищаются.
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(second, AuthCoreDefaults.AccessTokenCookieName);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(second, AuthCoreDefaults.RefreshTokenCookieName);
    }
}
