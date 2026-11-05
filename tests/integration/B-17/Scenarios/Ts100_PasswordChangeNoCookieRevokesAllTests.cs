using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B17.Infrastructure;

namespace LabsApp.IntegrationTests.B17.Scenarios;

/// <summary>
/// TS-100 «Смена пароля: без refresh-cookie отзываются все токены»
/// (negative, FR-016, P0).
///
/// given: пользователь авторизован только access-cookie (сессия — минтованный
///        access-cookie, ADR-015); в хранилище есть его refresh-токены (два
///        DI-сид-токена «устройств»).
/// when:  PUT /me/password с верным currentPassword (без refresh-cookie в
///        запросе).
/// then:  204; все refresh-токены пользователя отозваны — каждый последующий
///        /auth/refresh — 401 (FR-016 AC «Смена без refresh-cookie отзывает
///        все»; IF-013: cookie нет — отзываются все).
/// </summary>
public sealed class Ts100_PasswordChangeNoCookieRevokesAllTests : IClassFixture<B17WebAppFactory>
{
    private const string Login = "ts100";
    private const string Email = "ts100@x.ru";

    private readonly B17WebAppFactory _factory;

    public Ts100_PasswordChangeNoCookieRevokesAllTests(B17WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithoutRefreshCookie_RevokesEveryRefreshToken()
    {
        // given: пользователь; в хранилище два его refresh-токена «устройств».
        var user = B17ProfileHost.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-100",
            role: UserRoles.Student,
            groupId: null,
            password: B17ProfileHost.TestUserPassword);
        var firstDeviceRefresh = B17ProfileHost.SeedRefreshToken(_factory, user.Id);
        var secondDeviceRefresh = B17ProfileHost.SeedRefreshToken(_factory, user.Id);

        // given: в запросе ТОЛЬКО access-cookie (refresh-cookie нет).
        using var client = B17ProfileHost.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: успешная смена пароля с верным currentPassword.
        using var response = await client.PutAsJsonAsync(B17ProfileHost.PasswordEndpoint, new
        {
            currentPassword = B17ProfileHost.TestUserPassword,
            password = B17ProfileHost.NewPassword,
            confirmPassword = B17ProfileHost.NewPassword,
        });

        // then: 204.
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: каждый последующий /auth/refresh — 401 (отозваны ВСЕ токены).
        using var firstRefreshClient = B17ProfileHost.Create(_factory);
        using var firstRefresh = await B17ProfileHost.RefreshAsync(firstRefreshClient, firstDeviceRefresh);
        Assert.True(
            firstRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh первого токена (отозван), фактически {(int)firstRefresh.StatusCode}: {await firstRefresh.Content.ReadAsStringAsync()}");

        using var secondRefreshClient = B17ProfileHost.Create(_factory);
        using var secondRefresh = await B17ProfileHost.RefreshAsync(secondRefreshClient, secondDeviceRefresh);
        Assert.True(
            secondRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго токена (отозван), фактически {(int)secondRefresh.StatusCode}: {await secondRefresh.Content.ReadAsStringAsync()}");
    }
}
