using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B08.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B08.Scenarios;

/// <summary>
/// TS-062 «Logout при просроченном access и действующем refresh: 204, не 401»
/// (happy_path, FR-015, P0).
///
/// given: cookie access_token содержит JWT с exp в прошлом (доработка CR-003:
///        заявленное отклонение механизма given — вместо перевода
///        FakeTimeProvider после входа минтится просроченный access-JWT ключом
///        фикстуры, B08Host.MintExpiredAccessToken; наблюдаемое состояние
///        эквивалентно: cookie access_token содержит JWT той же структуры
///        sub/jti/iat/role, HS256 тем же ключом, exp в прошлом, валидатор хоста
///        его отвергает — предусловие фиксировано Assert'ом; refresh-.cookie
///        действительна); cookie refresh_token действительна.
/// when:  POST /api/v1/auth/logout; повторный POST /api/v1/auth/refresh.
/// then:  204 (не 401); обе cookie очищены (Max-Age=0); refresh отозван —
///        повторный refresh 401. FR-015 AC «Просроченный access при действующем
///        refresh».
/// </summary>
public sealed class Ts062_LogoutExpiredAccessTests : IClassFixture<B08WebAppFactory>
{
    private const string Login = "ts062-student";
    private const string Email = "ts062@lab.local";
    private const string FullName = "Студент ШестьдесятДва";

    private readonly B08WebAppFactory _factory;

    public Ts062_LogoutExpiredAccessTests(B08WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Logout_WithExpiredAccessButLiveRefresh_Returns204AndRevokesRefresh()
    {
        // given: сессия, затем access-JWT переведён в просроченный (exp в прошлом).
        using var client = B08Host.CreateClient(_factory);
        var user = B08Host.SeedStudent(_factory, Login, Email, FullName);
        var grant = B08Host.EstablishSession(_factory, client, user.Id, UserRoles.Student);

        var expiredAccess = B08Host.MintExpiredAccessToken(_factory, user.Id, UserRoles.Student);
        var tokens = _factory.Services.GetRequiredService<ITokenService>();
        Assert.True(
            tokens.ValidateAccessToken(expiredAccess) is null,
            "Предусловие: просроченный access-JWT отвергается валидатором хоста.");
        client.Cookies.Set(AuthCoreDefaults.AccessTokenCookieName, expiredAccess);

        // when: POST /api/v1/auth/logout с просроченным access и действующим refresh.
        using var logout = await client.PostAsync(B08Host.LogoutEndpoint, json: null);

        // then: 204 (не 401).
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // then: обе cookie очищены (Max-Age=0).
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.AccessTokenCookieName);
        ResponseAssertions.AssertCookieClearedByMaxAgeZero(logout, AuthCoreDefaults.RefreshTokenCookieName);

        // then: refresh отозван.
        var stored = B08Host.FindRefreshRecordByHash(_factory, grant.TokenHash);
        Assert.NotNull(stored);
        Assert.True(
            stored.RevokedAt is not null,
            $"Ожидался отзыв refresh (RevokedAt != null), фактически RevokedAt={stored.RevokedAt?.ToString() ?? "null"}.");

        // then: повторный POST /auth/refresh даёт 401.
        var repeatRequest = new HttpRequestMessage(HttpMethod.Post, B08Host.RefreshEndpoint);
        repeatRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCoreDefaults.RefreshTokenCookieName}={grant.Value}");
        using var refresh = await client.SendAsync(repeatRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
