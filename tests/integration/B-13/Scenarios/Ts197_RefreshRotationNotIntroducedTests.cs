using LabsApp.Auth;
using LabsApp.IntegrationTests.B13.Infrastructure;

namespace LabsApp.IntegrationTests.B13.Scenarios;

/// <summary>
/// TS-197 (P2, scope; FR-009) «Scope: ротация refresh не введена».
/// Ротация refresh и reuse-detection не реализуются (out_of_scope, ASM-003).
///
/// given: Пользователь с валидным refresh-токеном; значение зафиксировано
///        (DI-сид студента + CreateRefreshToken с записью хэша в
///        IRefreshTokenRepository — ADR-022, POST /auth/login не используется).
/// when:  Три последовательных POST /auth/refresh с одним и тем же
///        refresh-cookie.
/// then:  Все — 204; значение refresh-токена в хранилище и cookie не меняется
///        (refresh_token ни в одном ответе не переустанавливается; запись по
///        исходному хэшу жива и неизменна — revokedAt не появился, expiresAt
///        прежний).
/// </summary>
public sealed class Ts197_RefreshRotationNotIntroducedTests(B13WebAppFactory factory) : IClassFixture<B13WebAppFactory>
{
    private readonly B13WebAppFactory _factory = factory;

    [Fact]
    public async Task TS197_ThreeRefreshesWithSameCookie_All204_RefreshValueUnchanged()
    {
        // given: пользователь с валидным refresh-токеном; значение зафиксировано.
        var user = B13Seed.AddStudent(_factory, "ts197-student", "Студент СемьдесятСемь", groupId: null);
        using var client = B13ScopeHost.CreateClient(_factory);
        var grant = B13ScopeHost.EstablishRefreshSession(_factory, user.Id);

        var storedBefore = B13ScopeHost.FindStoredRefreshToken(_factory, grant);
        Assert.NotNull(storedBefore);
        Assert.Null(storedBefore.RevokedAt);
        var expiresAtBefore = storedBefore.ExpiresAt;

        // when: три последовательных POST /auth/refresh с одним и тем же refresh-cookie.
        var responses = new List<HttpResponseMessage>(3);
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                responses.Add(await B13ScopeHost.PostRefreshWithRawCookieAsync(client, grant.Value));
            }

            // then: все — 204.
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            // then: cookie не меняется — refresh_token ни в одном ответе не
            // переустанавливается (ротация не введена, out_of_scope ASM-003).
            foreach (var response in responses)
            {
                var setCookieNames = B13ScopeHost.ReadSetCookieNames(response);
                Assert.True(
                    !setCookieNames.Contains(AuthCoreDefaults.RefreshTokenCookieName),
                    $"Ротация refresh не введена: ответ переустанавливает refresh_token (Set-Cookie: [{string.Join(", ", setCookieNames)}]).");
            }

            // then: значение refresh-токена в хранилище не меняется — запись по
            // исходному хэшу найдена (не заменена новым токеном), жива
            // (revokedAt не появился) и неизменна (expiresAt прежний).
            var storedAfter = B13ScopeHost.FindStoredRefreshToken(_factory, grant);
            Assert.NotNull(storedAfter);
            Assert.Null(storedAfter.RevokedAt);
            Assert.Equal(expiresAtBefore, storedAfter.ExpiresAt);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
