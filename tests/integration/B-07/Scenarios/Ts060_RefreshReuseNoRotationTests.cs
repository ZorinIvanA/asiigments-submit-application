using LabsApp.Auth;
using LabsApp.IntegrationTests.B07.Infrastructure;

namespace LabsApp.IntegrationTests.B07.Scenarios;

/// <summary>
/// TS-060 «Повторное использование refresh: ротация не реализована
/// (out_of_scope)» (scope, FR-009, P1).
///
/// given: действующий refresh; один POST /auth/refresh уже выполнен.
/// when:  второй и третий POST /auth/refresh с той же refresh-cookie; инспекция
///        ISecurityTokenRepository.
/// then:  каждый вызов → 204 без тела с новой access-cookie (FR-009/ASM-016/
///        ADR-010); прежний refresh продолжает работать; revokedAt не появился;
///        expiresAt неизменен (TTL не продлевается). Out_of_scope «Ротация и
///        reuse-detection refresh-токенов» — лишнее не реализовано.
/// </summary>
public sealed class Ts060_RefreshReuseNoRotationTests : IClassFixture<B07AuthWebAppFactory>
{
    private readonly B07AuthWebAppFactory _factory;

    public Ts060_RefreshReuseNoRotationTests(B07AuthWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatedRefreshWithSameCookie_Always200_NoRevocation_NoTtlProlongation()
    {
        // given: действующий refresh; один POST /auth/refresh уже выполнен.
        using var client = B07AuthClients.CreateClient(_factory);
        using var login = await B07AuthClients.PostLoginAsync(client, "teacher", B07AuthWebAppFactory.TeacherPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var refresh1 = B07AuthClients.RequiredSetCookie(login, AuthCoreDefaults.RefreshTokenCookieName);
        var expiresAtAfterLogin = B07AuthClients.FindRefreshToken(_factory, refresh1).ExpiresAt;

        using var first = await client.PostAsync(B07AuthClients.RefreshEndpoint, content: null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // when: второй и третий POST /auth/refresh с той же refresh-cookie;
        // время сдвинуто между вызовами — продление TTL дало бы другой expiresAt.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var second = await client.PostAsync(B07AuthClients.RefreshEndpoint, content: null);

        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        using var third = await client.PostAsync(B07AuthClients.RefreshEndpoint, content: null);

        // then: каждый вызов → 204 без тела с новой access-cookie (FR-009/ADR-010).
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, third.StatusCode);
        foreach (var refreshResponse in new[] { second, third })
        {
            Assert.True(
                B07AuthClients.ReadSetCookies(refreshResponse).ContainsKey(AuthCoreDefaults.AccessTokenCookieName),
                "Каждый успешный refresh обязан сопровождаться новой access-cookie.");
            Assert.False(
                B07AuthClients.ReadSetCookies(refreshResponse).ContainsKey(AuthCoreDefaults.RefreshTokenCookieName),
                "Ротация refresh не реализована (out_of_scope): refresh_token переустанавливаться не должен.");
        }

        // then: прежний refresh продолжает работать (2-й и 3-й вызовы — 204) —
        // reuse-detection отсутствует; revokedAt не появился.
        var stored = B07AuthClients.FindRefreshToken(_factory, refresh1);
        Assert.Null(stored.RevokedAt);

        // then: expiresAt неизменен (TTL не продлевается).
        Assert.Equal(expiresAtAfterLogin, stored.ExpiresAt);
    }
}
