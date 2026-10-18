using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Domain.Validation;
using LabsApp.Hosting.Configuration;
using LabsApp.IntegrationTests.B12.Infrastructure;
using LabsApp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B12.Scenarios;

/// <summary>
/// TS-058 (P0, happy_path; FR-010) «Logout: отзыв refresh и сброс cookie».
/// given: Пользователь вошёл (оба cookie).
/// when:  POST /api/v1/auth/logout.
/// then:  204; Set-Cookie access_token и refresh_token с Max-Age=0; refresh-токен
///        отозван — последующий POST /auth/refresh с прежним значением — 401
///        (FR-010 AC «Выход с валидной сессией»).
/// </summary>
public sealed class Ts058_LogoutRevokesRefreshAndClearsCookiesTests(B12WebAppFactory factory)
    : IClassFixture<B12WebAppFactory>
{
    private readonly B12WebAppFactory _factory = factory;

    [Fact]
    public async Task Logout_NoContent_ClearsBothCookies_RevokesRefresh()
    {
        // given: пользователь вошёл (оба cookie) — сессия минтуется через DI (ADR-015).
        var teacher = _factory.Services.GetRequiredService<IUserRepository>()
                .GetByLogin(SeedOptions.DefaultTeacherLogin)
            ?? throw new InvalidOperationException("Сид-преподаватель не найден — given неисполним.");
        var accessToken = _factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(teacher.Id, UserRoles.Teacher);
        var refreshToken = B12AuthSessions.CreateLiveRefreshToken(_factory, teacher.Id);

        using var client = B12AuthSessions.CreateClient(_factory);

        // when: POST /auth/logout с обоими cookie.
        using var request = B12AuthSessions.CreateLogoutRequest(accessToken, refreshToken);
        using var response = await client.SendAsync(request);

        // then: 204; оба Set-Cookie присутствуют и содержат Max-Age=0.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(
            B12AuthSessions.TryGetSetCookie(response, AuthCoreDefaults.AccessTokenCookieName, out var accessClear),
            "Ожидается сброс cookie access_token (Set-Cookie Max-Age=0).");
        Assert.Contains("max-age=0", accessClear!, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            B12AuthSessions.TryGetSetCookie(response, AuthCoreDefaults.RefreshTokenCookieName, out var refreshClear),
            "Ожидается сброс cookie refresh_token (Set-Cookie Max-Age=0).");
        Assert.Contains("max-age=0", refreshClear!, StringComparison.OrdinalIgnoreCase);

        // then: refresh-токен отозван — последующий POST /auth/refresh с прежним
        // значением — 401 «Не авторизован».
        using var refreshRequest = B12AuthSessions.CreateRefreshRequest(refreshToken);
        using var refreshResponse = await client.SendAsync(refreshRequest);
        await ApiAssert.AssertMessageAsync(
            refreshResponse,
            HttpStatusCode.Unauthorized,
            ErrorTexts.Unauthorized);
    }
}
