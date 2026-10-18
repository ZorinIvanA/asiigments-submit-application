using LabsApp.Domain.Entities;
using LabsApp.IntegrationTests.B18Profile.Infrastructure;

namespace LabsApp.IntegrationTests.B18Profile.Scenarios;

/// <summary>
/// TS-096 «Смена пароля без refresh-cookie отзывает все» (idempotency, FR-016, P1).
///
/// given: пользователь авторизован (в запросе только access-cookie); в хранилище
///        есть его refresh-токены (два DI-сид-токена «устройств»).
/// when:  PUT /me/password с верным currentPassword (и валидной парой нового).
/// then:  204; все refresh-токены пользователя отозваны — каждый последующий
///        /auth/refresh — 401 (FR-016 AC «Смена без refresh-cookie отзывает все»;
///        IF-013: cookie нет — отзываются все).
/// </summary>
[Collection(B18ProfileSerialCollection.Name)]
public sealed class Ts096_PasswordChangeWithoutCookieRevokesAllTests : IClassFixture<B18ProfileWebAppFactory>
{
    private const string Login = "ts096";
    private const string Email = "ts096@x.ru";

    private readonly B18ProfileWebAppFactory _factory;

    public Ts096_PasswordChangeWithoutCookieRevokesAllTests(B18ProfileWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PutPassword_WithoutRefreshCookie_RevokesEveryRefreshToken()
    {
        // given: пользователь; в хранилище два его refresh-токена «устройств».
        var user = B18ProfileHarness.SeedUser(
            _factory,
            login: Login,
            email: Email,
            fullName: "Студент ТС-096",
            role: UserRoles.Student,
            groupId: null,
            password: B18ProfileHarness.TestUserPassword);
        var firstDeviceRefresh = B18ProfileHarness.SeedRefreshToken(_factory, user.Id);
        var secondDeviceRefresh = B18ProfileHarness.SeedRefreshToken(_factory, user.Id);

        // given: в запросе ТОЛЬКО access-cookie (refresh-cookie нет).
        using var client = B18ProfileHarness.CreateSessionClient(_factory, user.Id, UserRoles.Student);

        // when: успешная смена пароля.
        using var response = await client.PutAsJsonAsync(B18ProfileHarness.PasswordEndpoint, new
        {
            currentPassword = B18ProfileHarness.TestUserPassword,
            password = B18ProfileHarness.NewPassword,
            confirmPassword = B18ProfileHarness.NewPassword,
        });

        // then: 204.
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Ожидался статус 204, фактически {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // then: каждый последующий /auth/refresh — 401 (отозваны ВСЕ токены).
        using var firstRefreshClient = B18ProfileHarness.Create(_factory);
        using var firstRefresh = await B18ProfileHarness.RefreshAsync(firstRefreshClient, firstDeviceRefresh);
        Assert.True(
            firstRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh первого токена (отозван), фактически {(int)firstRefresh.StatusCode}: {await firstRefresh.Content.ReadAsStringAsync()}");

        using var secondRefreshClient = B18ProfileHarness.Create(_factory);
        using var secondRefresh = await B18ProfileHarness.RefreshAsync(secondRefreshClient, secondDeviceRefresh);
        Assert.True(
            secondRefresh.StatusCode == HttpStatusCode.Unauthorized,
            $"Ожидался статус 401 refresh второго токена (отозван), фактически {(int)secondRefresh.StatusCode}: {await secondRefresh.Content.ReadAsStringAsync()}");
    }
}
