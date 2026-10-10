using LabsApp.Auth;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-056 «Успешный refresh: новый access, refresh не меняется и не ротируется»
/// (happy_path, FR-009, P0).
///
/// given: действующий refresh после входа.
/// when:  POST /api/v1/auth/refresh.
/// then:  204 без тела (FR-009/ASM-016/ADR-010: refresh отвечает NoContent);
///        Set-Cookie access_token — новый JWT с exp больше прежнего;
///        refresh_token в ответе не переустанавливается (то же значение cookie;
///        expiresAt записи в хранилище неизменен). ASM-004.
/// </summary>
public sealed class Ts056_RefreshSuccessNoRotationTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts056_RefreshSuccessNoRotationTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshIssuesNewAccess_KeepsRefreshValueAndStoredExpiry()
    {
        // given: действующий refresh после входа (cookie читаются из Set-Cookie).
        using var client = B07AuthClients.CreateClientWithoutCookies(_factory);
        using var login = await B07AuthClients.PostLoginAsync(client, "teacher", B07AuthWebAppFactory.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var access1 = B07AuthClients.RequiredSetCookie(login, AuthCoreDefaults.AccessTokenCookieName);
        var refresh1 = B07AuthClients.RequiredSetCookie(login, AuthCoreDefaults.RefreshTokenCookieName);
        var expiresAtAfterLogin = B07AuthClients.FindRefreshToken(_factory, refresh1).ExpiresAt;

        // Детерминизм «exp больше прежнего»: бизнес-время сдвинуто на минуту,
        // поэтому exp нового access строго больше exp прежнего; продление TTL
        // refresh-записи при refresh дало бы другой expiresAt в хранилище.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        // when: POST /auth/refresh с той же refresh-cookie.
        using var request = new HttpRequestMessage(HttpMethod.Post, B07AuthClients.RefreshEndpoint);
        request.Headers.TryAddWithoutValidation("Cookie", $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh1}");
        using var refresh = await client.SendAsync(request);

        // then: 204 без тела; Set-Cookie access_token — новый JWT с exp больше прежнего.
        Assert.Equal(HttpStatusCode.NoContent, refresh.StatusCode);
        Assert.Null(refresh.Content.Headers.ContentType);

        var access2 = B07AuthClients.RequiredSetCookie(refresh, AuthCoreDefaults.AccessTokenCookieName);
        Assert.NotEqual(access1, access2);
        Assert.True(
            B07AuthClients.ReadAccessJwtExp(access2) > B07AuthClients.ReadAccessJwtExp(access1),
            "exp нового access-JWT обязан быть больше прежнего.");

        // then: refresh_token в ответе не меняется — Set-Cookie refresh_token нет.
        Assert.False(
            B07AuthClients.ReadSetCookies(refresh).ContainsKey(AuthCoreDefaults.RefreshTokenCookieName),
            "refresh не ротируется: refresh_token в ответе переустанавливаться не должен (ASM-004).");

        // then: прежнее значение refresh продолжает действовать (то же значение cookie).
        using var repeat = new HttpRequestMessage(HttpMethod.Post, B07AuthClients.RefreshEndpoint);
        repeat.Headers.TryAddWithoutValidation("Cookie", $"{AuthCoreDefaults.RefreshTokenCookieName}={refresh1}");
        using var repeatResponse = await client.SendAsync(repeat);
        Assert.Equal(HttpStatusCode.NoContent, repeatResponse.StatusCode);

        // then: expiresAt записи в хранилище неизменен (TTL не продлевается).
        var stored = B07AuthClients.FindRefreshToken(_factory, refresh1);
        Assert.Equal(expiresAtAfterLogin, stored.ExpiresAt);
        Assert.Null(stored.RevokedAt);
    }
}
