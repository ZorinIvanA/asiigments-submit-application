using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B16.Infrastructure;

namespace LabsApp.IntegrationTests.B16.Scenarios;

/// <summary>
/// TS-095 «Смена пароля: без refresh-cookie отзываются все» (negative, FR-016,
/// P1; нумерация текущего батча B-16).
///
/// given: пользователь авторизован — в запросе только access-cookie,
///        refresh-cookie отсутствует; в хранилище есть его refresh-токены
///        (два DI-сид-токена «устройств»).
/// when:  PUT /me/password с верным currentPassword и валидным новым паролем;
///        затем POST /auth/refresh с каждым его refresh.
/// then:  204; все refresh-токены пользователя отозваны (каждый refresh — 401)
///        (AC FR-016 «Смена без refresh-cookie отзывает все»; IF-013: cookie
///        нет — отзываются все).
/// </summary>
[Collection(SerialBuildAndProcessCollection.Name)]
public sealed class Ts095_PasswordChangeWithoutCookieRevokesAllTests : IClassFixture<B16WebAppFactory>
{
    private const string Login = "ts095pwd";
    private const string Email = "ts095pwd@x.ru";
    private const string OldPassword = "OldPass1!";
    private const string NewPassword = "NewPass1!";

    private readonly B16WebAppFactory _factory;

    public Ts095_PasswordChangeWithoutCookieRevokesAllTests(B16WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithoutRefreshCookie_RevokesEveryRefreshToken()
    {
        // given: пользователь; в хранилище два его refresh-токена.
        var user = B16Harness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-095",
            role: UserRoles.Student,
            groupId: null,
            password: OldPassword);
        var firstDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);
        var secondDeviceRefresh = B16Harness.SeedRefreshToken(_factory, user.Id);

        // given: в запросе ТОЛЬКО access-cookie (refresh-cookie отсутствует).
        using var client = B16Harness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: PUT с верным currentPassword и валидным новым паролем.
        using var response = await client.PutAsJsonAsync(B16Harness.PasswordEndpoint, new
        {
            currentPassword = OldPassword,
            password = NewPassword,
            confirmPassword = NewPassword,
        });

        // then: 204.
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: POST /auth/refresh с КАЖДЫМ его refresh — 401 (отозваны все).
        using var firstRefreshClient = B16Harness.Create(_factory);
        using var firstRefresh = await B16Harness.RefreshAsync(firstRefreshClient, firstDeviceRefresh);
        Assert.True(
            firstRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh первого токена (отозван), фактически {(int)firstRefresh.StatusCode}: {await firstRefresh.Content.ReadAsStringAsync()}");

        using var secondRefreshClient = B16Harness.Create(_factory);
        using var secondRefresh = await B16Harness.RefreshAsync(secondRefreshClient, secondDeviceRefresh);
        Assert.True(
            secondRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго токена (отозван), фактически {(int)secondRefresh.StatusCode}: {await secondRefresh.Content.ReadAsStringAsync()}");
    }
}
